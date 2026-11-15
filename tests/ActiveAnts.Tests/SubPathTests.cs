using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace ActiveAnts.Tests;

public sealed class SubPathTests
{
    private static Bitmap Canvas(RECT area, out Graphics g)
    {
        var bmp = new Bitmap(area.Width, area.Height, PixelFormat.Format32bppPArgb);
        g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.Half;
        g.TranslateTransform(-area.Left, -area.Top);
        g.Clear(Color.FromArgb(0));
        return bmp;
    }

    private static readonly SolidBrush Yellow = new(Color.FromArgb(unchecked((int)0xFFFFFF00)));

    public static TheoryData<int, int, int, int, float, float> Cases => new()
    {
        { 4, 4, 4, 4, 1f, 0.5f },
        { 4, 4, 4, 4, 1f, 13.5f },
        { 3, 5, 3, 5, 1f, 7f },         // thinner horizontal edges
        { 6, 2, 4, 1, 1.5f, 21f },      // all different, scaled
        { 0, 4, 4, 4, 1.25f, 39f },     // top hidden
        { 4, 0, 4, 0, 2f, 3f },         // left and right hidden
    };

    /// <summary>
    /// Strips drawn from their clipped sub-paths must give the same pixels as all edge lines
    /// drawn whole: the clipping and the phase bookkeeping keep the pattern continuous.
    /// </summary>
    [Theory]
    [MemberData(nameof(Cases))]
    public void Strips_match_the_whole_border(int top, int right, int bottom, int left, float scale, float dashOffset)
    {
        var frame = new RECT(100, 80, 437, 291);
        var widths = new EdgeWidths(top, right, bottom, left);
        RECT area = frame.Inflate(12);
        float k = StripLayout.PatternStretch(frame, 40 * scale);
        float offsetPx = dashOffset * k * scale;
        float margin = widths.Max * scale / 2 + 2;
        float dashPx = 8 * k * scale, periodPx = 40 * k * scale;

        using Bitmap full = Canvas(area, out Graphics gFull);
        BorderRenderer.DrawParts(gFull, Yellow, widths, scale, StripLayout.SubPaths(frame, widths, scale, area.Inflate(100), 0),
                                 offsetPx, dashPx, periodPx);

        using Bitmap parts = Canvas(area, out Graphics gParts);
        foreach (RECT strip in StripLayout.Strips(frame, widths, scale).Where(s => !s.IsEmpty))
        {
            gParts.SetClip(new Rectangle(strip.Left, strip.Top, strip.Width, strip.Height));
            BorderRenderer.DrawParts(gParts, Yellow, widths, scale, StripLayout.SubPaths(frame, widths, scale, strip, margin),
                                     offsetPx, dashPx, periodPx);
        }
        gFull.Dispose();
        gParts.Dispose();

        var differing = new List<string>();
        for (int x = 0; x < area.Width; x++)
            for (int y = 0; y < area.Height; y++)
            {
                Color a = full.GetPixel(x, y), b = parts.GetPixel(x, y);
                if (Math.Abs(a.A - b.A) > 2)
                    differing.Add($"({x + area.Left},{y + area.Top}) {a.A}/{b.A}");
            }
        Assert.True(differing.Count == 0, string.Join(" ", differing));
    }

    /// <summary>A thick top edge meeting a thin left edge: both run to the outer corner, square filled.</summary>
    [Fact]
    public void Square_corner_where_a_thick_edge_meets_a_thin_one()
    {
        var frame = new RECT(100, 100, 300, 200);
        var widths = new EdgeWidths(6, 2, 6, 2);
        RECT area = frame.Inflate(10);
        using Bitmap bmp = Canvas(area, out Graphics g);
        // Dash longer than the perimeter: solid.
        BorderRenderer.DrawParts(g, Yellow, widths, 1f, StripLayout.SubPaths(frame, widths, 1f, area, 0), 0, 10_000, 10_001);
        g.Dispose();

        // Outer corner square: x 99..100 (half the left width), y 97..100 (half the top width).
        for (int x = 99; x < 100; x++)
            for (int y = 97; y < 100; y++)
                Assert.Equal(255, bmp.GetPixel(x - area.Left, y - area.Top).A);
        // Nothing beyond it.
        Assert.Equal(0, bmp.GetPixel(98 - area.Left, 99 - area.Top).A);
        Assert.Equal(0, bmp.GetPixel(99 - area.Left, 96 - area.Top).A);
    }

