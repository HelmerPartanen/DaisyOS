using System;
using System.Linq;
using Avalonia.Controls;
using DaisyOS.Shell.Services.Diagnostics;
using Xunit;

namespace DaisyOS.Shell.Tests;

public class ShellMetricsMonitorTests
{
    [Fact]
    public void Sample_ReturnsValidShellMetricsSnapshot()
    {
        var monitor = new ShellMetricsMonitor();
        var snapshot = monitor.Sample();

        Assert.NotNull(snapshot);
        Assert.Equal("DaisyOS.Shell", snapshot.ProcessName);
        Assert.True(snapshot.ProcessId > 0);
        Assert.True(snapshot.GcMemoryMB >= 0);
        Assert.True(snapshot.WorkingSetMB >= 0);
        Assert.True(snapshot.ThreadCount >= 0);
    }

    [Fact]
    public void RegisterComponent_CalculatesComponentMetricsAndElementCount()
    {
        var monitor = new ShellMetricsMonitor();
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock());
        panel.Children.Add(new Button());

        monitor.RegisterComponent(
            "test_taskbar", "Taskbar", "dock", "Navigation",
            () => panel,
            activeCheck: () => true,
            customMemoryMbProvider: () => 5.5,
            customCpuWeightProvider: () => 2.0);

        var snapshot = monitor.Sample();
        var comp = snapshot.Components.FirstOrDefault(c => c.Id == "test_taskbar");

        Assert.NotNull(comp);
        Assert.Equal("Taskbar", comp.Name);
        Assert.Equal("dock", comp.Icon);
        Assert.Equal("Navigation", comp.Category);
        Assert.Equal("Active", comp.Status);
        Assert.Equal(5.5, comp.EstimatedMemoryMB);
        Assert.True(comp.VisualElementCount >= 1);
    }

    [Fact]
    public void Sample_SortsByCpuMemoryAndComplexity()
    {
        var monitor = new ShellMetricsMonitor();

        monitor.RegisterComponent(
            "comp_a", "Component A", "apps", "Category 1",
            () => new Border(),
            customMemoryMbProvider: () => 10.0,
            customCpuWeightProvider: () => 0.1);

        monitor.RegisterComponent(
            "comp_b", "Component B", "dock", "Category 2",
            () => new Border(),
            customMemoryMbProvider: () => 2.0,
            customCpuWeightProvider: () => 5.0);

        // Sort by Memory
        var memSnapshot = monitor.Sample("Memory");
        Assert.Equal("comp_a", memSnapshot.Components.First().Id);

        // Sort by CPU
        var cpuSnapshot = monitor.Sample("Cpu");
        Assert.Equal("comp_b", cpuSnapshot.Components.First().Id);
    }

    [Fact]
    public void CountVisualElements_ReturnsCorrectCountForNullAndControls()
    {
        Assert.Equal(0, ShellMetricsMonitor.CountVisualElements(null));
        var border = new Border();
        Assert.True(ShellMetricsMonitor.CountVisualElements(border) >= 1);
    }
}
