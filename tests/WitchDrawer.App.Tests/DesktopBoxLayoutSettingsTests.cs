using WitchDrawer.App.ViewModels;

namespace WitchDrawer.App.Tests;

public sealed class DesktopBoxLayoutSettingsTests
{
    [Fact]
    public void MappingListDimensions_FollowIconDensityPreset()
    {
        var settings = new DesktopBoxLayoutSettings();

        settings.ApplyPresetCommand.Execute("3x3");
        var largeWidth = settings.MappingListWidth;
        var largeRowHeight = settings.MappingListRowHeight;

        settings.ApplyPresetCommand.Execute("5x5");
        Assert.Equal(310, settings.MappingListWidth);
        Assert.Equal(32.3, settings.MappingListRowHeight);

        settings.ApplyPresetCommand.Execute("6x6");
        Assert.True(settings.MappingListWidth < 310);
        Assert.True(settings.MappingListRowHeight < 32.3);
        Assert.True(largeWidth > 310);
        Assert.True(largeRowHeight > 32.3);
    }
}
