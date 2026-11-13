using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using Ming_AutoClicker.Models;

namespace Ming_AutoClicker.Views.ImageMatching;

public sealed class TemplateEditWindow : Window
{
    public ImageTemplateVariant EditedTemplate { get; }
    public TemplatePreviewControl Preview { get; }

    public TemplateEditWindow(ImageTemplateVariant template, string screenshotDirectory, ImageClickPoint? legacyPoint = null)
    {
        EditedTemplate = template.Clone();
        Preview = new TemplatePreviewControl { ClickPoint = EditedTemplate.ClickPoint ?? legacyPoint ?? new(), IgnoredRegions = EditedTemplate.IgnoredRegions };
        SetResourceReference(TitleProperty, "ImageTemplateEditorTitle");
        SetResourceReference(StyleProperty, "PixelWindow");
        Width = 760; Height = 570; MinWidth = 540; MinHeight = 400;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var root = new DockPanel { Margin = new Thickness(16) };
        var hint = Text("ImageTemplateEditorHint");
        hint.Margin = new Thickness(0, 0, 0, 12);
        DockPanel.SetDock(hint, Dock.Top); root.Children.Add(hint);
        var modes = new WrapPanel { Margin = new Thickness(0, 0, 0, 12) };
        var pointMode = new RadioButton { IsChecked = true, Margin = new Thickness(0, 0, 16, 0) };
        pointMode.SetResourceReference(StyleProperty, "ModernRadioButton");
        pointMode.SetResourceReference(ContentControl.ContentProperty, "ImageEditClickPoint");
        var maskMode = new RadioButton();
        maskMode.SetResourceReference(StyleProperty, "ModernRadioButton");
        maskMode.SetResourceReference(ContentControl.ContentProperty, "ImageEditIgnoredRegion");
        modes.Children.Add(pointMode); modes.Children.Add(maskMode);
        DockPanel.SetDock(modes, Dock.Top); root.Children.Add(modes);
        var buttons = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        var center = Button("ImageResetCenter", () => { EditedTemplate.ClickPoint = new(); Preview.ClickPoint = EditedTemplate.ClickPoint; });
        var clear = Button("ImageClearIgnored", () => { EditedTemplate.IgnoredRegions.Clear(); Preview.InvalidateVisual(); });
        buttons.Children.Add(center); buttons.Children.Add(clear);
        var cancel = Button("ImageCancel", () => DialogResult = false); cancel.IsCancel = true;
        var save = Button("EditorSave", () => DialogResult = true); save.IsDefault = true;
        buttons.Children.Add(cancel); buttons.Children.Add(save);
        DockPanel.SetDock(buttons, Dock.Bottom); root.Children.Add(buttons);
        var path = Path.IsPathRooted(EditedTemplate.ImagePath) ? EditedTemplate.ImagePath : Path.Combine(screenshotDirectory, EditedTemplate.ImagePath);
        var bitmap = new BitmapImage();
        bitmap.BeginInit(); bitmap.UriSource = new Uri(Path.GetFullPath(path)); bitmap.CacheOption = BitmapCacheOption.OnLoad; bitmap.EndInit(); bitmap.Freeze();
        Preview.Source = bitmap;
        Preview.PointSelected += (_, e) => { EditedTemplate.ClickPoint = e.Point; Preview.ClickPoint = e.Point; };
        Preview.RegionSelected += (_, e) => { EditedTemplate.IgnoredRegions.Add(e.Region); Preview.InvalidateVisual(); };
        pointMode.Checked += (_, _) => Preview.DrawIgnoredRegion = false;
        maskMode.Checked += (_, _) => Preview.DrawIgnoredRegion = true;
        root.Children.Add(Preview);
        Content = root;
    }

    internal static TextBlock Text(string resource)
    {
        var text = new TextBlock { TextWrapping = TextWrapping.Wrap };
        text.SetResourceReference(TextBlock.TextProperty, resource);
        text.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
        return text;
    }

    internal static Button Button(string resource, Action action)
    {
        var button = new Button { Margin = new Thickness(8, 4, 0, 4), MinHeight = 40, MinWidth = 72 };
        button.SetResourceReference(ContentControl.ContentProperty, resource);
        button.SetResourceReference(StyleProperty, "BaseButton");
        button.Click += (_, _) => action();
        return button;
    }
}
