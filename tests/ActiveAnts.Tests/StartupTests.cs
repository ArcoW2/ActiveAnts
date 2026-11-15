namespace ActiveAnts.Tests;

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
