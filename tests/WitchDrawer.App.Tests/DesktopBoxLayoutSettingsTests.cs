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
        Assert.True(settings.IsCompactPreset);
        Assert.Equal(220, settings.MappingListWidth);
        Assert.Equal(24, settings.MappingListRowHeight);
        Assert.Equal(58, settings.MappingListMinHeight);
        Assert.Equal(12.5, settings.MappingListFontSize);
        Assert.Equal(14, settings.MappingListIconSize);
        Assert.True(largeWidth > 310);
        Assert.True(largeRowHeight > 32.3);
    }
}
