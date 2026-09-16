using Avalonia;
using Avalonia.Controls;

namespace Anode.Sdk;

/// <summary>
/// Resource keys of the Anode theme (docs/design/kicad-one-ui-kit.md). Chrome keys change with the theme;
/// ink keys never do. Plugins bind to these instead of hard-coding colours.
/// </summary>
public static class ThemeKeys
{
    public const string ChromeBg = "Chrome.Bg";
    public const string ChromeRaised = "Chrome.Raised";
    public const string ChromeActive = "Chrome.Active";
    public const string ChromeField = "Chrome.Field";
    public const string ChromeText = "Chrome.Text";
    public const string ChromeTextDim = "Chrome.TextDim";
    public const string ChromeTextFaint = "Chrome.TextFaint";
    public const string ChromeLine = "Chrome.Line";

    public const string Accent = "State.Accent";
    public const string AccentText = "State.AccentText";
    public const string AccentWash = "State.AccentWash";
    public const string Alert = "State.Alert";
    public const string AlertText = "State.AlertText";
    public const string AlertWash = "State.AlertWash";
    public const string Neutral500 = "State.Neutral500";

    public const string CanvasBackdrop = "Canvas.Backdrop";

    public const string InkCopperFront = "Ink.CopperFront";
    public const string InkCopperBack = "Ink.CopperBack";
    public const string InkMask = "Ink.Mask";
    public const string InkSilk = "Ink.Silk";
    public const string InkSheet = "Ink.Sheet";

    public const string FontSerif = "Font.Serif";
    public const string FontMono = "Font.Mono";

    /// <summary>Binds <paramref name="property"/> to a theme resource so it follows theme switches.</summary>
    public static T WithResource<T>(this T control, AvaloniaProperty property, string key)
        where T : Control
    {
        control.Bind(property, control.GetResourceObservable(key));
        return control;
    }
}
