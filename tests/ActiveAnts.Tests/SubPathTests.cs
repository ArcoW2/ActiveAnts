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

    private static Pen DashedPen(int width, float scale) =>
        new(Color.FromArgb(unchecked((int)0xFFFFFF00)), width * scale) { DashPattern = [8f / width, 32f / width] };

    /// <summary>
    /// Strips drawn from their sub-paths must give the same pixels as the whole border path
    /// (clockwise from the top-left corner) drawn as one polyline. Excluded: the corner square
    /// at the top-left, where the path starts and ends. The pattern has a seam there anyway
    /// (the perimeter is not a whole number of dash periods), and GDI+ ends the one-piece
    /// polyline there slightly differently from the split one.
    /// </summary>
    [Theory]
    [InlineData(4, 1f, 0f)]
    [InlineData(4, 1f, 13.5f)]
    [InlineData(4, 1.5f, 7f)]
    [InlineData(3, 1.25f, 21f)]
    [InlineData(1, 2f, 39f)]
    public void Strips_match_the_full_rectangle(int width, float scale, float dashOffset)
    {
        var frame = new RECT(100, 80, 437, 291);
        int p = WindowGeometry.Padding(width * scale);
        RECT area = WindowGeometry.OverlayRect(frame, p);
        float offsetUnits = dashOffset / width;

        using Pen pen = DashedPen(width, scale);
        using Bitmap full = Canvas(area, out Graphics gFull);
        pen.DashOffset = offsetUnits;
        gFull.DrawLines(pen, [new PointF(frame.Left, frame.Top), new PointF(frame.Right, frame.Top),
                              new PointF(frame.Right, frame.Bottom), new PointF(frame.Left, frame.Bottom),
                              new PointF(frame.Left, frame.Top)]);

        using Bitmap parts = Canvas(area, out Graphics gParts);
        foreach (RECT strip in StripLayout.Strips(frame, p))
        {
            gParts.SetClip(new Rectangle(strip.Left, strip.Top, strip.Width, strip.Height));
            BorderRenderer.DrawParts(gParts, pen, StripLayout.SubPaths(frame, strip, width * scale / 2 + 2), offsetUnits);
        }
        gFull.Dispose();
        gParts.Dispose();

        int seam = (int)Math.Ceiling(width * scale) + 3;
        int differing = 0;
        var diag = new List<string>();
        for (int x = 0; x < area.Width; x++)
            for (int y = 0; y < area.Height; y++)
            {
                if (Math.Abs(x + area.Left - frame.Left) < seam && Math.Abs(y + area.Top - frame.Top) < seam)
                    continue;
                Color a = full.GetPixel(x, y), b = parts.GetPixel(x, y);
                if (Math.Abs(a.A - b.A) > 2)
                {
                    differing++;
                    diag.Add($"({x + area.Left},{y + area.Top}) {a.A}/{b.A}");
                }
            }
        Assert.True(differing == 0, $"frame {frame} strips {string.Join(' ', StripLayout.Strips(frame, p))}: " + string.Join(" ", diag));
    }

    [Fact]
    public void Top_strip_is_one_polyline_through_both_top_corners_plus_the_left_edge_end()
    {
        var frame = new RECT(100, 100, 300, 200);
        RECT top = StripLayout.Strips(frame, 3)[0];
        List<SubPath> parts = StripLayout.SubPaths(frame, top, 4);

        Assert.Equal(2, parts.Count);
        Assert.Equal(0f, parts[0].Phase);                                // starts on the top edge
        Assert.Equal([new PointF(100, 100), new PointF(300, 100), new PointF(300, 107)], parts[0].Points);
        Assert.Equal(2 * 200 + 100 + 100 - 7, parts[1].Phase);           // tail of the left edge
        Assert.Equal([new PointF(100, 107), new PointF(100, 100)], parts[1].Points);
    }

    [Fact]
    public void Left_strip_gets_its_corner_tails_and_the_left_edge()
    {
        var frame = new RECT(100, 100, 300, 200);
        RECT left = StripLayout.Strips(frame, 3)[2];       // x 97..103, y 103..197
        List<SubPath> parts = StripLayout.SubPaths(frame, left, 4);

        Assert.Equal(2, parts.Count);
        Assert.Equal([new PointF(100, 100), new PointF(107, 100)], parts[0].Points);     // head of the top edge
        Assert.Equal(0f, parts[0].Phase);
        Assert.Equal([new PointF(107, 200), new PointF(100, 200), new PointF(100, 100)], parts[1].Points);
        Assert.Equal(200 + 100 + (300 - 107), parts[1].Phase);                           // through the BL corner
    }
}
