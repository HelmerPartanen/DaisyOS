using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;

namespace DaisyOS.Shell.Services.Diagnostics;

public class ShellComponentMetrics
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Icon { get; set; } = "widgets";
    public string Category { get; set; } = "Component";
    public string Status { get; set; } = "Active";
    public bool IsVisible { get; set; } = true;
    public int VisualElementCount { get; set; }
    public double EstimatedMemoryMB { get; set; }
    public double RelativeCpuPercent { get; set; }
    public double ImpactScore { get; set; }
}

public class ShellMetricsSnapshot
{
    public int ProcessId { get; set; }
    public string ProcessName { get; set; } = "DaisyOS.Shell";
    public double CpuUsagePercentage { get; set; }
    public double WorkingSetMB { get; set; }
    public double PrivateMemoryMB { get; set; }
    public double GcMemoryMB { get; set; }
    public int ThreadCount { get; set; }
    public int HandleCount { get; set; }
    public TimeSpan Uptime { get; set; }
    public List<ShellComponentMetrics> Components { get; set; } = new();
}

public class ComponentRegistration
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Icon { get; set; } = "widgets";
    public string Category { get; set; } = "Component";
    public Func<Control?>? ControlProvider { get; set; }
    public Func<bool>? ActiveCheck { get; set; }
    public Func<double>? CustomMemoryMbProvider { get; set; }
    public Func<double>? CustomCpuWeightProvider { get; set; }
}

public class ShellMetricsMonitor
{
    private readonly Process _process;
    private readonly AppPerformanceMonitor _baseMonitor;
    private readonly List<ComponentRegistration> _registrations = new();

    public ShellMetricsMonitor(Process? process = null)
    {
        _process = process ?? Process.GetCurrentProcess();
        _baseMonitor = new AppPerformanceMonitor(_process);
    }

    public void RegisterComponent(
        string id,
        string name,
        string icon,
        string category,
        Func<Control?> controlProvider,
        Func<bool>? activeCheck = null,
        Func<double>? customMemoryMbProvider = null,
        Func<double>? customCpuWeightProvider = null)
    {
        _registrations.RemoveAll(r => r.Id == id);
        _registrations.Add(new ComponentRegistration
        {
            Id = id,
            Name = name,
            Icon = icon,
            Category = category,
            ControlProvider = controlProvider,
            ActiveCheck = activeCheck,
            CustomMemoryMbProvider = customMemoryMbProvider,
            CustomCpuWeightProvider = customCpuWeightProvider
        });
    }

    public ShellMetricsSnapshot Sample(string sortBy = "Cpu")
    {
        var appMetrics = _baseMonitor.Sample();
        var components = new List<ShellComponentMetrics>();

        double totalCpuUsage = appMetrics.CpuUsagePercentage;

        double sumWeight = 0;
        var tempMetrics = new List<(ComponentRegistration Reg, int ElementCount, bool IsVisible, string Status, double MemMb, double RawWeight)>();

        foreach (var reg in _registrations)
        {
            var control = reg.ControlProvider?.Invoke();
            bool isVisible = control?.IsVisible ?? true;
            bool isActive = reg.ActiveCheck?.Invoke() ?? isVisible;

            string status = !isVisible ? "Hidden" : (isActive ? "Active" : "Idle");

            int elementCount = 0;
            if (control != null)
            {
                elementCount = CountVisualElements(control);
            }

            double memMb = reg.CustomMemoryMbProvider?.Invoke() ?? (elementCount * 0.008 + (isVisible ? 1.2 : 0.2));

            double customWeight = reg.CustomCpuWeightProvider?.Invoke() ?? 1.0;
            double activityMultiplier = !isVisible ? 0.05 : (isActive ? 1.0 : 0.2);
            double rawWeight = Math.Max(1, elementCount) * activityMultiplier * customWeight;

            sumWeight += rawWeight;
            tempMetrics.Add((reg, elementCount, isVisible, status, memMb, rawWeight));
        }

        if (sumWeight <= 0) sumWeight = 1.0;

        foreach (var item in tempMetrics)
        {
            double cpuShare = totalCpuUsage * (item.RawWeight / sumWeight);
            cpuShare = Math.Round(cpuShare, 2);
            double memMb = Math.Round(item.MemMb, 1);

            double impact = cpuShare * 10 + memMb;

            components.Add(new ShellComponentMetrics
            {
                Id = item.Reg.Id,
                Name = item.Reg.Name,
                Icon = item.Reg.Icon,
                Category = item.Reg.Category,
                Status = item.Status,
                IsVisible = item.IsVisible,
                VisualElementCount = item.ElementCount,
                EstimatedMemoryMB = memMb,
                RelativeCpuPercent = cpuShare,
                ImpactScore = Math.Round(impact, 2)
            });
        }

        components = sortBy.ToLowerInvariant() switch
        {
            "memory" => components.OrderByDescending(c => c.EstimatedMemoryMB).ThenByDescending(c => c.RelativeCpuPercent).ToList(),
            "elements" or "complexity" => components.OrderByDescending(c => c.VisualElementCount).ThenByDescending(c => c.RelativeCpuPercent).ToList(),
            "name" => components.OrderBy(c => c.Name).ToList(),
            _ => components.OrderByDescending(c => c.RelativeCpuPercent).ThenByDescending(c => c.EstimatedMemoryMB).ToList()
        };

        return new ShellMetricsSnapshot
        {
            ProcessId = appMetrics.ProcessId,
            ProcessName = "DaisyOS.Shell",
            CpuUsagePercentage = Math.Round(appMetrics.CpuUsagePercentage, 1),
            WorkingSetMB = Math.Round(appMetrics.WorkingSetMB, 1),
            PrivateMemoryMB = Math.Round(appMetrics.PrivateMemoryMB, 1),
            GcMemoryMB = Math.Round(appMetrics.GcMemoryMB, 1),
            ThreadCount = appMetrics.ThreadCount,
            HandleCount = appMetrics.HandleCount,
            Uptime = appMetrics.Uptime,
            Components = components
        };
    }

    public static int CountVisualElements(Visual? root)
    {
        if (root is null) return 0;

        int count = 1;
        try
        {
            foreach (var child in root.GetVisualChildren())
            {
                count += CountVisualElements(child);
            }
        }
        catch
        {
            // Fallback for visual tree detachment during layout
        }

        return count;
    }
}
