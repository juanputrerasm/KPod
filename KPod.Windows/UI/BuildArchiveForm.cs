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

        // Anchor rather than Dock: inside a table cell, Anchor is what lets a button
        // keep the size its own text asks for.
        Button browse = new()
        {
            Text = "Browse...",
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(8, 0, 0, 0),
        };
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

        FlowLayoutPanel buttons = new()
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
        };
        buttons.Controls.Add(cancel);
        buttons.Controls.Add(ok);

        // Flat on purpose: a nested TableLayoutPanel holding the field and its Browse
        // button left the button with no visible text, so every control sits directly
        // in this table instead.
        TableLayoutPanel layout = new()
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 4,
            Padding = new Padding(12, 12, 12, 8),
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));      // captions
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));  // fields
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));      // Browse
        for (int i = 0; i < 3; i++)
        {
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
        }

        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.Controls.Add(DialogLayout.Caption("Output folder:"), 0, 0);
        layout.Controls.Add(_folder, 1, 0);
        layout.Controls.Add(browse, 2, 0);

        layout.Controls.Add(DialogLayout.Caption("Filename:"), 0, 1);
        layout.Controls.Add(_name, 1, 1);
        layout.SetColumnSpan(_name, 2);

        layout.Controls.Add(DialogLayout.Caption("POD Comment:"), 0, 2);
        layout.Controls.Add(_comment, 1, 2);
        layout.SetColumnSpan(_comment, 2);

        layout.Controls.Add(buttons, 1, 3);
        layout.SetColumnSpan(buttons, 2);

        Controls.Add(layout);
        AcceptButton = ok;
        CancelButton = cancel;
    }

    /// <summary>The archive path the user asked for.</summary>
    internal string TargetPath => Path.Combine(_folder.Text.Trim(), _name.Text.Trim());

    internal string Comment => _comment.Text;

}
