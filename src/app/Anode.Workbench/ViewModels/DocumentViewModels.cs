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

    [RelayCommand]
    private void Activate() => Pane.Shell.ActivateTab(this);

    [RelayCommand]
    private Task Close() => Pane.Shell.CloseTabAsync(this);

    /// <summary>The document's title text changed without the document changing, e.g. after a language switch.</summary>
    public void RaiseTitle() => OnPropertyChanged(nameof(Title));

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

    public ObservableCollection<DocumentTabViewModel> Tabs { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ActiveView), nameof(Summary), nameof(HasDocument))]
    public partial DocumentTabViewModel? ActiveTab { get; set; }

    [ObservableProperty]
    public partial bool IsFocused { get; set; }

    public Control? ActiveView => Controls.ControlHost.Detach(ActiveTab?.Document.View);

    public string Summary => ActiveTab?.Document.Summary ?? string.Empty;

    public bool HasDocument => ActiveTab is not null;

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
