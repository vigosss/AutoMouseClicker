using System.Windows;
using System.Windows.Controls;
using Ming_AutoClicker.Services;

namespace Ming_AutoClicker.Views.ImageMatching;

public sealed class ImageWindowSelectWindow : Window
{
    private readonly ListBox _windows = new();
    public ImageWindowInfo? SelectedWindow => _windows.SelectedItem as ImageWindowInfo;

    public ImageWindowSelectWindow()
    {
        SetResourceReference(TitleProperty, "ImageChooseWindow");
        SetResourceReference(StyleProperty, "PixelWindow");
        Width = 620; Height = 420; MinWidth = 420; MinHeight = 300;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var root = new DockPanel { Margin = new Thickness(16) };
        var hint = TemplateEditWindow.Text("ImageChooseWindowHint");
        hint.Margin = new Thickness(0, 0, 0, 12);
        DockPanel.SetDock(hint, Dock.Top); root.Children.Add(hint);
        var buttons = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        var refresh = TemplateEditWindow.Button("ImageRefreshWindows", Refresh);
        var cancel = TemplateEditWindow.Button("ImageCancel", () => DialogResult = false); cancel.IsCancel = true;
        var select = TemplateEditWindow.Button("ImageUseWindow", () => { if (SelectedWindow != null) DialogResult = true; });
        select.IsEnabled = false;
        _windows.SelectionChanged += (_, _) => select.IsEnabled = SelectedWindow != null;
        buttons.Children.Add(refresh); buttons.Children.Add(cancel); buttons.Children.Add(select);
        DockPanel.SetDock(buttons, Dock.Bottom); root.Children.Add(buttons);
        _windows.SetResourceReference(StyleProperty, "ModernListBox");
        _windows.SetResourceReference(ItemsControl.ItemContainerStyleProperty, "CompactListBoxItem");
        root.Children.Add(_windows); Content = root;
        Loaded += (_, _) => Refresh();
    }

    private void Refresh()
    {
        _windows.ItemsSource = ImageSearchScopeService.GetWindows();
        _windows.SelectedItem = null;
    }
}
