using Anode.Geometry;
using Anode.Kicad;

namespace Anode.Render;

/// <summary>
/// Immutable per-frame view parameters, captured on the UI thread and read by any backend.
/// </summary>
/// <param name="WorldToScreen">Scene millimetres to device-independent pixels.</param>
/// <param name="Width">Viewport width in device-independent pixels.</param>
/// <param name="Height">Viewport height in device-independent pixels.</param>
/// <param name="SelectedOwners">Owners drawn highlighted. Backends cache by reference, so pass a new set when it changes.</param>
/// <param name="RenderScaling">Physical pixels per device-independent pixel.</param>
public sealed record ViewState(
    Transform2D WorldToScreen,
    double PixelsPerMm,
    double Width,
    double Height,
    bool FlipX,
    IReadOnlySet<int>? SelectedOwners = null,
    Net? HighlightNet = null,
    bool ShowGrid = true,
    double RenderScaling = 1)
{
    /// <summary>Primitives being moved, drawn with <see cref="PreviewTransform"/> above the board.</summary>
    public IReadOnlyList<LayerGeometry>? Preview { get; init; }

    /// <summary>Scene-space transform applied to <see cref="Preview"/>.</summary>
    public Transform2D PreviewTransform { get; init; } = Transform2D.Identity;

    /// <summary>Rubber-band selection rectangle in scene millimetres.</summary>
    public RectD? SelectionBox { get; init; }

    /// <summary>Right-to-left boxes select everything they touch; left-to-right only what they enclose.</summary>
    public bool SelectionBoxCrossing { get; init; }

    /// <summary>The rest of the board is dimmed while a net is highlighted or items are selected (but not while moving).</summary>
    public bool IsDimmed => HighlightNet is not null || (SelectedOwners is { Count: > 0 } && Preview is null);

    public RectD VisibleWorld
    {
        get
        {
            var toWorld = WorldToScreen.Inverse();
            var a = toWorld.Apply(new Vector2D(0, 0));
            var b = toWorld.Apply(new Vector2D(Width, Height));
            return new RectD(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Max(a.X, b.X), Math.Max(a.Y, b.Y));
        }
    }

    /// <summary>Grid spacing in mm so lines are at least 24 px apart.</summary>
    public double GridSpacing
    {
        get
        {
            ReadOnlySpan<double> steps = [0.05, 0.1, 0.25, 0.5, 1, 2.5, 5, 10, 25, 50, 100, 250, 500, 1000];
            foreach (double s in steps)
            {
                if (s * PixelsPerMm >= 24)
                {
                    return s;
                }
            }

            return steps[^1];
        }
    }

    public static ColorRgba SelectionBoxColor(bool crossing) =>
        crossing ? new ColorRgba(80, 210, 120, 255) : new ColorRgba(80, 150, 255, 255);
}
