using KPod.Core.Images;
using KPod.Core.Pods;

namespace KPod.Windows.UI;

/// <summary>The dimensions and palette chosen for a non-standard RAW payload.</summary>
internal sealed record RawPreviewOptions(int Width, int Height, int[] Palette);

/// <summary>
/// Asks for width, height and palette when a RAW payload is not one of the sizes
/// the decoder recognizes. Width times height must match the payload exactly, so a
/// wrong guess is rejected rather than drawn as garbage.
/// </summary>
internal sealed class RawDimensionsForm : Form
{
    private readonly int _byteCount;
    private readonly ComboBox _suggestions = new() { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly NumericUpDown _width = new() { Dock = DockStyle.Fill, Minimum = 1 };
    private readonly NumericUpDown _height = new() { Dock = DockStyle.Fill, Minimum = 1 };
    private readonly ComboBox _palettes = new() { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly List<PaletteChoice> _paletteChoices;

    internal RawDimensionsForm(
        string entryName, int byteCount, PodArchive? archive, byte[]? rawNameField = null)
    {
        _byteCount = byteCount;

        Text = "RAW Dimensions";
        Icon = AppIcon.Shared;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;
        AutoScaleMode = AutoScaleMode.Dpi;
        Font = SystemFonts.MessageBoxFont!;
        ClientSize = new Size(460, 260);

        IReadOnlyList<(int Width, int Height)> suggestions = RawImageDecoder.SuggestDimensions(byteCount);
        (int preferredWidth, int preferredHeight) = RawImageDecoder.PreferredDimensions(byteCount);

        _width.Maximum = Math.Max(1, byteCount);
        _height.Maximum = Math.Max(1, byteCount);
        _width.Value = preferredWidth;
        _height.Value = preferredHeight;

        // Twelve is enough to cover the plausible shapes without a scroll marathon.
        foreach ((int width, int height) in suggestions.Take(12))
        {
            _suggestions.Items.Add(new DimensionChoice(width, height));
        }

        _suggestions.SelectedIndexChanged += (_, _) =>
        {
            if (_suggestions.SelectedItem is DimensionChoice choice)
            {
                _width.Value = choice.Width;
                _height.Value = choice.Height;
            }
        };

        PaletteChoices palettes = PaletteResolver.ResolveChoices(entryName, archive, rawNameField);
        _paletteChoices = [.. palettes.Choices];
        foreach (PaletteChoice choice in _paletteChoices)
        {
            _palettes.Items.Add(choice.Label);
        }

        if (_palettes.Items.Count > 0)
        {
            _palettes.SelectedIndex = Math.Max(0, Math.Min(palettes.DefaultIndex, _palettes.Items.Count - 1));
        }

        Button swap = new() { Text = "Swap", AutoSize = true, Dock = DockStyle.Left };
        swap.Click += (_, _) => (_width.Value, _height.Value) = (_height.Value, _width.Value);

        Button ok = new() { Text = "OK", AutoSize = true, Margin = new Padding(4, 0, 0, 0) };
        Button cancel = new()
        {
            Text = "Cancel",
            DialogResult = DialogResult.Cancel,
            AutoSize = true,
            Margin = new Padding(4, 0, 0, 0),
        };
        ok.Click += (_, _) => Confirm();

        Label header = new()
        {
            Text = "Select dimensions for " + entryName + "\r\nRAW payload size: " + byteCount + " bytes",
            Dock = DockStyle.Fill,
            AutoSize = false,
        };

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
            RowCount = 7,
            Padding = new Padding(12),
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        for (int i = 0; i < 5; i++)
        {
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        }

        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.Controls.Add(header, 0, 0);
        layout.SetColumnSpan(header, 2);
        layout.Controls.Add(Caption("Suggested sizes:"), 0, 1);
        layout.Controls.Add(_suggestions, 1, 1);
        layout.Controls.Add(Caption("Width:"), 0, 2);
        layout.Controls.Add(_width, 1, 2);
        layout.Controls.Add(Caption("Height:"), 0, 3);
        layout.Controls.Add(_height, 1, 3);
        layout.Controls.Add(swap, 1, 4);
        layout.Controls.Add(Caption("Palette:"), 0, 5);
        layout.Controls.Add(_palettes, 1, 5);
        layout.Controls.Add(buttons, 1, 6);

        Controls.Add(layout);
        AcceptButton = ok;
        CancelButton = cancel;
    }

    /// <summary>The confirmed choice, or null while the dialog has not been accepted.</summary>
    internal RawPreviewOptions? Options { get; private set; }

    private void Confirm()
    {
        int width = (int)_width.Value;
        int height = (int)_height.Value;
        long required = (long)width * height;
        if (required != _byteCount)
        {
            Dialogs.Warn(
                this,
                "Width times height must exactly match the RAW size.\n"
                + width + " x " + height + " = " + required
                + " bytes, expected " + _byteCount + ".");
            return;
        }

        int[] palette = _palettes.SelectedIndex >= 0 && _palettes.SelectedIndex < _paletteChoices.Count
            ? _paletteChoices[_palettes.SelectedIndex].Palette
            : RawImageDecoder.LoadResourcePalette();

        Options = new RawPreviewOptions(width, height, palette);
        DialogResult = DialogResult.OK;
        Close();
    }

    private static Label Caption(string text) =>
        new() { Text = text, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 6, 8, 0) };

    /// <summary>A width and height pair as it reads in the suggestion list.</summary>
    private sealed record DimensionChoice(int Width, int Height)
    {
        public override string ToString() => Width + " x " + Height;
    }
}
