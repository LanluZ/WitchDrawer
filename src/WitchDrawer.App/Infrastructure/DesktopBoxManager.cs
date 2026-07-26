using System.Threading;
using System.Windows;
using CommunityToolkit.Mvvm.Messaging;
using WitchDrawer.App.Messages;
using WitchDrawer.App.ViewModels;
using WitchDrawer.App.Views;
using WitchDrawer.Core.Abstractions;
using WitchDrawer.Core.Logging;
using WitchDrawer.Core.Services;

namespace WitchDrawer.App.Infrastructure;

public sealed class DesktopBoxManager
{
    private const string BoxPositionSettingPrefix = "BoxPosition:";
    private const char PositionSeparator = ',';

    private readonly DrawerService _drawerService;
    private readonly TodoService _todoService;
    private readonly IFileLauncher _launcher;
    private readonly IAppLogger _logger;
    private readonly Dictionary<Guid, DesktopBoxWindow> _windows = [];
    private bool _closing;
    private GuideLineWindow? _verticalGuide;
    private GuideLineWindow? _horizontalGuide;
    private bool _isAdjustingPosition;

    public DesktopBoxManager(
        DrawerService drawerService,
        TodoService todoService,
        IFileLauncher launcher,
        IAppLogger logger)
    {
        _drawerService = drawerService;
        _todoService = todoService;
        _launcher = launcher;
        _logger = logger;
        WeakReferenceMessenger.Default.Register<DesktopBoxManager, BoxLayoutPresetChangedMessage>(
            this,
            static (recipient, message) => recipient.ApplyBoxLayoutPreset(message));
    }

    public event EventHandler? ItemsChanged;

    private int _refreshVersion;
    private readonly SemaphoreSlim _refreshGate = new(1, 1);

    public async Task RefreshAsync()
    {
        if (_closing)
        {
            return;
        }

        var version = Interlocked.Increment(ref _refreshVersion);
        await _refreshGate.WaitAsync();
        try
        {
            if (_closing || version != Volatile.Read(ref _refreshVersion))
            {
                return;
            }

            var boxes = await _drawerService.GetBoxesAsync();
            if (_closing || version != Volatile.Read(ref _refreshVersion))
            {
                return;
            }

            var boxIds = boxes.Select(box => box.Id).ToHashSet();

            foreach (var removedId in _windows.Keys.Where(id => !boxIds.Contains(id)).ToArray())
            {
                var win = _windows[removedId];
                win.LocationChanged -= OnWindowLocationChanged;
                win.PreviewMouseLeftButtonUp -= OnWindowMouseUp;
                win.ForceClose();
                _windows.Remove(removedId);
            }

            for (var index = 0; index < boxes.Count; index++)
            {
                if (_closing || version != Volatile.Read(ref _refreshVersion))
                {
                    return;
                }

                var box = boxes[index];
                if (!_windows.TryGetValue(box.Id, out var window))
                {
                    var layoutSettings = new DesktopBoxLayoutSettings();
                    var savedPreset = await _drawerService.GetSettingAsync(
                        BoxViewModel.GetLayoutPresetSettingKey(box.Id));
                    layoutSettings.ApplyPresetWithoutCallback(savedPreset);

                    var viewModel = new DesktopBoxViewModel(
                        box,
                        _drawerService,
                        _todoService,
                        _launcher,
                        _logger,
                        layoutSettings);
                    viewModel.ItemsChanged += (_, _) => ItemsChanged?.Invoke(this, EventArgs.Empty);

                    window = new DesktopBoxWindow(viewModel);
                    await PlaceWindowAsync(window, box.Id, index);
                    _windows.Add(box.Id, window);

                    window.LocationChanged += OnWindowLocationChanged;
                    window.PreviewMouseLeftButtonUp += OnWindowMouseUp;
                    window.SetPositionChangedCallback(async (id) =>
                    {
                        _isAdjustingPosition = true;
                        try
                        {
                            PerformSnappingAndAlignment(window, applySnap: true);
                        }
                        finally
                        {
                            _isAdjustingPosition = false;
                        }
                        HideGuides();
                        await SavePositionAsync(id);
                    });

                    window.Show();
                    window.QueueSendToBottom();
                }
                else
                {
                    window.ViewModel.UpdateBox(box);
                }

                await window.ViewModel.LoadAsync();
                window.QueueSendToBottom();
            }
        }
        finally
        {
            _refreshGate.Release();
        }
    }

