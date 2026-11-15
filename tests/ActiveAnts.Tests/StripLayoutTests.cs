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

    public static TheoryData<int, int, int, int, int> Frames => new()
    {
        { 100, 100, 400, 300, 1 },
        { 100, 100, 400, 300, 2 },
        { 100, 100, 400, 300, 5 },
        { 100, 100, 400, 300, 8 },
        { 100, 100, 103, 400, 4 },      // narrower than two strips
        { 100, 100, 400, 101, 4 },      // lower than two strips
        { 100, 100, 101, 101, 7 },      // tiny both ways
    };

    [Theory]
    [MemberData(nameof(Frames))]
    public void Four_strips_tile_the_band_without_gap_or_overlap(int l, int t, int r, int b, int width)
    {
        var frame = new RECT(l, t, r, b);
        int p = WindowGeometry.Padding(width);
        RECT outer = WindowGeometry.OverlayRect(frame, p);
        RECT inner = frame.Inflate(-p);     // may be empty for tiny frames
        int[,] grid = Coverage(outer, StripLayout.Strips(frame, p));

        for (int x = outer.Left; x < outer.Right; x++)
            for (int y = outer.Top; y < outer.Bottom; y++)
            {
                int expected = !inner.IsEmpty && Inside(inner, x, y) ? 0 : 1;
                Assert.True(expected == grid[x - outer.Left, y - outer.Top], $"pixel {x},{y}");
            }
    }

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

    [Theory]
    [MemberData(nameof(SpanningFrames))]
    public void Spanning_pieces_tile_each_monitors_band(int l, int t, int r, int b)
    {
        const int borderWidth = 4;
        var frame = new RECT(l, t, r, b);
        List<StripPiece> pieces = StripLayout.Pieces(frame, borderWidth, Monitors);
        RECT area = frame.Inflate(20);
        int[,] grid = Coverage(area, pieces.Select(x => x.Bounds));

        foreach (StripPiece piece in pieces)
            Assert.Contains(Monitors, m => m.Scale == piece.Scale && piece.Bounds.Intersect(m.Bounds) == piece.Bounds);

        for (int x = area.Left; x < area.Right; x++)
            for (int y = area.Top; y < area.Bottom; y++)
            {
                MonitorArea m = Monitors.Single(m => Inside(m.Bounds, x, y));
                int p = WindowGeometry.Padding(borderWidth * m.Scale);
                bool inBand = Inside(frame.Inflate(p), x, y) && !Inside(frame.Inflate(-p), x, y);
                Assert.True((inBand ? 1 : 0) == grid[x - area.Left, y - area.Top], $"pixel {x},{y}");
            }
    }

    [Theory]
    [MemberData(nameof(SpanningFrames))]
    public void Every_piece_is_centred_on_a_frame_edge(int l, int t, int r, int b)
    {
        const int borderWidth = 4;
        var frame = new RECT(l, t, r, b);
        foreach (StripPiece piece in StripLayout.Pieces(frame, borderWidth, Monitors))
        {
            int p = WindowGeometry.Padding(borderWidth * piece.Scale);
            RECT x = piece.Bounds;
            bool horizontal = (x.Top == frame.Top - p || x.Top == frame.Bottom - p) && x.Height == 2 * p;
            bool vertical = (x.Left == frame.Left - p || x.Left == frame.Right - p) && x.Width == 2 * p;
            Assert.True(horizontal || vertical, $"piece {x} scale {piece.Scale}");
        }
    }

    [Fact]
    public void Single_monitor_gives_four_pieces()
    {
        List<StripPiece> pieces = StripLayout.Pieces(new RECT(100, 100, 500, 500), 4, Monitors);
        Assert.Equal(4, pieces.Count);
        Assert.All(pieces, x => Assert.Equal(1f, x.Scale));
    }

    [Fact]
    public void Frame_off_all_monitors_gives_no_pieces() =>
        Assert.Empty(StripLayout.Pieces(new RECT(5000, 5000, 5100, 5100), 4, Monitors));
}
