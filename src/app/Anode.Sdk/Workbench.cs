using Avalonia.Styling;

namespace Anode.Sdk;

/// <summary>The single persistent notification. It stays until its cause is resolved and never times out.</summary>
/// <param name="IsAlert">Magenta dot: a decision is needed.</param>
public sealed record Banner(string Message, string? ActionLabel = null, Action? Action = null, bool IsAlert = true);

public interface IWorkbench
{
    IReadOnlyList<IDocument> Documents { get; }

    IDocument? ActiveDocument { get; }

    event Action? ActiveDocumentChanged;

    /// <summary>Active document's selection, issues or status changed.</summary>
    event Action? ActiveDocumentStateChanged;

    /// <summary>Opens a file with the plugin registered for its extension, or activates its existing tab.</summary>
    Task<IDocument?> OpenAsync(string path);

    /// <summary>
    /// Opens a file at one of its appearances — a sheet placed twice is one tab showing one of its places at a time.
    /// </summary>
    async Task<IDocument?> OpenAsync(string path, string? instance)
    {
        var document = await OpenAsync(path);
        if (document is not null && instance is not null)
        {
            document.ShowInstance(instance);
        }

        return document;
    }

    void Activate(IDocument document);

    /// <summary>Shows the banner over the canvas; showing another one replaces it.</summary>
    void ShowBanner(Banner banner);

    void DismissBanner(Banner banner);

    /// <summary>
    /// Asks the inspector to put the caret in the first value that can be changed — KiCad's E. The panel listens for
    /// this the way the panels listen for a change of language, so a document can ask for it without knowing which
    /// panel is up or where it is docked.
    /// </summary>
    void FocusInspector() => InspectorFocus.Request();

    /// <summary>
    /// Brings a panel to the front of its section, opening the section if it was closed — what a command that works
    /// through a panel does first, as Ctrl+F does with Find. Unlike the rail icon it never closes anything: asking
    /// twice leaves the panel where the first asking put it.
    /// </summary>
    void RevealPanel(string panelId);

    /// <summary>
    /// Asks where to put a file the plugin is about to write — an export, a report. Answers the path chosen, or null
    /// when nobody chose one. The plugin writes the file itself; the workbench only asks the question.
    /// </summary>
    /// <param name="suggestedName">The name to offer, extension included.</param>
    /// <param name="extension">The extension the picker should default to, with or without its dot.</param>
    /// <param name="titleKey">Translation key for the dialog's title.</param>
    /// <param name="startDirectory">Where to open the picker, when there is a sensible place.</param>
    Task<string?> AskWhereToWriteAsync(string suggestedName, string extension, string titleKey, string? startDirectory = null);

    /// <summary>Interface chrome theme. The drawing sheet stays light in both.</summary>
    ThemeVariant Theme { get; set; }

    ILog Log { get; }
}

/// <summary>
/// The request behind <see cref="IWorkbench.FocusInspector"/>. A document raises it; whichever inspector is showing
/// answers. Nothing is passed with it: the panel already knows what is selected.
/// </summary>
public static class InspectorFocus
{
    public static event Action? Requested;

    public static void Request() => Requested?.Invoke();
}
