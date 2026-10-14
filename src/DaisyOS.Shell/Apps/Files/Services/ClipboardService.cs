using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input.Platform;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using System.IO;
using System.Text;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace DaisyOS.Shell.Apps.Files.Services;

public enum ClipboardOperation
{
    None,
    Copy,
    Cut
}

public static class ClipboardService
{
    internal static readonly DataFormat<byte[]> GnomeFilesFormat = DataFormat.CreateBytesPlatformFormat("x-special/gnome-copied-files");
    internal static readonly DataFormat<byte[]> KdeCutFormat = DataFormat.CreateBytesPlatformFormat("application/x-kde-cutselection");
    public static List<string> CurrentFiles { get; private set; } = new();
    public static ClipboardOperation CurrentOperation { get; private set; } = ClipboardOperation.None;

    public static async Task SetFilesAsync(IEnumerable<string> paths, ClipboardOperation operation,
        IClipboard? clipboard = null, IStorageProvider? storageProvider = null)
    {
        clipboard ??= GetClipboard();
        storageProvider ??= GetMainWindow()?.StorageProvider;
        if (clipboard is null || storageProvider is null)
            throw new InvalidOperationException("The system file clipboard is unavailable.");
        var localPaths = paths.Select(Path.GetFullPath).Distinct(StringComparer.Ordinal).ToList();
        var items = new List<IStorageItem>();
        foreach (var path in localPaths)
        {
            IStorageItem? item = Directory.Exists(path)
                ? await storageProvider.TryGetFolderFromPathAsync(path)
                : await storageProvider.TryGetFileFromPathAsync(path);
            items.Add(item ?? throw new IOException($"Could not copy '{path}' to the file clipboard."));
        }
        await clipboard.SetDataAsync(CreateTransfer(items, operation));
        CurrentFiles = localPaths;
        CurrentOperation = operation;
    }

    internal static DataTransfer CreateTransfer(IEnumerable<IStorageItem> files, ClipboardOperation operation)
    {
        var items = files.ToArray();
        var transfer = new DataTransfer();
        foreach (var file in items) transfer.Add(DataTransferItem.CreateFile(file));
        var metadata = new DataTransferItem();
        metadata.Set(GnomeFilesFormat, Encoding.UTF8.GetBytes(
            (operation == ClipboardOperation.Cut ? "cut\n" : "copy\n") +
            string.Join("\n", items.Select(file => file.Path.AbsoluteUri))));
        metadata.Set(KdeCutFormat, Encoding.UTF8.GetBytes(operation == ClipboardOperation.Cut ? "1" : "0"));
        transfer.Add(metadata);
        return transfer;
    }

    public static async Task<(List<string> files, ClipboardOperation operation)> GetFilesAsync(IClipboard? clipboard = null)
    {
        clipboard ??= GetClipboard();
        if (clipboard is null) return ([], ClipboardOperation.None);
        using var data = await clipboard.TryGetDataAsync();
        if (data is null) return ([], ClipboardOperation.None);
        return await ReadTransferAsync(data);
    }

    internal static async Task<(List<string> files, ClipboardOperation operation)> ReadTransferAsync(IAsyncDataTransfer data)
    {
        var files = (await data.TryGetFilesAsync() ?? [])
            .Select(file => file.TryGetLocalPath()).Where(path => !string.IsNullOrWhiteSpace(path))
            .Cast<string>().Distinct(StringComparer.Ordinal).ToList();
        var gnome = await data.TryGetValueAsync(GnomeFilesFormat);
        var lines = gnome is null ? [] : Encoding.UTF8.GetString(gnome)
            .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        if (files.Count == 0 && lines.Length > 1)
            files = lines.Skip(1).Select(line => Uri.TryCreate(line, UriKind.Absolute, out var uri) && uri.IsFile &&
                    (uri.Host.Length == 0 || uri.IsLoopback) ? uri.LocalPath : null)
                .Where(path => path is not null).Cast<string>().Distinct(StringComparer.Ordinal).ToList();
        if (files.Count == 0) return ([], ClipboardOperation.None);
        var kdeCut = await data.TryGetValueAsync(KdeCutFormat);
        var cut = lines.FirstOrDefault() == "cut" || (kdeCut is not null && Encoding.UTF8.GetString(kdeCut).Trim() == "1");
        return (files, cut ? ClipboardOperation.Cut : ClipboardOperation.Copy);
    }

    public static void ClearInternal()
    {
        CurrentFiles.Clear();
        CurrentOperation = ClipboardOperation.None;
    }

    private static Avalonia.Controls.Window? GetMainWindow() =>
        (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
    private static IClipboard? GetClipboard() => GetMainWindow()?.Clipboard;
}
