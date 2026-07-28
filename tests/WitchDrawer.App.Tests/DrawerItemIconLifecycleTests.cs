using System.Windows.Media;
using System.Windows.Media.Imaging;
using WitchDrawer.App.ViewModels;
using WitchDrawer.Core.Models;

namespace WitchDrawer.App.Tests;

public sealed class DrawerItemIconLifecycleTests
{
    [Fact]
    public async Task ConstructingManyItems_DoesNotLoadIconsUntilRequested()
    {
        var loadCount = 0;
        var viewModels = Enumerable.Range(0, 500)
            .Select(_ => CreateViewModel((_, _, _) =>
            {
                Interlocked.Increment(ref loadCount);
                return Task.FromResult<ImageSource?>(null);
            }))
            .ToArray();

        await Task.Delay(25);

        Assert.Equal(0, Volatile.Read(ref loadCount));

        viewModels[0].RequestIcon();
        await WaitUntilAsync(() => Volatile.Read(ref loadCount) == 1);
    }

    [Fact]
    public async Task ReleaseIcon_DropsLateAsyncResult()
    {
        var loadStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var loadCompletion = new TaskCompletionSource<ImageSource?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var viewModel = CreateViewModel((_, _, _) =>
        {
            loadStarted.TrySetResult();
            return loadCompletion.Task;
        });

        viewModel.RequestIcon();
        await loadStarted.Task;
        viewModel.ReleaseIcon();
        loadCompletion.SetResult(CreateIcon());
        await Task.Delay(25);

        Assert.False(viewModel.HasIcon);
        Assert.Null(viewModel.IconImage);
    }

    private static DrawerItemViewModel CreateViewModel(
        Func<string, bool, int, Task<ImageSource?>> iconLoader)
    {
        var item = new DrawerItem(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "example.txt",
            ItemKind.File,
            @"C:\missing\example.txt",
            null,
            0,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow);

        return new DrawerItemViewModel(item, null, false, 32, null, iconLoader);
    }

    private static BitmapSource CreateIcon()
    {
        var pixels = new byte[16 * 16 * 4];
        var source = BitmapSource.Create(
            16,
            16,
            96,
            96,
            PixelFormats.Bgra32,
            null,
            pixels,
            16 * 4);
        source.Freeze();
        return source;
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        while (!condition())
        {
            await Task.Delay(10, timeout.Token);
        }
    }
}
