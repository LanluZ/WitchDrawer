using System.ComponentModel;
using System.Windows.Interop;
using WitchDrawer.Core.Logging;
using WitchDrawer.Native.HotKeys;

namespace WitchDrawer.App.Infrastructure;

internal sealed class QuickPanelHotKeyService : IDisposable
{
    private const int WmHotKey = 0x0312;
    private const int QuickPanelHotKeyId = 0x5744;
    private static readonly nint HwndMessage = new(-3);

    private readonly QuickPanelHotKeySettingsStore _settings;
    private readonly IAppLogger _logger;
    private readonly HwndSource _messageSource;
    private readonly NativeHotKey _hotKey;
    private bool _disposed;

    public QuickPanelHotKeyService(
        QuickPanelHotKeySettingsStore settings,
        QuickPanelHotKey initialHotKey,
        IAppLogger logger)
    {
        _settings = settings;
        _logger = logger;
        CurrentHotKey = initialHotKey;

        var parameters = new HwndSourceParameters("WitchDrawer.QuickPanelHotKey")
        {
            Width = 0,
            Height = 0,
            WindowStyle = 0,
            ParentWindow = HwndMessage
        };
        _messageSource = new HwndSource(parameters);
        _messageSource.AddHook(WndProc);
        _hotKey = new NativeHotKey(_messageSource.Handle, QuickPanelHotKeyId);
        RegisterInitialHotKey();
    }

    public event EventHandler? Pressed;

    public QuickPanelHotKey CurrentHotKey { get; private set; }

    public bool IsRegistered { get; private set; }

    public string RegistrationStatusText { get; private set; } = "已启用，点击按钮可修改";

    public async Task<string> ApplyAsync(QuickPanelHotKey candidate)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (candidate == CurrentHotKey && IsRegistered)
        {
            return "快捷键未更改";
        }

        var previous = CurrentHotKey;
        var previousWasRegistered = IsRegistered;
        try
        {
            _hotKey.Register(candidate.RegistrationModifiers, candidate.VirtualKey);
            IsRegistered = true;
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "Failed to register the requested quick panel hotkey.");
            RestorePreviousHotKey(previous, previousWasRegistered);
            return GetHotKeyErrorText(exception);
        }

        try
        {
            await _settings.SaveAsync(candidate);
            CurrentHotKey = candidate;
            RegistrationStatusText = "已启用，点击按钮可修改";
            return "已保存并立即生效";
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "Failed to save quick panel hotkey.");
            RestorePreviousHotKey(previous, previousWasRegistered);
            return "保存失败，已恢复原快捷键";
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _messageSource.RemoveHook(WndProc);
        _hotKey.Dispose();
        _messageSource.Dispose();
    }

    private void RegisterInitialHotKey()
    {
        try
        {
            _hotKey.Register(CurrentHotKey.RegistrationModifiers, CurrentHotKey.VirtualKey);
            IsRegistered = true;
            RegistrationStatusText = "已启用，点击按钮可修改";
        }
        catch (Exception exception)
        {
            IsRegistered = false;
            RegistrationStatusText = GetHotKeyErrorText(exception);
            _logger.Error(exception, "Failed to register configured quick panel hotkey.");
        }
    }

    private void RestorePreviousHotKey(QuickPanelHotKey previous, bool previousWasRegistered)
    {
        if (!previousWasRegistered)
        {
            _hotKey.Unregister();
            IsRegistered = false;
            return;
        }

        try
        {
            _hotKey.Register(previous.RegistrationModifiers, previous.VirtualKey);
            IsRegistered = true;
        }
        catch (Exception restoreException)
        {
            IsRegistered = false;
            _logger.Error(restoreException, "Failed to restore previous quick panel hotkey.");
        }
    }

    private nint WndProc(nint hwnd, int message, nint wParam, nint lParam, ref bool handled)
    {
        if (message == WmHotKey && wParam.ToInt32() == QuickPanelHotKeyId)
        {
            handled = true;
            Pressed?.Invoke(this, EventArgs.Empty);
        }

        return nint.Zero;
    }

    private static string GetHotKeyErrorText(Exception exception)
    {
        return exception is Win32Exception { NativeErrorCode: 1409 }
            ? "快捷键已被其他程序占用，请换一个组合"
            : "快捷键注册失败，请换一个组合重试";
    }
}
