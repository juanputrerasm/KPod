namespace KPod.Core.Images;

/// <summary>A decoded image as 32-bit ARGB pixels, ready for the UI to blit.</summary>
public sealed record DecodedImage(int Width, int Height, int[] Pixels);

/// <summary>
/// Decodes Terminal Reality raw image and palette formats.
///
/// <para>RAW format (<c>art\*.raw</c>, <c>data\*.raw</c>, <c>data\*.clr</c>): a flat
/// array of 8-bit palette indices with no header. Dimensions are inferred from the
/// file size: 4 096 bytes is 64x64 (art texture), 65 536 bytes is 256x256
/// (heightmap or colour-lookup table). Exact-square payloads fall back to
/// side x side; non-square payloads need the caller to choose.</para>
///
/// <para>ACT format (<c>art\*.act</c>): 768 bytes, 256 colours of 3 bytes each.
/// Terminal Reality stores 6-bit VGA values (0-63); 8-bit Adobe ACT files are
/// detected automatically when any channel exceeds 63.</para>
///
/// <para>This type never touches System.Drawing: it returns pixel arrays so the
/// core library stays runnable on any OS.</para>
/// </summary>
public static class RawImageDecoder
{
    /// <summary>Standard art-texture size: 64x64.</summary>
    public const int ArtTextureSide = 64;

    /// <summary>Standard heightmap / CLR size: 256x256.</summary>
    public const int LargeImageSide = 256;

    /// <summary>Bytes in an ACT palette file: 256 colours times 3 channels.</summary>
    public const int ActPaletteBytes = 768;

    private const string BundledPaletteResource = "KPod.Core.Assets.metalcr2.act";

    private static int[]? _resourcePalette;

    /// <summary>
    /// Decodes a 768-byte ACT palette into 256 ARGB values with full alpha.
    ///
    /// <para>Encoding is auto-detected. Every channel at or below 63 means 6-bit
    /// VGA, scaled with the exact formula <c>(v * 255 + 31) / 63</c> so 0 maps to 0
    /// and 63 maps to 255 with no clamping artefacts. Any channel above 63 means an
    /// 8-bit Adobe ACT, whose values are used directly.</para>
    /// </summary>
    public static int[] DecodeAct(byte[] actBytes)
    {
        if (actBytes.Length < ActPaletteBytes)
        {
            throw new ArgumentException(
                "ACT file must be at least 768 bytes, got " + actBytes.Length, nameof(actBytes));
        }

        bool is8Bit = false;
        for (int i = 0; i < ActPaletteBytes; i++)
        {
            if (actBytes[i] > 63)
            {
                is8Bit = true;
                break;
            }
        }

        int[] palette = new int[256];
        for (int i = 0; i < 256; i++)
        {
            int rv = actBytes[i * 3];
            int gv = actBytes[(i * 3) + 1];
            int bv = actBytes[(i * 3) + 2];
            int r, g, b;
            if (is8Bit)
            {
                r = rv;
                g = gv;
                b = bv;
            }
            else
            {
                r = ((rv * 255) + 31) / 63;
                g = ((gv * 255) + 31) / 63;
                b = ((bv * 255) + 31) / 63;
            }

            palette[i] = unchecked((int)0xFF000000) | (r << 16) | (g << 8) | b;
        }

        return palette;
    }

    /// <summary>A palette where index i maps to RGB(i, i, i), the no-ACT fallback.</summary>
    public static int[] GreyscalePalette()
    {
        int[] palette = new int[256];
        for (int i = 0; i < 256; i++)
        {
            palette[i] = unchecked((int)0xFF000000) | (i << 16) | (i << 8) | i;
        }

        return palette;
    }

    /// <summary>Decodes a raw 8-bit paletted image into ARGB pixels.</summary>
    public static DecodedImage DecodeRaw(byte[] rawBytes, int[] palette, int width, int height)
    {
        long required = (long)width * height;
        if (rawBytes.Length < required)
        {
            throw new ArgumentException(
                "RAW data too short: expected " + required + " bytes, got " + rawBytes.Length,
                nameof(rawBytes));
        }

        int[] pixels = new int[width * height];
        for (int i = 0; i < pixels.Length; i++)
        {
            pixels[i] = palette[rawBytes[i]];
        }

        return new DecodedImage(width, height, pixels);
    }

