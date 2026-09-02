using KPod.Core.Session;

namespace KPod.Windows.UI;

/// <summary>
/// Output folder, file name and optional 80-character comment for a new archive.
/// </summary>
internal sealed class BuildArchiveForm : Form
{
    private readonly TextBox _name = new() { Dock = DockStyle.Fill };
    private readonly TextBox _comment = new() { Dock = DockStyle.Fill, MaxLength = 79 };
    private readonly TextBox _folder = new() { Dock = DockStyle.Fill };

    internal BuildArchiveForm(PodSession session)
    {
        Text = "Make Archive";
        Icon = AppIcon.Shared;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;
        AutoScaleMode = AutoScaleMode.Dpi;
        Font = SystemFonts.MessageBoxFont!;
        ClientSize = new Size(560, 190);

        _folder.Text = session.SourceFolderPath ?? string.Empty;
        _comment.Text = session.ArchiveComment;

        Button browse = new() { Text = "Browse...", AutoSize = true, Dock = DockStyle.Right };
        browse.Click += (_, _) =>
        {
            string? chosen = FolderPicker.Choose(this, "Choose Output Folder", _folder.Text);
            if (chosen is not null)
            {
                _folder.Text = chosen;
            }
        };

        Button ok = new() { Text = "Build", AutoSize = true, Margin = new Padding(4, 0, 0, 0) };
        Button cancel = new()
        {
            Text = "Cancel",
            DialogResult = DialogResult.Cancel,
            AutoSize = true,
            Margin = new Padding(4, 0, 0, 0),
        };
        ok.Click += (_, _) =>
        {
            if (_name.Text.Trim().Length == 0)
            {
                Dialogs.Warn(this, "Enter an output filename.");
                return;
            }

            if (_folder.Text.Trim().Length == 0)
            {
                Dialogs.Warn(this, "Choose an output folder.");
                return;
            }

            DialogResult = DialogResult.OK;
            Close();
        };

        TableLayoutPanel folderRow = new() { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 };
        folderRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        folderRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        folderRow.Controls.Add(_folder, 0, 0);
        folderRow.Controls.Add(browse, 1, 0);

        FlowLayoutPanel buttons = new()
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
        };
        buttons.Controls.Add(cancel);
        buttons.Controls.Add(ok);

        TableLayoutPanel layout = new()
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 4,
            Padding = new Padding(12, 12, 12, 8),
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (int i = 0; i < 3; i++)
        {
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
        }

        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.Controls.Add(Caption("Output folder:"), 0, 0);
        layout.Controls.Add(folderRow, 1, 0);
        layout.Controls.Add(Caption("Filename:"), 0, 1);
        layout.Controls.Add(_name, 1, 1);
        layout.Controls.Add(Caption("POD Comment:"), 0, 2);
        layout.Controls.Add(_comment, 1, 2);
        layout.Controls.Add(buttons, 1, 3);

        Controls.Add(layout);
        AcceptButton = ok;
        CancelButton = cancel;
    }

    /// <summary>The archive path the user asked for.</summary>
    internal string TargetPath => Path.Combine(_folder.Text.Trim(), _name.Text.Trim());

    internal string Comment => _comment.Text;

    private static Label Caption(string text) =>
        new() { Text = text, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 6, 8, 0) };
}
