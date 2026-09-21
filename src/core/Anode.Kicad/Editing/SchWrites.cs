using Anode.Geometry;
using Anode.Sexpr;

namespace Anode.Kicad.Editing;

/// <summary>
/// Writing a value of a sheet item back into the file — what the inspector does when a field is committed. The
/// counterpart of <see cref="SchEdits"/>, which moves items about: this changes what they say.
///
/// Every write goes through the atoms of the existing tree, so an item that is edited and then undone leaves the
/// file byte for byte as it was, and the parts we never modelled travel untouched.
/// </summary>
public static class SchWrites
{
    /// <summary>The words of a label or a piece of free text.</summary>
    public static void SetText(SchItem item, string text)
    {
        if (item is not (SchLabel or SchText))
        {
            throw new NotSupportedException($"{item.GetType().Name} carries no text.");
        }

        (item.Node.AtomAt(1) ?? throw new KiCadFormatException($"({item.Node.Head} ...) has no text.")).SetString(text);
    }

    /// <summary>A symbol's or a child sheet's field, by name: Reference, Value, Footprint, Sheetname, Sheetfile.</summary>
    public static void SetField(SchItem owner, string field, string value)
    {
        var property = owner.Node.Lists().FirstOrDefault(l =>
            l.Head == "property" && string.Equals(l.Str(1), field, StringComparison.OrdinalIgnoreCase))
            ?? throw new KiCadFormatException($"The item has no field \"{field}\".");

        (property.AtomAt(2) ?? throw new KiCadFormatException($"Field \"{field}\" has no value.")).SetString(value);
    }

    /// <summary>
    /// The designator of a placed symbol. KiCad keeps it twice — in the <c>Reference</c> property, which is what is
    /// drawn, and in the instance block, which is what the rest of the project reads — so both are written or the
    /// file contradicts itself.
    ///
    /// With a <paramref name="sheetPath"/>, only that appearance of the sheet is renamed. A sheet placed twice keeps a
    /// designator per appearance, and writing one name into every path would give both copies of the part the same
    /// designator — exactly the clash annotation exists to prevent. Without one, every path is written, which is
    /// right for a sheet that appears once.
    /// </summary>
    public static void SetReference(SymbolInstance symbol, string reference, string? sheetPath = null)
    {
        SetField(symbol, "Reference", reference);

        foreach (var path in symbol.Node.Find("instances")?.Lists().Where(l => l.Head == "project") ?? [])
        {
            foreach (var entry in path.Lists().Where(l => l.Head == "path"))
            {
                if (sheetPath is not null && !string.Equals(entry.Str(1), sheetPath, StringComparison.Ordinal))
                {
                    continue;
                }

                if (entry.Find("reference") is { } written)
                {
                    (written.AtomAt(1) ?? throw new KiCadFormatException("An instance has no reference.")).SetString(reference);
                }
            }
        }
    }

    /// <summary>
    /// Which section of a multi-unit part a placement is: gate B of a quad gate rather than gate A. Like the
    /// designator, the number is kept twice — on the symbol and in the instance block — so both are written.
    ///
    /// The symbol's pin list is deliberately left alone. KiCad writes every pin of the whole part on each placed
    /// section, which the four sections of the 74LS125 in the demo designs confirm: all fourteen pins on each.
    /// </summary>
    /// <param name="sheetPath">Only that appearance of the sheet, as for <see cref="SetReference"/>; null writes every path.</param>
    public static void SetUnit(SymbolInstance symbol, int unit, string? sheetPath = null)
    {
        if (unit < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(unit), unit, "Sections are numbered from one.");
        }

        if (symbol.Node.Find("unit") is { } own)
        {
            (own.AtomAt(1) ?? throw new KiCadFormatException("A symbol has no unit number.")).SetNumber(unit);
        }

