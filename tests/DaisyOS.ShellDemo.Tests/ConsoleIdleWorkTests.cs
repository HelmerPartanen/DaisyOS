using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using DaisyOS.Core.Models.Gaming;
using DaisyOS.Core.Services.Gaming;
using DaisyOS.Shell.Views.Components.Console;
using Xunit;

namespace DaisyOS.ShellDemo.Tests;

public sealed class ConsoleIdleWorkTests
{
    [AvaloniaFact]
    public void HiddenConsoleDoesNotDiscoverGamesUntilShownAndCancelsWhenHidden()
    {
        var discovery = new Discovery();
        var console = new ConsoleHomeView(discovery, new Artwork()) { IsVisible = false };
        var window = new Window { Content = console };
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(0, discovery.Calls);
            console.IsVisible = true;
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(1, discovery.Calls);
            Assert.False(discovery.Token.IsCancellationRequested);
            console.IsVisible = false;
            Dispatcher.UIThread.RunJobs();
            Assert.True(discovery.Token.IsCancellationRequested);
        }
        finally { window.Close(); }
    }

    private sealed class Discovery : IGameDiscoveryService
    {
        public int Calls;
        public CancellationToken Token;
        public async Task<IReadOnlyList<GameIdentity>> DiscoverGamesAsync(CancellationToken cancellationToken = default)
        {
            Calls++;
            Token = cancellationToken;
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return [];
        }
    }

    private sealed class Artwork : IGameArtworkResolver
    {
        public event EventHandler<ArtworkChangedEventArgs>? ArtworkUpdated { add { } remove { } }
        public Task<GameArtworkAssets> GetArtworkAsync(GameIdentity identity, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Hidden console must not request artwork.");
    }
}
