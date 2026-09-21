using Avalonia.Input;

namespace Anode.Sdk;

/// <summary>
/// An action reachable from the command palette, menus and shortcuts. Every action in the workbench should be one
/// (the palette is the keyboard route to everything).
/// </summary>
/// <param name="TitleKey">Translation key of the title; unknown keys are shown as they are.</param>
public sealed record CommandDescriptor(string Id, string TitleKey)
{
    /// <summary>Values for the <c>{0}</c> placeholders of the title, e.g. the language name.</summary>
    public object?[]? TitleArgs { get; init; }

    /// <summary>Title in the active language.</summary>
    public string Title => TitleArgs is null ? Tr.T(TitleKey) : Tr.T(TitleKey, TitleArgs);

    /// <summary>Translation key of the scope: where the command acts ("board", "schematic", "rules").</summary>
    public string? ScopeKey { get; init; }

    /// <summary>Scope in the active language, shown to the right of the title in the palette.</summary>
    public string? Scope => ScopeKey is null ? null : Tr.T(ScopeKey);

    /// <summary>Shortcut as displayed, e.g. "X" or "⌘S". Handled by <see cref="Gesture"/>.</summary>
    public string? ShortcutText { get; init; }

    public KeyGesture? Gesture { get; init; }

    /// <summary>Shown with the "beta" tag.</summary>
    public bool IsBeta { get; init; }

    /// <summary>
    /// Which main menu the command belongs to — "menu.file", "menu.edit", "menu.view", "menu.place". The menu is
    /// built from the commands that exist at this moment, so it follows the document in front: a sheet brings its
    /// drawing tools into Place, a board brings its own. A command without this lives in the palette only.
    /// </summary>
    public string? MenuKey { get; init; }

    /// <summary>Order inside its menu; leave gaps so a plugin can slot items between the shell's own.</summary>
    public int MenuOrder { get; init; }

    public Func<bool> CanExecute { get; init; } = static () => true;

    public required Action Execute { get; init; }
}

public interface ICommandRegistry
{
    IReadOnlyList<CommandDescriptor> Commands { get; }

    event Action? Changed;

    /// <summary>Adds a command; disposing the result removes it again.</summary>
    IDisposable Register(CommandDescriptor command);

    CommandDescriptor? Find(string id);

    /// <summary>Runs the command if it exists and can execute.</summary>
    bool TryExecute(string id);
}
