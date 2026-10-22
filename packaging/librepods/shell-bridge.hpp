// DaisyOS integration for LibrePods (GPL-3.0, like upstream).
// Included after AirPodsTrayApp. Protocol v1 is private to the logged-in user.
#include <QJsonDocument>
#include <QJsonObject>
#include <QJsonArray>
#include <QTimer>
#include <QStandardPaths>

static QJsonArray shellSupportedModes(AirPodsTrayApp *pods)
{
    using M = AirpodsTrayApp::Enums::AirPodsModel;
    switch (pods->deviceInfo()->model()) {
    case M::AirPodsPro2Lightning: case M::AirPodsPro2USBC: case M::AirPods4ANC:
        return {0, 1, 2, 3};
    case M::AirPodsPro: case M::AirPodsMaxLightning: case M::AirPodsMaxUSBC:
        return {0, 1, 2};
    default: return {}; // Do not guess capabilities from a user-editable name.
    }
}

static void startShellBridge(AirPodsTrayApp *pods, QObject *owner)
{
    const auto runtime = QStandardPaths::writableLocation(QStandardPaths::RuntimeLocation);
    if (runtime.isEmpty()) return;
    const auto path = runtime + "/daisyos-librepods.sock";
    auto server = new QLocalServer(owner);
    server->setSocketOptions(QLocalServer::UserAccessOption);
    QLocalServer::removeServer(path);
    if (!server->listen(path)) return;
    QObject::connect(server, &QLocalServer::newConnection, server, [server, pods]() {
        while (auto socket = server->nextPendingConnection()) {
            QObject::connect(socket, &QLocalSocket::disconnected, socket, &QObject::deleteLater);
            QTimer::singleShot(3000, socket, [socket]() { socket->abort(); socket->deleteLater(); });
            QObject::connect(socket, &QLocalSocket::readyRead, socket, [socket, pods]() {
                if (socket->bytesAvailable() > 1024) { socket->disconnectFromServer(); return; }
                if (!socket->canReadLine()) return;
                const auto command = QString::fromUtf8(socket->readLine()).trimmed();
                QByteArray response = "error";
                auto info = pods->deviceInfo();
                if (command == "status") {
                    QJsonObject status{
                        {"version", 1}, {"connected", pods->areAirpodsConnected()},
                        {"address", info->bluetoothAddress()}, {"name", info->deviceName()},
                        {"mode", pods->shellNoiseModeKnown ? info->noiseControlModeInt() : -1},
                        {"modes", shellSupportedModes(pods)}, {"battery", info->batteryStatus()},
                        {"nearbyAddress", pods->shellNearbyAddress()}
                    };
                    response = QJsonDocument(status).toJson(QJsonDocument::Compact);
                } else if (command.startsWith("noise:") && command.size() == 25) {
                    const auto address = command.mid(6, 17);
                    bool valid = false;
                    const int mode = command.mid(24).toInt(&valid);
                    if (valid && command[23] == ':' && pods->areAirpodsConnected()
                        && address.compare(info->bluetoothAddress(), Qt::CaseInsensitive) == 0
                        && shellSupportedModes(pods).contains(mode)) {
                        pods->setNoiseControlModeInt(mode);
                        response = "ok";
                    }
                }
                socket->write(response + '\n');
                socket->disconnectFromServer();
            });
        }
    });
}
