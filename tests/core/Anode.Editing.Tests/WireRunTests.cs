using Anode.Geometry;

namespace Anode.Editing.Tests;

/// <summary>
/// Drawing a run of wires. The rules that matter are the ones a hand on a mouse gets wrong: a click writes the legs
/// up to it and carries on from there, and Esc leaves nothing behind — no run, and nothing that comes back the next
/// time the cursor moves.
/// </summary>
public class WireRunTests
{
    private static readonly Vector2L Origin = new(50_800_000, 50_800_000);
    private static readonly Vector2L Right = new(63_500_000, 50_800_000);
    private static readonly Vector2L Corner = new(63_500_000, 38_100_000);

    [Fact]
    public void A_run_travels_the_longer_axis_first()
    {
        // Wider than tall: along X, then down.
        Assert.Equal(
            [(Origin, new Vector2L(76_200_000, 50_800_000)), (new Vector2L(76_200_000, 50_800_000), new Vector2L(76_200_000, 44_450_000))],
            WireRun.Legs(Origin, new Vector2L(76_200_000, 44_450_000)));

        // Taller than wide: down, then along X.
        Assert.Equal(
            [(Origin, new Vector2L(50_800_000, 25_400_000)), (new Vector2L(50_800_000, 25_400_000), new Vector2L(57_150_000, 25_400_000))],
            WireRun.Legs(Origin, new Vector2L(57_150_000, 25_400_000)));

        // On one line it is a single leg, and a point on itself is no leg at all.
        Assert.Equal([(Origin, Right)], WireRun.Legs(Origin, Right));
        Assert.Empty(WireRun.Legs(Origin, Origin));
    }

    [Fact]
    public void The_first_click_starts_the_run_and_writes_nothing()
    {
        var run = new WireRun();

        Assert.Empty(run.Click(Origin));
        Assert.True(run.IsRunning);
        Assert.Equal(Origin, run.Start);
    }

    [Fact]
    public void Each_further_click_writes_its_legs_and_carries_on()
    {
        var run = new WireRun();
        run.Click(Origin);

        var legs = run.Click(Corner);

        Assert.Equal(2, legs.Count);
        Assert.Equal(Origin, legs[0].From);
        Assert.Equal(Corner, legs[^1].To);

        // The run continues from where it was clicked, so the next leg starts there.
        Assert.True(run.IsRunning);
        Assert.Equal(Corner, run.Start);
    }

    [Fact]
    public void Clicking_where_the_run_stands_ends_it()
    {
        var run = new WireRun();
        run.Click(Origin);

        Assert.Empty(run.Click(Origin));
        Assert.False(run.IsRunning);
    }

    [Fact]
    public void Esc_leaves_nothing_behind()
    {
        var run = new WireRun();
        run.Click(Origin);
        run.Click(Right);

        Assert.True(run.Cancel());
        Assert.False(run.IsRunning);

        // Nothing follows the cursor afterwards — this is what came back to life when the key never arrived.
        Assert.Empty(run.Preview(Corner));

        // And a second Esc has nothing to end, which is how the tool knows to give the pointer back.
        Assert.False(run.Cancel());
    }

    [Fact]
    public void The_preview_is_what_the_next_click_would_write()
    {
        var run = new WireRun();
        run.Click(Origin);

        Assert.Equal(WireRun.Legs(Origin, Corner), run.Preview(Corner));

        // With no run there is nothing to preview.
        run.Finish();
        Assert.Empty(run.Preview(Corner));
    }
}
