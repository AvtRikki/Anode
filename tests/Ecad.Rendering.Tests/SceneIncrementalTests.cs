using Ecad.Geometry;
using Ecad.KiCad;
using Ecad.KiCad.Editing;

namespace Ecad.Rendering.Tests;

public class SceneIncrementalTests
{
    private const string Text = """
        (kicad_pcb
        	(version 20241229)
        	(generator "pcbnew")
        	(layers
        		(0 "F.Cu" signal)
        		(2 "B.Cu" signal)
        		(25 "Edge.Cuts" user)
        	)
        	(footprint "Lib:A"
        		(layer "F.Cu")
        		(at 10 20)
        		(property "Reference" "U1"
        			(at 0 -2 0)
        			(layer "F.SilkS")
        		)
        		(pad "1" thru_hole circle
        			(at 1 0)
        			(size 1 1)
        			(drill 0.5)
        			(layers "*.Cu")
        		)
        	)
        	(footprint "Lib:B"
        		(layer "F.Cu")
        		(at 30 20)
        		(pad "1" smd rect
        			(at 0 0)
        			(size 1 2)
        			(layers "F.Cu")
        		)
        	)
        	(segment
        		(start 0 0)
        		(end 5 0)
        		(width 0.25)
        		(layer "F.Cu")
        	)
        	(gr_rect
        		(start -5 -5)
        		(end 50 40)
        		(stroke
        			(width 0.1)
        			(type default)
        		)
        		(fill no)
        		(layer "Edge.Cuts")
        	)
        )
        """;

    private static Dictionary<string, int> Counts(BoardScene scene) =>
        scene.Layers.ToDictionary(l => l.Name, l => l.PrimitiveCount);

    [Fact]
    public void Removing_and_re_adding_an_item_restores_every_layer()
    {
        var scene = SceneBuilder.Build(Board.Parse(Text));
        var footprint = scene.Board.Footprints[0];
        var before = Counts(scene);
        int copperVersion = scene.Find("F.Cu")!.Version;

        var removed = scene.Remove([footprint], collect: true);

        Assert.Empty(scene.OwnersOf(footprint));
        Assert.Equal(before.Values.Sum() - scene.PrimitiveCount, removed.Sum(l => l.PrimitiveCount));
        Assert.Contains(removed, l => l.Name == "F.SilkS");
        Assert.True(scene.Find("F.Cu")!.Version > copperVersion);

        SceneBuilder.AddItems(scene, [footprint]);

        Assert.Equal(before, Counts(scene));
        Assert.NotEmpty(scene.OwnersOf(footprint));
    }

    [Fact]
    public void Owner_bounds_cover_the_item()
    {
        var scene = SceneBuilder.Build(Board.Parse(Text));
        var footprint = scene.Board.Footprints[1];
        var bounds = scene.BoundsOf(footprint);

        var center = scene.ToSceneMm(footprint.Position.ToDouble());
        Assert.Equal(1, bounds.Width, 3);
        Assert.Equal(2, bounds.Height, 3);
        Assert.True(bounds.Contains(center.X, center.Y));
    }

    [Fact]
    public void Rebuilt_primitives_follow_the_edited_model()
    {
        var scene = SceneBuilder.Build(Board.Parse(Text));
        var footprint = scene.Board.Footprints[1];
        var before = scene.BoundsOf(footprint);

        scene.Remove([footprint]);
        BoardEdits.Transform(footprint, footprint.Position, 0, new Vector2L(10_000_000, 0));
        SceneBuilder.AddItems(scene, [footprint]);

        var after = scene.BoundsOf(footprint);
        Assert.Equal(before.MinX + 10, after.MinX, 3);
        Assert.Equal(before.MinY, after.MinY, 3);
        Assert.Equal(SceneBuilder.Build(scene.Board).PrimitiveCount, scene.PrimitiveCount);
    }

    [Fact]
    public void Footprint_children_are_grouped_under_the_footprint()
    {
        var scene = SceneBuilder.Build(Board.Parse(Text));
        var footprint = scene.Board.Footprints[0];

        var owners = scene.OwnersOf(footprint);
        Assert.Equal(2, owners.Count);
        Assert.All(owners, id => Assert.Same(footprint, scene.TopLevelOf(id)));
        Assert.Contains(owners, id => scene.Owner(id) is Pad);
    }
}
