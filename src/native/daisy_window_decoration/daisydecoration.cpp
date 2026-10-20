#include "daisydecoration.h"

#include <KConfigGroup>
#include <KDecoration3/DecoratedWindow>
#include <KDecoration3/DecorationButtonGroup>
#include <KDecoration3/DecorationSettings>
#include <KPluginFactory>
#include <KSharedConfig>

#include <QDir>
#include <QFileInfo>
#include <QFontMetrics>
#include <QPainter>
#include <QPainterPath>
#include <QStandardPaths>
#include <QTimer>

#include <algorithm>
#include <cmath>

K_PLUGIN_FACTORY_WITH_JSON(DaisyDecorationFactory,
                           "daisydecoration.json",
                           registerPlugin<DaisyDecoration>();)

namespace
{
constexpr qreal kMinimumCaptionReservation = 72.0;
constexpr qreal kMinimumControlWidth = 28.0;

QColor parseColor(const KConfigGroup &group, const char *key, const QColor &fallback)
{
    const auto value = group.readEntry(key, QString());
    const QColor parsed(value);
    return parsed.isValid() ? parsed : fallback;
}

void drawGlyph(QPainter *painter, KDecoration3::DecorationButtonType type, const QRectF &bounds, const QColor &color, bool checked, qreal glyphSize)
{
    painter->save();
    painter->setRenderHint(QPainter::Antialiasing);
    painter->setPen(QPen(color, 1.45, Qt::SolidLine, Qt::RoundCap, Qt::RoundJoin));

    const QPointF center = bounds.center();
    // size is the half extent used by the line and rectangle primitives, so
    // the rendered glyph's bounding box is exactly the requested DIP size.
    const qreal size = std::min(std::min(bounds.width(), bounds.height()), glyphSize) / 2.0;
    switch (type) {
    case KDecoration3::DecorationButtonType::Minimize:
        painter->drawLine(QPointF(center.x() - size, center.y()), QPointF(center.x() + size, center.y()));
        break;
    case KDecoration3::DecorationButtonType::Maximize: {
        const QRectF glyph(center.x() - size, center.y() - size, size * 2.0, size * 2.0);
        painter->drawRect(glyph);
        if (checked) {
            painter->drawLine(glyph.topLeft() + QPointF(2.0, -2.0), glyph.topRight() + QPointF(2.0, -2.0));
            painter->drawLine(glyph.topRight() + QPointF(2.0, -2.0), glyph.bottomRight() + QPointF(2.0, -2.0));
        }
        break;
    }
    case KDecoration3::DecorationButtonType::Close:
        painter->drawLine(QPointF(center.x() - size, center.y() - size), QPointF(center.x() + size, center.y() + size));
        painter->drawLine(QPointF(center.x() + size, center.y() - size), QPointF(center.x() - size, center.y() + size));
        break;
    default:
        break;
    }
    painter->restore();
}
}

DaisyCaptionButton::DaisyCaptionButton(KDecoration3::DecorationButtonType type, DaisyDecoration *decoration, QObject *parent)
    : KDecoration3::DecorationButton(type, decoration, parent)
    , m_daisyDecoration(decoration)
{
    connect(this, &KDecoration3::DecorationButton::hoveredChanged, this, qOverload<>(&KDecoration3::DecorationButton::update));
    connect(this, &KDecoration3::DecorationButton::pressedChanged, this, qOverload<>(&KDecoration3::DecorationButton::update));
    connect(this, &KDecoration3::DecorationButton::checkedChanged, this, qOverload<>(&KDecoration3::DecorationButton::update));
    connect(this, &KDecoration3::DecorationButton::enabledChanged, this, qOverload<>(&KDecoration3::DecorationButton::update));
}

