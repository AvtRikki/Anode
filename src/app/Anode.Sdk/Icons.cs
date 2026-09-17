using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;

// Shapes.Path and System.IO.Path collide under implicit usings.
using IconPath = Avalonia.Controls.Shapes.Path;

namespace Anode.Sdk;

/// <summary>
/// The workbench icon pack. Every icon is line art on a 16×16 grid: a 1.3 px stroke with round caps, over an
/// optional fill of the same colour at low opacity — the "two tones" the kit asks for.
///
/// Icons are drawn from path data rather than shipped as files, so a plugin needs no assets, no resource dictionary
/// and no third-party licence to use them, and they take the foreground of whatever they are placed in: the same
/// icon turns dark on a light row and light on the accent fill of the rail.
///
/// A plugin that needs a shape of its own registers it once with <see cref="Register"/> and then draws it by name.
/// </summary>
public static class Icons
{
    // ——— Files and project ———
    public const string Folder = "folder";
    public const string FolderOpen = "folder-open";
    public const string File = "file";
    public const string Save = "save";
    public const string Board = "board";
    public const string Sheets = "sheets";

    // ——— Editing ———
    public const string Undo = "undo";
    public const string Redo = "redo";
    public const string Delete = "delete";
    public const string Copy = "copy";
    public const string Select = "select";
    public const string Move = "move";
    public const string Rotate = "rotate";
    public const string Mirror = "mirror";
    public const string Ruler = "ruler";

    // ——— View ———
    public const string ZoomIn = "zoom-in";
    public const string ZoomOut = "zoom-out";
    public const string ZoomFit = "zoom-fit";
    public const string Grid = "grid";
    public const string Layers = "layers";
    public const string Eye = "eye";
    public const string EyeOff = "eye-off";
    public const string Split = "split";
    public const string DockLeft = "dock-left";
    public const string DockRight = "dock-right";
    public const string DockBottom = "dock-bottom";

    // ——— The board and the sheet ———
    public const string Component = "component";
    public const string Pad = "pad";
    public const string Via = "via";
    public const string Route = "route";
    public const string Wire = "wire";
    public const string Label = "label";
    public const string NoConnect = "no-connect";
    public const string BusEntry = "bus-entry";
    public const string Bus = "bus";

    // ——— Workbench ———
    public const string Search = "search";
    public const string Palette = "palette";
    public const string Settings = "settings";
    public const string Language = "language";
    public const string Theme = "theme";
    public const string Inspector = "inspector";
    public const string Checks = "checks";
    public const string Console = "console";
    public const string Info = "info";
    public const string Close = "close";
    public const string Pin = "pin";
    public const string Plus = "plus";
    public const string Minus = "minus";
    public const string ChevronDown = "chevron-down";
    public const string ChevronRight = "chevron-right";
    public const string Branch = "branch";

    /// <param name="Fill">Shape carrying the soft tone; null for pure line art.</param>
    private sealed record Art(string? Fill, string Stroke);

