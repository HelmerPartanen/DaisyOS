using System;
using System.IO;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using DaisyOS.Shell.ViewModels;

namespace DaisyOS.Shell.Views.Components.Desktop;

public partial class DesktopItemPropertiesDialog : Window
{
    public DesktopItemPropertiesDialog()
    {
        InitializeComponent();
    }

    public DesktopItemPropertiesDialog(DesktopItemViewModel item) : this()
    {
        PopulateDetails(item);
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private void PopulateDetails(DesktopItemViewModel item)
    {
        var iconImage = this.FindControl<Image>("ItemIcon");
        var nameText = this.FindControl<TextBlock>("ItemNameText");
        var typeText = this.FindControl<TextBlock>("ItemTypeText");
        var locationText = this.FindControl<TextBlock>("LocationText");
        var sizeText = this.FindControl<TextBlock>("SizeText");
        var createdText = this.FindControl<TextBlock>("CreatedText");
        var modifiedText = this.FindControl<TextBlock>("ModifiedText");
        var accessedText = this.FindControl<TextBlock>("AccessedText");
        var permissionsText = this.FindControl<TextBlock>("PermissionsText");

        if (iconImage != null) iconImage.Source = item.IconBitmap;
        if (nameText != null) nameText.Text = item.Label;

        var path = item.State.Id;
        if (item.Id.Value == DesktopItemViewModel.TrashItemId)
        {
            if (typeText != null) typeText.Text = Loc.Get("System Trash");
            if (locationText != null) locationText.Text = Loc.Get("trash:///");
            if (sizeText != null) sizeText.Text = "--";
            if (createdText != null) createdText.Text = "--";
            if (modifiedText != null) modifiedText.Text = "--";
            if (accessedText != null) accessedText.Text = "--";
            if (permissionsText != null) permissionsText.Text = Loc.Get("Read / Write");
            return;
        }

        if (item.IsWidgetCard)
        {
            if (typeText != null) typeText.Text = Loc.Get("Desktop Widget");
            if (locationText != null) locationText.Text = Loc.Get("daisyos://widgets");
            if (sizeText != null) sizeText.Text = Loc.Format("{0} × {1} grid cells", item.ColumnSpan, item.RowSpan);
            if (createdText != null) createdText.Text = "--";
            if (modifiedText != null) modifiedText.Text = "--";
            if (accessedText != null) accessedText.Text = "--";
            if (permissionsText != null) permissionsText.Text = Loc.Get("Shell Managed");
            return;
        }

        if (File.Exists(path))
        {
            var fileInfo = new FileInfo(path);
            var ext = fileInfo.Extension.ToLowerInvariant();
            var desc = ext switch
            {
                ".png" or ".jpg" or ".jpeg" or ".webp" or ".bmp" or ".gif" => "Image File",
                ".svg" => "Scalable Vector Graphic",
                ".pdf" => "PDF Document",
                ".txt" or ".md" or ".json" or ".xml" or ".yaml" or ".yml" => "Text Document",
                ".zip" or ".tar" or ".gz" or ".xz" => "Archive File",
                ".desktop" => "Application Launcher",
                _ => "File"
            };

            if (typeText != null) typeText.Text = desc;
            if (locationText != null) locationText.Text = fileInfo.DirectoryName ?? path;
            if (sizeText != null) sizeText.Text = FormatFileSize(fileInfo.Length);
            if (createdText != null) createdText.Text = fileInfo.CreationTime.ToString("g");
            if (modifiedText != null) modifiedText.Text = fileInfo.LastWriteTime.ToString("g");
            if (accessedText != null) accessedText.Text = fileInfo.LastAccessTime.ToString("g");
            if (permissionsText != null) permissionsText.Text = fileInfo.IsReadOnly ? Loc.Get("Read-only") : Loc.Get("Read / Write");
        }
        else if (Directory.Exists(path))
        {
            var dirInfo = new DirectoryInfo(path);
            if (typeText != null) typeText.Text = Loc.Get("Folder");
            if (locationText != null) locationText.Text = dirInfo.Parent?.FullName ?? path;

            try
            {
                int fileCount = dirInfo.GetFileSystemInfos().Length;
                if (sizeText != null) sizeText.Text = fileCount == 1 ? Loc.Get("1 item") : Loc.Format("{0} items", fileCount);
            }
            catch
            {
                if (sizeText != null) sizeText.Text = "--";
            }

            if (createdText != null) createdText.Text = dirInfo.CreationTime.ToString("g");
            if (modifiedText != null) modifiedText.Text = dirInfo.LastWriteTime.ToString("g");
            if (accessedText != null) accessedText.Text = dirInfo.LastAccessTime.ToString("g");
            if (permissionsText != null) permissionsText.Text = Loc.Get("Read / Write");
        }
        else
        {
            if (typeText != null) typeText.Text = Loc.Get("Item");
            if (locationText != null) locationText.Text = path;
            if (sizeText != null) sizeText.Text = "--";
            if (createdText != null) createdText.Text = "--";
            if (modifiedText != null) modifiedText.Text = "--";
            if (accessedText != null) accessedText.Text = "--";
            if (permissionsText != null) permissionsText.Text = "--";
        }
    }

    private static string FormatFileSize(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB ({bytes:N0} bytes)";
        if (bytes < 1024 * 1024 * 1024) return $"{bytes / (1024.0 * 1024.0):F2} MB ({bytes:N0} bytes)";
        return $"{bytes / (1024.0 * 1024.0 * 1024.0):F2} GB ({bytes:N0} bytes)";
    }

    private void OnCloseClicked(object? sender, RoutedEventArgs e)
    {
        Close();
    }
}
