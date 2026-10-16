// SPDX-License-Identifier: MIT
// No visible Qt UI. DaisyOS supplies consent and the editable shortcut controls.
#include <QGuiApplication>
#include <QAction>
#include <QKeySequence>
#include <QJsonArray>
#include <QJsonDocument>
#include <QJsonObject>
#include <QSocketNotifier>
#include <QElapsedTimer>
#include <KGlobalAccel>
#include <unistd.h>
#include <cstdio>

int main(int argc, char **argv)
{
    QGuiApplication app(argc, argv);
    QGuiApplication::setApplicationName("daisyos-portal-shortcuts");
    QGuiApplication::setDesktopFileName("org.daisyos.DesktopServices");
    QList<QAction *> actions;
    QByteArray input;
    bool configured = false;
    QElapsedTimer time; time.start();
    auto emitEvent = [&time](const char *event, QAction *action) {
        const auto id = action->data().toString().toUtf8().toBase64();
        printf("%s\t%s\t%llu\n", event, id.constData(), static_cast<unsigned long long>(time.elapsed())); fflush(stdout);
    };
    QObject::connect(KGlobalAccel::self(), &KGlobalAccel::globalShortcutActiveChanged, &app,
                     [&emitEvent](QAction *action, bool active) { emitEvent(active ? "pressed" : "released", action); });
    QSocketNotifier notifier(STDIN_FILENO, QSocketNotifier::Read);
    QObject::connect(&notifier, &QSocketNotifier::activated, &app, [&] {
        char buffer[4096]; const auto count = read(STDIN_FILENO, buffer, sizeof buffer);
        if (count <= 0) { app.quit(); return; }
        input.append(buffer, count);
        if (input.size() > 1024 * 1024) { app.exit(2); return; }
        const auto newline = input.indexOf('\n');
        if (newline < 0 || configured) return;
        configured = true;
        QJsonParseError error;
        const auto document = QJsonDocument::fromJson(input.left(newline), &error);
        input.clear();
        if (error.error != QJsonParseError::NoError || !document.isArray()) { app.exit(2); return; }
        const auto bindings = document.array();
        for (const auto &binding : bindings) {
            const auto object = binding.toObject();
            const auto key = QKeySequence::fromString(object["key"].toString(), QKeySequence::PortableText);
            if ((!object["key"].toString().isEmpty() && key.isEmpty()) || (!key.isEmpty() && !KGlobalAccel::isGlobalShortcutAvailable(key))) { puts("conflict"); fflush(stdout); app.exit(3); return; }
        }
        for (const auto &binding : bindings) {
            const auto object = binding.toObject();
            auto *action = new QAction(object["description"].toString(), &app);
            action->setObjectName(QString::number(getpid()) + QLatin1Char('-') + object["id"].toString().toUtf8().toHex()); action->setData(object["id"].toString());
            const auto key = QKeySequence::fromString(object["key"].toString(), QKeySequence::PortableText);
            if (!KGlobalAccel::self()->setShortcut(action, key.isEmpty() ? QList<QKeySequence>{} : QList<QKeySequence>{key}, KGlobalAccel::NoAutoloading)) {
                delete action; puts("failed"); fflush(stdout); app.exit(3); return;
            }
            actions.append(action);
        }
        puts("ready"); fflush(stdout);
    });
    const auto status = app.exec();
    for (auto *action : actions) { KGlobalAccel::self()->removeAllShortcuts(action); delete action; }
    return status;
}
