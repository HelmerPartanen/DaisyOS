using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using DaisyOS.Shell.Controls;
using Xunit;

namespace DaisyOS.ShellDemo.Tests;

public sealed class ComboBoxBehaviorTests
{
    private sealed class CountedLabel(string label)
    {
        public int Reads { get; private set; }
        public override string ToString() { Reads++; return label; }
    }

    [AvaloniaFact]
    public void CatalogPopulationMeasuresOnceAndLeavesWindowClosable()
    {
        var picker = new ComboBox();
        ComboBoxBehavior.SetUseAdaptiveWidth(picker, true);
        var window = new Window { Width = 700, Height = 400, Content = picker };
        window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();
            var labels = Enumerable.Range(0, 400).Select(index => new CountedLabel($"Font family {index}")).ToArray();
            foreach (var label in labels) picker.Items.Add(label);
            Dispatcher.UIThread.RunJobs();
            // No selected item or open popup: each string is read by the width probe.
            Assert.All(labels, label => Assert.InRange(label.Reads, 1, 2));
            Assert.True(picker.MinWidth > 0);
            var extra = new CountedLabel("A much longer font family name");
            picker.Items.Add(extra);
            Dispatcher.UIThread.RunJobs();
            Assert.InRange(extra.Reads, 1, 2);
            var closed = false;
            window.Closed += (_, _) => closed = true;
            window.Close();
            Assert.True(closed);
        }
        finally { window.Close(); }
    }
}
