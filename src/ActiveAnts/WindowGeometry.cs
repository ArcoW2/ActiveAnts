namespace ActiveAnts;

/// <summary>A monitor's bounds in physical pixels and its scale relative to 96 DPI.</summary>
internal readonly record struct MonitorArea(RECT Bounds, float Scale);

/// <summary>Window and monitor geometry, all in physical pixels.</summary>
internal static class WindowGeometry
{
    public const int FullScreenTolerance = 4;

    /// <summary>Margin around the frame edge that holds the antialiased stroke.</summary>
    public static int Padding(float strokeWidth) => (int)Math.Ceiling(strokeWidth / 2) + 1;

    public static RECT OverlayRect(RECT frame, int padding) => frame.Inflate(padding);

    public static float Scale(uint dpi) => dpi / 96f;

    /// <summary>
    /// Dash offset after <paramref name="elapsedMs"/> of animation: animStep pixels per animMs,
    /// taken from the clock so timer jitter does not change the speed. A positive step walks
    /// the ants counterclockwise, a negative one clockwise. Result is in [0, dashLen + gapLen).
    /// </summary>
    public static float DashOffsetAt(float startOffset, double elapsedMs, int animStep, int animMs, int dashLen, int gapLen)
    {
        int period = dashLen + gapLen;
        double offset = (startOffset + elapsedMs * animStep / animMs) % period;
        return (float)(offset < 0 ? offset + period : offset);
    }

    /// <summary>Window covers its monitor, within a tolerance for the DWM frame difference.</summary>
    public static bool IsFullScreen(RECT window, RECT monitor, int tolerance = FullScreenTolerance) =>
        window.Left <= monitor.Left + tolerance && window.Top <= monitor.Top + tolerance &&
        window.Right >= monitor.Right - tolerance && window.Bottom >= monitor.Bottom - tolerance;

    public static bool IsFullScreen(nint hwnd)
    {
        if (!Native.GetWindowRect(hwnd, out RECT window))
            return false;
        var info = new MONITORINFO { CbSize = 40 };
        nint monitor = Native.MonitorFromWindow(hwnd, Native.MONITOR_DEFAULTTONEAREST);
        if (!Native.GetMonitorInfo(monitor, ref info))
            return false;
        return IsFullScreen(window, info.Monitor);
    }

    /// <summary>
    /// The visible frame: DWM extended frame bounds, which exclude the invisible resize border.
    /// Falls back to the window rect.
    /// </summary>
    public static RECT VisibleRect(nint hwnd)
    {
        if (Native.DwmGetWindowAttribute(hwnd, Native.DWMWA_EXTENDED_FRAME_BOUNDS, out RECT rect, 16) == 0)
            return rect;
        Native.GetWindowRect(hwnd, out rect);
        return rect;
    }

    private static List<MonitorArea>? enumTarget;
    private static readonly Native.MonitorEnumProc enumProc = OnMonitor;   // held so the GC never collects it
    private static readonly nint enumProcPtr = System.Runtime.InteropServices.Marshal.GetFunctionPointerForDelegate(enumProc);

    /// <summary>All monitors with their effective DPI scale. UI thread only.</summary>
    public static List<MonitorArea> Monitors()
    {
        var result = new List<MonitorArea>();
        enumTarget = result;
        try
        {
            Native.EnumDisplayMonitors(0, 0, enumProcPtr, 0);
        }
        finally
        {
            enumTarget = null;
        }
        return result;
    }

    private static int OnMonitor(nint hMonitor, nint hdc, nint lprcMonitor, nint dwData)
    {
        var info = new MONITORINFO { CbSize = 40 };
        if (Native.GetMonitorInfo(hMonitor, ref info))
        {
            uint dpi = Native.GetDpiForMonitor(hMonitor, Native.MDT_EFFECTIVE_DPI, out uint dpiX, out _) == 0 ? dpiX : 96;
            enumTarget?.Add(new MonitorArea(info.Monitor, Scale(dpi)));
        }
        return 1;
    }
}
