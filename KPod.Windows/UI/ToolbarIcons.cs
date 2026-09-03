using System.Drawing.Text;

namespace KPod.Windows.UI;

/// <summary>
/// Toolbar glyphs taken from Segoe MDL2 Assets, the icon font Windows itself draws
/// its command bars with, rendered into bitmaps at the size the toolbar asks for.
///
/// <para>A font rather than shipped image files: the glyphs come out crisp at any
/// DPI, they follow the system text colour, and there is nothing to keep in the
/// repository. It is the same reasoning as <see cref="RowIcons"/>, which draws the
/// two list glyphs in code rather than shipping them.</para>
///
/// <para>The font arrived with Windows 10. KPod still runs on Windows 7 SP1 and 8.1
/// where it is absent, so <see cref="Render"/> returns null there and the toolbar
/// falls back to plain text labels.</para>
/// </summary>
internal static class ToolbarIcons
{
    private const string FontName = "Segoe MDL2 Assets";

    // Code points from the Segoe MDL2 Assets chart. Named for the action rather
    // than the glyph, because the action is what the toolbar is choosing.
    internal const string New = "";          // Page
    internal const string Open = "";         // OpenFile
    internal const string Save = "";         // Save
    internal const string SaveAs = "";       // SaveAs
    internal const string AddFiles = "";     // Add
    internal const string Extract = "";      // Download
    internal const string Remove = "";       // Delete
    internal const string Expand = "";       // ChevronDown
    internal const string Collapse = "";     // ChevronUp
    internal const string Search = "";       // Search
    internal const string About = "";        // Info

    private static readonly Lazy<FontFamily?> Family = new(LoadFamily);

    /// <summary>
    /// The font every glyph of one size is drawn with. Kept rather than rebuilt per
    /// glyph: a toolbar asks for a dozen icons at the same size in a row, and it
    /// outlives them all anyway.
    /// </summary>
    private static Font? _font;
    private static int _fontSide;

    private static Font? FontFor(int side)
    {
        if (_font is not null && _fontSide == side)
        {
            return _font;
        }

        FontFamily? family = Family.Value;
        if (family is null)
        {
            return null;
        }

        _font?.Dispose();

        // An em equal to the box maps the font's design grid, which these glyphs are
        // drawn on, straight onto the bitmap.
        _font = new Font(family, side, FontStyle.Regular, GraphicsUnit.Pixel);
        _fontSide = side;
        return _font;
    }

    private static FontFamily? LoadFamily()
    {
        try
        {
            return new FontFamily(FontName);
        }
        catch (ArgumentException)
        {
            // Not installed, which is the Windows 7 and 8.1 case.
            return null;
        }
    }

    /// <summary>
    /// One glyph drawn into a transparent square bitmap of the given side, in the
    /// system's control text colour. Returns null when the font is unavailable, so
    /// callers fall back rather than showing a blank button.
    /// </summary>
    internal static Bitmap? Render(string glyph, int side)
    {
        Font? shared = side > 0 ? FontFor(side) : null;
        if (shared is null)
        {
            return null;
        }

        Bitmap bitmap = new(side, side);
        Font? shrunk = null;
        try
        {
            using Graphics g = Graphics.FromImage(bitmap);

            // Grayscale antialiasing, not ClearType: subpixel rendering onto a
            // transparent bitmap leaves coloured fringes wherever the glyph meets
            // whatever is behind the toolbar.
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;

            using StringFormat format = new(StringFormat.GenericTypographic)
            {
                FormatFlags = StringFormatFlags.NoWrap | StringFormatFlags.NoClip,
            };

            Font font = shared;
            SizeF measured = g.MeasureString(glyph, font, PointF.Empty, format);

            // Line metrics can exceed the em, so a glyph occasionally measures taller
            // than the box. Shrink it to fit rather than let the bitmap clip it.
            float fit = Math.Min(
                side / Math.Max(measured.Width, 0.01f),
                side / Math.Max(measured.Height, 0.01f));
            if (fit < 1f)
            {
                shrunk = new Font(font.FontFamily, side * fit, FontStyle.Regular, GraphicsUnit.Pixel);
                font = shrunk;
                measured = g.MeasureString(glyph, font, PointF.Empty, format);
            }

            // Centred on the measured ink rather than on a line box, which is what
            // keeps a chevron and a floppy disk looking equally centred.
            using SolidBrush brush = new(SystemColors.ControlText);
            g.DrawString(glyph, font, brush,
                (side - measured.Width) / 2f, (side - measured.Height) / 2f, format);
            return bitmap;
        }
        catch (Exception)
        {
            bitmap.Dispose();
            throw;
        }
        finally
        {
            // Only a glyph that had to be scaled owns a font of its own.
            shrunk?.Dispose();
        }
    }
}
