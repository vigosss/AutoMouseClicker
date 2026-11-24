using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using Ming_AutoClicker.ViewModels;

namespace Ming_AutoClicker.Views.Recordings;

public partial class RecordingPage : UserControl
{
    private RecordingButtonPress? _buttonPress;

    public RecordingPage()
    {
        InitializeComponent();
        // ButtonBase executes the command on mouse release, Space release or Enter press.
        StartButton.PreviewMouseLeftButtonDown += (_, e) => _buttonPress = new(e.Timestamp);
        StartButton.PreviewMouseLeftButtonUp += (_, _) => SetFinishPress(0);
        StartButton.PreviewKeyDown += (_, e) =>
        {
            if (e.Key is not (Key.Space or Key.Return)) return;
            var key = (uint)KeyInterop.VirtualKeyFromKey(e.Key);
            if (!e.IsRepeat) _buttonPress = new(e.Timestamp, key);
            if (e.Key == Key.Return) SetFinishPress(key);
        };
        StartButton.PreviewKeyUp += (_, e) =>
        {
            if (e.Key == Key.Space) SetFinishPress((uint)KeyInterop.VirtualKeyFromKey(e.Key));
        };
        StartButton.AddHandler(MouseLeftButtonUpEvent, new MouseButtonEventHandler(ClearFinishPress), true);
        StartButton.AddHandler(KeyDownEvent, new KeyEventHandler(ClearFinishPress), true);
        StartButton.AddHandler(KeyUpEvent, new KeyEventHandler(ClearFinishPress), true);
        StartButton.LostKeyboardFocus += (_, _) => _buttonPress = null;
    }

    private void SetFinishPress(uint virtualKey) =>
        StartButton.CommandParameter = DataContext is RecordingPageViewModel { IsRecording: true } &&
            _buttonPress?.VirtualKey == virtualKey ? _buttonPress : null;

    private void ClearFinishPress(object sender, RoutedEventArgs e) => StartButton.CommandParameter = null;

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
