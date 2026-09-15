using Ecad.Tests;

namespace Ecad.KiCad.Tests;

public class FixtureBoardTests
{
    public static TheoryData<string> Boards() => TestData.Files(".kicad_pcb");

    [Theory]
    [MemberData(nameof(Boards))]
    public void Every_item_is_readable_and_file_stays_identical(string file)
    {
        Assert.SkipWhen(file.Length == 0, TestData.SkipReason);

        byte[] original = File.ReadAllBytes(TestData.FullPath(file));
        var board = Board.FromDocument(Ecad.Sexpr.SDocument.FromBytes(original));

        Assert.True(board.Version > 0);
        Assert.Equal(board.Root.FindAll("footprint").Count(), board.Footprints.Count);
        Assert.Equal(board.Root.FindAll("segment").Count(), board.Segments.Count);
        Assert.Equal(board.Root.FindAll("via").Count(), board.Vias.Count);
        Assert.Equal(board.Root.FindAll("zone").Count(), board.Zones.Count);

        // Touch every typed property to make sure nothing throws on real data.
        foreach (var fp in board.Footprints)
        {
            _ = (fp.LibId, fp.Reference, fp.Value, fp.Bounds);
            foreach (var pad in fp.Pads)
            {
                _ = (pad.Number, pad.Type, pad.Shape, pad.Size, pad.Drill, pad.Net, pad.LayerNames, pad.Chamfers, pad.Bounds);
                if (pad.Node.Find("net") is not null)
                {
                    Assert.NotNull(pad.Net);
                }
            }

            foreach (var t in fp.Texts)
            {
                _ = (t.Value, t.BoardPosition, t.BoardAngle, t.Size, t.IsHidden);
            }

            foreach (var zone in fp.Zones)
            {
                _ = zone.Outlines.Count();
            }
        }

        foreach (var s in board.Segments)
        {
            Assert.NotEmpty(s.LayerNames);
            _ = s.Net;
        }

        foreach (var a in board.Arcs)
        {
            Assert.NotNull(a.Geometry);
        }

        foreach (var v in board.Vias)
        {
            Assert.Equal(2, v.LayerNames.Count);
            _ = (v.ViaType, v.Net);
        }

        foreach (var z in board.Zones)
        {
            _ = (z.Net, z.Outlines.Count(), z.FilledPolygons.Count());
        }

        foreach (var shape in board.Shapes)
        {
            _ = shape.Bounds;
        }

        Assert.False(board.ComputeBounds().IsEmpty);
        Assert.True(original.AsSpan().SequenceEqual(board.Document.ToBytes()));
    }
}
