using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace ActiveAnts;

internal readonly record struct BorderStyle(uint Color, int Width, int DashLen, int GapLen)
{
    public static BorderStyle From(Settings s) => new(s.BorderColor, s.BorderWidth, s.DashLen, s.GapLen);
}

/// <summary>A pen is rebuilt only when this changes.</summary>
internal readonly record struct PenKey(BorderStyle Style, float Scale)
{
    public static PenKey For(Settings s, float scale) => new(BorderStyle.From(s), scale);
}

/// <summary>One strip window's rectangle and the DPI scale of the monitor it is on.</summary>
internal readonly record struct StripPiece(RECT Bounds, float Scale);

/// <summary>
/// Splits the border into strip windows. Each strip covers the band [edge - p, edge + p]
/// around one frame edge, so the stroke is centred on the visible frame edge. Top and bottom
/// span the full width including the corners; left and right fill the height between them.
/// </summary>
internal static class StripLayout
{
    /// <summary>Top, bottom, left, right. Any of them may be empty for tiny frames.</summary>
    public static RECT[] Strips(RECT frame, int padding)
    {
        RECT o = WindowGeometry.OverlayRect(frame, padding);
        int t = 2 * padding;
        var top = new RECT(o.Left, o.Top, o.Right, Math.Min(o.Top + t, o.Bottom));
        var bottom = new RECT(o.Left, Math.Max(o.Bottom - t, top.Bottom), o.Right, o.Bottom);
        var left = new RECT(o.Left, top.Bottom, Math.Min(o.Left + t, o.Right), bottom.Top);
        var right = new RECT(Math.Max(o.Right - t, left.Right), top.Bottom, o.Right, bottom.Top);
        return [top, bottom, left, right];
    }

    /// <summary>
    /// Per monitor: the strips at that monitor's scale, clipped to the monitor. A frame on one
    /// monitor gives four pieces; a spanning frame gives more, each with its own scale, all
    /// centred on the same frame edge.
    /// </summary>
    public static List<StripPiece> Pieces(RECT frame, int borderWidth, IReadOnlyList<MonitorArea> monitors)
    {
        var pieces = new List<StripPiece>(4);
        foreach (MonitorArea m in monitors)
        {
            int padding = WindowGeometry.Padding(borderWidth * m.Scale);
            foreach (RECT strip in Strips(frame, padding))
            {
                RECT piece = strip.Intersect(m.Bounds);
                if (!piece.IsEmpty)
                    pieces.Add(new StripPiece(piece, m.Scale));
            }
        }
        return pieces;
    }
}

/// <summary>
/// Paints the dashed border into one cached premultiplied-ARGB DIB per strip window and pushes
/// it with UpdateLayeredWindow. Every strip draws the same full rectangle, translated to its
/// own origin, so the dash pattern is one continuous path around the corners.
/// </summary>
internal sealed class BorderRenderer : IDisposable
{
    private readonly List<StripSurface> surfaces = [];
    private readonly Dictionary<PenKey, Pen> pens = [];
    private BorderStyle style;
    private RECT frame;
    private int activeCount;

    public BorderRenderer(BorderStyle style, Action<int, nint> hostMessage)
    {
        this.style = style;
        surfaces.Add(new StripSurface(new OverlayWindow(hostMessage)));
    }

    public bool Visible => activeCount > 0;

    public void SetStyle(BorderStyle newStyle)
    {
        if (newStyle == style)
            return;
        style = newStyle;
        DisposePens();
    }

    /// <summary>Full redraw at a new geometry.</summary>
    public void Show(RECT newFrame, IReadOnlyList<StripPiece> pieces, float dashOffset)
    {
        frame = newFrame;
        while (surfaces.Count < pieces.Count)
            surfaces.Add(new StripSurface(new OverlayWindow(null)));

        for (int i = 0; i < pieces.Count; i++)
        {
            StripSurface s = surfaces[i];
            s.Place(pieces[i]);
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
        Pen pen = PenFor(new PenKey(style, s.Scale));
        pen.DashOffset = dashOffset / style.Width;
        Graphics g = s.Graphics!;
        g.Clear(Color.FromArgb(0));
        g.DrawRectangle(pen, frame.Left, frame.Top, frame.Width, frame.Height);
    }

    private Pen PenFor(PenKey key)
    {
        if (pens.TryGetValue(key, out Pen? pen))
            return pen;
        // Dash pattern and offset are in units of the pen width, so they scale with it.
        pen = new Pen(Color.FromArgb(unchecked((int)key.Style.Color)), key.Style.Width * key.Scale)
        {
            DashPattern = [key.Style.DashLen / (float)key.Style.Width, key.Style.GapLen / (float)key.Style.Width],
        };
        pens[key] = pen;
        return pen;
    }

    private void DisposePens()
    {
        foreach (Pen pen in pens.Values)
            pen.Dispose();
        pens.Clear();
    }

    public void Dispose()
    {
        foreach (StripSurface s in surfaces)
            s.Dispose();
        surfaces.Clear();
        DisposePens();
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

        public void Place(StripPiece piece)
        {
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
