// SPDX-License-Identifier: GPL-2.0-or-later
#include "daisymenustyle.h"
#include <KConfig>
#include <KConfigGroup>
#include <QApplication>
#include <QAction>
#include <QFile>
#include <QMenu>
#include <QPainter>
#include <QPainterPath>
#include <QStandardPaths>
#include <QStyleFactory>
#include <QStyleOptionMenuItem>
#include <QSvgRenderer>
#include <algorithm>

DaisyMenuStyle::DaisyMenuStyle(const QString &configFile, QObject *parent)
    : QProxyStyle(QStyleFactory::create(QStringLiteral("Fusion"))), m_configFile(configFile)
{
    setParent(parent);
    setObjectName(QStringLiteral("DaisyOSWindowMenu"));
    reload();
}

void DaisyMenuStyle::reload()
{
    m_icons.clear();
    KConfig config(m_configFile, KConfig::SimpleConfig);
    const KConfigGroup group(&config, QStringLiteral("Menus"));
    auto color = [&](const char *key, const char *fallback) {
        const QColor value(group.readEntry(key, QString::fromLatin1(fallback)));
        return value.isValid() ? value : QColor(QString::fromLatin1(fallback));
    };
    m_surface = color("Surface", "#99212121");
    m_outer = color("FrameOuter", "#99000000");
    m_inner = color("FrameInner", "#14ffffff");
    m_text = color("ContentPrimary", "#ffffffff");
    m_secondary = color("ContentSecondary", "#ccffffff");
    m_disabled = color("ContentDisabled", "#66ffffff");
    m_hover = color("StateHover", "#14ffffff");
    m_pressed = color("StatePressed", "#24ffffff");
    m_separator = color("Separator", "#26ffffff");
    m_font = QFont(group.readEntry("FontFamily", QStringLiteral("Inter")));
    m_font.setPixelSize(std::clamp(group.readEntry("FontPixels", 13), 10, 40));
    m_font.setWeight(QFont::Normal);
    m_font.setFeature("tnum", 0);
    m_font.setFeature("pnum", 1);
    m_font.setHintingPreference(QFont::PreferFullHinting);
    m_radius = std::clamp(group.readEntry("CornerRadius", 14.0), 0.0, 40.0);
    m_itemRadius = std::clamp(group.readEntry("ItemCornerRadius", 8.0), 0.0, 30.0);
    m_itemHeight = std::clamp(group.readEntry("ItemHeight", 32), 24, 96);
    m_minWidth = std::clamp(group.readEntry("MinWidth", 184), 120, 600);
    m_iconSize = std::clamp(group.readEntry("IconSize", 18), 12, 40);
}

void DaisyMenuStyle::polish(QWidget *widget)
{
    QProxyStyle::polish(widget);
    if (auto *menu = qobject_cast<QMenu *>(widget)) {
        menu->setFont(m_font);
        menu->setMouseTracking(true);
        menu->setAttribute(Qt::WA_TranslucentBackground);
        menu->setWindowFlag(Qt::NoDropShadowWindowHint, true);
    }
}

void DaisyMenuStyle::drawPrimitive(PrimitiveElement element, const QStyleOption *option, QPainter *painter, const QWidget *widget) const
{
    if (element == PE_FrameMenu) return; // The DaisyOS frame is painted by PE_PanelMenu.
    if (element != PE_PanelMenu) {
        QProxyStyle::drawPrimitive(element, option, painter, widget);
        return;
    }
    painter->save();
    painter->setRenderHint(QPainter::Antialiasing);
    painter->setPen(Qt::NoPen);
    const QRectF rect(option->rect);
    QPainterPath outer, inner, content;
    outer.addRoundedRect(rect, m_radius, m_radius);
    inner.addRoundedRect(rect.adjusted(1, 1, -1, -1), std::max(0.0, m_radius - 1), std::max(0.0, m_radius - 1));
    content.addRoundedRect(rect.adjusted(2, 2, -2, -2), std::max(0.0, m_radius - 2), std::max(0.0, m_radius - 2));
    // Paint frame rings only: an opaque full-size underlay defeats blur.
    painter->fillPath(outer.subtracted(inner), m_outer);
    painter->fillPath(inner.subtracted(content), m_inner);
    painter->fillPath(content, m_surface);
    painter->restore();
}

