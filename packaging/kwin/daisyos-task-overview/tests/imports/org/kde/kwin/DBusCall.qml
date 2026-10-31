import QtQuick

QtObject {
    property string service
    property string path
    property string dbusInterface
    property string method
    property var arguments: []
    property var sent: []
    function call() { sent.push(JSON.parse(this.arguments[0])); }
    signal finished(var reply)
    signal failed()
}
