namespace ActiveAnts;

/// <summary>Config window: same groups and fields as the AHK version, with range checks.</summary>
internal sealed class ConfigForm : Form
{
    private readonly Func<Settings, bool, IWin32Window, bool> apply;
    private readonly Func<IWin32Window, bool> save;
    private readonly TextBox color;
    private readonly NumberBox width, dash, gap, step, animMs, settle, sync;
    private readonly CheckBox clockwise, debug, startup;

    /// <param name="apply">Takes the new settings and the startup choice; false keeps the window open.</param>
    /// <param name="save">Writes the INI; false keeps the window open.</param>
    public ConfigForm(Settings settings, bool runAtStartup,
                      Func<Settings, bool, IWin32Window, bool> apply, Func<IWin32Window, bool> save)
    {
        this.apply = apply;
        this.save = save;

        SuspendLayout();
        AutoScaleDimensions = new SizeF(96f, 96f);
        AutoScaleMode = AutoScaleMode.Dpi;
        Text = "Active ants - Config";
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        TopMost = true;
        ClientSize = new Size(472, 288);
        using (Stream? s = typeof(ConfigForm).Assembly.GetManifestResourceStream("ActiveAnts.ant.ico"))
            if (s != null)
                Icon = new Icon(s);

        var style = Group("Style", 10, 8, 220, 156);
        color = Field(style, 0, "Border color (ARGB):", new TextBox(), $"0x{settings.BorderColor:X8}");
        width = Field(style, 1, "Border width (px):", new NumberBox(), settings.BorderWidth.ToString());
        dash = Field(style, 2, "Dash length (px):", new NumberBox(), settings.DashLen.ToString());
        gap = Field(style, 3, "Gap length (px):", new NumberBox(), settings.GapLen.ToString());

        var anim = Group("Animation", 242, 8, 220, 156);
        step = Field(anim, 0, "Anim step (px/tick):", new NumberBox(), settings.AnimStep.ToString());
        animMs = Field(anim, 1, "Anim interval (ms):", new NumberBox(), settings.AnimMs.ToString());
        settle = Field(anim, 2, "Settle delay (ms):", new NumberBox(), settings.SettleMs.ToString());
        sync = Field(anim, 3, "Foreground sync (ms):", new NumberBox(), settings.SyncForegroundMs.ToString());
        clockwise = new CheckBox { Text = "Walk clockwise", Location = new Point(12, 126), AutoSize = true, Checked = settings.Clockwise };
        anim.Controls.Add(clockwise);

        var general = Group("General", 10, 172, 220, 72);
        debug = new CheckBox { Text = "Enable debug logging", Location = new Point(12, 20), AutoSize = true, Checked = settings.DebugEnabled };
        startup = new CheckBox { Text = "Run at Windows startup", Location = new Point(12, 44), AutoSize = true, Checked = runAtStartup };
        general.Controls.AddRange([debug, startup]);

        var applyButton = new Button { Text = "Apply", Bounds = new Rectangle(10, 254, 100, 26) };
        var saveButton = new Button { Text = "Save && Close", Bounds = new Rectangle(122, 254, 100, 26) };
        applyButton.Click += (_, _) => Apply();
        saveButton.Click += (_, _) => SaveAndClose();
        Controls.AddRange([applyButton, saveButton]);
        ResumeLayout(false);
    }

    private bool Apply() => TryRead(out Settings? s) && apply(s!, startup.Checked, this);

    private void SaveAndClose()
    {
        if (Apply() && save(this))
            Close();
    }

    private bool TryRead(out Settings? settings)
    {
        settings = null;
        if (!Settings.TryParseColor(color.Text, out uint c))
            return Error("Border color (ARGB) must be a 32-bit hex value, for example 0xFFFFFF00.");
        if (!ReadInt(width, "Border width", Settings.MinBorderWidth, out int w) ||
            !ReadInt(dash, "Dash length", Settings.MinDashLen, out int d) ||
            !ReadInt(gap, "Gap length", Settings.MinGapLen, out int g) ||
            !ReadInt(step, "Anim step", Settings.MinAnimStep, out int st) ||
            !ReadInt(animMs, "Anim interval", Settings.MinAnimMs, out int am) ||
            !ReadInt(settle, "Settle delay", Settings.MinSettleMs, out int se) ||
            !ReadInt(sync, "Foreground sync", Settings.MinSyncForegroundMs, out int sy))
            return false;

        settings = new Settings
        {
            BorderColor = c,
            BorderWidth = w,
            DashLen = d,
            GapLen = g,
            AnimStep = st,
            AnimMs = am,
            SettleMs = se,
            SyncForegroundMs = sy,
            Clockwise = clockwise.Checked,
            DebugEnabled = debug.Checked,
        };
        return true;
    }

    private bool ReadInt(TextBox box, string name, int min, out int value)
    {
        if (Settings.TryParseInt(box.Text, out value) && value >= min)
            return true;
        box.Focus();
        return Error($"{name} must be a whole number of at least {min}.");
    }

    private bool Error(string message)
    {
        MessageBox.Show(this, message, "Config", MessageBoxButtons.OK, MessageBoxIcon.Error);
        return false;
    }

    private GroupBox Group(string text, int x, int y, int w, int h)
    {
        var box = new GroupBox { Text = text, Bounds = new Rectangle(x, y, w, h) };
        Controls.Add(box);
        return box;
    }

    private static T Field<T>(GroupBox group, int row, string label, T box, string value) where T : TextBox
    {
        int y = 22 + row * 26;
        group.Controls.Add(new Label { Text = label, Bounds = new Rectangle(12, y + 3, 116, 20) });
        box.Bounds = new Rectangle(128, y, 80, 23);
        box.Text = value;
        group.Controls.Add(box);
        return box;
    }

    /// <summary>Edit that accepts digits only, like AHK's Number option.</summary>
    private sealed class NumberBox : TextBox
    {
        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.Style |= Native.ES_NUMBER;
                return cp;
            }
        }
    }
}
