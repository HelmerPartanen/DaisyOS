using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;

namespace DaisyOS.Shell.Apps.System.Settings;

public partial class SettingsView
{
    private void BuildSearchDiagnostics(int buildVersion)
    {
        if (Application.Current is not App app) { CategoryContent.Children.Add(Secondary(Loc.Get("Search is unavailable in this session."))); return; }
        bool paused = false, refreshing = false;
        var values = new Dictionary<string, TextBlock>();
        Control Row(string title)
        {
            var value = new TextBlock { Classes = { "SettingsValue" } };
            values[title] = value;
            return PreferenceRow(Loc.Get(title), null, value);
        }
        AddSection(Loc.Get("INDEX"), Loc.Get("File names stay on this computer. File contents are not indexed."),
            Row("Index status"), Row("Indexed files and folders"), Row("Scanned entries"), Row("Index size"), Row("Last reconciliation"),
            Row("Active root watchers"), Row("Indexing errors"), Row("Indexed applications"), Row("Registered settings"));
        var pause = new Button { Content = Loc.Get("Pause indexing") };
        var rebuild = new Button { Content = Loc.Get("Reconcile index") };
        pause.IsEnabled = false;
        rebuild.IsEnabled = false;
        CategoryContent.Children.Add(new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 8, Children = { pause, rebuild } });
        AddSection(Loc.Get("SEARCH"), Loc.Get("Latency reflects the latest 256 completed searches."),
            Row("Average search latency"), Row("P95 search latency"), Row("Completed searches"), Row("Cancelled searches"));
        var providers = new TextBlock { TextWrapping = Avalonia.Media.TextWrapping.Wrap };
        CategoryContent.Children.Add(providers);
        async Task RefreshAsync()
        {
            if (refreshing) return;
            refreshing = true;
            try
            {
                var report = await app.GetSearchStatusAsync();
                if (buildVersion != _categoryBuildVersion) return;
                var index = report.Index;
                values["Index status"].Text = Loc.Get(index?.Status ?? "Unavailable");
                values["Indexed files and folders"].Text = (index?.FileCount ?? 0).ToString("N0");
                values["Scanned entries"].Text = (index?.ScannedEntries ?? 0).ToString("N0");
                values["Index size"].Text = $"{(index?.DatabaseBytes ?? 0) / 1048576d:0.0} MB";
                values["Last reconciliation"].Text = index?.LastReconciliation?.ToLocalTime().ToString("g") ?? "—";
                values["Active root watchers"].Text = (index?.WatcherCount ?? 0).ToString();
                values["Indexing errors"].Text = (index?.ErrorCount ?? 0).ToString();
                values["Indexed applications"].Text = report.ApplicationCount.ToString("N0");
                values["Registered settings"].Text = report.SettingCount.ToString("N0");
                pause.Content = Loc.Get(index?.IsPaused == true ? "Resume indexing" : "Pause indexing");
                paused = index?.IsPaused == true;
                pause.IsEnabled = index is not null;
                rebuild.IsEnabled = index is not null;
                var metrics = report.Metrics;
                values["Average search latency"].Text = $"{metrics.AverageMs:0.00} ms";
                values["P95 search latency"].Text = $"{metrics.P95Ms:0.00} ms";
                values["Completed searches"].Text = metrics.CompletedQueries.ToString("N0");
                values["Cancelled searches"].Text = metrics.CancelledQueries.ToString("N0");
                providers.Text = string.Join(Environment.NewLine, global::System.Linq.Enumerable.Select(metrics.Providers,
                    provider => $"{provider.ProviderId}: {provider.Status} · {provider.DurationMs:0.00} ms"));
            }
            catch (Exception)
            {
                values["Index status"].Text = Loc.Get("Unavailable");
                pause.IsEnabled = false; rebuild.IsEnabled = false;
            }
            finally { refreshing = false; }
        }
        pause.Click += async (_, _) =>
        {
            pause.IsEnabled = false;
            try { await app.SetSearchPausedAsync(!paused); }
            catch (Exception) { values["Index status"].Text = Loc.Get("The index could not be updated."); }
            await RefreshAsync();
        };
        rebuild.Click += async (_, _) =>
        {
            rebuild.IsEnabled = false;
            try { await app.ReconcileSearchAsync(); }
            catch (Exception) { values["Index status"].Text = Loc.Get("Reconciliation could not be requested."); }
            await RefreshAsync();
        };
        _ = RefreshAsync();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        timer.Tick += (_, _) => { if (buildVersion != _categoryBuildVersion) timer.Stop(); else _ = RefreshAsync(); };
        providers.AttachedToVisualTree += (_, _) => timer.Start();
        providers.DetachedFromVisualTree += (_, _) => timer.Stop();
    }
}