    /// <summary>
    /// Reloads item lists for existing desktop windows without recreating them.
    /// </summary>
    public async Task RefreshItemsAsync()
    {
        if (_closing)
        {
            return;
        }

        await _refreshGate.WaitAsync();
        try
        {
            if (_closing)
            {
                return;
            }

            foreach (var window in _windows.Values.ToArray())
            {
                await window.ViewModel.LoadAsync();
            }
        }
        finally
        {
            _refreshGate.Release();
        }
    }

    public async Task SaveAllPositionsAsync()
    {
        foreach (var (boxId, window) in _windows)
        {
            var key = BoxPositionSettingPrefix + boxId.ToString("N");
            var value = $"{window.Left}{PositionSeparator}{window.Top}";
            await _drawerService.SetSettingAsync(key, value);
        }
    }

    /// <summary>
    /// Reopens the desktop window for a box that was hidden via its close (X)
    /// button. If the window still exists in memory it is simply shown again;
    /// otherwise a full refresh is triggered so it gets recreated.
    /// </summary>
    /// <returns><see langword="true"/> if a window was shown; <see langword="false"/> otherwise.</returns>
    public async Task<bool> ShowAsync(Guid boxId)
    {
        if (_closing)
        {
            return false;
        }

        if (_windows.TryGetValue(boxId, out var window) && !window.IsVisible)
        {
            window.Show();
            window.QueueSendToBottom();
            return true;
        }

        // Window was destroyed (e.g. fully closed) or never created this session:
        // refresh so the box window is recreated for the current box set.
        await RefreshAsync();
        return _windows.TryGetValue(boxId, out var refreshed) && refreshed.IsVisible;
    }

    public async Task SavePositionAsync(Guid boxId)
    {
        if (!_windows.TryGetValue(boxId, out var window))
        {
            return;
        }

        var key = BoxPositionSettingPrefix + boxId.ToString("N");
        var value = $"{window.Left}{PositionSeparator}{window.Top}";
        await _drawerService.SetSettingAsync(key, value);
    }

    public async Task CloseAllAsync()
    {
        _closing = true;
        await SaveAllPositionsAsync();
        foreach (var window in _windows.Values)
        {
            window.LocationChanged -= OnWindowLocationChanged;
            window.PreviewMouseLeftButtonUp -= OnWindowMouseUp;
            window.ForceClose();
        }

        _windows.Clear();

        _verticalGuide?.Close();
        _verticalGuide = null;
        _horizontalGuide?.Close();
        _horizontalGuide = null;
        WeakReferenceMessenger.Default.UnregisterAll(this);
    }

    private void ApplyBoxLayoutPreset(BoxLayoutPresetChangedMessage message)
    {
        if (!_windows.TryGetValue(message.BoxId, out var window))
        {
            return;
        }

        window.ViewModel.LayoutSettings.ApplyPresetWithoutCallback(message.Preset);
    }

