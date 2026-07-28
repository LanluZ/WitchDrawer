using System.Windows.Media.Imaging;
using WitchDrawer.App.Infrastructure;

namespace WitchDrawer.App.Tests;

public sealed class ShellIconProviderTests
{
    [Fact]
    public async Task GetIconAsync_ExistingExecutable_ReturnsRequestedPixelDimensions()
    {
        var executablePath = Environment.ProcessPath;
        Assert.False(string.IsNullOrWhiteSpace(executablePath));

        var icon = await ShellIconProvider.GetIconAsync(
            executablePath,
            isDirectory: false,
            size: 48);

        var bitmap = Assert.IsAssignableFrom<BitmapSource>(icon);
        Assert.Equal(48, bitmap.PixelWidth);
        Assert.Equal(48, bitmap.PixelHeight);
    }
}
