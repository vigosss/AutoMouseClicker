using System.Windows;
using System.Windows.Input;
using Ming_AutoClicker.Services;

namespace Ming_AutoClicker.Views;

public partial class RenameRecordingWindow : Window
{
    public string ResultName { get; private set; } = string.Empty;

    public RenameRecordingWindow(string currentName)
    {
        InitializeComponent();
        NameInput.Text = currentName;
        Loaded += (_, _) => { NameInput.Focus(); NameInput.SelectAll(); };
    }

    private void OnConfirm(object sender, RoutedEventArgs e)
    {
        var name = NameInput.Text.Trim();
        if (name.Length == 0)
        {
            ValidationText.Text = LocalizationService.Current.GetString("RecordingRenameEmpty");
            NameInput.Focus();
            NameInput.SelectAll();
            return;
        }
        ResultName = name;
        DialogResult = true;
    }

    private void OnCancel(object sender, RoutedEventArgs e) => DialogResult = false;

    private void OnWindowPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) { e.Handled = true; OnConfirm(sender, e); }
        else if (e.Key == Key.Escape) { e.Handled = true; DialogResult = false; }
    }
}
