#include <KConfigGroup>
#include <KConfigWatcher>
#include <KSharedConfig>
#include <KDesktopFile>
#include <KDirWatch>
#include <KIO/ApplicationLauncherJob>
#include <KIO/DesktopExecParser>
#include <KJob>
#include <KService>
#include <KServiceAction>
#include <KSycoca>
#include <QDBusConnection>
#include <QDBusConnectionInterface>
#include <QDBusContext>
#include <QDBusPendingCallWatcher>
#include <QDBusPendingReply>
#include <QDBusVariant>
#include <QDir>
#include <QFile>
#include <QFileInfo>
#include <QFutureWatcher>
#include <QGuiApplication>
#include <QJsonArray>
#include <QJsonDocument>
#include <QJsonObject>
#include <QProcess>
#include <QStandardPaths>
#include <QTimer>
#include <QUuid>
#include <QtConcurrent>
#include <algorithm>
#include <array>
#include <chrono>
#include <iostream>
#include <memory>
#include <sys/socket.h>
#include <sys/un.h>
#include <unistd.h>

static QStringList procArgs(const QString &pid) {
  QFile f("/proc/" + pid + "/cmdline");
  if (!f.open(QIODevice::ReadOnly))
    return {};
  QStringList out;
  for (const auto &part : f.readAll().split('\0'))
    if (!part.isEmpty())
      out << QString::fromLocal8Bit(part);
  return out;
}

// Finds the XWayland display owned by the compositor serving WAYLAND_DISPLAY.
// The user manager environment lacks DISPLAY, and KWin may restart XWayland
// on another number, so this is resolved per launch.
static void exportXWaylandDisplay() {
  const auto wayland = qEnvironmentVariable("WAYLAND_DISPLAY");
  if (wayland.isEmpty())
    return;
  const auto pids =
      QDir("/proc").entryList(QDir::Dirs | QDir::NoDotAndDotDot);
  QString compositor;
  for (const auto &pid : pids) {
    bool ok = false;
    pid.toInt(&ok);
    if (!ok)
      continue;
    const auto args = procArgs(pid);
    if (args.isEmpty() || QFileInfo(args[0]).fileName() != "kwin_wayland")
      continue;
    const auto i = args.indexOf("--socket");
    if (i >= 0 && i + 1 < args.size() && args[i + 1] == wayland) {
      compositor = pid;
      break;
    }
  }
  if (compositor.isEmpty())
    return;
  QString display, authority;
  for (const auto &pid : pids) {
    QFile status("/proc/" + pid + "/status");
    if (!status.open(QIODevice::ReadOnly))
      continue;
    bool child = false;
    for (const auto &line : status.readAll().split('\n'))
      if (line.startsWith("PPid:")) {
        child = line.mid(5).trimmed() == compositor.toLatin1();
        break;
      }
    if (!child)
      continue;
    const auto args = procArgs(pid);
    if (args.size() < 2 || QFileInfo(args[0]).fileName() != "Xwayland")
      continue;
    display = args[1];
    const auto a = args.indexOf("-auth");
    if (a >= 0 && a + 1 < args.size())
      authority = args[a + 1];
    break;
  }
  if (display.isEmpty()) {
    // Lazy XWayland: KWin owns the listener; SO_PEERCRED identifies it.
    const auto sockets =
        QDir("/tmp/.X11-unix").entryList({"X*"}, QDir::System);
    for (const auto &name : sockets) {
      bool ok = false;
      const int number = name.mid(1).toInt(&ok);
      if (!ok)
        continue;
      int fd = ::socket(AF_UNIX, SOCK_STREAM | SOCK_CLOEXEC, 0);
      if (fd < 0)
        continue;
      sockaddr_un addr{};
      addr.sun_family = AF_UNIX;
      const auto path = ("/tmp/.X11-unix/" + name).toLocal8Bit();
      qstrncpy(addr.sun_path, path.constData(), sizeof(addr.sun_path));
      ucred cred{};
      socklen_t len = sizeof(cred);
      const bool match =
          ::connect(fd, reinterpret_cast<sockaddr *>(&addr), sizeof(addr)) ==
              0 &&
          ::getsockopt(fd, SOL_SOCKET, SO_PEERCRED, &cred, &len) == 0 &&
          QString::number(cred.pid) == compositor;
      ::close(fd);
      if (match) {
        display = QStringLiteral(":%1").arg(number);
        break;
      }
    }
  }
  if (display.isEmpty())
    return;
  qputenv("DISPLAY", display.toLocal8Bit());
  // An inherited Xauthority may belong to a different session.
  if (authority.isEmpty())
    qunsetenv("XAUTHORITY");
  else
    qputenv("XAUTHORITY", authority.toLocal8Bit());
}

