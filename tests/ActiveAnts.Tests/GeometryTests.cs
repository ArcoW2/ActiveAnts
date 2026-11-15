namespace ActiveAnts.Tests;

public sealed class GeometryTests
{
    [Theory]
    [InlineData(1, 2)]
    [InlineData(2, 2)]
    [InlineData(3, 3)]
    [InlineData(4, 3)]
    [InlineData(5, 4)]
    [InlineData(6, 4)]
    public void Padding_is_half_width_rounded_up_plus_one(float width, float expected) =>
        Assert.Equal((int)expected, WindowGeometry.Padding(width));

    [Fact]
    public void Overlay_rect_is_frame_inflated_by_padding()
    {
        var frame = new RECT(100, 200, 500, 600);
        Assert.Equal(new RECT(97, 197, 503, 603), WindowGeometry.OverlayRect(frame, WindowGeometry.Padding(4)));
    }

    private static readonly RECT Monitor = new(0, 0, 1920, 1080);

    [Fact]
    public void Full_screen_exactly_at_tolerance() =>
        Assert.True(WindowGeometry.IsFullScreen(new RECT(4, 4, 1916, 1076), Monitor));

    [Theory]
    [InlineData(5, 0, 1920, 1080)]
    [InlineData(0, 5, 1920, 1080)]
    [InlineData(0, 0, 1915, 1080)]
    [InlineData(0, 0, 1920, 1075)]
    public void Not_full_screen_one_pixel_past_tolerance(int l, int t, int r, int b) =>
        Assert.False(WindowGeometry.IsFullScreen(new RECT(l, t, r, b), Monitor));

    [Fact]
    public void Full_screen_larger_than_monitor() =>
        Assert.True(WindowGeometry.IsFullScreen(new RECT(-8, -8, 1928, 1088), Monitor));

    [Theory]
    [InlineData(0f, 40, 2f)]        // one animMs: one animStep
    [InlineData(0f, 20, 1f)]        // half an animMs: half a step
    [InlineData(0f, 800, 0f)]       // 20 steps of 2 = 40 = one period: wraps
    [InlineData(0f, 820, 1f)]
    [InlineData(39f, 40, 1f)]       // start offset carried, then wrapped
    public void Dash_offset_follows_the_clock_and_wraps_at_period(float start, double elapsedMs, float expected) =>
        Assert.Equal(expected, WindowGeometry.DashOffsetAt(start, elapsedMs, 2, 40, 8, 32), 4);

    [Theory]
    [InlineData(0f, 40, 38f)]       // negative step (clockwise) wraps below zero into the period
    [InlineData(10f, 100, 5f)]
    [InlineData(0f, 800, 0f)]
    public void Dash_offset_with_negative_step_stays_in_period(float start, double elapsedMs, float expected) =>
        Assert.Equal(expected, WindowGeometry.DashOffsetAt(start, elapsedMs, -2, 40, 8, 32), 4);

    [Fact]
    public void Dash_offset_stands_still_with_zero_step() =>
        Assert.Equal(5f, WindowGeometry.DashOffsetAt(5f, 10_000, 0, 40, 8, 32));

    [Theory]
    [InlineData(96u, 1f)]
    [InlineData(120u, 1.25f)]
    [InlineData(144u, 1.5f)]
    [InlineData(192u, 2f)]
    public void Dpi_scale(uint dpi, float expected) => Assert.Equal(expected, WindowGeometry.Scale(dpi));
}