void DaisyCaptionButton::paint(QPainter *painter, const QRectF &repaintRegion)
{
    Q_UNUSED(repaintRegion)
    if (!m_daisyDecoration || !isVisible()) {
        return;
    }

    const QRectF bounds = geometry();
    const auto background = m_daisyDecoration->buttonBackground(type(), isHovered(), isPressed());
    if (background.alpha() > 0) {
        m_daisyDecoration->paintCaptionButtonBackground(painter, type(), bounds, background);
    }

    const QColor glyph = isEnabled()
        ? ((type() == KDecoration3::DecorationButtonType::Close && (isHovered() || isPressed()))
            ? m_daisyDecoration->onActionForeground()
            : m_daisyDecoration->foreground())
        : m_daisyDecoration->disabledForeground();
    drawGlyph(painter, type(), bounds, glyph, isChecked(), m_daisyDecoration->captionGlyphSize());
}

DaisyDecoration::DaisyDecoration(QObject *parent, const QVariantList &args)
    : KDecoration3::Decoration(parent, args)
{
}

bool DaisyDecoration::init()
{
    if (!isShadowOnly()) {
        m_captionButtons = new KDecoration3::DecorationButtonGroup(this);
        for (const auto type : {KDecoration3::DecorationButtonType::Minimize,
                                KDecoration3::DecorationButtonType::Maximize,
                                KDecoration3::DecorationButtonType::Close}) {
            m_captionButtons->addButton(new DaisyCaptionButton(type, this, m_captionButtons));
        }
    }

    connect(window(), &KDecoration3::DecoratedWindow::widthChanged, this, &DaisyDecoration::updateLayout);
    connect(window(), &KDecoration3::DecoratedWindow::maximizedChanged, this, &DaisyDecoration::updateLayout);
    connect(window(), &KDecoration3::DecoratedWindow::activeChanged, this, &DaisyDecoration::updateActiveState);
    connect(window(), &KDecoration3::DecoratedWindow::captionChanged, this, qOverload<>(&DaisyDecoration::update));
    connect(window(), &KDecoration3::DecoratedWindow::nextScaleChanged, this, &DaisyDecoration::updateLayout);
    connect(window(), &KDecoration3::DecoratedWindow::closeableChanged, this, &DaisyDecoration::updateLayout);
    connect(window(), &KDecoration3::DecoratedWindow::maximizeableChanged, this, &DaisyDecoration::updateLayout);
    connect(window(), &KDecoration3::DecoratedWindow::minimizeableChanged, this, &DaisyDecoration::updateLayout);
    connect(settings().get(), &KDecoration3::DecorationSettings::reconfigured, this, &DaisyDecoration::reloadTheme);

    watchThemeFile();
    reloadTheme();
    return true;
}

void DaisyDecoration::watchThemeFile()
{
    const auto configDirectory = QStandardPaths::writableLocation(QStandardPaths::GenericConfigLocation);
    const auto configPath = configDirectory + QStringLiteral("/daisyos-window-decorationrc");
    m_themeWatcher = std::make_unique<QFileSystemWatcher>();
    m_themeWatcher->addPath(configDirectory);
    if (QFileInfo::exists(configPath)) {
        m_themeWatcher->addPath(configPath);
    }

    const auto reload = [this, configPath] {
        if (!m_themeWatcher->files().contains(configPath) && QFileInfo::exists(configPath)) {
            m_themeWatcher->addPath(configPath);
        }
        QTimer::singleShot(0, this, &DaisyDecoration::reloadTheme);
    };
    connect(m_themeWatcher.get(), &QFileSystemWatcher::fileChanged, this, reload);
    connect(m_themeWatcher.get(), &QFileSystemWatcher::directoryChanged, this, reload);
}

