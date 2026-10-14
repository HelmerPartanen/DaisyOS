using System.Runtime.InteropServices;

namespace DaisyOS.Shell.Services.Theming;

/// <summary>Keep the bundled HarfBuzz ABI separate from Fontconfig/FreeType's system HarfBuzz.</summary>
internal static class LinuxTextLibraryIsolation
{
    private static readonly object Gate = new();
    private static nint _handle;

    public static void EnsureLoaded()
    {
        if (!OperatingSystem.IsLinux()) return;
        lock (Gate)
        {
            if (_handle != 0) return;
            var path = new[]
            {
                Path.Combine(AppContext.BaseDirectory, "libHarfBuzzSharp.so"),
                Path.Combine(AppContext.BaseDirectory, "runtimes", RuntimeInformation.RuntimeIdentifier, "native", "libHarfBuzzSharp.so"),
                Path.Combine(AppContext.BaseDirectory, "runtimes", "linux-" + RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant(), "native", "libHarfBuzzSharp.so")
            }.FirstOrDefault(File.Exists);
            if (path is null) return; // Keep the platform resolver for other packaging layouts.
            try
            {
                // RTLD_NOW | RTLD_DEEPBIND | RTLD_LOCAL: internal hb_* references
                // must resolve to this bundled version, even when system fonts
                // loaded a different global libharfbuzz first. Hold for process lifetime.
                _handle = Dlopen(path, 0x2 | 0x8);
                if (Environment.GetEnvironmentVariable("DAISYOS_PERFORMANCE_RESULT") is not null)
                    Console.Error.WriteLine($"[text-library] Isolated {path}: {_handle != 0}");
                if (_handle == 0) throw new InvalidOperationException("Could not load bundled text library: " + Marshal.PtrToStringUTF8(Dlerror()));
            }
            catch (DllNotFoundException) { } // Non-glibc Linux uses the regular resolver.
        }
    }

    [DllImport("libdl.so.2", EntryPoint = "dlopen")]
    private static extern nint Dlopen([MarshalAs(UnmanagedType.LPUTF8Str)] string path, int flags);
    [DllImport("libdl.so.2", EntryPoint = "dlerror")]
    private static extern nint Dlerror();
}
