using Avalonia.Controls;
using Anode.Sdk;
using CommunityToolkit.Mvvm.Input;

namespace Anode.Workbench.Services;

/// <summary>
/// The main menu, built from the commands that exist at this moment. The shell names the menus; what goes inside
/// them comes from whoever registered a command with a <see cref="CommandDescriptor.MenuKey"/> — so the menu follows
/// the document in front: a sheet brings its drawing tools into Place, and takes them away when it closes.
/// </summary>
public static class MainMenu
{
    /// <summary>The menus the shell knows, in the order macOS expects them; anything else follows, by key.</summary>
    private static readonly string[] Known = ["menu.file", "menu.edit", "menu.view", "menu.place"];

    public static NativeMenu Build(ICommandRegistry commands)
    {
        var menu = new NativeMenu();
        Fill(menu, commands);
        return menu;
    }

    /// <summary>
    /// Refills a menu that is already attached to a window. macOS tracks the menu it was given, so the way to change
    /// it is to change its items — handing over a different instance makes the exporter refuse the update.
    /// </summary>
    public static void Fill(NativeMenu menu, ICommandRegistry commands)
    {
        menu.Items.Clear();

        foreach (string key in Keys(commands))
        {
            var items = commands.Commands
                .Where(c => string.Equals(c.MenuKey, key, StringComparison.Ordinal))
                .OrderBy(c => c.MenuOrder)
                .ThenBy(c => c.Title, StringComparer.CurrentCulture)
                .ToList();

            if (items.Count == 0)
            {
                continue;
            }

            var submenu = new NativeMenu();
            foreach (var command in items)
            {
                var descriptor = command;
                submenu.Items.Add(new NativeMenuItem(descriptor.Title)
                {
                    Gesture = descriptor.Gesture,
                    Command = new RelayCommand(() => Run(descriptor), () => Safe(descriptor)),
                });
            }

            menu.Items.Add(new NativeMenuItem(Tr.T(key)) { Menu = submenu });
        }
    }

    private static IEnumerable<string> Keys(ICommandRegistry commands)
    {
        var used = commands.Commands
            .Select(c => c.MenuKey)
            .OfType<string>()
            .ToHashSet(StringComparer.Ordinal);

        return Known.Where(used.Contains).Concat(used.Except(Known, StringComparer.Ordinal).Order(StringComparer.Ordinal));
    }

    /// <summary>A menu must not take the window down with it: a command that throws is logged by its own scope.</summary>
    private static void Run(CommandDescriptor command)
    {
        if (Safe(command))
        {
            command.Execute();
        }
    }

    private static bool Safe(CommandDescriptor command)
    {
        try
        {
            return command.CanExecute();
        }
        catch (Exception)
        {
            return false;
        }
    }
}
