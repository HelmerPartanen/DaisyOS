#!/usr/bin/env python3
"""Apply narrowly scoped integration to the exact upstream revision in revision.txt."""
from pathlib import Path
import sys
root = Path(sys.argv[1])
p = root / 'linux/main.cpp'
s = p.read_text()
def replace(old, new):
    global s
    if old not in s:
        raise SystemExit(f'Upstream integration anchor missing: {old[:70]}')
    s = s.replace(old, new, 1)
replace('    bool areAirpodsConnected() const', '''    bool shellNoiseModeKnown = false;
    QDateTime shellLastNearby;
    QString shellNearbyAddress() const {
        return shellLastNearby.isValid() && shellLastNearby.msecsTo(QDateTime::currentDateTime()) < 10000
            ? m_settings->value("DaisyOS/address").toString() : QString();
    }
    bool areAirpodsConnected() const''')
replace('        m_deviceInfo->setBluetoothAddress(device.address().toString());',
        '        shellNoiseModeKnown = false;\n        m_settings->setValue("DaisyOS/address", device.address().toString());\n        m_deviceInfo->setBluetoothAddress(device.address().toString());')
replace('                m_deviceInfo->setNoiseControlMode(value.value());',
        '                shellNoiseModeKnown = true;\n                m_deviceInfo->setNoiseControlMode(value.value());')
# Reconnect only from authenticated, fresh advertisements for an available pair.
replace('        if (BLEUtils::isValidIrkRpa(m_deviceInfo->magicAccIRK(), device.address)) {', '        if (BLEUtils::isValidIrkRpa(m_deviceInfo->magicAccIRK(), device.address)) {\n            shellLastNearby = device.connectionState == BleInfo::ConnectionState::DISCONNECTED\n                && (!device.areBothPodsInCase || device.lidState == BleInfo::LidState::OPEN)\n                ? QDateTime::currentDateTime() : QDateTime();')
# The shell, not arbitrary media playback, owns connection decisions.
replace('            sendDisconnectRequestToAndroid();\n            connectToAirPods(true);', '            // Do not steal AirPods from another host when local media starts.\n            connectToAirPods(false);')
replace('    void initializeBluetooth() {\n        connectToPhone();', '    void initializeBluetooth() {\n        auto adapter = new QBluetoothLocalDevice(this);\n        connect(adapter, &QBluetoothLocalDevice::hostModeStateChanged, this,\n            [this](QBluetoothLocalDevice::HostMode mode) {\n                if (mode == QBluetoothLocalDevice::HostPoweredOff) m_bleManager->stopScan();\n                else if (!areAirpodsConnected()) m_bleManager->startScan();\n            });\n        connectToPhone();')
replace('int main(int argc, char *argv[])', '#include "shell-bridge.hpp"\n\nint main(int argc, char *argv[])')
# Upstream deletes the live socket before testing for another process. Keep its
# single-instance test meaningful; the stale socket is removed before listen.
replace('    QLocalServer::removeServer("app_server");\n\n    QFile stale("/tmp/app_server");\n    if (stale.exists())\n        stale.remove();\n', '')
replace('    trayApp->loadMainModule();\n\n    QLocalServer server;',
        '    trayApp->loadMainModule();\n    startShellBridge(trayApp, &app);\n\n    QLocalServer server;')
# Keep legacy IPC in the user's runtime directory as well.
s = s.replace('"app_server"', '(QStandardPaths::writableLocation(QStandardPaths::RuntimeLocation) + "/daisyos-librepods-legacy.sock")')
s = s.replace('        QFile stale("/tmp/app_server");\n        if (stale.exists())\n            stale.remove();', '')
p.write_text(s)
# The shell owns notifications and the UI; keep the companion quiet.
p = root / 'linux/trayiconmanager.cpp'
s = p.read_text().replace('    trayIcon->show();', '    // DaisyOS provides the AirPods UI.')
s = s.replace('void TrayIconManager::showNotification(const QString &title, const QString &message)\n{',
              'void TrayIconManager::showNotification(const QString &title, const QString &message)\n{\n    return; // DaisyOS owns connection notifications.')
p.write_text(s)
