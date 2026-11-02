// SPDX-License-Identifier: GPL-2.0-or-later
#include <effect/effect.h>
#include <effect/effecthandler.h>
#include <effect/effectwindow.h>
#include <scene/windowitem.h>
#include <scene/outlinedborderitem.h>
#include <window.h>
#include <KConfigGroup>
#include <KSharedConfig>
#include <QDebug>
#include <QPointer>
#include <QJsonArray>
#include <QJsonDocument>
#include <QJsonObject>
#include <algorithm>
#include <cmath>
#include <map>

// Use the same scene primitives as KDecoration3::BorderOutline. No window
// redirection, private texture coordinates or duplicate shader corner masks.
class DaisyFrameEffect final : public KWin::Effect
{
    Q_OBJECT
public:
    DaisyFrameEffect()
    {
        connect(KWin::effects, &KWin::EffectsHandler::windowDeleted, this,
                [this](KWin::EffectWindow *w) { release(w); });
        reconfigure(ReconfigureAll);
        qInfo() << "DaisyFrame: initialized";
    }
    ~DaisyFrameEffect() override
    {
        while (!m_frames.empty()) release(m_frames.begin()->first);
        KWin::effects->addRepaintFull();
    }
    static bool supported() { return true; }
    QString debug(const QString &parameter) const override
    {
        if (parameter == QStringLiteral("frames")) {
            QJsonArray frames;
            for (const auto &[w, frame] : m_frames) {
                if (!frame.window) continue;
                int bands = 0;
                for (auto *child : w->windowItem()->childItems()) {
                    if (qobject_cast<KWin::OutlinedBorderItem *>(child)) ++bands;
                }
                frames.append(QJsonObject{
                    {QStringLiteral("caption"), w->caption()},
                    {QStringLiteral("radius"), frame.window->borderRadius().topLeft()},
                    {QStringLiteral("originalRadius"), frame.originalRadius.topLeft()},
                    {QStringLiteral("bands"), bands},
                    {QStringLiteral("outerWidth"), frame.outer->outline().thickness()},
                    {QStringLiteral("innerWidth"), frame.inner->outline().thickness()},
                    {QStringLiteral("width"), frame.inner->innerRect().width()},
                    {QStringLiteral("height"), frame.inner->innerRect().height()},
                });
            }
            return QString::fromUtf8(QJsonDocument(frames).toJson(QJsonDocument::Compact));
        }
        return QStringLiteral("DaisyFrame 2.0 (native scene outlines; %1 windows)").arg(m_frames.size());
    }
    bool isActive() const override { return true; }
    int requestedEffectChainPosition() const override { return 99; }
    void reconfigure(ReconfigureFlags) override
    {
        auto config = KSharedConfig::openConfig(QStringLiteral("kwinrc"));
        config->reparseConfiguration();
        m_config = KConfigGroup(config, QStringLiteral("Round-Corners"));
        KWin::effects->addRepaintFull();
    }
    void prePaintWindow(KWin::RenderView *view, KWin::EffectWindow *w,
                        KWin::WindowPrePaintData &data) override
    {
        if (eligible(w)) {
            update(w);
            // Native clipping exposes the backdrop at the corners. This must
            // be set before the scene computes opaque occlusion regions.
            data.setTranslucent();
        } else {
            release(w);
        }
        Effect::prePaintWindow(view, w, data);
    }
private:
    struct Frame {
        QPointer<KWin::Window> window;
        KWin::BorderRadius originalRadius;
        KWin::BorderRadius appliedRadius;
        std::unique_ptr<KWin::OutlinedBorderItem> outer;
        std::unique_ptr<KWin::OutlinedBorderItem> inner;
    };

    void release(KWin::EffectWindow *w)
    {
        auto it = m_frames.find(w);
        if (it == m_frames.end()) return;
        auto &frame = it->second;
        // Do not overwrite a radius changed by a decoration or another effect.
        if (frame.window && !frame.window->isDeleted() && frame.window->borderRadius() == frame.appliedRadius)
            frame.window->setBorderRadius(frame.originalRadius);
        m_frames.erase(it);
    }

