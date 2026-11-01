// SPDX-License-Identifier: MIT
// Test-only effect, loaded exclusively in the disposable compositor fixture.
#include <effect/effect.h>
#include <workspace.h>
#include <window.h>
#include <QJsonDocument>
#include <QJsonObject>

class SnapProbe final : public KWin::Effect
{
    Q_OBJECT
public:
    bool isActive() const override { return false; }
    QString debug(const QString &request) const override
    {
        const auto input = QJsonDocument::fromJson(request.toUtf8()).object();
        const auto caption = input["caption"].toString();
        if (!caption.startsWith(QStringLiteral("Daisy Snap Test "))) return QStringLiteral("{}");
        for (auto *w : KWin::workspace()->windows()) {
            if (w->caption() != caption) continue;
            const auto op = input["op"].toString();
            QJsonObject result;
            if (op == "place") {
                w->setQuickTileModeAtCurrentPosition({});
                w->moveResize(KWin::RectF(input["x"].toDouble(), input["y"].toDouble(), 300, 200));
            } else if (op == "snap") {
                const auto point = KWin::workspace()->adjustWindowPosition(w,
                    QPointF(input["x"].toDouble(), input["y"].toDouble()), false);
                result["x"] = point.x();
                result["y"] = point.y();
            } else if (op == "tile") {
                KWin::workspace()->setActiveWindow(w);
                const auto mode = input["mode"].toString();
                KWin::QuickTileMode flags;
                if (mode.contains("left")) flags |= KWin::QuickTileFlag::Left;
                if (mode.contains("right")) flags |= KWin::QuickTileFlag::Right;
                if (mode.contains("top")) flags |= KWin::QuickTileFlag::Top;
                if (mode.contains("bottom")) flags |= KWin::QuickTileFlag::Bottom;
                KWin::workspace()->quickTileWindow(flags);
            }
            const auto frame = w->frameGeometry();
            const auto area = KWin::workspace()->clientArea(KWin::MaximizeArea, w);
            result["frame"] = QJsonObject{{"x", frame.x()}, {"y", frame.y()}, {"width", frame.width()}, {"height", frame.height()}};
            result["area"] = QJsonObject{{"x", area.x()}, {"y", area.y()}, {"width", area.width()}, {"height", area.height()}};
            return QString::fromUtf8(QJsonDocument(result).toJson(QJsonDocument::Compact));
        }
        return QStringLiteral("{}");
    }
};
KWIN_EFFECT_FACTORY(SnapProbe, "metadata.json")
#include "probe.moc"
