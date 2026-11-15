using System.ComponentModel;
using System.Globalization;
using System.Runtime.InteropServices;

namespace ActiveAnts;

/// <summary>
/// Settings in &lt;exe name&gt;.ini next to the exe. Same sections and keys as the AHK
/// marching-ants.ini; read and written through the Win32 private-profile API, so the
/// UTF-16 file written by the AHK version reads as-is.
/// </summary>
internal sealed record Settings
{
    public const int MinEdgeWidth = 0;          // 0: that edge is not drawn
    public const int MaxEdgeWidth = 10;
    public const int MinDashLen = 1;
    public const int MinGapLen = 1;
    public const int MinAnimStep = 0;
    public const int MinAnimMs = 10;
    public const int MinSettleMs = 0;
    public const int MinSyncForegroundMs = 100;

    public uint BorderColor { get; init; } = 0xFFFFFF00;
    public int TopWidth { get; init; } = 4;
    public int RightWidth { get; init; } = 4;
    public int BottomWidth { get; init; } = 4;
    public int LeftWidth { get; init; } = 4;
    public int DashLen { get; init; } = 8;
    public int GapLen { get; init; } = 32;
    public int AnimStep { get; init; } = 2;
    public int AnimMs { get; init; } = 40;
    public int SettleMs { get; init; } = 120;
    public int SyncForegroundMs { get; init; } = 500;
    public bool Clockwise { get; init; }      // false: counterclockwise, as the AHK version
    public bool DebugEnabled { get; init; }

    public static string IniPathFor(string exePath) => Path.ChangeExtension(exePath, ".ini");

    /// <summary>
    /// Missing file or key gives the default. An invalid or out-of-range value also gives the
    /// default, and is reported in <paramref name="problems"/>.
    /// </summary>
    public static Settings Load(string iniPath, List<string> problems)
    {
        var d = new Settings();
        string color = Read(iniPath, "Style", "borderColor");
        uint borderColor = d.BorderColor;
        if (color.Length > 0 && !TryParseColor(color, out borderColor))
        {
            problems.Add($"[Style] borderColor={color} is not a 32-bit hex value; using default");
            borderColor = d.BorderColor;
        }

        // borderWidth (AHK, one width for all edges) is the default for edges without their own key.
        int width = ReadInt(iniPath, "Style", "borderWidth", d.TopWidth, 1, problems, MaxEdgeWidth);
        return new Settings
        {
            BorderColor = borderColor,
            TopWidth = ReadInt(iniPath, "Style", "topWidth", width, MinEdgeWidth, problems, MaxEdgeWidth),
            RightWidth = ReadInt(iniPath, "Style", "rightWidth", width, MinEdgeWidth, problems, MaxEdgeWidth),
            BottomWidth = ReadInt(iniPath, "Style", "bottomWidth", width, MinEdgeWidth, problems, MaxEdgeWidth),
            LeftWidth = ReadInt(iniPath, "Style", "leftWidth", width, MinEdgeWidth, problems, MaxEdgeWidth),
            DashLen = ReadInt(iniPath, "Style", "dashLen", d.DashLen, MinDashLen, problems),
            GapLen = ReadInt(iniPath, "Style", "gapLen", d.GapLen, MinGapLen, problems),
            AnimStep = ReadInt(iniPath, "Anim", "animStep", d.AnimStep, MinAnimStep, problems),
            AnimMs = ReadInt(iniPath, "Anim", "animMs", d.AnimMs, MinAnimMs, problems),
            SettleMs = ReadInt(iniPath, "Anim", "settleMs", d.SettleMs, MinSettleMs, problems),
            SyncForegroundMs = ReadInt(iniPath, "Anim", "syncForegroundMs", d.SyncForegroundMs, MinSyncForegroundMs, problems),
            Clockwise = ReadInt(iniPath, "Anim", "clockwise", 0, 0, problems) != 0,
            DebugEnabled = ReadInt(iniPath, "Debug", "enabled", 0, 0, problems) != 0,
        };
    }

    /// <summary>Writes every key. Throws <see cref="Win32Exception"/> when the file cannot be written.</summary>
    public void Save(string iniPath)
    {
        Write(iniPath, "Style", "borderColor", $"0x{BorderColor:X8}");
        Write(iniPath, "Style", "topWidth", TopWidth);
        Write(iniPath, "Style", "rightWidth", RightWidth);
        Write(iniPath, "Style", "bottomWidth", BottomWidth);
        Write(iniPath, "Style", "leftWidth", LeftWidth);
        Write(iniPath, "Style", "dashLen", DashLen);
        Write(iniPath, "Style", "gapLen", GapLen);
        Write(iniPath, "Anim", "animStep", AnimStep);
        Write(iniPath, "Anim", "animMs", AnimMs);
        Write(iniPath, "Anim", "settleMs", SettleMs);
        Write(iniPath, "Anim", "syncForegroundMs", SyncForegroundMs);
        Write(iniPath, "Anim", "clockwise", Clockwise ? 1 : 0);
        Write(iniPath, "Debug", "enabled", DebugEnabled ? 1 : 0);
    }

    /// <summary>32-bit hex, with or without a 0x prefix.</summary>
    public static bool TryParseColor(string text, out uint value)
    {
        text = text.Trim();
        if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            text = text[2..];
        return uint.TryParse(text, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out value);
    }

    /// <summary>Decimal, or hex with a 0x prefix (as AHK's Integer() accepts).</summary>
    public static bool TryParseInt(string text, out int value)
    {
        text = text.Trim();
        if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            return int.TryParse(text[2..], NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out value);
        return int.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out value);
    }

    private static int ReadInt(string iniPath, string section, string key, int defaultValue, int min, List<string> problems,
                               int max = int.MaxValue)
    {
        string text = Read(iniPath, section, key);
        if (text.Length == 0)
            return defaultValue;
        if (!TryParseInt(text, out int value))
        {
            problems.Add($"[{section}] {key}={text} is not a number; using default {defaultValue}");
            return defaultValue;
        }
        if (value < min)
        {
            problems.Add($"[{section}] {key}={value} is below the minimum {min}; using default {defaultValue}");
            return defaultValue;
        }
        if (value > max)
        {
            problems.Add($"[{section}] {key}={value} is above the maximum {max}; using default {defaultValue}");
            return defaultValue;
        }
        return value;
    }

    private static unsafe string Read(string iniPath, string section, string key)
    {
        char* buffer = stackalloc char[256];
        uint length = Native.GetPrivateProfileString(section, key, "", buffer, 256, iniPath);
        return new string(buffer, 0, (int)length).Trim();
    }

    private static void Write(string iniPath, string section, string key, object value)
    {
        string text = Convert.ToString(value, CultureInfo.InvariantCulture)!;
        if (!Native.WritePrivateProfileString(section, key, text, iniPath))
            throw new Win32Exception(Marshal.GetLastPInvokeError(), $"Cannot write {iniPath}");
    }
}
