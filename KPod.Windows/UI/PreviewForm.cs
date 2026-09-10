using System.Globalization;
using System.Text;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using KPod.Core.Compat;
using KPod.Core.Images;
using KPod.Core.Models;
using KPod.Core.Pods;

namespace KPod.Windows.UI;

/// <summary>
/// A floating preview whose display mode comes from the entry's extension:
///
/// <list type="table">
///   <item><term>.raw, .clr</term><description>8-bit paletted image, palette resolved from the archive</description></item>
///   <item><term>.act</term><description>256-colour swatch grid</description></item>
///   <item><term>.wav</term><description>handed to the audio player instead of a window</description></item>
///   <item><term>.bmp, .png, .jpg and friends</term><description>decoded by GDI+</description></item>
///   <item><term>.txt, .def, .lvl, .sit and friends</term><description>plain text</description></item>
///   <item><term>anything else</term><description>hex dump of the first 4 096 bytes</description></item>
/// </list>
/// </summary>
internal sealed class PreviewForm : Form
{
    private const int MaxPreviewWidth = 1024;
    private const int MaxPreviewHeight = 768;
    /// <summary>Room for a scrollbar on each axis, so the image is never clipped by one.</summary>
    private const int ScrollBarAllowance = 20;
    private const int ContentPadding = 24;
    private const int HexDumpLimit = 4096;
    private static string? _savedRawPaletteLabel;

    private PreviewForm(string entryName)
    {
        Text = "Preview - " + entryName;
        Icon = AppIcon.Shared;
        StartPosition = FormStartPosition.CenterParent;
        AutoScaleMode = AutoScaleMode.Dpi;
        Font = SystemFonts.MessageBoxFont!;
        ClientSize = new Size(700, 500);
    }

    /// <summary>
    /// Opens the right preview for an entry. WAV goes straight to the audio player,
    /// and a cancelled RAW dimension prompt opens nothing at all.
    /// </summary>
    internal static void Open(
        IWin32Window owner,
        string entryName,
        byte[] data,
        PodArchive? archive,
        byte[]? rawNameField = null)
    {
        // Shown modally, so ShowDialog rather than Show. ShowDialog does not
        // dispose the form on close the way Show does, hence the using.
        if (entryName.EndsWith(".WAV", StringComparison.OrdinalIgnoreCase)
            || entryName.EndsWith(".MOD", StringComparison.OrdinalIgnoreCase))
        {
            using AudioPlayerForm player = new(entryName, data);
            player.ShowDialog(owner);
            return;
        }

        // .SMF is 4x4 Evolution's model format and opens in the same viewer. The magic is
        // checked too, so an entry whose extension is missing or wrong still opens as the
        // model it is rather than falling through to a hex dump.
        if (entryName.EndsWith(".BIN", StringComparison.OrdinalIgnoreCase)
            || entryName.EndsWith(".SMF", StringComparison.OrdinalIgnoreCase)
            || SmfModelDecoder.IsSmfModel(data))
        {
            using BinPreviewForm viewer = new(entryName, data, archive);
            viewer.ShowDialog(owner);
            return;
        }

        using PreviewForm form = new(entryName);
        if (!form.Build(owner, entryName, data, archive, rawNameField))
        {
            return;
        }

        form.ShowDialog(owner);
    }

    /// <summary>Fills the window. Returns false when the user cancelled the RAW prompt.</summary>
    private bool Build(
        IWin32Window owner,
        string entryName,
        byte[] data,
        PodArchive? archive,
        byte[]? rawNameField)
    {
        if (RawImageDecoder.IsRawImage(entryName))
        {
            return BuildRawImage(owner, entryName, data, archive, rawNameField);
        }

        if (RawImageDecoder.IsActPalette(entryName))
        {
            BuildActPalette(entryName, data);
            return true;
        }

        if (entryName.EndsWith(".TGA", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                BuildImage(ImageBuilder.ToBitmap(TgaImageDecoder.Decode(data, entryName)), checkerboard: true);
            }
            catch (ArgumentException ex)
            {
                BuildMessage(ex.Message);
            }
            return true;
        }

        if (entryName.EndsWith(".PNG", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                using MemoryStream stream = new(data, writable: false);
                using Image decoded = Image.FromStream(stream, useEmbeddedColorManagement: true, validateImageData: true);
                BuildImage(new Bitmap(decoded), checkerboard: true);
            }
            catch (Exception ex) when (ex is ArgumentException or OutOfMemoryException or NotSupportedException)
            {
                BuildMessage("Invalid PNG image: " + ex.Message);
            }
            return true;
        }

        if (RawImageDecoder.IsTextFile(entryName))
        {
            BuildText(data);
            return true;
        }

        // Not a format of ours; GDI+ may still know it, and if not, hex it is.
        try
        {
            using MemoryStream stream = new(data, writable: false);
            using Image decoded = Image.FromStream(stream);
            BuildImage(new Bitmap(decoded));
        }
        catch (Exception ex) when (ex is ArgumentException or OutOfMemoryException or NotSupportedException)
        {
            BuildHexDump(data);
        }

        return true;
    }

