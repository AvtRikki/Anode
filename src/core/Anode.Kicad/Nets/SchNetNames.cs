namespace Anode.Kicad;

/// <summary>
/// The name KiCad gives a net nobody labelled: it is made from the pin that drives it
/// (<c>SCH_PIN::GetDefaultNetName</c>).
///
/// <c>Net-(R1-Pad2)</c> when the pin has no name of its own, <c>Net-(U1-VCC)</c> when it has one, and the pad number
/// joined to the name — <c>unconnected-(U2-NC-Pad3)</c> — when the part carries that name on more than one pin, or
/// when the net is one meant to lead nowhere, which also changes the prefix. A net is that when the pin is of the
/// no-connect kind, or when somebody put a no-connect mark on it.
/// </summary>
public static class SchNetNames
{
    /// <summary>The electrical type of a pin KiCad treats as deliberately unconnected.</summary>
    private const string NoConnect = "no_connect";

    /// <param name="noConnect">Whether a no-connect mark stands on the net.</param>
    public static string FromPin(string reference, SchPin pin, LibSymbol? definition, bool noConnect = false)
    {
        bool unconnected = noConnect || pin.ElectricalType == NoConnect;
        string prefix = unconnected ? "unconnected-(" : "Net-(";
        string name = pin.Name;
        string number = pin.Number;

        if (name.Length == 0 || name == "~" || name == number)
        {
            return $"{prefix}{reference}-Pad{number})";
        }

        // Pin names need not be unique on a part; when they are not, the pad number tells the two apart.
        bool repeated = definition is not null && definition.Pins.Any(p =>
            p.Name == name && p.Number != number && (p.ElectricalType == NoConnect) == unconnected);

        return repeated || unconnected
            ? $"{prefix}{reference}-{name}-Pad{number})"
            : $"{prefix}{reference}-{name})";
    }
}
