using Ecad.Geometry;
using Ecad.Sexpr;

namespace Ecad.KiCad;

public sealed class Footprint : BoardItem
{
    private readonly List<Pad> _pads = [];
    private readonly List<Shape> _shapes = [];
    private readonly List<Text> _texts = [];
    private readonly List<Zone> _zones = [];

    internal Footprint(SList node, Board board)
        : base(node, board)
    {
        ReadPlacement();

        foreach (var child in node.Lists())
        {
            switch (child.Head)
            {
                case "pad":
                    _pads.Add(new Pad(child, board, this));
                    break;
                case "property" or "fp_text":
                    _texts.Add(new Text(child, board, this));
                    break;
                case "zone":
                    _zones.Add(new Zone(child, board, this));
                    break;
                case var head when Shape.IsShapeHead(head):
                    _shapes.Add(new Shape(child, board, Transform));
                    break;
            }
        }
    }

    /// <summary>Library identifier, e.g. <c>Resistor_SMD:R_0603_1608Metric</c>.</summary>
    public string LibId => Node.Str(1) ?? string.Empty;

    public string LayerName => Node.ChildString("layer") ?? "F.Cu";

    public bool IsOnBack => LayerName == "B.Cu";

    public Vector2L Position { get; private set; }

    /// <summary>Degrees, counter-clockwise as seen on screen.</summary>
    public double Orientation { get; private set; }

    public double ScaleX { get; private set; } = 1;

    public double ScaleY { get; private set; } = 1;

    /// <summary>Footprint (library) frame to board frame: scale, rotate, translate.</summary>
    public Transform2D Transform { get; private set; } = Transform2D.Identity;

    public override Transform2D ToBoard => Transform;

    public string? Reference => FieldValue("Reference", "reference");

    public string? Value => FieldValue("Value", "value");

    public IReadOnlyList<Pad> Pads => _pads;

    public IReadOnlyList<Shape> Shapes => _shapes;

    public IReadOnlyList<Text> Texts => _texts;

    public IReadOnlyList<Zone> Zones => _zones;

    public Box2L Bounds
    {
        get
        {
            var box = Box2L.Empty;
            foreach (var shape in _shapes)
            {
                box = box.Union(shape.Bounds);
            }

            foreach (var pad in _pads)
            {
                box = box.Union(pad.Bounds);
            }

            return box.IsEmpty ? Box2L.FromCircle(Position, 0) : box;
        }
    }

    private void ReadPlacement()
    {
        if (Node.Find("transform") is { } transform)
        {
            Position = transform.ChildPoint("translate") ?? default;
            Orientation = transform.ChildDouble("rotate") ?? 0;
            if (transform.Find("scale") is { Count: > 2 } scale)
            {
                ScaleX = SanitizeScale(scale.Double(1, 1));
                ScaleY = SanitizeScale(scale.Double(2, 1));
            }
        }
        else if (Node.Find("at") is { } at)
        {
            Position = at.Point();
            Orientation = at.Count > 3 ? at.Double(3) : 0;
        }

        Transform = KiCadTransforms.Placement(Position, Orientation, ScaleX, ScaleY);
    }

    private string? FieldValue(string propertyName, string fpTextType)
    {
        foreach (var text in _texts)
        {
            if ((text.Node.Head == "property" && text.FieldName == propertyName)
                || (text.Node.Head == "fp_text" && text.FieldName == fpTextType))
            {
                return text.Value;
            }
        }

        return null;
    }

    private static double SanitizeScale(double s) => double.IsFinite(s) && s > 0 ? s : 1;
}

public static class KiCadTransforms
{
    /// <summary>
    /// KiCad angles are counter-clockwise on screen, and screen Y grows downwards,
    /// which is a negative rotation in <see cref="Transform2D.Rotation"/> terms.
    /// </summary>
    public static Transform2D Rotation(double kicadDegrees) => Transform2D.Rotation(-kicadDegrees);

    public static Transform2D Placement(Vector2L position, double kicadDegrees, double scaleX = 1, double scaleY = 1)
    {
        var t = scaleX == 1 && scaleY == 1 ? Transform2D.Identity : Transform2D.Scale(scaleX, scaleY);
        return t.Then(Rotation(kicadDegrees)).Then(Transform2D.Translation(position));
    }
}
