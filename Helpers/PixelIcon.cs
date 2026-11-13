using System.Windows;
using System.Windows.Media;
using MahApps.Metro.IconPacks;

namespace Ming_AutoClicker.Helpers
{
    /// <summary>
    /// Reusable icon metadata for pixel-styled controls. Kept separate from Tag
    /// so views remain free to use Tag for command parameters and model data.
    /// </summary>
    public static class PixelIcon
    {
        public static readonly DependencyProperty KindProperty = DependencyProperty.RegisterAttached(
            "Kind",
            typeof(PackIconPixelartIconsKind),
            typeof(PixelIcon),
            new FrameworkPropertyMetadata(PackIconPixelartIconsKind.None));

        public static void SetKind(DependencyObject element, PackIconPixelartIconsKind value) =>
            element.SetValue(KindProperty, value);

        public static PackIconPixelartIconsKind GetKind(DependencyObject element) =>
            (PackIconPixelartIconsKind)element.GetValue(KindProperty);

        /// <summary>A custom silhouette takes precedence over the icon pack, when supplied.</summary>
        public static readonly DependencyProperty GeometryProperty = DependencyProperty.RegisterAttached(
            "Geometry", typeof(Geometry), typeof(PixelIcon), new FrameworkPropertyMetadata(null));

        public static void SetGeometry(DependencyObject element, Geometry? value) =>
            element.SetValue(GeometryProperty, value);

        public static Geometry? GetGeometry(DependencyObject element) =>
            (Geometry?)element.GetValue(GeometryProperty);
    }
}
