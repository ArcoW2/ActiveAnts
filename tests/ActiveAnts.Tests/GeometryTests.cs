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
    [InlineData(0, 2, 8, 32, 2)]
    [InlineData(38, 2, 8, 32, 0)]
    [InlineData(39, 2, 8, 32, 1)]
    [InlineData(5, 0, 8, 32, 5)]
    public void Dash_offset_wraps_at_period(int offset, int step, int dash, int gap, int expected) =>
        Assert.Equal(expected, WindowGeometry.NextDashOffset(offset, step, dash, gap));

    [Theory]
    [InlineData(96u, 1f)]
    [InlineData(120u, 1.25f)]
    [InlineData(144u, 1.5f)]
    [InlineData(192u, 2f)]
    public void Dpi_scale(uint dpi, float expected) => Assert.Equal(expected, WindowGeometry.Scale(dpi));
}
