using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using DaisyOS.Core.Models.Gaming;
using DaisyOS.Core.Services.Gaming;
using DaisyOS.Shell.ViewModels;

namespace DaisyOS.Shell.ViewModels;

public sealed class ConsoleGameItemViewModel : INotifyPropertyChanged
{
    private IImage? _coverImage;
    private IImage? _heroImage;
    private IImage? _logoImage;
    private IImage? _iconImage;
    private bool _isFallback = true;
    private string _artworkSource = "fallback";

    public GameIdentity Game { get; }
    public string Title => Game.Title;
    public string SourceText => Game.Source.ToString();
    public string SourceIdText => Game.SourceId ?? string.Empty;

    private bool _isLaunching;
    public bool IsLaunching
    {
        get => _isLaunching;
        set { _isLaunching = value; OnPropertyChanged(); }
    }

    private bool _isRunning;
    public bool IsRunning
    {
        get => _isRunning;
        set { _isRunning = value; OnPropertyChanged(); }
    }

    public IImage? CoverImage
    {
        get => _coverImage;
        private set
        {
            _coverImage = value;
            NotifyImageProperties();
        }
    }

    public IImage? HeroImage
    {
        get => _heroImage;
        private set
        {
            _heroImage = value;
            NotifyImageProperties();
        }
    }

    public IImage? LogoImage
    {
        get => _logoImage;
        private set
        {
            _logoImage = value;
            NotifyImageProperties();
        }
    }

    public IImage? IconImage
    {
        get => _iconImage;
        private set
        {
            _iconImage = value;
            NotifyImageProperties();
        }
    }

    public bool IsFallback
    {
        get => _isFallback;
        private set
        {
            _isFallback = value;
            OnPropertyChanged();
            NotifyImageProperties();
        }
    }

    public IImage? CardImage => CoverImage ?? LogoImage ?? HeroImage ?? IconImage;
    public bool HasCardImage => CardImage != null;
    public bool HasCoverImage => CoverImage != null;
    public bool HasHeroImage => HeroImage != null;
    public bool HasLogoImage => LogoImage != null;
    public bool HasIconImage => IconImage != null;
    public bool IsLogoCard => CoverImage == null && LogoImage != null;
    public bool ShowFallbackUI => !HasCoverImage && !HasLogoImage && !HasHeroImage;

    public ConsoleGameItemViewModel(GameIdentity game)
    {
        Game = game;
        LoadIcon();
    }

    private void LoadIcon()
    {
        if (!string.IsNullOrWhiteSpace(Game.IconNameOrPath))
        {
            var resolvedIconPath = DesktopItemLoader.ResolveIconPath(Game.IconNameOrPath);
            if (!string.IsNullOrEmpty(resolvedIconPath))
            {
                IconImage = DesktopItemLoader.LoadBitmapSafe(resolvedIconPath);
            }
        }
    }

    public void ApplyArtworkAssets(GameArtworkAssets assets)
    {
        IsFallback = assets.IsFallback;
        _artworkSource = assets.ArtworkSource;

        if (assets.HasCover && File.Exists(assets.CoverPath))
        {
            CoverImage = DesktopItemLoader.LoadBitmapSafe(assets.CoverPath!);
        }

        if (assets.HasHero && File.Exists(assets.HeroPath))
        {
            HeroImage = DesktopItemLoader.LoadBitmapSafe(assets.HeroPath!);
        }

        if (assets.HasLogo && File.Exists(assets.LogoPath))
        {
            LogoImage = DesktopItemLoader.LoadBitmapSafe(assets.LogoPath!);
        }

        if (assets.HasIcon && File.Exists(assets.IconPath))
        {
            IconImage = DesktopItemLoader.LoadBitmapSafe(assets.IconPath!);
        }

        NotifyImageProperties();
    }

    private void NotifyImageProperties()
    {
        OnPropertyChanged(nameof(CoverImage));
        OnPropertyChanged(nameof(HeroImage));
        OnPropertyChanged(nameof(LogoImage));
        OnPropertyChanged(nameof(IconImage));
        OnPropertyChanged(nameof(CardImage));
        OnPropertyChanged(nameof(HasCardImage));
        OnPropertyChanged(nameof(HasCoverImage));
        OnPropertyChanged(nameof(HasHeroImage));
        OnPropertyChanged(nameof(HasLogoImage));
        OnPropertyChanged(nameof(HasIconImage));
        OnPropertyChanged(nameof(IsLogoCard));
        OnPropertyChanged(nameof(ShowFallbackUI));
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
