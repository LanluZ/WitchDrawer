using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;

namespace WitchDrawer.App.ViewModels;

public sealed partial class DesktopBoxLayoutSettings : ObservableObject
{
    private double _iconSize = 20;
    private double _iconFrameSize = 30;
    private double _itemSpacing = 1;
    private double _itemSlotWidth = 51;
    private double _itemSlotHeight = 44;
    private Thickness _itemPadding = new Thickness(2, 1, 2, 1);
    private double _iconFontSize = 9;
    private TextWrapping _iconTextWrapping = TextWrapping.NoWrap;
    private double _iconTextMaxHeight = 14;
    private CornerRadius _itemCornerRadius = new CornerRadius(8);
    private CornerRadius _iconCornerRadius = new CornerRadius(6);
    private int _columns = 5;
    private string _currentPreset = "5x5";
    private Func<string, Task>? _presetChangedCallback;

    public double IconSize
    {
        get => _iconSize;
        set => SetProperty(ref _iconSize, value);
    }

    public double IconFrameSize
    {
        get => _iconFrameSize;
        set => SetProperty(ref _iconFrameSize, value);
    }

    public double ItemSpacing
    {
        get => _itemSpacing;
        set
        {
            if (SetProperty(ref _itemSpacing, value))
            {
                OnPropertyChanged(nameof(ItemMargin));
            }
        }
    }

    public double ItemSlotWidth
    {
        get => _itemSlotWidth;
        set => SetProperty(ref _itemSlotWidth, value);
    }

    public double ItemSlotHeight
    {
        get => _itemSlotHeight;
        set => SetProperty(ref _itemSlotHeight, value);
    }

    public Thickness ItemPadding
    {
        get => _itemPadding;
        set => SetProperty(ref _itemPadding, value);
    }

    public double IconFontSize
    {
        get => _iconFontSize;
        set => SetProperty(ref _iconFontSize, value);
    }

    public TextWrapping IconTextWrapping
    {
        get => _iconTextWrapping;
        set => SetProperty(ref _iconTextWrapping, value);
    }

    public double IconTextMaxHeight
    {
        get => _iconTextMaxHeight;
        set => SetProperty(ref _iconTextMaxHeight, value);
    }

    public CornerRadius ItemCornerRadius
    {
        get => _itemCornerRadius;
        set => SetProperty(ref _itemCornerRadius, value);
    }

    public CornerRadius IconCornerRadius
    {
        get => _iconCornerRadius;
        set => SetProperty(ref _iconCornerRadius, value);
    }

    public int Columns
    {
        get => _columns;
        set => SetProperty(ref _columns, value);
    }

    public double FallbackIconFontSize => Math.Max(9, Math.Round(IconSize * 0.32, 1));

    public Thickness ItemMargin => new(ItemSpacing);

    public bool IsCompactPreset => _currentPreset == "6x6";

    // List mode deliberately uses a stronger size step for the smallest preset.
    // Merely scaling the icon left a large shell around 6x6 items, especially on
    // 150% DPI screens. Medium/large presets retain their original proportions.
    public double MappingListWidth => _currentPreset switch
    {
        "3x3" => 364,
        "4x4" => 334,
        "5x5" => 310,
        _ => 220
    };

    public double MappingListRowHeight => _currentPreset switch
    {
        "3x3" => 46.3,
        "4x4" => 38.5,
        "5x5" => 32.3,
        _ => 24
    };

    public double MappingListMinHeight => IsCompactPreset
        ? 58
        : Math.Round(54 + (MappingListRowHeight * 2), 1);

    public double MappingListMaxHeight => IsCompactPreset
        ? 294
        : Math.Round(200 + (MappingListRowHeight * 10), 1);

    public double MappingListIconSize => IsCompactPreset
        ? 14
        : Math.Round(IconSize * 0.75, 1);

    public double MappingListIconFrameSize => MappingListIconSize + 2;

    public double MappingListIconColumnWidth => MappingListIconFrameSize + 4;

    public double MappingListFontSize => _currentPreset switch
    {
        "3x3" => 17.4,
        "4x4" => 16.6,
        "5x5" => 16,
        _ => 12.5
    };

    public double MappingListTitleFontSize => IsCompactPreset ? 13 : 15;

    public double MappingListFallbackFontSize => Math.Max(7, Math.Round(MappingListIconSize * 0.38, 1));

    public Thickness MappingListItemPadding => IsCompactPreset
        ? new Thickness(1, 0.5, 2, 0.5)
        : new Thickness(
            Math.Max(2, Math.Round(ItemSpacing + 1, 1)),
            Math.Max(2, Math.Round(ItemSpacing + 2, 1)),
            Math.Max(2, Math.Round(ItemSpacing + 1, 1)),
            Math.Max(2, Math.Round(ItemSpacing + 2, 1)));

