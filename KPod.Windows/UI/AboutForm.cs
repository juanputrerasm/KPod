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
        layout.Controls.Add(close, 0, 2);

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
