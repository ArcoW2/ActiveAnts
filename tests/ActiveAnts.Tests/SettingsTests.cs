namespace ActiveAnts.Tests;

public sealed class SettingsTests : IDisposable
{
    private readonly string dir = Directory.CreateTempSubdirectory("ActiveAntsTests").FullName;

    public void Dispose() => Directory.Delete(dir, recursive: true);

    private string Ini(string content)
    {
        string path = Path.Combine(dir, "test.ini");
        File.WriteAllText(path, content);
        return path;
    }

    [Fact]
    public void Defaults_when_file_missing()
    {
        var problems = new List<string>();
        Settings s = Settings.Load(Path.Combine(dir, "missing.ini"), problems);

        Assert.Equal(new Settings(), s);
        Assert.Equal(0xFFFFFF00u, s.BorderColor);
        Assert.Equal(4, s.BorderWidth);
        Assert.Equal(8, s.DashLen);
        Assert.Equal(32, s.GapLen);
        Assert.Equal(2, s.AnimStep);
        Assert.Equal(40, s.AnimMs);
        Assert.Equal(120, s.SettleMs);
        Assert.Equal(500, s.SyncForegroundMs);
        Assert.False(s.Clockwise);
        Assert.False(s.DebugEnabled);
        Assert.Empty(problems);
    }

    [Fact]
    public void Defaults_per_missing_key()
    {
        var problems = new List<string>();
        Settings s = Settings.Load(Ini("[Style]\r\nborderWidth=7\r\n[Anim]\r\nsettleMs=300\r\n"), problems);

        Assert.Equal(new Settings { BorderWidth = 7, SettleMs = 300 }, s);
        Assert.Empty(problems);
    }

    [Theory]
    [InlineData("Style", "borderWidth", Settings.MinBorderWidth)]
    [InlineData("Style", "dashLen", Settings.MinDashLen)]
    [InlineData("Style", "gapLen", Settings.MinGapLen)]
    [InlineData("Anim", "animStep", Settings.MinAnimStep)]
    [InlineData("Anim", "animMs", Settings.MinAnimMs)]
    [InlineData("Anim", "settleMs", Settings.MinSettleMs)]
    [InlineData("Anim", "syncForegroundMs", Settings.MinSyncForegroundMs)]
    public void Range_bound_at_minimum_accepted_below_falls_back(string section, string key, int min)
    {
        var problems = new List<string>();
        Settings atMin = Settings.Load(Ini($"[{section}]\r\n{key}={min}\r\n"), problems);
        Assert.Equal(min, Get(atMin, key));
        Assert.Empty(problems);

        Settings below = Settings.Load(Ini($"[{section}]\r\n{key}={min - 1}\r\n"), problems);
        Assert.Equal(Get(new Settings(), key), Get(below, key));
        Assert.Single(problems);
    }

    private static int Get(Settings s, string key) => key switch
    {
        "borderWidth" => s.BorderWidth,
        "dashLen" => s.DashLen,
        "gapLen" => s.GapLen,
        "animStep" => s.AnimStep,
        "animMs" => s.AnimMs,
        "settleMs" => s.SettleMs,
        "syncForegroundMs" => s.SyncForegroundMs,
        _ => throw new ArgumentException(key),
    };

    [Theory]
    [InlineData("red")]
    [InlineData("0xFFFFFFFFF")]
    [InlineData("0x")]
    public void Invalid_hex_color_falls_back(string value)
    {
        var problems = new List<string>();
        Settings s = Settings.Load(Ini($"[Style]\r\nborderColor={value}\r\n"), problems);

        Assert.Equal(0xFFFFFF00u, s.BorderColor);
        Assert.Single(problems);
    }

    [Fact]
    public void Non_number_falls_back()
    {
        var problems = new List<string>();
        Settings s = Settings.Load(Ini("[Style]\r\nborderWidth=wide\r\n"), problems);

        Assert.Equal(4, s.BorderWidth);
        Assert.Single(problems);
    }

    [Fact]
    public void Round_trip_of_every_key()
    {
        var original = new Settings
        {
            BorderColor = 0x80FF0010,
            BorderWidth = 3,
            DashLen = 11,
            GapLen = 13,
            AnimStep = 0,
            AnimMs = 17,
            SettleMs = 0,
            SyncForegroundMs = 750,
            Clockwise = true,
            DebugEnabled = true,
        };
        string path = Path.Combine(dir, "roundtrip.ini");
        original.Save(path);

        var problems = new List<string>();
        Assert.Equal(original, Settings.Load(path, problems));
        Assert.Empty(problems);
        Assert.Contains("borderColor=0x80FF0010", File.ReadAllText(path));
    }

    [Fact]
    public void Reads_the_existing_utf16_ahk_ini()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "TestData", "marching-ants.ini");
        byte[] bytes = File.ReadAllBytes(path);
        Assert.Equal([0xFF, 0xFE], bytes[..2]);     // UTF-16 LE with BOM, as written by AHK

        var problems = new List<string>();
        Assert.Equal(new Settings(), Settings.Load(path, problems));
        Assert.Empty(problems);
    }

    [Fact]
    public void Save_to_non_writable_location_throws()
    {
        string path = Path.Combine(dir, "no-such-dir", "x.ini");
        Assert.ThrowsAny<Exception>(() => new Settings().Save(path));
    }

    [Theory]
    [InlineData(@"C:\Apps\ActiveAnts.exe", @"C:\Apps\ActiveAnts.ini")]
    [InlineData(@"D:\Tools\Ants2.exe", @"D:\Tools\Ants2.ini")]
    public void Ini_name_derived_from_exe_name(string exe, string ini) =>
        Assert.Equal(ini, Settings.IniPathFor(exe));

    [Theory]
    [InlineData("0xFFFFFF00", 0xFFFFFF00u)]
    [InlineData("ffffff00", 0xFFFFFF00u)]
    [InlineData(" 0X1 ", 1u)]
    public void Color_parsing(string text, uint expected)
    {
        Assert.True(Settings.TryParseColor(text, out uint value));
        Assert.Equal(expected, value);
    }
}
