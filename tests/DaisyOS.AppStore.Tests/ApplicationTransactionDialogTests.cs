using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using DaisyOS.Core.Models;
using DaisyOS.Shell.Views.Dialogs;
using Xunit;

namespace DaisyOS.ShellDemo.Tests;
public sealed class ApplicationTransactionDialogTests
{
    [AvaloniaFact]
    public void RemovalDefaultsToKeepingDataAndUsesSmallActions()
    {
        var dialog = new ApplicationTransactionDialog(new(ApplicationOperation.Uninstall,
            new(new(ApplicationBackend.Container,"example","user","daisy-app-example"),"Example")));
        dialog.Show();
        try
        {
            dialog.UpdateLayout();
            Assert.False(dialog.RemoveData);
            Assert.Single(dialog.GetVisualDescendants().OfType<CheckBox>());
            var actions=dialog.GetVisualDescendants().OfType<Button>().Where(button=>button.Content is string).ToArray();
            Assert.Contains(actions,button=>Equals(button.Content,"Uninstall")&&button.Classes.Contains("Small"));
            Assert.Contains(actions,button=>Equals(button.Content,"Cancel")&&button.Classes.Contains("Small"));
        }
        finally { dialog.Close(); }
    }
    [AvaloniaFact]
    public void ProtectedPackageCannotBeConfirmed()
    {
        var dialog = new ApplicationTransactionDialog(new(ApplicationOperation.Uninstall,
            new(new(ApplicationBackend.Pacman,"kwin","system"),"KWin") {IsProtected=true,ProtectionReason="Required for the desktop"}));
        dialog.Show();
        try { dialog.UpdateLayout(); Assert.False(dialog.GetVisualDescendants().OfType<Button>().Single(button=>Equals(button.Content,"Uninstall")).IsEnabled); }
        finally { dialog.Close(); }
    }
}