void DaisyDecoration::reloadTheme()
{
    const auto config = KSharedConfig::openConfig(QStringLiteral("daisyos-window-decorationrc"));
    config->reparseConfiguration();
    const KConfigGroup group(config, QStringLiteral("Decoration"));

    m_palette.surfaceBase = parseColor(group, "SurfaceBase", QColor(QStringLiteral("#FF121214")));
    m_palette.surfaceRaised = parseColor(group, "SurfaceRaised", QColor(QStringLiteral("#FF1A1A1D")));
    // Decorations deliberately use solid materials, including with an older
    // shell theme export that still contains translucent panel colors.
    m_palette.surfaceBase.setAlpha(255);
    m_palette.surfaceRaised.setAlpha(255);
    m_palette.outerEdge = parseColor(group, "ShellFrameOuterEdge", QColor(QStringLiteral("#99000000")));
    m_palette.innerHighlight = parseColor(group, "ShellFrameInnerHighlight", QColor(QStringLiteral("#26FFFFFF")));
    m_palette.contentPrimary = parseColor(group, "ContentPrimary", QColor(QStringLiteral("#FFF5F5F7")));
    m_palette.contentDisabled = parseColor(group, "ContentDisabled", QColor(QStringLiteral("#FF98989F")));
    m_palette.stateHover = parseColor(group, "StateHover", QColor(QStringLiteral("#14FFFFFF")));
    m_palette.statePressed = parseColor(group, "StatePressed", QColor(QStringLiteral("#24FFFFFF")));
    m_palette.danger = parseColor(group, "StatusDanger", QColor(QStringLiteral("#FFFF383C")));
    m_palette.onAction = parseColor(group, "ContentOnAction", QColor(QStringLiteral("#FFFFFFFF")));
    m_titleBarHeight = std::clamp(group.readEntry("TitleBarHeight", 32.0), 28.0, 48.0);
    m_captionButtonWidth = std::clamp(group.readEntry("CaptionButtonWidth", 46.0), 32.0, 56.0);
    m_captionButtonHeight = std::clamp(group.readEntry("CaptionButtonHeight", 32.0), 28.0, 48.0);
    m_captionGlyphSize = std::clamp(group.readEntry("CaptionGlyphSize", 10.0), 8.0, 16.0);
    m_titleFontFamily = group.readEntry("TitleFontFamily", QStringLiteral("Atkinson Hyperlegible"));
    m_titleFontPixels = std::clamp(group.readEntry("TitleFontPixels", 13), 10, 24);
    m_frameMetrics.cornerRadius = std::clamp(group.readEntry("CornerRadius", 12.0), 0.0, 24.0);

    updateLayout();
    update();
}

bool DaisyDecoration::isShadowOnly() const
{
#if defined(DAISY_KDECORATION_HAS_SHADOW_STYLE)
    return style() == KDecoration3::Style::Shadow;
#else
    return false;
#endif
}

void DaisyDecoration::updateActiveState()
{
    setShadow(window()->isMaximized()
        ? nullptr
        : DaisyShadowFactory::get(m_frameMetrics,
                                  frameColors(),
                                  window()->isActive(),
                                  false));
    update();
}

void DaisyDecoration::updateLayout()
{
    updateFrameGeometry();
    updateButtonsGeometry();
    updateActiveState();
    update();
}

void DaisyDecoration::updateFrameGeometry()
{
    const bool maximized = window()->isMaximized();
    if (isShadowOnly()) {
        setBorders(QMarginsF(0.0, 0.0, 0.0, 0.0));
        setTitleBar(QRectF());
        setResizeOnlyBorders(window()->isResizeable() && !maximized
            ? QMarginsF(m_frameMetrics.resizeOnlyBorder,
                        m_frameMetrics.resizeOnlyBorder,
                        m_frameMetrics.resizeOnlyBorder,
                        m_frameMetrics.resizeOnlyBorder)
            : QMarginsF());
        // Corner clipping and both outline strokes are owned by the compositor
        // effect for every window type. The decoration supplies only shadow
        // and resize semantics for a client-side-decorated surface.
        setBorderRadius(KDecoration3::BorderRadius());
        setBorderOutline(KDecoration3::BorderOutline());
        setOpaque(false);
        return;
    }

    // Keep the server-side titlebar, but never allocate or paint side/bottom
    // frame pixels. The compositor effect is the single visual frame owner.
    setBorders(QMarginsF(0.0, m_titleBarHeight, 0.0, 0.0));
    setResizeOnlyBorders(window()->isResizeable() && !maximized
        ? QMarginsF(m_frameMetrics.resizeOnlyBorder,
                    m_frameMetrics.resizeOnlyBorder,
                    m_frameMetrics.resizeOnlyBorder,
                    m_frameMetrics.resizeOnlyBorder)
        : QMarginsF());
    setTitleBar(QRectF(0, 0, size().width(), m_titleBarHeight));
    setBorderRadius(KDecoration3::BorderRadius());
    setBorderOutline(KDecoration3::BorderOutline());
    setOpaque(maximized);
}

