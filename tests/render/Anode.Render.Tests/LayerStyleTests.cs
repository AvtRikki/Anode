using Anode.Render;

namespace Anode.Render.Tests;

/// <summary>
/// What is drawing and what is the ground it sits on. The ground — a board's body, a sheet's paper — is drawn but
/// never picked, selected or dimmed: dimming it makes the whole view blink every time something gets selected.
/// </summary>
public class LayerStyleTests
{
    [Fact]
    public void The_ground_under_a_drawing_is_decoration()
    {
        Assert.True(new LayerGeometry(LayerStyle.BoardBody).IsDecoration);
        Assert.True(new LayerGeometry(LayerStyle.Sch.Sheet).IsDecoration);
    }

    [Fact]
    public void What_is_drawn_on_it_is_not()
    {
        foreach (string layer in (string[])[LayerStyle.Sch.Wire, LayerStyle.Sch.Bus, LayerStyle.Sch.Symbol, LayerStyle.Sch.Label, "F.Cu"])
        {
            Assert.False(new LayerGeometry(layer).IsDecoration, layer);
        }
    }

    [Fact]
    public void The_ground_is_drawn_first()
    {
        // Both grounds share one draw order, far below everything else, so nothing hides under them.
        Assert.Equal(LayerStyle.DrawOrder(LayerStyle.BoardBody), LayerStyle.DrawOrder(LayerStyle.Sch.Sheet));
        Assert.True(LayerStyle.DrawOrder(LayerStyle.Sch.Sheet) < LayerStyle.DrawOrder(LayerStyle.Sch.Wire));
    }
}
