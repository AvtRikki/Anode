using System.Text;
using System.Text.RegularExpressions;
using Anode.Geometry;
using Anode.Sexpr;

namespace Anode.Kicad.Editing;

/// <summary>How the words looked for are compared with what a sheet says: KiCad's <c>EDA_SEARCH_MATCH_MODE</c>.</summary>
public enum SchFindMode
{
    /// <summary>Anywhere in the text.</summary>
    Plain,

    /// <summary>Only where it stands as a word of its own — letters, digits and underscores count as word.</summary>
    WholeWord,

    /// <summary>The whole text against a pattern with <c>*</c> for any run and <c>?</c> for any one character.</summary>
    Wildcard,

    /// <summary>A regular expression, found anywhere in the text.</summary>
    Regex,
}

/// <summary>What is looked for, and where. The defaults are KiCad's.</summary>
public sealed record SchFindOptions(string Text)
{
    public bool MatchCase { get; init; }

    public SchFindMode Mode { get; init; }

    /// <summary>Fields the sheet does not show are looked in too (KiCad's "search hidden fields").</summary>
    public bool HiddenFields { get; init; }

    /// <summary>The names and numbers of symbols' pins are looked in too (KiCad's "search pin names and numbers").</summary>
    public bool Pins { get; init; }

    /// <summary>
    /// Designators may be replaced. Off by default, as in KiCad: a find-and-replace for "R" meant for values would
    /// otherwise rename every resistor.
    /// </summary>
    public bool ReplaceReferences { get; init; }
}

/// <summary>What kind of text a find landed on, which decides whether and how it can be replaced.</summary>
public enum SchFindPlace
{
    /// <summary>A field of a symbol, a sheet or a label, other than the designator.</summary>
    Field,

    /// <summary>A symbol's designator, as it reads on this appearance of the sheet.</summary>
    Reference,

    /// <summary>A label's name.</summary>
    Label,

    /// <summary>Free text or a text box.</summary>
    Text,

    /// <summary>A pin of a symbol: its name or its number. Shown, never replaced — the pins belong to the library.</summary>
    Pin,

    /// <summary>A pin on a child sheet.</summary>
    SheetPin,
}

/// <summary>
/// One place a find landed. <see cref="Item"/> is what is selected and shown for it — the symbol a field belongs to,
/// not the field — and <see cref="Node"/> is where its text lives.
/// </summary>
public sealed record SchFindHit(SchItem Item, SchFindPlace Place, string? Name, string Text, Vector2L Position, SList Node)
{
    /// <summary>
    /// Whether a replace may write here. Pins belong to the library; a sheet's file name would need its file moved
    /// with it and a label's cross-references are worked out, not written — KiCad refuses all three, and so do we.
    /// A designator is replaced only when asked for.
    /// </summary>
    public bool CanReplace(SchFindOptions options) => Place switch
    {
        SchFindPlace.Pin => false,
        SchFindPlace.Reference => options.ReplaceReferences,
        SchFindPlace.Field => !string.Equals(Name, "Sheetfile", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(Name, "Sheet file", StringComparison.OrdinalIgnoreCase),
        _ => true,
    } && !Item.IsLocked;
}

/// <summary>
/// Finding and replacing words on a sheet: KiCad's Find and Replace (sch_find_replace_tool.cpp), with its rules for
/// what is looked in and how text is compared (<c>EDA_ITEM::Matches</c> and <c>Replace</c>).
///
/// The places found are given in KiCad's order — left to right, then top to bottom — so "next" walks the sheet as
/// the eye does. Where KiCad lists a symbol once for itself and once more for the field that matched, the field is
/// the one place given here: it says which field matched, and a symbol listed twice would be visited twice.
/// </summary>
public static class SchFind
{
    /// <summary>Every place on <paramref name="sheet"/> that matches, in KiCad's order.</summary>
    /// <param name="path">The appearance of the sheet whose designators are read; null reads the one on the symbol.</param>
    /// <param name="within">Look only in these items (KiCad's "search the current selection only").</param>
    public static IReadOnlyList<SchFindHit> All(
        Schematic sheet, SchFindOptions options, string? path = null, IReadOnlyCollection<SchItem>? within = null)
    {
        if (options.Text.Length == 0)
        {
            return [];
        }

        var matcher = new SchTextMatcher(options);
        var hits = new List<SchFindHit>();

        foreach (var item in within?.Where(i => i.IsAttached) ?? Searchable(sheet))
        {
            foreach (var hit in Candidates(item, options, path))
            {
                if (hit.Place == SchFindPlace.Reference
                    ? matcher.Matches(hit.Text) || (Suffixed(item, hit.Text, path) is { } unit && matcher.Matches(unit))
                    : matcher.Matches(hit.Text))
                {
                    hits.Add(hit);
                }
            }
        }

        return
        [
            .. hits
                .Select((hit, order) => (hit, order))
                .OrderBy(h => h.hit.Position.X)
                .ThenBy(h => h.hit.Position.Y)
                .ThenBy(h => h.hit.Item.Uuid, StringComparer.Ordinal)
                .ThenBy(h => h.order)
                .Select(h => h.hit),
        ];
    }

