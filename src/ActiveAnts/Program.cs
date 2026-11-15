namespace ActiveAnts;

internal static class Program
{
    // Shared with the AHK version, so the two exclude each other.
    private const string MutexName = @"Global\MarchingAntsOverlay";

    [STAThread]
    private static void Main()
    {
        using var mutex = new Mutex(false, MutexName);
        bool owned;
        try
        {
            // Waits for an exiting instance during Reload; times out if one is really running.
            owned = mutex.WaitOne(3000);
        }
        catch (AbandonedMutexException)
        {
            owned = true;
        }
        if (!owned)
            return;

        // .NET mutexes are thread-affine: acquire and release on this (the UI) thread.
        void Release()
        {
            if (!owned)
                return;
            mutex.ReleaseMutex();
            owned = false;
        }

        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        using (var context = new TrayContext(Release))
            Application.Run(context);
        Release();
    }
}
