import QtQuick 2.15
import QtTest 1.3
import "../contents/ui/WindowRoles.js" as WindowRoles

TestCase {
    name: "WindowRoles"

    function test_recognizesWallpaperNamespace() {
        verify(WindowRoles.isDaisyWallpaper("", "", "daisyos.wallpaper", "", 3, 8));
        verify(WindowRoles.isDaisyWallpaper("DaisyOS.Wallpaper", "", "", "", 3, 8));
        verify(WindowRoles.isDaisyWallpaper("", "DaisyOS.Shell", "DaisyOS.Shell", "", 0, 2));
        verify(WindowRoles.isDaisyWallpaper("", "DaisyOS.Shell", "DaisyOS.Shell", "", undefined, 0));
    }

    function test_rejectsOtherShellSurfaces() {
        verify(!WindowRoles.isDaisyWallpaper("", "DaisyOS.Shell", "DaisyOS.Shell", "", 1, 1));
        verify(!WindowRoles.isDaisyWallpaper("", "DaisyOS.Shell", "DaisyOS.Shell", "", 3, 8));
        verify(!WindowRoles.isDaisyWallpaper("code", "code", "code", "Visual Studio Code", 2, 4));
    }

    function test_recognizesFilesWindow() {
        verify(WindowRoles.isFilesWindow("org.daisyos.files", "", "", "Files"));
        verify(WindowRoles.isFilesWindow("", "DaisyOS.Files", "", ""));
        verify(!WindowRoles.isFilesWindow("code", "code", "code", "Visual Studio Code"));
    }
}
