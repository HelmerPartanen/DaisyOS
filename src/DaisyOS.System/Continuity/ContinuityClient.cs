using System.Text.Json;
using DaisyOS.Core.Continuity;
using DaisyOS.Core.Serialization;
using Tmds.DBus.Protocol;

namespace DaisyOS.System.Continuity;

public sealed class ContinuityClient
{
    public const string BusName = "org.daisyos.Continuity1";
    public const string ObjectPath = "/org/daisyos/Continuity1";

    public async Task<ContinuityStatus> ExecuteAsync(ContinuityCommand command, CancellationToken token = default)
    {
        using var bus = new DBusConnection(DBusAddress.Session ?? throw new IOException("No session bus."));
        await bus.ConnectAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(3), token).ConfigureAwait(false);
        return await ExecuteOnAsync(bus, command, token).ConfigureAwait(false);
    }

    internal static async Task<ContinuityStatus> ExecuteOnAsync(DBusConnection bus, ContinuityCommand command, CancellationToken token)
    {
        MessageBuffer call;
        using (var writer = bus.GetMessageWriter())
        {
            writer.WriteMethodCallHeader(destination: BusName, path: ObjectPath, @interface: BusName, member: "Execute", signature: "s");
            writer.WriteString(JsonSerializer.Serialize(command, DaisyCoreJsonContext.Default.ContinuityCommand));
            call = writer.CreateMessage();
        }
        var json = await bus.CallMethodAsync(call, (reply, _) => reply.GetBodyReader().ReadString())
            .WaitAsync(TimeSpan.FromSeconds(10), token).ConfigureAwait(false);
        return JsonSerializer.Deserialize(json, DaisyCoreJsonContext.Default.ContinuityStatus) ?? throw new IOException("Invalid Continuity status.");
    }
}