        foreach (var path in symbol.Node.Find("instances")?.Lists().Where(l => l.Head == "project") ?? [])
        {
            foreach (var entry in path.Lists().Where(l => l.Head == "path"))
            {
                if (sheetPath is not null && !string.Equals(entry.Str(1), sheetPath, StringComparison.Ordinal))
                {
                    continue;
                }

                if (entry.Find("unit") is { } written)
                {
                    (written.AtomAt(1) ?? throw new KiCadFormatException("An instance has no unit number.")).SetNumber(unit);
                }
            }
        }
    }

    /// <summary>
    /// Which way a part is drawn: 1 for its ordinary body, 2 for KiCad's De Morgan alternative. Only the drawing
    /// changes — the part, its designator and the net each pin is on are the same, which is why this is not kept
    /// per place the way a designator is.
    ///
    /// The word is written the way the file already writes it: an older file says <c>convert</c>, a newer one
    /// <c>body_style</c>, and a file must not end up saying both.
    /// </summary>
    public static void SetBodyStyle(SymbolInstance symbol, int style)
    {
        if (style is not (1 or 2))
        {
            throw new ArgumentOutOfRangeException(nameof(style), style, "A symbol is drawn one of two ways.");
        }

        if ((symbol.Node.Find("convert") ?? symbol.Node.Find("body_style")) is { } written)
        {
            (written.AtomAt(1) ?? throw new KiCadFormatException("A symbol has no body style.")).SetNumber(style);
            return;
        }

        // Nothing said so far, so it is drawn the ordinary way; saying so again would only add noise to the file.
        if (style == 1)
        {
            return;
        }

        // KiCad writes the style straight after the section, which is where a reader expects to find it.
        var node = SchNodes.Adopt(Sexpr.SDocument.Parse($"(body_style {style})").Root);
        symbol.Node.Insert(symbol.Node.Find("unit") is { } unit ? symbol.Node.IndexOf(unit) + 1 : 1, node);
    }

    /// <summary>
    /// The page a child sheet is, where it stands. A sheet placed twice is two pages of the design and keeps a
    /// number per place, so writing one without saying which place would give both the same number — the mistake
    /// a designator makes too.
    /// </summary>
    /// <param name="sheetPath">The appearance to number; null numbers every one, which is right for a sheet placed once.</param>
    public static void SetPage(SchSheet sheet, string page, string? sheetPath = null)
    {
        if (page.Length == 0)
        {
            throw new ArgumentException("A page needs a number, even if it is not a number.", nameof(page));
        }

        bool written = false;
        foreach (var project in sheet.Node.Find("instances")?.Lists().Where(l => l.Head == "project") ?? [])
        {
            foreach (var entry in project.Lists().Where(l => l.Head == "path"))
            {
                if (sheetPath is not null && !string.Equals(entry.Str(1), sheetPath, StringComparison.Ordinal))
                {
                    continue;
                }

                if (entry.Find("page") is { } already)
                {
                    (already.AtomAt(1) ?? throw new KiCadFormatException("An instance has no page.")).SetString(page);
                }
                else
                {
                    entry.Add(SchNodes.Adopt(Sexpr.SDocument.Parse($"(page {SEscape.Quote(page)})").Root));
                }

                written = true;
            }
        }

        if (!written)
        {
            throw new InvalidOperationException("The sheet does not stand anywhere that could be numbered.");
        }
    }

    /// <summary>
    /// The order KiCad writes a placed symbol's own words in. A word that is not there yet is put where KiCad would
    /// have put it, so a file we touch still reads the way one of its own does.
    /// </summary>
    private static readonly string[] SymbolWords =
    [
        "lib_id", "at", "unit", "body_style", "exclude_from_sim", "in_bom", "on_board", "in_pos_files", "dnp",
        "locked", "fields_autoplaced", "uuid",
    ];

    /// <summary>
    /// A yes-or-no word of an item — <c>dnp</c>, <c>in_bom</c>, <c>on_board</c>, <c>exclude_from_sim</c>.
    ///
    /// Mind which way round each one reads: KiCad writes what a part <em>is</em> for the bill and the board
    /// (<c>in_bom no</c> keeps it off) and what it is <em>excluded</em> from for simulation. The caller says what
    /// the file should say, not what the designer was asked.
    /// </summary>
    public static void SetFlag(SchItem item, string word, bool value)
    {
        var fresh = SchNodes.Adopt(Sexpr.SDocument.Parse($"({word} {(value ? "yes" : "no")})").Root);

        if (item.Node.Find(word) is { } written)
        {
            int at = item.Node.IndexOf(written);
            item.Node.RemoveAt(at);
            item.Node.Insert(at, fresh);
            return;
        }

        item.Node.Insert(Place(item.Node, word), fresh);
    }

    /// <summary>Where a word belongs among the ones the item already has.</summary>
    private static int Place(SList node, string word)
    {
        int rank = Array.IndexOf(SymbolWords, word);
        if (rank < 0)
        {
            return node.Count;
        }

        for (int i = 0; i < node.Count; i++)
        {
            if (node[i] is SList child && Array.IndexOf(SymbolWords, child.Head) is > -1 and var other && other > rank)
            {
                return i;
            }
        }

        // Nothing it should come before: after the last word it should come after, or at the end.
        return node.Find("uuid") is { } uuid ? node.IndexOf(uuid) : node.Count;
    }

    /// <summary>Moves the item to a point, keeping whatever angle it has.</summary>
    public static void SetPosition(SchItem item, Vector2L at)
    {
        if (item.Node.Find("at") is not { } node)
        {
            throw new NotSupportedException($"{item.GetType().Name} has no position of its own.");
        }

        node.SetPoint(at);
    }

    /// <summary>Turns the item about its own point. Sheets do not turn, as in KiCad.</summary>
    public static void SetAngle(SchItem item, double degrees)
    {
        if (item.Node.Find("at") is not { } node)
        {
            throw new NotSupportedException($"{item.GetType().Name} has no angle.");
        }

        node.SetAngle(3, KiCadNumber.Normalize360(degrees), omitWhenZero: false);
    }
}
