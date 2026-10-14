using DaisyOS.Shell.Services.Windows;
using DaisyOS.Shell.Views.Components.Taskbar;
using Xunit;

namespace DaisyOS.Tests;

public sealed class NativeAppWindowStateStoreTests
{
    [Fact]
    public void RegisterActivateMinimizeRestoreAndClose_TracksTheExpectedStates()
    {
        var store = new NativeAppWindowStateStore();

        store.Register("calculator");
        Assert.Equal(NativeAppWindowState.RunningInactive, store.GetState("calculator"));

        store.Activate("calculator");
        Assert.Equal(NativeAppWindowState.Active, store.GetState("calculator"));

        store.Minimize("calculator");
        Assert.Equal(NativeAppWindowState.Minimized, store.GetState("calculator"));

        store.Restore("calculator");
        Assert.Equal(NativeAppWindowState.RunningInactive, store.GetState("calculator"));

        store.Unregister("calculator");
        Assert.Equal(NativeAppWindowState.NotRunning, store.GetState("calculator"));
    }

    [Fact]
    public void Activate_MakesThePreviouslyActiveAppInactive()
    {
        var store = new NativeAppWindowStateStore();
        store.Register("notes");
        store.Register("settings");

        store.Activate("notes");
        store.Activate("settings");

        Assert.Equal(NativeAppWindowState.RunningInactive, store.GetState("notes"));
        Assert.Equal(NativeAppWindowState.Active, store.GetState("settings"));
    }

    [Fact]
    public void StateChanges_DoNotRaiseDuplicateEvents()
    {
        var store = new NativeAppWindowStateStore();
        var changes = new List<NativeAppWindowState>();
        store.StateChanged += (_, args) => changes.Add(args.State);

        store.Register("files");
        store.Register("files");
        store.Activate("files");
        store.Activate("files");

        Assert.Equal([NativeAppWindowState.RunningInactive, NativeAppWindowState.Active], changes);
    }

    [Theory]
    [InlineData(NativeAppWindowState.NotRunning, false, false, false)]
    [InlineData(NativeAppWindowState.RunningInactive, true, false, false)]
    [InlineData(NativeAppWindowState.Active, true, true, false)]
    [InlineData(NativeAppWindowState.Minimized, true, false, true)]
    public void TaskbarPresentation_MapsEveryWindowState(
        NativeAppWindowState state,
        bool isRunning,
        bool isActive,
        bool isMinimized)
    {
        var presentation = TaskbarWindowPresentation.From(state);

        Assert.Equal(isRunning, presentation.IsRunning);
        Assert.Equal(isActive, presentation.IsActive);
        Assert.Equal(isMinimized, presentation.IsMinimized);
    }
}
