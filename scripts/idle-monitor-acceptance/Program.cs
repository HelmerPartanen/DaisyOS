using System.Diagnostics;
using System.Runtime.InteropServices;

// Run only against the isolated compositor in test-rust-idle-monitor.py.
if (args.Length != 1) return 2;
var library = NativeLibrary.Load(args[0]);
var start = Marshal.GetDelegateForFunctionPointer<Start>(NativeLibrary.GetExport(library, "daisy_idle_monitor_start"));
var stop = Marshal.GetDelegateForFunctionPointer<Stop>(NativeLibrary.GetExport(library, "daisy_idle_monitor_stop"));
using var received = new ManualResetEventSlim();
Exception? failure = null;
int idleCount = 0, resumedCount = 0;
Callback idle = data =>
{
    try
    {
        if (data != 42) throw new Exception("Callback user data changed.");
        // Exercise managed code and async process startup on the native stack.
        using var process = Process.Start(new ProcessStartInfo("/usr/bin/true") { UseShellExecute = false })!;
        process.WaitForExit();
        Interlocked.Increment(ref idleCount);
    }
    catch (Exception error) { failure = error; }
};
Callback resumed = data =>
{
    if (data != 42) failure = new Exception("Resume user data changed.");
    Interlocked.Increment(ref resumedCount);
    received.Set();
};
try
{
    for (var i = 0; i < 50; i++)
    {
        received.Reset();
        var handle = start(50, idle, resumed, 42);
        if (handle == 0) throw new Exception("Monitor failed to start.");
        try
        {
            if (!received.Wait(TimeSpan.FromSeconds(3))) throw new TimeoutException("Missing callback.");
        }
        finally { stop(handle); }
        if (failure is not null) throw failure;
    }
    if (idleCount != 50 || resumedCount != 50) throw new Exception("Callback counts differ.");
    Console.WriteLine("PASS: 50 managed reverse-P/Invoke cycles, user data and process startup");
    return 0;
}
finally
{
    GC.KeepAlive(idle);
    GC.KeepAlive(resumed);
    NativeLibrary.Free(library);
}
[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
delegate void Callback(nint data);
[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
delegate nint Start(uint timeout, Callback idle, Callback resumed, nint data);
[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
delegate void Stop(nint handle);
