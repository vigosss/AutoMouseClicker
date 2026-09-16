using System.Windows;
using System.Windows.Input;
using Ming_AutoClicker.Models;
using Ming_AutoClicker.Services;
namespace Ming_AutoClicker.Views;
public partial class ItemHotkeyWindow : Window
{
 private HotkeyGesture? _candidate; public HotkeyGesture? Result {get;private set;} public bool ClearRequested{get;private set;}
 public ItemHotkeyWindow(HotkeyGesture? current){InitializeComponent();_candidate=current?.Clone();Input.Text=HotkeyGestureHelper.Format(_candidate);Loaded+=(_,_)=>Input.Focus();}
 private void OnKeyDown(object s,KeyEventArgs e){e.Handled=true;var key=e.Key==Key.System?e.SystemKey:e.Key;if(key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift)return;var m=Keyboard.Modifiers;var mods=HotkeyModifierKeys.None;if(m.HasFlag(ModifierKeys.Control))mods|=HotkeyModifierKeys.Control;if(m.HasFlag(ModifierKeys.Alt))mods|=HotkeyModifierKeys.Alt;if(m.HasFlag(ModifierKeys.Shift))mods|=HotkeyModifierKeys.Shift;_candidate=new HotkeyGesture{VirtualKey=(uint)KeyInterop.VirtualKeyFromKey(key),Modifiers=mods};Input.Text=HotkeyGestureHelper.Format(_candidate);Error.Text=HotkeyGestureHelper.TryValidate(_candidate,out var error)?"":error;}
 private void OnClear(object s,RoutedEventArgs e){ClearRequested=true;Result=null;DialogResult=true;} private void OnCancel(object s,RoutedEventArgs e)=>DialogResult=false;
 private void OnConfirm(object s,RoutedEventArgs e){if(!HotkeyGestureHelper.TryValidate(_candidate,out var error)){Error.Text=error;return;}Result=_candidate;DialogResult=true;}
}
