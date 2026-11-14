using System.Runtime.InteropServices;
using DaisyOS.Core.Models;

namespace DaisyOS.System.Applications;

/// <summary>Reads bundle metadata through libflatpak without importing or executing the package.</summary>
internal static class FlatpakFileMetadata
{
    [DllImport("libgio-2.0.so.0")] private static extern nint g_file_new_for_path([MarshalAs(UnmanagedType.LPUTF8Str)] string path);
    [DllImport("libflatpak.so.0")] private static extern nint flatpak_bundle_ref_new(nint file, out nint error);
    [DllImport("libflatpak.so.0")] private static extern nint flatpak_ref_get_name(nint reference);
    [DllImport("libflatpak.so.0")] private static extern nint flatpak_ref_get_arch(nint reference);
    [DllImport("libflatpak.so.0")] private static extern nint flatpak_ref_get_branch(nint reference);
    [DllImport("libflatpak.so.0")] private static extern int flatpak_ref_get_kind(nint reference);
    [DllImport("libgobject-2.0.so.0")] private static extern void g_object_unref(nint value);
    [DllImport("libglib-2.0.so.0")] private static extern void g_error_free(nint error);
    internal static InstalledApplication ReadBundle(string path)
    {
        var file = g_file_new_for_path(path);
        nint reference = 0, error = 0;
        try
        {
            reference = flatpak_bundle_ref_new(file, out error);
            if (reference == 0) throw new IOException("The selected file is not a valid Flatpak bundle.");
            if (flatpak_ref_get_kind(reference) != 0) throw new IOException("Choose an application bundle. Runtime bundles are managed by Flatpak.");
            var name = Marshal.PtrToStringUTF8(flatpak_ref_get_name(reference)) ?? throw new IOException("The Flatpak has no application ID.");
            var arch = Marshal.PtrToStringUTF8(flatpak_ref_get_arch(reference)) ?? "";
            var branch = Marshal.PtrToStringUTF8(flatpak_ref_get_branch(reference)) ?? "";
            return new(new(ApplicationBackend.Flatpak, name, Reference: $"app/{name}/{arch}/{branch}"), name, branch)
                { SourcePath = path, Architecture = arch, DesktopIds = [name + ".desktop"] };
        }
        finally
        {
            if (reference != 0) g_object_unref(reference);
            if (file != 0) g_object_unref(file);
            if (error != 0) g_error_free(error);
        }
    }
}
