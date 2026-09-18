using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Anode.Geometry;
using Anode.Kicad;

namespace Anode.Plugin.Schematic;

/// <summary>
/// A part drawn small: its body and the stubs of its pins, fitted to whatever room the row gives it. A list of names
/// reads slowly — a part is recognised by its shape long before its name is read.
///
/// The drawing is deliberately plain. No text, no fill, no pin names: at this size they would be a smear, and the
/// silhouette is the whole point. An arc is drawn through its three points, which at forty pixels is indis­tinguish­able
/// from the arc itself.
/// </summary>
internal sealed class SymbolPreview : Control
{
    public static readonly StyledProperty<LibSymbol?> SymbolProperty =
        AvaloniaProperty.Register<SymbolPreview, LibSymbol?>(nameof(Symbol));

    public static readonly StyledProperty<IBrush?> StrokeProperty =
        AvaloniaProperty.Register<SymbolPreview, IBrush?>(nameof(Stroke));

    static SymbolPreview()
    {
        AffectsRender<SymbolPreview>(SymbolProperty, StrokeProperty);
    }

    public LibSymbol? Symbol
    {
        get => GetValue(SymbolProperty);
        set => SetValue(SymbolProperty, value);
    }

    public IBrush? Stroke
    {
        get => GetValue(StrokeProperty);
        set => SetValue(StrokeProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        if (Symbol is not { } symbol || Bounds.Width <= 2 || Bounds.Height <= 2)
        {
            return;
        }

        var runs = Runs(symbol);
        if (runs.Count == 0)
        {
            return;
        }

        var extent = Extent(runs);
        if (extent.Width <= 0 && extent.Height <= 0)
        {
            return;
        }

        // Fit the part in the room given, keeping its proportions, with a little air around it.
        const double Padding = 3;
        double room = Math.Min(Bounds.Width, Bounds.Height) - (Padding * 2);
        double scale = room / Math.Max(Math.Max(extent.Width, extent.Height), 1);
        double cx = Bounds.Width / 2;
        double cy = Bounds.Height / 2;
        double mx = extent.X + (extent.Width / 2);
        double my = extent.Y + (extent.Height / 2);

        // A library frame has Y upwards; the screen has it down.
        Point Map(Vector2D p) => new(cx + ((p.X - mx) * scale), cy - ((p.Y - my) * scale));

        var pen = new Pen(Stroke ?? Brushes.Gray, 1);
        foreach (var run in runs)
        {
            for (int i = 1; i < run.Count; i++)
            {
                context.DrawLine(pen, Map(run[i - 1]), Map(run[i]));
            }

            if (run.Count == 1)
            {
                context.DrawEllipse(null, pen, Map(run[0]), 1, 1);
            }
        }
    }

    /// <summary>The part as a set of open runs of points, in library coordinates.</summary>
    private static List<List<Vector2D>> Runs(LibSymbol symbol)
    {
        var runs = new List<List<Vector2D>>();

        foreach (var graphic in symbol.GraphicsOf(1, 1))
        {
            switch (graphic.Kind)
            {
                case SchShapeKind.Rectangle:
                    var a = graphic.Start.ToDouble();
                    var b = graphic.End.ToDouble();
                    runs.Add([a, new Vector2D(b.X, a.Y), b, new Vector2D(a.X, b.Y), a]);
                    break;

                case SchShapeKind.Circle:
                    runs.Add(CircleRun(graphic.Center.ToDouble(), graphic.Radius));
                    break;

                case SchShapeKind.Arc:
                    runs.Add([graphic.Start.ToDouble(), graphic.Mid.ToDouble(), graphic.End.ToDouble()]);
                    break;

                case SchShapeKind.Polyline or SchShapeKind.Bezier:
                    if (graphic.Points is { Length: > 1 } points)
                    {
                        runs.Add([.. points.Select(p => p.ToDouble())]);
                    }

                    break;
            }
        }

        // The pins, as the stubs they are: enough to show how many there are and where they leave.
        foreach (var pin in symbol.PinsOf(1, 1))
        {
            runs.Add([pin.Position.ToDouble(), pin.EndPoint.ToDouble()]);
        }

        return runs;
    }

    private static List<Vector2D> CircleRun(Vector2D center, long radius)
    {
        const int Steps = 24;
        var run = new List<Vector2D>(Steps + 1);
        for (int i = 0; i <= Steps; i++)
        {
            double angle = 2 * Math.PI * i / Steps;
            run.Add(new Vector2D(center.X + (radius * Math.Cos(angle)), center.Y + (radius * Math.Sin(angle))));
        }

        return run;
    }

    private static Rect Extent(List<List<Vector2D>> runs)
    {
        double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
        foreach (var run in runs)
        {
            foreach (var point in run)
            {
                minX = Math.Min(minX, point.X);
                minY = Math.Min(minY, point.Y);
                maxX = Math.Max(maxX, point.X);
                maxY = Math.Max(maxY, point.Y);
            }
        }

        return new Rect(minX, minY, Math.Max(maxX - minX, 0), Math.Max(maxY - minY, 0));
    }
}
