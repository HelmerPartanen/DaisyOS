namespace DaisyOS.Shell.Apps.Files.Services;

// Cache passive indexes, never live watchers or old cancellation sources.
internal sealed class RecentDirectoryCache(int capacity = 4)
{
    private readonly LinkedList<(string Path, DirectoryIndex Index)> _items = [];
    public void Store(string path, DirectoryIndex index)
    {
        for (var node = _items.First; node != null; node = node.Next)
            if (node.Value.Path == path) { _items.Remove(node); break; }
        _items.AddFirst((path, index));
        while (_items.Count > capacity || _items.Sum(e => e.Index.Entries.Count) > 200000) _items.RemoveLast();
    }
    public DirectoryIndex? Take(string path)
    {
        for (var node = _items.First; node != null; node = node.Next)
            if (node.Value.Path == path)
            {
                _items.Remove(node);
                return node.Value.Index;
            }
        return null;
    }
}