    public Thickness MappingListPadding => IsCompactPreset
        ? new Thickness(4, 2, 4, 2)
        : new Thickness(7, 4, 7, 4);

    public Thickness MappingListMargin => IsCompactPreset
        ? new Thickness(0, 0, 0, 4)
        : new Thickness(0, 2, 0, 8);

    public Thickness MappingListItemMargin => IsCompactPreset
        ? new Thickness(0, 0.5, 0, 0.5)
        : new Thickness(0, 1, 0, 1);

    public Thickness MappingListWindowMargin => IsCompactPreset
        ? new Thickness(4)
        : new Thickness(6);

    public DesktopBoxLayoutSettings()
    {
        UpdateDimensions();
    }

    public void SetPresetChangedCallback(Func<string, Task> callback)
    {
        _presetChangedCallback = callback;
    }

    [CommunityToolkit.Mvvm.Input.RelayCommand]
    private async Task ApplyPresetAsync(string preset)
    {
        _currentPreset = preset;
        UpdateDimensions();

        if (_presetChangedCallback is not null)
        {
            await _presetChangedCallback(preset);
        }
    }

    private void UpdateDimensions()
    {
        switch (_currentPreset)
        {
            case "3x3":
                IconSize = 44;
                IconFrameSize = 60;
                ItemSpacing = 2;
                Columns = 3;
                ItemSlotWidth = 74;
                ItemSlotHeight = 74;
                ItemPadding = new Thickness(4);
                IconFontSize = 11;
                IconTextWrapping = TextWrapping.Wrap;
                IconTextMaxHeight = 32;
                ItemCornerRadius = new CornerRadius(14);
                IconCornerRadius = new CornerRadius(12);
                break;
            case "4x4":
                IconSize = 34;
                IconFrameSize = 46;
                ItemSpacing = 1.5;
                Columns = 4;
                ItemSlotWidth = 55;
                ItemSlotHeight = 55;
                ItemPadding = new Thickness(3);
                IconFontSize = 10;
                IconTextWrapping = TextWrapping.NoWrap;
                IconTextMaxHeight = 16;
                ItemCornerRadius = new CornerRadius(12);
                IconCornerRadius = new CornerRadius(10);
                break;
            case "5x5":
                IconSize = 26;
                IconFrameSize = 36;
                ItemSpacing = 1;
                Columns = 5;
                ItemSlotWidth = 44;
                ItemSlotHeight = 44;
                ItemPadding = new Thickness(2);
                IconFontSize = 9;
                IconTextWrapping = TextWrapping.NoWrap;
                IconTextMaxHeight = 14;
                ItemCornerRadius = new CornerRadius(10);
                IconCornerRadius = new CornerRadius(8);
                break;
            case "6x6":
                IconSize = 20;
                IconFrameSize = 30;
                ItemSpacing = 0.5;
                Columns = 6;
                ItemSlotWidth = 37;
                ItemSlotHeight = 37;
                ItemPadding = new Thickness(1);
                IconFontSize = 8;
                IconTextWrapping = TextWrapping.NoWrap;
                IconTextMaxHeight = 12;
                ItemCornerRadius = new CornerRadius(8);
                IconCornerRadius = new CornerRadius(6);
                break;
        }
        OnPropertyChanged(nameof(FallbackIconFontSize));
        OnPropertyChanged(nameof(IconFrameSize));
        OnPropertyChanged(nameof(ItemMargin));
        OnPropertyChanged(nameof(IsCompactPreset));
        OnPropertyChanged(nameof(MappingListWidth));
        OnPropertyChanged(nameof(MappingListRowHeight));
        OnPropertyChanged(nameof(MappingListMinHeight));
        OnPropertyChanged(nameof(MappingListMaxHeight));
        OnPropertyChanged(nameof(MappingListIconSize));
        OnPropertyChanged(nameof(MappingListIconFrameSize));
        OnPropertyChanged(nameof(MappingListIconColumnWidth));
        OnPropertyChanged(nameof(MappingListFontSize));
        OnPropertyChanged(nameof(MappingListTitleFontSize));
        OnPropertyChanged(nameof(MappingListFallbackFontSize));
        OnPropertyChanged(nameof(MappingListItemPadding));
        OnPropertyChanged(nameof(MappingListPadding));
        OnPropertyChanged(nameof(MappingListMargin));
        OnPropertyChanged(nameof(MappingListItemMargin));
        OnPropertyChanged(nameof(MappingListWindowMargin));
    }
}
