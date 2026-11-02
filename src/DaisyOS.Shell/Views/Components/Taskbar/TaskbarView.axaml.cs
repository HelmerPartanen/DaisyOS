using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using DaisyOS.Shell.Controls;
using DaisyOS.Shell.Services;
using DaisyOS.Shell.Services.Windows;
using DaisyOS.Shell.Services.Compositor;
using DaisyOS.Shell.ViewModels;

namespace DaisyOS.Shell.Views.Components.Taskbar
{
    public partial class TaskbarView : UserControl
    {
        public event global::System.EventHandler<Avalonia.Interactivity.RoutedEventArgs>? StartButtonClicked;

        /// <summary>Raised when an app icon is tapped (i.e. pressed and released without dragging).</summary>
        public event EventHandler<string>? AppIconClicked;

        /// <summary>Raised only from the explicit Force quit taskbar action.</summary>
        public event EventHandler<string>? ForceQuitRequested;

        /// <summary>Raised once a drag ends and the icon order actually changed, with the new left-to-right tags.</summary>
        public event EventHandler<IReadOnlyList<string>>? OrderChanged;

        /// <summary>Raised when the taskbar context menu visibility changes.</summary>
        public event EventHandler<bool>? ContextMenuVisibilityChanged;

        /// <summary>Raised when App Info is requested from a taskbar context menu.</summary>
        public event EventHandler<string>? AppInfoRequested;

        /// <summary>Raised when Close is requested on a running app from its context menu.</summary>
        public event EventHandler<string>? CloseAppRequested;

        // Current slot geometry: starts at the normal 40px item / 4px gap, then fits the dock width.
        private double ItemWidth = 40;
        private double Spacing = 4;
        private double _availableWidth = 1200;
        private const double DragThreshold = 4; // px of movement before a press becomes a drag
        private const double BottomEdgeFlare = 16;
        // A 4px marker sits flush with the button's 40px bottom edge.
        private const double WindowIndicatorTop = 36;

        private Canvas? _canvas;
        private Button? _startButton;
        private Border? _taskbarBody;
        private Border? _taskbarInnerFrame;
        private Border? _taskbarFill;
        private CartoucheBorder? _contextMenuHost;
        private StackPanel? _contextMenuItems;


        private readonly List<Button> _order = new();
        internal Func<IEnumerable<string?>, string?> DesktopEntryResolver { get; set; } = DesktopItemLoader.FindDesktopEntryFilePath;
        private readonly Dictionary<string, (string? DesktopId, string AppId, string ResolvedId)> _resolvedWindowAppIds = new(StringComparer.Ordinal);
        private readonly HashSet<string> _pinnedAppIds = new(StringComparer.Ordinal);
        private readonly Dictionary<string, NativeAppWindowState> _windowStates = new(StringComparer.Ordinal);
        private readonly Dictionary<string, string> _appNames = new(StringComparer.Ordinal);
        private readonly Dictionary<string, string> _compositorWindowIds = new(StringComparer.Ordinal);
        private readonly Dictionary<string, CompositorWindowRecord> _compositorRecords = new(StringComparer.Ordinal);
        private readonly Dictionary<string, MenuItem> _forceQuitItems = new(StringComparer.Ordinal);
        // KWin removes a closed Wayland client asynchronously. Keep the local
        // close authoritative until the next snapshot confirms the removal so
        // an old compositor record cannot paint a closed DaisyOS app as open.
        private readonly Dictionary<string, string> _closedOwnedApps = new(StringComparer.Ordinal);
        private Button? _activeContextMenuButton;

        // Active drag state
        private Button? _dragItem;
        private bool _pointerDown;
        private bool _isDragging;
        private bool _suppressNextClick;
        private string? _pendingActivationAppId;
        private bool? _pendingActivationWasActive;
        private double _pointerStartX;
        private double _dragStartLeft;
        private List<string>? _orderAtDragStart;
        private IReadOnlyList<string>? _pendingOrder;

        public bool IsContextMenuVisible => _contextMenuHost?.IsVisible == true;

        public TaskbarView()
        {
            AddHandler(InputElement.KeyDownEvent, (_, e) =>
            {
                if (e.Key is Key.Enter or Key.Space && e.Source is Visual visual)
                {
                    var button = visual.GetSelfAndVisualAncestors().OfType<Button>().FirstOrDefault(b => b.Tag is string);
                    if (button?.Tag is string id)
                    {
                        CaptureActivationState(id);
                        _launchActivationApp = id;
                        _launchActivationToken = Avalonia.Wayland.WaylandActivation.RequestAsync(e, id);
                    }
                }
            }, RoutingStrategies.Tunnel);
            InitializeComponent();

            _startButton = this.FindControl<Button>("StartButton");
            if (_startButton != null)
            {
                _startButton.Click += (s, e) => StartButtonClicked?.Invoke(this, e);
            }

            _taskbarBody = this.FindControl<Border>("TaskbarBody");
            _taskbarInnerFrame = this.FindControl<Border>("TaskbarInnerFrame");
            _taskbarFill = this.FindControl<Border>("TaskbarFill");
            _canvas = this.FindControl<Canvas>("AppIconsCanvas");
            _contextMenuHost = this.FindControl<CartoucheBorder>("TaskbarContextMenuHost");
            _contextMenuItems = this.FindControl<StackPanel>("TaskbarContextMenuItems");

            InitializeIcons();
            DetachedFromVisualTree += (_, _) => HideHoverPreview();
        }

        public void SetLauncherOpen(bool isOpen)
        {
            if (isOpen) HideHoverPreview();
            _startButton?.Classes.Set("LauncherActive", isOpen);
        }

        /// <summary>Applies native owned-window state and keeps unpinned running apps visible.</summary>
        public void SetAppWindowState(string appId, NativeAppWindowState state)
        {
            appId = NormalizeAppId(appId);
            _windowStates[appId] = state;
            if (state == NativeAppWindowState.NotRunning)
            {
                if (_compositorWindowIds.Remove(appId, out var closedWindowId))
                    _closedOwnedApps[appId] = closedWindowId;
            }
            else
            {
                _closedOwnedApps.Remove(appId);
                if (!_order.Any(button => string.Equals(button.Tag as string, appId, StringComparison.OrdinalIgnoreCase)))
                {
                    AddPinnedAppButton(appId);
                }
            }
            if (_order.FirstOrDefault(button => string.Equals(button.Tag as string, appId, StringComparison.Ordinal)) is { } button)
            {
                ApplyWindowState(button, appId, state);
                if (state == NativeAppWindowState.NotRunning
                    && !_pinnedAppIds.Contains(appId)
                    && !_compositorWindowIds.ContainsKey(appId))
                {
                    RemoveSpecificApp(button);
                }
            }
        }

