#pragma once

#include <KDecoration3/Decoration>

#include <QColor>
#include <QRectF>

#include <memory>

class QPainter;

namespace KDecoration3
{
class DecorationShadow;
}

struct DaisyFrameMetrics
{
    qreal thickness = 2.0;
    qreal outerStroke = 1.0;
    qreal clientCornerClipGuard = 1.0;
    qreal resizeOnlyBorder = 6.0;
    qreal cornerRadius = 12.0;

    KDecoration3::BorderRadius borderRadius(bool maximized) const;
};

struct DaisyFrameColors
{
    QColor surfaceBase;
    QColor outerEdge;
    QColor innerHighlight;
};

class DaisyFramePainter
{
public:
    static QRectF inset(const QRectF &rect, qreal amount);
    static void paint(QPainter *painter,
                      const QRectF &rect,
                      const DaisyFrameMetrics &metrics,
                      const DaisyFrameColors &colors,
                      bool maximized);
};

class DaisyShadowFactory
{
public:
    // DecorationShadow is deliberately shared and contains only the soft
    // shadow. The compositor effect owns all visible frame strokes.
    static std::shared_ptr<KDecoration3::DecorationShadow> get(const DaisyFrameMetrics &metrics,
                                                                const DaisyFrameColors &colors,
                                                                bool active,
                                                                bool includeFrame);
};
