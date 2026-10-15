using System.Net.Sockets;
using System.Text;
using DaisyOS.Shell.Services.Diagnostics;
using Xunit;

namespace DaisyOS.Shell.Tests;

public sealed class AutomaticGameTelemetryTests
{
    [Fact]
    public async Task Starts_capture_on_open_and_stops_on_close_without_game_launch_options()
    {
        var user = "test-" + Guid.NewGuid().ToString("N");
        var name = "daisyos-fps-" + user + "-1234";
        var table = Path.GetTempFileName();
        using var server = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        server.Bind(new UnixDomainSocketEndPoint("\0" + name)); server.Listen(4);
        try
        {
            File.WriteAllText(table, "000: socket @" + name + "\n000: socket @daisyos-fps-other-9876\n");
            var controller = new GameFrameTelemetryController(table, user);
            Assert.Equal(new[] { 1234 }, controller.AvailableProcesses());
            var received = new List<string>();
            async Task Receive()
            {
                using var peer = await server.AcceptAsync();
                await peer.SendAsync(Encoding.ASCII.GetBytes(":MangoHudControlVersion=1;"), SocketFlags.None);
                var buffer = new byte[1024];
                var length = await peer.ReceiveAsync(buffer, SocketFlags.None);
                received.Add(Encoding.ASCII.GetString(buffer, 0, length));
            }
            await Task.WhenAll(Receive(), controller.UpdateAsync(true, 1234, default)).WaitAsync(TimeSpan.FromSeconds(5));
            await controller.UpdateAsync(true, 1234, default); // No repeated startup request.
            await Task.WhenAll(Receive(), controller.UpdateAsync(false, 0, default)).WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(new[] { ":logging=1;", ":logging=0;" }, received);
        }
        finally { File.Delete(table); }
    }

    [Fact]
    public async Task Missing_capture_socket_is_retried_when_it_becomes_available()
    {
        var user = "test-" + Guid.NewGuid().ToString("N");
        var name = "daisyos-fps-" + user + "-4321";
        var table = Path.GetTempFileName();
        try
        {
            File.WriteAllText(table, "000: socket @" + name + "\n");
            var controller = new GameFrameTelemetryController(table, user);
            await controller.UpdateAsync(true, 4321, default);
            using var server = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            server.Bind(new UnixDomainSocketEndPoint("\0" + name)); server.Listen(1);
            var capture = Task.Run(async () =>
            {
                using var peer = await server.AcceptAsync();
                await peer.SendAsync(Encoding.ASCII.GetBytes(":ready;"), SocketFlags.None);
                var data = new byte[100];
                return Encoding.ASCII.GetString(data, 0, await peer.ReceiveAsync(data, SocketFlags.None));
            });
            await controller.UpdateAsync(true, 4321, default);
            Assert.Equal(":logging=1;", await capture.WaitAsync(TimeSpan.FromSeconds(5)));
        }
        finally { File.Delete(table); }
    }
}
