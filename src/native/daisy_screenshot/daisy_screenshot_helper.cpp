#include <QCommandLineOption>
#include <QCommandLineParser>
#include <QGuiApplication>
#include <QDBusConnection>
#include <QDBusInterface>
#include <QDBusMessage>
#include <QDBusPendingCall>
#include <QDBusPendingCallWatcher>
#include <QDBusReply>
#include <QDBusUnixFileDescriptor>
#include <QDir>
#include <QEventLoop>
#include <QFileInfo>
#include <QImage>
#include <QImageWriter>
#include <QDBusArgument>
#include <QJsonDocument>
#include <QJsonObject>
#include <QTimer>
#include <QVariantMap>

#include <array>
#include <atomic>
#include <chrono>
#include <cerrno>
#include <cstring>
#include <thread>
#include <poll.h>
#include <unistd.h>

namespace
{
constexpr auto kService = "org.kde.KWin.ScreenShot2";
constexpr auto kPath = "/org/kde/KWin/ScreenShot2";
constexpr auto kInterface = "org.kde.KWin.ScreenShot2";

void writeResult(const QJsonObject &result)
{
    const QByteArray payload = QJsonDocument(result).toJson(QJsonDocument::Compact);
    fwrite(payload.constData(), 1, static_cast<size_t>(payload.size()), stdout);
    fputc('\n', stdout);
    fflush(stdout);
}

int fail(const QString &message)
{
    fprintf(stderr, "daisy-screenshot-helper: %s\n", qPrintable(message));
    writeResult({{"ok", false}, {"error", message}});
    return 1;
}

bool parsePositiveInt(const QString &text, int *value)
{
    bool ok = false;
    const int parsed = text.toInt(&ok);
    if (!ok || parsed <= 0) {
        return false;
    }
    *value = parsed;
    return true;
}
}

