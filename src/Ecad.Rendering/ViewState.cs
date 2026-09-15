using Ecad.Geometry;
using Ecad.KiCad;

namespace Ecad.Rendering;

/// <summary>
/// Immutable per-frame view parameters, captured on the UI thread and read by any backend.
/// </summary>
/// <param name="WorldToScreen">Scene millimetres to device-independent pixels.</param>
/// <param name="Width">Viewport width in device-independent pixels.</param>
/// <param name="Height">Viewport height in device-independent pixels.</param>
/// <param name="RenderScaling">Physical pixels per device-independent pixel.</param>
public sealed record ViewState(
    Transform2D WorldToScreen,
    double PixelsPerMm,
    double Width,
    double Height,
    bool FlipX,
    int SelectedOwner = -1,
    Net? HighlightNet = null,
    bool ShowGrid = true,
    double RenderScaling = 1)
{
    public bool IsDimmed => HighlightNet is not null || SelectedOwner >= 0;

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
}
