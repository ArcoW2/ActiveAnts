using System.Runtime.InteropServices;
using Timer = System.Windows.Forms.Timer;

namespace ActiveAnts;

/// <summary>
/// Tracks the window to outline through WinEvent hooks: foreground changes switch it, moves and
/// resizes hide the border until the window has been still for settleMs. Callbacks arrive on
/// the UI thread (WINEVENT_OUTOFCONTEXT), so no locking.
/// </summary>
internal sealed class WindowTracker : IDisposable
{
    private readonly Native.WinEventProc foregroundProc;    // held so the GC never collects them
    private readonly Native.WinEventProc locationProc;
    private readonly Func<nint, bool> isOwnWindow;
    private readonly Timer settleTimer = new();
    private readonly Timer foregroundTimer = new() { Interval = 30 };
    private nint foregroundHook;
    private nint locationHook;

    public WindowTracker(Func<nint, bool> isOwnWindow, int settleMs)
    {
        this.isOwnWindow = isOwnWindow;
        foregroundProc = OnForeground;
        locationProc = OnLocation;
        SettleMs = settleMs;
        settleTimer.Tick += (_, _) => Settle();
        foregroundTimer.Tick += (_, _) =>
        {
            foregroundTimer.Stop();
            RedrawRequested?.Invoke();
        };
    }

    public event Action? RedrawRequested;
    public event Action? HideRequested;

    public nint Current { get; set; }
    public bool Dragging { get; private set; }
    public bool HooksValid => foregroundHook != 0 && locationHook != 0;

    public int SettleMs
    {
        set => settleTimer.Interval = Math.Max(1, value);
    }

    public bool Hook()
    {
        const uint flags = Native.WINEVENT_OUTOFCONTEXT | Native.WINEVENT_SKIPOWNPROCESS;
        foregroundHook = Native.SetWinEventHook(Native.EVENT_SYSTEM_FOREGROUND, Native.EVENT_SYSTEM_FOREGROUND, 0,
                                                Marshal.GetFunctionPointerForDelegate(foregroundProc), 0, 0, flags);
        locationHook = Native.SetWinEventHook(Native.EVENT_OBJECT_LOCATIONCHANGE, Native.EVENT_OBJECT_LOCATIONCHANGE, 0,
                                              Marshal.GetFunctionPointerForDelegate(locationProc), 0, 0, flags);
        return HooksValid;
    }

    public void Unhook()
    {
        if (foregroundHook != 0)
            Native.UnhookWinEvent(foregroundHook);
        if (locationHook != 0)
            Native.UnhookWinEvent(locationHook);
        foregroundHook = locationHook = 0;
    }

    /// <summary>Clears drag state and cancels a pending settle.</summary>
    public void Reset()
    {
        Dragging = false;
        settleTimer.Stop();
    }

    /// <summary>
    /// Safety net: switch to the true foreground window if a hook event was missed; otherwise
    /// refresh the geometry of the current window.
    /// </summary>
    public void SyncForeground()
    {
        try
        {
            nint fg = Native.GetForegroundWindow();
            if (fg != 0 && !isOwnWindow(fg) && fg != Current)
            {
                Current = fg;
                Reset();
                RedrawRequested?.Invoke();
            }
            else if (!Dragging)
            {
                RedrawRequested?.Invoke();
            }
        }
        catch (Exception e)
        {
            Log.Write("SyncForeground error: " + e.Message);
        }
    }

    private void OnForeground(nint hook, uint eventType, nint hwnd, int idObject, int idChild, uint thread, uint time)
    {
        try
        {
            if (idObject != 0)
                return;
            // Re-query: during Alt-Tab the hook hwnd may briefly be the task switcher.
            nint fg = Native.GetForegroundWindow();
            if (fg == 0 || isOwnWindow(fg))
            {
                if (hwnd != 0 && !isOwnWindow(hwnd) && Native.IsWindow(hwnd))
                    fg = hwnd;
                else
                    return;
            }
            Current = fg;
            Reset();
            // Deferred so the window is fully restored and positioned before it is measured.
            foregroundTimer.Stop();
            foregroundTimer.Start();
        }
        catch (Exception e)
        {
            Log.Write("OnForeground error: " + e.Message);
        }
    }

    private void OnLocation(nint hook, uint eventType, nint hwnd, int idObject, int idChild, uint thread, uint time)
    {
        try
        {
            if (idObject != 0 || hwnd != Current)
                return;
            if (!Dragging)
            {
                Dragging = true;
                HideRequested?.Invoke();
            }
            // Reset on every event: Settle runs once movement has stopped for settleMs.
            settleTimer.Stop();
            settleTimer.Start();
        }
        catch (Exception e)
        {
            Log.Write("OnLocation error: " + e.Message);
        }
    }

    private void Settle()
    {
        settleTimer.Stop();
        Dragging = false;
        RedrawRequested?.Invoke();
    }

    public void Dispose()
    {
        Unhook();
        settleTimer.Dispose();
        foregroundTimer.Dispose();
    }
}
