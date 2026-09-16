using Anode.Sdk;

namespace Anode.Workbench.Services;

internal sealed class Registration(Action dispose) : IDisposable
{
    private Action? _dispose = dispose;

    public void Dispose() => Interlocked.Exchange(ref _dispose, null)?.Invoke();
}

public sealed class CommandRegistry : ICommandRegistry
{
    private readonly List<CommandDescriptor> _commands = [];

    public IReadOnlyList<CommandDescriptor> Commands => _commands;

    public event Action? Changed;

    public IDisposable Register(CommandDescriptor command)
    {
        // A later registration with the same id replaces the earlier one (e.g. the active document's Undo).
        _commands.RemoveAll(c => c.Id == command.Id);
        _commands.Add(command);
        Changed?.Invoke();
        return new Registration(() =>
        {
            if (_commands.Remove(command))
            {
                Changed?.Invoke();
            }
        });
    }

    public CommandDescriptor? Find(string id) => _commands.LastOrDefault(c => c.Id == id);

    /// <summary>Tells listeners the titles changed, e.g. after a language switch.</summary>
    public void RaiseChanged() => Changed?.Invoke();

    public bool TryExecute(string id)
    {
        if (Find(id) is not { } command || !command.CanExecute())
        {
            return false;
        }

        command.Execute();
        return true;
    }
}

public sealed class PanelRegistry : IPanelRegistry
{
    private readonly List<PanelDescriptor> _panels = [];

    public IReadOnlyList<PanelDescriptor> Panels => _panels;

    public event Action? Changed;

    public IDisposable Register(PanelDescriptor panel)
    {
        if (_panels.Any(p => p.Id == panel.Id))
        {
            throw new InvalidOperationException($"Panel '{panel.Id}' is already registered.");
        }

        _panels.Add(panel);
        Changed?.Invoke();
        return new Registration(() =>
        {
            if (_panels.Remove(panel))
            {
                Changed?.Invoke();
            }
        });
    }
}

public sealed class DocumentRegistry : IDocumentRegistry
{
    private readonly List<IDocumentType> _types = [];

    public IReadOnlyList<IDocumentType> Types => _types;

    public IDisposable Register(IDocumentType type)
    {
        _types.Add(type);
        return new Registration(() => _types.Remove(type));
    }

    public IDocumentType? FindFor(string path)
    {
        string extension = Path.GetExtension(path).ToLowerInvariant();
        return _types.LastOrDefault(t => t.Extensions.Contains(extension));
    }
}
