using System.Diagnostics;
using System.Reflection;

namespace ActiveAnts;

/// <summary>About window: name, version, author, project link and the license text.</summary>
internal sealed class AboutForm : Form
{
    public const string ProjectUrl = "https://github.com/ArcoW2/ActiveAnts";

    public AboutForm()
    {
        SuspendLayout();
        AutoScaleDimensions = new SizeF(96f, 96f);
        AutoScaleMode = AutoScaleMode.Dpi;
        Text = "About Active ants";
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        TopMost = true;
        ClientSize = new Size(472, 380);

        Assembly assembly = typeof(AboutForm).Assembly;
        using (Stream? s = assembly.GetManifestResourceStream("ActiveAnts.ant.ico"))
            if (s != null)
            {
                Icon = new Icon(s);
                var picture = new PictureBox
                {
                    Image = new Icon(Icon, 48, 48).ToBitmap(),
                    SizeMode = PictureBoxSizeMode.Zoom,
                    Bounds = new Rectangle(12, 12, 48, 48),
                };
                Controls.Add(picture);
            }

        string version = assembly.GetName().Version?.ToString(3) ?? "";
        string copyright = assembly.GetCustomAttribute<AssemblyCopyrightAttribute>()?.Copyright ?? "";
        var title = new Label
        {
            Text = "Active ants " + version,
            Font = new Font(Font.FontFamily, 12f, FontStyle.Bold),
            Bounds = new Rectangle(72, 12, 388, 24),
        };
        var author = new Label
        {
            Text = "Created by Arco Witter\r\n" + copyright,
            Bounds = new Rectangle(72, 38, 388, 36),
        };
        var link = new LinkLabel { Text = ProjectUrl, Bounds = new Rectangle(72, 76, 388, 20) };
        link.LinkClicked += (_, _) => Process.Start(new ProcessStartInfo(ProjectUrl) { UseShellExecute = true });

        var licenseLabel = new Label { Text = "Released under the MIT License:", Bounds = new Rectangle(12, 106, 448, 20) };
        var license = new TextBox
        {
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            Text = LicenseText(),
            Bounds = new Rectangle(12, 128, 448, 204),
            TabStop = false,
        };
        var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, Bounds = new Rectangle(360, 342, 100, 26) };
        ok.Click += (_, _) => Close();
        AcceptButton = ok;
        CancelButton = ok;

        Controls.AddRange([title, author, link, licenseLabel, license, ok]);
        ResumeLayout(false);
    }

    /// <summary>The LICENSE file, embedded at build time; line breaks normalised for the edit control.</summary>
    internal static string LicenseText()
    {
        using Stream? s = typeof(AboutForm).Assembly.GetManifestResourceStream("ActiveAnts.LICENSE");
        if (s == null)
            return "MIT License";
        using var reader = new StreamReader(s);
        return reader.ReadToEnd().Replace("\r\n", "\n").Replace("\n", "\r\n");
    }
}
