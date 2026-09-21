namespace Anode.Render;

/// <summary>
/// Deciding what the view should do when something is to be shown — a check's finding, a part looked up in a panel.
///
/// The question is not where the thing is but how much of the reader's view to take away. Centring on it costs them
/// nothing they chose; zooming does, so it is done only when the thing would otherwise be hard to find at all. A
/// jump to a fixed zoom every time would throw away the view they had set up, which is worse than a small thing
/// being small.
/// </summary>
public static class CameraFocus
{
    /// <summary>Below this share of the view, a thing is small enough to be worth moving closer to.</summary>
    private const double TooSmall = 0.25;

    /// <summary>How much room is left round it when the view does move closer: three times its own size each way.</summary>
    private const double Around = 3;

    /// <summary>
    /// The area to fit, or null to keep the zoom as it is and only centre. An empty thing, or one that cannot be
    /// measured against an empty view, asks for nothing.
    /// </summary>
    public static RectD? AreaFor(RectD item, RectD visible)
    {
        if (item.IsEmpty || visible.IsEmpty || visible.Width <= 0 || visible.Height <= 0)
        {
            return null;
        }

        if (item.Width >= visible.Width * TooSmall || item.Height >= visible.Height * TooSmall)
        {
            return null;
        }

        return new RectD(
            item.MinX - (item.Width * Around),
            item.MinY - (item.Height * Around),
            item.MaxX + (item.Width * Around),
            item.MaxY + (item.Height * Around));
    }
}
