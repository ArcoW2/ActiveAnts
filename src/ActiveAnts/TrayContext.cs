using System.Diagnostics;
using Timer = System.Windows.Forms.Timer;

namespace ActiveAnts;

/// <summary>
/// The app: tray icon, timers, redraw, watchdog, restart and exit cleanup. Everything runs on
/// the UI thread.
/// </summary>
internal sealed class TrayContext : ApplicationContext
{
    private const int MaxConsecutiveErrors = 10;

    private readonly string exePath = Environment.ProcessPath!;
    private readonly string iniPath;
    private readonly Action releaseMutex;
    private readonly NotifyIcon tray;
    private readonly BorderRenderer renderer;
    private readonly WindowTracker tracker;
    private readonly Timer animTimer = new();
    private readonly Timer syncTimer = new();
    private readonly Timer watchdogTimer = new() { Interval = 4000 };
    private readonly Timer displayTimer = new() { Interval = 1500 };
    private readonly Timer rehookTimer = new();
    private Settings settings;
    private ConfigForm? configForm;
    private readonly Stopwatch animClock = new();    // runs while the animation runs
    private float dashStart;                         // offset when animClock was last reset
    private int consecutiveErrors;
    private long lastAnimTick;
    private bool restarting;

    public TrayContext(Action releaseMutex)
    {
        this.releaseMutex = releaseMutex;
        iniPath = Settings.IniPathFor(exePath);
        Log.FilePath = Path.Combine(Path.GetDirectoryName(exePath)!, "ants-debug.log");

        var problems = new List<string>();
        settings = Settings.Load(iniPath, problems);
        Log.Enabled = settings.DebugEnabled;
        Log.Write("START " + exePath);
        foreach (string p in problems)
            Log.Write("INI: " + p);
        LogDpiAwareness();

        renderer = new BorderRenderer(BorderStyle.From(settings), OnHostMessage);
        tracker = new WindowTracker(renderer.Owns, settings.SettleMs);
        tracker.RedrawRequested += Redraw;
        tracker.HideRequested += HideBorder;

        tray = CreateTray();

        animTimer.Interval = settings.AnimMs;
        syncTimer.Interval = settings.SyncForegroundMs;
        animTimer.Tick += (_, _) => Animate();
        syncTimer.Tick += (_, _) => tracker.SyncForeground();
        watchdogTimer.Tick += (_, _) => Watchdog();
        displayTimer.Tick += (_, _) => DisplayChanged();
        rehookTimer.Tick += (_, _) => Rehook();

        // A zero hook handle is picked up by the watchdog, which rehooks.
        if (!tracker.Hook())
            Log.Write("SetWinEventHook failed at startup");

        tracker.Current = Native.GetForegroundWindow();
        Redraw();
        syncTimer.Start();
        watchdogTimer.Start();
    }

    private NotifyIcon CreateTray()
    {
        var menu = new ContextMenuStrip();
        var config = new ToolStripMenuItem("Config...", null, (_, _) => ShowConfig());
        config.Font = new Font(config.Font, FontStyle.Bold);    // default item
        menu.Items.Add(config);
        menu.Items.Add("Reload", null, (_, _) => Restart("tray Reload"));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => ExitThread());

