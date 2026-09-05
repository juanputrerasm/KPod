using KPod.Core.Images;
using KPod.Core.Models;
using KPod.Core.Pods;

namespace KPod.Windows.UI;

internal sealed record LoadedBinTexture(
    string Name, PodEntry? Entry, Bitmap? Diffuse, Bitmap? Normal, bool IsRaw, bool UsedFallback, string? Warning);

internal static class BinTextureLoader
{
    internal static IReadOnlyList<LoadedBinTexture> Load(
        BinModel model, PodArchive? archive, int[] fallbackPalette)
    {
        List<LoadedBinTexture> loaded = [];
        foreach (string textureName in model.TextureNames)
        {
            if (archive is null)
            {
                loaded.Add(new LoadedBinTexture(textureName, null, null, null, false, false, "No archive is available for texture lookup."));
                continue;
            }

            BinTextureEntries entries = BinTextureResolver.Resolve(archive, textureName);
            if (entries.Diffuse is null)
            {
                loaded.Add(new LoadedBinTexture(textureName, null, null, null, false, false, "Missing texture: " + textureName));
                continue;
            }

            Bitmap? diffuse = null;
            Bitmap? normal = null;
            bool raw = entries.Diffuse.Name.EndsWith(".RAW", StringComparison.OrdinalIgnoreCase);
            bool usedFallback = false;
            string? warning = null;
            try
            {
                byte[] bytes = archive.GetEntryBytes(entries.Diffuse);
                if (raw)
                {
                    (int Width, int Height)? dimensions = RawImageDecoder.DetectDimensions(bytes.Length);
                    if (dimensions is null) throw new ArgumentException("RAW texture has an unknown size: " + bytes.Length + " bytes");
                    int[] palette;
                    if (entries.SameNamePalette is not null)
                        palette = RawImageDecoder.DecodeAct(archive.GetEntryBytes(entries.SameNamePalette));
                    else { palette = fallbackPalette; usedFallback = true; }
                    diffuse = ImageBuilder.ToBitmap(RawImageDecoder.DecodeRaw(bytes, palette, dimensions.Value.Width, dimensions.Value.Height));
                }
                else if (entries.Diffuse.Name.EndsWith(".TGA", StringComparison.OrdinalIgnoreCase))
                    diffuse = ImageBuilder.ToBitmap(TgaImageDecoder.Decode(bytes, entries.Diffuse.Name));
                else diffuse = DecodeGdi(bytes);

                if (entries.Normal is not null)
                {
                    byte[] normalBytes = archive.GetEntryBytes(entries.Normal);
                    normal = entries.Normal.Name.EndsWith(".TGA", StringComparison.OrdinalIgnoreCase)
                        ? ImageBuilder.ToBitmap(TgaImageDecoder.Decode(normalBytes, entries.Normal.Name))
                        : DecodeGdi(normalBytes);
                }

                if (!raw && !ValidHdSize(diffuse)) warning = entries.Diffuse.Name + " is " + diffuse.Width + "x" + diffuse.Height
                    + "; the engine will resample it to a square power-of-two size in 32..1024";
                if (normal is not null && !ValidHdSize(normal)) warning = entries.Normal!.Name + " is " + normal.Width + "x" + normal.Height
                    + "; the engine will resample it to a square power-of-two size in 32..1024";
            }
            catch (Exception ex) when (ex is ArgumentException or OutOfMemoryException or NotSupportedException)
            {
                diffuse?.Dispose(); normal?.Dispose(); diffuse = null; normal = null;
                warning = textureName + ": " + ex.Message;
            }
            loaded.Add(new LoadedBinTexture(textureName, entries.Diffuse, diffuse, normal, raw, usedFallback, warning));
        }
        return loaded;
    }

    private static Bitmap DecodeGdi(byte[] bytes)
    {
        using MemoryStream stream = new(bytes, writable: false);
        using Image image = Image.FromStream(stream, true, true);
        return new Bitmap(image);
    }

    private static bool ValidHdSize(Bitmap bitmap) => bitmap.Width == bitmap.Height && bitmap.Width is >= 32 and <= 1024
        && (bitmap.Width & (bitmap.Width - 1)) == 0;
}
