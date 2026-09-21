using System.Globalization;
using Anode.Geometry;
using Anode.Kicad;
using Anode.Kicad.Editing;
using Anode.Render.Fonts;
using Anode.Sdk;

namespace Anode.Plugin.Schematic;

/// <summary>Turns the selected sheet item into inspector rows.</summary>
internal static class SchItemProperties
{
    /// <summary>
    /// The three lines above the blocks: what the object is called, the kind as a chip beside it, and — filled in by
    /// the document, which is the one that knows — where it lives. An object with no name of its own puts its kind on
    /// the first line and lets the chip carry a count or a sort.
    /// </summary>
    /// <param name="sheetPath">The appearance of the sheet on show: a reused sheet names its parts per appearance.</param>
    public static (string Title, string? Subtitle, string? Tag) Header(SchItem item, string? sheetPath = null) => item switch
    {
        SymbolInstance symbol => (symbol.ReferenceAt(sheetPath) ?? symbol.LibId, null, Tr.T("sch.item.symbol")),
        SchWire wire => (Tr.T(wire.IsBus ? "sch.item.bus" : "sch.item.wire"), null, Length(wire)),
        SchBusEntry => (Tr.T("sch.item.busEntry"), null, null),
        SchJunction => (Tr.T("sch.item.junction"), null, null),
        SchNoConnect => (Tr.T("sch.item.noConnect"), null, null),
        SchLabel label => (label.Text, null, Tr.T(label.Kind switch
        {
            SchLabelKind.Global => "sch.item.globalLabel",
            SchLabelKind.Hierarchical => "sch.item.hierarchicalLabel",
            SchLabelKind.NetClassFlag => "sch.item.netclassFlag",
            _ => "sch.item.label",
        })),
        SchText text => (Shorten(text.Text), null, Tr.T("sch.item.text")),
        SchSheet sheet => (sheet.SheetName ?? Tr.T("sch.item.sheet"), null, Tr.T("sch.item.sheet")),
        SchGraphic graphic => (Tr.T("sch.item.graphic"), null, graphic.Kind.ToString().ToLowerInvariant()),
        _ => (Tr.T("sch.item.other"), null, null),
    };