    private bool BuildRawImage(
        IWin32Window owner,
        string entryName,
        byte[] data,
        PodArchive? archive,
        byte[]? rawNameField)
    {
        (int Width, int Height)? detected = RawImageDecoder.DetectDimensions(data.Length);
        int width;
        int height;
        PaletteChoices palettes = PaletteResolver.ResolveChoices(entryName, archive, rawNameField);
        int selectedIndex = palettes.DefaultIndex;
        if (_savedRawPaletteLabel is not null)
        {
            int saved = palettes.Choices.ToList().FindIndex(choice => choice.Label == _savedRawPaletteLabel);
            if (saved >= 0) selectedIndex = saved;
        }

        if (detected is null)
        {
            using RawDimensionsForm prompt = new(entryName, data.Length, archive, rawNameField);
            if (prompt.ShowDialog(owner) != DialogResult.OK || prompt.Options is null)
            {
                return false;
            }

            width = prompt.Options.Width;
            height = prompt.Options.Height;
            int prompted = palettes.Choices.ToList().FindIndex(choice => choice.Label == prompt.Options.PaletteLabel);
            if (prompted >= 0) selectedIndex = prompted;
        }
        else
        {
            (width, height) = detected.Value;
        }

        ComboBox palettePicker = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 260 };
        foreach (PaletteChoice choice in palettes.Choices) palettePicker.Items.Add(choice.Label);
        palettePicker.SelectedIndex = Math.Max(0, Math.Min(selectedIndex, palettePicker.Items.Count - 1));

