namespace ActiveAnts.Tests;

public sealed class StripLayoutTests
{
    /// <summary>Pixel coverage counts of the given rects over an area.</summary>
    private static int[,] Coverage(RECT area, IEnumerable<RECT> rects)
    {
        var grid = new int[area.Width, area.Height];
        foreach (RECT r in rects)
            for (int x = r.Left; x < r.Right; x++)
                for (int y = r.Top; y < r.Bottom; y++)
                    grid[x - area.Left, y - area.Top]++;
        return grid;
    }

    private static bool Inside(RECT r, int x, int y) => x >= r.Left && x < r.Right && y >= r.Top && y < r.Bottom;

    /// <summary>The pixels the strips must cover: each drawn edge's band, within the outer extent.</summary>
    private static bool InBand(RECT frame, EdgeWidths w, float scale, int x, int y)
    {
        int Pad(int width) => width > 0 ? WindowGeometry.Padding(width * scale) : 0;
        int pt = Pad(w.Top), pr = Pad(w.Right), pb = Pad(w.Bottom), pl = Pad(w.Left);
        var outer = new RECT(pl > 0 ? frame.Left - pl : frame.Left - 1, pt > 0 ? frame.Top - pt : frame.Top - 1,
                             pr > 0 ? frame.Right + pr : frame.Right + 1, pb > 0 ? frame.Bottom + pb : frame.Bottom + 1);
        return Inside(outer, x, y) &&
               (pt > 0 && y < frame.Top + pt || pb > 0 && y >= frame.Bottom - pb ||
                pl > 0 && x < frame.Left + pl || pr > 0 && x >= frame.Right - pr);
    }

    public static TheoryData<int, int, int, int, int, int, int, int> Frames => new()
    {
        { 100, 100, 400, 300, 1, 1, 1, 1 },
        { 100, 100, 400, 300, 4, 4, 4, 4 },
        { 100, 100, 400, 300, 5, 5, 5, 5 },
        { 100, 100, 400, 300, 3, 6, 3, 6 },     // thinner horizontal edges
        { 100, 100, 400, 300, 8, 2, 5, 1 },     // all different
        { 100, 100, 400, 300, 0, 4, 4, 4 },     // top hidden
        { 100, 100, 400, 300, 4, 0, 4, 0 },     // left and right hidden
        { 100, 100, 400, 300, 0, 4, 0, 4 },     // top and bottom hidden
        { 100, 100, 103, 400, 4, 4, 4, 4 },     // narrower than two strips
        { 100, 100, 400, 101, 4, 4, 4, 4 },     // lower than two strips
        { 100, 100, 101, 101, 7, 3, 7, 3 },     // tiny both ways
    };

    [Theory]
    [MemberData(nameof(Frames))]
    public void Strips_tile_the_drawn_bands_without_gap_or_overlap(int l, int t, int r, int b, int top, int right, int bottom, int left)
    {
        var frame = new RECT(l, t, r, b);
        var widths = new EdgeWidths(top, right, bottom, left);
        RECT area = frame.Inflate(10);
        int[,] grid = Coverage(area, StripLayout.Strips(frame, widths, 1f).Where(s => !s.IsEmpty));

        for (int x = area.Left; x < area.Right; x++)
            for (int y = area.Top; y < area.Bottom; y++)
            {
                int expected = InBand(frame, widths, 1f, x, y) ? 1 : 0;
                Assert.True(expected == grid[x - area.Left, y - area.Top], $"pixel {x},{y}");
            }
    }

    [Fact]
    public void Hidden_edge_has_no_strip()
    {
        RECT[] strips = StripLayout.Strips(new RECT(100, 100, 400, 300), new EdgeWidths(0, 4, 4, 0), 1f);
        Assert.True(strips[0].IsEmpty);     // top
        Assert.True(strips[2].IsEmpty);     // left
        Assert.False(strips[1].IsEmpty);
        Assert.False(strips[3].IsEmpty);
    }

    [Fact]
    public void All_edges_hidden_gives_no_pieces() =>
        Assert.Empty(StripLayout.Pieces(new RECT(100, 100, 500, 500), new EdgeWidths(0, 0, 0, 0), Monitors));

    private static readonly MonitorArea[] Monitors =
    [
        new(new RECT(0, 0, 1000, 800), 1f),
        new(new RECT(1000, 0, 2000, 800), 1.5f),
        new(new RECT(0, 800, 1000, 1600), 1.25f),
        new(new RECT(1000, 800, 2000, 1600), 2f),
    ];

    public static TheoryData<int, int, int, int> SpanningFrames => new()
    {
        { 800, 100, 1200, 500 },        // crosses the vertical boundary
        { 100, 600, 500, 1000 },        // crosses the horizontal boundary
        { 800, 600, 1200, 1000 },       // crosses both
        { 100, 100, 500, 500 },         // on one monitor
    };

    private static readonly EdgeWidths Mixed = new(3, 5, 4, 6);

    [Theory]
    [MemberData(nameof(SpanningFrames))]
    public void Spanning_pieces_tile_each_monitors_bands(int l, int t, int r, int b)
    {
        var frame = new RECT(l, t, r, b);
        List<StripPiece> pieces = StripLayout.Pieces(frame, Mixed, Monitors);
        RECT area = frame.Inflate(20);
        int[,] grid = Coverage(area, pieces.Select(x => x.Bounds));

        foreach (StripPiece piece in pieces)
            Assert.Contains(Monitors, m => m.Scale == piece.Scale && piece.Bounds.Intersect(m.Bounds) == piece.Bounds);

        for (int x = area.Left; x < area.Right; x++)
            for (int y = area.Top; y < area.Bottom; y++)
            {
                MonitorArea m = Monitors.Single(m => Inside(m.Bounds, x, y));
                int expected = InBand(frame, Mixed, m.Scale, x, y) ? 1 : 0;
                Assert.True(expected == grid[x - area.Left, y - area.Top], $"pixel {x},{y}");
            }
    }

    [Theory]
    [MemberData(nameof(SpanningFrames))]
    public void Every_piece_is_centred_on_a_frame_edge(int l, int t, int r, int b)
    {
        var frame = new RECT(l, t, r, b);
        foreach (StripPiece piece in StripLayout.Pieces(frame, Mixed, Monitors))
        {
            int P(int width) => WindowGeometry.Padding(width * piece.Scale);
            RECT x = piece.Bounds;
            bool top = x.Top == frame.Top - P(Mixed.Top) && x.Height == 2 * P(Mixed.Top);
            bool bottom = x.Top == frame.Bottom - P(Mixed.Bottom) && x.Height == 2 * P(Mixed.Bottom);
            bool left = x.Left == frame.Left - P(Mixed.Left) && x.Width == 2 * P(Mixed.Left);
            bool right = x.Left == frame.Right - P(Mixed.Right) && x.Width == 2 * P(Mixed.Right);
            Assert.True(top || bottom || left || right, $"piece {x} scale {piece.Scale}");
        }
    }

    [Fact]
    public void Single_monitor_gives_four_pieces()
    {
        List<StripPiece> pieces = StripLayout.Pieces(new RECT(100, 100, 500, 500), new EdgeWidths(4, 4, 4, 4), Monitors);
        Assert.Equal(4, pieces.Count);
        Assert.All(pieces, x => Assert.Equal(1f, x.Scale));
    }

    [Fact]
    public void Frame_off_all_monitors_gives_no_pieces() =>
        Assert.Empty(StripLayout.Pieces(new RECT(5000, 5000, 5100, 5100), new EdgeWidths(4, 4, 4, 4), Monitors));
}