        /// <summary>
        /// Reconciles compositor-owned application windows with the taskbar.
        /// DaisyOS's pinned applications retain their existing controls; other
        /// windows are temporary taskbar entries addressed by compositor id.
        /// </summary>
        public void SetCompositorWindows(IEnumerable<CompositorWindowRecord> windows)
        {
            var snapshot = windows.ToArray();
            _previewWindows = snapshot;
            var currentWindowIds = snapshot.Select(window => window.Id).ToHashSet(StringComparer.Ordinal);
            foreach (var staleId in _resolvedWindowAppIds.Keys.Where(id => !currentWindowIds.Contains(id)).ToArray())
                _resolvedWindowAppIds.Remove(staleId);
            _compositorRecords.Clear();
            foreach (var w in snapshot)
            {
                _compositorRecords[w.Id] = w;
                _compositorRecords[$"kwin:{w.Id}"] = w;
                if (!string.IsNullOrWhiteSpace(w.DaisyAppId))
                {
                    _compositorRecords[w.DaisyAppId] = w;
                }
                if (!string.IsNullOrWhiteSpace(w.AppId))
                {
                    _compositorRecords[w.AppId] = w;
                }
            }
            var ownedWindows = snapshot
                .Where(window => !string.IsNullOrWhiteSpace(window.DaisyAppId))
                .ToArray();
            var externalWindows = snapshot
                .Where(window => string.IsNullOrWhiteSpace(window.DaisyAppId))
                .ToArray();
            var wanted = externalWindows.Select(window => window.Id).ToHashSet(StringComparer.Ordinal);

            // DaisyOS app windows remain in their pinned slots. Their KWin id
            // is still recorded so a taskbar click uses compositor-authorized
            // focus restoration instead of a Wayland client trying to focus
            // itself without a valid activation token.
            var wantedOwnedTags = ownedWindows
                .Select(window => window.DaisyAppId!)
                .ToHashSet(StringComparer.Ordinal);
            foreach (var staleTag in _compositorWindowIds.Keys
                         .Where(tag => !tag.StartsWith("kwin:", StringComparison.Ordinal)
                             && !wantedOwnedTags.Contains(tag))
                         .ToArray())
            {
                if (!_pinnedAppIds.Contains(staleTag))
                {
                    RemoveCompositorWindow(staleTag);
                }
            }

            foreach (var window in ownedWindows)
            {
                var tag = window.DaisyAppId!;
                if (_closedOwnedApps.TryGetValue(tag, out var closedWindowId) && closedWindowId == window.Id)
                {
                    continue;
                }
                // Running native applications need a temporary slot even when
                // the user has not pinned them (the same rule as external apps).
                if (!_order.Any(button => string.Equals(button.Tag as string, tag, StringComparison.Ordinal)))
                    AddPinnedAppButton(tag);
                _compositorWindowIds[tag] = window.Id;
                SetAppWindowState(tag, ToTaskbarState(window));
                ApplyUnresponsiveState(tag, window);
            }

            var wantedExternalIds = externalWindows.Select(w => w.Id).ToHashSet(StringComparer.Ordinal);

            // Clean up stale pinned external windows
            foreach (var pinnedTag in _pinnedAppIds.ToArray())
            {
                if (_compositorWindowIds.TryGetValue(pinnedTag, out var runningWinId)
                    && !wantedOwnedTags.Contains(pinnedTag)
                    && !wantedExternalIds.Contains(runningWinId))
                {
                    _compositorWindowIds.Remove(pinnedTag);
                    SetAppWindowState(pinnedTag, NativeAppWindowState.NotRunning);
                }
            }

            // Clean up stale unpinned kwin windows
            foreach (var staleTag in _compositorWindowIds
                         .Where(pair => pair.Key.StartsWith("kwin:", StringComparison.Ordinal)
                             && !wanted.Contains(pair.Value))
                         .Select(pair => pair.Key)
                         .ToArray())
            {
                RemoveCompositorWindow(staleTag);
            }

            var orphanKwinButtons = _order
                .Where(b => b.Tag is string tag && tag.StartsWith("kwin:", StringComparison.Ordinal)
                    && (!_compositorWindowIds.TryGetValue(tag, out var wId) || !wanted.Contains(wId)))
                .ToList();
            foreach (var orphan in orphanKwinButtons)
            {
                RemoveSpecificApp(orphan);
            }

            foreach (var window in externalWindows)
            {
                // Skip DaisyOS shell surfaces and background windows
                if (string.Equals(window.AppId, "daisyos.shell", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(window.AppId, "daisyos-shell", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(window.Title, "daisyos shell", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(window.Title, "DaisyOS Shell", StringComparison.OrdinalIgnoreCase)
                    || (!string.IsNullOrWhiteSpace(window.Title) && window.Title.Contains("daisyos", StringComparison.OrdinalIgnoreCase) && string.IsNullOrWhiteSpace(window.DesktopFileId)))
                {
                    continue;
                }

                var effectiveAppId = ResolveEffectiveAppIdForWindow(window);
                var pinnedButton = _order.FirstOrDefault(button =>
                {
                    if (button.Tag is not string tag || tag.StartsWith("kwin:", StringComparison.Ordinal))
                        return false;
                    var tagTrimmed = NormalizeAppId(tag);
                    return string.Equals(tagTrimmed, effectiveAppId, StringComparison.OrdinalIgnoreCase)
                        || (!string.IsNullOrWhiteSpace(window.DesktopFileId) && string.Equals(tagTrimmed, NormalizeAppId(window.DesktopFileId), StringComparison.OrdinalIgnoreCase))
                        || (!string.IsNullOrWhiteSpace(window.AppId) && string.Equals(tagTrimmed, NormalizeAppId(window.AppId), StringComparison.OrdinalIgnoreCase))
                        || (!string.IsNullOrWhiteSpace(window.IconName) && string.Equals(tagTrimmed, NormalizeAppId(window.IconName), StringComparison.OrdinalIgnoreCase));
                });

                if (pinnedButton != null)
                {
                    // A shortcut may be pinned after its temporary window tile
                    // already exists. Merge that tile into the persistent pin.
                    var temporaryTag = $"kwin:{window.Id}";
                    if (_order.FirstOrDefault(button => Equals(button.Tag, temporaryTag)) is { } temporaryButton)
                    {
                        _compositorWindowIds.Remove(temporaryTag);
                        _windowStates.Remove(temporaryTag);
                        _appNames.Remove(temporaryTag);
                        RemoveSpecificApp(temporaryButton);
                    }
                    var tag = (string)pinnedButton.Tag!;
                    _compositorWindowIds[tag] = window.Id;
                    SetAppWindowState(tag, ToTaskbarState(window));
                    var label = TaskbarAppLabel.Format(tag, string.IsNullOrWhiteSpace(window.Title) ? window.AppId : window.Title);
                    _appNames[tag] = label;
                    pinnedButton.SetValue(ToolTip.TipProperty, label);
                    pinnedButton.SetValue(AutomationProperties.NameProperty, label);
                    ApplyUnresponsiveState(tag, window);
                }
                else
                {
                    var tag = $"kwin:{window.Id}";
                    if (!_compositorWindowIds.ContainsKey(tag))
                    {
                        if (!_order.Any(b => string.Equals(b.Tag as string, tag, StringComparison.Ordinal)))
                        {
                            AddCompositorWindow(tag, window);
                        }
                    }

                    SetAppWindowState(tag, ToTaskbarState(window));
                    if (_order.FirstOrDefault(button => string.Equals(button.Tag as string, tag, StringComparison.Ordinal)) is { } button)
                    {
                        var label = string.IsNullOrWhiteSpace(window.Title) ? window.AppId : window.Title;
                        _appNames[tag] = label;
                        button.SetValue(ToolTip.TipProperty, label);
                        button.SetValue(AutomationProperties.NameProperty, label);
                    }
                    ApplyUnresponsiveState(tag, window);
                }
            }

            UpdateInstanceBadges(snapshot);
            RefreshHoverPreview();
        }

        private void UpdateInstanceBadges(IReadOnlyList<CompositorWindowRecord> windows)
        {
            var counts = windows
                .Where(window => window.IsOnCurrentWorkspace)
                .GroupBy(window => !string.IsNullOrWhiteSpace(window.DaisyAppId)
                    ? window.DaisyAppId!
                    : ResolveEffectiveAppIdForWindow(window), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase);

            var geometryChanged = false;
            foreach (var button in _order)
            {
                if (button.Tag is not string tag || button.Content is not Panel panel) continue;
                var effectiveId = ResolveEffectiveAppId(tag);
                var count = counts.GetValueOrDefault(effectiveId);
                var rails = panel.Children.OfType<Border>()
                    .Where(child => child.Classes.Contains("TaskbarGroupedIndicator"))
                    .ToArray();
                var primaryRail = rails.FirstOrDefault(child => !child.Classes.Contains("Secondary"));
                if (primaryRail is null)
                {
                    primaryRail = new TaskbarStackedWindowBorder
                    {
                        Classes = { "TaskbarGroupedIndicator" },
                        IsHitTestVisible = false
                    };
                    panel.Children.Insert(0, primaryRail);
                }
                geometryChanged |= button.Classes.Contains("WindowGrouped") != (count > 1);
                button.Classes.Set("WindowGrouped", count > 1);
                primaryRail.IsVisible = count > 1;

                var name = _appNames.GetValueOrDefault(tag, effectiveId);
                var windowText = count > 1 ? Loc.Format(" — {0} windows", count) : string.Empty;
                button.SetValue(AutomationProperties.NameProperty, name + windowText);
            }
            if (geometryChanged) UpdateCanvasWidth();
        }

        public bool TryGetCompositorWindowId(string appId, out string windowId) =>
            _compositorWindowIds.TryGetValue(NormalizeAppId(appId), out windowId!);

        /// <summary>
        /// Returns the compositor focus state captured before the taskbar layer
        /// could take focus from the clicked application. The value is consumed
        /// by the matching activation only.
        /// </summary>
        public bool? TakeWasActiveAtActivation(string appId)
        {
            var matches = string.Equals(
                _pendingActivationAppId,
                NormalizeAppId(appId),
                StringComparison.Ordinal);
            var result = matches ? _pendingActivationWasActive : null;
            _pendingActivationAppId = null;
            _pendingActivationWasActive = null;
            return result;
        }

        private void CaptureActivationState(string appId)
        {
            var normalizedAppId = NormalizeAppId(appId);
            _pendingActivationAppId = normalizedAppId;
            _pendingActivationWasActive = TryGetCompositorWindowId(appId, out var windowId) &&
                _compositorRecords.TryGetValue(windowId, out var record)
                    ? record.IsActive
                    : _windowStates.GetValueOrDefault(normalizedAppId) == NativeAppWindowState.Active;
        }

        private bool _iconsInitialized;

        private void InitializeIcons()
        {
            if (_canvas is null || _iconsInitialized) return;
            _iconsInitialized = true;

            _order.Clear();
            foreach (var child in _canvas.Children.OfType<Button>())
            {
                _order.Add(child);
                WireUpIcon(child);
                if (child.Tag is string appId)
                {
                    _pinnedAppIds.Add(appId);
                    _appNames[appId] = TaskbarAppLabel.Format(appId, child.GetValue(ToolTip.TipProperty) as string ?? appId);
                    ApplyWindowState(child, appId, _windowStates.GetValueOrDefault(appId, NativeAppWindowState.NotRunning));
                }
            }

            if (_pendingOrder is { Count: > 0 })
            {
                ApplyOrder(_pendingOrder);
            }
            else
            {
                LayoutAllInstant();
                UpdateCanvasWidth();
            }
        }

        /// <summary>Restores a persisted order while retaining newly introduced dock items.</summary>
        public void ApplyOrder(IReadOnlyList<string> order)
        {
            InitializeIcons();

            var cleanOrder = order
                .Where(id => !string.IsNullOrWhiteSpace(id) && !id.StartsWith("kwin:", StringComparison.Ordinal))
                .Select(id => ResolveInstalledAppId(id))
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            _pendingOrder = cleanOrder;
            _pinnedAppIds.Clear();
            _pinnedAppIds.UnionWith(cleanOrder);

            if (_canvas is null) return;

            // Remove unpinned static XAML apps that aren't in cleanOrder and aren't running
            var unpinnedStaticIcons = _order
                .Where(btn => btn.Tag is string tag
                    && !tag.StartsWith("kwin:", StringComparison.Ordinal)
                    && !_pinnedAppIds.Contains(tag)
                    && (!_windowStates.TryGetValue(tag, out var st) || st == NativeAppWindowState.NotRunning)
                    && !_compositorWindowIds.ContainsKey(tag))
                .ToList();

            foreach (var unpinned in unpinnedStaticIcons)
            {
                RemoveSpecificApp(unpinned);
            }

            // Ensure all pinned apps in cleanOrder exist on canvas
            foreach (var appId in cleanOrder)
            {
                if (!_order.Any(btn => string.Equals(btn.Tag as string, appId, StringComparison.OrdinalIgnoreCase)))
                {
                    AddPinnedAppButton(appId);
                }
            }

            ApplyOrderCore(cleanOrder);
            LayoutAllInstant();
            UpdateCanvasWidth();
        }

        private void ApplyOrderCore(IReadOnlyList<string> order)
        {
            var rank = order
                .Select((id, index) => (id, index))
                .ToDictionary(pair => pair.id, pair => pair.index, StringComparer.Ordinal);
            _order.Sort((left, right) =>
            {
                var leftId = left.Tag as string ?? string.Empty;
                var rightId = right.Tag as string ?? string.Empty;
                var leftRank = rank.TryGetValue(leftId, out var l) ? l : int.MaxValue;
                var rightRank = rank.TryGetValue(rightId, out var r) ? r : int.MaxValue;
                return leftRank != rightRank ? leftRank.CompareTo(rightRank) : string.Compare(leftId, rightId, StringComparison.Ordinal);
            });
        }

        private void WireUpIcon(Button icon)
        {
            UnwireIcon(icon);

            icon.Cursor = new Cursor(StandardCursorType.Hand);

            // Use tunnel strategy for pointer pressed so we can intercept drag and right clicks
            // before the Button's default handling swallows the event.
            icon.AddHandler(InputElement.PointerPressedEvent, Icon_PointerPressed, RoutingStrategies.Tunnel);
            icon.PointerEntered += PreviewPointerEntered;
            icon.PointerExited += PreviewPointerExited;
            icon.PointerMoved += Icon_PointerMoved;
            icon.PointerReleased += Icon_PointerReleased;
            icon.PointerCaptureLost += Icon_PointerCaptureLost;
            icon.Click += Icon_Click;
            icon.ContextRequested += Icon_ContextRequested;
        }

        private void UnwireIcon(Button icon)
        {
            icon.RemoveHandler(InputElement.PointerPressedEvent, Icon_PointerPressed);
            icon.PointerEntered -= PreviewPointerEntered;
            icon.PointerExited -= PreviewPointerExited;
            icon.PointerMoved -= Icon_PointerMoved;
            icon.PointerReleased -= Icon_PointerReleased;
            icon.PointerCaptureLost -= Icon_PointerCaptureLost;
            icon.Click -= Icon_Click;
            icon.ContextRequested -= Icon_ContextRequested;
        }

        private TimeSpan MotionDuration(string key, int fallback) =>
            Application.Current is App { ReduceMotionEnabled: true } ? TimeSpan.Zero :
            this.TryFindResource(key, out var resource) && resource is TimeSpan duration
                ? duration : TimeSpan.FromMilliseconds(fallback);

        private Transitions GetPositionTransitions() => new()
        {
            new DoubleTransition { Property = Canvas.LeftProperty, Duration = MotionDuration("MotionDurationBase", 160), Easing = new CubicEaseOut() },
            new TransformOperationsTransition { Property = Visual.RenderTransformProperty, Duration = MotionDuration("MotionDurationFast", 100), Easing = new CubicEaseOut() }
        };

        private Transitions GetDragTransitions() => new()
        {
            new TransformOperationsTransition { Property = Visual.RenderTransformProperty, Duration = MotionDuration("MotionDurationFast", 100), Easing = new CubicEaseOut() }
        };

        private void LayoutAllInstant()
        {
            for (int i = 0; i < _order.Count; i++)
            {
                var icon = _order[i];
                icon.Transitions = GetDragTransitions(); // avoid animating in from 0 on first layout
                Canvas.SetLeft(icon, SlotX(i));
                icon.Transitions = GetPositionTransitions();
            }
        }

        protected override Size MeasureOverride(Size availableSize)
        {
            var limit = this.TryFindResource("TaskbarMaximumWidth", out var resource) && resource is double value ? value : 1200;
            var taskbarScale = this.TryFindResource("TaskbarScale", out var scaleResource) && scaleResource is double configuredScale ? configuredScale : 1;
            var available = Math.Min(limit, double.IsFinite(availableSize.Width) ? Math.Max(0, availableSize.Width - 24) : limit) / taskbarScale;
            if (Math.Abs(_availableWidth - available) > 0.1)
            {
                _availableWidth = available;
                UpdateCanvasWidth();
            }
            return base.MeasureOverride(availableSize);
        }

        private void UpdateCanvasWidth()
        {
            if (_canvas is null) return;
            var count = _order.Count;
            // Frame/insets (12), Start (40), and the gap after Start (4) stay fixed.
            var naturalWidth = count == 0 ? 0 : count * 40 + (count - 1) * 4 + _order.Sum(StackExtent);
            var scale = naturalWidth == 0 ? 1 : Math.Min(1, Math.Max(0, _availableWidth - 56) / naturalWidth);
            ItemWidth = 40 * scale;
            Spacing = 4 * scale;
            _canvas.Width = naturalWidth * scale;
            for (var index = 0; index < count; index++)
            {
                var button = _order[index];
                button.MinWidth = 0;
                button.Width = ItemWidth;
                button.HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Center;
                button.VerticalContentAlignment = Avalonia.Layout.VerticalAlignment.Center;
                if (button.Content is Panel panel)
                {
                    panel.RenderTransformOrigin = RelativePoint.Center;
                    panel.RenderTransform = scale == 1 ? null : new ScaleTransform(scale, scale);
                }
                if (button != _dragItem)
                {
                    button.Transitions = GetDragTransitions();
                    Canvas.SetLeft(button, SlotX(index));
                    button.Transitions = GetPositionTransitions();
                }
            }
        }

        private void ApplyWindowState(Button button, string appId, NativeAppWindowState state)
        {
            var presentation = TaskbarWindowPresentation.From(state);
            // Only WindowActive (and WindowUnresponsive, set separately in
            // ApplyUnresponsiveState) have a matching Button.TaskbarApp.* style in
            // TaskbarView.axaml. WindowRunning/WindowMinimized only affect the
            // indicator Border below, so they aren't set on the button itself.
            button.Classes.Set("WindowActive", presentation.IsActive);
            if (!presentation.IsRunning)
            {
                // Pinned controls survive window removal, including a crashed
                // client that was previously marked unresponsive by KWin.
                button.Classes.Remove("WindowUnresponsive");
                if (_forceQuitItems.TryGetValue(appId, out var forceQuit))
                    forceQuit.IsEnabled = false;
            }

            if (button.Content is Panel panel && panel.Children.OfType<Border>().FirstOrDefault(b => b.Classes.Contains("TaskbarWindowIndicator")) is { } indicator)
            {
                indicator.Classes.Set("WindowRunning", presentation.IsRunning && !presentation.IsMinimized);
                indicator.Classes.Set("WindowActive", presentation.IsActive);
                indicator.Classes.Set("WindowMinimized", presentation.IsMinimized);
            }

            var appName = TaskbarAppLabel.Format(appId, _appNames.GetValueOrDefault(appId, button.GetValue(ToolTip.TipProperty) as string ?? appId));
            var accessibleName = presentation.StateLabel is null ? appName : $"{appName} — {presentation.StateLabel}";
            button.SetValue(ToolTip.TipProperty, accessibleName);
            button.SetValue(AutomationProperties.NameProperty, accessibleName);
        }




        public void RemoveSpecificApp(Button icon)
        {
            if (_canvas is null || !_order.Contains(icon)) return;

            UnwireIcon(icon);
            _order.Remove(icon);
            _canvas.Children.Remove(icon);

            // Smoothly shift remaining icons to their updated slots
            for (int i = 0; i < _order.Count; i++)
            {
                AnimateToSlot(_order[i], i);
            }

            UpdateCanvasWidth();
        }

        private void AddCompositorWindow(string tag, CompositorWindowRecord window)
        {
            if (_canvas is null) return;

            var panel = new Panel
            {
                Width = 40,
                Height = 40,
                ClipToBounds = false,
                Children =
                {
                    new Grid
                    {
                        Width = 32,
                        Height = 32,
                        HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                        VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                        Children = { CreateCompositorIcon(window) }
                    },
                    new Border
                    {
                        Classes = { "TaskbarWindowIndicator" },
                        IsHitTestVisible = false
                    }
                }
            };

            var iconButton = new Button
            {
                Classes = { "ShellButton", "TaskbarApp", "TaskbarAppIcon" },
                Tag = tag,
                Content = panel,
                [ToolTip.TipProperty] = window.DisplayName
            };

            var forceQuit = new MenuItem
            {
                Header = "Force quit",
                IsEnabled = window.IsUnresponsive
            };
            Loc.Bind(forceQuit, MenuItem.HeaderProperty, "Force quit");
            forceQuit.Click += (_, _) => ForceQuitRequested?.Invoke(this, tag);

            _canvas.Children.Add(iconButton);
            _order.Add(iconButton);
            _compositorWindowIds[tag] = window.Id;
            _forceQuitItems[tag] = forceQuit;
            _appNames[tag] = window.DisplayName;
            WireUpIcon(iconButton);
            Canvas.SetLeft(iconButton, SlotX(_order.Count - 1));
            UpdateCanvasWidth();
        }

        private void RemoveCompositorWindow(string tag)
        {
            if (_canvas is null
                || _order.FirstOrDefault(button => string.Equals(button.Tag as string, tag, StringComparison.Ordinal)) is not { } icon)
            {
                return;
            }

            var effectiveId = ResolveEffectiveAppId(tag);
            var isPinned = IsPinned(tag) || IsPinned(effectiveId);

            if (isPinned)
            {
                _compositorWindowIds.Remove(tag);
                _compositorWindowIds.Remove(effectiveId);
                SetAppWindowState(tag, NativeAppWindowState.NotRunning);
                SetAppWindowState(effectiveId, NativeAppWindowState.NotRunning);
                _forceQuitItems.Remove(tag);
                _forceQuitItems.Remove(effectiveId);
                return;
            }

            _compositorWindowIds.Remove(tag);
            _forceQuitItems.Remove(tag);
            _windowStates.Remove(tag);
            _appNames.Remove(tag);
            RemoveSpecificApp(icon);
        }

        private static NativeAppWindowState ToTaskbarState(CompositorWindowRecord window) =>
            window.IsMinimized ? NativeAppWindowState.Minimized :
            window.IsActive ? NativeAppWindowState.Active :
            NativeAppWindowState.RunningInactive;

        private void ApplyUnresponsiveState(string tag, CompositorWindowRecord window)
        {
            if (_order.FirstOrDefault(button => string.Equals(button.Tag as string, tag, StringComparison.Ordinal)) is not { } button)
            {
                return;
            }

            button.Classes.Set("WindowUnresponsive", window.IsUnresponsive);
            if (_forceQuitItems.TryGetValue(tag, out var forceQuit))
            {
                forceQuit.IsEnabled = window.IsUnresponsive && window.CanClose && window.ProcessId > 1;
            }

            var appName = _appNames.GetValueOrDefault(tag, button.GetValue(ToolTip.TipProperty) as string ?? tag);
            if (window.IsUnresponsive)
            {
                button.SetValue(ToolTip.TipProperty, $"{appName} — Not responding");
                button.SetValue(AutomationProperties.NameProperty, $"{appName} — Not responding");
            }
        }

        private Control CreateCompositorIcon(CompositorWindowRecord window)
        {
            var iconCandidates = new List<string?>
            {
                window.IconName,
                NormalizeAppId(window.DesktopFileId),
                NormalizeAppId(window.AppId),
                window.Title
            };

            if ((!string.IsNullOrWhiteSpace(window.Title) && window.Title.Contains("Steam", StringComparison.OrdinalIgnoreCase))
                || (!string.IsNullOrWhiteSpace(window.AppId) && window.AppId.Contains("steam", StringComparison.OrdinalIgnoreCase)))
            {
                iconCandidates.Add("steam");
                iconCandidates.Add("com.valvesoftware.Steam");
            }

            var iconPath = DesktopItemLoader.ResolveApplicationIconPath(iconCandidates);

            // The resolver normally finds a themed or Flatpak-exported asset.
            // Retain the desktop-entry fallback for packages such as VS Code or Steam
            // that keep their icon beside the executable or desktop entry.
            var image = string.IsNullOrWhiteSpace(iconPath)
                ? LoadDesktopEntryIcon(window.DesktopFileId) ?? LoadDesktopEntryIcon("steam.desktop")
                : null;

            // External applications share the same button, state indicator,
            // tooltip, and accessibility contract as DaisyOS-owned apps. Use
            // their desktop-file icon whenever it is available and retain a
            // stable neutral fallback for incomplete desktop entries.
            if (iconPath.EndsWith(".svg", StringComparison.OrdinalIgnoreCase))
            {
                // Render SVGs directly at the taskbar's final 26px optical
                // size. Routing a vector through Image can cache an
                // intermediate bitmap, which is especially noticeable for
                // high-contrast circular icons such as Spotify.
                return new Avalonia.Svg.Skia.Svg(new Uri("file:///"))
                {
                    Classes = { "AppIcon" },
                    Path = iconPath,
                    Width = 26,
                    Height = 26,
                    Stretch = Avalonia.Media.Stretch.Uniform,
                    EnableCache = true
                };
            }

            if (image is not null || !string.IsNullOrWhiteSpace(iconPath))
            {
                var icon = new Image
                {
                    Classes = { "AppIcon" },
                    Source = image ?? DesktopItemLoader.LoadBitmapSafe(iconPath),
                    // Match the native app glyph geometry. Keeping external
                    // icons at their intended logical size avoids resampling
                    // low-density artwork into a larger 32px box.
                    Width = 26,
                    Height = 26,
                    Stretch = Avalonia.Media.Stretch.Uniform
                };
                RenderOptions.SetBitmapInterpolationMode(icon, BitmapInterpolationMode.HighQuality);
                return icon;
            }

            return new Border
            {
                Width = 32,
                Height = 32,
                Background = this.FindResource("StateSelectedBrush") as IBrush,
                CornerRadius = new CornerRadius(10),
                Child = new DaisyIcon
                {
                    Icon = "squares-four",
                    Size = 18,
                    Foreground = this.FindResource("ContentPrimaryBrush") as IBrush,
                    HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                    VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
                }
            };
        }

        private static string NormalizeAppId(string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return string.Empty;
            var id = value.EndsWith(".desktop", StringComparison.OrdinalIgnoreCase) ? value[..^8] : value;
            // Desktop launchers persist full desktop IDs; KWin and owned-window
            // tracking use the built-in app's short ID. Keep one pinned identity.
            if (id.StartsWith("org.daisyos.", StringComparison.OrdinalIgnoreCase))
            {
                var nativeId = id["org.daisyos.".Length..].ToLowerInvariant();
                if (nativeId is "mail" or "notes" or "files" or "settings" or "calendar" or "calculator" or "appstore" or "taskmanager" or "uikit" or "diskmanager" or "appinstaller")
                    return nativeId;
            }
            return id;
        }

        private static Avalonia.Media.IImage? LoadDesktopEntryIcon(string? desktopFileId)
        {
            if (string.IsNullOrWhiteSpace(desktopFileId))
            {
                return null;
            }

            var fileName = desktopFileId.EndsWith(".desktop", StringComparison.OrdinalIgnoreCase)
                ? desktopFileId
                : $"{desktopFileId}.desktop";
            var desktopFile = new[]
            {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share", "applications", fileName),
                Path.Combine("/usr/local/share/applications", fileName),
                Path.Combine("/usr/share/applications", fileName),
                Path.Combine("/var/lib/flatpak/exports/share/applications", fileName),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share", "flatpak", "exports", "share", "applications", fileName)
            }.FirstOrDefault(File.Exists);
            if (desktopFile is null)
            {
                return null;
            }

            var (_, iconName) = DesktopItemLoader.ParseDesktopFile(desktopFile);
            var desktopIcon = DesktopItemLoader.LoadBitmapSafe(iconName);
            if (desktopIcon is not null)
            {
                return desktopIcon;
            }

            // A few applications, including the official VS Code package,
            // point their desktop entry at an icon name without installing it
            // in the shared theme directories. Look in their well-known app
            // resource root before using the neutral fallback.
            var executable = ReadDesktopEntryValue(desktopFile, "Exec")
                ?.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .FirstOrDefault();
            var command = executable is null ? null : Path.GetFileName(executable);
            if (string.IsNullOrWhiteSpace(command)
                || command.Any(character => !char.IsLetterOrDigit(character) && character is not '-' and not '_' and not '.'))
            {
                return null;
            }

            var names = new[] { command, iconName }.Where(name => !string.IsNullOrWhiteSpace(name));
            var roots = new[] { Path.Combine("/usr/share", command), Path.Combine("/opt", command) };
            foreach (var root in roots)
            {
                foreach (var name in names)
                {
                    foreach (var extension in new[] { ".png", ".svg" })
                    {
                        var bundledPath = Path.Combine(root, "resources", "app", "resources", "linux", name + extension);
                        var bundledIcon = DesktopItemLoader.LoadBitmapSafe(bundledPath);
                        if (bundledIcon is not null)
                        {
                            return bundledIcon;
                        }
                    }
                }
            }

            return null;
        }

        private static string? ReadDesktopEntryValue(string path, string key)
        {
            var inDesktopEntry = false;
            foreach (var line in File.ReadLines(path))
            {
                if (line.StartsWith('['))
                {
                    inDesktopEntry = string.Equals(line, "[Desktop Entry]", StringComparison.Ordinal);
                    continue;
                }

                if (inDesktopEntry && line.StartsWith(key + "=", StringComparison.Ordinal))
                {
                    return line[(key.Length + 1)..];
                }
            }

            return null;
        }

        private static double StackExtent(Button button) => button.Classes.Contains("WindowGrouped") ? 6 : 0;

        private double SlotX(int index) => index * (ItemWidth + Spacing)
            + _order.Take(index).Sum(StackExtent) * ItemWidth / 40;

        private void Icon_ContextRequested(object? sender, ContextRequestedEventArgs e)
        {
            e.Handled = true;
            if (sender is Button icon)
            {
                ShowContextMenuFor(icon);
            }
        }

        private Task<string>? _launchActivationToken;
        private string? _launchActivationApp;
        public Task<string>? TakeActivationToken(string appId)
        {
            var token = _launchActivationApp == appId ? _launchActivationToken : null;
            _launchActivationToken = null; _launchActivationApp = null;
            return token;
        }
        public void SetLaunching(string appId, bool launching)
        {
            var normalized = appId.EndsWith(".desktop", StringComparison.Ordinal) ? appId[..^8] : appId;
            foreach (var button in _order)
            {
                var tag = button.Tag as string ?? "";
                if (tag != normalized && tag != appId) continue;
                if (button.Content is Panel panel)
                    foreach (var indicator in panel.Children.OfType<Border>().Where(b => b.Classes.Contains("TaskbarWindowIndicator")))
                    {
                        if (Application.Current is App { ReduceMotionEnabled: true })
                            indicator.SetCurrentValue(Animatable.TransitionsProperty, (Transitions?)null);
                        else indicator.ClearValue(Animatable.TransitionsProperty);
                        indicator.Classes.Set("WindowLaunching", launching);
                    }
            }
        }

        private void Icon_PointerPressed(object? sender, PointerPressedEventArgs e)
        {
            HideHoverPreview();
            if (sender is not Button icon || _canvas is null) return;

            if (e.GetCurrentPoint(icon).Properties.IsRightButtonPressed)
            {
                _pendingActivationAppId = null;
                _pendingActivationWasActive = null;
                e.Handled = true;
                ShowContextMenuFor(icon);
                return;
            }

            if (!e.GetCurrentPoint(icon).Properties.IsLeftButtonPressed) return;

            HideContextMenu();
            if (icon.Tag is string appId)
            {
                CaptureActivationState(appId);
                _launchActivationApp = appId;
                _launchActivationToken = Avalonia.Wayland.WaylandActivation.RequestAsync(e, appId);
            }

            _dragItem = icon;
            _pointerDown = true;
            _isDragging = false;
            _pointerStartX = e.GetPosition(_canvas).X;
            _dragStartLeft = Canvas.GetLeft(icon);
            // Leave a normal press to Avalonia's Button lifecycle. Capture begins only once this
            // gesture crosses the drag threshold, preserving the same pressed/click effect as Start.
        }

        private void Icon_PointerMoved(object? sender, PointerEventArgs e)
        {
            if (!_pointerDown || _dragItem is null || _canvas is null) return;
            if (sender is not Button icon || icon != _dragItem) return;

            var currentX = e.GetPosition(_canvas).X;
            var delta = currentX - _pointerStartX;

            if (!_isDragging)
            {
                if (Math.Abs(delta) < DragThreshold) return;
                BeginDrag(icon);
                e.Pointer.Capture(icon);
            }

            var maxLeft = SlotX(_order.Count - 1);
            var newLeft = Math.Clamp(_dragStartLeft + delta, 0, Math.Max(0, maxLeft));

            // Track the pointer 1:1 - no position transition on the dragged item itself, keeping render transform.
            icon.Transitions = GetDragTransitions();
            Canvas.SetLeft(icon, newLeft);

            ReorderIfNeeded(icon, newLeft);
        }

        private void BeginDrag(Button icon)
        {
            _isDragging = true;
            _orderAtDragStart = _order.Select(b => b.Tag as string ?? string.Empty).ToList();
            icon.Classes.Add("dragging");
            icon.ZIndex = 1000;
        }

        private void ReorderIfNeeded(Button dragged, double draggedLeft)
        {
            var draggedCenter = draggedLeft + ItemWidth / 2;
            var index = _order.IndexOf(dragged);

            while (index > 0)
            {
                var leftBoundary = SlotX(index) - Spacing / 2;
                if (draggedCenter >= leftBoundary) break;

                var neighbor = _order[index - 1];
                (_order[index - 1], _order[index]) = (_order[index], _order[index - 1]);
                index--;

                AnimateToSlot(neighbor, index + 1);
            }

            while (index < _order.Count - 1)
            {
                var rightBoundary = SlotX(index + 1) - Spacing / 2;
                if (draggedCenter <= rightBoundary) break;

                var neighbor = _order[index + 1];
                (_order[index + 1], _order[index]) = (_order[index], _order[index + 1]);
                index++;

                AnimateToSlot(neighbor, index - 1);
            }
        }

        private void AnimateToSlot(Button icon, int index)
        {
            var left = SlotX(index);
            icon.Transitions = GetPositionTransitions();
            Canvas.SetLeft(icon, left);
        }

        private void Icon_PointerReleased(object? sender, PointerReleasedEventArgs e)
        {
            if (sender is not Button icon) return;
            EndInteraction(icon, e);
            e.Pointer.Capture(null);
        }

        private void Icon_PointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
        {
            if (sender is not Button icon) return;
            EndInteraction(icon, e);
        }

        private void EndInteraction(Button icon, RoutedEventArgs e)
        {
            if (!_pointerDown) return;

            var wasDragging = _isDragging;

            var index = _order.IndexOf(icon);
            if (index >= 0)
            {
                icon.Transitions = GetPositionTransitions();
                Canvas.SetLeft(icon, SlotX(index));
            }

            icon.Classes.Remove("dragging");
            icon.ZIndex = 0;

            if (wasDragging)
            {
                _pendingActivationAppId = null;
                _pendingActivationWasActive = null;
                e.Handled = true;
                _suppressNextClick = true;
                var newOrder = _order.Select(b => b.Tag as string ?? string.Empty).ToList();
                if (_orderAtDragStart != null && !newOrder.SequenceEqual(_orderAtDragStart))
                {
                    OrderChanged?.Invoke(this, GetCurrentPinnedOrder());
                }
            }
            _pointerDown = false;
            _isDragging = false;
            _dragItem = null;
            _orderAtDragStart = null;
        }

        // Let Button own normal activation. The pointer handlers above are only for reordering;
        // emitting activation from both paths made taps susceptible to capture/drag sequencing.
        private void Icon_Click(object? sender, RoutedEventArgs e)
        {
            HideContextMenu();

            if (_suppressNextClick)
            {
                _suppressNextClick = false;
                _pendingActivationAppId = null;
                _pendingActivationWasActive = null;
                return;
            }

            if (sender is Button { Tag: string tag })
            {
                AppIconClicked?.Invoke(this, tag);
            }
        }

        /// <summary>Detaches the context menu host so it can sit inside the native overlay layer.</summary>
        public Control? DetachContextMenuForNativeOverlay()
        {
            if (_contextMenuHost is null)
                return null;

            if (_contextMenuHost.Parent is Panel parent)
            {
                parent.Children.Remove(_contextMenuHost);
            }

            return _contextMenuHost;
        }

        public void HideContextMenu()
        {
            if (_activeContextMenuButton != null)
            {
                _activeContextMenuButton.Classes.Remove("ContextMenuActive");
                _activeContextMenuButton = null;
            }

            if (_contextMenuHost is not null && _contextMenuHost.IsVisible)
            {
                _contextMenuHost.IsVisible = false;
                ContextMenuVisibilityChanged?.Invoke(this, false);
            }
        }

        public string ResolveEffectiveAppId(string tag)
        {
            if (string.IsNullOrWhiteSpace(tag)) return string.Empty;

            if (_compositorRecords.TryGetValue(tag, out var record))
            {
                return ResolveEffectiveAppIdForWindow(record);
            }
            if (tag.StartsWith("kwin:", StringComparison.Ordinal) && _compositorRecords.TryGetValue(tag[5..], out var rec2))
            {
                return ResolveEffectiveAppIdForWindow(rec2);
            }
            return NormalizeAppId(tag);
        }

        public void ShowContextMenuFor(Button icon)
        {
            HideHoverPreview();
            var rawAppId = icon.Tag as string;
            if (string.IsNullOrWhiteSpace(rawAppId) || _contextMenuHost is null || _contextMenuItems is null)
            {
                return;
            }

            if (_activeContextMenuButton != null && _activeContextMenuButton != icon)
            {
                _activeContextMenuButton.Classes.Remove("ContextMenuActive");
            }
            _activeContextMenuButton = icon;
            _activeContextMenuButton.Classes.Add("ContextMenuActive");

            var effectiveAppId = ResolveEffectiveAppId(rawAppId);

            var isRunning = (_windowStates.TryGetValue(rawAppId, out var state) && state != NativeAppWindowState.NotRunning)
                || (_windowStates.TryGetValue(effectiveAppId, out var effState) && effState != NativeAppWindowState.NotRunning)
                || rawAppId.StartsWith("kwin:", StringComparison.Ordinal);
            var isPinned = IsPinned(effectiveAppId) || IsPinned(rawAppId);
            var appActions = DesktopActionReader.GetActions(effectiveAppId);
            if (appActions.Count == 0 && !string.Equals(rawAppId, effectiveAppId, StringComparison.Ordinal))
            {
                appActions = DesktopActionReader.GetActions(rawAppId);
            }

            _contextMenuItems.Children.Clear();

            // 1. "Open" - uniform across running and non-running states
            var openItem = CreateMenuItem("Open", "arrow-square-out");
            openItem.Click += (_, _) =>
            {
                HideContextMenu();
                _pendingActivationAppId = null;
                _pendingActivationWasActive = null;
                AppIconClicked?.Invoke(this, rawAppId);
            };
            _contextMenuItems.Children.Add(openItem);

            // Optional app-provided actions (e.g. New Window, New Private Window)
            // Exposed if present in application metadata
            if (appActions.Count > 0)
            {
                foreach (var action in appActions)
                {
                    var actionItem = CreateMenuItem(action.Name, ResolveActionIcon(action), localize: false);
                    actionItem.Click += (_, _) =>
                    {
                        HideContextMenu();
                        LaunchDesktopAction(action);
                    };
                    _contextMenuItems.Children.Add(actionItem);
                }
            }

            // Separator between Open/Actions and Pin/Unpin
            _contextMenuItems.Children.Add(CreateMenuSeparator());

            // 2. Pin / Unpin
            if (isPinned)
            {
                var unpinItem = CreateMenuItem("Unpin from Taskbar", "push-pin-slash");
                unpinItem.Click += (_, _) =>
                {
                    HideContextMenu();
                    UnpinApp(effectiveAppId, icon);
                };
                _contextMenuItems.Children.Add(unpinItem);
            }
            else
            {
                var pinItem = CreateMenuItem("Pin to Taskbar", "push-pin");
                pinItem.Click += (_, _) =>
                {
                    HideContextMenu();
                    PinApp(effectiveAppId, icon);
                };
                _contextMenuItems.Children.Add(pinItem);
            }

            // 3. Close (for running apps)
            if (isRunning)
            {
                _contextMenuItems.Children.Add(CreateMenuSeparator());

                var closeItem = CreateMenuItem("Close", "x", isDanger: true);
                closeItem.Click += (_, _) =>
                {
                    HideContextMenu();
                    CloseAppRequested?.Invoke(this, rawAppId);
                };
                _contextMenuItems.Children.Add(closeItem);
            }

            // 4. App Info (always at the bottom)
            var appInfoItem = CreateMenuItem("App Info", "info");
            appInfoItem.Click += (_, _) =>
            {
                HideContextMenu();
                AppInfoRequested?.Invoke(this, effectiveAppId);
            };
            _contextMenuItems.Children.Add(appInfoItem);

            // Compute position directly above the clicked taskbar icon
            PositionContextMenu(icon);

            _contextMenuHost.IsVisible = true;
            ContextMenuVisibilityChanged?.Invoke(this, true);
        }

        public void PinApp(string appId, Button? contextButton = null)
        {
            var effectiveId = ResolveInstalledAppId(ResolveEffectiveAppId(appId));
            _pinnedAppIds.Add(effectiveId);

            Button? runningButton = contextButton;
            if (runningButton is null)
            {
                runningButton = _order.FirstOrDefault(b =>
                {
                    if (b.Tag is not string tag) return false;
                    return string.Equals(tag, appId, StringComparison.OrdinalIgnoreCase)
                        || string.Equals(tag, effectiveId, StringComparison.OrdinalIgnoreCase)
                        || (tag.StartsWith("kwin:", StringComparison.Ordinal)
                            && _compositorWindowIds.TryGetValue(tag, out var wId)
                            && _compositorRecords.TryGetValue(wId, out var rec)
                            && string.Equals(ResolveEffectiveAppIdForWindow(rec), effectiveId, StringComparison.OrdinalIgnoreCase));
                });
            }

            if (runningButton is not null)
            {
                var oldTag = runningButton.Tag as string ?? appId;
                runningButton.Tag = effectiveId;
                if (!string.Equals(oldTag, effectiveId, StringComparison.Ordinal))
                {
                    if (_compositorWindowIds.Remove(oldTag, out var windowId))
                    {
                        _compositorWindowIds[effectiveId] = windowId;
                    }
                    if (_windowStates.Remove(oldTag, out var state))
                    {
                        _windowStates[effectiveId] = state;
                    }
                    if (_appNames.Remove(oldTag, out var name))
                    {
                        _appNames[effectiveId] = name;
                    }
                }
                ApplyWindowState(runningButton, effectiveId, _windowStates.GetValueOrDefault(effectiveId, NativeAppWindowState.NotRunning));
            }
            else if (!_order.Any(b => string.Equals(b.Tag as string, effectiveId, StringComparison.OrdinalIgnoreCase)))
            {
                AddPinnedAppButton(effectiveId);
            }

            OrderChanged?.Invoke(this, GetCurrentPinnedOrder());
        }

        public void UnpinApp(string appId, Button icon)
        {
            var effectiveId = ResolveEffectiveAppId(appId);
            _pinnedAppIds.Remove(effectiveId);
            _pinnedAppIds.Remove(appId);

            var rawTag = icon.Tag as string ?? appId;
            var isRunning = (_windowStates.TryGetValue(rawTag, out var state) && state != NativeAppWindowState.NotRunning)
                || (_windowStates.TryGetValue(effectiveId, out var effState) && effState != NativeAppWindowState.NotRunning)
                || _compositorWindowIds.ContainsKey(rawTag)
                || _compositorWindowIds.ContainsKey(effectiveId);

            if (!isRunning)
            {
                RemoveSpecificApp(icon);
            }
            else if (string.Equals(rawTag, effectiveId, StringComparison.OrdinalIgnoreCase) && _compositorWindowIds.TryGetValue(effectiveId, out var winId))
            {
                // If it is currently running as a pinned item, downgrade its tag to "kwin:<winId>" so it will be cleaned up on close
                var kwinTag = $"kwin:{winId}";
                icon.Tag = kwinTag;
                _compositorWindowIds.Remove(effectiveId);
                _compositorWindowIds[kwinTag] = winId;
                if (_windowStates.Remove(effectiveId, out var wState))
                {
                    _windowStates[kwinTag] = wState;
                }
                if (_appNames.Remove(effectiveId, out var aName))
                {
                    _appNames[kwinTag] = aName;
                }
            }

            OrderChanged?.Invoke(this, GetCurrentPinnedOrder());
        }

        public bool IsPinned(string appId) => _pinnedAppIds.Contains(appId) || _pinnedAppIds.Contains(ResolveEffectiveAppId(appId));

        public IReadOnlyList<string> GetCurrentPinnedOrder() =>
            _order
                .Select(b => b.Tag as string)
                .Where(id => !string.IsNullOrWhiteSpace(id) && !id!.StartsWith("kwin:", StringComparison.Ordinal) && (_pinnedAppIds.Contains(id!) || _pinnedAppIds.Contains(ResolveEffectiveAppId(id!))))
                .Select(id => ResolveEffectiveAppId(id!))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

        private string ResolveInstalledAppId(string appId)
        {
            var normalized = NormalizeAppId(appId);
            if (normalized is "mail" or "notes" or "files" or "settings" or "calendar" or "calculator" or "appstore" or "taskmanager" or "uikit" or "diskmanager" or "appinstaller")
                return normalized;
            var desktopFile = DesktopEntryResolver([appId]);
            return desktopFile is null ? normalized : NormalizeAppId(Path.GetFileName(desktopFile));
        }

        private string ResolveEffectiveAppIdForWindow(CompositorWindowRecord window)
        {
            if (!string.IsNullOrWhiteSpace(window.DaisyAppId)) return window.DaisyAppId;
            if (_resolvedWindowAppIds.TryGetValue(window.Id, out var cached)
                && cached.DesktopId == window.DesktopFileId && cached.AppId == window.AppId)
                return cached.ResolvedId;

            // KWin's desktopFileName can itself be a WM class or Wayland app ID.
            // Resolve it through installed metadata before persisting a launch ID.
            var desktopFile = DesktopEntryResolver([window.DesktopFileId, window.AppId]);
            var resolvedId = desktopFile is not null ? NormalizeAppId(Path.GetFileName(desktopFile))
                : !string.IsNullOrWhiteSpace(window.DesktopFileId) ? NormalizeAppId(window.DesktopFileId)
                : !string.IsNullOrWhiteSpace(window.AppId) ? NormalizeAppId(window.AppId) : window.Id;
            _resolvedWindowAppIds[window.Id] = (window.DesktopFileId, window.AppId, resolvedId);
            return resolvedId;
        }

        private Control CreateAppIconContent(string appId)
        {
            if (string.Equals(appId, "calendar", StringComparison.OrdinalIgnoreCase))
            {
                return new Grid
                {
                    Width = 32,
                    Height = 32,
                    Children = { new CalendarAppIcon() }
                };
            }

            if (string.Equals(appId, "appstore", StringComparison.OrdinalIgnoreCase))
            {
                return new Grid
                {
                    Width = 32,
                    Height = 32,
                    Children =
                    {
                        new DaisyOS.Shell.Controls.DaisyIcon
                        {
                            Icon = "package",
                            Size = 22,
                            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                            IsHitTestVisible = false
                        }
                    }
                };
            }

            string? resourceKey = appId.ToLowerInvariant() switch
            {
                "mail" => "AppIconMail",
                "notes" => "AppIconNotes",
                "calculator" => "AppIconCalculator",
                "settings" => "AppIconSettings",
                "files" => "AppIconFiles",
                "appstore" => "AppIconStore",
                "uikit" => "AppIconUiKit",
                "diskmanager" => "AppIconDiskManager",
                "appinstaller" => "AppIconAppInstaller",
                "taskmanager" => "AppIconSystemMonitor",
                "launcher" or "start" => "AppIconLauncher",
                _ => null
            };

            if (resourceKey != null)
            {
                var themedImage = new Image
                {
                    Classes = { "AppIcon" },
                    Width = 26,
                    Height = 26,
                    IsHitTestVisible = false
                };
                RenderOptions.SetBitmapInterpolationMode(themedImage, BitmapInterpolationMode.HighQuality);
                themedImage[!Image.SourceProperty] = new Avalonia.Markup.Xaml.MarkupExtensions.DynamicResourceExtension(resourceKey);

                return new Grid
                {
                    Width = 32,
                    Height = 32,
                    Children = { themedImage }
                };
            }

            var iconCandidates = new[]
            {
                appId,
                $"{appId}.desktop",
                NormalizeAppId(appId)
            };
            var iconPath = DesktopItemLoader.ResolveApplicationIconPath(iconCandidates);
            var image = string.IsNullOrWhiteSpace(iconPath)
                ? LoadDesktopEntryIcon(appId)
                : null;

            if (iconPath.EndsWith(".svg", StringComparison.OrdinalIgnoreCase))
            {
                return new Avalonia.Svg.Skia.Svg(new Uri("file:///"))
                {
                    Classes = { "AppIcon" },
                    Path = iconPath,
                    Width = 26,
                    Height = 26,
                    Stretch = Avalonia.Media.Stretch.Uniform,
                    EnableCache = true
                };
            }

            if (image is not null || !string.IsNullOrWhiteSpace(iconPath))
            {
                var icon = new Image
                {
                    Classes = { "AppIcon" },
                    Source = image ?? DesktopItemLoader.LoadBitmapSafe(iconPath),
                    Width = 26,
                    Height = 26,
                    Stretch = Avalonia.Media.Stretch.Uniform
                };
                RenderOptions.SetBitmapInterpolationMode(icon, BitmapInterpolationMode.HighQuality);
                return icon;
            }

            return new Border
            {
                Width = 32,
                Height = 32,
                Background = this.FindResource("StateSelectedBrush") as IBrush,
                CornerRadius = new CornerRadius(10),
                Child = new DaisyIcon
                {
                    Icon = "squares-four",
                    Size = 18,
                    Foreground = this.FindResource("ContentPrimaryBrush") as IBrush,
                    HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                    VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
                }
            };
        }

        private Button CreateAppIconButton(string appId)
        {
            var displayName = TaskbarAppLabel.Format(appId, _appNames.GetValueOrDefault(appId, appId));
            if (string.Equals(appId, "calendar", StringComparison.OrdinalIgnoreCase)) displayName = "Calendar";
            else if (string.Equals(appId, "diskmanager", StringComparison.OrdinalIgnoreCase)) displayName = "Disk Manager";
            else if (string.Equals(appId, "mail", StringComparison.OrdinalIgnoreCase)) displayName = "Mail";
            else if (string.Equals(appId, "notes", StringComparison.OrdinalIgnoreCase)) displayName = "Notes";
            else if (string.Equals(appId, "calculator", StringComparison.OrdinalIgnoreCase)) displayName = "Calculator";
            else if (string.Equals(appId, "settings", StringComparison.OrdinalIgnoreCase)) displayName = "Settings";
            else if (string.Equals(appId, "files", StringComparison.OrdinalIgnoreCase)) displayName = "Files";
            else if (string.Equals(appId, "appinstaller", StringComparison.OrdinalIgnoreCase)) displayName = "App Installer";
            else if (string.Equals(appId, "appstore", StringComparison.OrdinalIgnoreCase)) displayName = "App Store";

            var panel = new Panel
            {
                Width = 40,
                Height = 40,
                ClipToBounds = false,
                Children =
                {
                    new Grid
                    {
                        Width = 32,
                        Height = 32,
                        HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                        VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                        Children = { CreateAppIconContent(appId) }
                    },
                    new Border
                    {
                        Classes = { "TaskbarWindowIndicator" },
                        IsHitTestVisible = false
                    }
                }
            };

            var button = new Button
            {
                Classes = { "ShellButton", "TaskbarApp", "TaskbarAppIcon" },
                Tag = appId,
                Content = panel,
                [ToolTip.TipProperty] = displayName
            };
            AutomationProperties.SetName(button, displayName);
            _appNames[appId] = displayName;
            return button;
        }

        private void AddPinnedAppButton(string appId)
        {
            if (_canvas is null) return;
            var iconButton = CreateAppIconButton(appId);

            _canvas.Children.Add(iconButton);
            _order.Add(iconButton);

            WireUpIcon(iconButton);
            Canvas.SetLeft(iconButton, SlotX(_order.Count - 1));
            UpdateCanvasWidth();
        }

        private void PositionContextMenu(Button icon)
        {
            if (_contextMenuHost is null) return;

            var screenWidth = 1440.0;
            if (TopLevel.GetTopLevel(this) is { } topLevel && topLevel.Bounds.Width > 0)
            {
                screenWidth = topLevel.Bounds.Width;
            }
            else if (TopLevel.GetTopLevel(_contextMenuHost) is { } root && root.Bounds.Width > 0)
            {
                screenWidth = root.Bounds.Width;
            }

            var taskbarScale = this.TryFindResource("TaskbarScale", out var scaleResource) && scaleResource is double configuredScale ? configuredScale : 1;
            var taskbarWidth = _taskbarBody?.Bounds.Width > 0 ? _taskbarBody.Bounds.Width : ((_canvas?.Width ?? 0) + 58);
            var taskbarLeft = Math.Max(0, (screenWidth - taskbarWidth * taskbarScale) / 2);
            var iconLeft = Canvas.GetLeft(icon);
            // 50px offset = outer body padding (1px) + inner border (1px) + pad (4px) + StartButton (40px) + spacing (4px)
            var iconCenterOnScreen = taskbarLeft + (50 + iconLeft + (ItemWidth / 2)) * taskbarScale;

            const double estimatedMenuWidth = 200.0;
            var menuLeft = Math.Clamp(iconCenterOnScreen - (estimatedMenuWidth / 2), 12, screenWidth - estimatedMenuWidth - 12);

            _contextMenuHost.TailX = iconCenterOnScreen - menuLeft;
            _contextMenuHost.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left;
            // Keep the menu above the scaled taskbar, with its original 3px gap.
            _contextMenuHost.Margin = new Thickness(menuLeft, 0, 0, 52 * taskbarScale + 3);
        }



        private static void LaunchDesktopAction(DesktopActionInfo action)
        {
            try
            {
                var tokens = DaisyOS.System.Processes.DesktopExecParser.Parse(action.Exec);
                if (tokens != null && tokens.Count > 0)
                {
                    var launcher = new DaisyOS.System.Processes.SafeProcessLauncher();
                    launcher.Launch(tokens[0], tokens.Skip(1).ToArray());
                }
            }
            catch (Exception ex)
            {
                global::System.Console.Error.WriteLine($"[daisy-taskbar] Failed to launch desktop action '{action.Name}': {ex.Message}");
            }
        }

        private static string ResolveActionIcon(DesktopActionInfo action)
        {
            var name = action.Name.ToLowerInvariant();
            if (name.Contains("private") || name.Contains("incognito"))
            {
                return "eye-slash";
            }
            if (name.Contains("window"))
            {
                return "app-window";
            }
            if (name.Contains("tab"))
            {
                return "browser";
            }
            if (name.Contains("new") || name.Contains("add") || name.Contains("create"))
            {
                return "plus";
            }
            return "arrow-square-out";
        }

        private static MenuItem CreateMenuItem(string text, string iconName, bool isDanger = false, bool localize = true)
        {
            var item = new MenuItem
            {
                Header = text,
                Icon = new DaisyIcon
                {
                    Icon = iconName,
                    Classes = { "MenuIcon" }
                }
            };

            if (isDanger)
            {
                item.Classes.Add("Danger");
                if (Application.Current?.TryFindResource("StatusDangerBrush", out var dangerBrush) == true && dangerBrush is IBrush brush)
                {
                    item.Foreground = brush;
                }
            }

            return localize ? Loc.Bind(item, MenuItem.HeaderProperty, text) : item;
        }

        private static Separator CreateMenuSeparator()
        {
            return new Separator();
        }
    }
}
