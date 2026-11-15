using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace ActiveAnts;

/// <summary>The border edges, in path order: clockwise from the top-left corner.</summary>
internal enum Edge { Top, Right, Bottom, Left }

/// <summary>Stroke width per edge in pixels at 96 DPI; 0 hides that edge.</summary>
internal readonly record struct EdgeWidths(int Top, int Right, int Bottom, int Left)
{
    public static EdgeWidths From(Settings s) => new(s.TopWidth, s.RightWidth, s.BottomWidth, s.LeftWidth);

    public int this[Edge edge] => edge switch
    {
        Edge.Top => Top,
        Edge.Right => Right,
        Edge.Bottom => Bottom,
        _ => Left,
    };

    public int Max => Math.Max(Math.Max(Top, Right), Math.Max(Bottom, Left));
}

internal readonly record struct BorderStyle(uint Color, EdgeWidths Widths, int DashLen, int GapLen)
{
    public static BorderStyle From(Settings s) => new(s.BorderColor, EdgeWidths.From(s), s.DashLen, s.GapLen);
}

/// <summary>One strip window's rectangle and the DPI scale of the monitor it is on.</summary>
internal readonly record struct StripPiece(RECT Bounds, float Scale);

/// <summary>Part of one edge's line and its distance along the border path from the start.</summary>
internal readonly record struct SubPath(PointF From, PointF To, float Phase, Edge Edge);

/// <summary>
/// Splits the border into strip windows. Each drawn edge has a strip covering the band
/// [edge - p, edge + p] around it, p from that edge's own width, so the stroke is centred on the
/// visible frame edge. Top and bottom span the full width including the corners; left and right
/// fill the height between them. An edge of width 0 has no strip.
/// </summary>
internal static class StripLayout
{
    /// <summary>Top, bottom, left, right. Empty for hidden edges and, for tiny frames, sometimes others.</summary>
    public static RECT[] Strips(RECT frame, EdgeWidths widths, float scale)
    {
        int Pad(Edge e) => widths[e] > 0 ? WindowGeometry.Padding(widths[e] * scale) : 0;
        int pt = Pad(Edge.Top), pr = Pad(Edge.Right), pb = Pad(Edge.Bottom), pl = Pad(Edge.Left);
        // Next to a hidden edge the neighbours end on the frame line; one pixel of room.
        int oL = pl > 0 ? frame.Left - pl : frame.Left - 1;
        int oT = pt > 0 ? frame.Top - pt : frame.Top - 1;
        int oR = pr > 0 ? frame.Right + pr : frame.Right + 1;
        int oB = pb > 0 ? frame.Bottom + pb : frame.Bottom + 1;

        RECT top = pt > 0 ? new RECT(oL, oT, oR, Math.Min(frame.Top + pt, oB)) : default;
        int midTop = pt > 0 ? top.Bottom : oT;
        RECT bottom = pb > 0 ? new RECT(oL, Math.Max(frame.Bottom - pb, midTop), oR, oB) : default;
        int midBottom = pb > 0 ? bottom.Top : oB;
        RECT left = pl > 0 ? new RECT(oL, midTop, Math.Min(frame.Left + pl, oR), midBottom) : default;
        RECT right = pr > 0 ? new RECT(Math.Max(frame.Right - pr, pl > 0 ? left.Right : oL), midTop, oR, midBottom) : default;
        return [top, bottom, left, right];
    }

    /// <summary>
    /// Per monitor: the strips at that monitor's scale, clipped to the monitor. A frame on one
    /// monitor gives a piece per drawn edge; a spanning frame gives more, each with its own
    /// scale, all centred on the same frame edge.
    /// </summary>
    public static List<StripPiece> Pieces(RECT frame, EdgeWidths widths, IReadOnlyList<MonitorArea> monitors)
    {
        var pieces = new List<StripPiece>(4);
        foreach (MonitorArea m in monitors)
        {
            foreach (RECT strip in Strips(frame, widths, m.Scale))
            {
                RECT piece = strip.Intersect(m.Bounds);
                if (!piece.IsEmpty)
                    pieces.Add(new StripPiece(piece, m.Scale));
            }
        }
        return pieces;
    }

