using System.Diagnostics;

namespace DaisyOS.Shell.Apps.Mail;

/// <summary>Desktop notifications and headless background sync for new mail.</summary>
public static class MailNotifier
{
    public static void Notify(MailAccount account, IReadOnlyList<MailItem> fresh)
    {
        if (fresh.Count == 0) return;
        var title = fresh.Count == 1 ? fresh[0].Sender : fresh.Count + " new messages";
        var body = fresh.Count == 1 ? fresh[0].Subject : string.Join("\n", fresh.Take(3).Select(i => i.Sender + ": " + i.Subject));
        try
        {
            var info = new ProcessStartInfo("notify-send") { UseShellExecute = false, CreateNoWindow = true };
            foreach (var arg in new[] { "--app-name=DaisyOS Mail", "--icon=mail-unread", title + " · " + account.Email, body }) info.ArgumentList.Add(arg);
            Process.Start(info)?.Dispose();
        }
        catch { /* Notifications are best-effort. */ }
    }

    /// <summary>Syncs every account's inbox without opening a window and notifies about new unread mail.</summary>
    public static async Task<int> BackgroundCheckAsync(CancellationToken ct = default)
    {
        var storage = new MailStorage();
        await using var service = new MailService(storage);
        foreach (var account in storage.Read("accounts", MailJsonContext.Default.ListMailAccount, []))
        {
            try
            {
                var path = storage.Read("folders-" + account.Id, MailJsonContext.Default.ListMailFolder, []).FirstOrDefault()?.Path ?? "INBOX";
                var key = MailStorage.CacheKey(account.Id, path);
                var before = storage.Read(key, MailJsonContext.Default.ListMailItem, []);
                var known = before.Count == 0 ? 0u : before.Max(i => i.Uid);
                var items = await service.FetchAsync(account, path, 100, ct);
                if (known > 0) Notify(account, items.Where(i => i.Uid > known && i.Unread).ToList());
            }
            catch (Exception ex) when (ex is not OperationCanceledException) { Console.Error.WriteLine("Mail check failed for " + account.Email + ": " + ex.Message); }
        }
        return 0;
    }
}
