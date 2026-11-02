using System;
using System.Collections.Generic;
using System.Linq;

namespace DaisyOS.Core.Services;

public sealed record ClipboardItem(
    string Id,
    string Text,
    string Preview,
    DateTimeOffset Timestamp,
    bool IsPinned = false);

public sealed class ClipboardHistoryService
{
    private const int MaxHistoryCount = 50;
    private readonly List<ClipboardItem> _items = new();
    private readonly object _lock = new();

    public static ClipboardHistoryService Instance { get; } = new();

    public event EventHandler? HistoryChanged;

    public IReadOnlyList<ClipboardItem> GetItems()
    {
        lock (_lock)
        {
            return _items.OrderByDescending(i => i.IsPinned)
                         .ThenByDescending(i => i.Timestamp)
                         .ToList();
        }
    }

    public void AddClip(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return;

        lock (_lock)
        {
            // Remove existing duplicate if not pinned
            var existingIdx = _items.FindIndex(i => string.Equals(i.Text, text, StringComparison.Ordinal));
            bool wasPinned = false;
            if (existingIdx >= 0)
            {
                wasPinned = _items[existingIdx].IsPinned;
                _items.RemoveAt(existingIdx);
            }

            var preview = text.Length > 200 ? text[..197] + "..." : text;
            preview = preview.Replace('\r', ' ').Replace('\n', ' ');

            var item = new ClipboardItem(
                Guid.NewGuid().ToString("N"),
                text,
                preview,
                DateTimeOffset.Now,
                wasPinned);

            _items.Insert(0, item);

            // Trim unpinned items beyond limit
            while (_items.Count > MaxHistoryCount)
            {
                var lastUnpinned = _items.FindLastIndex(i => !i.IsPinned);
                if (lastUnpinned >= 0)
                {
                    _items.RemoveAt(lastUnpinned);
                }
                else
                {
                    break;
                }
            }
        }

        HistoryChanged?.Invoke(this, EventArgs.Empty);
    }

    public void RemoveClip(string id)
    {
        lock (_lock)
        {
            _items.RemoveAll(i => i.Id == id);
        }
        HistoryChanged?.Invoke(this, EventArgs.Empty);
    }

    public void TogglePin(string id)
    {
        lock (_lock)
        {
            var idx = _items.FindIndex(i => i.Id == id);
            if (idx >= 0)
            {
                var cur = _items[idx];
                _items[idx] = cur with { IsPinned = !cur.IsPinned };
            }
        }
        HistoryChanged?.Invoke(this, EventArgs.Empty);
    }

    public void ClearAll()
    {
        lock (_lock)
        {
            _items.RemoveAll(i => !i.IsPinned);
        }
        HistoryChanged?.Invoke(this, EventArgs.Empty);
    }
}

