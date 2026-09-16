using System.Globalization;
using Avalonia.Input;
using Avalonia.Styling;
using Anode.Sdk;
using Anode.Workbench.Panels;
using Anode.Workbench.ViewModels;

namespace Anode.Workbench;

/// <summary>Commands and panels the workbench provides itself, before any plugin.</summary>
public static class ShellContributions
{
    public static void Register(ShellViewModel shell)
    {
        var command = OperatingSystem.IsMacOS() ? KeyModifiers.Meta : KeyModifiers.Control;

        CommandDescriptor[] commands =
        [
            new("shell.palette", "command.shell.palette")
            {
                ScopeKey = "scope.window", ShortcutText = "⌘K", Gesture = new KeyGesture(Key.K, command),
                Execute = shell.OpenPalette,
            },
            new("file.open", "command.file.open")
            {
                ScopeKey = "scope.file", ShortcutText = "⌘O", Gesture = new KeyGesture(Key.O, command),
                Execute = () => _ = OpenPickedAsync(shell),
            },
            new("file.save", "command.file.save")
            {
                ScopeKey = "scope.file", ShortcutText = "⌘S", Gesture = new KeyGesture(Key.S, command),
                CanExecute = () => shell.ActiveDocument is { CanSave: true },
                Execute = () => _ = shell.ActiveDocument is { } doc ? shell.SaveAsync(doc) : Task.CompletedTask,
            },
            new("file.saveAs", "command.file.saveAs")
            {
                ScopeKey = "scope.file", ShortcutText = "⌘⇧S", Gesture = new KeyGesture(Key.S, command | KeyModifiers.Shift),
                CanExecute = () => shell.ActiveDocument is { CanSave: true },
                Execute = () => _ = shell.ActiveDocument is { } doc ? shell.SaveAsync(doc, askForPath: true) : Task.CompletedTask,
            },
            new("doc.close", "command.doc.close")
            {
                ScopeKey = "scope.window", ShortcutText = "⌘W", Gesture = new KeyGesture(Key.W, command),
                CanExecute = () => shell.ActivePane.ActiveTab is not null,
                Execute = () => _ = shell.ActivePane.ActiveTab is { } tab ? shell.CloseTabAsync(tab) : Task.CompletedTask,
            },
            new("view.split", "command.view.split")
            {
                ScopeKey = "scope.window", ShortcutText = "⌘\\", Gesture = new KeyGesture(Key.OemPipe, command),
                Execute = shell.ToggleSplit,
            },
            new("view.leftDock", "command.view.leftDock")
            {
                ScopeKey = "scope.window", ShortcutText = "⌥1", Gesture = new KeyGesture(Key.D1, KeyModifiers.Alt),
                Execute = () => shell.IsLeftDockVisible = !shell.IsLeftDockVisible,
            },
            new("view.rightDock", "command.view.rightDock")
            {
                ScopeKey = "scope.window", ShortcutText = "⌥2", Gesture = new KeyGesture(Key.D2, KeyModifiers.Alt),
                Execute = () => shell.IsRightDockVisible = !shell.IsRightDockVisible,
            },
            new("view.bottomDock", "command.view.bottomDock")
            {
                ScopeKey = "scope.window", ShortcutText = "⌥3", Gesture = new KeyGesture(Key.D3, KeyModifiers.Alt),
                Execute = () => shell.IsBottomDockVisible = !shell.IsBottomDockVisible,
            },
            new("view.theme", "command.view.theme")
            {
                ScopeKey = "scope.window",
                Execute = () => shell.Theme = shell.Theme == ThemeVariant.Dark ? ThemeVariant.Light : ThemeVariant.Dark,
            },
            new("view.start", "command.view.start")
            {
                ScopeKey = "scope.window",
                Execute = () => ShowStartPage(shell),
            },
        ];

        foreach (var descriptor in commands)
        {
            shell.Commands.Register(descriptor);
        }

        RegisterLanguages(shell);

        shell.Panels.Register(new PanelDescriptor("shell.project", "panel.project", DockSide.Left, _ => new ProjectPanel(shell))
        {
            RailLabelKey = "panel.project.rail", Group = "project", Order = 0,
        });
        shell.Panels.Register(new PanelDescriptor("shell.inspector", "panel.inspector", DockSide.Right, _ => new InspectorPanel(shell))
        {
            RailLabelKey = "panel.inspector.rail", Group = "inspect", Order = 0,
        });
        shell.Panels.Register(new PanelDescriptor("shell.issues", "panel.issues", DockSide.Bottom, _ => new IssuesPanel(shell))
        {
            RailLabelKey = "panel.issues.rail", Order = 0,
        });
        shell.Panels.Register(new PanelDescriptor("shell.console", "panel.console", DockSide.Bottom, _ => new ConsolePanel(shell.Log))
        {
            RailLabelKey = "panel.console.rail", Order = 10,
        });
    }

    /// <summary>One command per language a catalog was found for, so the palette lists them by their own name.</summary>
    public static void RegisterLanguages(ShellViewModel shell)
    {
        foreach (var culture in Tr.AvailableCultures)
        {
            var target = culture;
            shell.Commands.Register(new CommandDescriptor($"view.language.{culture.Name}", "command.view.language")
            {
                ScopeKey = "scope.window",
                TitleArgs = [Native(culture)],
                CanExecute = () => !string.Equals(Tr.Culture.Name, target.Name, StringComparison.OrdinalIgnoreCase),
                Execute = () => shell.SetLanguage(target),
            });
        }
    }

    public static void ShowStartPage(ShellViewModel shell)
    {
        if (shell.Documents.OfType<StartPageDocument>().FirstOrDefault() is { } existing)
        {
            shell.Activate(existing);
            return;
        }

        shell.AddDocument(new StartPageDocument(shell));
    }

    /// <summary>A language is named in itself: "English", "Русский".</summary>
    private static string Native(CultureInfo culture) =>
        culture.NativeName.Length > 0 ? char.ToUpper(culture.NativeName[0], culture) + culture.NativeName[1..] : culture.Name;

    private static async Task OpenPickedAsync(ShellViewModel shell)
    {
        if (shell.PickFileToOpen is { } pick && await pick() is { } path)
        {
            await shell.OpenAsync(path);
        }
    }
}
