using Avalonia.Controls;
using DaisyOS.Shell.Controls;

namespace DaisyOS.Shell.Apps.AppStore;

public partial class AppStoreWindow : Window
{
    private readonly SystemAppWindowChrome _chrome;
    private readonly bool _ownsViewModel;
    public AppStoreWindow() : this(new AppStoreViewModel()) { }
    internal AppStoreWindow(AppStoreViewModel viewModel) : this(viewModel, true) { }
    internal AppStoreWindow(AppStoreViewModel viewModel, bool ownsViewModel)
    {
        _ownsViewModel = ownsViewModel;
        InitializeComponent();
        DataContext = viewModel;
        _chrome = new SystemAppWindowChrome(this, StoreView.FindControl<AppWindowTitleBar>("AppTitleBar")!,
            StoreView.FindControl<Grid>("MainPaneLayout")!, normalRowDefinitions: "Auto,*");
        Opened += (_, _) => { if (!viewModel.IsLoading && !viewModel.HasActiveOperations) _ = viewModel.RefreshAsync(); };
    }
    protected override void OnClosed(EventArgs e)
    { _chrome.Dispose(); if (_ownsViewModel) (DataContext as AppStoreViewModel)?.Dispose(); base.OnClosed(e); }
}
