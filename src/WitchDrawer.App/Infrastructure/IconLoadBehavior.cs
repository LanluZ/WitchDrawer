using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using WitchDrawer.App.ViewModels;

namespace WitchDrawer.App.Infrastructure;

public static class IconLoadBehavior
{
    public static readonly DependencyProperty LoadWhenVisibleProperty = DependencyProperty.RegisterAttached(
        "LoadWhenVisible",
        typeof(bool),
        typeof(IconLoadBehavior),
        new PropertyMetadata(false, OnLoadWhenVisibleChanged));

    private static readonly DependencyProperty CurrentItemProperty = DependencyProperty.RegisterAttached(
        "CurrentItem",
        typeof(DrawerItemViewModel),
        typeof(IconLoadBehavior));

    public static void SetLoadWhenVisible(DependencyObject element, bool value)
    {
        element.SetValue(LoadWhenVisibleProperty, value);
    }

    public static bool GetLoadWhenVisible(DependencyObject element)
    {
        return (bool)element.GetValue(LoadWhenVisibleProperty);
    }

    public static void RequestIconsForRealizedItems(ItemsControl itemsControl)
    {
        VisitRealizedItems(itemsControl, static item => item.RequestIcon());
    }

    public static void ReleaseIconsForRealizedItems(ItemsControl itemsControl)
    {
        VisitRealizedItems(itemsControl, static item => item.ReleaseIcon());
    }

    private static void VisitRealizedItems(ItemsControl itemsControl, Action<DrawerItemViewModel> action)
    {
        VisitVisualChildren(itemsControl, itemsControl, action);
    }

    private static void VisitVisualChildren(
        DependencyObject current,
        ItemsControl owner,
        Action<DrawerItemViewModel> action)
    {
        var childCount = VisualTreeHelper.GetChildrenCount(current);
        for (var index = 0; index < childCount; index++)
        {
            var child = VisualTreeHelper.GetChild(current, index);
            if (child is ListBoxItem container
                && ReferenceEquals(ItemsControl.ItemsControlFromItemContainer(container), owner))
            {
                if (container.DataContext is DrawerItemViewModel item)
                {
                    action(item);
                }
                continue;
            }

            VisitVisualChildren(child, owner, action);
        }
    }

    private static void OnLoadWhenVisibleChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs e)
    {
        if (dependencyObject is not FrameworkElement element)
        {
            return;
        }

        if (e.NewValue is true)
        {
            element.Loaded += OnLoaded;
            element.Unloaded += OnUnloaded;
            element.DataContextChanged += OnDataContextChanged;
            if (element.IsLoaded)
            {
                UpdateCurrentItem(element);
            }
        }
        else
        {
            element.Loaded -= OnLoaded;
            element.Unloaded -= OnUnloaded;
            element.DataContextChanged -= OnDataContextChanged;
            ReleaseCurrentItem(element);
        }
    }

    private static void OnLoaded(object sender, RoutedEventArgs e)
    {
        UpdateCurrentItem((FrameworkElement)sender);
    }

    private static void OnUnloaded(object sender, RoutedEventArgs e)
    {
        ReleaseCurrentItem((FrameworkElement)sender);
    }

    private static void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        var element = (FrameworkElement)sender;
        if (element.IsLoaded)
        {
            UpdateCurrentItem(element);
        }
    }

    private static void UpdateCurrentItem(FrameworkElement element)
    {
        var current = (DrawerItemViewModel?)element.GetValue(CurrentItemProperty);
        var next = element.DataContext as DrawerItemViewModel;
        if (ReferenceEquals(current, next))
        {
            return;
        }

        current?.ReleaseIcon();
        element.SetValue(CurrentItemProperty, next);
        next?.RequestIcon();
    }

    private static void ReleaseCurrentItem(FrameworkElement element)
    {
        if (element.GetValue(CurrentItemProperty) is DrawerItemViewModel item)
        {
            item.ReleaseIcon();
            element.ClearValue(CurrentItemProperty);
        }
    }
}
