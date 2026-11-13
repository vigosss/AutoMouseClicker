using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Ming_AutoClicker.Models;
using Ming_AutoClicker.Services;
using Ming_AutoClicker.ViewModels;
using Ming_AutoClicker.Views.Macros;

namespace Ming_AutoClicker.Tests;

internal static partial class Program
{
    static partial void AddEditorTests(List<(string Name, Func<Task> Run)> tests)
    {
        tests.Add(("Invalid editor drafts survive selection changes and block save", EditorInvalidDrafts));
        tests.Add(("Editor save, reopen and cancel preserve new actions", EditorSaveAndCancel));
        tests.Add(("Editor reorder/delete and keyboard selection stay isolated", EditorSelection));
        tests.Add(("Add-action menu binds all commands and supports repeated additions", EditorAddActionMenu));
        tests.Add(("Both localized panels render at minimum and normal sizes", EditorRendering));
        tests.Add(("Scroll waits for physical modifiers and remains cancellable", ScrollPhysicalModifiers));
    }

    private static Task EditorInvalidDrafts()
    {
        using var fixture = new EditorFixture();
        var vm = fixture.ViewModel;
        var saved = 0;
        vm.SaveCompleted += (_, _) => saved++;
        vm.AddMouseScrollActionCommand.Execute(null);
        var scroll = (MouseScrollAction)vm.SelectedAction!;
        vm.ScrollNotchesText = "abc";
        vm.AddKeyPressActionCommand.Execute(null);
        vm.SaveCommand.Execute(null);
        Equal(0, saved);
        Check(ReferenceEquals(scroll, vm.SelectedAction));
        Equal("abc", vm.ScrollNotchesText);
        foreach (var invalid in new[] { "", "0", "-1", "2.5", "9999999999999999999", "17895698" })
        {
            vm.ScrollNotchesText = invalid;
            vm.SaveCommand.Execute(null);
            Equal(0, saved);
        }
        vm.ScrollNotchesText = "3";
        vm.ScrollPositionModeValue = ScrollPositionMode.Coordinates;
        vm.ScrollXText = "-1920";
        vm.ScrollYText = "100";
        vm.SaveCommand.Execute(null);
        Equal(1, saved);
        Equal(-1920, scroll.X);
        Equal(100, scroll.Y);
        Equal(3, scroll.Notches);
        vm.ScrollXText = "bad";
        vm.SaveCommand.Execute(null);
        Equal(1, saved);
        vm.ScrollPositionModeValue = ScrollPositionMode.CurrentCursor;
        vm.SaveCommand.Execute(null);
        Equal(2, saved);
        vm.ScrollPositionModeValue = ScrollPositionMode.Coordinates;
        vm.SaveCommand.Execute(null);
        Equal(2, saved);
        return Task.CompletedTask;
    }

    private static Task EditorSaveAndCancel()
    {
        using var fixture = new EditorFixture();
        var vm = fixture.ViewModel;
        vm.AddMouseScrollActionCommand.Execute(null);
        vm.ScrollDirectionValue = ScrollDirection.Left;
        vm.ScrollNotchesText = "5";
        vm.ActionNote = "horizontal";
        vm.AddKeyPressActionCommand.Execute(null);
        vm.KeyVirtualKey = 0x43;
        vm.KeyControl = true;
        vm.ActionNote = "copy";
        vm.SaveCompleted += (_, _) => fixture.Save();
        vm.SaveCommand.Execute(null);
        var loaded = fixture.Storage.LoadById(vm.Macro.Id)!;
        Equal(2, loaded.Actions.Count);
        Equal(5, ((MouseScrollAction)loaded.Actions[0]).Notches);
        Equal(KeyModifiers.Control, ((KeyPressAction)loaded.Actions[1]).Modifiers);
        vm.KeyControl = false;
        vm.SelectedAction = vm.Actions[0];
        vm.ScrollNotchesText = "garbage";
        vm.CancelCommand.Execute(null);
        Equal(2, vm.Actions.Count);
        Equal(KeyModifiers.Control, ((KeyPressAction)vm.Actions[1]).Modifiers);
        vm.SelectedAction = vm.Actions[0];
        Equal("5", vm.ScrollNotchesText);
        Equal("horizontal", vm.ActionNote);
        return Task.CompletedTask;
    }

