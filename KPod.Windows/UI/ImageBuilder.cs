using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using KPod.Core.Images;

namespace KPod.Windows.UI;

/// <summary>
/// Turns the core decoder's ARGB pixel arrays into GDI+ bitmaps. This is the only
/// place the two meet: KPod.Core never references System.Drawing.
/// </summary>
internal static class ImageBuilder
{
    /// <summary>Copies a decoded image into a 32-bit ARGB bitmap in one blit.</summary>
    internal static Bitmap ToBitmap(DecodedImage image)
    {
        Bitmap bitmap = new(image.Width, image.Height, PixelFormat.Format32bppArgb);
        BitmapData data = bitmap.LockBits(
            new Rectangle(0, 0, image.Width, image.Height),
            ImageLockMode.WriteOnly,
            PixelFormat.Format32bppArgb);
        try
        {
            // The decoder packs ARGB in the same order GDI+ stores it, so each row
            // copies straight across; Stride is honoured in case it is padded.
            for (int y = 0; y < image.Height; y++)
            {
                nint rowStart = data.Scan0 + (y * data.Stride);
                System.Runtime.InteropServices.Marshal.Copy(
                    image.Pixels, y * image.Width, rowStart, image.Width);
            }
        }
        finally
        {
            bitmap.UnlockBits(data);
        }

        return bitmap;
    }

    /// <summary>
    /// Nearest-neighbour scale-up, so a 64x64 art texture is legible without the
    /// smoothing that would turn its palette into mush.
    /// </summary>
    internal static Bitmap ScaleNearest(Bitmap source, int factor)
    {
        Bitmap scaled = new(source.Width * factor, source.Height * factor, PixelFormat.Format32bppArgb);
        using (Graphics g = Graphics.FromImage(scaled))
        {
            g.InterpolationMode = InterpolationMode.NearestNeighbor;
            g.PixelOffsetMode = PixelOffsetMode.Half;
            g.DrawImage(source, 0, 0, scaled.Width, scaled.Height);
        }

        source.Dispose();
        return scaled;
    }
}
