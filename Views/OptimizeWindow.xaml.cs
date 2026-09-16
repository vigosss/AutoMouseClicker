using System.Collections.ObjectModel;
using System.Windows;
using Ming_AutoClicker.Models;
using Ming_AutoClicker.Services;
namespace Ming_AutoClicker.Views;
public partial class OptimizeWindow : Window
{
 private readonly Recording _copy; public Recording? Result{get;private set;} public ObservableCollection<RecordedAction> Actions{get;}
 public OptimizeWindow(Recording source){InitializeComponent();_copy=source.DeepClone();Actions=new(_copy.Actions);Grid.ItemsSource=Actions;NameBox.Text=_copy.Name;UpdateSummary();}
 private IEnumerable<RecordedAction> Selected()=>Grid.SelectedItems.Cast<RecordedAction>();
 private void RemoveWait(object s,RoutedEventArgs e){foreach(var a in Selected())a.DelayBeforeMs=0;Grid.Items.Refresh();UpdateSummary();}
 private void RemoveMoves(object s,RoutedEventArgs e){foreach(var a in Selected().Where(x=>x.Type==RecordedActionType.MouseMove))a.IsEnabled=false;Grid.Items.Refresh();UpdateSummary();}
 private void MergeMoves(object s,RoutedEventArgs e){RecordedAction? previous=null;foreach(var a in Actions.Where(x=>x.Type==RecordedActionType.MouseMove&&x.IsEnabled)){if(previous!=null&&previous.X==a.X&&previous.Y==a.Y)previous.IsEnabled=false;previous=a;}Grid.Items.Refresh();UpdateSummary();}
 private void UpdateSummary(){Summary.Text=LocalizationService.Current.Format("OptimizeSummary",Actions.Count(x=>x.IsEnabled),TimeSpan.FromMilliseconds(Actions.Sum(x=>x.DelayBeforeMs)).ToString(@"mm\:ss"));}
 private void Save(object s,RoutedEventArgs e){if(string.IsNullOrWhiteSpace(NameBox.Text))return;_copy.Name=NameBox.Text.Trim();_copy.Actions=Actions.Select(x=>x.Clone()).ToList();Result=_copy;DialogResult=true;}
 private void Cancel(object s,RoutedEventArgs e)=>DialogResult=false;
}
