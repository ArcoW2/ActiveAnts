using System.Diagnostics;
using System.Runtime.InteropServices;

namespace ActiveAnts;

/// <summary>
/// Paces animation frames on the compositor (display refresh) instead of a timer. A background
/// thread waits for each compositor tick and, when about IntervalMs has passed, posts a frame
/// message with the tick's timestamp to the host window. Frames therefore land on refreshes,
/// evenly spaced, whatever the refresh rate is. Waiting costs no CPU; while stopped the thread
/// blocks on an event.
/// </summary>
internal sealed unsafe class FramePacer : IDisposable
{
    public const int WM_FRAME = 0x8000 + 1;     // WM_APP + 1; lParam = Stopwatch timestamp

    private const uint WaitTimeoutMs = 100;     // keeps the loop alive if the compositor goes idle

    private readonly nint host;
    private readonly ManualResetEventSlim running = new(false);
    private readonly delegate* unmanaged[Stdcall]<uint, nint*, uint, uint> waitForCompositorClock;
    private Thread? thread;
    private volatile bool disposed;
    private volatile int intervalMs;
    private int pending;                         // 1 while a posted frame is not yet handled

    public FramePacer(nint host, int intervalMs)
    {
        this.host = host;
        this.intervalMs = intervalMs;
        // Windows 11; on Windows 10 fall back to DwmFlush.
        if (NativeLibrary.TryLoad("dcomp.dll", out nint dcomp) &&
            NativeLibrary.TryGetExport(dcomp, "DCompositionWaitForCompositorClock", out nint export))
            waitForCompositorClock = (delegate* unmanaged[Stdcall]<uint, nint*, uint, uint>)export;
        Log.Write("FramePacer: " + (waitForCompositorClock != null ? "compositor clock" : "DwmFlush"));
        EnsureThread();
    }

    public int IntervalMs
    {
        set => intervalMs = value;
    }

    public bool Running => running.IsSet;

    public void Start() => running.Set();

    public void Stop() => running.Reset();

    /// <summary>Called on the UI thread when a frame message is handled.</summary>
    public void FrameHandled() => Volatile.Write(ref pending, 0);

    /// <summary>Watchdog recovery: restarts the thread if it died.</summary>
    public void EnsureThread()
    {
        if (thread is { IsAlive: true })
            return;
        Volatile.Write(ref pending, 0);
        thread = new Thread(Loop) { IsBackground = true, Name = "ActiveAnts frame pacer" };
        thread.Start();
    }

    private void Loop()
    {
        long lastFrame = 0, lastTick = 0;
        double tickMs = 0;                       // measured time between compositor ticks
        while (!disposed)
        {
            try
            {
                if (!running.IsSet)
                {
                    running.Wait();
                    lastFrame = lastTick = 0;    // resume: first tick gives a frame at once
                    continue;
                }
                WaitForCompositor();
                long now = Stopwatch.GetTimestamp();
                if (lastTick != 0)
                {
                    double d = Stopwatch.GetElapsedTime(lastTick, now).TotalMilliseconds;
                    if (d < WaitTimeoutMs)
                        tickMs = tickMs == 0 ? d : tickMs * 0.9 + d * 0.1;
                }
                lastTick = now;

                // Frame on the first tick at or after the interval (a quarter tick of slack for
                // tick jitter): never more frames than animMs asks for.
                if (lastFrame != 0 && Stopwatch.GetElapsedTime(lastFrame, now).TotalMilliseconds < intervalMs - tickMs / 4)
                    continue;
                // UI thread still busy with the previous frame: skip, never queue up.
                if (Interlocked.Exchange(ref pending, 1) == 1)
                    continue;
                lastFrame = now;
                if (!Native.PostMessage(host, WM_FRAME, 0, (nint)now))
                    Volatile.Write(ref pending, 0);
            }
            catch (Exception e)
            {
                Log.Write("FramePacer error: " + e.Message);
                Thread.Sleep(100);
            }
        }
    }

    private void WaitForCompositor()
    {
        if (waitForCompositorClock != null)
        {
            waitForCompositorClock(0, null, WaitTimeoutMs);
            return;
        }
        long start = Stopwatch.GetTimestamp();
        if (Native.DwmFlush() != 0 || Stopwatch.GetElapsedTime(start).TotalMilliseconds < 1)
            Thread.Sleep(1);                     // no composition to wait for: do not spin
    }

    public void Dispose()
    {
        disposed = true;
        running.Set();
        thread?.Join(500);
        running.Dispose();
    }
}
