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