int main(int argc, char *argv[])
{
    QGuiApplication application(argc, argv);
    application.setApplicationName(QStringLiteral("daisy-screenshot-helper"));
    application.setDesktopFileName(QStringLiteral("org.daisyos.screenshot-helper"));
    application.setOrganizationDomain(QStringLiteral("daisyos.org"));
    application.setOrganizationName(QStringLiteral("DaisyOS"));

    QCommandLineParser parser;
    parser.setApplicationDescription(QStringLiteral("Trusted KWin screenshot helper for DaisyOS"));
    parser.addHelpOption();
    const QCommandLineOption modeOption({"m", "mode"}, "Capture mode: screen, window, or area.", "mode");
    const QCommandLineOption outputOption({"o", "output"}, "PNG output path.", "path");
    const QCommandLineOption xOption("x", "Area x coordinate.", "coordinate");
    const QCommandLineOption yOption("y", "Area y coordinate.", "coordinate");
    const QCommandLineOption widthOption("width", "Area width.", "pixels");
    const QCommandLineOption heightOption("height", "Area height.", "pixels");
    const QCommandLineOption formatOption("format", "Transfer format: png or bmp.", "format", "png");
    const QCommandLineOption cursorOption("include-cursor", "Include the pointer in the capture.");
    parser.addOptions({modeOption, outputOption, xOption, yOption, widthOption, heightOption, cursorOption, formatOption});
    parser.process(application);

    const QString mode = parser.value(modeOption).trimmed().toLower();
    const QString formatName = parser.value(formatOption).trimmed().toLower();
    if (formatName != "png" && formatName != "bmp") return fail(QStringLiteral("Choose output format png or bmp."));
    const QString outputPath = parser.value(outputOption).trimmed();
    if (mode != QStringLiteral("screen") && mode != QStringLiteral("window") && mode != QStringLiteral("area")) {
        return fail(QStringLiteral("Choose capture mode screen, window, or area."));
    }
    if (outputPath.isEmpty()) {
        return fail(QStringLiteral("An output PNG path is required."));
    }

    int x = 0;
    int y = 0;
    int width = 0;
    int height = 0;
    if (mode == QStringLiteral("area")) {
        bool xOk = false;
        bool yOk = false;
        x = parser.value(xOption).toInt(&xOk);
        y = parser.value(yOption).toInt(&yOk);
        if (!xOk || !yOk || !parsePositiveInt(parser.value(widthOption), &width) || !parsePositiveInt(parser.value(heightOption), &height)) {
            return fail(QStringLiteral("An area capture needs numeric x, y, width, and height values."));
        }
    }

    const QFileInfo outputInfo(outputPath);
    QDir outputDirectory = outputInfo.dir();
    if (!outputDirectory.exists() && !outputDirectory.mkpath(QStringLiteral("."))) {
        return fail(QStringLiteral("The screenshot folder could not be created."));
    }

    std::array<int, 2> pipeFds{};
    if (pipe(pipeFds.data()) != 0) {
        return fail(QStringLiteral("Could not create the capture pipe: %1").arg(QString::fromLocal8Bit(strerror(errno))));
    }

    QByteArray pixels;
    QString readError;
    std::atomic<bool> cancelled{false};
    std::atomic<qint64> expectedBytes{0};
    std::thread reader([&]() {
        std::array<char, 64 * 1024> buffer{};
        const auto deadline = std::chrono::steady_clock::now() + std::chrono::seconds(12);
        while (!cancelled && std::chrono::steady_clock::now() < deadline) {
            if (expectedBytes > 0 && pixels.size() >= expectedBytes) break;
            pollfd fd{pipeFds[0], POLLIN, 0};
            const int ready = poll(&fd, 1, 25);
            if (ready == 0 || (ready < 0 && errno == EINTR)) continue;
            if (ready < 0) { readError = QString::fromLocal8Bit(strerror(errno)); break; }
            const ssize_t readCount = read(pipeFds[0], buffer.data(), buffer.size());
            if (readCount > 0) {
                pixels.append(buffer.data(), static_cast<int>(readCount));
                if (pixels.size() > 512 * 1024 * 1024) { readError = QStringLiteral("Capture exceeds 512 MiB."); break; }
            } else if (readCount == 0) {
                break;
            } else if (errno != EINTR) {
                readError = QString::fromLocal8Bit(strerror(errno));
                break;
            }
        }
        close(pipeFds[0]);
    });

    QDBusInterface screenshotApi(QString::fromLatin1(kService), QString::fromLatin1(kPath), QString::fromLatin1(kInterface), QDBusConnection::sessionBus());
    if (!screenshotApi.isValid()) {
        cancelled = true;
        close(pipeFds[1]);
        reader.join();
        return fail(QStringLiteral("KWin's secure screenshot interface is unavailable: %1").arg(screenshotApi.lastError().message()));
    }

    QVariantMap options;
    options.insert(QStringLiteral("include-cursor"), parser.isSet(cursorOption));
    options.insert(QStringLiteral("native-resolution"), true);

    QDBusUnixFileDescriptor descriptor(pipeFds[1]);
    QVariantList arguments;
    QString method;
    if (mode == QStringLiteral("screen")) {
        method = QStringLiteral("CaptureActiveScreen");
        arguments = {options, QVariant::fromValue(descriptor)};
    } else if (mode == QStringLiteral("window")) {
        method = QStringLiteral("CaptureActiveWindow");
        options.insert(QStringLiteral("include-decoration"), true);
        options.insert(QStringLiteral("include-shadow"), true);
        arguments = {options, QVariant::fromValue(descriptor)};
    } else {
        method = QStringLiteral("CaptureArea");
        arguments = {x, y, static_cast<uint>(width), static_cast<uint>(height), options, QVariant::fromValue(descriptor)};
    }

    // Release every local writer copy, not only the original descriptor.
    QDBusPendingCall pendingCall = screenshotApi.asyncCallWithArgumentList(method, arguments);
    close(pipeFds[1]);
    arguments.clear();
    descriptor = QDBusUnixFileDescriptor();

    // Wait for the reply with an explicit 12-second timeout via an event loop.
    // This prevents hanging forever if KWin shows an auth dialog or is unresponsive.
    QDBusPendingCallWatcher watcher(pendingCall);
    {
        QEventLoop loop;
        QTimer timeout;
        timeout.setSingleShot(true);
        QObject::connect(&timeout, &QTimer::timeout, &loop, &QEventLoop::quit);
        QObject::connect(&watcher, &QDBusPendingCallWatcher::finished, &loop, &QEventLoop::quit);
        timeout.start(12000);
        if (!watcher.isFinished()) loop.exec();
    }

    const QDBusMessage reply = watcher.isFinished() ? watcher.reply() : QDBusMessage();
    QVariantMap attributes;
    if (watcher.isFinished() && reply.type() != QDBusMessage::ErrorMessage && !reply.arguments().isEmpty()) {
        attributes = qdbus_cast<QVariantMap>(reply.arguments().constFirst());
        expectedBytes = qint64(attributes.value(QStringLiteral("stride")).toInt())
            * attributes.value(QStringLiteral("height")).toInt();
    } else {
        cancelled = true;
    }
    reader.join();

    if (!watcher.isFinished()) {
        return fail(QStringLiteral("KWin screenshot timed out (no reply within 12 seconds). "
                                   "Is the screenshotEnabled=true in kwinrc [Plugins]?"));
    }

    if (reply.type() == QDBusMessage::ErrorMessage) {
        return fail(QStringLiteral("KWin capture failed: %1 (%2)").arg(reply.errorName(), reply.errorMessage()));
    }
    if (!readError.isEmpty()) {
        return fail(QStringLiteral("Could not read the capture: %1").arg(readError));
    }
    if (reply.arguments().isEmpty()) {
        return fail(QStringLiteral("KWin returned no screenshot metadata."));
    }

    const int imageWidth = attributes.value(QStringLiteral("width")).toInt();
    const int imageHeight = attributes.value(QStringLiteral("height")).toInt();
    const int stride = attributes.value(QStringLiteral("stride")).toInt();
    const auto format = static_cast<QImage::Format>(attributes.value(QStringLiteral("format")).toInt());
    if (attributes.value(QStringLiteral("type")).toString() != QStringLiteral("raw")
        || imageWidth <= 0 || imageHeight <= 0 || stride <= 0
        || pixels.size() < qint64(stride) * imageHeight) {
        const QString details = QStringLiteral("type=%1 w=%2 h=%3 stride=%4 bytes=%5")
            .arg(attributes.value(QStringLiteral("type")).toString())
            .arg(imageWidth).arg(imageHeight).arg(stride).arg(pixels.size());
        return fail(QStringLiteral("KWin returned an invalid raw screenshot. Details: %1").arg(details));
    }

    QImage raw(reinterpret_cast<const uchar *>(pixels.constData()), imageWidth, imageHeight, stride, format);
    if (raw.isNull()) {
        return fail(QStringLiteral("KWin returned an unsupported image format (%1).").arg(static_cast<int>(format)));
    }
    QImage image = raw;
    const double scale = attributes.value(QStringLiteral("scale")).toDouble();
    if (scale > 0) {
        image.setDevicePixelRatio(scale);
    }
    QImageWriter writer(outputPath, formatName.toLatin1());
    writer.setCompression(1);
    if (!writer.write(image)) {
        return fail(QStringLiteral("The screenshot could not be saved as PNG."));
    }

    writeResult({{"ok", true}, {"path", outputPath}, {"width", image.width()}, {"height", image.height()}});
    return 0;
}