    private static readonly Dictionary<string, Art> Set = new(StringComparer.Ordinal)
    {
        [Folder] = new(
            "M2 5.2 A1.4 1.4 0 0 1 3.4 3.8 H6.4 L7.9 5.8 H12.6 A1.4 1.4 0 0 1 14 7.2 V11.6 A1.4 1.4 0 0 1 12.6 13 H3.4 A1.4 1.4 0 0 1 2 11.6 Z",
            "M2 5.2 A1.4 1.4 0 0 1 3.4 3.8 H6.4 L7.9 5.8 H12.6 A1.4 1.4 0 0 1 14 7.2 V11.6 A1.4 1.4 0 0 1 12.6 13 H3.4 A1.4 1.4 0 0 1 2 11.6 Z"),
        [FolderOpen] = new(
            "M3.6 8 H14.6 L12.4 13 H2 Z",
            "M2 12.4 V5.2 A1.4 1.4 0 0 1 3.4 3.8 H6.4 L7.9 5.8 H12.2 A1.4 1.4 0 0 1 13.6 7.2 V8 M2 12.6 L3.9 8 H14.6 L12.4 13 H3.2 A1.2 1.2 0 0 1 2 12.6 Z"),
        [File] = new(
            "M4 2.2 H9.4 L12.4 5.2 V13.8 H4 Z",
            "M4 2.2 H9.4 L12.4 5.2 V13.8 H4 Z M9.4 2.2 V5.2 H12.4"),
        [Save] = new(
            "M5.4 2.6 H10.6 V6.4 H5.4 Z",
            "M2.8 2.6 H11.2 L13.2 4.6 V13.4 H2.8 Z M5.4 2.6 V6.4 H10.6 V2.6 M5 13.4 V9.4 H11 V13.4"),
        [Board] = new(
            "M2 2.6 H14 V13.4 H2 Z",
            "M2 2.6 H14 V13.4 H2 Z M4.6 5.4 H8 L10 7.4 H13.4 M2.6 10.2 H6 L7.8 12 H11.2"),
        [Sheets] = new(
            "M5.6 1.8 H12.4 V5.4 H5.6 Z",
            "M5.6 1.8 H12.4 V5.4 H5.6 Z M1.6 10.6 H6.2 V14.2 H1.6 Z M9.8 10.6 H14.4 V14.2 H9.8 Z M9 5.4 V8 H4 V10.6 M12.1 8 V10.6 M4 8 H12.1"),

        [Undo] = new(
            null,
            "M6 4.4 L2.8 7.6 L6 10.8 M2.8 7.6 H9.6 A3.6 3.6 0 1 1 9.6 14.8 H6.4"),
        [Redo] = new(
            null,
            "M10 4.4 L13.2 7.6 L10 10.8 M13.2 7.6 H6.4 A3.6 3.6 0 1 0 6.4 14.8 H9.6"),
        [Delete] = new(
            "M4.6 5.4 H11.4 L10.7 13.6 H5.3 Z",
            "M2.8 4.6 H13.2 M6.4 4.6 V2.8 H9.6 V4.6 M4.6 5.4 L5.3 13.6 H10.7 L11.4 5.4 M8 7 V12"),
        [Copy] = new(
            "M5.6 5.6 H13.2 V13.2 H5.6 Z",
            "M5.6 5.6 H13.2 V13.2 H5.6 Z M3.2 10.4 V2.8 H10.8"),
        [Select] = new(
            "M3.4 1.8 L3.4 12.8 L6.4 10 L8.3 13.9 L10.2 13 L8.3 9.2 L12.4 9.2 Z",
            "M3.4 1.8 L3.4 12.8 L6.4 10 L8.3 13.9 L10.2 13 L8.3 9.2 L12.4 9.2 Z"),
        [Move] = new(
            null,
            "M8 1.8 V14.2 M1.8 8 H14.2 M8 1.8 L6.2 3.8 M8 1.8 L9.8 3.8 M8 14.2 L6.2 12.2 M8 14.2 L9.8 12.2 M1.8 8 L3.8 6.2 M1.8 8 L3.8 9.8 M14.2 8 L12.2 6.2 M14.2 8 L12.2 9.8"),
        [Rotate] = new(
            null,
            "M13.4 8 A5.4 5.4 0 1 1 11.2 3.6 M13.4 2.2 V5.8 H9.8"),
        [Mirror] = new(
            "M9.8 4.6 L13.4 8 L9.8 11.4 Z",
            "M8 1.8 V14.2 M6.2 4.6 L2.6 8 L6.2 11.4 Z M9.8 4.6 L13.4 8 L9.8 11.4 Z"),
        [Ruler] = new(
            "M2 9.8 L9.8 2 L14 6.2 L6.2 14 Z",
            "M2 9.8 L9.8 2 L14 6.2 L6.2 14 Z M4.6 9.2 L6 10.6 M6.8 7 L8.2 8.4 M9 4.8 L10.4 6.2"),

        [ZoomIn] = new(
            "M7.2 2.4 A4.4 4.4 0 1 1 7.2 11.2 A4.4 4.4 0 1 1 7.2 2.4 Z",
            "M7.2 2.4 A4.4 4.4 0 1 1 7.2 11.2 A4.4 4.4 0 1 1 7.2 2.4 Z M5.2 6.8 H9.2 M7.2 4.8 V8.8 M10.6 10.2 L14 13.6"),
        [ZoomOut] = new(
            "M7.2 2.4 A4.4 4.4 0 1 1 7.2 11.2 A4.4 4.4 0 1 1 7.2 2.4 Z",
            "M7.2 2.4 A4.4 4.4 0 1 1 7.2 11.2 A4.4 4.4 0 1 1 7.2 2.4 Z M5.2 6.8 H9.2 M10.6 10.2 L14 13.6"),
        [ZoomFit] = new(
            null,
            "M2.4 6 V2.4 H6 M10 2.4 H13.6 V6 M13.6 10 V13.6 H10 M6 13.6 H2.4 V10 M5.6 5.6 H10.4 V10.4 H5.6 Z"),
        [Grid] = new(
            null,
            "M2.4 2.4 H13.6 V13.6 H2.4 Z M6.1 2.4 V13.6 M9.9 2.4 V13.6 M2.4 6.1 H13.6 M2.4 9.9 H13.6"),
        [Layers] = new(
            "M8 2.2 L14.2 5.6 L8 9 L1.8 5.6 Z",
            "M8 2.2 L14.2 5.6 L8 9 L1.8 5.6 Z M1.8 8.8 L8 12.2 L14.2 8.8 M1.8 11.4 L8 14.8 L14.2 11.4"),
        [Eye] = new(
            "M8 5.6 A2.4 2.4 0 1 1 8 10.4 A2.4 2.4 0 1 1 8 5.6 Z",
            "M1.4 8 C3.4 4.6 5.6 3.2 8 3.2 C10.4 3.2 12.6 4.6 14.6 8 C12.6 11.4 10.4 12.8 8 12.8 C5.6 12.8 3.4 11.4 1.4 8 Z M8 5.6 A2.4 2.4 0 1 1 8 10.4 A2.4 2.4 0 1 1 8 5.6 Z"),
        [EyeOff] = new(
            null,
            "M1.4 8 C3.4 4.6 5.6 3.2 8 3.2 C10.4 3.2 12.6 4.6 14.6 8 C12.6 11.4 10.4 12.8 8 12.8 C5.6 12.8 3.4 11.4 1.4 8 Z M8 5.6 A2.4 2.4 0 1 1 8 10.4 A2.4 2.4 0 1 1 8 5.6 Z M2.6 2.6 L13.4 13.4"),
        [Split] = new(
            "M8.6 2.6 H13.6 V13.4 H8.6 Z",
            "M2.4 2.6 H13.6 V13.4 H2.4 Z M8 2.6 V13.4"),
        [DockLeft] = new(
            "M2.4 2.6 H6.4 V13.4 H2.4 Z",
            "M2.4 2.6 H13.6 V13.4 H2.4 Z M6.4 2.6 V13.4"),
        [DockRight] = new(
            "M9.6 2.6 H13.6 V13.4 H9.6 Z",
            "M2.4 2.6 H13.6 V13.4 H2.4 Z M9.6 2.6 V13.4"),
        [DockBottom] = new(
            "M2.4 9.8 H13.6 V13.4 H2.4 Z",
            "M2.4 2.6 H13.6 V13.4 H2.4 Z M2.4 9.8 H13.6"),

        [Component] = new(
            "M5.2 5.2 H10.8 V10.8 H5.2 Z",
            "M5.2 5.2 H10.8 V10.8 H5.2 Z M5.2 6.8 H3 M5.2 9.2 H3 M10.8 6.8 H13 M10.8 9.2 H13 M6.8 5.2 V3 M9.2 5.2 V3 M6.8 10.8 V13 M9.2 10.8 V13"),
        [Pad] = new(
            "M4.4 4.4 H11.6 V11.6 H4.4 Z",
            "M4.4 4.4 H11.6 V11.6 H4.4 Z M8 6.4 A1.6 1.6 0 1 1 8 9.6 A1.6 1.6 0 1 1 8 6.4 Z"),
        [Via] = new(
            "M8 3 A5 5 0 1 1 8 13 A5 5 0 1 1 8 3 Z",
            "M8 3 A5 5 0 1 1 8 13 A5 5 0 1 1 8 3 Z M8 6.2 A1.8 1.8 0 1 1 8 9.8 A1.8 1.8 0 1 1 8 6.2 Z"),
        // A trace between two pads. Square pads read as copper at 16 px where small discs turn into beads.
        [Route] = new(
            "M1.6 10.8 H4.8 V14 H1.6 Z M11.2 2 H14.4 V5.2 H11.2 Z",
            "M3.2 12.4 H6.6 L12.8 6.2 V3.6 M1.6 10.8 H4.8 V14 H1.6 Z M11.2 2 H14.4 V5.2 H11.2 Z"),

        // A label is a name on a flag; a no-connect is the cross that says "left alone on purpose"; a bus entry is
        // the little diagonal that takes a wire off the rails.
        [Label] = new(
            "M2.2 4.6 H10.4 L13.8 8 L10.4 11.4 H2.2 Z",
            "M2.2 4.6 H10.4 L13.8 8 L10.4 11.4 H2.2 Z"),
        [NoConnect] = new(
            null,
            "M3.6 3.6 L12.4 12.4 M12.4 3.6 L3.6 12.4"),
        [BusEntry] = new(
            null,
            "M1.8 3.4 H14.2 M10.6 5.2 L6.2 9.6 M6.2 9.6 H1.8"),

        // A wire runs corner to corner between two connection dots; a bus is the pair of rails a wire taps into.
        [Wire] = new(
            "M1.6 11.4 A1.4 1.4 0 1 1 1.6 14.2 A1.4 1.4 0 1 1 1.6 11.4 Z M12.8 1.8 A1.4 1.4 0 1 1 12.8 4.6 A1.4 1.4 0 1 1 12.8 1.8 Z",
            "M3 12.8 H7.4 L10.4 3.2 H13.5 M1.6 11.4 A1.4 1.4 0 1 1 1.6 14.2 A1.4 1.4 0 1 1 1.6 11.4 Z M12.8 1.8 A1.4 1.4 0 1 1 12.8 4.6 A1.4 1.4 0 1 1 12.8 1.8 Z"),
        [Bus] = new(
            "M1.8 3.2 H14.2 V5.6 H1.8 Z",
            "M1.8 3.2 H14.2 M1.8 5.6 H14.2 M6.2 5.6 L9.4 10.4 M9.4 10.4 H14.2"),

        [Search] = new(
            "M7.2 2.4 A4.4 4.4 0 1 1 7.2 11.2 A4.4 4.4 0 1 1 7.2 2.4 Z",
            "M7.2 2.4 A4.4 4.4 0 1 1 7.2 11.2 A4.4 4.4 0 1 1 7.2 2.4 Z M10.6 10.2 L14 13.6"),
        [Palette] = new(
            "M2 3.4 H14 V6 H2 Z",
            "M2 3.4 H14 V12.6 H2 Z M2 6 H14 M4.4 8.6 H8.6 M4.4 10.6 H11"),
        [Settings] = new(
            "M8 5.4 A2.6 2.6 0 1 1 8 10.6 A2.6 2.6 0 1 1 8 5.4 Z",
            "M8 5.4 A2.6 2.6 0 1 1 8 10.6 A2.6 2.6 0 1 1 8 5.4 Z M8 1.6 V3.6 M8 12.4 V14.4 M1.6 8 H3.6 M12.4 8 H14.4 M3.5 3.5 L4.9 4.9 M11.1 11.1 L12.5 12.5 M12.5 3.5 L11.1 4.9 M4.9 11.1 L3.5 12.5"),
        [Language] = new(
            "M8 2 A6 6 0 1 1 8 14 A6 6 0 1 1 8 2 Z",
            "M8 2 A6 6 0 1 1 8 14 A6 6 0 1 1 8 2 Z M2 8 H14 M8 2 C5.4 4.6 5.4 11.4 8 14 M8 2 C10.6 4.6 10.6 11.4 8 14"),
        [Theme] = new(
            "M8 2.4 A5.6 5.6 0 0 1 8 13.6 Z",
            "M8 2.4 A5.6 5.6 0 1 1 8 13.6 A5.6 5.6 0 1 1 8 2.4 Z M8 2.4 V13.6"),
        [Inspector] = new(
            null,
            "M2.2 4.4 H13.8 M2.2 8 H13.8 M2.2 11.6 H9.2"),
        [Checks] = new(
            "M8 2 L14.6 13.6 H1.4 Z",
            "M8 2 L14.6 13.6 H1.4 Z M8 6.4 V9.6 M8 11.4 V11.9"),
        [Console] = new(
            "M1.8 3 H14.2 V13 H1.8 Z",
            "M1.8 3 H14.2 V13 H1.8 Z M4.4 6.4 L6.6 8.2 L4.4 10 M8.2 10.2 H11.8"),
        [Info] = new(
            "M8 2 A6 6 0 1 1 8 14 A6 6 0 1 1 8 2 Z",
            "M8 2 A6 6 0 1 1 8 14 A6 6 0 1 1 8 2 Z M8 7.2 V11.2 M8 4.8 V5.3"),
        [Close] = new(
            null,
            "M4.2 4.2 L11.8 11.8 M11.8 4.2 L4.2 11.8"),
        [Pin] = new(
            "M5.6 2.4 H10.4 V6.2 L12 9 H4 L5.6 6.2 Z",
            "M5.6 2.4 H10.4 V6.2 L12 9 H4 L5.6 6.2 Z M8 9 V14"),
        [Plus] = new(
            null,
            "M8 3.4 V12.6 M3.4 8 H12.6"),
        [Minus] = new(
            null,
            "M3.4 8 H12.6"),
        [ChevronDown] = new(
            null,
            "M4 6.2 L8 10.2 L12 6.2"),
        [ChevronRight] = new(
            null,
            "M6.2 4 L10.2 8 L6.2 12"),
        [Branch] = new(
            "M6.4 2.6 A1.7 1.7 0 1 1 3 2.6 A1.7 1.7 0 1 1 6.4 2.6 Z M6.4 13.4 A1.7 1.7 0 1 1 3 13.4 A1.7 1.7 0 1 1 6.4 13.4 Z M13 2.6 A1.7 1.7 0 1 1 9.6 2.6 A1.7 1.7 0 1 1 13 2.6 Z",
            "M6.4 2.6 A1.7 1.7 0 1 1 3 2.6 A1.7 1.7 0 1 1 6.4 2.6 Z M6.4 13.4 A1.7 1.7 0 1 1 3 13.4 A1.7 1.7 0 1 1 6.4 13.4 Z M13 2.6 A1.7 1.7 0 1 1 9.6 2.6 A1.7 1.7 0 1 1 13 2.6 Z M4.7 4.3 V11.7 M11.3 4.3 V6 A3.2 3.2 0 0 1 8.1 9.2 H4.7"),
    };

