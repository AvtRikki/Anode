using System.Diagnostics;
using Ecad.KiCad;
using Ecad.Tests;

namespace Ecad.Rendering.Tests;

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
}
