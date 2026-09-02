using KPod.Core.Session;

namespace KPod.Windows.UI;

/// <summary>
/// Destination folder and folder-structure toggle for an extraction. The confirmed
/// choices are written straight back into the session.
/// </summary>
internal sealed class ExtractOptionsForm : Form
{
    private readonly TextBox _folder = new() { Dock = DockStyle.Fill };
    private readonly CheckBox _preserveFolders = new()
    {
        Text = "Preserve folder structure",
        Dock = DockStyle.Fill,
        Checked = true,
    };

    private readonly PodSession _session;

    internal ExtractOptionsForm(PodSession session)
    {
        _session = session;
        Text = "Extract";
        Icon = AppIcon.Shared;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;
        AutoScaleMode = AutoScaleMode.Dpi;
        Font = SystemFonts.MessageBoxFont!;
        ClientSize = new Size(520, 160);

        _folder.Text = session.TargetFolderPath ?? string.Empty;
        _preserveFolders.Checked = session.PreserveExtractFolderStructure;

        Button browse = new() { Text = "Browse...", AutoSize = true, Dock = DockStyle.Right };
        browse.Click += (_, _) =>
        {
            string? chosen = FolderPicker.Choose(this, "Choose Extract Destination", _folder.Text);
            if (chosen is not null)
            {
                _folder.Text = chosen;
            }
        };

        Button ok = new() { Text = "Extract", AutoSize = true, Margin = new Padding(4, 0, 0, 0) };
        Button cancel = new()
        {
            Text = "Cancel",
            DialogResult = DialogResult.Cancel,
            AutoSize = true,
            Margin = new Padding(4, 0, 0, 0),
        };
        ok.Click += (_, _) =>
        {
            if (_folder.Text.Trim().Length == 0)
            {
                Dialogs.Warn(this, "Choose a destination.");
                return;
            }

            session.TargetFolderPath = _folder.Text.Trim();
            session.PreserveExtractFolderStructure = _preserveFolders.Checked;
            DialogResult = DialogResult.OK;
            Close();
        };

        TableLayoutPanel folderRow = new()
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 1,
        };
        folderRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        folderRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        folderRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        folderRow.Controls.Add(
            new Label { Text = "Destination:", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 0);
        folderRow.Controls.Add(_folder, 1, 0);
        folderRow.Controls.Add(browse, 2, 0);

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
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(12, 12, 12, 8),
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.Controls.Add(folderRow, 0, 0);
        layout.Controls.Add(_preserveFolders, 0, 1);
        layout.Controls.Add(buttons, 0, 2);

        Controls.Add(layout);
        AcceptButton = ok;
        CancelButton = cancel;
    }
}
