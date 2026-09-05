using System.Reflection;

namespace KPod.Windows.UI;

/// <summary>Application name, description and version.</summary>
internal sealed class AboutForm : Form
{
    internal AboutForm()
    {
        Text = "About " + Dialogs.AppName;
        Icon = AppIcon.Shared;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;
        AutoScaleMode = AutoScaleMode.Dpi;
        Font = SystemFonts.MessageBoxFont!;
        ClientSize = new Size(400, 230);

        Label title = new()
        {
            Text = Dialogs.AppName + " v" + ShortVersion(),
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter,
            Font = new Font(SystemFonts.MessageBoxFont!.FontFamily, 20f, FontStyle.Bold),
        };

        Label subtitle = new()
        {
            Text = "Terminal Reality POD Archive Viewer and Extractor\r\n"
                 + "by Juan Pablo Utreras \"Kmaster\"\r\n"
                 + "Based on WinPod by MDMRE\r\n\r\n"
                 + "www.mtm2.com",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter,
        };

        Button close = new()
        {
            Text = "Close",
            DialogResult = DialogResult.OK,
            AutoSize = true,
            Anchor = AnchorStyles.None,
        };
        Button notices = new() { Text = "Third-party notices…", AutoSize = true };
        notices.Click += (_, _) =>
        {
            using ThirdPartyNoticesForm form = new();
            form.ShowDialog(this);
        };
        FlowLayoutPanel buttons = new()
        {
            Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false, AutoSize = true, Anchor = AnchorStyles.None,
        };
        buttons.Controls.Add(notices);
        buttons.Controls.Add(close);

        TableLayoutPanel layout = new()
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(16, 12, 16, 8),
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        layout.Controls.Add(title, 0, 0);
        layout.Controls.Add(subtitle, 0, 1);
        layout.Controls.Add(buttons, 0, 2);

        Controls.Add(layout);
        AcceptButton = close;
        CancelButton = close;
    }

    /// <summary>Major and minor only; the full four-part version is in the file properties.</summary>
    private static string ShortVersion()
    {
        Version version = typeof(AboutForm).Assembly.GetName().Version ?? new Version(1, 0);
        return version.Major + "." + version.Minor;
    }
}

internal sealed class ThirdPartyNoticesForm : Form
{
    internal ThirdPartyNoticesForm()
    {
        Text = "Third-party notices";
        Icon = AppIcon.Shared;
        StartPosition = FormStartPosition.CenterParent;
        AutoScaleMode = AutoScaleMode.Dpi;
        Font = SystemFonts.MessageBoxFont!;
        ClientSize = new Size(700, 520);
        MinimumSize = new Size(500, 360);

        TextBox text = new()
        {
            Dock = DockStyle.Fill, Multiline = true, ReadOnly = true,
            ScrollBars = ScrollBars.Both, WordWrap = true,
            Font = new Font(FontFamily.GenericMonospace, 9f), Text = ReadNotices(),
        };
        Button close = new() { Text = "Close", DialogResult = DialogResult.OK, AutoSize = true, Anchor = AnchorStyles.Right };
        Panel footer = new() { Dock = DockStyle.Bottom, Height = 44, Padding = new Padding(6) };
        close.Dock = DockStyle.Right;
        footer.Controls.Add(close);
        Controls.Add(text);
        Controls.Add(footer);
        AcceptButton = close;
        CancelButton = close;
    }

    private static string ReadNotices()
    {
        List<string> notices = [];
        Assembly assembly = typeof(ThirdPartyNoticesForm).Assembly;
        foreach (string resource in assembly.GetManifestResourceNames()
            .Where(name => name.StartsWith("KPod.Windows.Licenses.", StringComparison.Ordinal))
            .OrderBy(name => name, StringComparer.Ordinal))
        {
            using Stream? stream = assembly.GetManifestResourceStream(resource);
            if (stream is null) continue;
            using StreamReader reader = new(stream);
            notices.Add(reader.ReadToEnd());
        }
        notices.AddRange(ReadInstalledAddOnNotices());
        if (notices.Count == 0) return "Third-party notices resources are unavailable.";
        return string.Join(Environment.NewLine + Environment.NewLine, notices);
    }

    /// <summary>
    /// The MOD playback add-on brings its own licenses along with its DLLs, so the notices
    /// describe what is actually installed rather than what the exe was built to allow.
    /// </summary>
    private static IEnumerable<string> ReadInstalledAddOnNotices()
    {
        List<string> notices = [];
        try
        {
            foreach (string file in Directory.GetFiles(AppContext.BaseDirectory, "License.*.txt")
                .OrderBy(file => file, StringComparer.Ordinal))
            {
                notices.Add(File.ReadAllText(file));
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        return notices;
    }
}
