#pragma once

#include "daisyframestyle.h"

#include <KDecoration3/Decoration>
#include <KDecoration3/DecorationButton>

#include <QColor>
#include <QFileSystemWatcher>
#include <QPointer>
#include <QVariant>

#include <memory>

class QPainter;

namespace KDecoration3
{
class DecorationButtonGroup;
class DecorationShadow;
}

class DaisyDecoration;

class DaisyCaptionButton final : public KDecoration3::DecorationButton
{
    Q_OBJECT

public:
    DaisyCaptionButton(KDecoration3::DecorationButtonType type, DaisyDecoration *decoration, QObject *parent);

    void paint(QPainter *painter, const QRectF &repaintRegion) override;

private:
    QPointer<DaisyDecoration> m_daisyDecoration;
};

class DaisyDecoration final : public KDecoration3::Decoration
{
    Q_OBJECT

public:
    explicit DaisyDecoration(QObject *parent = nullptr, const QVariantList &args = {});

    bool init() override;
    void paint(QPainter *painter, const QRectF &repaintRegion) override;

    QColor foreground() const;
    QColor onActionForeground() const;
    QColor disabledForeground() const;
    QColor buttonBackground(KDecoration3::DecorationButtonType type, bool hovered, bool pressed) const;
    qreal captionGlyphSize() const;
    void paintCaptionButtonBackground(QPainter *painter,
                                      KDecoration3::DecorationButtonType type,
                                      const QRectF &bounds,
                                      const QColor &background) const;

private Q_SLOTS:
    void reloadTheme();
    void updateLayout();
    void updateActiveState();

private:
    struct Palette {
        QColor surfaceBase;
        QColor surfaceRaised;
        QColor outerEdge;
        QColor innerHighlight;
        QColor contentPrimary;
        QColor contentDisabled;
        QColor stateHover;
        QColor statePressed;
        QColor danger;
        QColor onAction;
    };

    void updateFrameGeometry();
    void updateButtonsGeometry();
    void watchThemeFile();
    bool isShadowOnly() const;
    QColor titleSurface() const;
    DaisyFrameColors frameColors() const;

    Palette m_palette;
    std::unique_ptr<QFileSystemWatcher> m_themeWatcher;
    KDecoration3::DecorationButtonGroup *m_captionButtons = nullptr;
    qreal m_titleBarHeight = 32.0;
    qreal m_captionButtonWidth = 46.0;
    qreal m_captionButtonHeight = 32.0;
    qreal m_captionGlyphSize = 10.0;
    int m_titleFontPixels = 13;
    QString m_titleFontFamily = QStringLiteral("Inter");
    DaisyFrameMetrics m_frameMetrics;
    qreal m_visibleCaptionButtonsWidth = 0.0;
};