    /// <summary>
    /// Factor that stretches the dash period so the perimeter holds a whole number of periods.
    /// The pattern then has no seam where the path starts and ends; the change is at most half a
    /// period spread over the whole border.
    /// </summary>
    public static float PatternStretch(RECT frame, float periodPx)
    {
        float perimeter = 2f * (frame.Width + frame.Height);
        float periods = Math.Max(1f, MathF.Round(perimeter / periodPx));
        return perimeter / (periods * periodPx);
    }

    /// <summary>
    /// The parts of the drawn edges that fall inside <paramref name="region"/> grown by
    /// <paramref name="margin"/>. Each edge is one straight line on the frame edge, extended at
    /// both ends by half the width of the neighbouring edge, so dashes run to the outer corner
    /// at their own thickness and the corner square is filled where a dash passes (a square
    /// corner, also where a thick edge meets a thin one). Phase is the distance along the border
    /// path (clockwise from the top-left corner), so the pattern runs on around the corners.
    /// </summary>
    public static List<SubPath> SubPaths(RECT frame, EdgeWidths widths, float scale, RECT region, float margin)
    {
        float l = frame.Left, t = frame.Top, r = frame.Right, b = frame.Bottom, w = r - l, h = b - t;
        float perimeter = 2 * (w + h);
        float Ext(Edge e) => widths[e] * scale / 2;     // 0 for a hidden edge
        (Edge Edge, PointF From, PointF To, float Phase)[] edges =
        [
            (Edge.Top, new(l - Ext(Edge.Left), t), new(r + Ext(Edge.Right), t), -Ext(Edge.Left)),
            (Edge.Right, new(r, t - Ext(Edge.Top)), new(r, b + Ext(Edge.Bottom)), w - Ext(Edge.Top)),
            (Edge.Bottom, new(r + Ext(Edge.Right), b), new(l - Ext(Edge.Left), b), w + h - Ext(Edge.Right)),
            (Edge.Left, new(l, b + Ext(Edge.Bottom)), new(l, t - Ext(Edge.Top)), 2 * w + h - Ext(Edge.Bottom)),
        ];
        var clip = RectangleF.FromLTRB(region.Left - margin, region.Top - margin, region.Right + margin, region.Bottom + margin);

        var result = new List<SubPath>(3);
        foreach (var e in edges)
        {
            if (widths[e.Edge] == 0 || !ClipEdge(e.From, e.To, clip, out PointF start, out PointF end, out float skipped))
                continue;
            float phase = e.Phase + skipped;
            result.Add(new SubPath(start, end, phase < 0 ? phase + perimeter : phase, e.Edge));
        }
        return result;
    }

    /// <summary>Clips an axis-aligned edge; <paramref name="skipped"/> is the length cut off its start.</summary>
    private static bool ClipEdge(PointF from, PointF to, RectangleF clip, out PointF start, out PointF end, out float skipped)
    {
        start = end = default;
        skipped = 0;
        bool horizontal = from.Y == to.Y;
        float fixedAt = horizontal ? from.Y : from.X;
        float lo = horizontal ? clip.Left : clip.Top, hi = horizontal ? clip.Right : clip.Bottom;
        float fixedLo = horizontal ? clip.Top : clip.Left, fixedHi = horizontal ? clip.Bottom : clip.Right;
        if (fixedAt < fixedLo || fixedAt > fixedHi)
            return false;

        float a = horizontal ? from.X : from.Y, z = horizontal ? to.X : to.Y;
        float s, e;
        if (a <= z)
        {
            s = Math.Max(a, lo);
            e = Math.Min(z, hi);
            skipped = s - a;
        }
        else
        {
            s = Math.Min(a, hi);
            e = Math.Max(z, lo);
            skipped = a - s;
        }
        if (a <= z ? s >= e : s <= e)
            return false;
        start = horizontal ? new PointF(s, fixedAt) : new PointF(fixedAt, s);
        end = horizontal ? new PointF(e, fixedAt) : new PointF(fixedAt, e);
        return true;
    }
}

