#include "daisyframestyle.h"

#include <KDecoration3/DecorationShadow>

#include <QHash>
#include <QImage>
#include <QMarginsF>
#include <QMutex>
#include <QMutexLocker>
#include <QPainter>
#include <QPainterPath>

#include <algorithm>

KDecoration3::BorderRadius DaisyFrameMetrics::borderRadius(bool maximized) const
{
    return KDecoration3::BorderRadius(maximized ? 0.0 : cornerRadius + clientCornerClipGuard);
}

QRectF DaisyFramePainter::inset(const QRectF &rect, qreal amount)
{
    return rect.marginsRemoved(QMarginsF(amount, amount, amount, amount));
}

QPainterPath DaisyFramePainter::titleBarClip(const QRectF &frame, qreal titleHeight, qreal radius)
{
    QPainterPath result;
    if (frame.isEmpty() || titleHeight <= 0) return result;
    radius = std::clamp(radius, 0.0, std::min(frame.width(), frame.height()) / 2.0);
    result.addRoundedRect(frame, radius, radius);
    QPainterPath title;
    title.addRect(QRectF(frame.topLeft(), QSizeF(frame.width(), std::min(titleHeight, frame.height()))));
    return result.intersected(title);
}

void DaisyFramePainter::paint(QPainter *painter,
                              const QRectF &rect,
                              const DaisyFrameMetrics &metrics,
                              const DaisyFrameColors &colors,
                              bool maximized)
{
    const qreal radius = maximized ? 0.0 : metrics.cornerRadius;

    painter->save();
    painter->setRenderHint(QPainter::Antialiasing);
    painter->setPen(Qt::NoPen);
    painter->setBrush(colors.outerEdge);
    maximized ? painter->drawRect(rect) : painter->drawRoundedRect(rect, radius, radius);

    const QRectF highlight = inset(rect, metrics.outerStroke);
    painter->setBrush(colors.innerHighlight);
    maximized
        ? painter->drawRect(highlight)
        : painter->drawRoundedRect(highlight,
                                   std::max<qreal>(0.0, radius - metrics.outerStroke),
                                   std::max<qreal>(0.0, radius - metrics.outerStroke));

    const QRectF content = inset(rect, metrics.thickness);
    painter->setBrush(colors.surfaceBase);
    maximized
        ? painter->drawRect(content)
        : painter->drawRoundedRect(content,
                                   std::max<qreal>(0.0, radius - metrics.thickness),
                                   std::max<qreal>(0.0, radius - metrics.thickness));
    painter->restore();
}

std::shared_ptr<KDecoration3::DecorationShadow> DaisyShadowFactory::get(const DaisyFrameMetrics &metrics,
                                                                        const DaisyFrameColors &colors,
                                                                        bool active,
                                                                        bool includeFrame)
{
    static QMutex mutex;
    static QHash<QString, std::weak_ptr<KDecoration3::DecorationShadow>> cache;
    const QString key = QStringLiteral("%1:%2:%3:%4:%5")
                            .arg(metrics.cornerRadius, 0, 'f', 3)
                            .arg(active ? 1 : 0)
                            .arg(includeFrame ? 1 : 0)
                            .arg(colors.outerEdge.rgba(), 8, 16, QLatin1Char('0'))
                            .arg(colors.innerHighlight.rgba(), 8, 16, QLatin1Char('0'));
    QMutexLocker locker(&mutex);
    if (const auto existing = cache.value(key).lock()) {
        return existing;
    }

    constexpr int textureSize = 72;
    constexpr qreal innerOffset = 22.0;
    constexpr qreal innerSize = 28.0;
    QImage image(textureSize, textureSize, QImage::Format_ARGB32_Premultiplied);
    image.fill(Qt::transparent);

    QPainter painter(&image);
    painter.setRenderHint(QPainter::Antialiasing);
    const QRectF inner(innerOffset, innerOffset - 1.0, innerSize, innerSize);
    for (int expansion = 14; expansion >= 1; --expansion) {
        // Preserve the native Daisy shadow recipe. Active and inactive states
        // have separate cache entries so they can diverge without per-window
        // allocations when the theme grows distinct shadow tokens.
        const QColor shadow(0, 0, 0, std::max(1, 2 + (14 - expansion)));
        painter.setPen(Qt::NoPen);
        painter.setBrush(shadow);
        painter.drawRoundedRect(DaisyFramePainter::inset(inner, -expansion),
                                metrics.cornerRadius + expansion,
                                metrics.cornerRadius + expansion);
    }

    // The compositor effect owns the visible frame for SSD and CSD windows.
    // DecorationShadow must contain only the soft shadow, never an outline.
    Q_UNUSED(includeFrame)
    painter.setCompositionMode(QPainter::CompositionMode_DestinationOut);
    painter.setBrush(Qt::black);
    painter.drawRoundedRect(inner, metrics.cornerRadius, metrics.cornerRadius);
    painter.end();

    auto shadow = std::make_shared<KDecoration3::DecorationShadow>();
    shadow->setShadow(image);
    shadow->setInnerShadowRect(QRectF(image.rect().center(), QSizeF(1, 1)));
    shadow->setPadding(QMarginsF(18, 18, 22, 22));
    cache.insert(key, shadow);
    return shadow;
}
