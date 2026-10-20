// SPDX-License-Identifier: GPL-2.0-or-later
#pragma once
#include <QProxyStyle>
#include <QColor>
#include <QFont>

// Native counterpart of DropdownMenuStyles.axaml. Only QMenu is styled;
// KWin retains its actions, enabled/check states, shortcuts and target window.
class DaisyMenuStyle final : public QProxyStyle {
public:
    explicit DaisyMenuStyle(const QString &configFile, QObject *parent = nullptr);
    void reload();
    void polish(QWidget *widget) override;
    void drawPrimitive(PrimitiveElement, const QStyleOption *, QPainter *, const QWidget *) const override;
    void drawControl(ControlElement, const QStyleOption *, QPainter *, const QWidget *) const override;
    QSize sizeFromContents(ContentsType, const QStyleOption *, const QSize &, const QWidget *) const override;
    int pixelMetric(PixelMetric, const QStyleOption * = nullptr, const QWidget * = nullptr) const override;
    int styleHint(StyleHint, const QStyleOption * = nullptr, const QWidget * = nullptr, QStyleHintReturn * = nullptr) const override;
private:
    QString m_configFile;
    QColor m_surface, m_outer, m_inner, m_text, m_secondary, m_disabled, m_hover, m_pressed, m_separator;
    QFont m_font;
    qreal m_radius = 14, m_itemRadius = 8;
    int m_itemHeight = 32, m_minWidth = 184, m_iconSize = 18;
};