void DaisyDecoration::updateButtonsGeometry()
{
    if (!m_captionButtons) {
        return;
    }

    const qreal windowWidth = std::max<qreal>(0.0, size().width());
    const int fullButtonSlots = static_cast<int>(std::floor(
        std::max<qreal>(0.0, windowWidth - kMinimumCaptionReservation) / std::max<qreal>(1.0, m_captionButtonWidth)));
    const bool canShowOne = windowWidth >= kMinimumControlWidth;

    // Preserve close whenever possible, then restore/maximize, then minimize.
    // This deliberately removes visual controls before allowing anything to
    // overflow a narrow server-decorated client.
    const bool showClose = window()->isCloseable() && (fullButtonSlots >= 1 || canShowOne);
    const bool showMaximize = window()->isMaximizeable() && fullButtonSlots >= 2;
    const bool showMinimize = window()->isMinimizeable() && fullButtonSlots >= 3;
    const int visibleCount = (showMinimize ? 1 : 0) + (showMaximize ? 1 : 0) + (showClose ? 1 : 0);
    const qreal buttonWidth = visibleCount > 0
        ? std::min(m_captionButtonWidth, windowWidth / visibleCount)
        : 0.0;
    const qreal buttonHeight = std::min(m_captionButtonHeight, m_titleBarHeight);

    qreal x = 0.0;
    for (auto *button : m_captionButtons->buttons()) {
        const bool visible = (button->type() == KDecoration3::DecorationButtonType::Minimize && showMinimize)
            || (button->type() == KDecoration3::DecorationButtonType::Maximize && showMaximize)
            || (button->type() == KDecoration3::DecorationButtonType::Close && showClose);
        button->setVisible(visible);
        if (visible) {
            button->setGeometry(QRectF(x, 0.0, buttonWidth, buttonHeight));
            x += buttonWidth;
        }
    }
    m_captionButtons->setSpacing(0.0);
    m_visibleCaptionButtonsWidth = x;
    m_captionButtons->setPos(QPointF(std::max<qreal>(0.0, windowWidth - m_visibleCaptionButtonsWidth), 0.0));
}

QColor DaisyDecoration::titleSurface() const
{
    if (window()->isActive()) {
        return m_palette.surfaceBase;
    }

    QColor inactive = m_palette.surfaceRaised;
    inactive.setAlpha(255);
    return inactive;
}

DaisyFrameColors DaisyDecoration::frameColors() const
{
    return DaisyFrameColors{
        .surfaceBase = m_palette.surfaceBase,
        .outerEdge = m_palette.outerEdge,
        .innerHighlight = m_palette.innerHighlight,
    };
}

QColor DaisyDecoration::foreground() const
{
    return window()->isActive() ? m_palette.contentPrimary : m_palette.contentDisabled;
}

QColor DaisyDecoration::onActionForeground() const
{
    return m_palette.onAction;
}

QColor DaisyDecoration::disabledForeground() const
{
    QColor disabled = m_palette.contentDisabled;
    disabled.setAlpha(std::min(disabled.alpha(), 150));
    return disabled;
}

QColor DaisyDecoration::buttonBackground(KDecoration3::DecorationButtonType type, bool hovered, bool pressed) const
{
    if (!hovered && !pressed) {
        return Qt::transparent;
    }

    if (type == KDecoration3::DecorationButtonType::Close) {
        return m_palette.danger;
    }
    return pressed ? m_palette.statePressed : m_palette.stateHover;
}

