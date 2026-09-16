namespace Anode.Geometry.Tests;

public class GeometryTests
{
    [Fact]
    public void Rotation_by_right_angles_is_exact()
    {
        var p = new Vector2L(1_000_000, 0);
        Assert.Equal(new Vector2L(0, 1_000_000), Transform2D.Rotation(90).ApplyRounded(p));
        Assert.Equal(new Vector2L(-1_000_000, 0), Transform2D.Rotation(-180).ApplyRounded(p));
        Assert.Equal(0, Transform2D.Rotation(270).Apply(p).X);
    }

    [Fact]
    public void Then_applies_left_to_right_and_inverse_undoes()
    {
        var t = Transform2D.Rotation(30).Then(Transform2D.Translation(5, -7)).Then(Transform2D.Scale(-1, 2));
        var p = new Vector2D(3, 4);

        var expected = Transform2D.Scale(-1, 2).Apply(Transform2D.Translation(5, -7).Apply(Transform2D.Rotation(30).Apply(p)));
        var actual = t.Apply(p);
        Assert.Equal(expected.X, actual.X, 9);
        Assert.Equal(expected.Y, actual.Y, 9);

        var back = t.Inverse().Apply(actual);
        Assert.Equal(p.X, back.X, 9);
        Assert.Equal(p.Y, back.Y, 9);
        Assert.True(t.IsMirrored);
    }

    [Fact]
    public void Arc_through_three_points()
    {
        // Quarter circle of radius 10 around the origin, going from +X to +Y.
        var arc = ArcMath.FromStartMidEnd(new(10, 0), new(Math.Sqrt(50), Math.Sqrt(50)), new(0, 10))!.Value;
        Assert.Equal(0, arc.Center.X, 9);
        Assert.Equal(0, arc.Center.Y, 9);
        Assert.Equal(10, arc.Radius, 9);
        Assert.Equal(Math.PI / 2, arc.Sweep, 9);

        // Same end points, opposite direction through the long way round.
        var reverse = ArcMath.FromStartMidEnd(new(10, 0), new(-10, 0), new(0, 10))!.Value;
        Assert.Equal(-3 * Math.PI / 2, reverse.Sweep, 9);
    }

    [Fact]
    public void Collinear_points_make_no_arc()
    {
        Assert.Null(ArcMath.FromStartMidEnd(new(0, 0), new(1, 1), new(2, 2)));
    }

    [Fact]
    public void Tessellation_respects_chord_error()
    {
        var arc = new Arc(Vector2D.Zero, 1_000_000, 0, Math.PI);
        var points = ArcMath.Tessellate(arc, 5_000);

        Assert.Equal(arc.Start.X, points[0].X, 6);
        Assert.Equal(arc.End.X, points[^1].X, 6);
        for (int i = 1; i < points.Length; i++)
        {
            var mid = (points[i - 1] + points[i]) / 2;
            Assert.True(arc.Radius - mid.Length <= 5_000 + 1e-6);
        }
    }

    [Fact]
    public void Box_union_and_intersection()
    {
        var box = Box2L.Empty.Union(new Vector2L(5, 5)).Union(new Vector2L(-1, 3));
        Assert.Equal(new Box2L(-1, 3, 5, 5), box);
        Assert.True(box.Intersects(new Box2L(5, 5, 9, 9)));
        Assert.False(box.Intersects(Box2L.Empty));
        Assert.Equal(box, box.Union(Box2L.Empty));
    }

    [Fact]
    public void Mm_conversion_rounds_to_nanometres()
    {
        Assert.Equal(123_456_789, Units.MmToNm(123.456789));
        Assert.Equal(-1_500_000, Units.MmToNm(-1.5));
    }
}