    private async Task PlaceWindowAsync(Window window, Guid boxId, int fallbackIndex)
    {
        // SizeToContent windows report NaN for Width/Height before they are shown; measure
        // first and use DesiredSize so saved positions are restored correctly.
        window.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));

        var savedPosition = await _drawerService.GetSettingAsync(BoxPositionSettingPrefix + boxId.ToString("N"));
        if (TryParsePosition(savedPosition, out var left, out var top))
        {
            var workArea = SystemParameters.WorkArea;
            window.Left = Math.Max(workArea.Left, Math.Min(left, workArea.Right - window.DesiredSize.Width));
            window.Top = Math.Max(workArea.Top, Math.Min(top, workArea.Bottom - window.DesiredSize.Height));
            return;
        }

        PlaceNewWindow(window, fallbackIndex);
    }

    private static bool TryParsePosition(string? raw, out double left, out double top)
    {
        left = 0;
        top = 0;
        if (string.IsNullOrEmpty(raw))
        {
            return false;
        }

        var parts = raw.Split(PositionSeparator);
        if (parts.Length != 2)
        {
            return false;
        }

        return double.TryParse(parts[0], out left) && double.TryParse(parts[1], out top);
    }

    private static void PlaceNewWindow(Window window, int index)
    {
        const double margin = 18;
        const double gap = 12;
        const double topPadding = 84;

        var workArea = SystemParameters.WorkArea;
        var centerX = workArea.Left + (workArea.Width - window.DesiredSize.Width) / 2;
        var centerY = workArea.Top + (workArea.Height - window.DesiredSize.Height) / 2;

        var offset = index * (window.DesiredSize.Width + gap);
        window.Left = Math.Max(workArea.Left + margin, Math.Min(centerX + offset, workArea.Right - window.DesiredSize.Width - margin));
        window.Top = Math.Max(workArea.Top + margin, Math.Min(centerY + topPadding * 0.5, workArea.Bottom - window.DesiredSize.Height - margin));
    }

    private void OnWindowLocationChanged(object? sender, EventArgs e)
    {
        if (_isAdjustingPosition || _closing)
        {
            return;
        }

        if (sender is not DesktopBoxWindow draggedWindow)
        {
            return;
        }

        if (System.Windows.Input.Mouse.LeftButton != System.Windows.Input.MouseButtonState.Pressed)
        {
            HideGuides();
            return;
        }

        _isAdjustingPosition = true;
        try
        {
            PerformSnappingAndAlignment(draggedWindow, applySnap: false);
        }
        finally
        {
            _isAdjustingPosition = false;
        }
    }

    private void OnWindowMouseUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        HideGuides();
        if (sender is DesktopBoxWindow window)
        {
            _ = SavePositionAsync(window.ViewModel.BoxId);
        }
    }

    private void HideGuides()
    {
        HideVerticalGuide();
        HideHorizontalGuide();
    }

    private void ShowVerticalGuide(double x, double yStart, double height)
    {
        if (_verticalGuide == null)
        {
            _verticalGuide = new GuideLineWindow(true);
        }
        _verticalGuide.UpdateLine(x, yStart, x, yStart + height);
        if (!_verticalGuide.IsVisible)
        {
            _verticalGuide.Show();
        }
    }

    private void HideVerticalGuide()
    {
        _verticalGuide?.Hide();
    }

    private void ShowHorizontalGuide(double y, double xStart, double width)
    {
        if (_horizontalGuide == null)
        {
            _horizontalGuide = new GuideLineWindow(false);
        }
        _horizontalGuide.UpdateLine(xStart, y, xStart + width, y);
        if (!_horizontalGuide.IsVisible)
        {
            _horizontalGuide.Show();
        }
    }

    private void HideHorizontalGuide()
    {
        _horizontalGuide?.Hide();
    }

    private void PerformSnappingAndAlignment(DesktopBoxWindow draggedWindow, bool applySnap = true)
    {
        const double snapThreshold = 10.0;
        const double visualGap = 8.0;

        var boundsA = GetVisibleBounds(draggedWindow);
        double currentLeft = boundsA.Left;
        double currentTop = boundsA.Top;
        double width = boundsA.Width;
        double height = boundsA.Height;
        double rightA = boundsA.Right;
        double bottomA = boundsA.Bottom;
        double hCenterA = currentLeft + width / 2.0;
        double vCenterA = currentTop + height / 2.0;
        double leftInset = boundsA.Left - draggedWindow.Left;
        double topInset = boundsA.Top - draggedWindow.Top;

        double? bestSnappedVisibleLeft = null;
        double? bestSnappedVisibleTop = null;

        double? verticalGuideX = null;
        double verticalGuideYMin = double.MaxValue;
        double verticalGuideYMax = double.MinValue;

        double? horizontalGuideY = null;
        double horizontalGuideXMin = double.MaxValue;
        double horizontalGuideXMax = double.MinValue;

        foreach (var pair in _windows)
        {
            var otherWindow = pair.Value;
            if (otherWindow == draggedWindow || !otherWindow.IsVisible)
            {
                continue;
            }

            var boundsB = GetVisibleBounds(otherWindow);
            double leftB = boundsB.Left;
            double topB = boundsB.Top;
            double widthB = boundsB.Width;
            double heightB = boundsB.Height;
            double rightB = boundsB.Right;
            double bottomB = boundsB.Bottom;
            double hCenterB = leftB + widthB / 2.0;
            double vCenterB = topB + heightB / 2.0;

            // 1. Vertical snapping
            if (Math.Abs(currentLeft - leftB) <= snapThreshold)
            {
                bestSnappedVisibleLeft = leftB;
                verticalGuideX = leftB;
                verticalGuideYMin = Math.Min(verticalGuideYMin, Math.Min(currentTop, topB));
                verticalGuideYMax = Math.Max(verticalGuideYMax, Math.Max(bottomA, bottomB));
            }
            else if (Math.Abs(rightA - rightB) <= snapThreshold)
            {
                bestSnappedVisibleLeft = rightB - width;
                verticalGuideX = rightB;
                verticalGuideYMin = Math.Min(verticalGuideYMin, Math.Min(currentTop, topB));
                verticalGuideYMax = Math.Max(verticalGuideYMax, Math.Max(bottomA, bottomB));
            }
            else if (Math.Abs(currentLeft - (rightB + visualGap)) <= snapThreshold)
            {
                bestSnappedVisibleLeft = rightB + visualGap;
                verticalGuideX = rightB + visualGap / 2.0;
                verticalGuideYMin = Math.Min(verticalGuideYMin, Math.Min(currentTop, topB));
                verticalGuideYMax = Math.Max(verticalGuideYMax, Math.Max(bottomA, bottomB));
            }
            else if (Math.Abs(rightA - (leftB - visualGap)) <= snapThreshold)
            {
                bestSnappedVisibleLeft = leftB - visualGap - width;
                verticalGuideX = leftB - visualGap / 2.0;
                verticalGuideYMin = Math.Min(verticalGuideYMin, Math.Min(currentTop, topB));
                verticalGuideYMax = Math.Max(verticalGuideYMax, Math.Max(bottomA, bottomB));
            }
            else if (Math.Abs(hCenterA - hCenterB) <= snapThreshold)
            {
                bestSnappedVisibleLeft = hCenterB - width / 2.0;
                verticalGuideX = hCenterB;
                verticalGuideYMin = Math.Min(verticalGuideYMin, Math.Min(currentTop, topB));
                verticalGuideYMax = Math.Max(verticalGuideYMax, Math.Max(bottomA, bottomB));
            }

            // 2. Horizontal snapping
            if (Math.Abs(currentTop - topB) <= snapThreshold)
            {
                bestSnappedVisibleTop = topB;
                horizontalGuideY = topB;
                horizontalGuideXMin = Math.Min(horizontalGuideXMin, Math.Min(currentLeft, leftB));
                horizontalGuideXMax = Math.Max(horizontalGuideXMax, Math.Max(rightA, rightB));
            }
            else if (Math.Abs(bottomA - bottomB) <= snapThreshold)
            {
                bestSnappedVisibleTop = bottomB - height;
                horizontalGuideY = bottomB;
                horizontalGuideXMin = Math.Min(horizontalGuideXMin, Math.Min(currentLeft, leftB));
                horizontalGuideXMax = Math.Max(horizontalGuideXMax, Math.Max(rightA, rightB));
            }
            else if (Math.Abs(currentTop - (bottomB + visualGap)) <= snapThreshold)
            {
                bestSnappedVisibleTop = bottomB + visualGap;
                horizontalGuideY = bottomB + visualGap / 2.0;
                horizontalGuideXMin = Math.Min(horizontalGuideXMin, Math.Min(currentLeft, leftB));
                horizontalGuideXMax = Math.Max(horizontalGuideXMax, Math.Max(rightA, rightB));
            }
            else if (Math.Abs(bottomA - (topB - visualGap)) <= snapThreshold)
            {
                bestSnappedVisibleTop = topB - visualGap - height;
                horizontalGuideY = topB - visualGap / 2.0;
                horizontalGuideXMin = Math.Min(horizontalGuideXMin, Math.Min(currentLeft, leftB));
                horizontalGuideXMax = Math.Max(horizontalGuideXMax, Math.Max(rightA, rightB));
            }
            else if (Math.Abs(vCenterA - vCenterB) <= snapThreshold)
            {
                bestSnappedVisibleTop = vCenterB - height / 2.0;
                horizontalGuideY = vCenterB;
                horizontalGuideXMin = Math.Min(horizontalGuideXMin, Math.Min(currentLeft, leftB));
                horizontalGuideXMax = Math.Max(horizontalGuideXMax, Math.Max(rightA, rightB));
            }
        }

        if (applySnap)
        {
            if (bestSnappedVisibleLeft.HasValue)
            {
                draggedWindow.Left = bestSnappedVisibleLeft.Value - leftInset;
            }
            if (bestSnappedVisibleTop.HasValue)
            {
                draggedWindow.Top = bestSnappedVisibleTop.Value - topInset;
            }
        }

        if (verticalGuideX.HasValue && verticalGuideYMax > verticalGuideYMin)
        {
            ShowVerticalGuide(verticalGuideX.Value, verticalGuideYMin, verticalGuideYMax - verticalGuideYMin);
        }
        else
        {
            HideVerticalGuide();
        }

        if (horizontalGuideY.HasValue && horizontalGuideXMax > horizontalGuideXMin)
        {
            ShowHorizontalGuide(horizontalGuideY.Value, horizontalGuideXMin, horizontalGuideXMax - horizontalGuideXMin);
        }
        else
        {
            HideHorizontalGuide();
        }
    }

    private static Rect GetVisibleBounds(DesktopBoxWindow window)
    {
        var margin = window.WindowBorder.Margin;
        return new Rect(
            window.Left + margin.Left,
            window.Top + margin.Top,
            Math.Max(0, window.ActualWidth - margin.Left - margin.Right),
            Math.Max(0, window.ActualHeight - margin.Top - margin.Bottom));
    }
}