        Button save = new() { Text = "Save as BMP...", AutoSize = true };
        FlowLayoutPanel controls = new()
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            Padding = new Padding(8, 6, 8, 6),
            WrapContents = false,
        };
        controls.Controls.Add(new Label { Text = "Palette:", AutoSize = true, Margin = new Padding(0, 6, 4, 0) });
        controls.Controls.Add(palettePicker);
        controls.Controls.Add(save);

        Panel scroll = new() { Dock = DockStyle.Fill, AutoScroll = true };
        Controls.Add(scroll);
        Controls.Add(controls);
        scroll.Resize += (_, _) => CentreCanvas(scroll);

        Bitmap? original = null;
        ImageCanvas? canvas = null;
        void Repaint()
        {
            int index = Math.Max(0, palettePicker.SelectedIndex);
            PaletteChoice choice = palettes.Choices[index];
            _savedRawPaletteLabel = choice.Label;
            original?.Dispose();
            original = ImageBuilder.ToBitmap(RawImageDecoder.DecodeRaw(data, choice.Palette, width, height));
            Bitmap display = width <= 64 ? ImageBuilder.ScaleNearest(new Bitmap(original), 4) : new Bitmap(original);
            if (canvas is null)
            {
                canvas = new ImageCanvas(display, checkerboard: false);
                scroll.Controls.Add(canvas);
            }
            else canvas.ReplaceImage(display);
            CentreCanvas(scroll);
        }

        palettePicker.SelectedIndexChanged += (_, _) => Repaint();
        save.Click += (_, _) =>
        {
            if (original is null) return;
            using SaveFileDialog dialog = new()
            {
                Filter = "Bitmap image (*.bmp)|*.bmp|All files (*.*)|*.*",
                FileName = Path.GetFileNameWithoutExtension(entryName) + ".bmp",
                AddExtension = true,
                DefaultExt = "bmp",
            };
            if (dialog.ShowDialog(this) == DialogResult.OK) original.Save(dialog.FileName, ImageFormat.Bmp);
        };

        Repaint();
        SizeToContent(new Size(canvas?.Width ?? width, canvas?.Height ?? height), controls);
        Disposed += (_, _) => original?.Dispose();

        return true;
    }

    private void BuildImage(Bitmap bitmap, bool checkerboard = false)
    {
        ImageCanvas picture = new(bitmap, checkerboard);
        Panel scroll = new() { Dock = DockStyle.Fill, AutoScroll = true };
        scroll.Controls.Add(picture);
        Controls.Add(scroll);
        scroll.Resize += (_, _) => CentreCanvas(scroll);

        SizeToContent(bitmap.Size, toolbar: null);
    }

    /// <summary>
    /// Sizes the window to hold the image <em>and</em> the toolbar above it. Sizing from
    /// the image alone is what used to push "Save as BMP..." off the right-hand edge of a
    /// small texture's window: a 64x64 RAW is 256 pixels wide once magnified, and the
    /// palette row needs closer to 450.
    /// </summary>
    private void SizeToContent(Size content, Control? toolbar)
    {
        Size bar = toolbar is null ? Size.Empty : MeasureToolbar(toolbar);
        int width = Math.Max(content.Width + ScrollBarAllowance, bar.Width) + ContentPadding;
        int height = content.Height + bar.Height + ScrollBarAllowance + ContentPadding;
        ClientSize = ScreenFit.Cap(
            Math.Min(width, MaxPreviewWidth), Math.Min(height, MaxPreviewHeight));
        // A window narrower than the toolbar clips its own buttons, so make that
        // unreachable by dragging as well.
        MinimumSize = new Size(
            Math.Min(bar.Width + ContentPadding, MaxPreviewWidth) + (Width - ClientSize.Width),
            MinimumSize.Height);
    }

    /// <summary>
    /// The room a single non-wrapping row of controls needs. Summed from the children
    /// rather than read off the panel: this runs before the window is shown, when a
    /// docked panel has not been laid out and an AutoSize button still reports the
    /// default 75 pixels rather than the width its caption needs.
    /// </summary>
    internal static Size MeasureToolbar(Control toolbar)
    {
        int width = toolbar.Padding.Horizontal;
        int height = 0;
        foreach (Control child in toolbar.Controls)
        {
            Size preferred = child.GetPreferredSize(Size.Empty);
            width += Math.Max(child.Width, preferred.Width) + child.Margin.Horizontal;
            height = Math.Max(height, Math.Max(child.Height, preferred.Height) + child.Margin.Vertical);
        }
        return new Size(width, height + toolbar.Padding.Vertical);
    }

    /// <summary>Keeps an image smaller than its viewport in the middle of it.</summary>
    private static void CentreCanvas(Panel scroll)
    {
        if (scroll.Controls.Count == 0) return;
        Control canvas = scroll.Controls[0];
        canvas.Location = new Point(
            Math.Max(0, (scroll.ClientSize.Width - canvas.Width) / 2),
            Math.Max(0, (scroll.ClientSize.Height - canvas.Height) / 2));
    }

    private void BuildActPalette(string entryName, byte[] data)
    {
        int[] palette;
        try
        {
            palette = RawImageDecoder.DecodeAct(data);
        }
        catch (ArgumentException ex)
        {
            BuildMessage("Invalid ACT file: " + ex.Message);
            return;
        }

        PaletteGrid grid = new(palette) { Dock = DockStyle.Fill };
        Label header = new()
        {
            Text = "256-colour VGA palette: " + entryName,
            Dock = DockStyle.Top,
            Height = 24,
            TextAlign = ContentAlignment.MiddleCenter,
        };

        Controls.Add(grid);
        Controls.Add(header);
        ClientSize = new Size(PaletteGrid.PreferredSide + 24, PaletteGrid.PreferredSide + 48);
    }

    private void BuildText(byte[] data)
    {
        TextBox text = new()
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Both,
            WordWrap = false,
            Font = new Font(FontFamily.GenericMonospace, 9.5f),
            Text = PodText.Latin1.GetString(data).Replace("\r\n", "\n").Replace("\n", "\r\n"),
        };
        text.Select(0, 0);
        Controls.Add(text);
        ClientSize = ScreenFit.Cap(700, 540);
    }

    private void BuildHexDump(byte[] data)
    {
        TextBox text = new()
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Both,
            WordWrap = false,
            Font = new Font(FontFamily.GenericMonospace, 9f),
            Text = HexDump(data),
        };
        text.Select(0, 0);
        Controls.Add(text);
        ClientSize = ScreenFit.Cap(720, 520);
    }

    private void BuildMessage(string message)
    {
        Controls.Add(new Label
        {
            Text = message,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter,
        });
        ClientSize = new Size(420, 120);
    }

    /// <summary>
    /// Classic 16-bytes-per-line dump with an ASCII gutter, capped so a 40 MB entry
    /// does not become a 40 MB string.
    /// </summary>
    internal static string HexDump(byte[] data)
    {
        int limit = Math.Min(data.Length, HexDumpLimit);
        StringBuilder text = new(limit * 4);
        text.Append("Binary data - first ").Append(limit).Append(" of ").Append(data.Length)
            .Append(" bytes").Append("\r\n\r\n");

        for (int i = 0; i < limit; i += 16)
        {
            text.Append(i.ToString("X6", CultureInfo.InvariantCulture)).Append("  ");
            int lineLength = Math.Min(16, limit - i);
            for (int j = 0; j < lineLength; j++)
            {
                text.Append(data[i + j].ToString("X2", CultureInfo.InvariantCulture)).Append(' ');
                if (j == 7)
                {
                    text.Append(' ');
                }
            }

            text.Append(' ', ((16 - lineLength) * 3) + (lineLength <= 8 ? 1 : 0));
            text.Append(" |");
            for (int j = 0; j < lineLength; j++)
            {
                char c = (char)data[i + j];
                text.Append(char.IsControl(c) ? '.' : c);
            }

            text.Append("|\r\n");
        }

        return text.ToString();
    }
}