    private static Task EditorSelection()
    {
        using var fixture = new EditorFixture();
        var vm = fixture.ViewModel;
        vm.AddKeyPressActionCommand.Execute(null);
        var first = (KeyPressAction)vm.SelectedAction!;
        vm.KeyVirtualKey = 0x43;
        vm.KeyControl = true;
        vm.AddKeyPressActionCommand.Execute(null);
        var second = (KeyPressAction)vm.SelectedAction!;
        vm.KeyVirtualKey = 0x09;
        vm.KeyAlt = true;
        var view = new MacroEditorView { DataContext = vm };
        Layout(view, 700, 500);
        var keyBox = Descendants<ComboBox>(view).Single(c =>
            BindingOperations.GetBinding(c, ComboBox.SelectedValueProperty)?.Path.Path == nameof(vm.KeyVirtualKey));
        Equal(0x09u, (uint)keyBox.SelectedValue);
        Check(Descendants<TextBlock>(keyBox).Any(text => text.Text == "Tab"), "Selected key should display its name");
        vm.SelectedAction = first;
        Layout(view, 700, 500);
        Equal(0x43u, (uint)keyBox.SelectedValue);
        Check(Descendants<TextBlock>(keyBox).Any(text => text.Text == "C"), "Selected key should update its visible name");
        Check(vm.KeyControl && !vm.KeyAlt);
        keyBox.SelectedValue = 0x56u;
        Equal(0x56u, first.VirtualKey);
        vm.SelectedAction = second;
        Layout(view, 700, 500);
        Equal(0x09u, (uint)keyBox.SelectedValue);
        Equal(KeyModifiers.Alt, second.Modifiers);
        Equal(KeyModifiers.Control, first.Modifiers);
        vm.MoveUpCommand.Execute(null);
        Check(ReferenceEquals(second, vm.Actions[0]));
        vm.RemoveActionCommand.Execute(null);
        Equal(1, vm.Actions.Count);
        Check(ReferenceEquals(first, vm.Actions[0]));
        view.DataContext = null;
        return Task.CompletedTask;
    }

    private static Task EditorRendering()
    {
        using var fixture = new EditorFixture();
        var vm = fixture.ViewModel;
        vm.AddMouseScrollActionCommand.Execute(null);
        var scroll = vm.SelectedAction!;
        vm.ScrollPositionModeValue = ScrollPositionMode.Coordinates;
        vm.ScrollDirectionValue = ScrollDirection.Right;
        vm.ScrollXText = "-1920";
        vm.ScrollYText = "100";
        vm.AddKeyPressActionCommand.Execute(null);
        var key = vm.SelectedAction!;
        vm.KeyVirtualKey = 0x43;
        vm.KeyControl = true;
        var view = new MacroEditorView
        {
            DataContext = vm,
            Background = (Brush)Application.Current.FindResource("BackgroundBrush")
        };
        // A never-shown host gives DynamicResource the same application resource invalidation
        // path as the real editor window, without opening a window or injecting desktop input.
        var host = new Window { Content = view };
        foreach (var language in new[] { AppLanguage.English, AppLanguage.SimplifiedChinese })
        {
            LocalizationService.Current.ApplyLanguage(language);
            view.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
            foreach (var action in new[] { scroll, key })
            {
                vm.SelectedAction = action;
                Layout(view, 700, 500);
                Equal(action.Type.ToString(), vm.SelectedActionType);
                Check(!action.Description.StartsWith("Action"), "Missing action translation");
                var kind = action == scroll ? "scroll" : "keyboard";
                var locale = language == AppLanguage.English ? "en" : "zh";
                Render(view, $"editor-{kind}-{locale}-minimum.png", 700, 500);
                Render(view, $"editor-{kind}-{locale}.png", 800, 600);
            }
            var addButton = (Button)view.FindName("AddActionButton");
            Equal(LocalizationService.Current.GetString("EditorAddAction"), addButton.Content as string);
            // A closed ContextMenu is outside the visual tree; create it in each locale
            // without showing a Popup or sending desktop input.
            var menuView = new MacroEditorView { DataContext = vm };
            var menu = GetAddActionMenu(menuView);
            var keys = new[] { "EditorAddFindImage", "EditorAddWait", "EditorAddClick", "EditorAddScroll", "EditorAddKeyboard" };
            Equal(keys.Length, menu.Items.Count);
            for (var i = 0; i < keys.Length; i++)
                Equal(LocalizationService.Current.GetString(keys[i]), ((MenuItem)menu.Items[i]).Header as string);
            menuView.DataContext = null;
        }
        view.DataContext = null;
        host.Content = null;
        host.Close();
        LocalizationService.Current.ApplyLanguage(AppLanguage.English);
        return Task.CompletedTask;
    }

