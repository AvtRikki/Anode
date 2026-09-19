namespace Anode.Kicad.Editing;

/// <summary>One part carrying a designator, at one place in the design.</summary>
/// <param name="Unit">The section it is at that place, which is what tells a clash from a package drawn in parts.</param>
public sealed record DesignatorUse(SheetInstance Place, SymbolInstance Symbol, string Reference, int Unit);

/// <summary>
/// Designators that name more than one part, across the whole design. A designator belongs to a place, not to a
/// file — a sheet placed twice names its parts differently in each place — so the check walks the hierarchy and
/// compares what each part is called where it stands.
/// </summary>
public static class SchDuplicates
{
    /// <summary>
    /// Every designator used twice for the same section. The sections of one package share a designator by design,
    /// even across sheets and even when KiCad has renamed a copy of the definition, so the section is the test, not
    /// the part. Power symbols are names for nets rather than parts; parts still waiting for a number, including the
    /// bare "U" of old simulation sheets, have their own way of being found.
    /// </summary>
    public static IReadOnlyList<(string Reference, IReadOnlyList<DesignatorUse> Uses)> Find(
        IReadOnlyList<SheetInstance> places,
        Func<string, Schematic?> open)
    {
        var uses = new List<DesignatorUse>();
        foreach (var place in places)
        {
            if (open(place.File) is not { } sheet)
            {
                continue;
            }

            foreach (var symbol in sheet.Symbols)
            {
                if (symbol.ReferenceAt(place.Path) is { Length: > 0 } reference && Counts(reference, symbol))
                {
                    uses.Add(new DesignatorUse(place, symbol, reference, symbol.UnitAt(place.Path)));
                }
            }
        }

        return
        [
            .. uses
                .GroupBy(u => u.Reference, StringComparer.Ordinal)
                .Where(g => g.GroupBy(u => u.Unit).Any(unit => unit.Count() > 1))
                .OrderBy(g => g.Key, StringComparer.Ordinal)
                .Select(g => (g.Key, (IReadOnlyList<DesignatorUse>)[.. g])),
        ];
    }

    private static bool Counts(string reference, SymbolInstance symbol) =>
        !reference.StartsWith('#')
        && !SchAnnotation.IsUnannotated(reference)
        && reference.Any(char.IsAsciiDigit)
        && symbol.Definition?.IsPower != true;
}
