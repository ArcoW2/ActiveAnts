namespace ActiveAnts;

/// <summary>Debug log next to the exe, written only when [Debug] enabled=1.</summary>
internal static class Log
{
    public static bool Enabled { get; set; }

    public static string FilePath { get; set; } = "ants-debug.log";

    public static void Write(string message)
    {
        if (!Enabled)
            return;
        try
        {
            File.AppendAllText(FilePath, $"{DateTime.Now:HH:mm:ss} | {message}{Environment.NewLine}");
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
