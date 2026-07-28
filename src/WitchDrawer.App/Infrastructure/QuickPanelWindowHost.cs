namespace WitchDrawer.App.Infrastructure;

internal interface IQuickPanelWindow
{
    Task ToggleAsync();

    void ForceClose();
}

internal sealed class QuickPanelWindowHost(Func<IQuickPanelWindow> windowFactory)
{
    private IQuickPanelWindow? _window;

    internal bool IsWindowCreated => _window is not null;

    public Task ToggleAsync()
    {
        _window ??= windowFactory();
        return _window.ToggleAsync();
    }

    public void Close()
    {
        _window?.ForceClose();
        _window = null;
    }
}
