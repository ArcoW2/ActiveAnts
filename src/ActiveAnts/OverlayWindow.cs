namespace ActiveAnts;

/// <summary>
/// One layered, click-through, topmost strip window. A raw window, not a Form: a per-monitor
/// aware Form rescales and moves itself on WM_DPICHANGED, which would break the strip.
/// The host strip (the first one created) also receives the display and power broadcasts and
/// the frame messages of the frame pacer.
/// </summary>
internal sealed class OverlayWindow : NativeWindow, IDisposable
{
    private readonly Action<int, nint, nint>? hostMessage;

    public OverlayWindow(Action<int, nint, nint>? hostMessage)
    {
        this.hostMessage = hostMessage;
        CreateHandle(new CreateParams
        {
            Caption = "ActiveAnts",
            Style = Native.WS_POPUP,
            ExStyle = Native.WS_EX_LAYERED | Native.WS_EX_TRANSPARENT | Native.WS_EX_TOOLWINDOW |
                      Native.WS_EX_TOPMOST | Native.WS_EX_NOACTIVATE,
            Width = 1,
            Height = 1,
        });
    }

    public bool Shown { get; private set; }

    public void ShowNoActivate()
    {
        if (Shown)
            return;
        Native.ShowWindow(Handle, Native.SW_SHOWNOACTIVATE);
        Shown = true;
    }

    public void Hide()
    {
        if (!Shown)
            return;
        Native.ShowWindow(Handle, Native.SW_HIDE);
        Shown = false;
    }

    protected override void WndProc(ref Message m)
    {
        switch (m.Msg)
        {
            case Native.WM_DPICHANGED:
                m.Result = 0;       // swallowed: geometry is physical pixels, set by us only
                return;
            case Native.WM_DISPLAYCHANGE or Native.WM_POWERBROADCAST:
                hostMessage?.Invoke(m.Msg, m.WParam, m.LParam);
                break;
            case FramePacer.WM_FRAME:
                hostMessage?.Invoke(m.Msg, m.WParam, m.LParam);
                m.Result = 0;
                return;
        }
        base.WndProc(ref m);
    }

    public void Dispose() => DestroyHandle();
}
