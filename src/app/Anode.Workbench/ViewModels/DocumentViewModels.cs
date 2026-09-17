using System.Collections.ObjectModel;
using System.ComponentModel;
using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Anode.Sdk;

namespace Anode.Workbench.ViewModels;

public sealed partial class DocumentTabViewModel : ObservableObject, IDisposable
{
    public DocumentTabViewModel(IDocument document, DocumentPaneViewModel pane)
    {
        Document = document;
        Pane = pane;
        document.PropertyChanged += OnDocumentChanged;
    }

    public IDocument Document { get; }

    public DocumentPaneViewModel Pane { get; internal set; }

    public string Title => Document.Title;

    public bool IsDirty => Document.IsDirty;

    [ObservableProperty]
    public partial bool IsActive { get; set; }

    /// <summary>A pinned tab keeps its place at the front and shows the pin instead of the close button.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PinLabel))]
    public partial bool IsPinned { get; set; }

    public string CloseLabel => Tr.T("shell.tabs.close");

    public string CloseHint => Tr.T("shell.tabs.closeHint");

    public string CloseOthersLabel => Tr.T("shell.tabs.closeOthers");

    public string PinLabel => Tr.T(IsPinned ? "shell.tabs.unpin" : "shell.tabs.pin");

    public string PinHint => Tr.T("shell.tabs.pinHint");

    public Control? CloseIcon => Icons.Draw(Icons.Close, 11, 1.4);

    public Control? PinIcon => Icons.Draw(Icons.Pin, 11, 1.2);

    [RelayCommand]
    private void Activate() => Pane.Shell.ActivateTab(this);

    [RelayCommand]
    private Task Close() => Pane.Shell.CloseTabAsync(this);

    [RelayCommand]
    private Task CloseOthers() => Pane.Shell.CloseOthersAsync(this);

    [RelayCommand]
    private void TogglePin() => Pane.Shell.SetPinned(this, !IsPinned);

    /// <summary>The document's title text changed without the document changing, e.g. after a language switch.</summary>
    public void RaiseTitle() =>
        OnPropertiesChanged(nameof(Title), nameof(CloseLabel), nameof(CloseHint), nameof(CloseOthersLabel), nameof(PinLabel), nameof(PinHint));

    private void OnPropertiesChanged(params string[] names)
    {
        foreach (string name in names)
        {
            OnPropertyChanged(name);
        }
    }

    public void Dispose() => Document.PropertyChanged -= OnDocumentChanged;

    private void OnDocumentChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(IDocument.Title):
                OnPropertyChanged(nameof(Title));
                break;
            case nameof(IDocument.IsDirty):
                OnPropertyChanged(nameof(IsDirty));
                break;
            case nameof(IDocument.Summary) when IsActive:
                Pane.RaiseSummary();
                break;
        }
    }
}

/// <summary>One half of the document area: its own tab strip and editor. The window splits into at most two.</summary>
public sealed partial class DocumentPaneViewModel(ShellViewModel shell) : ObservableObject
{
    public ShellViewModel Shell { get; } = shell;

    private IReadOnlyList<ToolButtonViewModel> _tools = [];
    private IDocument? _toolsOf;

    public ObservableCollection<DocumentTabViewModel> Tabs { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ActiveView), nameof(Summary), nameof(HasDocument), nameof(Tools), nameof(HasTools))]
    public partial DocumentTabViewModel? ActiveTab { get; set; }

    [ObservableProperty]
    public partial bool IsFocused { get; set; }

    public Control? ActiveView => Controls.ControlHost.Detach(ActiveTab?.Document.View);

    public string Summary => ActiveTab?.Document.Summary ?? string.Empty;

    public bool HasDocument => ActiveTab is not null;

    /// <summary>
    /// Tools of the document in front, floating over its canvas. The buttons are kept: rebuilding them on every
    /// change of the document's state would replace the bar under the pointer and eat a press that had begun.
    /// </summary>
    public IReadOnlyList<ToolButtonViewModel> Tools
    {
        get
        {
            var document = ActiveTab?.Document;
            if (!ReferenceEquals(document, _toolsOf))
            {
                _toolsOf = document;
                _tools = document is { Tools.Count: > 0 }
                    ? [.. document.Tools.Select(t => new ToolButtonViewModel(t, document))]
                    : [];
            }

            return _tools;
        }
    }

    public bool HasTools => Tools.Count > 0;

    /// <summary>The document changed tool: the same buttons, only the pressed one moved.</summary>
    public void RaiseTools()
    {
        foreach (var tool in Tools)
        {
            tool.RaiseActive();
        }
    }

    /// <summary>The right half of a split window, drawn with a separating line.</summary>
    public bool IsSecondary => Shell.Panes.IndexOf(this) > 0;

    public void RaiseSummary() => OnPropertyChanged(nameof(Summary));

    public void RaiseIsSecondary() => OnPropertyChanged(nameof(IsSecondary));

    partial void OnActiveTabChanged(DocumentTabViewModel? oldValue, DocumentTabViewModel? newValue)
    {
        if (oldValue is not null)
        {
            oldValue.IsActive = false;
        }

        if (newValue is not null)
        {
            newValue.IsActive = true;
        }
    }
}

/// <summary>One button of the floating tool bar.</summary>
public sealed partial class ToolButtonViewModel(ToolDescriptor descriptor, IDocument document) : ObservableObject
{
    private Control? _icon;

    public string Title => descriptor.ShortcutText is { Length: > 0 } key
        ? $"{descriptor.Title} · {key}"
        : descriptor.Title;

    public Control? Icon => _icon ??= Icons.Draw(descriptor.IconKey, 16);

    /// <summary>The kinds behind the chevron; empty for a tool that is only itself.</summary>
    public IReadOnlyList<ToolButtonViewModel> Variants { get; } =
        [.. descriptor.Variants.Select(v => new ToolButtonViewModel(v, document))];

    public bool HasVariants => Variants.Count > 0;

    public bool IsActive => string.Equals(document.ActiveToolId, descriptor.Id, StringComparison.Ordinal)
        || descriptor.Variants.Any(v => string.Equals(document.ActiveToolId, v.Id, StringComparison.Ordinal))
        || (document.ActiveToolId is null && descriptor.Id.EndsWith(".select", StringComparison.Ordinal));

    /// <summary>The tool in use moved; the button itself is the same one.</summary>
    public void RaiseActive()
    {
        OnPropertyChanged(nameof(IsActive));
        foreach (var variant in Variants)
        {
            variant.RaiseActive();
        }
    }

    [RelayCommand]
    private void Use() => descriptor.Activate();
}