// Native Wayland clients receive fractional scale through the compositor.
// XWayland clients instead consult the X resource database; without Plasma's
// full session daemon that database remains at 96 DPI and applications such as
// Spotify render tiny. Keep only that legacy bridge synchronized, avoiding a
// forced toolkit factor that would double-scale native Wayland applications.
static void syncApplicationScale() {
  const auto config = KSharedConfig::openConfig("kwinrc");
  config->reparseConfiguration();
  const KConfigGroup xwayland(config, "Xwayland");
  const qreal scale = std::clamp(xwayland.readEntry("Scale", 1.0), 1.0, 4.0);
  const auto display = qEnvironmentVariable("DISPLAY");
  static qreal appliedScale = -1;
  static QString appliedDisplay;
  if (display.isEmpty() ||
      (qFuzzyCompare(scale, appliedScale) && display == appliedDisplay))
    return;

  QProcess xrdb;
  xrdb.setProcessChannelMode(QProcess::ForwardedErrorChannel);
  xrdb.start("xrdb", {"-merge"});
  if (xrdb.waitForStarted(1000)) {
    const auto dpi = QByteArray::number(qRound(96.0 * scale));
    xrdb.write("Xft.dpi: " + dpi + "\n");
    xrdb.closeWriteChannel();
    if (xrdb.waitForFinished(1000) && xrdb.exitCode() == 0) {
      appliedScale = scale;
      appliedDisplay = display;
    }
  }
}

