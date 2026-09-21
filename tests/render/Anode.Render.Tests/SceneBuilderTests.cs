using System.Diagnostics;
using Anode.Kicad;
using Anode.Tests;

namespace Anode.Render.Tests;

public class SceneBuilderTests(ITestOutputHelper output)
{
    private const string Board4Layer = """
        (kicad_pcb
        	(version 20241229)
        	(generator "pcbnew")
        	(layers
        		(0 "F.Cu" signal)
        		(4 "In1.Cu" signal)
        		(6 "In2.Cu" signal)
        		(2 "B.Cu" signal)
        		(5 "F.SilkS" user)
        		(25 "Edge.Cuts" user)
        	)
        	(net 0 "")
        	(net 1 "GND")
        	(footprint "Lib:Part"
        		(layer "F.Cu")
        		(at 10 10)
        		(pad "1" thru_hole circle
        			(at 0 0)
        			(size 2 2)
        			(drill 1)
        			(layers "*.Cu" "*.Mask")
        			(net 1 "GND")
        		)
        		(pad "2" smd roundrect
        			(at 3 0)
        			(size 1 2)
        			(layers "F.Cu")
        			(roundrect_rratio 0.25)
        		)
        	)
        	(via
        		(at 20 10)
        		(size 0.8)
        		(drill 0.4)
        		(layers "F.Cu" "In1.Cu")
        		blind
        		(net 1)
        	)
        	(segment
        		(start 10 10)
        		(end 20 10)
        		(width 0.25)
        		(layer "F.Cu")
        		(net 1)
        	)
        	(gr_rect
        		(start 0 0)
        		(end 30 20)
        		(stroke
        			(width 0.1)
        			(type default)
        		)
        		(fill no)
        		(layer "Edge.Cuts")
        	)
        )
        """;

    [Fact]
    public void Primitives_land_on_expanded_layers()
    {
        var scene = SceneBuilder.Build(Board.Parse(Board4Layer));

        // Through-hole pad on all four copper layers; blind via only on F.Cu and In1.Cu.
        Assert.Equal(2, scene.Find("F.Cu")!.Circles.Count);
        Assert.Equal(2, scene.Find("In1.Cu")!.Circles.Count);
        Assert.Single(scene.Find("In2.Cu")!.Circles);
        Assert.Single(scene.Find("B.Cu")!.Circles);

        Assert.Single(scene.Find("F.Cu")!.Polygons);
        Assert.Single(scene.Find("F.Cu")!.Lines);
        Assert.Equal(2, scene.Find(LayerStyle.PlatedHoles)!.Circles.Count);
        Assert.Equal(4, scene.Find("Edge.Cuts")!.Lines.Count);
    }

    [Fact]
    public void Scene_is_centred_on_board_outline()
    {
        var scene = SceneBuilder.Build(Board.Parse(Board4Layer));

        Assert.Equal(-15.05, scene.BoardOutline.MinX, 4);
        Assert.Equal(15.05, scene.BoardOutline.MaxX, 4);

        var pad = scene.Find("B.Cu")!.Circles[0];
        Assert.Equal(-5, pad.Center.X, 4);
        Assert.Equal(0, pad.Center.Y, 4);
        Assert.Equal(1, pad.Radius, 4);
        Assert.IsType<Pad>(scene.Owner(pad.Owner));
    }

    [Fact]
    public void Layers_are_ordered_back_to_front()
    {
        var names = SceneBuilder.Build(Board.Parse(Board4Layer)).Layers.Select(l => l.Name).ToList();

        Assert.True(names.IndexOf("B.Cu") < names.IndexOf("In2.Cu"));
        Assert.True(names.IndexOf("In2.Cu") < names.IndexOf("In1.Cu"));
        Assert.True(names.IndexOf("In1.Cu") < names.IndexOf("F.Cu"));
        Assert.Equal(LayerStyle.PlatedHoles, names[^1]);
    }

    [Fact]
    public void Roundrect_pad_is_a_closed_outline_within_its_size()
    {
        var polygon = SceneBuilder.Build(Board.Parse(Board4Layer)).Find("F.Cu")!.Polygons.Single();
        var bounds = polygon.Bounds;

        Assert.Equal(1, bounds.Width, 3);
        Assert.Equal(2, bounds.Height, 3);
        Assert.True(polygon.Points.Length > 8);
    }

