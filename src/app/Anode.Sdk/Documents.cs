using System.ComponentModel;
using System.Runtime.CompilerServices;
using Avalonia.Controls;

namespace Anode.Sdk;

/// <summary>A kind of file a plugin can open as a document tab.</summary>
public interface IDocumentType
{
    string Id { get; }

    /// <summary>Short noun used next to tabs and in palette scopes: "плата", "схема".</summary>
    string Label { get; }

    /// <summary>Lower-case extensions including the dot, e.g. ".kicad_pcb".</summary>
    IReadOnlyList<string> Extensions { get; }

    /// <summary>Loads the file. Called off the UI thread; create UI lazily in <see cref="IDocument.View"/>.</summary>
    Task<IDocument> OpenAsync(string path, CancellationToken cancellationToken);

    /// <summary>
    /// Whether this type can start a file from nothing — "New sheet", "New board". The workbench offers it in the
    /// palette and on the start page; a type that only reads files leaves this alone.
    /// </summary>
    bool CanCreate => false;

    /// <summary>Writes an empty document at <paramref name="path"/>. Called off the UI thread.</summary>
    Task CreateAsync(string path, CancellationToken cancellationToken) =>
        throw new NotSupportedException($"{Id} cannot create files.");
}

public interface IDocumentRegistry
{
    IReadOnlyList<IDocumentType> Types { get; }

    IDisposable Register(IDocumentType type);

    IDocumentType? FindFor(string path);
}

/// <summary>One open tab. One tab is one document.</summary>
public interface IDocument : INotifyPropertyChanged, IDisposable
{
    /// <summary>Id of the <see cref="IDocumentType"/> that opened this document; null for documents without one.</summary>
    string? DocumentTypeId { get; }

    /// <summary>Tab text, usually the file name.</summary>
    string Title { get; }

    string? FilePath { get; }

    /// <summary>Unsaved changes; the tab shows the alert dot.</summary>
    bool IsDirty { get; }

    /// <summary>Right side of the tab strip: "плата · 4 слоя", "схема · 140%".</summary>
    string Summary { get; }

    /// <summary>The editor, including its own toolbar. Created on first access, on the UI thread.</summary>
    Control View { get; }

    /// <summary>Status bar fields contributed while the document is active, left to right.</summary>
    IReadOnlyList<StatusField> StatusFields { get; }

    /// <summary>Tools this document offers; empty for a document that has none.</summary>
    IReadOnlyList<ToolDescriptor> Tools => [];

    /// <summary>The tool in use, so its button reads as pressed; null when the pointer just selects.</summary>
    string? ActiveToolId => null;

    /// <summary>What the inspector shows; null when nothing is selected.</summary>
    SelectionInfo? Selection { get; }

    /// <summary>Check results listed in the bottom dock.</summary>
    IReadOnlyList<Issue> Issues { get; }

    bool CanSave { get; }

    /// <summary>Saves to <paramref name="path"/> or the current file.</summary>
    Task<bool> SaveAsync(string? path = null);

    /// <summary>The document became the active tab (register document-scoped commands here).</summary>
    void Activate(IPluginContext context);

    void Deactivate();
}

/// <summary>
/// A tool of a document: what the pointer does on its canvas until another one takes over. The workbench draws them
/// floating over the canvas and offers them in its context menu, so a document decides what it can do and the frame
/// only shows it.
/// </summary>
/// <param name="IconKey">Name from <see cref="Icons"/>.</param>
public sealed record ToolDescriptor(string Id, string TitleKey, string IconKey)
{
    public string Title => Tr.T(TitleKey);

    /// <summary>Shortcut as displayed, e.g. "W". The document registers the gesture itself.</summary>
    public string? ShortcutText { get; init; }

    /// <summary>
    /// Kinds of the same tool — a label and its global and hierarchical sorts. The button does the tool itself and
    /// offers these behind a chevron; a document that has none leaves this empty.
    /// </summary>
    public IReadOnlyList<ToolDescriptor> Variants { get; init; } = [];

    public required Action Activate { get; init; }
}

/// <param name="AlignEnd">Pushed to the right side of the status bar.</param>
public sealed record StatusField(string Text, bool AlignEnd = false, bool IsAlert = false);

/// <param name="Title">Large heading, e.g. the reference "U3".</param>
/// <param name="Subtitle">E.g. "Регулятор · SOT-23-3".</param>
/// <param name="Tag">Optional tag next to the title, e.g. "В наличии".</param>
public sealed record SelectionInfo(string Title, string? Subtitle, IReadOnlyList<PropertyItem> Properties, string? Tag = null);

/// <param name="IsSection">A section overline ("ВЫВОДЫ") instead of a name/value row.</param>
public sealed record PropertyItem(string Name, string Value, bool IsSection = false);

public enum IssueSeverity
{
    Error,
    Warning,
}

/// <param name="Location">Monospaced location, e.g. "F.Cu · 34.2, 41.7".</param>
/// <param name="ActionLabelKey">Translation key of the row's action button.</param>
public sealed record Issue(IssueSeverity Severity, string Title, string Detail, string Location, string ActionLabelKey = "action.show", Action? Action = null)
{
    public string ActionLabel => Tr.T(ActionLabelKey);
}

/// <summary>Convenience base for documents: property change plumbing and empty defaults.</summary>
public abstract class DocumentBase : IDocument
{
    private Control? _view;

    public event PropertyChangedEventHandler? PropertyChanged;

    public virtual string? DocumentTypeId => null;

    public abstract string Title { get; }

    public string? FilePath { get; protected set; }

    public virtual bool IsDirty => false;

    public virtual string Summary => string.Empty;

    public Control View => _view ??= CreateView();

    public virtual IReadOnlyList<StatusField> StatusFields => [];

    /// <summary>Tools this document offers; a document without any leaves this empty.</summary>
    public virtual IReadOnlyList<ToolDescriptor> Tools => [];

    /// <summary>The tool in use, so its button reads as pressed.</summary>
    public virtual string? ActiveToolId => null;

    public virtual SelectionInfo? Selection => null;

    public virtual IReadOnlyList<Issue> Issues => [];

    public virtual bool CanSave => false;

    public virtual Task<bool> SaveAsync(string? path = null) => Task.FromResult(false);

    public virtual void Activate(IPluginContext context)
    {
    }

    public virtual void Deactivate()
    {
    }

    public virtual void Dispose()
    {
        GC.SuppressFinalize(this);
    }

    protected abstract Control CreateView();

    protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    protected void OnPropertiesChanged(params string[] names)
    {
        foreach (string name in names)
        {
            OnPropertyChanged(name);
        }
    }
}
