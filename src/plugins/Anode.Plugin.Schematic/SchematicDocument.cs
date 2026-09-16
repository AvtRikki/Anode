using System.Globalization;
using Avalonia.Controls;
using Avalonia.Input;
using Anode.Kicad;
using Anode.Sdk;
using Anode.Render;

namespace Anode.Plugin.Schematic;

/// <summary>Opens <c>.kicad_sch</c> files as document tabs.</summary>
public sealed class SchematicDocumentType(ILog log) : IDocumentType
{
    /// <summary>Panels and documents refer to the type by this id.</summary>
    public const string TypeId = "anode.schematic";

    public string Id => TypeId;

    public string Label => Tr.T("sch.document.label");

    public IReadOnlyList<string> Extensions { get; } = [".kicad_sch"];

    public Task<IDocument> OpenAsync(string path, CancellationToken cancellationToken) => Task.Run(
        () =>
        {
            var schematic = Anode.Kicad.Schematic.Load(path);
            var scene = SchematicSceneBuilder.Build(schematic);
            if (GraphicsOptions.Renderer == RendererKind.OpenGl)
            {
                SceneTriangulator.Triangulate(scene);
            }

            log.Info(Tr.T("sch.log.loaded", Path.GetFileName(path), schematic.Symbols.Count, scene.PrimitiveCount));
            return (IDocument)new SchematicDocument(schematic, scene, path);
        },
        cancellationToken);
}

/// <summary>One open sheet: the canvas and what the workbench shows around it.</summary>
public sealed class SchematicDocument : DocumentBase
{
    private readonly List<IDisposable> _registrations = [];
    private readonly IReadOnlyList<SheetCheck> _checks;
    private SchematicCanvas? _canvas;
    private string _cursor = string.Empty;
    private string _frame = string.Empty;
    private double _zoom;

    internal SchematicDocument(Anode.Kicad.Schematic schematic, SchematicScene scene, string path)
    {
        Sheet = schematic;
        Scene = scene;
        FilePath = path;
        _checks = SheetChecks.Run(schematic);
        Tr.Changed += OnLanguageChanged;
    }

    public Anode.Kicad.Schematic Sheet { get; }

    public SchematicScene Scene { get; }

    public override string? DocumentTypeId => SchematicDocumentType.TypeId;

    public override string Title => Path.GetFileName(FilePath ?? Tr.T("sch.document.untitled"));

    /// <summary>Reading only for now: the schematic has no edits, so there is nothing to save.</summary>
    public override bool CanSave => false;

    public override string Summary
    {
        get
        {
            string zoom = _zoom > 0 ? _zoom.ToString("0", CultureInfo.InvariantCulture) + "%" : Count();
            return Tr.T("sch.summary.sheet", zoom);
        }
    }

    public override IReadOnlyList<StatusField> StatusFields
    {
        get
        {
            List<StatusField> fields = [];
            if (_cursor.Length > 0)
            {
                fields.Add(new StatusField(_cursor));
            }

            fields.Add(new StatusField(Count()));
            if (_canvas?.Selection is { } selection)
            {
                fields.Add(new StatusField(Tr.T("sch.status.selected", SchItemProperties.Header(selection).Title)));
            }

            if (_frame.Length > 0)
            {
                fields.Add(new StatusField(_frame, AlignEnd: true));
            }

            return fields;
        }
    }

    public override SelectionInfo? Selection
    {
        get
        {
            if (_canvas?.Selection is not { } item)
            {
                return null;
            }

            var (title, subtitle, tag) = SchItemProperties.Header(item);
            return new SelectionInfo(title, subtitle, [.. SchItemProperties.For(item)], tag);
        }
    }

    public override IReadOnlyList<Issue> Issues => [.. _checks.Select(c => c.ToIssue())];

    public override void Activate(IPluginContext context)
    {
        _registrations.Add(context.Commands.Register(new CommandDescriptor("sch.fit", "sch.command.fit")
        {
            ScopeKey = "scope.schematic",
            ShortcutText = "Home",
            Execute = () => _canvas?.ZoomToFit(),
        }));
    }

    public override void Deactivate()
    {
        foreach (var registration in _registrations)
        {
            registration.Dispose();
        }

        _registrations.Clear();
    }

    public override void Dispose()
    {
        Deactivate();
        Tr.Changed -= OnLanguageChanged;
        base.Dispose();
    }

    protected override Control CreateView()
    {
        var canvas = new SchematicCanvas { Scene = Scene };
        _canvas = canvas;
        canvas.CursorMoved += position =>
        {
            _cursor = position is { } p
                ? Tr.T("sch.status.cursor",
                    p.X.ToString("0.00", CultureInfo.InvariantCulture),
                    p.Y.ToString("0.00", CultureInfo.InvariantCulture),
                    Tr.T("sch.units.mm"))
                : string.Empty;
            OnPropertyChanged(nameof(StatusFields));
        };
        canvas.FrameRendered += ms =>
        {
            _frame = string.Create(CultureInfo.InvariantCulture, $"{canvas.BackendName} · {ms:0.0} ms");
            OnPropertyChanged(nameof(StatusFields));
        };
        canvas.ViewChanged += () =>
        {
            _zoom = canvas.ZoomPercent;
            OnPropertyChanged(nameof(Summary));
        };
        canvas.SelectionChanged += () => OnPropertiesChanged(nameof(Selection), nameof(StatusFields));

        return canvas;
    }

    private string Count() => Tr.T("sch.status.symbols", Sheet.Symbols.Count, Tr.Plural("sch.symbol", Sheet.Symbols.Count));

    private void OnLanguageChanged() =>
        OnPropertiesChanged(nameof(Title), nameof(Summary), nameof(StatusFields), nameof(Selection), nameof(Issues));
}

/// <summary>A check result kept in translation-independent form, so it survives a language switch.</summary>
internal sealed record SheetCheck(IssueSeverity Severity, string TitleKey, string DetailKey, object?[] DetailArgs, string LocationKey, object?[] LocationArgs)
{
    public Issue ToIssue() => new(Severity, Tr.T(TitleKey), Tr.T(DetailKey, DetailArgs), Tr.T(LocationKey, LocationArgs));
}

/// <summary>What can be said about a sheet without a full ERC.</summary>
internal static class SheetChecks
{
    public static IReadOnlyList<SheetCheck> Run(Anode.Kicad.Schematic schematic)
    {
        List<SheetCheck> checks = [];

        if (!schematic.IsSupportedVersion)
        {
            checks.Add(new SheetCheck(IssueSeverity.Error, "sch.issue.oldFormat.title", "sch.issue.oldFormat.detail", [],
                "sch.issue.version", [schematic.Version]));
        }
        else if (schematic.IsNewerThanKnown)
        {
            checks.Add(new SheetCheck(IssueSeverity.Error, "sch.issue.newFormat.title", "sch.issue.newFormat.detail", [],
                "sch.issue.version", [schematic.Version]));
        }

        // A symbol whose definition is missing from the file draws as nothing, so it is worth saying out loud.
        var missing = schematic.Symbols
            .Where(s => s.Definition is null)
            .Select(s => s.Reference ?? s.LibId)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToList();

        if (missing.Count > 0)
        {
            string names = string.Join(", ", missing.Take(6)) + (missing.Count > 6 ? " …" : string.Empty);
            checks.Add(new SheetCheck(IssueSeverity.Warning, "sch.issue.noDefinition.title", "sch.issue.noDefinition.detail", [names],
                "sch.issue.definitions", [missing.Count, schematic.Symbols.Count]));
        }

        return checks;
    }
}