    /// <summary>Every icon in the pack, for galleries and pickers.</summary>
    public static IReadOnlyCollection<string> Names => Set.Keys;

    public static bool Has(string? name) => name is { Length: > 0 } && Set.ContainsKey(name);

    /// <summary>
    /// Adds an icon of your own to the pack, drawn on the same 16×16 grid as the rest. Registering a name that is
    /// already taken replaces it, so a plugin can also restyle a workbench icon for its own panels.
    /// </summary>
    /// <param name="fill">Path of the soft tone, or null for pure line art.</param>
    /// <param name="stroke">Path of the line art.</param>
    public static void Register(string name, string? fill, string stroke) => Set[name] = new Art(fill, stroke);

    /// <summary>The icon as a control, or null when the name is unknown, so callers can fall back to a text label.</summary>
    public static Control? Draw(string? name, double size = 16, double strokeThickness = 1.3)
    {
        if (name is null || !Set.TryGetValue(name, out var art))
        {
            return null;
        }

        var canvas = new Canvas { Width = 16, Height = 16 };
        if (art.Fill is { } fill)
        {
            canvas.Children.Add(Layer(fill, filled: true, strokeThickness));
        }

        canvas.Children.Add(Layer(art.Stroke, filled: false, strokeThickness));

        return new Viewbox
        {
            Width = size,
            Height = size,
            Child = canvas,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
    }

    private static IconPath Layer(string data, bool filled, double strokeThickness)
    {
        var path = new IconPath
        {
            Data = Geometry.Parse(data),
            StrokeThickness = filled ? 0 : strokeThickness,
            StrokeLineCap = PenLineCap.Round,
            StrokeJoin = PenLineJoin.Round,
            Opacity = filled ? 0.2 : 1,
        };

        // Icons inherit their colour: a rail button that fills with accent turns its icon with it.
        var foreground = path.GetObservable(TextElement.ForegroundProperty);
        path.Bind(filled ? IconPath.FillProperty : IconPath.StrokeProperty, foreground);
        return path;
    }
}