static QString iconAsset(const QString &name)
{
    static const QHash<QString, QString> icons{
        {"window-maximize", "Maximize"}, {"window-restore", "arrow-shrink"},
        {"window-minimize", "Minimize"}, {"window-close", "cancel-01"},
        {"window-keep-above", "arrow-up-02"}, {"window-keep-below", "arrow-down-02"},
        {"transform-move", "drag-drop"}, {"transform-scale", "arrow-expand"},
        {"view-fullscreen", "full-screen"}, {"edit-none-border", "square-dashed"},
        {"view-private", "view-off"}, {"configure-shortcuts", "keyboard"},
        {"preferences-system-windows-actions", "settings-01"}, {"overflow-menu", "more-vertical"},
        {"computer", "computer"}, {"virtual-desktops", "square-stack"}, {"activities", "square-stack"}
    };
    return icons.value(name);
}

void DaisyMenuStyle::drawControl(ControlElement element, const QStyleOption *option, QPainter *painter, const QWidget *widget) const
{
    if (element != CE_MenuItem) {
        QProxyStyle::drawControl(element, option, painter, widget);
        return;
    }
    const auto *item = qstyleoption_cast<const QStyleOptionMenuItem *>(option);
    if (!item) return;
    painter->save();
    painter->setRenderHint(QPainter::Antialiasing);
    painter->setFont(m_font);
    const QRectF bounds(item->rect);
    if (item->menuItemType == QStyleOptionMenuItem::Separator) {
        painter->setPen(QPen(m_separator, 1));
        painter->drawLine(QPointF(bounds.left() + 8, bounds.center().y() + .5), QPointF(bounds.right() - 8, bounds.center().y() + .5));
        painter->restore();
        return;
    }
    const bool enabled = item->state & State_Enabled;
    const bool selected = enabled && (item->state & State_Selected);
    if (selected) {
        painter->setPen(Qt::NoPen);
        painter->setBrush(item->state & State_Sunken ? m_pressed : m_hover);
        painter->drawRoundedRect(bounds.adjusted(0, 1, 0, -1), m_itemRadius, m_itemRadius);
    }
    const QColor foreground = enabled ? m_text : m_disabled;
    const bool rtl = item->direction == Qt::RightToLeft;
    auto visualRect = [&](const QRectF &rect) {
        return rtl ? QRectF(bounds.right() - (rect.left() - bounds.left()) - rect.width(), rect.top(), rect.width(), rect.height()) : rect;
    };
    const QRectF iconRect = visualRect(QRectF(bounds.left() + 8, bounds.center().y() - m_iconSize / 2.0, m_iconSize, m_iconSize));
    const auto *menu = qobject_cast<const QMenu *>(widget);
    const auto *action = menu ? menu->actionAt(item->rect.center()) : nullptr;
    const QString suppliedAsset = action ? action->property("daisyosMenuIcon").toString() : QString();
    const QString asset = suppliedAsset.isEmpty() ? iconAsset(item->icon.name()) : suppliedAsset;
    if (!asset.isEmpty()) {
        const qreal dpr = painter->device()->devicePixelRatioF();
        const QString key = asset + QLatin1Char(':') + foreground.name(QColor::HexArgb) + QLatin1Char(':') + QString::number(dpr);
        auto cached = m_icons.constFind(key);
        if (cached == m_icons.cend()) {
            const QString path = QStandardPaths::locate(QStandardPaths::GenericDataLocation, QStringLiteral("daisyos/window-menu-icons/") + asset + QStringLiteral(".svg"));
            QFile file(path);
            if (file.open(QIODevice::ReadOnly)) {
                auto data = file.readAll();
                data.replace("currentColor", "#000000");
                QSvgRenderer renderer(data);
                QPixmap glyph(QSize(qCeil(m_iconSize * dpr), qCeil(m_iconSize * dpr)));
                glyph.setDevicePixelRatio(dpr);
                glyph.fill(Qt::transparent);
                QPainter iconPainter(&glyph);
                renderer.render(&iconPainter, QRectF(0, 0, m_iconSize, m_iconSize));
                // Shared HugeIcons include both currentColor and literal strokes.
                // Tint the rendered alpha, exactly as the shell's mono icon control.
                iconPainter.setCompositionMode(QPainter::CompositionMode_SourceIn);
                iconPainter.fillRect(QRectF(0, 0, m_iconSize, m_iconSize), foreground);
                iconPainter.end();
                cached = m_icons.insert(key, glyph);
            }
        }
        if (cached != m_icons.cend()) painter->drawPixmap(iconRect.topLeft(), cached.value());
    }
    // A real checkmark replaces Qt's checkbox-style window-state indicator.
    const QRectF checkRect = visualRect(QRectF(bounds.left() + 8 + m_iconSize + 10, bounds.center().y() - 7, 14, 14));
    if (item->checked) {
        painter->setPen(QPen(foreground, 1.8, Qt::SolidLine, Qt::RoundCap, Qt::RoundJoin));
        QPainterPath check;
        check.moveTo(checkRect.left() + 2, checkRect.center().y());
        check.lineTo(checkRect.left() + 5.5, checkRect.bottom() - 3);
        check.lineTo(checkRect.right() - 1, checkRect.top() + 3);
        painter->drawPath(check);
    }
    const int textInset = 8 + m_iconSize + 10 + 20;
    const QRectF textRect = visualRect(bounds.adjusted(textInset, 0, -28, 0));
    const QStringList parts = item->text.split(QLatin1Char('\t'));
    painter->setPen(foreground);
    const int textFlags = Qt::AlignVCenter | Qt::TextShowMnemonic | (rtl ? Qt::AlignRight : Qt::AlignLeft);
    const QFontMetrics metrics(m_font);
    const int shortcutWidth = parts.size() > 1 ? metrics.horizontalAdvance(parts[1]) + 24 : 0;
    QRectF labelRect = textRect;
    if (rtl) labelRect.adjust(shortcutWidth, 0, 0, 0); else labelRect.adjust(0, 0, -shortcutWidth, 0);
    painter->drawText(labelRect, textFlags, metrics.elidedText(parts[0], Qt::ElideRight, int(labelRect.width())));
    if (parts.size() > 1) {
        painter->setPen(enabled ? m_secondary : m_disabled);
        painter->drawText(textRect, Qt::AlignVCenter | (rtl ? Qt::AlignLeft : Qt::AlignRight), parts[1]);
    }
    if (item->menuItemType == QStyleOptionMenuItem::SubMenu) {
        const QRectF arrow = visualRect(QRectF(bounds.right() - 17, bounds.center().y() - 4, 5, 8));
        painter->setPen(QPen(foreground, 1.5, Qt::SolidLine, Qt::RoundCap, Qt::RoundJoin));
        QPainterPath path;
        path.moveTo(rtl ? arrow.right() : arrow.left(), arrow.top());
        path.lineTo(rtl ? arrow.left() : arrow.right(), arrow.center().y());
        path.lineTo(rtl ? arrow.right() : arrow.left(), arrow.bottom());
        painter->drawPath(path);
    }
    painter->restore();
}

