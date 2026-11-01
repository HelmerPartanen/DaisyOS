// SPDX-License-Identifier: GPL-2.0-or-later
#include <effect/offscreeneffect.h>
#include <effect/effecthandler.h>
#include <effect/effectwindow.h>
#include <core/output.h>
#include <core/pixelgrid.h>
#include <opengl/glutils.h>
#include <window.h>
#include <KConfigGroup>
#include <KSharedConfig>
#include <QDebug>
#include <QSet>
#include <algorithm>
#include <cmath>

class DaisyFrameEffect final : public KWin::OffscreenEffect
{
    Q_OBJECT
public:
    DaisyFrameEffect()
    {
        m_shader = KWin::ShaderManager::instance()->generateShaderFromFile(
            KWin::ShaderTrait::MapTexture, {}, QStringLiteral(":/daisy/frame/frame.frag"));
        if (!m_shader) {
            qCritical() << "DaisyFrame: shader compilation failed; leaving windows untouched";
        }
        connect(KWin::effects, &KWin::EffectsHandler::windowDeleted, this,
                [this](KWin::EffectWindow *w) { m_redirected.remove(w); });
        reconfigure(ReconfigureAll);
        qInfo() << "DaisyFrame: initialized";
    }
    ~DaisyFrameEffect() override
    {
        // OffscreenEffect stores raw shader pointers. Detach before destroying it.
        for (auto *w : std::as_const(m_redirected)) {
            unredirect(w);
        }
        KWin::effects->addRepaintFull();
    }
    static bool supported() { return KWin::OffscreenEffect::supported(); }
    QString debug(const QString &) const override { return QStringLiteral("DaisyFrame 1.0 (embedded shader)"); }
    bool isActive() const override { return bool(m_shader); }
    int requestedEffectChainPosition() const override { return 99; }
    // Keep scanout conservative while any windows use offscreen rendering.
    bool blocksDirectScanout() const override { return !m_redirected.isEmpty(); }
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
            // Rounded clipping and semi-transparent CSD frames must repaint
            // what is behind them, including the first frame after a resize.
            data.setTranslucent();
        }
        OffscreenEffect::prePaintWindow(view, w, data);
    }
    bool drawWindow(const KWin::RenderTarget &target, const KWin::RenderViewport &viewport,
                    KWin::EffectWindow *w, int mask, const KWin::Region &region,
                    KWin::WindowPaintData &data) override
    {
        if (!eligible(w)) {
            if (m_redirected.remove(w)) unredirect(w);
            return OffscreenEffect::drawWindow(target, viewport, w, mask, region, data);
        }
        redirect(w);
        m_redirected.insert(w);
        setShader(w, m_shader.get());
        const double scale = w->screen()->scale();
        const auto frame = KWin::snapToPixels(w->frameGeometry(), scale) * scale;
        const auto expanded = KWin::snapToPixels(w->expandedGeometry(), scale) * scale;
        const bool active = KWin::effects->activeWindow() == w;
        const bool square = w->window()->maximizeMode() != KWin::MaximizeRestore
            || w->window()->quickTileMode() != KWin::QuickTileMode();
        const float radius = square ? 0.0f : number(active ? "Size" : "InactiveCornerRadius", 16, 256) * scale;
        const float outer = number(active ? "OutlineThickness" : "InactiveOutlineThickness", 1, 16) * scale;
        const float inner = number(active ? "SecondOutlineThickness" : "InactiveSecondOutlineThickness", 1, 16) * scale;
        KWin::ShaderManager::instance()->pushShader(m_shader.get());
        m_shader->setUniform("frameSize", QVector2D(frame.width(), frame.height()));
        m_shader->setUniform("textureSize", QVector2D(expanded.width(), expanded.height()));
        m_shader->setUniform("frameOffset", QVector2D(frame.topLeft() - expanded.topLeft()));
        m_shader->setUniform("radius", radius);
        m_shader->setUniform("widths", QVector2D(outer, inner));
        m_shader->setUniform("outerColor", color(active ? "OutlineColor" : "InactiveOutlineColor",
            active ? "ActiveOutlineAlpha" : "InactiveOutlineAlpha", QColor(0, 0, 0, 153)));
        m_shader->setUniform("innerColor", color(active ? "SecondOutlineColor" : "InactiveSecondOutlineColor",
            active ? "ActiveSecondOutlineAlpha" : "InactiveSecondOutlineAlpha", QColor(255, 255, 255, 26)));
        if (!m_painted) {
            qInfo() << "DaisyFrame: painting window frames";
            m_painted = true;
        }
        const bool result = OffscreenEffect::drawWindow(target, viewport, w, mask, region, data);
        KWin::ShaderManager::instance()->popShader();
        return result;
    }
private:
    bool eligible(KWin::EffectWindow *w) const
    {
        if (!m_shader || !w->window() || !w->screen() || w->isDeleted()
            || w->isFullScreen() || w->isLockScreen() || w->window()->isInternal()
            || w->window()->inherits("KWin::LayerShellV1Window")
            || !((w->isNormalWindow() && m_config.readEntry("IncludeNormalWindows", true))
                || (w->isDialog() && m_config.readEntry("IncludeDialogs", true)))) return false;
        const auto frame = w->frameGeometry();
        if (frame.width() < 4 || frame.height() < 4) return false;
        const auto exclusions = m_config.readEntry("Exclusions", QStringList{
            QStringLiteral("DaisyOS Lock Screen"), QStringLiteral("DaisyOS Lock Wallpaper")});
        for (const auto &exclusion : exclusions) {
            if (!exclusion.trimmed().isEmpty()
                && (w->windowClass().contains(exclusion, Qt::CaseInsensitive)
                    || w->caption().contains(exclusion, Qt::CaseInsensitive))) return false;
        }
        return true;
    }
    float number(const char *key, float fallback, float maximum) const
    {
        const float value = m_config.readEntry(key, fallback);
        return std::isfinite(value) ? std::clamp(value, 0.0f, maximum) : fallback;
    }
    QColor color(const char *rgb, const char *alpha, QColor fallback) const
    {
        QColor result(m_config.readEntry(rgb, fallback.name()));
        if (!result.isValid()) result = fallback;
        result.setAlpha(std::clamp(m_config.readEntry(alpha, fallback.alpha()), 0, 255));
        return result;
    }
    std::unique_ptr<KWin::GLShader> m_shader;
    QSet<KWin::EffectWindow *> m_redirected;
    KConfigGroup m_config;
    bool m_painted = false;
};
KWIN_EFFECT_FACTORY_SUPPORTED(DaisyFrameEffect, "metadata.json", return DaisyFrameEffect::supported();)
#include "frameeffect.moc"
