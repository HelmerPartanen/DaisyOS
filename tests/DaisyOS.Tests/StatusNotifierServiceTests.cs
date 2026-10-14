using System.Diagnostics;
using DaisyOS.Shell.Services.Tray;
using Tmds.DBus.Protocol;
using Xunit;

namespace DaisyOS.Tests;

public sealed class StatusNotifierServiceTests
{
    [Fact]
    public async Task PrivateBusRegistersRefreshesActivatesAndRemovesRealTrayItem()
    {
        using var daemon = Process.Start(new ProcessStartInfo("dbus-daemon")
        {
            ArgumentList = { "--session", "--nofork", "--print-address=1" },
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        })!;
        try
        {
            var address = await daemon.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(5));
            Assert.NotNull(address);
            await using var tray = new StatusNotifierService(address);
            await tray.StartAsync();
            Assert.True(tray.IsAvailable);
            using var itemConnection = new DBusConnection(address);
            await itemConnection.ConnectAsync();
            await itemConnection.RequestNameAsync("org.kde.StatusNotifierItem.DaisyTest");
            var handler = new ItemHandler(); itemConnection.AddMethodHandler(handler);
            MessageBuffer register;
            using (var writer = itemConnection.GetMessageWriter())
            {
                writer.WriteMethodCallHeader(destination: "org.kde.StatusNotifierWatcher", path: "/StatusNotifierWatcher",
                    @interface: "org.kde.StatusNotifierWatcher", member: "RegisterStatusNotifierItem", signature: "s");
                writer.WriteString("/StatusNotifierItem"); register = writer.CreateMessage();
            }
            await itemConnection.CallMethodAsync(register);
            await tray.RefreshAsync();
            var item = Assert.Single(tray.Items);
            Assert.Equal("Test background app", item.Title);
            await using var secondHost = new StatusNotifierService(address);
            await secondHost.StartAsync();
            await secondHost.RefreshAsync();
            Assert.Equal(item.Title, Assert.Single(secondHost.Items).Title);
            await tray.ActivateAsync(item, 12, 34);
            Assert.Equal((12, 34), handler.Activated);
            handler.Status = "Passive";
            await tray.RefreshAsync(); Assert.Empty(tray.Items);
            handler.Status = "Active";
            await tray.RefreshAsync(); Assert.Single(tray.Items);
            var menu = await tray.GetMenuAsync(item);
            Assert.Equal("Open", Assert.Single(menu).Label);
            await tray.InvokeMenuAsync(item, 1);
            Assert.Equal(1, handler.Clicked);
            itemConnection.Dispose();
            await tray.RefreshAsync(); Assert.Empty(tray.Items);
        }
        finally
        {
            if (!daemon.HasExited) daemon.Kill();
            await daemon.WaitForExitAsync();
        }
    }

    private sealed class ItemHandler : IPathMethodHandler
    {
        public string Path => "/StatusNotifierItem";
        public bool HandlesChildPaths => true;
        public string Status = "Active";
        public (int, int) Activated;
        public int Clicked;
        public ValueTask HandleMethodAsync(MethodContext context)
        {
            var request = context.Request;
            if (request.MemberAsString == "GetAll")
            {
                using var writer = context.CreateReplyWriter("a{sv}");
                writer.WriteDictionary(new Dictionary<string, VariantValue>
                {
                    ["Title"] = "Test background app",
                    ["Status"] = Status,
                    ["IconName"] = "steam",
                    ["Menu"] = new ObjectPath("/StatusNotifierItem/Menu"),
                    ["ItemIsMenu"] = false
                });
                context.Reply(writer.CreateMessage());
            }
            else if (request.MemberAsString == "Activate")
            {
                var reader = request.GetBodyReader(); Activated = (reader.ReadInt32(), reader.ReadInt32());
                using var writer = context.CreateReplyWriter(""); context.Reply(writer.CreateMessage());
            }
            else if (request.MemberAsString == "AboutToShow")
            {
                using var writer = context.CreateReplyWriter("b"); writer.WriteBool(false); context.Reply(writer.CreateMessage());
            }
            else if (request.MemberAsString == "GetLayout")
            {
                using var writer = context.CreateReplyWriter("u(ia{sv}av)");
                writer.WriteUInt32(1); writer.WriteStructureStart(); writer.WriteInt32(0);
                writer.WriteDictionary(new Dictionary<string, VariantValue>());
                var start = writer.WriteArrayStart(DBusType.Variant);
                writer.WriteVariant(VariantValue.Struct(1,
                    new Dict<string, VariantValue> { ["label"] = "_Open", ["enabled"] = true },
                    VariantValue.ArrayOfVariant(Array.Empty<VariantValue>())));
                writer.WriteArrayEnd(start); context.Reply(writer.CreateMessage());
            }
            else if (request.MemberAsString == "Event")
            {
                Clicked = request.GetBodyReader().ReadInt32();
                using var writer = context.CreateReplyWriter(""); context.Reply(writer.CreateMessage());
            }
            return default;
        }
    }
}
