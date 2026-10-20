// SPDX-License-Identifier: GPL-2.0-or-later
#include "daisymenustyle.h"
#include <effect/effect.h>
#include <options.h>
#include <core/region.h>
#include <QWindow>
#include <QPainterPath>
#include <QAction>
#include <QApplication>
#include <QDBusConnection>
#include <QDir>
#include <QFileSystemWatcher>
#include <QJsonArray>
#include <QJsonDocument>
#include <QJsonObject>
#include <QMenu>
#include <QPointer>
#include <QStandardPaths>
#include <QTimer>

namespace KWin {
// All native KWin QMenus share this renderer, including window-decoration,
// keyboard, Wayland xdg_toplevel and XWayland window-menu requests. No input
// interception, synthetic actions, or active-window inference is involved.
class DaisyWindowMenus final : public Effect {
    Q_OBJECT
    Q_CLASSINFO("D-Bus Interface", "org.daisyos.WindowMenus1")
    DaisyMenuStyle *m_style;
    QList<QPointer<QMenu>> m_menus;
    QFileSystemWatcher m_watcher;
    QTimer m_reload;
public:
    DaisyWindowMenus()
        : m_style(new DaisyMenuStyle(QStandardPaths::writableLocation(QStandardPaths::ConfigLocation)
              + QStringLiteral("/daisyos-window-decorationrc"), this))
    {
        qApp->installEventFilter(this);
        // A shell/effect reload may happen after KWin has constructed its menu.
        for (auto *widget : QApplication::allWidgets()) {
            if (auto *menu = qobject_cast<QMenu *>(widget)) styleMenu(menu);
        }
        const QString configDir = QStandardPaths::writableLocation(QStandardPaths::ConfigLocation);
        QDir().mkpath(configDir);
        m_watcher.addPath(configDir); // Theme export replaces the file atomically.
        m_reload.setSingleShot(true);
        m_reload.setInterval(50);
        connect(&m_watcher, &QFileSystemWatcher::directoryChanged, &m_reload, qOverload<>(&QTimer::start));
        connect(&m_reload, &QTimer::timeout, this, [this] {
            m_style->reload();
            for (const auto &menu : std::as_const(m_menus)) {
                if (!menu) continue;
                m_style->polish(menu);
                QEvent changed(QEvent::StyleChange);
                QCoreApplication::sendEvent(menu, &changed);
                menu->updateGeometry();
                if (menu->isVisible()) menu->adjustSize();
                menu->update();
                updateBlur(menu);
            }
        });
        QDBusConnection::sessionBus().registerObject(QStringLiteral("/org/daisyos/WindowMenus"), this, QDBusConnection::ExportAllSlots);
    }
    ~DaisyWindowMenus() override
    {
        qApp->removeEventFilter(this);
        QDBusConnection::sessionBus().unregisterObject(QStringLiteral("/org/daisyos/WindowMenus"));
        for (const auto &menu : std::as_const(m_menus)) {
            if (!menu) continue;
            menu->close();
            if (menu->windowHandle()) menu->windowHandle()->setProperty("kwin_blur", QVariant());
            menu->setStyle(nullptr);
            menu->setProperty("daisyosWindowMenu", QVariant());
        }
    }
    bool isActive() const override { return false; } // Never inhibits direct scanout.
    bool eventFilter(QObject *object, QEvent *event) override
    {
        if (event->type() == QEvent::Polish || event->type() == QEvent::Show || event->type() == QEvent::Resize || event->type() == QEvent::WinIdChange) {
            if (auto *menu = qobject_cast<QMenu *>(object)) {
                styleMenu(menu);
                QTimer::singleShot(0, this, [this, menu = QPointer<QMenu>(menu)] { if (menu) updateBlur(menu); });
            }
        }
        return false; // KWin keeps all keyboard, pointer, focus and dismissal handling.
    }
    void updateBlur(QMenu *menu)
    {
        if (!menu->windowHandle() || menu->size().isEmpty()) return;
        QPainterPath shape;
        shape.addRoundedRect(QRectF(QPointF(), menu->size()), m_style->cornerRadius(), m_style->cornerRadius());
        // KWin's internal Qt windows consume RegionF through this property.
        const RegionF region(QRegion(shape.toFillPolygon().toPolygon()));
        menu->windowHandle()->setProperty("kwin_blur", QVariant::fromValue(region));
        menu->setProperty("daisyosBlurRequested", true);
    }
    void styleMenu(QMenu *menu)
    {
        for (auto *action : menu->actions()) {
            QString asset;
            if (action->data().isValid()) {
                // Use KWin's operation IDs, never localized text or icon-theme availability.
                switch (static_cast<Options::WindowOperation>(action->data().toInt())) {
                case Options::MaximizeOp: asset = "Maximize"; break;
                case Options::UntileOrRestoreOp: asset = "arrow-shrink"; break;
                case Options::MinimizeOp: asset = "Minimize"; break;
                case Options::MoveOp: case Options::UnrestrictedMoveOp: asset = "drag-drop"; break;
                case Options::ResizeOp: case Options::UnrestrictedResizeOp: asset = "arrow-expand"; break;
                case Options::CloseOp: asset = "cancel-01"; break;
                case Options::OnAllDesktopsOp: asset = "square-stack"; break;
                case Options::KeepAboveOp: asset = "arrow-up-02"; break;
                case Options::KeepBelowOp: asset = "arrow-down-02"; break;
                case Options::FullScreenOp: asset = "full-screen"; break;
                case Options::NoBorderOp: asset = "square-dashed"; break;
                case Options::ExcludeFromCaptureOp: asset = "view-off"; break;
                case Options::SetupWindowShortcutOp: asset = "keyboard"; break;
                case Options::WindowRulesOp: case Options::ApplicationRulesOp: asset = "settings-01"; break;
                default: break;
                }
            }
            if (!asset.isEmpty()) action->setProperty("daisyosMenuIcon", asset);
        }
        if (menu->property("daisyosWindowMenu").toBool()) return;
        menu->setProperty("daisyosWindowMenu", true);
        m_menus.append(menu);
        menu->setStyle(m_style);
        // Preserve the concrete menu's shortcut and action objects unchanged.
    }
public Q_SLOTS:
    QString State() const
    {
        QJsonArray visible;
        int styled = 0;
        for (const auto &menu : m_menus) {
            if (!menu) continue;
            ++styled;
            if (menu->isVisible()) visible.append(QJsonObject{
                {"style", menu->style()->objectName()}, {"font", menu->font().family()},
                {"blurRequested", menu->property("daisyosBlurRequested").toBool()},
                {"surfaceAlpha", m_style->surfaceColor().alpha()}, {"x", menu->x()}, {"y", menu->y()}, {"width", menu->width()}, {"height", menu->height()}});
        }
        return QString::fromUtf8(QJsonDocument(QJsonObject{{"rendererRevision", 4}, {"styledMenus", styled}, {"visible", visible}}).toJson(QJsonDocument::Compact));
    }
};
KWIN_EFFECT_FACTORY_SUPPORTED(DaisyWindowMenus, "metadata.json", return qobject_cast<QApplication *>(QCoreApplication::instance());)
}
#include "main.moc"
