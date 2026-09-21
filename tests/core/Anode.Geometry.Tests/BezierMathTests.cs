namespace Anode.Geometry.Tests;

/// <summary>
/// Reading a Bézier curve. Only its first and last points are on it; the two in between pull it. Drawing all four
/// joined up gives the frame the curve hangs in, which is a different shape and visibly so.
/// </summary>
public class BezierMathTests
{
    private static readonly Vector2D From = new(0, 0);
    private static readonly Vector2D PullFrom = new(0, 100);
    private static readonly Vector2D PullTo = new(100, 100);
    private static readonly Vector2D To = new(100, 0);

    [Fact]
    public void The_curve_begins_and_ends_where_it_is_told_to()
    {
        var points = BezierMath.Tessellate([From, PullFrom, PullTo, To]);

        Assert.Equal(From.X, points[0].X, 6);
        Assert.Equal(From.Y, points[0].Y, 6);
        Assert.Equal(To.X, points[^1].X, 6);
        Assert.Equal(To.Y, points[^1].Y, 6);
    }

    [Fact]
    public void The_points_that_pull_it_are_not_on_it()
    {
        var points = BezierMath.Tessellate([From, PullFrom, PullTo, To]);

        // The curve is pulled up towards a hundred but reaches only three quarters of the way — which is what makes
        // it a curve rather than the three-sided frame those four points would draw.
        Assert.DoesNotContain(points, p => Math.Abs(p.Y - 100) < 1);
        Assert.Equal(75, points.Max(p => p.Y), 3);
    }

    [Fact]
    public void Half_way_along_is_half_way_across()
    {
        var middle = BezierMath.PointAt(From, PullFrom, PullTo, To, 0.5);

        Assert.Equal(50, middle.X, 6);
        Assert.Equal(75, middle.Y, 6);
    }

    [Fact]
    public void A_straight_run_of_control_points_stays_straight()
    {
        var points = BezierMath.Tessellate([new(0, 0), new(10, 0), new(20, 0), new(30, 0)]);

        Assert.All(points, p => Assert.Equal(0, p.Y, 9));
        Assert.Equal(30, points[^1].X, 6);
    }

    [Fact]
    public void Curves_joined_end_to_end_are_read_as_one_run()
    {
        // Seven points: two curves sharing the middle one.
        Vector2D[] two = [new(0, 0), new(0, 10), new(10, 10), new(10, 0), new(10, -10), new(20, -10), new(20, 0)];

        var points = BezierMath.Tessellate(two);

        Assert.Equal(0, points[0].X, 6);
        Assert.Equal(20, points[^1].X, 6);
        Assert.Contains(points, p => Math.Abs(p.X - 10) < 0.001 && Math.Abs(p.Y) < 0.001);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(3)]
    public void Fewer_than_four_points_is_not_a_curve(int count)
    {
        var given = Enumerable.Range(0, count).Select(i => new Vector2D(i, i)).ToArray();

        Assert.Equal(given, BezierMath.Tessellate(given));
    }
}
