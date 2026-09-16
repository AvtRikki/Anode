using Anode.Geometry;
using Anode.Sexpr;

namespace Anode.Kicad;

/// <summary><c>gr_text</c>, <c>fp_text</c> or a footprint <c>property</c> field.</summary>
public sealed class Text : BoardItem
{
    private readonly Footprint? _footprint;

    internal Text(SList node, Board board, Footprint? footprint)
        : base(node, board)
    {
        _footprint = footprint;
    }

    public Footprint? Footprint => _footprint;

    public override BoardItem TopLevel => (BoardItem?)_footprint ?? this;

    /// <summary>Property name ("Reference", "Value"...) or fp_text type ("reference", "user"...); null for gr_text.</summary>
    public string? FieldName => Node.Head is "property" or "fp_text" ? Node.Str(1) : null;

    public string Value => (Node.Head is "property" or "fp_text" ? Node.Str(2) : Node.Str(1)) ?? string.Empty;

    /// <summary>
    /// Value with footprint text variables expanded: <c>${REFERENCE}</c>, <c>${VALUE}</c>, <c>${LAYER}</c>
    /// and <c>${FIELD_NAME}</c> for any footprint property. Unknown variables are left as written.
    /// </summary>
    public string DisplayValue
    {
        get
        {
            string value = Value;
            if (!value.Contains("${", StringComparison.Ordinal))
            {
                return value;
            }

            var sb = new System.Text.StringBuilder(value.Length);
            int i = 0;
            while (i < value.Length)
            {
                int start = value.IndexOf("${", i, StringComparison.Ordinal);
                int end = start < 0 ? -1 : value.IndexOf('}', start + 2);
                if (start < 0 || end < 0)
                {
                    sb.Append(value, i, value.Length - i);
                    break;
                }

                sb.Append(value, i, start - i);
                string name = value[(start + 2)..end];
                sb.Append(ResolveVariable(name) ?? value[start..(end + 1)]);
                i = end + 1;
            }

            return sb.ToString();
        }
    }

    private string? ResolveVariable(string name)
    {
        if (name.Equals("LAYER", StringComparison.OrdinalIgnoreCase))
        {
            return LayerName;
        }

        if (_footprint is null)
        {
            return null;
        }

        foreach (var text in _footprint.Texts)
        {
            if (text != this && text.FieldName is { } field && text.Node.Head == "property"
                && field.Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                return text.Value;
            }
        }

        return name.ToUpperInvariant() switch
        {
            "REFERENCE" => _footprint.Reference,
            "VALUE" => _footprint.Value,
            "FOOTPRINT_NAME" => _footprint.LibId[(_footprint.LibId.IndexOf(':') + 1)..],
            "FOOTPRINT_LIBRARY" => _footprint.LibId.Contains(':') ? _footprint.LibId[.._footprint.LibId.IndexOf(':')] : string.Empty,
            _ => null,
        };
    }

    /// <summary>Position as stored (footprint-relative for footprint texts).</summary>
    public Vector2L Position => Node.Find("at") is { } at ? at.Point() : default;

    /// <summary>Angle as stored in <c>(at x y angle)</c>, degrees.</summary>
    public double StoredAngle => Node.Find("at") is { Count: > 3 } at ? at.Double(3) : 0;

    public string? LayerName => Node.ChildString("layer");

    public bool IsKnockout => Node.Find("layer")?.HasSymbol("knockout") == true;

    public bool IsHidden => Node.HasSymbol("hide") || Node.ChildBool("hide") || Effects?.ChildBool("hide") == true;

    /// <summary><c>(size height width)</c> in nm.</summary>
    public Vector2L Size => Font?.Find("size") is { Count: > 2 } s ? new Vector2L(s.Nm(2), s.Nm(1)) : new Vector2L(1_000_000, 1_000_000);

    public long? Thickness => Font?.ChildNm("thickness");

    public bool IsBold => Font?.ChildBool("bold") == true;

    public bool IsItalic => Font?.ChildBool("italic") == true;

    public bool IsMirrored => Effects?.Find("justify")?.HasSymbol("mirror") == true;

    public string HorizontalJustify =>
        Effects?.Find("justify") is { } j ? j.HasSymbol("left") ? "left" : j.HasSymbol("right") ? "right" : "center" : "center";

    public string VerticalJustify =>
        Effects?.Find("justify") is { } j ? j.HasSymbol("top") ? "top" : j.HasSymbol("bottom") ? "bottom" : "center" : "center";

    public Vector2L BoardPosition => _footprint is null ? Position : _footprint.Transform.ApplyRounded(Position);

    /// <summary>Text angle in board frame; KiCad stores it absolute even for footprint texts, like pad orientation.</summary>
    public double BoardAngle => StoredAngle;

    /// <summary>
    /// Footprint texts are kept readable (never upside down) unless marked <c>(unlocked yes)</c>;
    /// "unlocked" refers to keep-upright, not to the locked flag.
    /// </summary>
    public bool IsKeepUpright =>
        _footprint is not null && !Node.ChildBool("unlocked") && Node.Find("at")?.HasSymbol("unlocked") != true;

    /// <summary>Angle actually drawn: (-90, 90] for keep-upright texts, [0, 360) otherwise.</summary>
    public double DrawAngle
    {
        get
        {
            double angle = BoardAngle;
            if (IsKeepUpright)
            {
                while (angle > 90)
                {
                    angle -= 180;
                }

                while (angle <= -90)
                {
                    angle += 180;
                }

                return angle;
            }

            angle %= 360;
            return angle < 0 ? angle + 360 : angle;
        }
    }

    public double LineSpacing => Font?.ChildDouble("line_spacing") ?? 1;

    /// <summary>Stroke width in nm, following KiCad's effective pen width rules.</summary>
    public long PenWidth
    {
        get
        {
            var size = Size;
            long minSize = Math.Min(Math.Abs(size.X), Math.Abs(size.Y));
            long pen = Thickness ?? 0;
            if (pen <= 1)
            {
                pen = (long)Math.Round(minSize / (IsBold ? 5.0 : 8.0));
            }
            else if (IsBold && Board.Version >= KiCadFormat.BoldIsStrokeMultiplier)
            {
                pen = (long)Math.Round(pen * KiCadFormat.BoldStrokeMultiplier);
            }

            // KiCad clamps pens of small texts to a quarter of the glyph size.
            return Math.Min(pen, (long)Math.Round(minSize * 0.25));
        }
    }

    public override Transform2D ToBoard => _footprint?.Transform ?? Transform2D.Identity;

    private SList? Effects => Node.Find("effects");

    private SList? Font => Effects?.Find("font");
}
