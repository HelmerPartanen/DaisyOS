#include "daisyframestyle.h"

#include <QGuiApplication>
#include <QImage>
#include <QPainter>
#include <QPainterPath>
#include <cmath>
#include <cstdlib>
#include <iostream>

static void require(bool condition, const char *message)
{
    if (!condition) {
        std::cerr << message << '\n';
        std::exit(1);
    }
}

int main(int argc, char **argv)
{
    QGuiApplication app(argc, argv);
    for (const qreal scale : {1.0, 1.25, 1.5, 2.0}) {
        for (const qreal width : {8.0, 48.0, 240.0}) {
            for (const qreal configuredRadius : {0.0, 12.0, 16.0, 24.0, 256.0}) {
                for (const qreal inset : {0.0, 2.0}) {
                    const QRectF frame(inset, inset, width - 2 * inset, 160 - 2 * inset);
                    const qreal radius = std::min(configuredRadius, std::min(frame.width(), frame.height()) / 2);
                    QImage image(QSize(qCeil(width * scale), qCeil(160 * scale)), QImage::Format_ARGB32_Premultiplied);
                    image.setDevicePixelRatio(scale);
                    image.fill(Qt::transparent);
                    QPainter painter(&image);
                    painter.setRenderHint(QPainter::Antialiasing);
                    // Preserve a caller's damage clip as well as the titlebar shape.
                    painter.setClipRect(QRectF(0, 0, width, 32));
                    painter.setClipPath(DaisyFramePainter::titleBarClip(frame, 32 - inset, radius), Qt::IntersectClip);
                    painter.fillRect(QRectF(-100, -100, width + 200, 400), Qt::black);
                    // A danger hover and an oversized caption must obey the same clip.
                    painter.fillRect(QRectF(width - 46, 0, 46, 48), Qt::red);
                    painter.setPen(Qt::white);
                    painter.drawText(QRectF(-20, -10, width + 40, 60), Qt::AlignCenter, QStringLiteral("Wide caption XXXXX"));
                    painter.end();
                    for (int y = 0; y < image.height(); ++y) {
                        for (int x = 0; x < image.width(); ++x) {
                            const QPointF p((x + .5) / scale, (y + .5) / scale);
                            const qreal guard = 1.5 / scale;
                            const bool outsideFrame = p.x() < frame.left() - guard || p.x() > frame.right() + guard
                                || p.y() < frame.top() - guard || p.y() > 32 + guard;
                            const qreal dx = p.x() < frame.center().x() ? p.x() - frame.left() : frame.right() - p.x();
                            const qreal dy = p.y() - frame.top();
                            const bool outsideCorner = dx < radius && dy < radius
                                && std::hypot(radius - dx, radius - dy) > radius + guard;
                            if (outsideFrame || outsideCorner)
                                require(qAlpha(image.pixel(x, y)) == 0, "Titlebar paint escaped its rounded clip");
                        }
                    }
                    if (radius == 0 && inset == 0)
                        require(qAlpha(image.pixel(0, 0)) == 255, "Square/maximized titlebar lost its corner");
                    if (width == 240 && radius == 16 && inset == 0) {
                        require(qAlpha(image.pixel(0, 0)) == 0, "Rounded corner is opaque");
                        require(qAlpha(image.pixel(qFloor(120 * scale), qFloor(16 * scale))) == 255, "Caption surface has a hole");
                    }
                }
            }
        }
    }
    require(DaisyFramePainter::titleBarClip(QRectF(), 32, 16).isEmpty(), "Empty frame must not paint");
    std::cout << "PASS: titlebar clipping, both corners, hover/text overflow, square state, narrow frames, 100/125/150/200% scale\n";
}
