using Microsoft.Win32;

namespace ActiveAnts;

/// <summary>
/// Per-user Run key. Value name shared with the AHK version, so enabling it here replaces the
/// AHK startup entry.
/// </summary>
internal static class StartupRegistration
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "MarchingAnts";

    public static string Command(string exePath) => $"\"{exePath}\"";

    /// <summary>True only when the value starts this exe (case-insensitive), not any other one.</summary>
    public static bool IsEnabledValue(string? value, string exePath) =>
        string.Equals(value?.Trim(), Command(exePath), StringComparison.OrdinalIgnoreCase);

    public static bool IsEnabled(string exePath)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        return IsEnabledValue(key?.GetValue(ValueName) as string, exePath);
    }

    public static void Set(bool enable, string exePath)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey, writable: true);
        if (enable)
            key.SetValue(ValueName, Command(exePath), RegistryValueKind.String);
        else
            key.DeleteValue(ValueName, throwOnMissingValue: false);
    }
}