    /// <summary>
    /// Replaces what was found in one place. Returns false, and changes nothing, where the place may not be
    /// written or no longer says what was looked for.
    /// </summary>
    public static bool Replace(SchFindHit hit, SchFindOptions options, string with, string? path = null)
    {
        if (Planned(hit, options, with, path) is not { } write)
        {
            return false;
        }

        write();
        return true;
    }

    /// <summary>Whether <see cref="Replace"/> would change anything here, asked without changing it.</summary>
    public static bool CanReplace(SchFindHit hit, SchFindOptions options, string with, string? path = null) =>
        Planned(hit, options, with, path) is not null;

    /// <summary>The write a replace comes to, worked out from the text as it stands; null where there is none.</summary>
    private static Action? Planned(SchFindHit hit, SchFindOptions options, string with, string? path)
    {
        if (!hit.CanReplace(options) || !hit.Node.IsAttachedTo(hit.Item.Node))
        {
            return null;
        }

        var matcher = new SchTextMatcher(options);
        switch (hit.Place)
        {
            case SchFindPlace.Reference when hit.Item is SymbolInstance symbol:
            {
                string reference = symbol.ReferenceAt(path) ?? string.Empty;
                return matcher.Replace(reference, with, out string renamed)
                    ? () => SchWrites.SetReference(symbol, renamed, path)
                    : null;
            }

            case SchFindPlace.Label:
            {
                // A label's name is written escaped, as a net name is: a slash is the hierarchy's, so the file says
                // {slash}. What is looked for and what replaces it are escaped the same way, as KiCad does. Files
                // older KiCads wrote carry the slash bare — the demos have both spellings of VPP/MCLR — and KiCad's
                // replace finds such a label and then fails to change it; the bare spelling is tried as well here.
                var escaped = options with { Text = EscapeNetName(options.Text) };
                string raw = hit.Node.Str(1) ?? string.Empty;
                return new SchTextMatcher(escaped).Replace(raw, EscapeNetName(with), out string written)
                    || matcher.Replace(raw, EscapeNetName(with), out written)
                    ? () => SetAtom(hit.Node, 1, written)
                    : null;
            }

            default:
            {
                int at = hit.Place == SchFindPlace.Field ? 2 : 1;
                string raw = hit.Node.Str(at) ?? string.Empty;
                return matcher.Replace(raw, with, out string written) ? () => SetAtom(hit.Node, at, written) : null;
            }
        }
    }

    /// <summary>
    /// The items KiCad walks: everything on the sheet that can carry words. Tables are left out as KiCad leaves them —
    /// it looks in neither a table nor its cells.
    /// </summary>
    private static IEnumerable<SchItem> Searchable(Schematic sheet) =>
        sheet.Symbols.Cast<SchItem>()
            .Concat(sheet.Sheets)
            .Concat(sheet.Labels)
            .Concat(sheet.Texts);

