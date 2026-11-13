using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using Ming_AutoClicker.ViewModels;

namespace Ming_AutoClicker.Views.Recordings;

public partial class RecordingPage : UserControl
{
    public RecordingPage()
    {
        InitializeComponent();
    }

    private void OnItemRequestBringIntoView(object sender, RequestBringIntoViewEventArgs e) => e.Handled = true;

    private void OnMoreClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { ContextMenu: { } menu } button) return;
        if (button.DataContext is RecordingItemViewModel item && item.SelectCommand.CanExecute(null))
            item.SelectCommand.Execute(null);
        menu.PlacementTarget = button;
        menu.Placement = PlacementMode.Bottom;
        menu.IsOpen = true;
    }
}