static qint64 now() {
  return std::chrono::duration_cast<std::chrono::nanoseconds>(
             std::chrono::steady_clock::now().time_since_epoch())
      .count();
}
struct AppMetadata {
  KService::Ptr service;
  QHash<QString, KServiceAction> actions;
  bool dbusActivatable = false;
};
struct Snapshot {
  QHash<QString, AppMetadata> apps;
  QString catalog;
};
static std::shared_ptr<const Snapshot> discover() {
  auto s = std::make_shared<Snapshot>();
  QJsonArray entries;
  for (const auto &app : KService::allServices()) {
    if (!app->isApplication())
      continue;
    auto id = app->storageId();
    KDesktopFile file(app->entryPath());
    const auto tryExec = file.desktopGroup().readEntry("TryExec", QString{});
    if (!tryExec.isEmpty() && QStandardPaths::findExecutable(tryExec).isEmpty())
      continue;
    AppMetadata metadata;
    metadata.service = app;
    metadata.dbusActivatable = app->property<bool>("DBusActivatable");
    for (const auto &action : app->actions())
      metadata.actions.insert(action.name(), action);
    s->apps.insert(id, metadata);
    if (app->noDisplay() || !app->showInCurrentDesktop() ||
        !app->showOnCurrentPlatform())
      continue;
    QJsonObject labels;
    // KConfig entryMap resolves one locale rather than retaining localized
    // keys. Snapshot the languages supported by DaisyOS before publishing the
    // registry.
    for (const QString &locale :
         QStringList{"", "en", "en_US", "fi", "fi_FI"}) {
      file.setLocale(locale);
      for (const QString &key :
           QStringList{"Name", "Comment", "GenericName", "Keywords"}) {
        const auto value = file.desktopGroup().readEntry(key, QString{});
        labels.insert(locale.isEmpty() ? key : key + "[" + locale + "]", value);
      }
    }
    KIO::DesktopExecParser exec(*app, {});
    const auto argv = exec.resultingArguments();
    entries.append(QJsonObject{
        {"id", id},
        {"name", app->name()},
        {"description", app->comment()},
        {"genericName", app->genericName()},
        {"keywords", QJsonArray::fromStringList(app->keywords())},
        {"labels", labels},
        {"icon", app->icon()},
        {"path", app->entryPath()},
        {"argv", QJsonArray::fromStringList(argv)},
        {"categories", QJsonArray::fromStringList(app->categories())}});
  }
  s->catalog =
      QString::fromUtf8(QJsonDocument(entries).toJson(QJsonDocument::Compact));
  return s;
}
enum class TraceEvent {
  DaemonReceived,
  LookupStarted,
  LookupFinished,
  DispatchStarted,
  DispatchAccepted,
  SurfaceAssociated,
  Count
};
struct Request {
  QString owner, id, app, action, token, state, error;
  QStringList uris;
  QList<QDBusMessage> waiting;
  std::array<qint64, static_cast<size_t>(TraceEvent::Count)> trace;
  Request() { trace.fill(-1); }
  bool done = false;
  qint64 created = now();
};
class Launcher : public QObject, protected QDBusContext {
  Q_OBJECT
  Q_CLASSINFO("D-Bus Interface", "org.daisyos.Launcher1")
  Q_PROPERTY(uint ApiVersion READ apiVersion CONSTANT)
  std::shared_ptr<const Snapshot> snapshot = discover();
  QHash<QString, std::shared_ptr<Request>> requests;
  QSet<QString> authorizedCallers;
  const bool telemetryEnabled =
      qEnvironmentVariable("DAISYOS_LAUNCH_TELEMETRY") != "0";
  QTimer debounce;
  bool rebuilding = false;
  bool authorized() {
    if (!calledFromDBus())
      return true;
    const auto caller = message().service();
    if (authorizedCallers.contains(caller))
      return true;
    auto uid = connection().interface()->serviceUid(caller);
    if (!uid.isValid() || uid.value() != getuid()) {
      sendErrorReply(QDBusError::AccessDenied,
                     "Caller is not the session user");
      return false;
    }
    if (authorizedCallers.size() >= 256)
      authorizedCallers.clear();
    authorizedCallers.insert(caller);
    return true;
  }
  void stamp(const std::shared_ptr<Request> &r, TraceEvent event) {
    if (telemetryEnabled)
      r->trace[static_cast<size_t>(event)] = now();
  }
  void state(const std::shared_ptr<Request> &r, const QString &value) {
    r->state = value;
    emit LaunchStateChanged(r->id, value);
  }
  void finish(const std::shared_ptr<Request> &r, const QString &error = {}) {
    if (r->done)
      return;
    r->done = true;
    r->error = error;
    r->token.clear();
    state(r, error.isEmpty() ? "Accepted" : error);
    if (error.isEmpty())
      stamp(r, TraceEvent::DispatchAccepted);
    else
      emit LaunchFailed(r->id, error, "Application launch did not complete");
    for (const auto &m : r->waiting)
      QDBusConnection("daisy-launch-api")
          .send((error.isEmpty()
                     ? m.createReply(QVariantList{true})
                     : m.createErrorReply("org.daisyos.Launcher1." + error,
                                          "Launch failed")));
    r->waiting.clear();
  }
  void dispatch(const std::shared_ptr<Request> &r) {
    if (r->done)
      return;
    state(r, "Resolving");
    auto snap = snapshot;
    auto lookupStarted = now();
    auto metadata = snap->apps.value(r->app);
    if (r->app.startsWith('/')) {
      metadata.service = KService::Ptr(new KService(r->app));
      if (!metadata.service->isValid() || !metadata.service->isApplication()) {
        finish(r, "NotFound");
        return;
      }
      KDesktopFile file(r->app);
      metadata.dbusActivatable = file.desktopGroup().readEntry("DBusActivatable", false);
      for (const auto &action : metadata.service->actions()) metadata.actions.insert(action.name(), action);
    }
    auto app = metadata.service;
    auto lookupFinished = now();
    if (telemetryEnabled) {
      r->trace[static_cast<size_t>(TraceEvent::LookupStarted)] = lookupStarted;
      r->trace[static_cast<size_t>(TraceEvent::LookupFinished)] =
          lookupFinished;
    }
    if (!app) {
      finish(r, "NotFound");
      return;
    }
    KServiceAction action;
    if (!r->action.isEmpty()) {
      action = metadata.actions.value(r->action);
      if (action.name().isEmpty()) {
        finish(r, "InvalidArgument");
        return;
      }
    }
    state(r, "Dispatching");
    stamp(r, TraceEvent::DispatchStarted);
    exportXWaylandDisplay();
    syncApplicationScale();
    if (metadata.dbusActivatable) {
      QString name = app->storageId();
      if (name.endsWith(".desktop"))
        name.chop(8);
      QString path = "/" + name;
      path.replace('.', '/');
      path.replace('-', '_');
      QVariantMap data;
      if (!r->token.isEmpty())
        data.insert("activation-token", r->token);
      auto m = QDBusMessage::createMethodCall(
          name, path, "org.freedesktop.Application",
          !r->action.isEmpty() ? "ActivateAction"
          : r->uris.isEmpty()  ? "Activate"
                               : "Open");
      if (!r->action.isEmpty())
        m.setArguments(
            {r->action, QVariant::fromValue(QList<QDBusVariant>{}), data});
      else if (!r->uris.isEmpty())
        m.setArguments({r->uris, data});
      else
        m.setArguments({data});
      auto w = new QDBusPendingCallWatcher(
          QDBusConnection::sessionBus().asyncCall(m, 15000), this);
      connect(w, &QDBusPendingCallWatcher::finished, this, [this, r, w] {
        QDBusPendingReply<> reply = *w;
        finish(r, reply.isError() ? "BackendFailed" : QString{});
        w->deleteLater();
      });
    } else {
      auto job = r->action.isEmpty() ? new KIO::ApplicationLauncherJob(app)
                                     : new KIO::ApplicationLauncherJob(action);
      QList<QUrl> urls;
      for (const auto &u : r->uris)
        urls.append(QUrl(u));
      job->setUrls(urls);
      job->setStartupId(r->token.toUtf8());
      job->setUiDelegate(nullptr);
      connect(job, &KJob::result, this, [this, r](KJob *j) {
        finish(r, j->error() ? "BackendFailed" : QString{});
      });
      job->start();
    }
  }

public:
  explicit Launcher(QObject *parent = nullptr) : QObject(parent) {
    // Watch application directories even in sessions without kded/sycoca
    // maintenance.
    auto files = new KDirWatch(this);
    QStringList directories =
        QStandardPaths::standardLocations(QStandardPaths::ApplicationsLocation);
    directories << QDir::homePath() +
                       "/.local/share/flatpak/exports/share/applications"
                << "/var/lib/flatpak/exports/share/applications";
    for (const auto &directory : directories)
      files->addDir(directory, KDirWatch::WatchFiles | KDirWatch::WatchSubDirs);
    auto rebuildTimer = new QTimer(this);
    rebuildTimer->setSingleShot(true);
    rebuildTimer->setInterval(300);
    auto builder = new QProcess(this);
    builder->setStandardOutputFile(QProcess::nullDevice());
    builder->setStandardErrorFile(QProcess::nullDevice());
    connect(files, &KDirWatch::dirty, rebuildTimer,
            qOverload<>(&QTimer::start));
    connect(files, &KDirWatch::created, rebuildTimer,
            qOverload<>(&QTimer::start));
    connect(files, &KDirWatch::deleted, rebuildTimer,
            qOverload<>(&QTimer::start));
    connect(rebuildTimer, &QTimer::timeout, this, [builder, rebuildTimer] {
      if (builder->state() != QProcess::NotRunning) {
        rebuildTimer->start();
        return;
      }
      builder->start("kbuildsycoca6", {"--noincremental"});
    });
    connect(builder, qOverload<int, QProcess::ExitStatus>(&QProcess::finished),
            this, [this](int code, QProcess::ExitStatus status) {
              if (code == 0 && status == QProcess::NormalExit)
                debounce.start();
            });
    debounce.setSingleShot(true);
    debounce.setInterval(300);
    connect(KSycoca::self(), &KSycoca::databaseChanged, &debounce,
            qOverload<>(&QTimer::start));
    connect(&debounce, &QTimer::timeout, this, [this] {
      if (rebuilding) {
        debounce.start();
        return;
      }
      rebuilding = true;
      auto w = new QFutureWatcher<std::shared_ptr<const Snapshot>>(this);
      connect(w, &QFutureWatcherBase::finished, this, [this, w] {
        try {
          snapshot = w->result();
          emit RegistryChanged();
        } catch (...) {
        }
        rebuilding = false;
        w->deleteLater();
      });
      w->setFuture(QtConcurrent::run(discover));
    });
    auto clean = new QTimer(this);
    clean->setInterval(10000);
    connect(clean, &QTimer::timeout, this, [this] {
      for (auto it = requests.begin(); it != requests.end();) {
        if (now() - it.value()->created > 120000000000LL && it.value()->done)
          it = requests.erase(it);
        else
          ++it;
      }
    });
    clean->start();
  }
  uint apiVersion() const { return 1; }
public slots:
  QString Catalog() { return authorized() ? snapshot->catalog : QString{}; }
  bool Launch(QString requestId, QString desktopId, QString actionId,
              QStringList uris, QString token, QVariantMap options) {
    const auto received = now();
    if (!authorized())
      return false;
    const auto owner =
        calledFromDBus() ? message().service() : QString("local");
    if (requestId.size() != 32 ||
        !std::all_of(requestId.begin(), requestId.end(),
                     [](QChar c) {
                       return (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f');
                     }) ||
        (desktopId.startsWith('/')
             ? desktopId.size() > 4096 || !desktopId.endsWith(".desktop")
             : desktopId.size() > 256 || desktopId.contains('/')) ||
        actionId.size() > 128 || token.size() > 4096 || uris.size() > 64 ||
        !options.isEmpty()) {
      if (calledFromDBus())
        sendErrorReply("org.daisyos.Launcher1.InvalidArgument",
                       "Invalid request");
      return false;
    }
    for (const auto &u : uris)
      if (u.size() > 8192 || !QUrl(u).isValid() || QUrl(u).scheme().isEmpty()) {
        if (calledFromDBus())
          sendErrorReply("org.daisyos.Launcher1.InvalidArgument",
                         "Invalid URI");
        return false;
      }
    if (requests.contains(requestId)) {
      auto r = requests.value(requestId);
      if (r->owner == owner && r->app == desktopId && r->action == actionId &&
          r->uris == uris)
        return true;
      if (calledFromDBus())
        sendErrorReply("org.daisyos.Launcher1.InvalidArgument",
                       "Request ID conflict");
      return false;
    }
    int pending = 0, recent = 0;
    for (const auto &r : requests)
      if (r->owner == owner) {
        if (!r->done)
          pending++;
        if (now() - r->created < 1000000000LL)
          recent++;
      }
    if (pending >= 16 || recent >= 20 || requests.size() >= 1024) {
      if (calledFromDBus())
        sendErrorReply("org.daisyos.Launcher1.RateLimited",
                       "Too many requests");
      return false;
    }
    auto r = std::make_shared<Request>();
    r->id = requestId;
    r->owner = owner;
    r->app = desktopId;
    r->action = actionId;
    r->uris = uris;
    r->token = token;
    if (telemetryEnabled)
      r->trace[static_cast<size_t>(TraceEvent::DaemonReceived)] = received;
    requests.insert(requestId, r);
    state(r, "Requested");
    QTimer::singleShot(0, this, [this, r] { dispatch(r); });
    QTimer::singleShot(15000, this, [this, r] { finish(r, "TimedOut"); });
    return true;
  }
  bool Await(QString id) {
    if (!authorized())
      return false;
    auto r = requests.value(id);
    if (!r || (calledFromDBus() && r->owner != message().service())) {
      sendErrorReply("org.daisyos.Launcher1.NotFound", "Unknown request");
      return false;
    }
    if (r->done) {
      if (!r->error.isEmpty())
        sendErrorReply("org.daisyos.Launcher1." + r->error, "Launch failed");
      return r->error.isEmpty();
    }
    if (r->waiting.size() >= 4) {
      sendErrorReply("org.daisyos.Launcher1.RateLimited", "Too many waiters");
      return false;
    }
    setDelayedReply(true);
    r->waiting.append(message());
    return false;
  }
  void Cancel(QString id) {
    if (!authorized())
      return;
    auto r = requests.value(id);
    if (r && r->owner == message().service())
      finish(r, "Cancelled");
  }
  QString Trace(QString id) {
    if (!authorized())
      return {};
    auto r = requests.value(id);
    if (!r || r->owner != message().service())
      return {};
    if (!telemetryEnabled)
      return "{}";
    QJsonObject trace{{"activation_ready", QJsonValue::Null},
                      {"first_committed", QJsonValue::Null},
                      {"first_presented", QJsonValue::Null},
                      {"ready", QJsonValue::Null}};
    const char *names[] = {
        "daemon_received",          "registry_lookup_started",
        "registry_lookup_finished", "dispatch_started",
        "dispatch_accepted",        "surface_associated"};
    for (size_t i = 0; i < r->trace.size(); i++)
      trace.insert(names[i], r->trace[i] < 0
                                 ? QJsonValue{QJsonValue::Null}
                                 : QJsonValue{QString::number(r->trace[i])});
    return QString::fromUtf8(
        QJsonDocument(trace).toJson(QJsonDocument::Compact));
  }
  void Associate(QString id, QString window) {
    if (!authorized())
      return;
    auto r = requests.value(id);
    if (r && r->owner == message().service() && r->error.isEmpty() &&
        window.size() < 256) {
      stamp(r, TraceEvent::SurfaceAssociated);
      state(r, "WindowIdentified");
    }
  }
signals:
  void LaunchStateChanged(QString requestId, QString state);
  void LaunchFailed(QString requestId, QString errorCode, QString message);
  void RegistryChanged();
};
int main(int argc, char **argv) {
  if (qEnvironmentVariable("XDG_CURRENT_DESKTOP").split(QLatin1Char(':')).contains(QStringLiteral("DaisyOS"), Qt::CaseInsensitive)) {
    // A development terminal may contain Electron's internal test variables.
    // They are never valid session-wide launch environment.
    qunsetenv("ELECTRON_RUN_AS_NODE");
    qunsetenv("ELECTRON_NO_ATTACH_CONSOLE");
    qputenv("GTK_USE_PORTAL", "1");
    qputenv("GIO_USE_PORTALS", "1");
    qputenv("QT_QPA_PLATFORMTHEME", "xdgdesktopportal");
    qputenv("ELECTRON_OZONE_PLATFORM_HINT", "auto");
    qputenv("QT_SCALE_FACTOR_ROUNDING_POLICY", "PassThrough");
    qputenv("QT_ENABLE_HIGHDPI_SCALING", "1");
    if (qEnvironmentVariableIsEmpty("GDK_BACKEND"))
      qputenv("GDK_BACKEND", "wayland,x11");
  }
  QGuiApplication app(argc, argv);
  app.setApplicationName("DaisyLaunch");
  app.setQuitOnLastWindowClosed(false);
  QCoreApplication::setQuitLockEnabled(false);
  // Persistent KIO launches must not keep a stale cursor environment after a
  // preference change. Reload only on config notifications, never per launch.
  const auto cursorConfig = KSharedConfig::openConfig("kcminputrc");
  const auto cursorWatcher = KConfigWatcher::create(cursorConfig);
  const auto updateCursorEnvironment = [cursorConfig] {
    cursorConfig->reparseConfiguration();
    const KConfigGroup mouse(cursorConfig, "Mouse");
    const auto theme = mouse.readEntry("cursorTheme", QByteArray{});
    if (!theme.isEmpty()) qputenv("XCURSOR_THEME", theme);
    const int size = mouse.readEntry("cursorSize", 0);
    if (size > 0) qputenv("XCURSOR_SIZE", QByteArray::number(size));
  };
  updateCursorEnvironment();
  QObject::connect(cursorWatcher.data(), &KConfigWatcher::configChanged, &app,
                   [updateCursorEnvironment] { updateCursorEnvironment(); });
  Launcher launcher;
  const auto args = app.arguments();
  if (args.size() == 2 && args[1] == "--catalog") {
    std::cout << launcher.Catalog().toUtf8().constData() << std::endl;
    return 0;
  }
  if (args.size() > 2 && args[1] == "--launch") {
    QStringList uris;
    QString action, token;
    for (int i = 3; i + 1 < args.size(); i += 2) {
      if (args[i] == "--uri")
        uris.append(args[i + 1]);
      else if (args[i] == "--action")
        action = args[i + 1];
      else if (args[i] == "--token")
        token = args[i + 1];
      else
        return 2;
    }
    const auto id = QUuid::createUuid().toString(QUuid::Id128);
    QObject::connect(&launcher, &Launcher::LaunchStateChanged, &app,
                     [&](QString, QString state) {
                       if (state == "Accepted")
                         app.exit(0);
                       else if (state == "BackendFailed" ||
                                state == "NotFound" || state == "TimedOut" ||
                                state == "InvalidArgument")
                         app.exit(1);
                     });
    if (!launcher.Launch(id, args[2], action, uris, token, {}))
      return 2;
    return app.exec();
  }
  auto bus = QDBusConnection::connectToBus(QDBusConnection::SessionBus,
                                           "daisy-launch-api");
  if (!bus.registerObject("/org/daisyos/Launcher1", &launcher,
                          QDBusConnection::ExportAllSlots |
                              QDBusConnection::ExportAllSignals |
                              QDBusConnection::ExportAllProperties) ||
      !bus.registerService("org.daisyos.Launcher1"))
    return 1;
  return app.exec();
}
#include "main.moc"
