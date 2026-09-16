using System.Windows.Controls;
using System.Windows.Input;
using System.Windows;
using Ming_AutoClicker.ViewModels;
namespace Ming_AutoClicker.Views;
public partial class RecordingPage : UserControl
{
 public RecordingPage(){InitializeComponent();Loaded+=(_,_)=>{if(DataContext is RecordingPageViewModel vm){UpdateMode(vm);vm.PropertyChanged+=(_,e)=>{if(e.PropertyName==nameof(vm.HasSelection))UpdateMode(vm);};}};}
 private void UpdateMode(RecordingPageViewModel vm)=>PlayPanel.Visibility=vm.HasSelection?Visibility.Visible:Visibility.Collapsed;
 private void OnRenameKeyDown(object sender,KeyEventArgs e){if(((FrameworkElement)sender).DataContext is not RecordingItemViewModel vm)return;if(e.Key==Key.Enter){vm.ConfirmRenameCommand.Execute(null);e.Handled=true;}else if(e.Key==Key.Escape){vm.CancelRenameCommand.Execute(null);e.Handled=true;}}
 private void OnRenameLostFocus(object sender,KeyboardFocusChangedEventArgs e){if(((FrameworkElement)sender).DataContext is RecordingItemViewModel vm&&vm.IsRenaming)vm.ConfirmRenameCommand.Execute(null);}
}
