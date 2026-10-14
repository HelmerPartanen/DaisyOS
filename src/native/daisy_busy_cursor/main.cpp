// SPDX-License-Identifier: GPL-2.0-or-later
#include <effect/effect.h>
#include <effect/effecthandler.h>
#include <cursor.h>
#include <cursorsource.h>
#include <input.h>
#include <pointer_input.h>
#include <wayland_server.h>
#include <wayland/seat.h>
#include <workspace.h>
#include <QDBusConnection>
#include <QDBusConnectionInterface>
#include <QDBusContext>
#include <QDBusServiceWatcher>
#include <QElapsedTimer>
#include <QPointer>
#include <QTimer>
#include <QJsonDocument>
#include <QJsonObject>
#include <QCryptographicHash>
#include <unistd.h>

namespace KWin {
// This changes the native cursor source only. It never intercepts input or paints
// a second pointer. KWin supplies theme animation, hotspots, scaling and output migration.
class DaisyNativeBusyCursor final : public Effect, protected QDBusContext {
    Q_OBJECT
    Q_CLASSINFO("D-Bus Interface", "org.daisyos.BusyCursor1")
    struct Pending { qint64 started; qint64 expires; bool blocking; };
    QHash<QString, Pending> m_pending;
    QElapsedTimer m_clock;
    QTimer m_poll;
    ShapeCursorSource m_busy;
    QPointer<CursorSource> m_underlying;
    QDBusServiceWatcher m_owners;
    bool m_changing = false;
    bool m_overridden = false;
    bool authorized() {
        if (!calledFromDBus()) return false;
        const auto uid = connection().interface()->serviceUid(message().service());
        return uid.isValid() && uid.value() == getuid();
    }
    QString key(const QString &id) const { return message().service() + '/' + id; }
    void restore() {
        if (!m_overridden) return;
        m_changing = true;
        auto mouse = Cursors::self()->mouse();
        if (mouse->source() == &m_busy) mouse->setSource(m_underlying);
        m_overridden = false;
        m_underlying = nullptr;
        m_busy.setShape(QByteArray()); // Stop theme animation timers when idle.
        // Reevaluate native lock, drag, decoration and client cursor priorities.
        input()->pointer()->removeEffectsOverrideCursor();
        m_changing = false;
    }
    void update() {
        const auto now = m_clock.elapsed();
        for (auto it = m_pending.begin(); it != m_pending.end();) {
            if (it->expires <= now) it = m_pending.erase(it); else ++it;
        }
        bool ready = false, blocking = false;
        for (const auto &item : std::as_const(m_pending)) {
            if (now - item.started >= 150) { ready = true; blocking |= item.blocking; }
        }
        auto mouse = Cursors::self()->mouse();
        const auto source = mouse->source();
        const bool suspended = effects->isScreenLocked() || effects->isMouseInterception()
            || effects->hasActiveFullScreenEffect() || input()->isSelectingWindow()
            || waylandServer()->seat()->isDrag() || workspace()->moveResizeWindow()
            || Cursors::self()->isCursorHidden() || (source && source != &m_busy && source->isBlank());
        if (!ready || suspended) restore();
        else {
            m_changing = true;
            m_busy.setTheme(input()->pointer()->cursorTheme());
            m_busy.setShape(blocking ? Qt::WaitCursor : Qt::BusyCursor);
            if (mouse->source() != &m_busy) {
                m_underlying = mouse->source();
                mouse->setSource(&m_busy);
            }
            m_overridden = true;
            m_changing = false;
        }
        if (m_pending.isEmpty()) m_poll.stop(); else if (!m_poll.isActive()) m_poll.start();
    }
public:
    DaisyNativeBusyCursor() : m_owners(QString(), QDBusConnection::sessionBus(), QDBusServiceWatcher::WatchForUnregistration) {
        m_clock.start();
        m_poll.setInterval(30);
        m_busy.setTheme(input()->pointer()->cursorTheme());
        connect(Cursors::self()->mouse(), &Cursor::themeChanged, this, [this] {
            m_busy.setTheme(input()->pointer()->cursorTheme());
        });
        connect(Cursors::self()->mouse(), &Cursor::cursorChanged, this, [this] {
            if (!m_changing && Cursors::self()->mouse()->source() != &m_busy) update();
        });
        connect(&m_poll, &QTimer::timeout, this, &DaisyNativeBusyCursor::update);
        connect(effects, &EffectsHandler::startupAdded, this, [this](const QString &id, const QIcon &) {
            const auto now = m_clock.elapsed();
            m_pending.insert("startup/" + id, {now, now + 15000, false}); update();
        });
        connect(effects, &EffectsHandler::startupRemoved, this, [this](const QString &id) {
            m_pending.remove("startup/" + id); update();
        });
        connect(effects, &EffectsHandler::screenLockingChanged, this, [this](bool) { update(); });
        connect(&m_owners, &QDBusServiceWatcher::serviceUnregistered, this, [this](const QString &owner) {
            for (auto it = m_pending.begin(); it != m_pending.end();) {
                if (it.key().startsWith(owner + '/')) it = m_pending.erase(it); else ++it;
            }
            m_owners.removeWatchedService(owner); update();
        });
        QDBusConnection::sessionBus().registerObject("/org/daisyos/BusyCursor1", this, QDBusConnection::ExportAllSlots);
    }
    ~DaisyNativeBusyCursor() override {
        QDBusConnection::sessionBus().unregisterObject("/org/daisyos/BusyCursor1"); restore();
    }
    bool isActive() const override { return false; } // No scene painting or direct-scanout inhibition.
public Q_SLOTS:
    bool Begin(const QString &id, bool blocking) {
        if (!authorized() || id.size() != 32 || m_pending.size() >= 256) return false;
        for (const auto ch : id) if (!ch.isDigit() && (ch < 'a' || ch > 'f')) return false;
        const auto request = key(id);
        const auto now = m_clock.elapsed();
        auto it = m_pending.find(request);
        if (it == m_pending.end()) m_pending.insert(request, {now, now + 15000, blocking});
        else it->expires = now + 15000; // Renewal does not restart the presentation delay.
        m_owners.addWatchedService(message().service()); update(); return true;
    }
    void End(const QString &id) {
        if (!authorized()) return;
        m_pending.remove(key(id)); update();
        const auto prefix = message().service() + '/';
        bool hasRequests = false;
        for (auto it = m_pending.cbegin(); it != m_pending.cend(); ++it) hasRequests |= it.key().startsWith(prefix);
        if (!hasRequests) m_owners.removeWatchedService(message().service());
    }
    QString State() const {
        const auto image = m_busy.image();
        const auto hash = image.isNull() ? QByteArray() : QCryptographicHash::hash(
            QByteArrayView(reinterpret_cast<const char *>(image.constBits()), image.sizeInBytes()), QCryptographicHash::Sha256).toHex();
        return QString::fromUtf8(QJsonDocument(QJsonObject{{"pending", m_pending.size()}, {"visible", m_overridden},
            {"shape", QString::fromUtf8(m_busy.shape())}, {"frame", QString::fromUtf8(hash)},
            {"width", image.width()}, {"hotspotX", m_busy.hotspot().x()}, {"hotspotY", m_busy.hotspot().y()}}).toJson(QJsonDocument::Compact));
    }
};
KWIN_EFFECT_FACTORY_SUPPORTED(DaisyNativeBusyCursor, "metadata.json", return waylandServer() && input() && input()->pointer();)
}
#include "main.moc"
