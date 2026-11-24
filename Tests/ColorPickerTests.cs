using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Ming_AutoClicker.Views.Shared;
using Drawing = System.Drawing;

namespace Ming_AutoClicker.Tests;

internal static partial class Program
{
    private static void AddColorPickerTests(List<(string Name, Func<Task> Run)> tests)
    {
        tests.Add(("Color picker previews and confirms the same frozen pixel", ColorPickerSwatch));
        tests.Add(("Color picker keeps its measured preview on the cursor's monitor", ColorPickerMonitorEdges));
    }

    private static CoordinatePickWindow CreatePicker(bool pickColor, Drawing.Bitmap screenshot)
    {
        var desktop = (-480, -100, screenshot.Width, screenshot.Height);
        Func<Drawing.Bitmap> capture = () => screenshot;
        var picker = (CoordinatePickWindow)Activator.CreateInstance(typeof(CoordinatePickWindow),
            BindingFlags.Instance | BindingFlags.NonPublic, null, [pickColor, desktop, capture], null)!;
        var canvas = (Canvas)picker.FindName("MainCanvas");
        // Two physical pixels per DIP on a desktop extending above and left of the primary.
        var size = new Size(screenshot.Width / 2d, screenshot.Height / 2d);
        canvas.Measure(size);
        canvas.Arrange(new Rect(size));
        picker.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
        return picker;
    }

    private static void InvokePicker(CoordinatePickWindow picker, string method, params object[] arguments) =>
        typeof(CoordinatePickWindow).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(picker, arguments);

    private static Task ColorPickerSwatch()
    {
        using var screenshot = new Drawing.Bitmap(640, 320);
        screenshot.SetPixel(41, 21, Drawing.Color.FromArgb(10, 150, 245));
        screenshot.SetPixel(42, 21, Drawing.Color.FromArgb(245, 6, 10));
        var picker = CreatePicker(true, screenshot);
        try
        {
            var swatch = (Border)picker.FindName("ColorSwatch");
            var hex = (TextBlock)picker.FindName("CoordText");
            var row = (StackPanel)swatch.Parent;
            Check(row.Children.IndexOf(swatch) < row.Children.IndexOf(hex));
            Equal(Visibility.Visible, swatch.Visibility);
            Equal(Visibility.Visible, ((FrameworkElement)picker.FindName("InfoPanel")).Visibility);

            var hover = new Point(20.8, 10.8);
            InvokePicker(picker, "UpdateCrosshair", hover);
            Equal("#0A96F5", hex.Text);
            Equal(Color.FromRgb(10, 150, 245), ((SolidColorBrush)swatch.Background).Color);
            Check(picker.SelectedColorHex == null);
            var selected = new Drawing.Point();
            picker.CoordinatePicked += (x, y) => selected = new Drawing.Point(x, y);
            InvokePicker(picker, "ConfirmSelection", new Point(21.1, 10.8));
            Equal(new Drawing.Point(-438, -79), selected);
            Equal("#F5060A", picker.SelectedColorHex);
            Equal(picker.SelectedColorHex, hex.Text);
            Equal(Color.FromRgb(245, 6, 10), ((SolidColorBrush)swatch.Background).Color);
        }
        finally { picker.Close(); }

        using var coordinateBitmap = new Drawing.Bitmap(8, 8);
        var coordinate = CreatePicker(false, coordinateBitmap);
        try
        {
            Equal(Visibility.Collapsed, ((FrameworkElement)coordinate.FindName("ColorSwatch")).Visibility);
            Equal(Visibility.Collapsed, ((FrameworkElement)coordinate.FindName("InfoPanel")).Visibility);
        }
        finally { coordinate.Close(); }
        return Task.CompletedTask;
    }

    private static Task ColorPickerMonitorEdges()
    {
        using var screenshot = new Drawing.Bitmap(1600, 800);
        var picker = CreatePicker(true, screenshot);
        try
        {
            var panel = (FrameworkElement)picker.FindName("InfoPanel");
            var monitor = new Drawing.Rectangle(-480, 0, 600, 500);
            var monitorOnCanvas = new Rect(0, 50, 300, 250);
            foreach (var x in new[] { 0d, 299.5 })
                foreach (var y in new[] { 50d, 299.5 })
                {
                    var pointer = new Point(x, y);
                    InvokePicker(picker, "PlaceInfoPanel", pointer, monitor);
                    var bounds = new Rect(new Point(Canvas.GetLeft(panel), Canvas.GetTop(panel)), panel.DesiredSize);
                    Check(bounds.Width > 30 && bounds.Height > 10);
                    Check(monitorOnCanvas.Contains(bounds));
                    Check(!bounds.Contains(pointer));
                }
        }
        finally { picker.Close(); }
        return Task.CompletedTask;
    }
}