    /// <summary>Every piece of text an item offers to be looked in, whether it matches or not.</summary>
    private static IEnumerable<SchFindHit> Candidates(SchItem item, SchFindOptions options, string? path)
    {
        switch (item)
        {
            case SymbolInstance symbol:
                foreach (var field in symbol.Fields)
                {
                    if (field.IsHidden && !options.HiddenFields)
                    {
                        continue;
                    }

                    bool isReference = string.Equals(field.Name, "Reference", StringComparison.Ordinal);
                    yield return isReference
                        ? new SchFindHit(symbol, SchFindPlace.Reference, field.Name, symbol.ReferenceAt(path) ?? field.Value, field.Position, field.Node)
                        : new SchFindHit(symbol, SchFindPlace.Field, field.Name, KicadText.Unescape(field.Value), field.Position, field.Node);
                }

                if (options.Pins && symbol.Definition is { } definition)
                {
                    var toSheet = symbol.ToSheet;
                    foreach (var pin in definition.PinsOf(symbol.UnitAt(path), symbol.BodyStyle))
                    {
                        var at = toSheet.ApplyRounded(pin.Position);
                        yield return new SchFindHit(symbol, SchFindPlace.Pin, "name", pin.Name, at, pin.Node);
                        yield return new SchFindHit(symbol, SchFindPlace.Pin, "number", pin.Number, at, pin.Node);
                    }
                }

                break;

            case SchSheet child:
                foreach (var field in child.Fields)
                {
                    if (!field.IsHidden || options.HiddenFields)
                    {
                        yield return new SchFindHit(child, SchFindPlace.Field, field.Name, KicadText.Unescape(field.Value), field.Position, field.Node);
                    }
                }

                foreach (var pin in child.Pins)
                {
                    yield return new SchFindHit(child, SchFindPlace.SheetPin, null, KicadText.Unescape(pin.Name), pin.Position, pin.Node);
                }

                break;

            case SchLabel label:
                yield return new SchFindHit(label, SchFindPlace.Label, null, label.Shown, label.Position, label.Node);

                // A label's own fields; its cross-references are worked out when drawn and say nothing of their own.
                foreach (var property in label.Node.Lists().Where(l => l.Head == "property"))
                {
                    var field = new SchField(property);
                    if (!string.Equals(field.Name, "Intersheetrefs", StringComparison.Ordinal) && (!field.IsHidden || options.HiddenFields))
                    {
                        yield return new SchFindHit(label, SchFindPlace.Field, field.Name, KicadText.Unescape(field.Value), field.Position, property);
                    }
                }

                break;

            case SchText text:
                yield return new SchFindHit(text, SchFindPlace.Text, null, text.Text, text.Position, text.Node);
                break;
        }
    }

    /// <summary>
    /// The designator with its unit's letter — "U1A" — which KiCad also matches for a part of several units, so
    /// that one gate of a package can be found by the name it is shown with.
    /// </summary>
    private static string? Suffixed(SchItem item, string reference, string? path) =>
        item is SymbolInstance { Definition.UnitCount: > 1 } symbol ? reference + UnitLetters(symbol.UnitAt(path)) : null;

    /// <summary>KiCad's <c>LetterSubReference</c>: A to Z, then AA, AB and on.</summary>
    public static string UnitLetters(int unit)
    {
        var suffix = new StringBuilder();
        do
        {
            int u = (unit - 1) % 26;
            suffix.Insert(0, (char)('A' + u));
            unit = (unit - u) / 26;
        }
        while (unit > 0);

        return suffix.ToString();
    }

    /// <summary>KiCad's <c>EscapeString</c> for net names: a slash becomes <c>{slash}</c>, line breaks are dropped.</summary>
    private static string EscapeNetName(string text)
    {
        var escaped = new StringBuilder(text.Length);
        foreach (char c in text)
        {
            if (c == '/')
            {
                escaped.Append("{slash}");
            }
            else if (c is not ('\n' or '\r'))
            {
                escaped.Append(c);
            }
        }

        return escaped.ToString();
    }

    private static void SetAtom(SList node, int index, string value) =>
        (node.AtomAt(index) ?? throw new KiCadFormatException($"({node.Head} ...) has no text to replace.")).SetString(value);

    /// <summary>The node still hangs from the item: a place found before an undo may since have been taken away.</summary>
    private static bool IsAttachedTo(this SList node, SList item)
    {
        for (var at = node; at is not null; at = at.Parent)
        {
            if (ReferenceEquals(at, item))
            {
                return true;
            }
        }

        return false;
    }
}

/// <summary>
/// KiCad's comparison of a piece of text with what is looked for (<c>EDA_ITEM::Matches</c>) and its replacement
/// (<c>EDA_ITEM::Replace</c>), rule for rule.
/// </summary>
public sealed class SchTextMatcher(SchFindOptions options)
{
    private readonly Regex? _regex = options.Mode == SchFindMode.Regex ? Compile(options) : null;

    public bool Matches(string text)
    {
        string search = options.Text;
        if (search.Length == 0)
        {
            return false;
        }

        if (options.Mode == SchFindMode.Regex)
        {
            return _regex?.IsMatch(text) ?? false;
        }

        if (!options.MatchCase)
        {
            text = text.ToUpperInvariant();
            search = search.ToUpperInvariant();
        }

        return options.Mode switch
        {
            SchFindMode.WholeWord => FindWord(text, search, 0) >= 0,
            SchFindMode.Wildcard => Wildcard(text, search),
            _ => text.Contains(search, StringComparison.Ordinal),
        };
    }

