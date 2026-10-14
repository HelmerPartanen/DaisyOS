// KWin owns window management. This script only publishes an authoritative,
// unprivileged snapshot to the DaisyOS session bus whenever the model changes.
// The shell consumes this to render its taskbar and switcher; it never infers
// windows from processes or X11 stacking state.

var bridgeService = "org.daisyos.Shell.WindowBridge";
var bridgePath = "/org/daisyos/Shell/WindowBridge";
var bridgeInterface = "org.daisyos.Shell.WindowBridge";
var lastSnapshot = "";
var daisyAppIds = {};

var daisyAppTitles = {
    "settings": "settings",
    "notes": "notes",
    "calendar": "calendar",
    "calculator": "calculator",
    "files": "files",
    "disk manager": "diskmanager",
    "app store": "appstore",
    "sovelluskauppa": "appstore"
};

var daisyAppIdsByWaylandAppId = {
    "org.daisyos.settings": "settings",
    "org.daisyos.notes": "notes",
    "org.daisyos.calendar": "calendar",
    "org.daisyos.calculator": "calculator",
    "org.daisyos.files": "files",
    "org.daisyos.diskmanager": "diskmanager",
    "org.daisyos.appstore": "appstore"
};

// These are DaisyOS shell actions, not application shortcuts. Registering
// them in KWin ensures the compositor owns the grab before Spectacle or any
// ordinary client can receive the key sequence.
function requestDaisyScreenshot(action) {
    callDBus(bridgeService, bridgePath, bridgeInterface, "RequestScreenshot", action, function () {});
}

function requestDaisyLock() {
    callDBus(bridgeService, bridgePath, bridgeInterface, "RequestLock", "", function () {});
}

function requestMediaAction(action) {
    callDBus(bridgeService, bridgePath, bridgeInterface, "RequestMediaAction", action, function () {});
}

function requestShellAction(action) {
    callDBus(bridgeService, bridgePath, bridgeInterface, "RequestShellAction", action, function () {});
}

// Keep launcher input independent of the overview effect's QML lifecycle.
registerShortcut("DaisyOS Launcher", "Toggle DaisyOS Launcher", "Meta", function () {
    requestShellAction("launcher");
});

registerShortcut("DaisyOS Capture Screen", "Capture the active display with DaisyOS Screenshot", "Print", function () {
    requestDaisyScreenshot("screen");
});

registerShortcut("DaisyOS Capture Region", "Select a region with DaisyOS Screenshot", "Meta+Shift+S", function () {
    requestDaisyScreenshot("region");
});

registerShortcut("DaisyOS Lock Screen", "Lock the session with DaisyOS Lock Screen", "Meta+L", function () {
    requestDaisyLock();
});

registerShortcut("DaisyOS Open Files", "Open DaisyOS Files", "Meta+E", function () {
    requestShellAction("files");
});

registerShortcut("DaisyOS Open Settings", "Open DaisyOS Settings", "Meta+I", function () {
    requestShellAction("settings");
});

registerShortcut("DaisyOS Notifications", "Show DaisyOS notifications", "Meta+N", function () {
    requestShellAction("notifications");
});

registerShortcut("DaisyOS Clipboard History", "Show DaisyOS clipboard history", "Meta+V", function () {
    requestShellAction("clipboard");
});

registerShortcut("DaisyOS New Note", "Create a new DaisyOS note", "Meta+Shift+N", function () {
    requestShellAction("new-note");
});

registerShortcut("DaisyOS Task Manager", "Open DaisyOS Task Manager", "Ctrl+Shift+Esc", function () {
    requestShellAction("task-manager");
});

registerShortcut("DaisyOS Media Play Pause", "Play or pause media", "Media Play", function () {
    requestMediaAction("play-pause");
});

registerShortcut("DaisyOS Media Next", "Skip to the next track", "Media Next", function () {
    requestMediaAction("next");
});

registerShortcut("DaisyOS Media Previous", "Return to the previous track", "Media Previous", function () {
    requestMediaAction("previous");
});

function stringValue(value) {
    return value === undefined || value === null ? "" : String(value);
}

