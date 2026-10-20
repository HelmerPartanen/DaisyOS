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
        return this.eligible(w) && !this.reduced && effects.animationTimeFactor > 0
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
    presence(w, showing, role) {
        role = role || Effect.WindowAddedGrabRole;
        if (!this.allowed(w, role) || (showing && !w.visible)) { this.stop(w); return; }
        if (role !== Effect.WindowClosedGrabRole && w.daisyMotion && w.daisyMotionKind === "presence"
            && redirect(w.daisyMotion, showing === w.daisyMotionShowing ? Effect.Forward : Effect.Backward)) return;
        this.stop(w);
        // Start at the actual source endpoint; only reverse an already running timeline.
        w.daisyMotionKind = "presence";
        w.daisyMotionShowing = showing;
        const fileWindow = /(^|[\s.])(?:org\.daisyos\.)?files(?:[\s.]|$)/i.test(String(w.windowClass || w.desktopFileName || ""));
        // Files opens without a zoom pulse; keep the app's everyday motion quiet.
        const presenceAnimations = [{ type: Effect.Opacity, from: showing ? 0 : 1, to: showing ? 1 : 0 }];
        if (!fileWindow) presenceAnimations.unshift({ type: Effect.Scale, from: showing ? 0.96 : 1, to: showing ? 1 : 0.96 });
        w.daisyMotion = animate({ window: w, duration: animationTime(fileWindow ? (showing ? 120 : 100) : (showing ? 180 : 140)),
            curve: fileWindow ? (showing ? QEasingCurve.OutCubic : QEasingCurve.InCubic) : QEasingCurve.InOutCubic,
            keepAlive: role === Effect.WindowClosedGrabRole, animations: presenceAnimations });
    }
    minimize(w) {
        const showing = !w.minimized;
        const role = showing ? Effect.WindowUnminimizedGrabRole : Effect.WindowMinimizedGrabRole;
        if (!this.allowed(w, role)) { this.stop(w); return; }
        if (w.daisyMotion && w.daisyMotionKind === "minimize"
            && redirect(w.daisyMotion, showing === w.daisyMotionShowing ? Effect.Forward : Effect.Backward)) return;
        this.stop(w);
        const g = w.geometry, icon = w.iconGeometry;
        const target = icon && icon.width > 0 && icon.height > 0;
        const scale = target ? Math.max(0.08, Math.min(0.25, icon.width / g.width, icon.height / g.height)) : 0.94;
        const dx = target ? icon.x + icon.width / 2 - g.x - g.width / 2 : 0;
        const dy = target ? icon.y + icon.height / 2 - g.y - g.height / 2 : 12;
        w.daisyMotionKind = "minimize";
        w.daisyMotionShowing = showing;
        w.daisyMotion = animate({ window: w, duration: animationTime(200),
            curve: QEasingCurve.InOutCubic, keepAlive: false,
            animations: [{ type: Effect.Scale, from: showing ? scale : 1, to: showing ? 1 : scale },
                         { type: Effect.Translation,
                           from: {value1: showing ? dx : 0, value2: showing ? dy : 0},
                           to: {value1: showing ? 0 : dx, value2: showing ? 0 : dy} },
                         { type: Effect.Opacity, from: showing ? 0 : 1, to: showing ? 1 : 0 }] });
    }
    manage(w) {
        if (this.windows.has(w)) return;
        this.windows.add(w);
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
            w.daisyMotionKind = "geometry";
            w.daisyGeometrySource = old;
            w.daisyMotion = animate({ window: w, duration: animationTime(220),
                curve: QEasingCurve.OutCubic, keepAlive: false,
                animations: [{ type: Effect.Size,
                    from: {value1: old.width, value2: old.height}, to: {value1: g.width, value2: g.height} },
                    { type: Effect.Translation,
                      from: {value1: old.x - g.x + (old.width - g.width) / 2,
                             value2: old.y - g.y + (old.height - g.height) / 2},
                      to: {value1: 0, value2: 0} }] });
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
