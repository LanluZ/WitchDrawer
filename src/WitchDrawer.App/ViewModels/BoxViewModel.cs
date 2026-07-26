using CommunityToolkit.Mvvm.Messaging;
using WitchDrawer.App.Messages;
using WitchDrawer.Core.Models;
using WitchDrawer.Core.Services;

namespace WitchDrawer.App.ViewModels;

public sealed class BoxViewModel
{
    private readonly DrawerService _drawerService;

    public BoxViewModel(Box model, DrawerService drawerService)
    {
        Model = model;
        _drawerService = drawerService;

        LayoutSettings = new DesktopBoxLayoutSettings();
        LayoutSettings.SetPresetChangedCallback(async (preset) => 
        {
            await _drawerService.SetSettingAsync(GetLayoutPresetSettingKey(Id), preset);
            WeakReferenceMessenger.Default.Send(new BoxLayoutPresetChangedMessage(Id, preset));
        });

        _ = LoadPresetAsync();
    }

    private async Task LoadPresetAsync()
    {
        var preset = await _drawerService.GetSettingAsync(GetLayoutPresetSettingKey(Id));
        LayoutSettings.ApplyPresetWithoutCallback(preset);
    }

    internal static string GetLayoutPresetSettingKey(Guid boxId) => $"BoxPreset_{boxId}";

    public DesktopBoxLayoutSettings LayoutSettings { get; }
    
    public Box Model { get; }

    public Guid Id => Model.Id;

    public string Name => Model.Name;

    public BoxType Type => Model.Type;

    public string TypeLabel => Model.Type switch
    {
        BoxType.Normal => "普通",
        BoxType.Mapping => "映射",
        BoxType.Pixel => "像素",
        BoxType.Todo => "待办",
        _ => "未知"
    };

    public string Description => Model.Type switch
    {
        BoxType.Normal => "拖入后移动到收纳盒",
        BoxType.Mapping => "只保存路径引用",
        BoxType.Pixel => "像素艺术风格收纳",
        BoxType.Todo => "独立桌面待办清单",
        _ => string.Empty
    };

    public string Badge => Model.Type switch
    {
        BoxType.Normal => "N",
        BoxType.Mapping => "M",
        BoxType.Pixel => "P",
        BoxType.Todo => "T",
        _ => "?"
    };

    public string StorageLabel => Model.Type switch
    {
        BoxType.Normal or BoxType.Pixel => Model.StoragePath ?? string.Empty,
        BoxType.Todo => "待办事项保存在本地数据库",
        _ => "源文件保留在原位置"
    };

    public string DeleteWarning => Model.Type switch
    {
        BoxType.Todo => "该待办盒中的所有事项将一并删除，此操作无法撤销。",
        BoxType.Mapping => "只会移除映射引用，源文件不会被移动或删除。",
        _ => "收纳盒内的文件将恢复到原来的位置；如有重名会自动加后缀。"
    };
}

