// SPDX-License-Identifier: GPL-2.0-or-later
#include "daisymenustyle.h"
#include <effect/effect.h>
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
                menu->updateGeometry();
                menu->update();
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
            menu->setStyle(nullptr);
            menu->setProperty("daisyosWindowMenu", QVariant());
        }
    }
    bool isActive() const override { return false; } // Never inhibits direct scanout.
    bool eventFilter(QObject *object, QEvent *event) override
    {
        if (event->type() == QEvent::Polish || event->type() == QEvent::Show) {
            if (auto *menu = qobject_cast<QMenu *>(object)) styleMenu(menu);
        }
        return false; // KWin keeps all keyboard, pointer, focus and dismissal handling.
    }
    void styleMenu(QMenu *menu)
    {
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
                {"x", menu->x()}, {"y", menu->y()}, {"width", menu->width()}, {"height", menu->height()}});
        }
        return QString::fromUtf8(QJsonDocument(QJsonObject{{"styledMenus", styled}, {"visible", visible}}).toJson(QJsonDocument::Compact));
    }
};
KWIN_EFFECT_FACTORY_SUPPORTED(DaisyWindowMenus, "metadata.json", return qobject_cast<QApplication *>(QCoreApplication::instance());)
}
#include "main.moc"
