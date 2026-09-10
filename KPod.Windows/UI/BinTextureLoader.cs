using KPod.Core.Images;
using KPod.Core.Models;
using KPod.Core.Pods;

namespace KPod.Windows.UI;

internal sealed record LoadedBinTexture(
    string Name, PodEntry? Entry, Bitmap? Diffuse, Bitmap? Normal, bool IsRaw, bool UsedFallback, string? Warning)
{
    /// <summary>
    /// Whether this texture carries a real alpha channel: a 4x4 Evolution .RAW with its
    /// .OPA opacity plane merged in, or a two-sample .TIF. Such art is alpha-tested on its
    /// own channel and never colour-keyed.
    /// </summary>
    public bool HasAlpha { get; init; }
}

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
            bool hasAlpha = false;
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
                    DecodedImage decoded = RawImageDecoder.DecodeRaw(bytes, palette, dimensions.Value.Width, dimensions.Value.Height);
                    /*
                      A 4x4 Evolution .OPA is the texture's opacity plane, paired by stem. It
                      is a real 0..255 gradient rather than a mask, so it is merged into the
                      alpha channel instead of being reduced to the MTM colour key, which
                      would harden every soft foliage edge into a stencil. Archives without
                      one are unaffected.
                    */
                    if (entries.OpacityPlane is not null)
                    {
                        DecodedImage merged = RawImageDecoder.ApplyOpacityPlane(
                            decoded, archive.GetEntryBytes(entries.OpacityPlane));
                        hasAlpha = !ReferenceEquals(merged, decoded);
                        decoded = merged;
                    }
                    diffuse = ImageBuilder.ToBitmap(decoded);
                }
                else if (entries.Diffuse.Name.EndsWith(".TIF", StringComparison.OrdinalIgnoreCase))
                {
                    // Evo 2 model art is palette-indexed TIFF, which GDI+ will open but not
                    // in the form the game stores it: its second sample is an opacity plane
                    // rather than a channel GDI+ knows about.
                    diffuse = ImageBuilder.ToBitmap(TiffImageDecoder.Decode(bytes, entries.Diffuse.Name));
                    hasAlpha = TiffImageDecoder.HasAlphaSample(bytes, entries.Diffuse.Name);
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

                // The HD size rule is about PNG/TGA replacements. A .TIF is the game's own
                // art in its own format, so it is exempt the way a .RAW is.
                bool hdSource = !raw && !entries.Diffuse.Name.EndsWith(".TIF", StringComparison.OrdinalIgnoreCase);
                if (hdSource && !ValidHdSize(diffuse)) warning = entries.Diffuse.Name + " is " + diffuse.Width + "x" + diffuse.Height
                    + "; the engine will resample it to a square power-of-two size in 32..1024";
                if (normal is not null && !ValidHdSize(normal)) warning = entries.Normal!.Name + " is " + normal.Width + "x" + normal.Height
                    + "; the engine will resample it to a square power-of-two size in 32..1024";
            }
            catch (Exception ex) when (ex is ArgumentException or OutOfMemoryException or NotSupportedException)
            {
                diffuse?.Dispose(); normal?.Dispose(); diffuse = null; normal = null;
                warning = textureName + ": " + ex.Message;
            }
            loaded.Add(new LoadedBinTexture(textureName, entries.Diffuse, diffuse, normal, raw, usedFallback, warning)
            {
                HasAlpha = hasAlpha,
            });
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
