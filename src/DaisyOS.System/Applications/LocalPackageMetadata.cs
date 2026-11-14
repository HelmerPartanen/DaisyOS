using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using DaisyOS.System.Processes;
namespace DaisyOS.System.Applications;
public sealed record LocalPackageMetadata(string Name, string Version, string Architecture)
{
    public static async Task<LocalPackageMetadata> ReadAsync(string path, ICommandRunner runner, CancellationToken token)
    {
        if(path.EndsWith(".rpm", StringComparison.OrdinalIgnoreCase)) return ReadRpm(path);
        using var file = File.OpenRead(path);
        var magic = new byte[8]; file.ReadExactly(magic);
        if(Encoding.ASCII.GetString(magic) != "!<arch>\n") throw new IOException("This file is not a Debian package.");
        var header = new byte[60];
        while(file.Position < file.Length)
        {
            token.ThrowIfCancellationRequested(); file.ReadExactly(header);
            var name = Encoding.ASCII.GetString(header, 0, 16).Trim().TrimEnd('/');
            if(!long.TryParse(Encoding.ASCII.GetString(header,48,10).Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var length) || length < 0 || length > file.Length - file.Position)
                throw new IOException("Invalid Debian archive header.");
            if(name.StartsWith("control.tar", StringComparison.Ordinal))
            {
                if(length > 16 * 1024 * 1024) throw new IOException("Package metadata is too large.");
                var temporary = Path.Combine(Path.GetTempPath(), "daisy-control-" + Guid.NewGuid().ToString("N"));
                try
                {
                    var bytes = new byte[(int)length]; file.ReadExactly(bytes); await File.WriteAllBytesAsync(temporary, bytes, token).ConfigureAwait(false);
                    var control = await runner.RunAsync("bsdtar", ["-xOf", temporary, "./control"], TimeSpan.FromSeconds(10), token).ConfigureAwait(false);
                    if(!control.Succeeded) control = await runner.RunAsync("bsdtar", ["-xOf", temporary, "control"], TimeSpan.FromSeconds(10), token).ConfigureAwait(false);
                    if(!control.Succeeded) throw new IOException("Could not read Debian package metadata: " + control.StandardError);
                    string Value(string key) => control.StandardOutput.Split('\n').FirstOrDefault(line => line.StartsWith(key + ":", StringComparison.Ordinal))?[(key.Length+1)..].Trim() ?? "";
                    var metadata = new LocalPackageMetadata(Value("Package"),Value("Version"),Value("Architecture")); Validate(metadata); return metadata;
                }
                finally { if(File.Exists(temporary))File.Delete(temporary); }
            }
            file.Seek(length + (length & 1), SeekOrigin.Current);
        }
        throw new IOException("The Debian package has no control metadata.");
    }
    private static LocalPackageMetadata ReadRpm(string path)
    {
        using var file = File.OpenRead(path); var lead = new byte[96]; file.ReadExactly(lead);
        if(!lead.AsSpan(0,4).SequenceEqual(new byte[]{0xed,0xab,0xee,0xdb})) throw new IOException("This file is not an RPM package.");
        Header(file); file.Position = (file.Position + 7) & ~7L;
        var values = Header(file);
        string Get(int tag) => values.GetValueOrDefault(tag, "");
        var metadata = new LocalPackageMetadata(Get(1000),Get(1001) + "-" + Get(1002),Get(1022)); Validate(metadata); return metadata;
    }
    private static Dictionary<int,string> Header(Stream file)
    {
        var header = new byte[16];file.ReadExactly(header);
        if(!header.AsSpan(0,4).SequenceEqual(new byte[]{0x8e,0xad,0xe8,0x01}))throw new IOException("Invalid RPM header.");
        var count=BinaryPrimitives.ReadInt32BigEndian(header.AsSpan(8,4));var length=BinaryPrimitives.ReadInt32BigEndian(header.AsSpan(12,4));
        if(count<0||count>65536||length<0||length>16*1024*1024)throw new IOException("RPM metadata exceeds limits.");
        var indexes=new byte[count*16];file.ReadExactly(indexes);var data=new byte[length];file.ReadExactly(data);
        var values=new Dictionary<int,string>();
        for(var i=0;i<count;i++)
        {
            var index=indexes.AsSpan(i*16,16);var tag=BinaryPrimitives.ReadInt32BigEndian(index);var type=BinaryPrimitives.ReadInt32BigEndian(index[4..]);var offset=BinaryPrimitives.ReadInt32BigEndian(index[8..]);
            if(type!=6||offset<0||offset>=length)continue;
            var end=Array.IndexOf(data,(byte)0,offset);if(end<0)throw new IOException("Invalid RPM string.");
            values[tag]=Encoding.UTF8.GetString(data,offset,end-offset);
        }
        return values;
    }
    private static void Validate(LocalPackageMetadata metadata)
    {
        if(metadata.Name.Length==0||metadata.Name.Length>200||!metadata.Name.All(c=>char.IsAsciiLetterOrDigit(c)||"+.-_".Contains(c))||metadata.Architecture.Length==0)
            throw new IOException("Package name or architecture is invalid.");
    }
}