    private static Task EditorAddActionMenu()
    {
        using var fixture = new EditorFixture();
        var vm = fixture.ViewModel;
        var view = new MacroEditorView { DataContext = vm };
        var menu = GetAddActionMenu(view);
        var commands = new[] { vm.AddFindImageActionCommand, vm.AddWaitActionCommand,
            vm.AddMouseClickActionCommand, vm.AddMouseScrollActionCommand, vm.AddKeyPressActionCommand };
        var types = new[] { ActionType.FindImage, ActionType.Wait, ActionType.MouseClick, ActionType.MouseScroll, ActionType.KeyPress };
        Equal(commands.Length, menu.Items.Count);
        for (var i = 0; i < commands.Length; i++)
        {
            var item = (MenuItem)menu.Items[i];
            Check(ReferenceEquals(commands[i], item.Command), "Menu command should bind to the editor view model");
            Check(item.Command.CanExecute(item.CommandParameter));
            item.Command.Execute(item.CommandParameter);
            Equal(i + 1, vm.Actions.Count);
            Equal(types[i], vm.SelectedAction!.Type);
            Check(ReferenceEquals(vm.Actions.Last(), vm.SelectedAction));
        }
        var keyboardItem = (MenuItem)menu.Items[4];
        var previous = vm.SelectedAction;
        keyboardItem.Command.Execute(keyboardItem.CommandParameter);
        Equal(6, vm.Actions.Count);
        Check(!ReferenceEquals(previous, vm.SelectedAction));
        Equal(ActionType.KeyPress, vm.SelectedAction!.Type);
        Check(ReferenceEquals(vm.Actions.Last(), vm.SelectedAction));
        view.DataContext = null;
        return Task.CompletedTask;
    }

    private static ContextMenu GetAddActionMenu(MacroEditorView view)
    {
        var button = (Button)view.FindName("AddActionButton");
        Check(button != null, "The editor should expose one add-action button");
        var menu = button!.ContextMenu;
        Check(menu != null, "The add-action button should expose its action menu");
        menu!.PlacementTarget = button;
        view.Dispatcher.Invoke(() => { }, DispatcherPriority.DataBind);
        return menu;
    }

    private static async Task ScrollPhysicalModifiers()
    {
        var held = 1;
        var sender = new FakeInputSender { KeyState = key => key == 0xA2 && Volatile.Read(ref held) != 0 };
        var scroll = new MacroInputService(sender).ScrollAsync(new());
        await Task.Delay(30);
        Equal(0, sender.Batches.Count);
        Volatile.Write(ref held, 0);
        await scroll;
        Equal(1, sender.Batches.Count);

        using var cts = new CancellationTokenSource();
        sender = new FakeInputSender { KeyState = key => key == 0xA2 };
        scroll = new MacroInputService(sender).ScrollAsync(new(), cts.Token);
        await Task.Delay(30);
        cts.Cancel();
        await Throws<OperationCanceledException>(() => scroll);
        Equal(0, sender.Batches.Count);
    }

    private static void Layout(FrameworkElement view, int width, int height)
    {
        view.Width = width;
        view.Height = height;
        view.Measure(new Size(width, height));
        view.Arrange(new Rect(0, 0, width, height));
        view.UpdateLayout();
    }

    private static void Render(FrameworkElement view, string filename, int width, int height)
    {
        Layout(view, width, height);
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(view);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        var directory = Path.Combine(AppContext.BaseDirectory, "artifacts");
        Directory.CreateDirectory(directory);
        using var output = File.Create(Path.Combine(directory, filename));
        encoder.Save(output);
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match) yield return match;
            foreach (var descendant in Descendants<T>(child)) yield return descendant;
        }
    }

    private sealed class EditorFixture : IDisposable
    {
        private readonly ScreenCaptureService _capture = new();
        private readonly ImageMatchService _match;
        private string? _path;
        public MacroStorageService Storage { get; } = new();
        public MacroEditorViewModel ViewModel { get; }
        public EditorFixture()
        {
            _match = new ImageMatchService(_capture);
            ViewModel = new( Storage, _capture, _match, new MacroProfile { Name = "Regression macro" });
        }
        public void Save() => _path = Storage.Save(ViewModel.Macro);
        public void Dispose()
        {
            ViewModel.Dispose();
            _match.Dispose();
            _capture.Dispose();
            if (_path != null) File.Delete(_path);
        }
    }
}
