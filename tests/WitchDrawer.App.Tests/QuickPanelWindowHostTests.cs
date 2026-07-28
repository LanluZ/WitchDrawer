using WitchDrawer.App.Infrastructure;

namespace WitchDrawer.App.Tests;

public sealed class QuickPanelWindowHostTests
{
    [Fact]
    public async Task Window_IsCreatedOnlyOnFirstToggle_AndReused()
    {
        var created = 0;
        var window = new FakeQuickPanelWindow();
        var host = new QuickPanelWindowHost(() =>
        {
            created++;
            return window;
        });

        Assert.False(host.IsWindowCreated);
        Assert.Equal(0, created);

        await host.ToggleAsync();
        await host.ToggleAsync();

        Assert.True(host.IsWindowCreated);
        Assert.Equal(1, created);
        Assert.Equal(2, window.ToggleCount);
    }

    [Fact]
    public void Close_DoesNotCreateAnUnusedWindow()
    {
        var created = 0;
        var host = new QuickPanelWindowHost(() =>
        {
            created++;
            return new FakeQuickPanelWindow();
        });

        host.Close();

        Assert.Equal(0, created);
    }

    private sealed class FakeQuickPanelWindow : IQuickPanelWindow
    {
        public int ToggleCount { get; private set; }

        public Task ToggleAsync()
        {
            ToggleCount++;
            return Task.CompletedTask;
        }

        public void ForceClose()
        {
        }
    }
}
