using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using DaisyOS.Core.Models;
using DaisyOS.Shell.Services.Localization;
namespace DaisyOS.Shell.Views.Dialogs;

internal sealed class ApplicationTransactionDialog : Window
{
    private bool _accepted;
    private readonly CheckBox _removeData = new() { Content = Loc.Get("Also delete this app’s saved data"), IsChecked = false };
    public ApplicationTransactionPlan Plan { get; private set; }
    public bool RemoveData => _removeData.IsChecked == true;
    public ApplicationTransactionDialog(ApplicationTransactionPlan plan)
    {
        Plan=plan;
        var repairing=plan.Operation==ApplicationOperation.Repair;
        var removing=plan.Operation==ApplicationOperation.Uninstall;
        Title=Loc.Get(removing?"Uninstall application":repairing?"Repair application":"Install application");Width=480;SizeToContent=SizeToContent.Height;
        CanResize=false;CanMinimize=false;CanMaximize=false;ShowInTaskbar=false;WindowStartupLocation=WindowStartupLocation.CenterOwner;
        this.Bind(BackgroundProperty,this.GetResourceObservable("SurfaceBaseSolidBrush"));
        var accept=new Button { Content=Loc.Get(removing?"Uninstall":repairing?"Repair":"Install"),Classes={removing?"Danger":"Primary","Small"},IsEnabled=plan.CanExecute };
        var cancel=new Button { Content=Loc.Get("Cancel"),Classes={"Secondary","Small"},IsCancel=true,IsDefault=true };
        accept.Click+=(_,_)=>{_accepted=true;Close(true);};cancel.Click+=(_,_)=>Close(false);
        var content=new StackPanel { Margin=new Thickness(24),Spacing=16 };
        content.Children.Add(new TextBlock { Text=plan.Application.Name,FontSize=20,FontWeight=FontWeight.SemiBold,TextWrapping=TextWrapping.Wrap });
        content.Children.Add(new TextBlock { Text=$"{plan.Application.Identity.Backend} · {plan.Application.Identity.Scope}",TextWrapping=TextWrapping.Wrap });
        content.Children.Add(new TextBlock { Text=Loc.Get(removing?"The application and its shortcuts will be removed. Your saved data and personal documents are kept unless you choose otherwise.":
            plan.Application.Identity.Backend==ApplicationBackend.Container?"This application will use its own compatibility environment. The first installation may download additional system components.":"Review this application before installing. Native packages may request administrator authentication."),TextWrapping=TextWrapping.Wrap });
        if(plan.AffectedPackages.Length>0)
        {
            content.Children.Add(new TextBlock { Text=Loc.Get("Package changes"),FontWeight=FontWeight.Medium });
            content.Children.Add(new ScrollViewer { MaxHeight=180,Content=new TextBlock { Text=string.Join('\n',plan.AffectedPackages.Where(value=>!value.StartsWith("source:",StringComparison.Ordinal)).Select(value=>value.StartsWith("portable:",StringComparison.Ordinal)?value[9..].Split('=')[0]:value)),TextWrapping=TextWrapping.Wrap } });
        }
        if(plan.RequiresReview)
        {
            content.Children.Add(new TextBlock { Text=Loc.Get("Choose the app’s dedicated installation folder. User folders move to Trash. System installations require authentication and are permanently removed. Shared folders cannot be removed."),TextWrapping=TextWrapping.Wrap });
            var chosen=new TextBlock { TextWrapping=TextWrapping.Wrap };
            var choose=new Button { Content=Loc.Get("Choose installation folder"),Classes={"Secondary","Small"} };
            choose.Click+=async(_,_)=>
            {
                if(Application.Current is not App app)return;
                var paths=await app.PickFilesAsync(this,new Apps.Files.Picker.DaisyFilePickerOptions(Loc.Get("Choose installation folder"),false,true,[],SelectDirectory:true));
                if(paths?.FirstOrDefault() is not { } path)return;
                try
                {
                    Plan=await new DaisyOS.System.Applications.ApplicationManagementService().PrepareUninstallAsync(plan.Application with { ReviewedPaths=[path] });
                    chosen.Text=path+(Plan.Error.Length>0?"\n"+Plan.Error:Plan.Application.Identity.Scope=="system"?"\n"+Loc.Get("Administrator authentication required. This folder will be permanently removed.")+"\n"+string.Join('\n',Plan.Application.OwnedLaunchers):"");
                    accept.IsEnabled=Plan.CanExecute;
                }
                catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or InvalidOperationException)
                { chosen.Text=ex.Message;accept.IsEnabled=false; }
            };
            content.Children.Add(choose);content.Children.Add(chosen);
        }
        if(plan.Error.Length>0)content.Children.Add(new TextBlock { Text=plan.Error,TextWrapping=TextWrapping.Wrap });
        if(removing&&plan.SupportsDataRemoval)content.Children.Add(_removeData);
        content.Children.Add(new StackPanel { Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right,Spacing=8,Children={cancel,accept} });
        Content=content;
    }
    public async Task<bool> ConfirmAsync(Window? owner)
    {
        if(owner is { IsVisible:true } && Application.Current is not App)return await ShowDialog<bool>(owner);
        if(owner is { IsVisible:true } && Application.Current is App app && !app.IsLayerShellSurface(owner))return await ShowDialog<bool>(owner);
        var completion=new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        Closed+=(_,_)=>completion.TrySetResult(_accepted);WindowStartupLocation=WindowStartupLocation.CenterScreen;Topmost=true;Show();Activate();return await completion.Task;
    }
}
