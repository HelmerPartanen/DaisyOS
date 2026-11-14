using Avalonia;
using Avalonia.Controls;
using DaisyOS.Core.Models;
using DaisyOS.Shell.Services.Localization;
using DaisyOS.Shell.Views.Dialogs;
namespace DaisyOS.Shell.Services;
internal static class ApplicationManagementUi
{
    public static async Task<bool> RemoveAsync(InstalledApplicationService service, AppEntry app, Window? owner)
    {
        try
        {
            var target=await service.ResolveAsync(app);
            if(!target.CanManage) { (Application.Current as App)?.Feedback.Show(target.UnavailableReason);return false; }
            var dialog=new ApplicationTransactionDialog(await service.PrepareUninstallAsync(target));
            if(!await dialog.ConfirmAsync(owner))return false;
            var result=await service.ExecuteUninstallAsync(dialog.Plan,dialog.RemoveData);
            (Application.Current as App)?.Feedback.Show(Loc.Get(result.Message));
            if(result.Details.Length>0&&!result.Succeeded&&owner is {IsVisible:true})
                await new ConfirmationDialog(Loc.Get("Application removal"),Loc.Get(result.Message),result.Details,Loc.Get("Close")).ShowDialog<bool>(owner);
            return result.Succeeded;
        }
        catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or InvalidOperationException or global::System.Text.Json.JsonException)
        { (Application.Current as App)?.Feedback.Show(Loc.Get("The application could not be removed.")+" "+ex.Message);return false; }
    }
}
