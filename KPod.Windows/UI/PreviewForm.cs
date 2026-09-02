using System.Globalization;
using System.Text;
using KPod.Core.Compat;
using KPod.Core.Images;
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
    private const int HexDumpLimit = 4096;

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
        if (entryName.EndsWith(".WAV", StringComparison.OrdinalIgnoreCase))
        {
            using AudioPlayerForm player = new(entryName, data);
            player.ShowDialog(owner);
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
        int[] palette;

        if (detected is null)
        {
            using RawDimensionsForm prompt = new(entryName, data.Length, archive, rawNameField);
            if (prompt.ShowDialog(owner) != DialogResult.OK || prompt.Options is null)
            {
                return false;
            }

            width = prompt.Options.Width;
            height = prompt.Options.Height;
            palette = prompt.Options.Palette;
        }
        else
        {
            (width, height) = detected.Value;
            palette = PaletteResolver.Resolve(entryName, archive, rawNameField);
        }

        Bitmap bitmap = ImageBuilder.ToBitmap(RawImageDecoder.DecodeRaw(data, palette, width, height));

        // Art textures are 64x64; at 1:1 they are too small to judge.
        if (width <= 64)
        {
            bitmap = ImageBuilder.ScaleNearest(bitmap, 4);
        }

        BuildImage(bitmap);
        return true;
    }

    private void BuildImage(Bitmap bitmap)
    {
        PictureBox picture = new()
        {
            Image = bitmap,
            SizeMode = PictureBoxSizeMode.AutoSize,
        };
        Panel scroll = new() { Dock = DockStyle.Fill, AutoScroll = true };
        scroll.Controls.Add(picture);
        Controls.Add(scroll);

        ClientSize = ScreenFit.Cap(
            Math.Min(bitmap.Width + 40, MaxPreviewWidth),
            Math.Min(bitmap.Height + 60, MaxPreviewHeight));
        Disposed += (_, _) => bitmap.Dispose();
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