/// <summary>
/// Paints the dashed border into one cached premultiplied-ARGB DIB per strip window and pushes
/// it with UpdateLayeredWindow. Every strip draws only the parts of the edge lines that cross
/// it, with the dash offset moved to match, so the pattern is one continuous path around the
/// corners while the dash work per frame scales with the strip, not the whole perimeter.
/// Dashes are filled rectangles, not GDI+ dashed lines: GDI+ draws a short dashed line that
/// lies entirely in a gap as solid, which left stray blocks at the corners.
/// </summary>
internal sealed class BorderRenderer : IDisposable
{
    private readonly List<StripSurface> surfaces = [];
    private readonly Dictionary<float, float> stretch = [];     // per DPI scale, for the current frame
    private BorderStyle style;
    private SolidBrush brush;
    private RECT frame;
    private int activeCount;

    public BorderRenderer(BorderStyle style, Action<int, nint, nint> hostMessage)
    {
        this.style = style;
        brush = new SolidBrush(Color.FromArgb(unchecked((int)style.Color)));
        surfaces.Add(new StripSurface(new OverlayWindow(hostMessage)));
    }

    public bool Visible => activeCount > 0;

    /// <summary>The first strip window; it lives as long as the renderer.</summary>
    public nint HostHandle => surfaces[0].Window.Handle;

    public void SetStyle(BorderStyle newStyle)
    {
        if (newStyle.Color != style.Color)
        {
            brush.Dispose();
            brush = new SolidBrush(Color.FromArgb(unchecked((int)newStyle.Color)));
        }
        style = newStyle;
    }

    /// <summary>Full redraw at a new geometry.</summary>
    public void Show(RECT newFrame, IReadOnlyList<StripPiece> pieces, float dashOffset)
    {
        frame = newFrame;
        stretch.Clear();
        foreach (StripPiece piece in pieces)
            stretch.TryAdd(piece.Scale, StripLayout.PatternStretch(frame, (style.DashLen + style.GapLen) * piece.Scale));
        float margin = style.Widths.Max * pieces.Select(p => p.Scale).DefaultIfEmpty(1f).Max() / 2 + 2;
        while (surfaces.Count < pieces.Count)
            surfaces.Add(new StripSurface(new OverlayWindow(null)));

        for (int i = 0; i < pieces.Count; i++)
        {
            StripSurface s = surfaces[i];
            s.Place(pieces[i], frame, style.Widths, margin);
            Render(s, dashOffset);
            s.Push();
            s.Window.ShowNoActivate();
        }
        for (int i = pieces.Count; i < activeCount; i++)
            surfaces[i].Window.Hide();
        activeCount = pieces.Count;
    }

    /// <summary>Animation frame: same geometry, new dash offset.</summary>
    public void Animate(float dashOffset)
    {
        for (int i = 0; i < activeCount; i++)
        {
            Render(surfaces[i], dashOffset);
            surfaces[i].Push();
        }
    }

    public void Hide()
    {
        for (int i = 0; i < activeCount; i++)
            surfaces[i].Window.Hide();
        activeCount = 0;
    }

    public bool Owns(nint hwnd) => surfaces.Exists(s => s.Window.Handle == hwnd);

    public bool WindowsAlive() => surfaces.TrueForAll(s => Native.IsWindow(s.Window.Handle));

    private void Render(StripSurface s, float dashOffset)
    {
        Graphics g = s.Graphics!;
        g.Clear(Color.FromArgb(0));
        float k = stretch[s.Scale] * s.Scale;       // 96-DPI pixels -> physical, seamless period
        DrawParts(g, brush, style.Widths, s.Scale, s.SubPaths, dashOffset * k, style.DashLen * k,
                  (style.DashLen + style.GapLen) * k);
    }

    /// <summary>
    /// Fills the dashes of the edge parts, the pattern continuous along the whole border path:
    /// a point at distance u along the path is in a dash when (u + offsetPx) mod periodPx is
    /// below dashPx. All lengths in physical pixels; each part's thickness is its edge's width.
    /// </summary>
    internal static void DrawParts(Graphics g, Brush brush, EdgeWidths widths, float scale,
                                   IReadOnlyList<SubPath> parts, float offsetPx, float dashPx, float periodPx)
    {
        foreach (SubPath part in parts)
        {
            float thick = widths[part.Edge] * scale, half = thick / 2;
            bool horizontal = part.From.Y == part.To.Y;
            float a = horizontal ? part.From.X : part.From.Y, z = horizontal ? part.To.X : part.To.Y;
            float dir = z >= a ? 1 : -1, length = Math.Abs(z - a);
            float s0 = offsetPx + part.Phase;
            // Distance along the part to the start of the dash at or before its start.
            for (float start = MathF.Floor(s0 / periodPx) * periodPx - s0; start < length; start += periodPx)
            {
                float t0 = Math.Max(start, 0), t1 = Math.Min(start + dashPx, length);
                if (t1 <= t0)
                    continue;
                float p0 = a + dir * t0, p1 = a + dir * t1;
                float lo = Math.Min(p0, p1), size = Math.Abs(p1 - p0);
                g.FillRectangle(brush, horizontal
                    ? new RectangleF(lo, part.From.Y - half, size, thick)
                    : new RectangleF(part.From.X - half, lo, thick, size));
            }
        }
    }