/// <summary>Owns and draws a bitmap, with an optional transparency checkerboard.</summary>
internal sealed class ImageCanvas : Control
{
    private Bitmap _image;
    private readonly bool _checkerboard;

    internal ImageCanvas(Bitmap image, bool checkerboard)
    {
        _image = image;
        _checkerboard = checkerboard;
        DoubleBuffered = true;
        Size = image.Size;
    }

    internal void ReplaceImage(Bitmap image)
    {
        Bitmap old = _image;
        _image = image;
        Size = image.Size;
        old.Dispose();
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        if (_checkerboard)
        {
            const int tile = 12;
            using SolidBrush light = new(Color.White);
            using SolidBrush dark = new(Color.LightGray);
            for (int y = 0; y < Height; y += tile)
                for (int x = 0; x < Width; x += tile)
                    e.Graphics.FillRectangle(((x / tile) + (y / tile)) % 2 == 0 ? light : dark, x, y, tile, tile);
        }
        e.Graphics.InterpolationMode = InterpolationMode.NearestNeighbor;
        e.Graphics.DrawImageUnscaled(_image, 0, 0);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _image.Dispose();
        base.Dispose(disposing);
    }
}

/// <summary>
/// The 16 by 16 swatch grid for an ACT palette. Hovering a swatch reports its index
/// and hex value, which is the only practical way to read an index off the grid.
/// </summary>
internal sealed class PaletteGrid : Control
{
    private const int Swatch = 20;
    private const int Columns = 16;

    internal const int PreferredSide = Swatch * Columns;

    private readonly int[] _palette;
    private readonly ToolTip _tip = new();
    private int _hoveredIndex = -1;

    internal PaletteGrid(int[] palette)
    {
        _palette = palette;
        DoubleBuffered = true;
        MinimumSize = new Size(PreferredSide, PreferredSide);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        using Pen edge = new(Color.DimGray);
        for (int i = 0; i < 256; i++)
        {
            int column = i % Columns;
            int row = i / Columns;
            Rectangle cell = new(column * Swatch, row * Swatch, Swatch, Swatch);
            using SolidBrush brush = new(Color.FromArgb(_palette[i]));
            e.Graphics.FillRectangle(brush, cell);
            e.Graphics.DrawRectangle(edge, cell.X, cell.Y, cell.Width - 1, cell.Height - 1);
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        int index = ((e.Y / Swatch) * Columns) + (e.X / Swatch);
        if (index == _hoveredIndex || index < 0 || index >= 256 || e.X >= PreferredSide)
        {
            return;
        }

        _hoveredIndex = index;
        _tip.SetToolTip(
            this,
            "Index " + index + " - #" + (_palette[index] & 0xFFFFFF).ToString("X6", CultureInfo.InvariantCulture));
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _tip.Dispose();
        }

        base.Dispose(disposing);
    }
}
