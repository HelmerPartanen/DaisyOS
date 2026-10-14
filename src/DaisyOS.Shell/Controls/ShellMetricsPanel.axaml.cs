using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using DaisyOS.Shell.Services.Diagnostics;

namespace DaisyOS.Shell.Controls;

public partial class ShellMetricsPanel : UserControl
{
    public event EventHandler<bool>? PanelVisibilityChanged;
    public static readonly StyledProperty<bool> IsPanelOpenProperty =
        AvaloniaProperty.Register<ShellMetricsPanel, bool>(nameof(IsPanelOpen), false);

    public bool IsPanelOpen
    {
        get => GetValue(IsPanelOpenProperty);
        set => SetValue(IsPanelOpenProperty, value);
    }

    private ShellMetricsMonitor? _monitor;
    private DispatcherTimer? _timer;
    private string _currentSort = "Cpu";

    static ShellMetricsPanel()
    {
        IsPanelOpenProperty.Changed.AddClassHandler<ShellMetricsPanel>((x, e) => x.UpdateVisibility(e.GetNewValue<bool>()));
    }

    public ShellMetricsPanel()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    public void InitializeMonitor(ShellMetricsMonitor monitor)
    {
        _monitor = monitor;
        if (IsPanelOpen)
        {
            RefreshMetrics();
        }
    }

    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        _monitor ??= new ShellMetricsMonitor();
        StartTimer();
        UpdateVisibility(IsPanelOpen);
    }

    private void OnUnloaded(object? sender, RoutedEventArgs e)
    {
        StopTimer();
    }

    public void TogglePanel()
    {
        IsPanelOpen = !IsPanelOpen;
    }

    public void ShowPanel()
    {
        IsPanelOpen = true;
    }

    public void HidePanel()
    {
        IsPanelOpen = false;
    }

    private void StartTimer()
    {
        if (_timer is null)
        {
            _timer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(1000)
            };
            _timer.Tick += (s, ev) => RefreshMetrics();
        }

        if (!_timer.IsEnabled)
        {
            _timer.Start();
            RefreshMetrics();
        }
    }

    private void StopTimer()
    {
        if (_timer is not null && _timer.IsEnabled)
        {
            _timer.Stop();
        }
    }

    private void UpdateVisibility(bool isVisible)
    {
        IsVisible = isVisible;
        PanelVisibilityChanged?.Invoke(this, isVisible);
        if (isVisible)
        {
            StartTimer();
        }
        else
        {
            StopTimer();
        }
    }

    private void OnCloseClicked(object? sender, RoutedEventArgs e)
    {
        HidePanel();
    }

    private void OnSortCpuClicked(object? sender, RoutedEventArgs e)
    {
        SetSortMode("Cpu");
    }

    private void OnSortMemoryClicked(object? sender, RoutedEventArgs e)
    {
        SetSortMode("Memory");
    }

    private void OnSortComplexityClicked(object? sender, RoutedEventArgs e)
    {
        SetSortMode("Complexity");
    }

    private void SetSortMode(string mode)
    {
        _currentSort = mode;

        var btnCpu = this.FindControl<Button>("BtnSortCpu");
        var btnMem = this.FindControl<Button>("BtnSortMemory");
        var btnComp = this.FindControl<Button>("BtnSortComplexity");

        if (btnCpu != null) UpdateChipStyle(btnCpu, mode == "Cpu");
        if (btnMem != null) UpdateChipStyle(btnMem, mode == "Memory");
        if (btnComp != null) UpdateChipStyle(btnComp, mode == "Complexity");

        RefreshMetrics();
    }

    private static void UpdateChipStyle(Button button, bool isActive)
    {
        if (isActive)
        {
            button.Classes.Add("Active");
        }
        else
        {
            button.Classes.Remove("Active");
        }
    }

    public void RefreshMetrics()
    {
        if (_monitor is null || !IsVisible) return;

        try
        {
            var snapshot = _monitor.Sample(_currentSort);

            // Update Shell Summary Header & Cards
            var txtPid = this.FindControl<TextBlock>("TxtPid");
            var txtCpu = this.FindControl<TextBlock>("TxtShellCpu");
            var txtGc = this.FindControl<TextBlock>("TxtGcRam");
            var txtWs = this.FindControl<TextBlock>("TxtWorkingSet");
            var txtThreads = this.FindControl<TextBlock>("TxtThreads");
            var txtHandles = this.FindControl<TextBlock>("TxtHandles");
            var txtUptime = this.FindControl<TextBlock>("TxtUptime");
            var iconCpu = this.FindControl<DaisyIcon>("IconCpu");

            if (txtPid != null) txtPid.Text = Loc.Format("PID {0}", snapshot.ProcessId);
            if (txtCpu != null) txtCpu.Text = Loc.Format("{0:F1}%", snapshot.CpuUsagePercentage);
            if (txtGc != null) txtGc.Text = Loc.Format("{0:F1} MB", snapshot.GcMemoryMB);
            if (txtWs != null) txtWs.Text = Loc.Format("({0:F0} MB WS)", snapshot.WorkingSetMB);
            if (txtThreads != null) txtThreads.Text = Loc.Format("{0} thr", snapshot.ThreadCount);
            if (txtHandles != null) txtHandles.Text = Loc.Format("{0} hnd", snapshot.HandleCount);
            if (txtUptime != null) txtUptime.Text = Loc.Format("{0:hh\\:mm\\:ss}", snapshot.Uptime);

            if (iconCpu != null)
            {
                var resourceKey = snapshot.CpuUsagePercentage > 60.0
                    ? "StatusDangerBrush"
                    : snapshot.CpuUsagePercentage > 25.0
                        ? "StatusWarningBrush"
                        : "StatusSuccessBrush";
                iconCpu.Foreground = this.FindResource(resourceKey) as IBrush;
            }

            // Render Component Breakdown Cards
            var container = this.FindControl<StackPanel>("ComponentsContainer");
            if (container == null) return;

            container.Children.Clear();

            double maxVal = _currentSort switch
            {
                "Memory" => snapshot.Components.Max(c => c.EstimatedMemoryMB),
                "Complexity" => snapshot.Components.Max(c => (double)c.VisualElementCount),
                _ => snapshot.Components.Max(c => c.RelativeCpuPercent)
            };
            if (maxVal <= 0) maxVal = 1.0;

            int rank = 1;
            foreach (var comp in snapshot.Components)
            {
                var card = CreateComponentCard(comp, rank++, maxVal);
                container.Children.Add(card);
            }
        }
        catch
        {
            // Robust against transient sampling updates
        }
    }

    private Control CreateComponentCard(ShellComponentMetrics comp, int rank, double maxVal)
    {
        var border = new Border
        {
            Background = this.FindResource("StateSelectedBrush") as IBrush,
            BorderBrush = this.FindResource("StrokeDefaultBrush") as IBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = CornerRadius.Parse("8"),
            Padding = new Thickness(10, 8)
        };

        var mainGrid = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,Auto"),
            ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto")
        };

        // Rank & Icon Badge
        var iconBorder = new Border
        {
            Background = this.FindResource("SurfaceSunkenBrush") as IBrush,
            CornerRadius = CornerRadius.Parse("6"),
            Padding = new Thickness(6),
            Margin = new Thickness(0, 0, 10, 0),
            VerticalAlignment = VerticalAlignment.Center
        };

        var iconText = new DaisyIcon
        {
            Icon = string.IsNullOrWhiteSpace(comp.Icon) ? "squares-four" : comp.Icon,
            Size = 18,
            Foreground = this.FindResource("ActionPrimaryBrush") as IBrush,
            VerticalAlignment = VerticalAlignment.Center
        };
        iconBorder.Child = iconText;
        Grid.SetRow(iconBorder, 0);
        Grid.SetColumn(iconBorder, 0);
        mainGrid.Children.Add(iconBorder);

        // Name, Category & Status Tag
        var nameStack = new StackPanel
        {
            Spacing = 2,
            VerticalAlignment = VerticalAlignment.Center
        };

        var titleRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6
        };

        var rankText = DaisyOS.Shell.Services.Theming.TypographyPolicy.Bind(new TextBlock
        {
            Text = $"#{rank}",
            FontSize = 11,
            FontWeight = FontWeight.Bold,
            Foreground = this.FindResource("ContentTertiaryBrush") as IBrush,
            VerticalAlignment = VerticalAlignment.Center
        }, "Caption");

        var nameText = DaisyOS.Shell.Services.Theming.TypographyPolicy.Bind(new TextBlock
        {
            Text = comp.Name,
            FontSize = 13,
            FontWeight = FontWeight.SemiBold,
            Foreground = this.FindResource("ContentPrimaryBrush") as IBrush,
            VerticalAlignment = VerticalAlignment.Center
        }, "BodySmall");

        titleRow.Children.Add(rankText);
        titleRow.Children.Add(nameText);
        nameStack.Children.Add(titleRow);

        var detailsRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8
        };

        var catText = DaisyOS.Shell.Services.Theming.TypographyPolicy.Bind(new TextBlock
        {
            Text = comp.Category,
            FontSize = 10,
            Foreground = this.FindResource("ContentTertiaryBrush") as IBrush
        }, "Caption");

        var statusBorder = new Border
        {
            CornerRadius = CornerRadius.Parse("4"),
            Padding = new Thickness(4, 1),
            Background = comp.Status switch
            {
                "Active" => this.FindResource("StatusSuccessContainerBrush") as IBrush,
                "Idle" => this.FindResource("StatusWarningContainerBrush") as IBrush,
                _ => this.FindResource("SurfaceSunkenBrush") as IBrush
            }
        };
        var statusText = DaisyOS.Shell.Services.Theming.TypographyPolicy.Bind(new TextBlock
        {
            Text = comp.Status,
            FontSize = 9,
            FontWeight = FontWeight.Bold,
            Foreground = comp.Status switch
            {
                "Active" => this.FindResource("StatusSuccessBrush") as IBrush,
                "Idle" => this.FindResource("StatusWarningBrush") as IBrush,
                _ => this.FindResource("ContentTertiaryBrush") as IBrush
            }
        }, "Caption");
        statusBorder.Child = statusText;

        detailsRow.Children.Add(catText);
        detailsRow.Children.Add(statusBorder);
        nameStack.Children.Add(detailsRow);

        Grid.SetRow(nameStack, 0);
        Grid.SetColumn(nameStack, 1);
        mainGrid.Children.Add(nameStack);

        // Numeric Metrics (CPU %, RAM MB, Elements)
        var metricsStack = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            Spacing = 1
        };

        var primaryMetric = DaisyOS.Shell.Services.Theming.TypographyPolicy.Bind(new TextBlock
        {
            FontSize = 13,
            FontWeight = FontWeight.Bold,
            HorizontalAlignment = HorizontalAlignment.Right,
            Foreground = this.FindResource("ContentPrimaryBrush") as IBrush
        }, "BodySmall");

        var secondaryMetric = DaisyOS.Shell.Services.Theming.TypographyPolicy.Bind(new TextBlock
        {
            FontSize = 10,
            HorizontalAlignment = HorizontalAlignment.Right,
            Foreground = this.FindResource("ContentSecondaryBrush") as IBrush
        }, "Caption");

        if (_currentSort == "Memory")
        {
            primaryMetric.Text = Loc.Format("{0:F1} MB", comp.EstimatedMemoryMB);
            secondaryMetric.Text = Loc.Format("{0:F1}% CPU | {1} elem", comp.RelativeCpuPercent, comp.VisualElementCount);
        }
        else if (_currentSort == "Complexity")
        {
            primaryMetric.Text = Loc.Format("{0} elem", comp.VisualElementCount);
            secondaryMetric.Text = Loc.Format("{0:F1}% CPU | {1:F1} MB", comp.RelativeCpuPercent, comp.EstimatedMemoryMB);
        }
        else
        {
            primaryMetric.Text = Loc.Format("{0:F1}% CPU", comp.RelativeCpuPercent);
            secondaryMetric.Text = Loc.Format("{0:F1} MB | {1} elem", comp.EstimatedMemoryMB, comp.VisualElementCount);
        }

        metricsStack.Children.Add(primaryMetric);
        metricsStack.Children.Add(secondaryMetric);

        Grid.SetRow(metricsStack, 0);
        Grid.SetColumn(metricsStack, 2);
        mainGrid.Children.Add(metricsStack);

        // Progress / Load Bar below
        double ratio = _currentSort switch
        {
            "Memory" => comp.EstimatedMemoryMB / maxVal,
            "Complexity" => comp.VisualElementCount / maxVal,
            _ => comp.RelativeCpuPercent / maxVal
        };
        ratio = Math.Clamp(ratio, 0.04, 1.0);

        var progressTrack = new Border
        {
            Background = this.FindResource("SurfaceSunkenBrush") as IBrush,
            CornerRadius = CornerRadius.Parse("3"),
            Height = 4,
            Margin = new Thickness(0, 8, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Stretch
        };

        var barBrush = comp.RelativeCpuPercent > 20.0
            ? this.FindResource("StatusDangerBrush") as IBrush
            : (comp.RelativeCpuPercent > 5.0
                ? this.FindResource("StatusWarningBrush") as IBrush
                : this.FindResource("ActionPrimaryBrush") as IBrush);

        var progressBar = new Border
        {
            Background = barBrush,
            CornerRadius = CornerRadius.Parse("3"),
            Height = 4,
            HorizontalAlignment = HorizontalAlignment.Left
        };
        // Using RelativePanel or layout percentage via Width fraction
        progressBar.Width = Math.Max(12, 380 * ratio);

        progressTrack.Child = progressBar;
        Grid.SetRow(progressTrack, 1);
        Grid.SetColumn(progressTrack, 0);
        Grid.SetColumnSpan(progressTrack, 3);
        mainGrid.Children.Add(progressTrack);

        border.Child = mainGrid;
        return border;
    }
}
