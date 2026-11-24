using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using MahApps.Metro.IconPacks;
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
        tests.Add(("Color drafts, scope and pointer operation survive editor selection and save", EditorColorSettings));
        tests.Add(("Color controls expose the three pointer operations and two scopes in both languages", EditorColorControls));
        tests.Add(("Legacy window color scopes survive editing until the user chooses another scope", EditorLegacyColorScope));
        tests.Add(("A saved color position keeps its name until a pointer operation is chosen", EditorLegacyColorOperation));
        tests.Add(("Macro target repair, transfer mode and image miss policy survive editing", EditorTransferActions));
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
            var keys = new[] { "EditorAddFindImage", "EditorAddFindColor", "EditorAddWait", "EditorAddClick", "EditorAddScroll", "EditorAddKeyboard", "EditorAddExecuteMacro" };
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
        using var fixture = new EditorFixture(macroLibrary: new[] { new MacroProfile { Name = "Target" } });
        var vm = fixture.ViewModel;
        var view = new MacroEditorView { DataContext = vm };
        var menu = GetAddActionMenu(view);
        var commands = new[] { vm.AddFindImageActionCommand, vm.AddFindColorActionCommand, vm.AddWaitActionCommand,
            vm.AddMouseClickActionCommand, vm.AddMouseScrollActionCommand, vm.AddKeyPressActionCommand, vm.AddExecuteMacroActionCommand };
        var types = new[] { ActionType.FindImage, ActionType.FindColor, ActionType.Wait, ActionType.MouseClick, ActionType.MouseScroll, ActionType.KeyPress, ActionType.ExecuteMacro };
        Equal(commands.Length, menu.Items.Count);
        var colorItem = menu.Items.Cast<MenuItem>().Single(item => item.Command == vm.AddFindColorActionCommand);
        var clickItem = menu.Items.Cast<MenuItem>().Single(item => item.Command == vm.AddMouseClickActionCommand);
        Equal(PackIconPixelartIconsKind.ColorsSwatch, Descendants<PackIconPixelartIcons>(colorItem).Single().Kind);
        Equal(PackIconPixelartIconsKind.Bullseye, Descendants<PackIconPixelartIcons>(clickItem).Single().Kind);
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
        var keyboardItem = (MenuItem)menu.Items[Array.IndexOf(types, ActionType.KeyPress)];
        var previous = vm.SelectedAction;
        keyboardItem.Command.Execute(keyboardItem.CommandParameter);
        Equal(types.Length + 1, vm.Actions.Count);
        Check(!ReferenceEquals(previous, vm.SelectedAction));
        Equal(ActionType.KeyPress, vm.SelectedAction!.Type);
        Check(ReferenceEquals(vm.Actions.Last(), vm.SelectedAction));
        vm.SelectedAction = vm.Actions.Single(a => a is ExecuteMacroAction);
        Equal(MacroTransferMode.Call, vm.TransferMode);
        Equal(string.Empty, vm.SelectedTargetMacroId);
        Equal(LocalizationService.Current.GetString("MacroSelectTarget"), vm.MacroTargetWarning);
        view.DataContext = null;
        return Task.CompletedTask;
    }

    private static Task EditorColorSettings()
    {
        using var fixture = new EditorFixture();
        var vm = fixture.ViewModel;
        vm.AddFindColorActionCommand.Execute(null);
        var find = (FindColorAction)vm.SelectedAction!;
        Equal("5", vm.ColorToleranceText);
        Equal("30", vm.ColorTimeoutSecondsText);
        vm.ColorTargetText = "#17386B";
        vm.ColorOperationValue = ColorOperation.MoveTo;
        vm.ColorScopeKind = ImageSearchScopeKind.Region;
        var corners = vm.ColorRegionInputs!;
        corners.X1 = "-31"; corners.X2 = "-30";
        corners.Y1 = "16"; corners.Y2 = "16";
        vm.ColorToleranceText = "256";
        vm.AddMouseClickActionCommand.Execute(null);
        var click = (MouseClickAction)vm.SelectedAction!;
        Equal(ClickPositionSource.Fixed, click.PositionSource);
        var saves = 0;
        vm.SaveCompleted += (_, _) => { fixture.Save(); saves++; };
        vm.SaveCommand.Execute(null);
        Equal(0, saves);
        Check(ReferenceEquals(find, vm.SelectedAction));
        Equal("256", vm.ColorToleranceText);
        vm.ColorToleranceText = "0";
        vm.ColorTimeoutSecondsText = "0";
        vm.SaveCommand.Execute(null);
        Equal(1, saves);
        var loaded = fixture.Storage.LoadById(vm.Macro.Id)!;
        var savedFind = (FindColorAction)loaded.Actions[0];
        Equal("#17386B", savedFind.TargetColor);
        Equal(0, savedFind.Tolerance);
        Equal(new System.Drawing.Rectangle(-31, 16, 2, 1), savedFind.SearchScope.GetRectangle());
        Equal(ColorOperation.MoveTo, savedFind.Operation);
        Equal(ClickPositionSource.Fixed, ((MouseClickAction)loaded.Actions[1]).PositionSource);
        return Task.CompletedTask;
    }

    private static Task EditorColorControls()
    {
        var strings = LocalizationService.Current;
        var originalLanguage = strings.SelectedLanguage;
        try
        {
            foreach (var language in new[] { AppLanguage.SimplifiedChinese, AppLanguage.English })
            {
                strings.ApplyLanguage(language);
                using var fixture = new EditorFixture();
                var vm = fixture.ViewModel;
                var view = new MacroEditorView { DataContext = vm };
                vm.AddFindImageActionCommand.Execute(null);
                vm.ImageAdvancedExpanded = true;
                Layout(view, 700, 500);
                var image = (FindImageAction)vm.SelectedAction!;
                var imageCombo = (ComboBox)view.FindName("ImageScopeCombo");
                var imageItems = imageCombo.Items.Cast<ComboBoxItem>().ToArray();
                Sequence(new[] { ImageSearchScopeKind.Desktop, ImageSearchScopeKind.Region, ImageSearchScopeKind.Window },
                    imageItems.Select(item => (ImageSearchScopeKind)item.Tag));
                Sequence(new[] { "ImageScopeDesktop", "ImageScopeRegion", "ImageScopeWindow" }.Select(strings.GetString),
                    imageItems.Select(item => (string)item.Content));
                imageCombo.SetCurrentValue(ComboBox.SelectedItemProperty, imageItems[2]);
                Equal(ImageSearchScopeKind.Window, image.SearchScope.Kind);
                vm.AddFindColorActionCommand.Execute(null);
                Layout(view, 700, 500);
                Equal(Visibility.Visible, ((StackPanel)view.FindName("FindColorPanel")).Visibility);
                var color = (FindColorAction)vm.SelectedAction!;
                var colorCombo = (ComboBox)view.FindName("ColorScopeCombo");
                var colorItems = colorCombo.Items.Cast<ComboBoxItem>().ToArray();
                Sequence(new[] { ImageSearchScopeKind.Desktop, ImageSearchScopeKind.Region },
                    colorItems.Select(item => (ImageSearchScopeKind)item.Tag));
                Sequence(language == AppLanguage.SimplifiedChinese ? new[] { "全图", "选择区域" } : new[] { "Entire desktop", "Select region" },
                    colorItems.Select(item => (string)item.Content));
                Equal(ImageSearchScopeKind.Desktop, (ImageSearchScopeKind)colorCombo.SelectedValue);
                foreach (var item in colorItems.Reverse())
                {
                    colorCombo.SetCurrentValue(ComboBox.SelectedItemProperty, item);
                    Equal((ImageSearchScopeKind)item.Tag, color.SearchScope.Kind);
                }
                Equal(ImageSearchScopeKind.Window, image.SearchScope.Kind);
                var hexBox = (TextBox)view.FindName("ColorTargetBox");
                hexBox.SetCurrentValue(TextBox.TextProperty, "#103FE2");
                var swatch = Descendants<Border>((StackPanel)view.FindName("FindColorPanel")).Single(border =>
                    BindingOperations.GetBinding(border, Border.BackgroundProperty)?.Path.Path == nameof(vm.ColorSwatch));
                Equal(Color.FromRgb(16, 63, 226), ((SolidColorBrush)swatch.Background).Color);
                Equal("#103FE2", color.TargetColor);
                var operation = (ComboBox)view.FindName("ColorOperationCombo");
                var operations = new[] { ColorOperation.Click, ColorOperation.RightClick, ColorOperation.MoveTo };
                var labels = language == AppLanguage.SimplifiedChinese
                    ? new[] { "左键点击", "右键点击", "鼠标移动到" }
                    : new[] { "Left click", "Right click", "Move mouse to" };
                Equal(operations.Length, operation.Items.Count);
                Equal(ColorOperation.Click, (ColorOperation)operation.SelectedValue);
                Equal(ColorOperation.Click, color.Operation);
                for (int i = operations.Length - 1; i >= 0; i--)
                {
                    operation.SetCurrentValue(ComboBox.SelectedIndexProperty, i);
                    Layout(view, 700, 500);
                    Equal(operations[i], (ColorOperation)operation.SelectedValue);
                    Equal(operations[i], color.Operation);
                    Check(Descendants<TextBlock>(operation).Any(text => text.Text == labels[i]),
                        $"Closed color operation selector did not display '{labels[i]}'");
                }
                vm.ColorOperationValue = ColorOperation.SavePosition;
                Equal(ColorOperation.Click, color.Operation);
                view.DataContext = null;
            }
        }
        finally { strings.ApplyLanguage(originalLanguage); }
        return Task.CompletedTask;
    }

    private static Task EditorLegacyColorScope()
    {
        var legacy = new FindColorAction
        {
            SearchScope = new()
            {
                Kind = ImageSearchScopeKind.Window, WindowProcessName = "legacy-app",
                WindowClassName = "LegacyClass", WindowTitle = "Legacy window",
                WindowRegion = new() { X = .25, Y = .125, Width = .5, Height = .75 }
            }
        };
        using var old = new EditorFixture(new MacroProfile { Name = "Legacy scope", Actions = [legacy, new WaitAction()] });
        old.Save();
        var original = old.Storage.LoadById(old.ViewModel.Macro.Id)!;
        var serializedScope = JsonSerializer.Serialize(legacy.SearchScope);
        foreach (var choice in new[] { ImageSearchScopeKind.Desktop, ImageSearchScopeKind.Region })
        {
            using var fixture = new EditorFixture(original.DeepClone());
            var vm = fixture.ViewModel;
            var view = new MacroEditorView { DataContext = vm };
            var color = (FindColorAction)vm.Actions[0];
            vm.SelectedAction = color;
            Layout(view, 700, 500);
            var chooser = (ComboBox)view.FindName("ColorScopeCombo");
            var hint = (TextBlock)view.FindName("LegacyColorScopeHint");
            Check(chooser.SelectedIndex == -1, "A saved window scope must not appear as desktop or region");
            Equal(Visibility.Visible, hint.Visibility);
            Check(hint.Text.Contains("Legacy window") && hint.Text.Contains("legacy-app"));
            vm.SelectedAction = vm.Actions[1];
            vm.SelectedAction = color;
            Layout(view, 700, 500);
            var saves = 0;
            vm.SaveCompleted += (_, _) => { fixture.Save(); saves++; };
            vm.SaveCommand.Execute(null);
            Equal(1, saves);
            var unchanged = (FindColorAction)fixture.Storage.LoadById(original.Id)!.Actions[0];
            Equal(serializedScope, JsonSerializer.Serialize(unchanged.SearchScope));

            var item = chooser.Items.Cast<ComboBoxItem>().Single(row => Equals(row.Tag, choice));
            chooser.SetCurrentValue(ComboBox.SelectedItemProperty, item);
            Layout(view, 700, 500);
            Equal(choice, color.SearchScope.Kind);
            Equal(Visibility.Collapsed, hint.Visibility);
            if (choice == ImageSearchScopeKind.Region)
            {
                var bounds = vm.ColorRegionInputs!;
                bounds.X1 = "-16"; bounds.Y1 = "2"; bounds.X2 = "-9"; bounds.Y2 = "5";
            }
            vm.SaveCommand.Execute(null);
            Equal(2, saves);
            var changed = ((FindColorAction)fixture.Storage.LoadById(original.Id)!.Actions[0]).SearchScope;
            Equal(choice, changed.Kind);
            Check(changed.WindowRegion == null && string.IsNullOrEmpty(changed.WindowTitle));
            if (choice == ImageSearchScopeKind.Region)
                Equal(new System.Drawing.Rectangle(-16, 2, 8, 4), changed.GetRectangle());
            view.DataContext = null;
        }
        return Task.CompletedTask;
    }

    private static Task EditorLegacyColorOperation()
    {
        const string name = "确认按钮";
        using var saved = new EditorFixture(new MacroProfile
        {
            Name = "Legacy operation",
            Actions = [
                new FindColorAction { Operation = ColorOperation.SavePosition, VariableName = name },
                new MouseClickAction { PositionSource = ClickPositionSource.Variable, VariableName = name }
            ]
        });
        saved.Save();
        var original = saved.Storage.LoadById(saved.ViewModel.Macro.Id)!;
        foreach (var choice in new[] { ColorOperation.Click, ColorOperation.RightClick, ColorOperation.MoveTo })
        {
            using var fixture = new EditorFixture(original.DeepClone());
            var vm = fixture.ViewModel;
            var view = new MacroEditorView { DataContext = vm };
            var host = new Window { Content = view };
            var color = (FindColorAction)vm.Actions[0];
            vm.SelectedAction = color;
            Layout(view, 700, 500);
            var chooser = (ComboBox)view.FindName("ColorOperationCombo");
            var nameBox = (TextBox)view.FindName("ColorVariableBox");
            var nameEditor = (FrameworkElement)nameBox.Parent;
            Equal(4, chooser.Items.Count);
            Equal(3, chooser.SelectedIndex);
            Equal(ColorOperation.SavePosition, (ColorOperation)chooser.SelectedValue);
            Equal(Visibility.Visible, nameEditor.Visibility);
            Equal(name, nameBox.Text);
            Equal(ColorOperation.SavePosition, color.Operation);
            Equal(name, color.VariableName);
            if (choice == ColorOperation.Click)
            {
                var strings = LocalizationService.Current;
                var previousLanguage = strings.SelectedLanguage;
                try
                {
                    strings.ApplyLanguage(strings.EffectiveLanguage == AppLanguage.English
                        ? AppLanguage.SimplifiedChinese : AppLanguage.English);
                    view.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                    Layout(view, 700, 500);
                    Equal(4, chooser.Items.Count);
                    Equal(ColorOperation.SavePosition, (ColorOperation)chooser.SelectedValue);
                    Equal(ColorOperation.SavePosition, color.Operation);
                    Check(Descendants<TextBlock>(chooser).Any(text => text.Text == strings.GetString("EditorSavePosition")),
                        "Legacy operation should retain its translated label inside the closed combo after changing language");
                }
                finally
                {
                    strings.ApplyLanguage(previousLanguage);
                    view.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                }
            }

            vm.SelectedAction = vm.Actions[1];
            Layout(view, 700, 500);
            var source = (ComboBox)view.FindName("ClickPositionSourceCombo");
            Equal(ClickPositionSource.Variable, (ClickPositionSource)source.SelectedValue);
            Equal(name, ((ComboBox)view.FindName("ClickVariableCombo")).SelectedItem);
            vm.SelectedAction = color;
            Layout(view, 700, 500);
            var saves = 0;
            vm.SaveCompleted += (_, _) => { fixture.Save(); saves++; };
            vm.SaveCommand.Execute(null);
            Equal(1, saves);
            var unchanged = fixture.Storage.LoadById(original.Id)!;
            Equal(ColorOperation.SavePosition, ((FindColorAction)unchanged.Actions[0]).Operation);
            Equal(name, ((FindColorAction)unchanged.Actions[0]).VariableName);
            Equal(name, ((MouseClickAction)unchanged.Actions[1]).VariableName);
            nameBox.SetCurrentValue(TextBox.TextProperty, "second name");
            Equal("second name", color.VariableName);
            vm.SaveCommand.Execute(null);
            Equal(2, saves);
            Equal("second name", ((FindColorAction)fixture.Storage.LoadById(original.Id)!.Actions[0]).VariableName);
            chooser.SetCurrentValue(ComboBox.SelectedValueProperty, choice);
            Layout(view, 700, 500);
            Equal(choice, color.Operation);
            Equal(choice, (ColorOperation)chooser.SelectedValue);
            Equal(3, chooser.Items.Count);
            Equal(Visibility.Collapsed, nameEditor.Visibility);
            vm.ColorOperationValue = ColorOperation.SavePosition;
            Equal(choice, color.Operation);
            vm.SaveCommand.Execute(null);
            Equal(3, saves);
            var converted = (FindColorAction)fixture.Storage.LoadById(original.Id)!.Actions[0];
            Equal(choice, converted.Operation);
            view.DataContext = null;
            host.Content = null;
            host.Close();
        }
        return Task.CompletedTask;
    }

    private static Task EditorTransferActions()
    {
        var profile = new MacroProfile { Name = "Parent" };
        using (var onlySelf = new EditorFixture(profile, new[] { profile }))
        {
            var emptyView = new MacroEditorView { DataContext = onlySelf.ViewModel };
            Layout(emptyView, 700, 500);
            var menu = GetAddActionMenu(emptyView);
            var addTransfer = (MenuItem)menu.Items[menu.Items.Count - 1];
            Check(!onlySelf.ViewModel.HasMacroTargets, "The current macro must be excluded from the target library");
            Check(!onlySelf.ViewModel.AddExecuteMacroActionCommand.CanExecute(null), "A transfer requires another macro");
            Check(ReferenceEquals(menu.DataContext, onlySelf.ViewModel), "The menu must inherit the current editor");
            Check(ReferenceEquals(addTransfer.Command, onlySelf.ViewModel.AddExecuteMacroActionCommand),
                $"Unexpected menu command for '{addTransfer.Header}', items: {menu.Items.Count}");
            Check(!addTransfer.IsEnabled, "The add-transfer menu item should be disabled when the library contains only the current macro");
            emptyView.DataContext = null;
        }

        var missingId = Guid.NewGuid().ToString();
        var missing = new ExecuteMacroAction { TargetMacroId = missingId };
        var target = new MacroProfile { Name = "Collect rewards" };
        var image = new FindImageAction { ImagePath = "optional.png" };
        profile.Actions.Add(missing);
        profile.Actions.Add(image);
        using var fixture = new EditorFixture(profile, new[] { profile, target });
        var vm = fixture.ViewModel;
        var saved = 0;
        vm.SaveCompleted += (_, _) => { saved++; fixture.Save(); };
        vm.SelectedAction = missing;
        var view = new MacroEditorView { DataContext = vm };
        Layout(view, 700, 500);
        var targets = (ComboBox)view.FindName("MacroTargetCombo");
        var call = (RadioButton)view.FindName("MacroCallOption");
        var jump = (RadioButton)view.FindName("MacroJumpOption");
        Equal(1, targets.Items.Count);
        Equal(missingId, missing.TargetMacroId);
        Check(targets.SelectedItem == null && vm.MacroTargetWarning.Contains(missingId), "A missing target must be unselected and identified by its original ID");
        vm.SaveCommand.Execute(null);
        Equal(0, saved);
        Check(vm.StatusMessage.Contains(missingId), "Saving a broken target must identify the original ID");
        targets.SetCurrentValue(ComboBox.SelectedValueProperty, target.Id);
        Check(call.IsChecked == true && jump.IsChecked == false, "Call and return should be selected by default");
        jump.SetCurrentValue(RadioButton.IsCheckedProperty, true);
        Layout(view, 700, 500);
        Check(jump.IsChecked == true && call.IsChecked == false, "Selecting jump must clear call and return");
        Equal(MacroTransferMode.Jump, missing.Mode);
        Equal(target.Id, missing.TargetMacroId);
        Equal(string.Empty, vm.MacroTargetWarning);
        Check(missing.Description.Contains(target.Name), "The step description should use the repaired target name");
        Check(Descendants<TextBlock>(targets).Any(t => t.Text == target.Name), "The closed target dropdown should display the repaired macro's name");
        Equal(LocalizationService.Current.GetString("EditorMacroJumpHint"), ((TextBlock)view.FindName("MacroTransferHint")).Text);

        vm.SelectedAction = image;
        Layout(view, 700, 500);
        var policies = (ComboBox)view.FindName("ImageNotFoundCombo");
        Equal(ImageNotFoundBehavior.Stop, (ImageNotFoundBehavior)policies.SelectedValue);
        foreach (var policy in new[] { ImageNotFoundBehavior.SkipStep, ImageNotFoundBehavior.EndRound })
        {
            policies.SetCurrentValue(ComboBox.SelectedValueProperty, policy);
            Equal(policy, image.NotFoundBehavior);
        }
        vm.SaveCommand.Execute(null);
        Equal(1, saved);
        vm.SelectedAction = missing;
        vm.TransferMode = MacroTransferMode.Call;
        vm.CancelCommand.Execute(null);
        var cancelled = (ExecuteMacroAction)vm.Actions[0];
        Equal(MacroTransferMode.Jump, cancelled.Mode);
        Check(cancelled.Description.Contains(target.Name));
        var loaded = fixture.Storage.LoadById(profile.Id)!;
        target.Name = "Renamed macro";
        using var reopened = new EditorFixture(loaded, new[] { target });
        var restored = (ExecuteMacroAction)reopened.ViewModel.Actions[0];
        Check(restored.Description.Contains(target.Name));
        Equal(MacroTransferMode.Jump, restored.Mode);
        Equal(ImageNotFoundBehavior.EndRound, ((FindImageAction)loaded.Actions[1]).NotFoundBehavior);
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
        // Realize the presenters so each item inherits the menu's editor context off screen.
        menu.Visibility = Visibility.Visible;
        menu.ApplyTemplate();
        menu.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        menu.Arrange(new Rect(new Point(), menu.DesiredSize));
        menu.UpdateLayout();
        view.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
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
        public EditorFixture(MacroProfile? macro = null, IEnumerable<MacroProfile>? macroLibrary = null)
        {
            _match = new ImageMatchService(_capture);
            ViewModel = new(Storage, _capture, _match, macro ?? new MacroProfile { Name = "Regression macro" }, macroLibrary);
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
