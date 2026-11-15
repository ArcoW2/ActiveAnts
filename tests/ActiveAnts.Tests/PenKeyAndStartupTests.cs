namespace ActiveAnts.Tests;

public sealed class PenKeyTests
{
    private static readonly Settings Base = new();

    private static Settings Change(string what) => what switch
    {
        "color" => Base with { BorderColor = 0xFF00FF00 },
        "width" => Base with { BorderWidth = 5 },
        "dash" => Base with { DashLen = 9 },
        "gap" => Base with { GapLen = 33 },
        "animStep" => Base with { AnimStep = 3 },
        "animMs" => Base with { AnimMs = 50 },
        "settleMs" => Base with { SettleMs = 200 },
        "syncForegroundMs" => Base with { SyncForegroundMs = 1000 },
        "debug" => Base with { DebugEnabled = true },
        _ => throw new ArgumentException(what),
    };

    [Theory]
    [InlineData("color")]
    [InlineData("width")]
    [InlineData("dash")]
    [InlineData("gap")]
    public void Style_change_forces_rebuild(string what) =>
        Assert.NotEqual(PenKey.For(Base, 1f), PenKey.For(Change(what), 1f));

    [Fact]
    public void Dpi_scale_change_forces_rebuild() =>
        Assert.NotEqual(PenKey.For(Base, 1f), PenKey.For(Base, 1.5f));

    [Theory]
    [InlineData("animStep")]
    [InlineData("animMs")]
    [InlineData("settleMs")]
    [InlineData("syncForegroundMs")]
    [InlineData("debug")]
    public void Other_changes_keep_the_pen(string what) =>
        Assert.Equal(PenKey.For(Base, 1.25f), PenKey.For(Change(what), 1.25f));
}

public sealed class StartupTests
{
    private const string Exe = @"C:\Apps\ActiveAnts\ActiveAnts.exe";

    [Fact]
    public void Enabled_for_this_exe() =>
        Assert.True(StartupRegistration.IsEnabledValue("\"" + Exe + "\"", Exe));

    [Fact]
    public void Enabled_is_case_insensitive() =>
        Assert.True(StartupRegistration.IsEnabledValue("\"" + Exe.ToUpperInvariant() + "\"", Exe));

    [Theory]
    [InlineData("\"C:\\Win32App\\AhkScripts\\ActiveWindowDashedBorder\\ActiveWindowDashedBorder.exe\"")]
    [InlineData("\"C:\\Program Files\\AutoHotkey\\v2\\AutoHotkey64.exe\" \"C:\\x\\ActiveWindowDashedBorder.ahk\"")]
    [InlineData("")]
    [InlineData(null)]
    public void Not_enabled_for_another_value(string? value) =>
        Assert.False(StartupRegistration.IsEnabledValue(value, Exe));
}