qreal DaisyDecoration::captionGlyphSize() const
{
    return m_captionGlyphSize;
}

void DaisyDecoration::paintCaptionButtonBackground(QPainter *painter,
                                                    KDecoration3::DecorationButtonType type,
                                                    const QRectF &bounds,
                                                    const QColor &background) const
{
    // The close control reaches the title bar's right edge. Clip its hover
    // material to the inner rounded surface so the outer frame/highlight stay
    // visible instead of being painted over by the danger color.
    if (type != KDecoration3::DecorationButtonType::Close || window()->isMaximized()) {
        painter->fillRect(bounds, background);
        return;
    }

    const qreal innerRadius = std::max<qreal>(0.0, m_frameMetrics.cornerRadius - m_frameMetrics.thickness);
    const QRectF titleClip(m_frameMetrics.thickness,
                           m_frameMetrics.thickness,
                           std::max<qreal>(0.0, size().width() - (m_frameMetrics.thickness * 2.0)),
                           std::max<qreal>(0.0, m_titleBarHeight - m_frameMetrics.thickness));
    if (titleClip.isEmpty()) {
        return;
    }

    QPainterPath clip;
    clip.moveTo(titleClip.left(), titleClip.bottom());
    clip.lineTo(titleClip.right(), titleClip.bottom());
    clip.lineTo(titleClip.right(), titleClip.top() + innerRadius);
    clip.quadTo(titleClip.right(), titleClip.top(), titleClip.right() - innerRadius, titleClip.top());
    clip.lineTo(titleClip.left(), titleClip.top());
    clip.closeSubpath();

    painter->save();
    painter->setClipPath(clip);
    painter->fillRect(bounds, background);
    painter->restore();
}

void DaisyDecoration::paint(QPainter *painter, const QRectF &repaintRegion)
{
    Q_UNUSED(repaintRegion)
    if (isShadowOnly()) {
        // Shadow-only visuals are compositor primitives (BorderOutline,
        // BorderRadius and DecorationShadow), not a second decoration bitmap.
        return;
    }
    painter->save();
    painter->setRenderHint(QPainter::Antialiasing);
    painter->setPen(Qt::NoPen);

    const QRectF titleRect(0, 0, size().width(), m_titleBarHeight);
    painter->setClipRect(titleRect);
    painter->setBrush(titleSurface());
    painter->drawRect(titleRect);
    painter->setClipping(false);

    // Keep every title-bar primitive inside the actual decoration width. This
    // matters for legacy/tool windows that KWin allows to be very narrow.
    painter->setClipRect(QRectF(0.0, 0.0, std::max<qreal>(0.0, size().width()), m_titleBarHeight));
    const qreal captionRight = std::max<qreal>(0.0, size().width() - m_visibleCaptionButtonsWidth - 10.0);

    // Center the caption against the full window, not merely inside the
    // asymmetric space beside the caption buttons. Mirror the occupied side
    // so the text remains geometrically centered while still being elided
    // before it can overlap the controls.
    const qreal captionInset = std::max<qreal>(10.0, size().width() - captionRight);
    const QRectF captionRect(captionInset,
                             0.0,
                             std::max<qreal>(0.0, size().width() - (captionInset * 2.0)),
                             m_titleBarHeight);
    if (captionRect.width() > 0.0) {
        painter->setPen(foreground());
        QFont font = painter->font();
        font.setFamily(m_titleFontFamily);
        font.setPixelSize(m_titleFontPixels);
        font.setWeight(QFont::Medium);
        painter->setFont(font);
        const auto caption = painter->fontMetrics().elidedText(window()->caption(), Qt::ElideRight, static_cast<int>(captionRect.width()));
        painter->drawText(captionRect, Qt::AlignCenter | Qt::TextSingleLine, caption);
    }

    if (m_captionButtons) {
        m_captionButtons->paint(painter, rect());
    }
    painter->setClipping(false);
    painter->restore();
}

#include "daisydecoration.moc"
