using System.Windows.Controls;
using System.Windows;
using System.Windows.Input;
using Ming_AutoClicker.ViewModels;
namespace Ming_AutoClicker.Views;
public partial class RecordingPage : UserControl
{
 public RecordingPage(){InitializeComponent();Loaded+=(_,_)=>{if(DataContext is RecordingPageViewModel vm){UpdateMode(vm);vm.PropertyChanged+=(_,e)=>{if(e.PropertyName==nameof(vm.HasSelection))UpdateMode(vm);};}};}
 private void UpdateMode(RecordingPageViewModel vm)=>PlayPanel.Visibility=vm.HasSelection?Visibility.Visible:Visibility.Collapsed;
 private void OnItemRequestBringIntoView(object sender,RequestBringIntoViewEventArgs e)=>e.Handled=true;
}