    /// <summary>
    /// Every occurrence replaced. A wildcard pattern is replaced as the words it is written with, not as a pattern —
    /// which is what KiCad does too, its replacing knowing only plain words, whole words and expressions.
    /// </summary>
    public bool Replace(string text, string with, out string result)
    {
        result = text;
        if (options.Text.Length == 0)
        {
            return false;
        }

        if (options.Mode == SchFindMode.Regex)
        {
            if (_regex is null || !_regex.IsMatch(text))
            {
                return false;
            }

            result = _regex.Replace(text, Substitution(with));
            return true;
        }

        string upper = options.MatchCase ? text : text.ToUpperInvariant();
        string search = options.MatchCase ? options.Text : options.Text.ToUpperInvariant();
        bool whole = options.Mode == SchFindMode.WholeWord;

        var built = new StringBuilder(text.Length);
        bool replaced = false;
        int i = 0;
        while (i < upper.Length)
        {
            int next = upper.IndexOf(search, i, StringComparison.Ordinal);
            if (next < 0)
            {
                built.Append(text, i, text.Length - i);
                i = text.Length;
                break;
            }

            built.Append(text, i, next - i);
            int end = next + search.Length;
            if (!whole || IsWordAt(upper, next, end))
            {
                built.Append(with);
                replaced = true;
                i = end;
            }
            else
            {
                built.Append(text[next]);
                i = next + 1;
            }
        }

        result = built.ToString();
        return replaced;
    }

    private static int FindWord(string text, string search, int from)
    {
        for (int i = from; i < text.Length;)
        {
            int next = text.IndexOf(search, i, StringComparison.Ordinal);
            if (next < 0)
            {
                return -1;
            }

            if (IsWordAt(text, next, next + search.Length))
            {
                return next;
            }

            i = next + 1;
        }

        return -1;
    }

    private static bool IsWordAt(string text, int start, int end) =>
        (start == 0 || !IsWordChar(text[start - 1])) && (end == text.Length || !IsWordChar(text[end]));

    private static bool IsWordChar(char c) => char.IsLetterOrDigit(c) || c == '_';

    /// <summary>wxWidgets' <c>wxString::Matches</c>: the whole text against <c>*</c> and <c>?</c>.</summary>
    private static bool Wildcard(string text, string pattern)
    {
        int t = 0, p = 0, star = -1, mark = 0;
        while (t < text.Length)
        {
            if (p < pattern.Length && (pattern[p] == '?' || pattern[p] == text[t]))
            {
                t++;
                p++;
            }
            else if (p < pattern.Length && pattern[p] == '*')
            {
                star = p++;
                mark = t;
            }
            else if (star >= 0)
            {
                p = star + 1;
                t = ++mark;
            }
            else
            {
                return false;
            }
        }

        while (p < pattern.Length && pattern[p] == '*')
        {
            p++;
        }

        return p == pattern.Length;
    }

    /// <summary>An expression that will not compile matches nothing, as in KiCad, rather than stopping the search.</summary>
    private static Regex? Compile(SchFindOptions options)
    {
        try
        {
            return new Regex(options.Text, (options.MatchCase ? RegexOptions.None : RegexOptions.IgnoreCase) | RegexOptions.CultureInvariant,
                TimeSpan.FromSeconds(1));
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    /// <summary>
    /// The replacement as wxRegEx reads it — <c>&amp;</c> and <c>\0</c> for the whole match, <c>\1</c> to <c>\9</c>
    /// for a group — written the way .NET wants it.
    /// </summary>
    private static string Substitution(string with)
    {
        var built = new StringBuilder(with.Length);
        for (int i = 0; i < with.Length; i++)
        {
            char c = with[i];
            if (c == '\\' && i + 1 < with.Length)
            {
                char next = with[++i];
                built.Append(char.IsAsciiDigit(next) ? "${" + next + "}" : next == '$' ? "$$" : next.ToString());
            }
            else if (c == '&')
            {
                built.Append("$0");
            }
            else if (c == '$')
            {
                built.Append("$$");
            }
            else
            {
                built.Append(c);
            }
        }

        return built.ToString();
    }
}
