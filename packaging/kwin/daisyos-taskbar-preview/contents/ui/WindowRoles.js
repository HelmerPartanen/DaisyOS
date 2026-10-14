.pragma library

function isDaisyWallpaper(desktopFileName, resourceClass, resourceName, caption, layer, stackingOrder) {
    var identity = [desktopFileName, resourceClass, resourceName, caption]
        .map(function(value) { return String(value || "").toLowerCase(); })
        .join(" ");

    var explicitlyNamed = identity.indexOf("daisyos.wallpaper") >= 0
        || (identity.indexOf("daisyos") >= 0 && identity.indexOf("wallpaper") >= 0);
    // KWin 6.7 intentionally hides the layer-shell namespace from
    // WindowModel, but exposes the protocol layer: DaisyOS' wallpaper is the
    // sole shell surface on Background (0); desktop items use Bottom (1).
    return explicitlyNamed || Number(layer) === 0 || Number(stackingOrder) === 0;
}

function isFilesWindow(desktopFileName, resourceClass, resourceName, caption) {
    var identity = [desktopFileName, resourceClass, resourceName, caption]
        .map(function(value) { return String(value || "").toLowerCase(); })
        .join(" ");
    return identity.indexOf("org.daisyos.files") >= 0
        || identity.indexOf("daisyos.files") >= 0
        || String(caption || "").toLowerCase() === "files";
}
