using System.Globalization;
using System.Text;
using Anode.Sexpr;

namespace Anode.Kicad;

/// <summary>
/// How a sheet's copy of a symbol has drifted from the library's — KiCad's <c>lib_symbol_mismatch</c>, told in what a
/// designer would look for: pins added, gone or changed, shapes that are not the same, fields that say something
/// else, a different number of units. Null from <see cref="Between"/> means nothing a sheet shows differs.
///
/// What is compared is what the symbol means, not how its file happens to be written, as KiCad's own comparison
/// does (<c>LIB_SYMBOL::Compare</c> with <c>ERC</c>): pins by number within their unit, shapes as a set, fields by
/// name and only for their text — a field dragged elsewhere in the library is not the part changing. The reference
/// and the value are not compared at all: every placement carries its own, and KiCad 7 wrote a placement's "U2" and
/// "~" into the sheet's copy, which made every such sheet differ from its library for nothing. A sheet written
/// by an older KiCad spells some words otherwise — a bare <c>hide</c> for <c>(hide yes)</c>, <c>1.270</c> for
/// <c>1.27</c>, a colour of all zeros for none — and those are read as the same.
/// </summary>
/// <param name="PinsAdded">Pins the library has and the sheet's copy lacks.</param>
/// <param name="PinsRemoved">Pins the sheet's copy has that the library no longer does.</param>
/// <param name="PinsChanged">Pins both have, written differently: moved, renamed, retyped.</param>
/// <param name="ShapesChanged">Lines, outlines and words on the body that are not in both.</param>
/// <param name="Fields">The names of fields whose text differs, or that only one of them has.</param>
public sealed record SymbolDrift(
    int PinsAdded,
    int PinsRemoved,
    int PinsChanged,
    int ShapesChanged,
    IReadOnlyList<string> Fields,
    bool UnitsChanged,
    bool PowerChanged)
{
    /// <summary>What differs between the copy on a sheet and the definition in a library; null when nothing does.</summary>
    /// <param name="library">The library's definition, flattened as a sheet would carry it.</param>
    public static SymbolDrift? Between(LibSymbol placed, LibSymbol library)
    {
        var (placedPins, placedShapes) = Contents(placed.Node);
        var (libraryPins, libraryShapes) = Contents(library.Node);

        int added = libraryPins.Keys.Count(k => !placedPins.ContainsKey(k));
        int removed = placedPins.Keys.Count(k => !libraryPins.ContainsKey(k));
        int changed = placedPins.Count(p => libraryPins.TryGetValue(p.Key, out var other) && !Same(p.Value, other));
        int shapes = Math.Max(Surplus(placedShapes, libraryShapes), Surplus(libraryShapes, placedShapes));

        var placedFields = FieldsOf(placed.Node);
        var libraryFields = FieldsOf(library.Node);
        List<string> fields =
        [
            .. placedFields.Keys.Union(libraryFields.Keys)
                .Where(name => !string.Equals(placedFields.GetValueOrDefault(name), libraryFields.GetValueOrDefault(name), StringComparison.Ordinal)),
        ];

        bool units = placed.UnitCount != library.UnitCount;
        bool power = placed.IsPower != library.IsPower;

        return added == 0 && removed == 0 && changed == 0 && shapes == 0 && fields.Count == 0 && !units && !power
            ? null
            : new SymbolDrift(added, removed, changed, shapes, fields, units, power);
    }

    /// <summary>
    /// The pins of a symbol by unit, body style and number — a number may be used twice, for pins joined inside the
    /// part — and everything else drawn on its bodies, each read into a form where only meaning is left.
    /// </summary>
    private static (Dictionary<string, List<string>> Pins, List<string> Shapes) Contents(SList symbol)
    {
        var pins = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var shapes = new List<string>();
        foreach (var body in symbol.Lists().Where(l => l.Head == "symbol"))
        {
            string unit = UnitOf(body.Str(1));
            foreach (var item in body.Lists())
            {
                string text = Canonical(item);
                if (item.Head == "pin")
                {
                    string key = unit + "/" + (item.Find("number")?.Str(1) ?? string.Empty);
                    if (!pins.TryGetValue(key, out var list))
                    {
                        pins[key] = list = [];
                    }

                    list.Add(text);
                }
                else
                {
                    shapes.Add(unit + "/" + text);
                }
            }
        }

        return (pins, shapes);
    }

    /// <summary>"<c>_1_2</c>" of "<c>R_1_2</c>": the unit and body style, which are what a sub-symbol's name says.</summary>
    private static string UnitOf(string? name)
    {
        if (name is null)
        {
            return string.Empty;
        }

        int last = name.LastIndexOf('_');
        int before = last > 0 ? name.LastIndexOf('_', last - 1) : -1;
        return before >= 0 ? name[before..] : name;
    }

    private static Dictionary<string, string> FieldsOf(SList symbol)
    {
        var fields = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var field in symbol.Lists().Where(l => l.Head == "property" && l.Str(1) is not ("Reference" or "Value")))
        {
            fields.TryAdd(field.Str(1) ?? string.Empty, field.Str(2) ?? string.Empty);
        }

        return fields;
    }

    private static bool Same(List<string> a, List<string> b) =>
        a.Count == b.Count && Surplus(a, b) == 0;

    /// <summary>How many of <paramref name="a"/> have no partner in <paramref name="b"/>, each partner used once.</summary>
    private static int Surplus(List<string> a, List<string> b)
    {
        var left = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (string item in b)
        {
            left[item] = left.GetValueOrDefault(item) + 1;
        }

        int surplus = 0;
        foreach (string item in a)
        {
            if (left.GetValueOrDefault(item) is > 0 and var count)
            {
                left[item] = count - 1;
            }
            else
            {
                surplus++;
            }
        }

        return surplus;
    }

    /// <summary>
    /// A node written so that two spellings of one meaning come out the same: numbers in one form, a bare
    /// <c>hide</c> as <c>(hide yes)</c>, a <c>(hide no)</c> or an all-zero colour — both of which say "nothing" —
    /// left out, and the lists inside in one order, since KiCad reads them by name wherever they stand.
    /// </summary>
    internal static string Canonical(SNode node)
    {
        if (node is SAtom atom)
        {
            return Word(atom);
        }

        var list = (SList)node;
        var text = new StringBuilder("(");
        var inner = new List<string>();
        for (int i = 0; i < list.Count; i++)
        {
            switch (list[i])
            {
                case SAtom { Kind: SAtomKind.Symbol, Raw: "hide" } when i > 0:
                    inner.Add("(hide yes)");
                    break;
                case SAtom word:
                    if (i > 0)
                    {
                        text.Append(' ');
                    }

                    text.Append(Word(word));
                    break;
                case SList child when !SaysNothing(child):
                    inner.Add(Canonical(child));
                    break;
            }
        }

        inner.Sort(StringComparer.Ordinal);
        foreach (string child in inner)
        {
            text.Append(' ').Append(child);
        }

        return text.Append(')').ToString();
    }

    private static bool SaysNothing(SList list) => list.Head switch
    {
        "hide" => list.Str(1) is "no",
        "color" => Enumerable.Range(1, list.Count - 1).All(i => list.AtomAt(i) is { } a && a.TryGetDouble(out double v) && v == 0),
        _ => false,
    };

    private static string Word(SAtom atom)
    {
        if (atom.Kind == SAtomKind.String)
        {
            return atom.Raw;
        }

        string raw = atom.Raw;
        bool numeric = raw.Length > 0 && (char.IsAsciiDigit(raw[0]) || raw[0] is '-' or '+' or '.');
        return numeric && decimal.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out decimal value)
            ? (value == 0 ? 0m : value / 1.000000000000000000000000000000000m).ToString(CultureInfo.InvariantCulture)
            : raw;
    }
}