    /// <summary>The GDI+ quirk this replaces: a short part lying entirely in a gap must stay empty.</summary>
    [Fact]
    public void Short_part_in_a_gap_draws_nothing()
    {
        var part = new SubPath(new PointF(5, 0), new PointF(5, 10), 0, Edge.Right);
        RECT area = new(0, 0, 10, 20);
        using Bitmap bmp = Canvas(area, out Graphics g);
        BorderRenderer.DrawParts(g, Yellow, new EdgeWidths(4, 4, 4, 4), 1f, [part], 10, 8, 40);   // gap 8..40
        g.Dispose();
        for (int y = 0; y < 20; y++)
            Assert.Equal(0, bmp.GetPixel(5, y).A);
    }

    [Fact]
    public void Dashes_follow_offset_and_period()
    {
        var part = new SubPath(new PointF(0, 5), new PointF(100, 5), 0, Edge.Top);
        RECT area = new(0, 0, 100, 10);
        using Bitmap bmp = Canvas(area, out Graphics g);
        BorderRenderer.DrawParts(g, Yellow, new EdgeWidths(2, 2, 2, 2), 1f, [part], 5, 8, 40);
        g.Dispose();
        // Dash where (x + 5) mod 40 < 8: x in [0,3) and [35,43) and [75,83).
        string row = string.Concat(Enumerable.Range(0, 100).Select(x => bmp.GetPixel(x, 5).A > 127 ? '#' : '.'));
        Assert.Equal(new string('#', 3) + new string('.', 32) + new string('#', 8) + new string('.', 32) + new string('#', 8)
                     + new string('.', 17), row);
    }

    [Fact]
    public void Edge_lines_extend_by_half_the_neighbour_width_with_continuous_phase()
    {
        var frame = new RECT(100, 100, 300, 200);
        List<SubPath> parts = StripLayout.SubPaths(frame, new EdgeWidths(6, 2, 6, 4), 1f, frame.Inflate(50), 0);

        SubPath top = parts.Single(p => p.Edge == Edge.Top);
        Assert.Equal(new PointF(98, 100), top.From);        // left width 4: 2 past the corner
        Assert.Equal(new PointF(301, 100), top.To);         // right width 2: 1 past the corner
        Assert.Equal(600 - 2, top.Phase);                   // -2, wrapped by the perimeter 600

        SubPath right = parts.Single(p => p.Edge == Edge.Right);
        Assert.Equal(new PointF(300, 97), right.From);      // top width 6: 3 past the corner
        Assert.Equal(200 - 3, right.Phase);
    }

    [Fact]
    public void Hidden_edge_is_not_drawn_and_neighbours_end_on_the_frame_line()
    {
        var frame = new RECT(100, 100, 300, 200);
        List<SubPath> parts = StripLayout.SubPaths(frame, new EdgeWidths(0, 4, 4, 4), 1f, frame.Inflate(50), 0);

        Assert.DoesNotContain(parts, p => p.Edge == Edge.Top);
        Assert.Equal(new PointF(300, 100), parts.Single(p => p.Edge == Edge.Right).From);
        Assert.Equal(new PointF(100, 100), parts.Single(p => p.Edge == Edge.Left).To);
    }

    [Theory]
    [InlineData(100, 50, 40f)]
    [InlineData(337, 211, 40f)]
    [InlineData(3440, 1400, 60f)]
    [InlineData(3, 2, 40f)]
    public void Pattern_stretch_fits_a_whole_number_of_periods(int w, int h, float period)
    {
        var frame = new RECT(0, 0, w, h);
        float k = StripLayout.PatternStretch(frame, period);
        double periods = 2.0 * (w + h) / (period * k);
        Assert.Equal(Math.Round(periods), periods, 3);
        if (2 * (w + h) >= period)
            Assert.InRange(k, 2f / 3, 1.5f);       // at most half a period over the border
    }
}
