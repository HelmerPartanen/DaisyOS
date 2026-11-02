using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using DaisyOS.Shell.Apps.Files;
using DaisyOS.Shell.Apps.Files.Picker;
using DaisyOS.Shell.Apps.LocalSend;
using DaisyOS.System.Networking.LocalSend;
using Xunit;

namespace DaisyOS.ShellDemo.Tests;

public sealed class LocalSendSelectionTests
{
    [AvaloniaFact]
    public async Task FilesSharesTheWholeSelectionButNotAnUnselectedContextEntry()
    {
        var folder = Directory.CreateTempSubdirectory("daisy-share-selection-");
        var a = Path.Combine(folder.FullName, "a.txt");
        var b = Path.Combine(folder.FullName, "b.txt");
        var c = Path.Combine(folder.FullName, "c.txt");
        foreach (var path in new[] { a, b, c }) await File.WriteAllTextAsync(path, "test");
        var window = new FilesWindow(folder.FullName, false);
        window.Show();
        try
        {
            var vm = Assert.IsType<FilesViewModel>(window.DataContext);
            var tab = vm.ActiveTab!;
            await tab.NavigationTask;
            var first = tab.FilteredEntries.Single(entry => entry.Path == a);
            var second = tab.FilteredEntries.Single(entry => entry.Path == b);
            var third = tab.FilteredEntries.Single(entry => entry.Path == c);
            tab.SelectedItems.Clear();
            tab.SelectedItems.Add(first);
            tab.SelectedItems.Add(second);
            Assert.Equal(new[] { a, b }, window.GetLocalSendPaths(first));
            Assert.Equal(new[] { c }, window.GetLocalSendPaths(third));
        }
        finally { window.Close(); folder.Delete(true); }
    }

    [AvaloniaFact]
    public async Task ShareSheetSendsBothFilesAndKeepsSelectionInTheChooser()
    {
        var folder = Directory.CreateTempSubdirectory("daisy-send-many-");
        var paths = new[] { Path.Combine(folder.FullName, "one.txt"), Path.Combine(folder.FullName, "two.txt") };
        foreach (var path in paths) await File.WriteAllTextAsync(path, "test");
        var service = new SendStub();
        var sheet = new LocalSendWindow();
        var window = new Window { Content = sheet, Width = 1000, Height = 800 };
        window.Show();
        try
        {
            sheet.Open(service, false, paths);
            var options = sheet.CreateFilePickerOptions();
            Assert.True(options.AllowMultiple);
            Assert.Equal(paths, options.InitialSelection);
            var picker = new DaisyFilePickerWindow(options);
            try { Assert.Equal(paths, picker.SelectedPaths); }
            finally { picker.Close(); }
            var send = sheet.FindControl<Button>("SendButton")!;
            send.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(paths, service.Paths);
            Assert.False(send.IsEnabled);
            Assert.False(sheet.FindControl<Button>("ChooseFilesButton")!.IsEnabled);
            sheet.QueueFiles([paths[0]]);
            sheet.Open(service, false, [paths[0]]); // Reopening cannot replace an active transfer.
            send.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(1, service.Calls);
            Assert.Equal(paths, service.Paths);
            service.Completion.SetResult();
            for (var i = 0; i < 100 && sheet.IsOpen; i++) await Task.Delay(10);
            Assert.False(sheet.IsOpen);
        }
        finally { sheet.Dismiss(); window.Close(); folder.Delete(true); }
    }

    private sealed class SendStub : ILocalSendService
    {
        public event EventHandler? DevicesChanged { add { } remove { } }
        public event EventHandler<LocalSendIncomingTransfer>? IncomingTransferRequested { add { } remove { } }
        public event EventHandler<LocalSendTransferProgress>? TransferProgressChanged { add { } remove { } }
        public event EventHandler? ReceivingStateChanged { add { } remove { } }
        public bool IsRunning => true;
        public bool IsReceivingEnabled => false;
        public DateTimeOffset? ReceivingUntil => null;
        public TaskCompletionSource Completion { get; } = new();
        public IReadOnlyList<string>? Paths { get; private set; }
        public int Calls { get; private set; }
        public IReadOnlyList<LocalSendDevice> GetDevices() => [new("peer", "Test peer", "127.0.0.1", 53317, "desktop", "fingerprint", DateTimeOffset.Now)];
        public IReadOnlyList<LocalSendIncomingTransfer> GetPendingTransfers() => [];
        public Task StartAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task StopAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public void EnableReceivingFor(TimeSpan duration) { }
        public void DisableReceiving() { }
        public void RequestDiscoveryRefresh(bool forceSubnetProbe = false) { }
        public Task<bool> DecideIncomingAsync(string transferId, bool accept, CancellationToken cancellationToken = default) => Task.FromResult(false);
        public Task SendAsync(LocalSendDevice device, IReadOnlyList<string> paths, CancellationToken cancellationToken = default)
        { Calls++; Paths = paths; return Completion.Task; }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
