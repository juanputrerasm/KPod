using System.Drawing.Drawing2D;

namespace KPod.Windows.UI;

/// <summary>
/// The 16px folder and file glyphs used in the entry list. Drawn here rather than
/// shipped as assets: two shapes this simple cost less as code than as files, and
/// they scale with the list's small-image size.
/// </summary>
internal static class RowIcons
{
    internal const int FolderIndex = 0;
    internal const int FileIndex = 1;

    /// <summary>Builds the image list the entry list binds to.</summary>
    internal static ImageList CreateImageList(int side)
    {
        ImageList images = new()
        {
            ImageSize = new Size(side, side),
            ColorDepth = ColorDepth.Depth32Bit,
        };
        // Do NOT dispose these. ImageList.Images.Add only stores a reference; the
        // bitmaps are not copied until the native handle is realized, which happens
        // when the ListView creates its window. Disposing them here made that
        // realization throw ArgumentException out of ListView.OnHandleCreated,
        // which aborted the control's creation and left the list permanently empty.
        // The handle can also be recreated later (DPI or theme change), so forcing
        // it early and then disposing would only move the failure. The ImageList
        // owns them for its lifetime instead.
        images.Images.Add(DrawFolder(side));
        images.Images.Add(DrawFile(side));

        return images;
    }

    private static Bitmap DrawFolder(int side)
    {
        Bitmap bitmap = new(side, side);
        using Graphics g = Graphics.FromImage(bitmap);
        g.SmoothingMode = SmoothingMode.None;

        float scale = side / 16f;
        RectangleF body = new(1 * scale, 4 * scale, 14 * scale, 10 * scale);
        RectangleF tab = new(1 * scale, 2 * scale, 6 * scale, 3 * scale);

        using SolidBrush fill = new(Color.FromArgb(0xF0, 0xC0, 0x60));
        using SolidBrush tabFill = new(Color.FromArgb(0xD8, 0xA8, 0x48));
        using Pen edge = new(Color.FromArgb(0x8A, 0x66, 0x22));
        g.FillRectangle(tabFill, tab);
        g.FillRectangle(fill, body);
        g.DrawRectangle(edge, body.X, body.Y, body.Width - 1, body.Height - 1);
        return bitmap;
    }

    private static Bitmap DrawFile(int side)
    {
        Bitmap bitmap = new(side, side);
        using Graphics g = Graphics.FromImage(bitmap);
        g.SmoothingMode = SmoothingMode.None;

        float scale = side / 16f;
        RectangleF body = new(3 * scale, 1 * scale, 10 * scale, 14 * scale);

        using SolidBrush fill = new(Color.White);
        using Pen edge = new(Color.FromArgb(0x80, 0x80, 0x80));
        using Pen rule = new(Color.FromArgb(0xB0, 0xB8, 0xC4));
        g.FillRectangle(fill, body);
        g.DrawRectangle(edge, body.X, body.Y, body.Width - 1, body.Height - 1);
        for (int line = 0; line < 4; line++)
        {
            float y = (4 + (line * 2.5f)) * scale;
            g.DrawLine(rule, body.X + (2 * scale), y, body.Right - (2 * scale), y);
        }

        return bitmap;
    }
}