    /// <summary>
    /// The inspector proper: named blocks in a fixed order, of which each kind has only some. A row that carries a
    /// <c>Commit</c> can be written; the rest are computed and stay bare.
    ///
    /// Two of the design's blocks are missing here on purpose. Electrics and class rules describe a net, and a sheet
    /// has no notion of a net until connectivity is built — so rather than show empty headings, the blocks that
    /// cannot yet be filled are left out.
    /// </summary>
    /// <param name="edit">Applies a change as one undoable step: a name for the history, and what to do.</param>
    public static IEnumerable<InspectorBlock> Blocks(
        SchItem item,
        Action<string, Action> edit,
        Func<SchItem, string?>? netOf = null,
        string? sheetPath = null)
    {
        switch (item)
        {
            case SymbolInstance symbol:
                yield return new InspectorBlock(Tr.T("sch.block.identity"),
                [
                    Writable("reference", symbol.ReferenceAt(sheetPath) ?? None, v => edit(Name("reference"), () => SchWrites.SetReference(symbol, v, sheetPath))),
                    Writable("value", symbol.Value ?? None, v => edit(Name("value"), () => SchWrites.SetField(symbol, "Value", v))),
                    Computed("library", symbol.LibId),

                    // Only a part drawn in sections can be asked which one it is; on the rest the row is noise.
                    .. symbol.Definition is { UnitCount: > 1 } sectioned
                        ? new[]
                        {
                            Writable("unit", symbol.UnitAt(sheetPath).ToString(CultureInfo.InvariantCulture), v =>
                            {
                                if (int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out int unit)
                                    && unit >= 1 && unit <= sectioned.UnitCount)
                                {
                                    edit(Name("unit"), () => SchWrites.SetUnit(symbol, unit, sheetPath));
                                }
                            }),
                        }
                        : [],
                    // What the part is left out of. KiCad keeps these words in two minds — a part is in the bill
                    // and on the board, but excluded from simulation — so each switch is read the way its own word
                    // is written and shown the way the designer thinks of it: what this part is kept out of.
                    Flag(symbol, "dnp", "doNotPopulate", symbol.IsDnp, edit),
                    Flag(symbol, "in_bom", "excludeFromBom", !symbol.InBom, edit, inverted: true),
                    Flag(symbol, "on_board", "excludeFromBoard", !symbol.OnBoard, edit, inverted: true),
                    Flag(symbol, "exclude_from_sim", "excludeFromSim", symbol.ExcludedFromSim, edit),

                    // De Morgan: only a part that carries a second body can be asked which way it is drawn.
                    .. symbol.Definition is { HasAlternateBody: true }
                        ? new[]
                        {
                            new InspectorRow(Name("bodyStyle"), Tr.T("sch.value.alternateBody"))
                            {
                                Switch = symbol.BodyStyle == 2,
                                Commit = v => edit(Name("bodyStyle"), () => SchWrites.SetBodyStyle(symbol, v == "yes" ? 2 : 1)),
                            },
                        }
                        : [],
                    .. symbol.Footprint is { Length: > 0 } footprint
                        ? new[] { Writable("footprint", footprint, v => edit(Name("footprint"), () => SchWrites.SetField(symbol, "Footprint", v))) }
                        : [],
                ]);

                yield return Geometry(item, edit);

                if (symbol.Definition?.PinsOf(symbol.UnitAt(sheetPath), symbol.BodyStyle).ToList() is { Count: > 0 } pins)
                {
                    yield return new InspectorBlock(
                        Tr.T("sch.block.connectionsOf", $"{pins.Count} {Tr.Plural("sch.pin", pins.Count)}"),
                        [.. pins.Select(pin => new InspectorRow(pin.Number, pin.Name))])
                    {
                        IsConnections = true,
                    };
                }

                break;

            case SchSheet sheet:
                yield return new InspectorBlock(Tr.T("sch.block.identity"),
                [
                    Writable("name", sheet.SheetName ?? None, v => edit(Name("name"), () => SchWrites.SetField(sheet, "Sheetname", v))),
                    Writable("file", sheet.SheetFile ?? None, v => edit(Name("file"), () => SchWrites.SetField(sheet, "Sheetfile", v))),

                    // The page is this appearance's: a sheet placed twice is two pages, numbered apart.
                    .. sheet.PageAt(sheetPath) is { Length: > 0 } page || sheet.Node.Find("instances") is not null
                        ? new[]
                        {
                            Writable("page", sheet.PageAt(sheetPath) ?? None, v =>
                            {
                                if (v.Length > 0)
                                {
                                    edit(Name("page"), () => SchWrites.SetPage(sheet, v, sheetPath));
                                }
                            }),
                        }
                        : [],
                ]);

                yield return new InspectorBlock(Tr.T("sch.block.geometry"),
                [
                    Position(item, edit),
                    Computed("size", Pair(sheet.Size, " × ")),
                ]);

                if (sheet.Pins.Count > 0)
                {
                    yield return new InspectorBlock(
                        Tr.T("sch.block.connectionsOf", $"{sheet.Pins.Count} {Tr.Plural("sch.pin", sheet.Pins.Count)}"),
                        [.. sheet.Pins.Select(pin => new InspectorRow(string.Empty, pin.Name) { Trailing = Shape(pin.Shape) })])
                    {
                        IsConnections = true,
                    };
                }

                break;

            case SchLabel label:
                yield return new InspectorBlock(Tr.T("sch.block.identity"),
                [
                    Writable("text", label.Text, v => edit(Name("text"), () => SchWrites.SetText(label, v))),
                    .. label.Kind is SchLabelKind.Global or SchLabelKind.Hierarchical
                        ? new[] { Computed("shape", Shape(label.Shape)) }
                        : [],
                ]);

                foreach (var electrics in Electrics(item, netOf))
                {
                    yield return electrics;
                }

                yield return Typography(label, edit);
                yield return Geometry(item, edit);
                break;

            case SchText text:
                yield return new InspectorBlock(Tr.T("sch.block.identity"),
                [
                    Writable("text", text.Text, v => edit(Name("text"), () => SchWrites.SetText(text, v))),
                ]);

                yield return Typography(text, edit);
                yield return Geometry(item, edit);
                break;

            case SchWire wire:
                foreach (var electrics in Electrics(item, netOf))
                {
                    yield return electrics;
                }

                var points = wire.Points;
                if (points.Length > 0)
                {
                    yield return new InspectorBlock(Tr.T("sch.block.geometry"),
                    [
                        Computed("position", Pair(points[0])),
                        Computed("size", Pair(points[^1])),
                        Computed("length", Mm((long)Total(points))),
                    ]);
                }

                break;

            case SchGraphic graphic:
                yield return new InspectorBlock(Tr.T("sch.block.geometry"),
                [
                    Computed("shape", graphic.Kind.ToString().ToLowerInvariant()),
                    Computed("position", Pair(graphic.Kind == SchShapeKind.Circle ? graphic.Center : graphic.Start)),
                ]);

                break;

            case SchJunction junction:
                yield return new InspectorBlock(Tr.T("sch.block.geometry"),
                [
                    Position(item, edit),
                    Computed("size", Mm(junction.Diameter)),
                ]);

                break;

            default:
                yield return Geometry(item, edit);
                break;
        }
    }

    /// <summary>
    /// What the thing is connected to. Absent rather than empty when nothing is known: a block headed "electrics"
    /// with nothing under it would suggest the answer is "none" when it is "not worked out".
    /// </summary>
    private static IEnumerable<InspectorBlock> Electrics(SchItem item, Func<SchItem, string?>? netOf)
    {
        if (netOf?.Invoke(item) is { Length: > 0 } net)
        {
            yield return new InspectorBlock(Tr.T("sch.block.electrics"), [Computed("net", net)]);
        }
    }

    /// <summary>
    /// How the text is set: its face — the stroke font, or any this machine or the file itself has — and whether it
    /// is bold, italic or both, the four KiCad offers as one choice.
    /// </summary>
    private static InspectorBlock Typography(SchItem item, Action<string, Action> edit)
    {
        var font = item.Font;
        string stroke = Tr.T("sch.property.strokeFont");
        string style = Style(font.Bold, font.Italic);

        return new InspectorBlock(Tr.T("sch.block.typography"),
        [
            new InspectorRow(Name("font"), font.Face ?? stroke)
            {
                Choices = [stroke, .. OutlineText.Families()],
                Commit = v => edit(Name("font"), () => FontWrites.SetFace(item.Node, v == stroke ? null : v)),
            },
            new InspectorRow(Name("style"), style)
            {
                Choices = [.. Styles.Select(s => Tr.T($"sch.style.{s.Key}"))],
                Commit = v =>
                {
                    if (Styles.FirstOrDefault(s => Tr.T($"sch.style.{s.Key}") == v) is { Key: not null } picked)
                    {
                        edit(Name("style"), () =>
                        {
                            FontWrites.SetBold(item.Node, picked.Bold);
                            FontWrites.SetItalic(item.Node, picked.Italic);
                        });
                    }
                },
            },
        ]);
    }

    private static readonly (string Key, bool Bold, bool Italic)[] Styles =
        [("normal", false, false), ("bold", true, false), ("italic", false, true), ("boldItalic", true, true)];

    private static string Style(bool bold, bool italic) =>
        Tr.T($"sch.style.{Styles.First(s => s.Bold == bold && s.Italic == italic).Key}");

    /// <summary>Where the item sits, and which way it faces — the one block almost everything on a sheet has.</summary>
    private static InspectorBlock Geometry(SchItem item, Action<string, Action> edit) =>
        new(Tr.T("sch.block.geometry"),
        [
            Position(item, edit),
            Writable("angle", Deg(item.Angle), v =>
            {
                if (ParseAngle(v) is { } degrees)
                {
                    edit(Name("angle"), () => SchWrites.SetAngle(item, degrees));
                }
            }),
        ]);

    private static InspectorRow Position(SchItem item, Action<string, Action> edit) =>
        Writable("position", Pair(item.Position), v =>
        {
            if (ParsePoint(v) is { } at)
            {
                edit(Name("position"), () => SchWrites.SetPosition(item, at));
            }
        });

    /// <summary>
    /// A switch for one of the words a part carries. <paramref name="on"/> is what the designer sees — what the
    /// part is kept out of — and <paramref name="inverted"/> says the file writes the opposite of that.
    /// </summary>
    private static InspectorRow Flag(
        SymbolInstance symbol,
        string word,
        string nameKey,
        bool on,
        Action<string, Action> edit,
        bool inverted = false)
    {
        return new InspectorRow(Name(nameKey), Tr.T("sch.value.excluded"))
        {
            Switch = on,
            Commit = v =>
            {
                bool wanted = v == "yes";
                edit(Name(nameKey), () => SchWrites.SetFlag(symbol, word, inverted ? !wanted : wanted));
            },
        };
    }

    private static InspectorRow Writable(string nameKey, string value, Action<string> commit) =>
        new(Name(nameKey), value) { Commit = commit };

    private static InspectorRow Computed(string nameKey, string value) => new(Name(nameKey), value);

    private static string Name(string nameKey) => Tr.T($"sch.property.{nameKey}");

    /// <summary>
    /// A pair of millimetres as the inspector writes it: "148.59 / 105.41". No unit is appended — a pair is always
    /// millimetres, and the suffix only cost the panel the width it needed to show the second number.
    /// </summary>
    private static string Pair(Vector2L p, string separator = " / ") =>
        $"{Units.NmToMm(p.X).ToString("0.###", CultureInfo.InvariantCulture)}{separator}"
        + $"{Units.NmToMm(p.Y).ToString("0.###", CultureInfo.InvariantCulture)}";

    /// <summary>The direction written on a label or a sheet pin, in the reader's language.</summary>
    private static string Shape(string shape) => Tr.T($"sch.shape.{shape}");

    /// <summary>"127.0 / 88.9", "127, 88.9" or "127 88.9" — millimetres, however they were typed.</summary>
    private static Vector2L? ParsePoint(string text)
    {
        var parts = text.Replace('/', ' ').Replace(',', ' ').Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var numbers = parts.Where(part => double.TryParse(part, NumberStyles.Float, CultureInfo.InvariantCulture, out _)).ToList();
        if (numbers.Count < 2)
        {
            return null;
        }

        return new Vector2L(
            Units.MmToNm(double.Parse(numbers[0], CultureInfo.InvariantCulture)),
            Units.MmToNm(double.Parse(numbers[1], CultureInfo.InvariantCulture)));
    }

    private static double? ParseAngle(string text) =>
        double.TryParse(text.Replace("°", string.Empty).Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double degrees)
            ? degrees
            : null;

    public static IEnumerable<PropertyItem> For(SchItem item)
    {
        switch (item)
        {
            case SymbolInstance symbol:
                yield return Row("reference", symbol.Reference ?? None);
                yield return Row("value", symbol.Value ?? None);
                yield return Row("library", symbol.LibId);
                if (symbol.Footprint is { Length: > 0 } footprint)
                {
                    yield return Row("footprint", footprint);
                }

                yield return Row("position", Point(symbol.Position));
                yield return Row("angle", Deg(symbol.Angle));
                yield return Row("unit", symbol.Unit.ToString(CultureInfo.InvariantCulture));
                if (symbol.Mirror is { } mirror)
                {
                    yield return Row("mirror", mirror);
                }

                yield return Row("dnp", Tr.T(symbol.IsDnp ? "sch.value.yes" : "sch.value.no"));
                if (symbol.Definition is { } definition)
                {
                    yield return Row("pins", definition.PinsOf(symbol.Unit, symbol.BodyStyle).Count().ToString(CultureInfo.InvariantCulture));
                }

                break;

            case SchWire wire:
                var points = wire.Points;
                if (points.Length > 0)
                {
                    yield return Row("position", Point(points[0]));
                    yield return Row("size", Point(points[^1]));
                    yield return Row("length", Mm((long)Total(points)));
                }

                break;

            case SchLabel label:
                yield return Row("text", label.Text);
                yield return Row("position", Point(label.Position));
                yield return Row("angle", Deg(label.Angle));
                if (label.Kind == SchLabelKind.Hierarchical || label.Kind == SchLabelKind.Global)
                {
                    yield return Row("shape", label.Shape);
                }

                break;

            case SchText text:
                yield return Row("text", text.Text);
                yield return Row("position", Point(text.Position));
                yield return Row("angle", Deg(text.Angle));
                break;

            case SchSheet sheet:
                yield return Row("name", sheet.SheetName ?? None);
                yield return Row("file", sheet.SheetFile ?? None);
                yield return Row("position", Point(sheet.Position));
                yield return Row("size", Point(sheet.Size, " × "));
                yield return Row("pins", sheet.Pins.Count.ToString(CultureInfo.InvariantCulture));
                break;

            case SchJunction junction:
                yield return Row("position", Point(junction.Position));
                yield return Row("size", Mm(junction.Diameter));
                break;

            case SchNoConnect noConnect:
                yield return Row("position", Point(noConnect.Position));
                break;

            case SchGraphic graphic:
                yield return Row("shape", graphic.Kind.ToString().ToLowerInvariant());
                yield return Row("position", Point(graphic.Kind == SchShapeKind.Circle ? graphic.Center : graphic.Start));
                break;
        }

        if (item.Uuid is { } uuid)
        {
            yield return Row("uuid", uuid);
        }
    }

    private static string None => Tr.T("sch.value.none");

    private static PropertyItem Row(string nameKey, string value) => new(Tr.T($"sch.property.{nameKey}"), value);

    private static string? Length(SchWire wire) => wire.Points is { Length: > 1 } points ? Mm((long)Total(points)) : null;

    private static double Total(Vector2L[] points)
    {
        double sum = 0;
        for (int i = 1; i < points.Length; i++)
        {
            sum += (points[i] - points[i - 1]).Length;
        }

        return sum;
    }

    private static string Shorten(string text) => text.Length <= 40 ? text : text[..39] + "…";

    private static string Mm(long nm) => $"{Units.NmToMm(nm).ToString("0.###", CultureInfo.InvariantCulture)} {Tr.T("sch.units.mm")}";

    private static string Point(Vector2L p, string separator = ", ") =>
        $"{Units.NmToMm(p.X).ToString("0.###", CultureInfo.InvariantCulture)}{separator}"
        + $"{Units.NmToMm(p.Y).ToString("0.###", CultureInfo.InvariantCulture)} {Tr.T("sch.units.mm")}";

    private static string Deg(double degrees) => $"{degrees.ToString("0.##", CultureInfo.InvariantCulture)}°";
}
