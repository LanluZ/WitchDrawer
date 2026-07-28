using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using WitchDrawer.App.Infrastructure;
using WitchDrawer.Core.Logging;
using WitchDrawer.Core.Models;

namespace WitchDrawer.App.ViewModels;

public sealed class DrawerItemViewModel : ObservableObject
{
    private const int MaxIconLoadAttempts = 4;

    private ImageSource? _iconImage;
    private bool _hasIcon;
    private int _isLoadingIcon;
    private int _isIconRequested;
    private int _iconRequestVersion;
    private int _requestedIconPixelSize;
    private int _gridColumn;
    private int _gridRow;
    private double _gridLeft;
    private double _gridTop;
    private bool _isDragSource;
    private double _tempOffsetX;
    private double _tempOffsetY;

    private readonly bool _isPixelated;
    private readonly IAppLogger? _logger;
    private readonly Func<string, bool, int, Task<ImageSource?>> _iconLoader;
    private CancellationTokenSource? _iconLoadCts;

    public DrawerItemViewModel(
        DrawerItem model,
        string? boxName = null,
        bool isPixelated = false,
        int iconPixelSize = 32,
        IAppLogger? logger = null)
        : this(model, boxName, isPixelated, iconPixelSize, logger, ShellIconProvider.GetIconAsync)
    {
    }

    internal DrawerItemViewModel(
        DrawerItem model,
        string? boxName,
        bool isPixelated,
        int iconPixelSize,
        IAppLogger? logger,
        Func<string, bool, int, Task<ImageSource?>> iconLoader)
    {
        Model = model;
        BoxName = boxName ?? string.Empty;
        _isPixelated = isPixelated;
        _logger = logger;
        _iconLoader = iconLoader;
        _requestedIconPixelSize = NormalizeIconPixelSize(iconPixelSize);
        _gridColumn = Math.Max(0, model.GridColumn ?? 0);
        _gridRow = Math.Max(0, model.GridRow ?? 0);
    }

    public DrawerItem Model { get; }

    public Guid Id => Model.Id;

    public string DisplayName
    {
        get
        {
            var name = Model.DisplayName;
            if (name.EndsWith(".lnk", System.StringComparison.OrdinalIgnoreCase))
            {
                return name[..^4];
            }
            return name;
        }
    }

    public string KindLabel => Model.ItemKind == ItemKind.Directory ? "文件夹" : "文件";

    public string KindBadge => Model.ItemKind == ItemKind.Directory ? "DIR" : "FILE";

    public string PathLabel => Model.EffectivePath ?? string.Empty;

    public string ShortPathLabel
    {
        get
        {
            var path = PathLabel;
            if (path.Length <= 48)
            {
                return path;
            }

            return "..." + path[^45..];
        }
    }

    public string BoxName { get; }

    public bool IsPixelated => _isPixelated;

    public int GridColumn
    {
        get => _gridColumn;
        private set => SetProperty(ref _gridColumn, value);
    }

    public int GridRow
    {
        get => _gridRow;
        private set => SetProperty(ref _gridRow, value);
    }

    public double GridLeft
    {
        get => _gridLeft;
        private set => SetProperty(ref _gridLeft, value);
    }

    public double GridTop
    {
        get => _gridTop;
        private set => SetProperty(ref _gridTop, value);
    }

    public bool IsDragSource
    {
        get => _isDragSource;
        set => SetProperty(ref _isDragSource, value);
    }

    public string FallbackIconText => Model.ItemKind == ItemKind.Directory ? "DIR" : GetFallbackExtension();

    public ImageSource? IconImage
    {
        get => _iconImage;
        private set
        {
            if (SetProperty(ref _iconImage, value))
            {
                HasIcon = value is not null;
            }
        }
    }

    public bool HasIcon
    {
        get => _hasIcon;
        private set => SetProperty(ref _hasIcon, value);
    }

    public void ReloadIconIfNeeded()
    {
        if (Volatile.Read(ref _isIconRequested) == 1 && !HasIcon)
        {
            QueueIconLoad();
        }
    }

    public void RequestIcon()
    {
        var wasRequested = Interlocked.Exchange(ref _isIconRequested, 1);
        if (wasRequested == 0)
        {
            QueueIconLoad();
        }
    }

    public void ReleaseIcon()
    {
        Interlocked.Exchange(ref _isIconRequested, 0);
        Interlocked.Increment(ref _iconRequestVersion);
        Interlocked.Exchange(ref _iconLoadCts, null)?.Cancel();
        IconImage = null;
    }

    public void RequestIconSize(int iconPixelSize)
    {
        var normalizedSize = NormalizeIconPixelSize(iconPixelSize);
        var previousSize = Interlocked.Exchange(ref _requestedIconPixelSize, normalizedSize);
        if (Volatile.Read(ref _isIconRequested) == 1
            && (previousSize != normalizedSize || !HasIcon))
        {
            QueueIconLoad();
        }
    }

    public void SetGridPosition(int column, int row, DesktopBoxLayoutSettings layoutSettings)
    {
        GridColumn = column;
        GridRow = row;
        UpdateCanvasPosition(layoutSettings);
    }

    public void SetTempOffset(double offsetX, double offsetY, DesktopBoxLayoutSettings layoutSettings)
    {
        _tempOffsetX = offsetX;
        _tempOffsetY = offsetY;
        UpdateCanvasPosition(layoutSettings);
    }

