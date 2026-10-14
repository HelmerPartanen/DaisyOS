using System.Text;

namespace DaisyOS.Shell.Services.Wallpaper;

/// <summary>Bounded reader for the binary property lists embedded in Apple wallpaper XMP.</summary>
internal sealed class WallpaperPropertyList
{
    private readonly byte[] _data;
    private readonly int _offsetSize, _referenceSize, _count, _table;
    private int _remaining = 100000;
    private readonly HashSet<int> _reading = [];

    private WallpaperPropertyList(byte[] data)
    {
        _data = data;
        if (data.Length < 40 || !data.AsSpan(0, 8).SequenceEqual("bplist00"u8)) throw new InvalidDataException("Invalid wallpaper property list.");
        var trailer = data.Length - 32;
        _offsetSize = data[trailer + 6];
        _referenceSize = data[trailer + 7];
        _count = checked((int)Unsigned(trailer + 8, 8));
        _table = checked((int)Unsigned(trailer + 24, 8));
        if (_offsetSize is < 1 or > 8 || _referenceSize is < 1 or > 8 || _count is < 1 or > 32768 ||
            _table < 8 || (long)_table + (long)_count * _offsetSize > trailer) throw new InvalidDataException("Invalid wallpaper property list table.");
    }

    internal static Dictionary<string, object?> Read(byte[] data)
    {
        var reader = new WallpaperPropertyList(data);
        return reader.Object(checked((int)reader.Unsigned(data.Length - 16, 8)), 0) as Dictionary<string, object?>
            ?? throw new InvalidDataException("Wallpaper property list must contain a dictionary.");
    }

    private ulong Unsigned(int offset, int size)
    {
        if (size is < 1 or > 8 || offset < 0 || offset > _data.Length - size) throw new InvalidDataException("Invalid wallpaper property list offset.");
        ulong value = 0;
        foreach (var b in _data.AsSpan(offset, size)) value = (value << 8) | b;
        return value;
    }

    private object? Object(int id, int depth)
    {
        if (depth > 32 || --_remaining <= 0 || id < 0 || id >= _count || !_reading.Add(id)) throw new InvalidDataException("Invalid wallpaper property list reference.");
        try
        {
            var position = checked((int)Unsigned(_table + id * _offsetSize, _offsetSize));
            if (position < 8 || position >= _table) throw new InvalidDataException("Invalid wallpaper property list object.");
            var tag = _data[position++];
            var type = tag >> 4;
            var length = tag & 15;
            if (type == 0) return tag == 9 ? true : tag == 8 ? false : null;
            if (type == 1)
            {
                if (length > 3) throw new InvalidDataException("Oversized wallpaper integer.");
                return (double)Unsigned(position, 1 << length);
            }
            if (type == 2)
            {
                if (length == 2) return (double)BitConverter.Int32BitsToSingle(unchecked((int)Unsigned(position, 4)));
                if (length == 3) return BitConverter.Int64BitsToDouble(unchecked((long)Unsigned(position, 8)));
                throw new InvalidDataException("Invalid wallpaper real.");
            }
            if (length == 15)
            {
                var integer = _data[position++];
                if (integer >> 4 != 1 || (integer & 15) > 3) throw new InvalidDataException("Invalid wallpaper collection length.");
                var bytes = 1 << (integer & 15);
                length = checked((int)Unsigned(position, bytes));
                position += bytes;
            }
            if (length > 32768) throw new InvalidDataException("Oversized wallpaper collection.");
            if (type is 5 or 6)
            {
                var bytes = checked(length * (type == 6 ? 2 : 1));
                if (position > _table - bytes) throw new InvalidDataException("Invalid wallpaper string.");
                return (type == 6 ? Encoding.BigEndianUnicode : Encoding.ASCII).GetString(_data, position, bytes);
            }
            if (type is not (10 or 13)) throw new InvalidDataException("Unsupported wallpaper property list value.");
            if ((long)position + (long)length * _referenceSize * (type == 13 ? 2 : 1) > _table) throw new InvalidDataException("Invalid wallpaper collection.");
            var values = new object?[length];
            for (var i = 0; i < length; i++) values[i] = Object(checked((int)Unsigned(position + i * _referenceSize, _referenceSize)), depth + 1);
            if (type == 10) return values;
            var dictionary = new Dictionary<string, object?>(StringComparer.Ordinal);
            for (var i = 0; i < length; i++)
            {
                if (values[i] is not string key) throw new InvalidDataException("Invalid wallpaper dictionary key.");
                dictionary[key] = Object(checked((int)Unsigned(position + (length + i) * _referenceSize, _referenceSize)), depth + 1);
            }
            return dictionary;
        }
        finally { _reading.Remove(id); }
    }
}
