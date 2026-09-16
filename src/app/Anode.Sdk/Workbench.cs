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

    void Activate(IDocument document);

    /// <summary>Shows the banner over the canvas; showing another one replaces it.</summary>
    void ShowBanner(Banner banner);

    void DismissBanner(Banner banner);

    /// <summary>Interface chrome theme. The drawing sheet stays light in both.</summary>
    ThemeVariant Theme { get; set; }

    ILog Log { get; }
}