QSize DaisyMenuStyle::sizeFromContents(ContentsType type, const QStyleOption *option, const QSize &size, const QWidget *widget) const
{
    if (type != CT_MenuItem) return QProxyStyle::sizeFromContents(type, option, size, widget);
    const auto *item = qstyleoption_cast<const QStyleOptionMenuItem *>(option);
    if (!item) return size;
    if (item->menuItemType == QStyleOptionMenuItem::Separator) return QSize(m_minWidth - 12, 9);
    const auto parts = item->text.split(QLatin1Char('\t'));
    const QFontMetrics metrics(m_font);
    int width = metrics.horizontalAdvance(parts[0]) + 8 + m_iconSize + 10 + 20 + 28;
    if (parts.size() > 1) width += metrics.horizontalAdvance(parts[1]) + 24;
    return QSize(std::max(m_minWidth - 12, width), std::max(m_itemHeight, metrics.height() + 12) + 2);
}

int DaisyMenuStyle::pixelMetric(PixelMetric metric, const QStyleOption *option, const QWidget *widget) const
{
    switch (metric) {
    case PM_MenuPanelWidth: return 6;
    case PM_MenuHMargin: case PM_MenuVMargin: case PM_MenuDesktopFrameWidth: return 0;
    case PM_SmallIconSize: return m_iconSize;
    default: return QProxyStyle::pixelMetric(metric, option, widget);
    }
}
int DaisyMenuStyle::styleHint(StyleHint hint, const QStyleOption *option, const QWidget *widget, QStyleHintReturn *data) const
{
    switch (hint) {
    case SH_Menu_MouseTracking: return true;
    case SH_Menu_SubMenuPopupDelay: return 160;
    case SH_Menu_Scrollable: return true;
    case SH_Menu_SloppySubMenus: return true;
    default: return QProxyStyle::styleHint(hint, option, widget, data);
    }
}