    public void Dispose()
    {
        foreach (StripSurface s in surfaces)
            s.Dispose();
        surfaces.Clear();
        brush.Dispose();
    }

    /// <summary>One strip window plus its DIB section, memory DC and GDI+ wrapper.</summary>
    private sealed class StripSurface(OverlayWindow window) : IDisposable
    {
        private nint hdc, hbitmap, oldBitmap;
        private Bitmap? bitmap;
        private int width, height;

        public OverlayWindow Window { get; } = window;
        public RECT Bounds { get; private set; }
        public float Scale { get; private set; }
        public Graphics? Graphics { get; private set; }
        public List<SubPath> SubPaths { get; private set; } = [];

        public void Place(StripPiece piece, RECT frame, EdgeWidths widths, float margin)
        {
            SubPaths = StripLayout.SubPaths(frame, widths, piece.Scale, piece.Bounds, margin);
            if (piece.Bounds.Width != width || piece.Bounds.Height != height)
                Allocate(piece.Bounds.Width, piece.Bounds.Height);
            Bounds = piece.Bounds;
            Scale = piece.Scale;
            Graphics!.ResetTransform();
            Graphics.TranslateTransform(-Bounds.Left, -Bounds.Top);
        }

        // Position and size are always passed: with NULL for them, Windows does not show the
        // new content (measured: only the full redraws reached the screen).
        public unsafe void Push()
        {
            var dst = new POINT { X = Bounds.Left, Y = Bounds.Top };
            var size = new SIZE { Cx = width, Cy = height };
            var src = new POINT();
            var blend = new BLENDFUNCTION
            {
                BlendOp = Native.AC_SRC_OVER,
                SourceConstantAlpha = 255,
                AlphaFormat = Native.AC_SRC_ALPHA,
            };
            Native.UpdateLayeredWindow(Window.Handle, 0, &dst, &size,
                                       hdc, &src, 0, &blend, Native.ULW_ALPHA);
        }

        private void Allocate(int w, int h)
        {
            Free();
            var header = new BITMAPINFOHEADER
            {
                Size = 40,
                Width = w,
                Height = -h,        // top-down, matching GDI+ row order
                Planes = 1,
                BitCount = 32,
            };
            hbitmap = Native.CreateDIBSection(0, header, 0, out nint bits, 0, 0);
            if (hbitmap == 0)
                throw new InvalidOperationException($"CreateDIBSection failed for {w}x{h}");
            hdc = Native.CreateCompatibleDC(0);
            oldBitmap = Native.SelectObject(hdc, hbitmap);
            // Wraps the DIB memory directly; premultiplied ARGB is what UpdateLayeredWindow expects.
            bitmap = new Bitmap(w, h, w * 4, PixelFormat.Format32bppPArgb, bits);
            Graphics = Graphics.FromImage(bitmap);
            Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            Graphics.PixelOffsetMode = PixelOffsetMode.Half;    // integer coordinates are pixel edges
            width = w;
            height = h;
        }

        private void Free()
        {
            Graphics?.Dispose();
            Graphics = null;
            bitmap?.Dispose();
            bitmap = null;
            if (hdc != 0 && oldBitmap != 0)
                Native.SelectObject(hdc, oldBitmap);
            if (hdc != 0)
                Native.DeleteDC(hdc);
            if (hbitmap != 0)
                Native.DeleteObject(hbitmap);
            hdc = hbitmap = oldBitmap = 0;
            width = height = 0;
        }

        public void Dispose()
        {
            Free();
            Window.Dispose();
        }
    }
}