    public void UpdateCanvasPosition(DesktopBoxLayoutSettings layoutSettings)
    {
        GridLeft = GridColumn * layoutSettings.ItemSlotWidth + _tempOffsetX;
        GridTop = GridRow * layoutSettings.ItemSlotHeight + _tempOffsetY;
    }

    private void QueueIconLoad()
    {
        Interlocked.Increment(ref _iconRequestVersion);
        Interlocked.Exchange(ref _iconLoadCts, null)?.Cancel();
        _ = LoadIconAsync();
    }

    private async Task LoadIconAsync()
    {
        if (Volatile.Read(ref _isIconRequested) == 0
            || Interlocked.Exchange(ref _isLoadingIcon, 1) == 1)
        {
            return;
        }

        var requestVersion = Volatile.Read(ref _iconRequestVersion);
        using var loadCts = new CancellationTokenSource();
        Interlocked.Exchange(ref _iconLoadCts, loadCts)?.Cancel();
        var cancellationToken = loadCts.Token;
        var path = PathLabel;
        if (string.IsNullOrWhiteSpace(path))
        {
            Interlocked.CompareExchange(ref _iconLoadCts, null, loadCts);
            Interlocked.Exchange(ref _isLoadingIcon, 0);
            return;
        }

        var attemptedSize = Volatile.Read(ref _requestedIconPixelSize);
        try
        {
            var (icon, terminalException) = await LoadIconWithRetriesAsync(
                path,
                Model.ItemKind == ItemKind.Directory,
                attemptedSize,
                requestVersion,
                cancellationToken).ConfigureAwait(false);

            cancellationToken.ThrowIfCancellationRequested();
            if (!IsCurrentIconRequest(requestVersion))
            {
                return;
            }

            await SetIconOnUiThreadAsync(icon, requestVersion, cancellationToken);
            if (terminalException is not null)
            {
                _logger?.Error(
                    terminalException,
                    $"Failed to load icon for drawer item {Id:D} at {attemptedSize}px.");
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            if (IsCurrentIconRequest(requestVersion))
            {
                try
                {
                    await SetIconOnUiThreadAsync(null, requestVersion, CancellationToken.None);
                }
                catch
                {
                    // The WPF dispatcher can be unavailable while the app is shutting down.
                }
            }

            _logger?.Error(
                exception,
                $"Unexpected icon loading failure for drawer item {Id:D} at {attemptedSize}px.");
        }
        finally
        {
            Interlocked.CompareExchange(ref _iconLoadCts, null, loadCts);
            Interlocked.Exchange(ref _isLoadingIcon, 0);
            if (Volatile.Read(ref _isIconRequested) == 1
                && requestVersion != Volatile.Read(ref _iconRequestVersion))
            {
                _ = LoadIconAsync();
            }
        }
    }

    private async Task<(ImageSource? Icon, Exception? TerminalException)> LoadIconWithRetriesAsync(
        string path,
        bool isDirectory,
        int requestedSize,
        int requestVersion,
        CancellationToken cancellationToken)
    {
        Exception? terminalException = null;

        for (var attempt = 1; attempt <= MaxIconLoadAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var icon = await _iconLoader(path, isDirectory, requestedSize)
                    .WaitAsync(cancellationToken)
                    .ConfigureAwait(false);
                terminalException = null;

                if (icon is not null || attempt == MaxIconLoadAttempts)
                {
                    return (icon, null);
                }
            }
            catch (Exception exception)
            {
                terminalException = exception;
                if (attempt == MaxIconLoadAttempts)
                {
                    break;
                }
            }

            if (!IsCurrentIconRequest(requestVersion))
            {
                break;
            }

            await Task.Delay(150 * attempt, cancellationToken).ConfigureAwait(false);
        }

        return (null, terminalException);
    }

    private async Task SetIconOnUiThreadAsync(
        ImageSource? icon,
        int requestVersion,
        CancellationToken cancellationToken)
    {
        var application = Application.Current;
        if (application is null || application.Dispatcher.CheckAccess())
        {
            if (!cancellationToken.IsCancellationRequested && IsCurrentIconRequest(requestVersion))
            {
                IconImage = icon;
            }
            return;
        }

        await application.Dispatcher.InvokeAsync(() =>
        {
            if (!cancellationToken.IsCancellationRequested && IsCurrentIconRequest(requestVersion))
            {
                IconImage = icon;
            }
        });
    }

    private bool IsCurrentIconRequest(int requestVersion)
    {
        return Volatile.Read(ref _isIconRequested) == 1
            && requestVersion == Volatile.Read(ref _iconRequestVersion);
    }

    private static int NormalizeIconPixelSize(int iconPixelSize)
    {
        return Math.Clamp(
            iconPixelSize,
            DpiAwareIconSize.MinimumSourcePixelSize,
            DpiAwareIconSize.MaximumSourcePixelSize);
    }

    private string GetFallbackExtension()
    {
        var extension = Path.GetExtension(DisplayName).TrimStart('.');
        if (string.IsNullOrWhiteSpace(extension))
        {
            return "FILE";
        }

        return extension.Length <= 4 ? extension.ToUpperInvariant() : extension[..4].ToUpperInvariant();
    }
}