        using Stream s = typeof(TrayContext).Assembly.GetManifestResourceStream("ActiveAnts.ant.ico")!;
        var icon = new NotifyIcon
        {
            Icon = new Icon(s, SystemInformation.SmallIconSize),
            Text = "Active ants",
            ContextMenuStrip = menu,
            Visible = true,
        };
        icon.DoubleClick += (_, _) => ShowConfig();
        return icon;
    }

    private static void LogDpiAwareness()
    {
        nint context = Native.GetThreadDpiAwarenessContext();
        bool perMonitor = Native.GetAwarenessFromDpiAwarenessContext(context) == Native.DPI_AWARENESS_PER_MONITOR_AWARE;
        bool v2 = Native.AreDpiAwarenessContextsEqual(context, Native.DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2);
        Log.Write($"DPI awareness: per-monitor={perMonitor} v2={v2}");
    }

    // ---- drawing ----

    /// <summary>Full redraw: recompute the geometry of the tracked window.</summary>
    private void Redraw()
    {
        try
        {
            nint hwnd = tracker.Current;
            if (hwnd == 0 || !Native.IsWindow(hwnd) || renderer.Owns(hwnd) ||
                Native.IsIconic(hwnd) || WindowGeometry.IsFullScreen(hwnd))
            {
                HideBorder();
                return;
            }

            RECT frame = WindowGeometry.VisibleRect(hwnd);
            if (frame.Width <= 0 || frame.Height <= 0)
                return;

            List<StripPiece> pieces = StripLayout.Pieces(frame, settings.BorderWidth, WindowGeometry.Monitors());
            if (pieces.Count == 0)
            {
                HideBorder();
                return;
            }

            renderer.Show(frame, pieces, DashOffset());
            lastAnimTick = Environment.TickCount64;     // arm the watchdog on first show
            if (!animTimer.Enabled)
                animTimer.Start();
            animClock.Start();
            consecutiveErrors = 0;
        }
        catch (Exception e)
        {
            CountError("Redraw", e);
        }
    }

    /// <summary>Animation frame: geometry is known, only the dash offset moves.</summary>
    private void Animate()
    {
        lastAnimTick = Environment.TickCount64;     // heartbeat for the watchdog
        if (tracker.Dragging || !renderer.Visible)
            return;
        try
        {
            if (!Native.IsWindow(tracker.Current))
            {
                HideBorder();
                return;
            }
            renderer.Animate(DashOffset());
            consecutiveErrors = 0;
        }
        catch (Exception e)
        {
            CountError("Animate", e);
        }
    }

    private float DashOffset() =>
        WindowGeometry.DashOffsetAt(dashStart, animClock.Elapsed.TotalMilliseconds,
                                    settings.Clockwise ? -settings.AnimStep : settings.AnimStep,
                                    settings.AnimMs, settings.DashLen, settings.GapLen);

    private void HideBorder()
    {
        animTimer.Stop();       // no work while nothing is shown
        animClock.Stop();       // the pattern stays put while hidden or dragging
        renderer.Hide();
    }

    private void CountError(string where, Exception e)
    {
        consecutiveErrors++;
        Log.Write($"{where} error #{consecutiveErrors}: {e.Message}");
        if (consecutiveErrors >= MaxConsecutiveErrors)
            Restart("too many consecutive errors");
    }

    // ---- resilience ----

    private void OnHostMessage(int msg, nint wParam)
    {
        if (msg == Native.WM_DISPLAYCHANGE)
        {
            // Debounced: a dock or monitor change sends a burst.
            displayTimer.Stop();
            displayTimer.Start();
        }
        else if (msg == Native.WM_POWERBROADCAST &&
                 (wParam == Native.PBT_APMRESUMESUSPEND || wParam == Native.PBT_APMRESUMEAUTOMATIC))
        {
            // Hooks can go stale after hibernation; give the system time to settle.
            ScheduleRehook(2000);
        }
    }

    private void DisplayChanged()
    {
        displayTimer.Stop();
        Log.Write("Display change: refreshing geometry");
        Redraw();
    }

    private void ScheduleRehook(int delayMs)
    {
        rehookTimer.Stop();
        rehookTimer.Interval = delayMs;
        rehookTimer.Start();
    }

    private void Rehook()
    {
        rehookTimer.Stop();
        tracker.Unhook();
        if (!tracker.Hook())
        {
            Restart("Rehook: SetWinEventHook failed");
            return;
        }
        Log.Write("Rehook: hooks re-registered");
        tracker.Reset();
        RestartTimer(animTimer, settings.AnimMs);
        RestartTimer(syncTimer, settings.SyncForegroundMs);
        tracker.Current = Native.GetForegroundWindow();
        Redraw();
    }

    private void Watchdog()
    {
        if (!renderer.WindowsAlive())
        {
            Restart("Watchdog: overlay window gone");
            return;
        }
        if (!tracker.HooksValid)
        {
            Log.Write("Watchdog: hook handle is zero - rehooking");
            ScheduleRehook(100);
            return;
        }
        if (renderer.Visible && lastAnimTick > 0 && Environment.TickCount64 - lastAnimTick > settings.AnimMs * 10L)
        {
            Log.Write("Watchdog: animation frozen - restarting timers");
            RestartTimer(animTimer, settings.AnimMs);
            RestartTimer(syncTimer, settings.SyncForegroundMs);
        }
    }

    private static void RestartTimer(Timer timer, int interval)
    {
        timer.Stop();
        timer.Interval = interval;
        timer.Start();
    }

    /// <summary>Release the mutex first so the new instance takes over without waiting.</summary>
    private void Restart(string reason)
    {
        if (restarting)
            return;
        restarting = true;
        Log.Write("Restart: " + reason);
        releaseMutex();
        try
        {
            Process.Start(new ProcessStartInfo(exePath) { UseShellExecute = false });
        }
        catch (Exception e)
        {
            Log.Write("Restart failed: " + e.Message);
        }
        ExitThread();
    }

    // ---- config ----

    private void ShowConfig()
    {
        if (configForm != null)
        {
            configForm.Activate();
            return;
        }
        configForm = new ConfigForm(settings, StartupRegistration.IsEnabled(exePath), Apply, Save);
        configForm.FormClosed += (_, _) => configForm = null;
        configForm.Show();
    }

    private bool Apply(Settings newSettings, bool runAtStartup, IWin32Window owner)
    {
        // Keep the pattern where it is; the new speed applies from here.
        dashStart = DashOffset() % (newSettings.DashLen + newSettings.GapLen);
        bool animating = animClock.IsRunning;
        animClock.Reset();
        if (animating)
            animClock.Start();
        settings = newSettings;
        Log.Enabled = settings.DebugEnabled;
        renderer.SetStyle(BorderStyle.From(settings));
        tracker.SettleMs = settings.SettleMs;
        RestartTimer(animTimer, settings.AnimMs);
        RestartTimer(syncTimer, settings.SyncForegroundMs);

        bool ok = true;
        // Only touch the registry when the desired state differs.
        if (runAtStartup != StartupRegistration.IsEnabled(exePath))
        {
            try
            {
                StartupRegistration.Set(runAtStartup, exePath);
            }
            catch (Exception e)
            {
                Log.Write("SetStartup error: " + e.Message);
                MessageBox.Show(owner, "Cannot change the Windows startup entry:\n" + e.Message, "Config",
                                MessageBoxButtons.OK, MessageBoxIcon.Error);
                ok = false;
            }
        }
        Redraw();
        return ok;
    }

    private bool Save(IWin32Window owner)
    {
        try
        {
            settings.Save(iniPath);
            return true;
        }
        catch (Exception e)
        {
            Log.Write("Save error: " + e.Message);
            MessageBox.Show(owner, $"Cannot save settings to {iniPath}:\n{e.Message}", "Config",
                            MessageBoxButtons.OK, MessageBoxIcon.Error);
            return false;
        }
    }

    // ---- exit ----

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            Log.Write("EXIT");
            configForm?.Dispose();
            foreach (Timer t in new[] { animTimer, syncTimer, watchdogTimer, displayTimer, rehookTimer })
                t.Dispose();
            tracker.Dispose();
            renderer.Dispose();
            tray.Visible = false;
            tray.ContextMenuStrip?.Dispose();
            tray.Dispose();
        }
        base.Dispose(disposing);
    }
}