    /// <summary>
    /// Infers image dimensions from the file size, or returns null when the size
    /// is not one this decoder recognizes.
    /// </summary>
    public static (int Width, int Height)? DetectDimensions(int byteCount)
    {
        if (byteCount == ArtTextureSide * ArtTextureSide)
        {
            return (ArtTextureSide, ArtTextureSide);
        }

        if (byteCount == LargeImageSide * LargeImageSide)
        {
            return (LargeImageSide, LargeImageSide);
        }

        int side = (int)Math.Sqrt(byteCount);
        return side > 0 && side * side == byteCount ? (side, side) : null;
    }

    /// <summary>
    /// Exact width and height pairs whose product matches <paramref name="byteCount"/>,
    /// ordered from most square-like to most elongated.
    /// </summary>
    public static IReadOnlyList<(int Width, int Height)> SuggestDimensions(int byteCount)
    {
        List<(int Width, int Height)> suggestions = [];
        if (byteCount <= 0)
        {
            return suggestions;
        }

        for (int width = 1; (long)width * width <= byteCount; width++)
        {
            if (byteCount % width != 0)
            {
                continue;
            }

            suggestions.Add((width, byteCount / width));
        }

        suggestions.Sort((a, b) =>
        {
            int byAspect = Math.Abs(a.Width - a.Height).CompareTo(Math.Abs(b.Width - b.Height));
            return byAspect != 0
                ? byAspect
                : Math.Max(a.Width, a.Height).CompareTo(Math.Max(b.Width, b.Height));
        });
        return suggestions;
    }

    /// <summary>
    /// The width and height to preselect for a non-standard RAW payload. The three
    /// common VGA and SVGA screen sizes win over the most square-like factor pair.
    /// </summary>
    public static (int Width, int Height) PreferredDimensions(int byteCount)
    {
        switch (byteCount)
        {
            case 64000:
                return (320, 200);
            case 256000:
                return (640, 400);
            case 307200:
                return (640, 480);
        }

        IReadOnlyList<(int Width, int Height)> suggestions = SuggestDimensions(byteCount);
        return suggestions.Count > 0 ? suggestions[0] : (byteCount, 1);
    }

    /// <summary>True when the name has a raw image extension this decoder supports.</summary>
    public static bool IsRawImage(string name)
    {
        string upper = name.ToUpperInvariant();
        return upper.EndsWith(".RAW", StringComparison.Ordinal)
            || upper.EndsWith(".CLR", StringComparison.Ordinal);
    }

    /// <summary>True when the name has an ACT palette extension.</summary>
    public static bool IsActPalette(string name) =>
        name.ToUpperInvariant().EndsWith(".ACT", StringComparison.Ordinal);

    /// <summary>True when the extension suggests plain text content.</summary>
    public static bool IsTextFile(string name)
    {
        string upper = name.ToUpperInvariant();
        foreach (string extension in TextExtensions)
        {
            if (upper.EndsWith(extension, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static readonly string[] TextExtensions =
    [
        ".TXT", ".DEF", ".NAV", ".TDF", ".TEX", ".LVL", ".INI", ".LST", ".INF",
        ".CFG", ".VOX", ".SIT", ".TRN", ".NDX", ".TNL", ".TTX", ".TRK",
    ];

    /// <summary>
    /// The MTM1 default Metal Crusher palette, embedded in the assembly. Falls back
    /// to greyscale when the resource cannot be read.
    /// </summary>
    public static int[] LoadResourcePalette()
    {
        if (_resourcePalette is not null)
        {
            return (int[])_resourcePalette.Clone();
        }

        int[] palette = GreyscalePalette();
        try
        {
            using Stream? stream = typeof(RawImageDecoder).Assembly
                .GetManifestResourceStream(BundledPaletteResource);
            if (stream is not null)
            {
                byte[] bytes = new byte[ActPaletteBytes];
                int read = 0;
                while (read < bytes.Length)
                {
                    int chunk = stream.Read(bytes, read, bytes.Length - read);
                    if (chunk <= 0)
                    {
                        break;
                    }

                    read += chunk;
                }

                if (read == bytes.Length)
                {
                    palette = DecodeAct(bytes);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or NotSupportedException)
        {
            // A missing or unreadable palette is not worth failing a preview over.
        }

        _resourcePalette = palette;
        return (int[])palette.Clone();
    }
}
