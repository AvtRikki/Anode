using Ecad.Geometry;

namespace Ecad.Rendering;

/// <summary>Maps scene millimetres to screen pixels (device-independent). Y grows downwards in both.</summary>
public sealed class Camera2D
{
    public const double MinPixelsPerMm = 0.01;
    public const double MaxPixelsPerMm = 100_000;

    public Vector2D Center { get; set; }

    public double PixelsPerMm { get; set; } = 10;

    /// <summary>View from the bottom side: mirrors X.</summary>
    public bool FlipX { get; set; }

    public double ViewportWidth { get; set; } = 1;

    public double ViewportHeight { get; set; } = 1;

    private double Sx => FlipX ? -PixelsPerMm : PixelsPerMm;

    public Vector2D WorldToScreen(Vector2D world) =>
        new((world.X - Center.X) * Sx + ViewportWidth / 2, (world.Y - Center.Y) * PixelsPerMm + ViewportHeight / 2);

    public Vector2D ScreenToWorld(Vector2D screen) =>
        new((screen.X - ViewportWidth / 2) / Sx + Center.X, (screen.Y - ViewportHeight / 2) / PixelsPerMm + Center.Y);

    /// <summary>World → screen as an affine transform (for backends that take a matrix).</summary>
    public Transform2D WorldToScreenTransform =>
        new(Sx, 0, 0, PixelsPerMm, -Center.X * Sx + ViewportWidth / 2, -Center.Y * PixelsPerMm + ViewportHeight / 2);

    public RectD VisibleWorld
    {
        get
        {
            var a = ScreenToWorld(new Vector2D(0, 0));
            var b = ScreenToWorld(new Vector2D(ViewportWidth, ViewportHeight));
            return new RectD(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Max(a.X, b.X), Math.Max(a.Y, b.Y));
        }
    }

    /// <summary>Zooms keeping the world point under <paramref name="screen"/> fixed.</summary>
    public void ZoomAt(Vector2D screen, double factor)
    {
        var before = ScreenToWorld(screen);
        PixelsPerMm = Math.Clamp(PixelsPerMm * factor, MinPixelsPerMm, MaxPixelsPerMm);
        var after = ScreenToWorld(screen);
        Center += before - after;
    }

    public void PanPixels(double dx, double dy) => Center -= new Vector2D(dx / Sx, dy / PixelsPerMm);

    public void Fit(RectD world, double marginFraction = 0.05)
    {
        if (world.IsEmpty || ViewportWidth <= 0 || ViewportHeight <= 0)
        {
            return;
        }

        Center = new Vector2D((world.MinX + world.MaxX) / 2, (world.MinY + world.MaxY) / 2);
        double w = Math.Max(world.Width, 1e-3) * (1 + 2 * marginFraction);
        double h = Math.Max(world.Height, 1e-3) * (1 + 2 * marginFraction);
        PixelsPerMm = Math.Clamp(Math.Min(ViewportWidth / w, ViewportHeight / h), MinPixelsPerMm, MaxPixelsPerMm);
    }
}