function isDaisyShellProcess(window) {
    var appId = stringValue(window.desktopFileName).toLowerCase();
    var resource = stringValue(window.resourceClass).toLowerCase();
    var resourceName = stringValue(window.resourceName).toLowerCase();
    return appId.indexOf("daisyos-shell") >= 0
        || resource.indexOf("daisyos-shell") >= 0
        || resource.indexOf("daisyos.shell") >= 0
        || resourceName.indexOf("daisyos-shell") >= 0
        || resourceName.indexOf("daisyos.shell") >= 0;
}

function daisyAppId(window) {
    var id = stringValue(window.internalId);
    if (daisyAppIds[id]) return daisyAppIds[id];

    // Normal DaisyOS windows explicitly publish xdg_toplevel.app_id. This is
    // stable across title changes and lets KWin authorize restore/focus just
    // like it does for every third-party Wayland application.
    var identities = [window.desktopFileName, window.resourceClass, window.resourceName];
    for (var index = 0; index < identities.length; ++index) {
        var declared = stringValue(identities[index]).toLowerCase();
        if (daisyAppIdsByWaylandAppId[declared]) {
            daisyAppIds[id] = daisyAppIdsByWaylandAppId[declared];
            return daisyAppIds[id];
        }
    }

    if (!isDaisyShellProcess(window)) return "";

    var title = stringValue(window.caption).toLowerCase();
    for (var candidate in daisyAppTitles) {
        if (title === daisyAppTitles[candidate]) {
            daisyAppIds[id] = candidate;
            return candidate;
        }
    }

    return "";
}

function isShellWindow(window) {
    var caption = stringValue(window.caption).toLowerCase();
    return caption === "daisyos shell" || (isDaisyShellProcess(window) && !daisyAppId(window));
}

function managedWindows() {
    // KWin 6 calls these windowList/windowAdded. Older KWin releases expose
    // the same managed-client model as clientList/clientAdded. Supporting
    // both makes the taskbar bridge independent of that naming change.
    if (typeof workspace.windowList === "function") return workspace.windowList();
    if (typeof workspace.clientList === "function") return workspace.clientList();
    return [];
}

function isApplicationWindow(window) {
    if (!window || window.internal || window.skipTaskbar || isShellWindow(window)) return false;
    if (window.managed === false) return false;
    // Do not whitelist only normalWindow/dialog.  Native Wayland clients can
    // be classified by KWin as another ordinary xdg_toplevel kind (notably an
    // undecorated Avalonia window), even though they are fully manageable and
    // taskbar eligible.  KWin already exposes the authoritative negative
    // tests: internal, skipTaskbar and specialWindow.  Keeping every remaining
    // managed client makes system and third-party apps follow one lifecycle.
    if (window.specialWindow === true || window.popupWindow === true) return false;
    return true;
}

function describe(window) {
    var desktopFileId = stringValue(window.desktopFileName);
    var appId = desktopFileId || stringValue(window.resourceClass) || stringValue(window.resourceName) || "application";
    var activeWindow = workspace.activeWindow;
    var activeId = activeWindow ? stringValue(activeWindow.internalId) : "";
    return {
        id: stringValue(window.internalId),
        appId: appId,
        title: stringValue(window.caption),
        desktopFileId: desktopFileId || null,
        iconName: stringValue(window.icon) || null,
        outputName: window.output ? stringValue(window.output.name) : null,
        workspace: window.desktops && window.desktops.length > 0 ? Number(window.desktops[0].x11DesktopNumber || 0) : 0,
        // KWin can expose different JS wrapper instances for the same native
        // window. The stable UUID keeps taskbar state in sync with commands.
        isActive: activeId === stringValue(window.internalId),
        isMinimized: !!window.minimized,
        isMaximized: Number(window.maximizeMode) === 3,
        isFullScreen: !!window.fullScreen,
        isOnCurrentWorkspace: isOnCurrentWorkspace(window),
        canClose: !!window.closeable,
        daisyAppId: daisyAppId(window) || null,
        processId: Number(window.pid || 0),
        isUnresponsive: !!window.unresponsive
    };
}

function isOnCurrentWorkspace(window) {
    return window.onAllDesktops || !window.desktops || window.desktops.length === 0
        || window.desktops.some(function (desktop) { return desktop.id === workspace.currentDesktop.id; });
}

