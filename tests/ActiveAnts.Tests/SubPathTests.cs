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

    private static Pen DashedPen(int width, float scale, float stretch) =>
        new(Color.FromArgb(unchecked((int)0xFFFFFF00)), width * scale)
        {
            DashPattern = [8f * stretch / width, 32f * stretch / width],
        };

    /// <summary>
    /// Strips drawn from their sub-paths must give the same pixels as the whole border drawn as
    /// one polyline, including the top-left corner where the path starts and ends. The reference
    /// starts and ends mid-way along the top edge instead; only a small square there is excluded.
    /// </summary>
    // Not offset 0: with the stretched pattern a dash edge then lands exactly on the top-left
    // corner, and float rounding decides whether the corner gets its join; both are valid.
    [Theory]
    [InlineData(4, 1f, 0.5f)]
    [InlineData(4, 1f, 13.5f)]
    [InlineData(4, 1.5f, 7f)]
    [InlineData(3, 1.25f, 21f)]
    [InlineData(1, 2f, 39f)]
    public void Strips_match_the_full_border(int width, float scale, float dashOffset)
    {
        var frame = new RECT(100, 80, 437, 291);
        int p = WindowGeometry.Padding(width * scale);
        RECT area = WindowGeometry.OverlayRect(frame, p);
        float k = StripLayout.PatternStretch(frame, 40 * scale);
        float offsetUnits = dashOffset * k / width;
        using Pen pen = DashedPen(width, scale, k);

        int mid = 250;
        SubPath reference = new(
            [new PointF(mid, frame.Top), new PointF(frame.Right, frame.Top), new PointF(frame.Right, frame.Bottom),
             new PointF(frame.Left, frame.Bottom), new PointF(frame.Left, frame.Top), new PointF(mid, frame.Top)],
            mid - frame.Left);
        using Bitmap full = Canvas(area, out Graphics gFull);
        BorderRenderer.DrawParts(gFull, pen, [reference], offsetUnits);

        using Bitmap parts = Canvas(area, out Graphics gParts);
        foreach (RECT strip in StripLayout.Strips(frame, p))
        {
            gParts.SetClip(new Rectangle(strip.Left, strip.Top, strip.Width, strip.Height));
            BorderRenderer.DrawParts(gParts, pen, StripLayout.SubPaths(frame, strip, width * scale / 2 + 2), offsetUnits);
        }
        gFull.Dispose();
        gParts.Dispose();

        int seam = (int)Math.Ceiling(width * scale) + 3;
        var differing = new List<string>();
        for (int x = 0; x < area.Width; x++)
            for (int y = 0; y < area.Height; y++)
            {
                if (Math.Abs(x + area.Left - mid) < seam && Math.Abs(y + area.Top - frame.Top) < seam)
                    continue;
                Color a = full.GetPixel(x, y), b = parts.GetPixel(x, y);
                if (Math.Abs(a.A - b.A) > 2)
                    differing.Add($"({x + area.Left},{y + area.Top}) {a.A}/{b.A}");
            }
        Assert.True(differing.Count == 0, string.Join(" ", differing));
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

    [Fact]
    public void Top_strip_is_one_polyline_through_the_top_left_corner_and_both_top_corners()
    {
        var frame = new RECT(100, 100, 300, 200);
        RECT top = StripLayout.Strips(frame, 3)[0];
        List<SubPath> parts = StripLayout.SubPaths(frame, top, 4);

        SubPath part = Assert.Single(parts);
        Assert.Equal([new PointF(100, 107), new PointF(100, 100), new PointF(300, 100), new PointF(300, 107)], part.Points);
        Assert.Equal(2 * 200 + 100 + 100 - 7, part.Phase);      // starts on the tail of the left edge
    }

    [Fact]
    public void Left_strip_gets_its_corner_tails_and_the_left_edge()
    {
        var frame = new RECT(100, 100, 300, 200);
        RECT left = StripLayout.Strips(frame, 3)[2];       // x 97..103, y 103..197
        List<SubPath> parts = StripLayout.SubPaths(frame, left, 4);

        SubPath part = Assert.Single(parts);                // bottom-left corner, left edge, top-left corner
        Assert.Equal([new PointF(107, 200), new PointF(100, 200), new PointF(100, 100), new PointF(107, 100)], part.Points);
        Assert.Equal(200 + 100 + (300 - 107), part.Phase);
    }
}
