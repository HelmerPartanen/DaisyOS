// SPDX-License-Identifier: MIT
"use strict";

class DaisyWindowMotion {
    constructor() {
        this.windows = new Set();
        this.loadConfig();
        effect.configChanged.connect(() => this.loadConfig());
        effects.windowAdded.connect(w => { this.manage(w); this.presence(w, true); });
        effects.windowClosed.connect(w => {
            if (!w.visible || w.skipsCloseAnimation) this.stop(w);
            else this.presence(w, false, Effect.WindowClosedGrabRole);
            this.windows.delete(w);
        });
        effect.animationEnded.connect((w, id) => {
            if (w.daisyMotion && w.daisyMotion.indexOf(id) >= 0) {
                w.daisyMotion = w.daisyMotion.filter(value => value !== id);
                if (!w.daisyMotion.length) {
                    delete w.daisyMotion;
                    delete w.daisyMotionKind;
                    delete w.daisyMotionShowing;
                }
            }
        });
        effects.windowDeleted.connect(w => this.windows.delete(w));
        effects.windowDataChanged.connect((w, role) => {
            if ([Effect.WindowAddedGrabRole, Effect.WindowClosedGrabRole,
                 Effect.WindowMinimizedGrabRole, Effect.WindowUnminimizedGrabRole].indexOf(role) >= 0
                && effect.isGrabbed(w, role)) this.stop(w);
        });
        effects.hasActiveFullScreenEffectChanged.connect(() => {
            if (effects.hasActiveFullScreenEffect) this.windows.forEach(w => this.stop(w));
        });
        effects.stackingOrder.forEach(w => this.manage(w));
    }
    loadConfig() {
        this.reduced = effect.readConfig("ReduceMotion", false);
        if (this.reduced) this.windows.forEach(w => this.stop(w));
    }
    eligible(w) {
        return w.managed && (w.normalWindow || w.dialog) && !w.popupWindow
            && !w.lockScreen && !w.outline && !w.fullScreen
            && !!w.caption && !/^DaisyOS Lock/.test(w.caption);
    }
    allowed(w, role) {
        return this.eligible(w) && !w.move && !w.resize && !this.reduced && effects.animationTimeFactor > 0
            && !effects.hasActiveFullScreenEffect && !effect.isGrabbed(w, role);
    }
    stop(w) {
        if (w.daisyMotion) cancel(w.daisyMotion);
        delete w.daisyMotion;
        delete w.daisyMotionKind;
        delete w.daisyMotionShowing;
        delete w.daisyOldGeometry;
        delete w.daisyGeometrySource;
    }
    transition(w, kind, showing, from, to, milliseconds, curve) {
        const duration = animationTime(milliseconds);
        // KWin retargets from the current painted value. Cancelling first
        // discards the active transform and exposes unanimated geometry for
        // the first minimize/close frame.
        if (w.daisyMotion && w.daisyMotion.length === 3) {
            const ids = w.daisyMotion;
            const scale = retarget(ids[0], {value1: to.sx, value2: to.sy}, duration);
            const position = retarget(ids[1], {value1: to.x, value2: to.y}, duration);
            const opacity = retarget(ids[2], to.opacity, duration);
            if (scale && position && opacity) {
                w.daisyMotionKind = kind;
                w.daisyMotionShowing = showing;
                return;
            }
        }
        this.stop(w);
        w.daisyMotionKind = kind;
        w.daisyMotionShowing = showing;
        w.daisyMotion = animate({ window: w, duration: duration, curve: curve,
            // A running opening/minimize may be retargeted to a close. Keep
            // the deleted surface available until that same timeline ends.
            keepAlive: true,
            animations: [
                {type: Effect.Scale, from: {value1: from.sx, value2: from.sy}, to: {value1: to.sx, value2: to.sy}},
                {type: Effect.Translation, from: {value1: from.x, value2: from.y}, to: {value1: to.x, value2: to.y}},
                {type: Effect.Opacity, from: from.opacity, to: to.opacity}
            ] });
    }
    presence(w, showing, role) {
        role = role || Effect.WindowAddedGrabRole;
        if (!this.allowed(w, role) || (showing && !w.visible)) { this.stop(w); return; }
        const fileWindow = /(^|[\s.])(?:org\.daisyos\.)?files(?:[\s.]|$)/i.test(String(w.windowClass || w.desktopFileName || ""));
        const normal = {sx: 1, sy: 1, x: 0, y: 0, opacity: 1};
        const hidden = {sx: fileWindow ? 1 : 0.96, sy: fileWindow ? 1 : 0.96, x: 0, y: 0, opacity: 0};
        // Closing fades in place. A center-scale exit moved every edge before
        // dismissal, which looked like the application first changed position.
        const closed = {sx: 1, sy: 1, x: 0, y: 0, opacity: 0};
        this.transition(w, "presence", showing, showing ? hidden : normal, showing ? normal : closed,
            fileWindow ? (showing ? 120 : 100) : (showing ? 180 : 140),
            showing ? QEasingCurve.OutCubic : QEasingCurve.InCubic);
    }
    minimize(w) {
        const showing = !w.minimized;
        const role = showing ? Effect.WindowUnminimizedGrabRole : Effect.WindowMinimizedGrabRole;
        if (!this.allowed(w, role)) { this.stop(w); return; }
        const g = w.geometry, icon = w.iconGeometry;
        const target = icon && icon.width > 0 && icon.height > 0;
        const scale = target ? Math.max(0.08, Math.min(0.25, icon.width / g.width, icon.height / g.height)) : 0.94;
        const dx = target ? icon.x + icon.width / 2 - g.x - g.width / 2 : 0;
        const dy = target ? icon.y + icon.height / 2 - g.y - g.height / 2 : 12;
        const normal = {sx: 1, sy: 1, x: 0, y: 0, opacity: 1};
        const minimized = {sx: scale, sy: scale, x: dx, y: dy, opacity: 0};
        this.transition(w, "minimize", showing, showing ? minimized : normal, showing ? normal : minimized,
            200, QEasingCurve.InOutCubic);
    }
    manage(w) {
        if (this.windows.has(w)) return;
        this.windows.add(w);
        // A drag must track KWin's real geometry immediately. In particular,
        // tearing a maximized/tiled window away must not retain the old
        // translation or start another maximize transition under the pointer.
        w.windowStartUserMovedResized.connect(() => this.stop(w));
        w.minimizedChanged.connect(() => this.minimize(w));
        w.windowHiddenChanged.connect(() => {
            if (!w.minimized && w.daisyMotionKind !== "minimize") this.presence(w, w.visible);
        });
        w.windowMaximizedStateAboutToChange.connect(() => {
            this.stop(w);
            if (w.visible && this.allowed(w, Effect.WindowAddedGrabRole))
                w.daisyOldGeometry = Object.assign({}, w.geometry);
        });
        w.windowMaximizedStateChanged.connect(() => {
            const old = w.daisyOldGeometry, g = w.geometry;
            delete w.daisyOldGeometry;
            if (!old || !w.visible || !this.allowed(w, Effect.WindowAddedGrabRole)
                || g.width <= 0 || g.height <= 0) return;
            this.transition(w, "geometry", true,
                {sx: old.width / g.width, sy: old.height / g.height,
                 x: old.x - g.x + (old.width - g.width) / 2,
                 y: old.y - g.y + (old.height - g.height) / 2, opacity: 1},
                {sx: 1, sy: 1, x: 0, y: 0, opacity: 1}, 220, QEasingCurve.OutCubic);
            w.daisyGeometrySource = old;
        });
        w.windowFrameGeometryChanged.connect((window, old) => {
            if (w.daisyMotionKind === "geometry" && w.daisyGeometrySource &&
                (old.width !== w.geometry.width || old.height !== w.geometry.height) &&
                (old.width !== w.daisyGeometrySource.width || old.height !== w.daisyGeometrySource.height)) this.stop(w);
        });
        w.windowFullScreenChanged.connect(() => this.stop(w));
    }
}
new DaisyWindowMotion();