function publishSnapshot() {
    var managed = managedWindows();
    var windows = managed.filter(isApplicationWindow).map(describe);
    // Fullscreen games and video clients may opt out of the taskbar. Their
    // desktop coverage is independent of taskbar eligibility and keyboard focus.
    var covering = managed.filter(function (window) {
        return window && !window.internal && window.managed !== false && !isShellWindow(window)
            && window.specialWindow !== true && window.popupWindow !== true
            && !window.minimized && isOnCurrentWorkspace(window);
    });
    var snapshot = JSON.stringify({ windows: windows,
        desktopOccluded: covering.some(function (window) { return window.fullScreen || Number(window.maximizeMode) === 3; }),
        fullscreenActive: covering.some(function (window) { return !!window.fullScreen; }) });
    if (snapshot === lastSnapshot) return;
    lastSnapshot = snapshot;
    callDBus(bridgeService, bridgePath, bridgeInterface, "PublishSnapshot", snapshot, function () {});
}

function observeWindow(window) {
    if (!window) return;
    var refresh = function () { publishSnapshot(); };

    // A newly-created Wayland or XWayland client can be added to KWin before
    // its role, desktop-file id and taskbar eligibility are finalized.  Watch
    // those readiness transitions as well as the steady-state window flags so
    // the taskbar receives the client as soon as KWin makes it visible.
    if (window.windowShown) window.windowShown.connect(refresh);
    if (window.readyForPaintingChanged) window.readyForPaintingChanged.connect(refresh);
    if (window.surfaceChanged) window.surfaceChanged.connect(refresh);
    if (window.windowClassChanged) window.windowClassChanged.connect(refresh);
    if (window.windowRoleChanged) window.windowRoleChanged.connect(refresh);
    if (window.desktopFileNameChanged) window.desktopFileNameChanged.connect(refresh);
    if (window.skipTaskbarChanged) window.skipTaskbarChanged.connect(refresh);
    if (window.minimizedChanged) window.minimizedChanged.connect(refresh);
    if (window.maximizedChanged) window.maximizedChanged.connect(refresh);
    if (window.fullScreenChanged) window.fullScreenChanged.connect(refresh);
    if (window.frameGeometryChanged) window.frameGeometryChanged.connect(refresh);
    if (window.activeChanged) window.activeChanged.connect(refresh);
    if (window.unresponsiveChanged) window.unresponsiveChanged.connect(refresh);
    if (window.captionChanged) window.captionChanged.connect(refresh);
    if (window.desktopsChanged) window.desktopsChanged.connect(refresh);
    if (window.outputChanged) window.outputChanged.connect(refresh);
    enforceClientDecorations(window);
}

function enforceClientDecorations(window) {
    if (!window) return;
    var appId = stringValue(window.desktopFileName).toLowerCase();
    var customFrameApps = ["files", "settings", "notes"];
    if (customFrameApps.indexOf(appId.replace(/^org\.daisyos\./, "")) >= 0 ||
        customFrameApps.indexOf(daisyAppId(window)) >= 0) {
        window.noBorder = true;
    }
}

if (workspace.windowAdded) {
    workspace.windowAdded.connect(function (window) {
        enforceClientDecorations(window);
        observeWindow(window);
        publishSnapshot();
    });
} else if (workspace.clientAdded) {
    workspace.clientAdded.connect(function (window) {
        enforceClientDecorations(window);
        observeWindow(window);
        publishSnapshot();
    });
}
if (workspace.windowRemoved) {
    workspace.windowRemoved.connect(function (window) {
        delete daisyAppIds[stringValue(window.internalId)];
        publishSnapshot();
    });
} else if (workspace.clientRemoved) {
    workspace.clientRemoved.connect(function (window) {
        delete daisyAppIds[stringValue(window.internalId)];
        publishSnapshot();
    });
}
if (workspace.windowActivated) workspace.windowActivated.connect(publishSnapshot);
else if (workspace.clientActivated) workspace.clientActivated.connect(publishSnapshot);
if (workspace.currentDesktopChanged) workspace.currentDesktopChanged.connect(publishSnapshot);
managedWindows().forEach(observeWindow);
publishSnapshot();
print("[daisy-window-bridge] Persistent KWin lifecycle bridge loaded.");