    [Fact]
    public void Triangulator_fills_every_polygon()
    {
        var scene = SceneBuilder.Build(Board.Parse(Board4Layer));
        var polygon = scene.Find("F.Cu")!.Polygons.Single();
        Assert.False(polygon.IsTriangulated);

        SceneTriangulator.Triangulate(scene, TestContext.Current.CancellationToken);

        Assert.True(polygon.IsTriangulated);
        Assert.NotEmpty(polygon.Triangles);
        Assert.Equal(0, polygon.Triangles.Length % 3);
        Assert.All(polygon.Triangles, i => Assert.InRange(i, 0, polygon.Points.Length - 1));
    }

    public static TheoryData<string> Boards() => TestData.Files(".kicad_pcb");

    [Theory]
    [MemberData(nameof(Boards))]
    public void Fixture_boards_build_scenes(string file)
    {
        Assert.SkipWhen(file.Length == 0, TestData.SkipReason);

        var sw = Stopwatch.StartNew();
        var board = Board.Load(TestData.FullPath(file));
        var loaded = sw.Elapsed;
        var scene = SceneBuilder.Build(board);

        output.WriteLine($"{file}: load {loaded.TotalMilliseconds:F0} ms, scene {(sw.Elapsed - loaded).TotalMilliseconds:F0} ms, {scene.PrimitiveCount:N0} primitives");
        Assert.True(scene.PrimitiveCount > 0);
        Assert.False(scene.Bounds.IsEmpty);
        Assert.All(scene.Layers.SelectMany(l => l.Polygons), p => Assert.True(p.Points.All(v => float.IsFinite(v.X) && float.IsFinite(v.Y))));
    }

    /// <summary>
    /// A picture on a sheet is drawn where it stands and at the size it says: KiCad centres one on its point, so a
    /// scene that hung it off a corner would put every logo in the wrong place.
    /// </summary>
    [Fact]
    public void A_pictures_place_on_the_sheet_is_the_point_it_stands_at()
    {
        string file = Path.Combine(TestData.KiCadDir, "demos", "tiny_tapeout", "rp2040.kicad_sch");
        Assert.SkipUnless(File.Exists(file), TestData.SkipReason);

        var sheet = Anode.Kicad.Schematic.Load(file);
        var image = sheet.Images.Single();
        var scene = SchematicSceneBuilder.Build(sheet);

        var drawn = Assert.Single(scene.Layers.SelectMany(l => l.Images));
        Assert.Equal(image.Data, drawn.Encoded);

        // Its middle is the point the file gives, and its size is the picture's own.
        var centre = scene.ToScene(image.Position.ToDouble());
        var size = image.Size!.Value;
        Assert.Equal(centre.X, (drawn.Bounds.MinX + drawn.Bounds.MaxX) / 2, 3);
        Assert.Equal(centre.Y, (drawn.Bounds.MinY + drawn.Bounds.MaxY) / 2, 3);
        Assert.Equal(size.Width / 1_000_000.0, drawn.Bounds.MaxX - drawn.Bounds.MinX, 3);
    }

    /// <summary>
    /// A rule area is a boundary, and a boundary comes back round: the file lists its corners once, so an area of
    /// four corners must be drawn as four sides and not three.
    /// </summary>
    [Fact]
    public void A_rule_area_is_drawn_closed()
    {
        string file = Path.Combine(TestData.KiCadDir, "demos", "royalblue54L_feather", "sch", "Debugger.kicad_sch");
        Assert.SkipUnless(File.Exists(file), TestData.SkipReason);

        var sheet = Anode.Kicad.Schematic.Load(file);
        var scene = SchematicSceneBuilder.Build(sheet);

        var drawn = scene.Layers.Single(l => l.Name == LayerStyle.Sch.RuleArea);
        Assert.NotEmpty(drawn.Lines);

        // The side that closes the shape is the one an open polyline would miss: something must be drawn along it.
        foreach (var area in sheet.RuleAreas)
        {
            var corners = area.Outline!.Points;
            var last = scene.ToScene(corners[^1].ToDouble());
            var first = scene.ToScene(corners[0].ToDouble());

            Assert.Contains(drawn.Lines, line => OnSegment(line.A, last, first) && OnSegment(line.B, last, first));
        }

        // A piece of a line lies on the segment from a to b — within a hair, since a dash is cut along it.
        static bool OnSegment(System.Numerics.Vector2 point, System.Numerics.Vector2 a, System.Numerics.Vector2 b)
        {
            var along = b - a;
            var offset = point - a;
            float length = along.Length();
            if (length <= 0)
            {
                return false;
            }

            float across = Math.Abs((along.X * offset.Y) - (along.Y * offset.X)) / length;
            float at = System.Numerics.Vector2.Dot(offset, along) / length;
            return across < 0.001f && at >= -0.001f && at <= length + 0.001f;
        }
    }
}