    void update(KWin::EffectWindow *w)
    {
        auto *window = w->window();
        auto *item = w->windowItem();
        auto [it, inserted] = m_frames.try_emplace(w);
        auto &frame = it->second;
        if (inserted) {
            frame.window = window;
            frame.originalRadius = window->borderRadius();
            frame.appliedRadius = frame.originalRadius;
            // Siblings of the clipped content: never apply its corner mask a
            // second time to either outline. Window opacity, visibility and
            // animation transforms still apply to the whole WindowItem.
            frame.outer = std::make_unique<KWin::OutlinedBorderItem>(KWin::RectF(), KWin::BorderOutline(), item);
            frame.inner = std::make_unique<KWin::OutlinedBorderItem>(KWin::RectF(), KWin::BorderOutline(), item);
            frame.outer->setZ(1);
            frame.inner->setZ(2);
        } else if (window->borderRadius() != frame.appliedRadius) {
            frame.originalRadius = window->borderRadius();
        }
        const bool active = KWin::effects->activeWindow() == w;
        const bool square = window->maximizeMode() != KWin::MaximizeRestore
            || window->quickTileMode() != KWin::QuickTileMode();
        const qreal radius = square ? 0.0 : number(active ? "Size" : "InactiveCornerRadius", 16, 256);
        frame.appliedRadius = KWin::BorderRadius(radius);
        window->setBorderRadius(frame.appliedRadius);
        // Native outlines expand outwards from their inner rect. Nest the
        // second rect outside the first band rather than overlaying two rings
        // on client pixels. Tile/maximize has no outside frame to avoid seams
        // over neighbouring windows and panels.
        const qreal outer = square ? 0.0 : number(active ? "OutlineThickness" : "InactiveOutlineThickness", 1, 16);
        const qreal inner = square ? 0.0 : number(active ? "SecondOutlineThickness" : "InactiveSecondOutlineThickness", 1, 16);
        const KWin::RectF rect(QPointF(0, 0), w->frameGeometry().size());
        frame.inner->setOutline(KWin::BorderOutline(inner,
            color(active ? "SecondOutlineColor" : "InactiveSecondOutlineColor",
                active ? "ActiveSecondOutlineAlpha" : "InactiveSecondOutlineAlpha", QColor(255, 255, 255, 26)),
            KWin::BorderRadius(radius)));
        frame.inner->setInnerRect(rect);
        frame.inner->setVisible(inner > 0);
        frame.outer->setOutline(KWin::BorderOutline(outer,
            color(active ? "OutlineColor" : "InactiveOutlineColor",
                active ? "ActiveOutlineAlpha" : "InactiveOutlineAlpha", QColor(0, 0, 0, 153)),
            KWin::BorderRadius(radius + inner)));
        frame.outer->setInnerRect(rect.adjusted(-inner, -inner, inner, inner));
        frame.outer->setVisible(outer > 0);
        if (!m_painted) {
            qInfo() << "DaisyFrame: painting window frames (native scene outlines)";
            m_painted = true;
        }
    }

    bool eligible(KWin::EffectWindow *w) const
    {
        if (!w->window() || !w->windowItem() || !w->screen() || w->isDeleted()
            || w->isFullScreen() || w->isLockScreen() || w->window()->isInternal()
            || w->window()->inherits("KWin::LayerShellV1Window")
            || !((w->isNormalWindow() && m_config.readEntry("IncludeNormalWindows", true))
                || (w->isDialog() && m_config.readEntry("IncludeDialogs", true)))) return false;
        const auto rect = w->frameGeometry();
        if (rect.width() < 4 || rect.height() < 4) return false;
        const auto exclusions = m_config.readEntry("Exclusions", QStringList{
            QStringLiteral("DaisyOS Lock Screen"), QStringLiteral("DaisyOS Lock Wallpaper")});
        for (const auto &exclusion : exclusions) {
            if (!exclusion.trimmed().isEmpty()
                && (w->windowClass().contains(exclusion, Qt::CaseInsensitive)
                    || w->caption().contains(exclusion, Qt::CaseInsensitive))) return false;
        }
        return true;
    }
    qreal number(const char *key, qreal fallback, qreal maximum) const
    {
        const qreal value = m_config.readEntry(key, fallback);
        return std::isfinite(value) ? std::clamp(value, 0.0, maximum) : fallback;
    }
    QColor color(const char *rgb, const char *alpha, QColor fallback) const
    {
        QColor result(m_config.readEntry(rgb, fallback.name()));
        if (!result.isValid()) result = fallback;
        result.setAlpha(std::clamp(m_config.readEntry(alpha, fallback.alpha()), 0, 255));
        return result;
    }
    std::map<KWin::EffectWindow *, Frame> m_frames;
    KConfigGroup m_config;
    bool m_painted = false;
};
KWIN_EFFECT_FACTORY_SUPPORTED(DaisyFrameEffect, "metadata.json", return DaisyFrameEffect::supported();)
#include "frameeffect.moc"
