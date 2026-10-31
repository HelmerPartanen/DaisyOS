using DaisyOS.Continuity;
using DaisyOS.Continuity.Protocol.V1;

namespace DaisyOS.Continuity.Tests;

public sealed class InputPositionTests
{
    [Theory]
    [InlineData("right", "left")]
    [InlineData("left", "right")]
    [InlineData("top", "bottom")]
    [InlineData("bottom", "top")]
    public async Task NewerPositionAppliesOppositeEdgeAndPersistsWithoutEcho(string edge, string opposite)
    {
        var root = Path.Combine(Path.GetTempPath(), "daisy-position-" + Guid.NewGuid().ToString("N"));
        try
        {
            using var local = new SecureState(Path.Combine(root, "local"));
            using var remote = new SecureState(Path.Combine(root, "remote"));
            local.Settings.Enabled = true;
            local.Settings.Peers[remote.Id] = new(Convert.ToBase64String(remote.Identity.RawData), "Remote", "127.0.0.1:1");
            await using (var daemon = new ContinuityDaemon(local, 47621, () => true, root, "unix:path=/nonexistent-position"))
            {
                var position = new InputPosition { Edge = edge, Revision = 10, Author = remote.Id };
                daemon.ApplyInputPosition(remote.Id, position);
                Assert.Equal(opposite, local.Settings.Peers[remote.Id].InputEdge);
                daemon.ApplyInputPosition(remote.Id, new() { Edge = opposite, Revision = 9, Author = remote.Id });
                daemon.ApplyInputPosition(remote.Id, position);
                Assert.Equal(opposite, local.Settings.Peers[remote.Id].InputEdge);
                Assert.Equal(10, local.Settings.Peers[remote.Id].EdgeRevision);
            }
            local.Dispose();
            using var reloaded = new SecureState(Path.Combine(root, "local"));
            Assert.Equal(opposite, reloaded.Settings.Peers[remote.Id].InputEdge);
        }
        finally { Directory.Delete(root, true); }
    }
}
