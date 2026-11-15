using System.Reflection;

namespace ActiveAnts.Tests;

public sealed class AboutTests
{
    [Fact]
    public void License_text_is_embedded()
    {
        string text = AboutForm.LicenseText();
        Assert.StartsWith("MIT License", text);
        Assert.Contains("Copyright (c) 2026 Arco Witter", text);
        Assert.DoesNotContain("\n", text.Replace("\r\n", ""));     // CRLF only, for the edit control
    }

    [Fact]
    public void Assembly_carries_author_and_copyright()
    {
        Assembly a = typeof(AboutForm).Assembly;
        Assert.Equal("Copyright (c) 2026 Arco Witter", a.GetCustomAttribute<AssemblyCopyrightAttribute>()?.Copyright);
        Assert.Equal("Arco Witter", a.GetCustomAttribute<AssemblyCompanyAttribute>()?.Company);
        Assert.Equal("1.0.0", a.GetName().Version?.ToString(3));
    }
}
