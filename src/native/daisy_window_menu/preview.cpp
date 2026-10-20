// SPDX-License-Identifier: GPL-2.0-or-later
#include "daisymenustyle.h"
#include <QApplication>
#include <QAction>
#include <QMenu>
#include <QPixmap>
#include <QTimer>
#include <QStyleOptionMenuItem>
#include <QPainter>
#include <QDebug>
#include <QLabel>

int main(int argc, char **argv)
{
    QApplication app(argc, argv);
    QIcon::setThemeSearchPaths({QStringLiteral("/usr/share/icons")});
    QIcon::setThemeName(QStringLiteral("breeze"));
    if (argc == 2 && QString::fromLocal8Bit(argv[1]) == QStringLiteral("--interactive")) {
        QLabel window(QStringLiteral("DaisyOS native window-menu verification\nRight-click the title bar or press Alt+F3."));
        window.setWindowTitle(QStringLiteral("DaisyOS menu verification"));
        window.setAlignment(Qt::AlignCenter);
        window.resize(460, 220);
        window.show();
        QTimer::singleShot(180000, &app, &QApplication::quit);
        return app.exec();
    }
    if (argc != 3) return 2;
    DaisyMenuStyle style(QString::fromLocal8Bit(argv[1]));
    QMenu menu;
    menu.setStyle(&style);
    auto *maximize = menu.addAction(QIcon::fromTheme("window-maximize"), "Maximize");
    maximize->setCheckable(true);
    maximize->setEnabled(false);
    auto *minimize = menu.addAction(QIcon::fromTheme("window-minimize"), "Minimize");
    minimize->setShortcut(QKeySequence("Meta+PgDown"));
    auto *above = menu.addAction(QIcon::fromTheme("window-keep-above"), "Keep Above Others");
    above->setCheckable(true);
    above->setChecked(true);
    auto *more = menu.addMenu(QIcon::fromTheme("overflow-menu"), "More Actions");
    more->setStyle(&style);
    auto *move = more->addAction(QIcon::fromTheme("transform-move"), "Move");
    more->addAction(QIcon::fromTheme("transform-scale"), "Resize")->setEnabled(false);
    auto *below = more->addAction(QIcon::fromTheme("window-keep-below"), "Keep Below Others");
    below->setCheckable(true);
    more->addAction(QIcon::fromTheme("view-fullscreen"), "Fullscreen")->setCheckable(true);
    auto *border = more->addAction(QIcon::fromTheme("edit-none-border"), "No Titlebar and Frame");
    border->setCheckable(true);
    border->setProperty("daisyosMenuIcon", QStringLiteral("square-dashed"));
    more->addSeparator();
    more->addAction(QIcon::fromTheme("configure-shortcuts"), "Set Window Shortcut…");
    more->addAction(QIcon::fromTheme("preferences-system-windows-actions"), "Configure Special Window Settings…");
    menu.addSeparator();
    auto *close = menu.addAction(QIcon::fromTheme("window-close"), "Close");
    close->setShortcut(QKeySequence("Alt+F4"));
    int called = 0;
    QObject::connect(move, &QAction::triggered, [&] { called++; });
    menu.ensurePolished();
    menu.adjustSize();
    more->ensurePolished();
    more->adjustSize();
    menu.setActiveAction(more->menuAction());
    const qreal scale = qEnvironmentVariable("DAISY_MENU_PREVIEW_SCALE", "1").toDouble();
    const QSize logicalSize(menu.width() + more->width() + 28, std::max(menu.height(), more->height() + 120) + 24);
    QPixmap image(logicalSize * scale);
    image.setDevicePixelRatio(scale);
    image.fill(QColor("#171717"));
    QPainter painter(&image);
    menu.render(&painter, QPoint(12, 12));
    more->render(&painter, QPoint(menu.width() + 16, 120));
    painter.end();
    if (!image.save(QString::fromLocal8Bit(argv[2]))) return 3;
    // Painting must preserve command identity, checked/disabled state and shortcuts.
    move->trigger();
    if (menu.font().featureValue("tnum") != 0 || menu.font().featureValue("pnum") != 1
        || called != 1 || maximize->isEnabled() || !above->isChecked()
        || minimize->shortcut() != QKeySequence("Meta+PgDown") || close->shortcut() != QKeySequence("Alt+F4")) return 4;
    qInfo() << "DaisyOS native menu rendering and action-state checks passed" << image.size() << menu.font().family();
    return 0;
}
