namespace KPod.Core.Images;

/// <summary>
/// Minimal TIFF reader for the palette-indexed art 4x4 Evolution 2 keeps its model and
/// vegetation textures in.
///
/// <para>Only the forms the game actually ships are supported, verified across every .TIF
/// entry of the BAJBEACH and PEAK stock tracks:</para>
/// <list type="bullet">
///   <item>little-endian ("II", magic 42), uncompressed (Compression 1)</item>
///   <item>PhotometricInterpretation 3 (palette colour) with a ColorMap</item>
///   <item>PlanarConfiguration 1 (chunky), or absent, which means 1</item>
///   <item>SamplesPerPixel 1, index only, fully opaque</item>
///   <item>SamplesPerPixel 2, index plus a second 8-bit sample used as opacity</item>
/// </list>
///
/// <para>Anything else is refused with a reason rather than decoded into plausible-looking
/// garbage: a viewer that silently shows the wrong texture is worse than one that says it
/// cannot read the file.</para>
///
/// <para>ColorMap stores three consecutive runs - all reds, then all greens, then all blues -
/// of 16-bit values, not interleaved RGB triples. Both that and the 16-bit scaling are easy
/// to get subtly wrong and produce a washed-out or channel-swapped image.</para>
/// </summary>
public static class TiffImageDecoder
{
    private const ushort LittleEndianOrder = 0x4949;
    private const ushort BigEndianOrder = 0x4D4D;
    private const ushort TiffMagic = 42;

    private const ushort TagImageWidth = 256;
    private const ushort TagImageLength = 257;
    private const ushort TagBitsPerSample = 258;
    private const ushort TagCompression = 259;
    private const ushort TagPhotometric = 262;
    private const ushort TagStripOffsets = 273;
    private const ushort TagSamplesPerPixel = 277;
    private const ushort TagRowsPerStrip = 278;
    private const ushort TagPlanarConfiguration = 284;
    private const ushort TagColorMap = 320;

    private const int CompressionNone = 1;
    private const int PhotometricPalette = 3;
    private const int PlanarChunky = 1;

    /// <summary>True when these bytes begin a TIFF header this decoder should be given.</summary>
    public static bool IsTiff(byte[] data)
    {
        if (data is null || data.Length < 8) return false;
        ushort order = (ushort)((data[0] << 8) | data[1]);
        if (order != LittleEndianOrder && order != BigEndianOrder) return false;
        return ReadUInt16(data, 2, order == LittleEndianOrder) == TiffMagic;
    }

    /// <summary>Decodes one palette TIFF, with the second sample as alpha when present.</summary>
    public static DecodedImage Decode(byte[] data, string name)
    {
        if (!IsTiff(data)) throw new ArgumentException(name + ": not a TIFF");
        bool little = ((ushort)((data[0] << 8) | data[1])) == LittleEndianOrder;

        uint ifdOffset = ReadUInt32(data, 4, little);
        Dictionary<ushort, TiffField> tags = ReadIfd(data, (int)ifdOffset, little, name);

        int width = (int)Scalar(tags, TagImageWidth, 0);
        int height = (int)Scalar(tags, TagImageLength, 0);
        int compression = (int)Scalar(tags, TagCompression, CompressionNone);
        int photometric = (int)Scalar(tags, TagPhotometric, -1);
        int samplesPerPixel = (int)Scalar(tags, TagSamplesPerPixel, 1);
        int planar = (int)Scalar(tags, TagPlanarConfiguration, PlanarChunky);

        if (width <= 0 || height <= 0) throw new ArgumentException(name + ": TIFF has no dimensions");
        if (compression != CompressionNone)
            throw new ArgumentException(name + ": unsupported TIFF compression " + compression);
        if (photometric != PhotometricPalette)
            throw new ArgumentException(name + ": unsupported TIFF photometric " + photometric
                + " (only palette colour is handled)");
        if (planar != PlanarChunky)
            throw new ArgumentException(name + ": unsupported TIFF planar configuration " + planar);
        if (samplesPerPixel is not (1 or 2))
            throw new ArgumentException(name + ": unsupported TIFF samples/pixel " + samplesPerPixel);
        if (tags.TryGetValue(TagBitsPerSample, out TiffField? bits) && bits.Values.Any(v => v != 8))
            throw new ArgumentException(name + ": unsupported TIFF bit depth "
                + string.Join("/", bits.Values));

        if (!tags.TryGetValue(TagColorMap, out TiffField? colorMap) || colorMap.Values.Count < 768)
            throw new ArgumentException(name + ": TIFF palette image has no usable ColorMap");

        /*
          ColorMap entries are nominally 0..65535, but some writers store 0..255 in the low
          byte. If nothing in the map exceeds 255 it is read as already 8-bit rather than
          crushed to near-black.
        */
        int entries = colorMap.Values.Count / 3;
        bool sixteenBit = colorMap.Values.Any(v => v > 255);
        byte[] palette = new byte[entries * 3];
        for (int i = 0; i < entries; i++)
        {
            palette[i * 3] = (byte)(sixteenBit ? colorMap.Values[i] >> 8 : colorMap.Values[i]);
            palette[i * 3 + 1] = (byte)(sixteenBit ? colorMap.Values[entries + i] >> 8 : colorMap.Values[entries + i]);
            palette[i * 3 + 2] = (byte)(sixteenBit ? colorMap.Values[entries * 2 + i] >> 8 : colorMap.Values[entries * 2 + i]);
        }

        IReadOnlyList<long> stripOffsets = tags.TryGetValue(TagStripOffsets, out TiffField? so)
            ? so.Values : throw new ArgumentException(name + ": TIFF has no strip offsets");
        int rowsPerStrip = (int)Scalar(tags, TagRowsPerStrip, height);
        if (rowsPerStrip <= 0) rowsPerStrip = height;

        int[] pixels = new int[width * height];
        int rowBytes = width * samplesPerPixel;
        int row = 0;
        for (int strip = 0; strip < stripOffsets.Count && row < height; strip++)
        {
            long offset = stripOffsets[strip];
            int rowsHere = Math.Min(rowsPerStrip, height - row);
            for (int r = 0; r < rowsHere; r++, row++)
            {
                long src = offset + (long)r * rowBytes;
                if (src < 0 || src + rowBytes > data.Length)
                    throw new ArgumentException(name + ": truncated TIFF strip " + strip);
                for (int x = 0; x < width; x++)
                {
                    int index = data[src + (long)x * samplesPerPixel];
                    int alpha = samplesPerPixel == 2 ? data[src + (long)x * samplesPerPixel + 1] : 255;
                    int entry = Math.Min(index, entries - 1) * 3;
                    pixels[row * width + x] = (alpha << 24)
                        | (palette[entry] << 16) | (palette[entry + 1] << 8) | palette[entry + 2];
                }
            }
        }
        return new DecodedImage(width, height, pixels);
    }

    /// <summary>True when this TIFF carries the second sample Evo uses as an opacity plane.</summary>
    public static bool HasAlphaSample(byte[] data, string name)
    {
        if (!IsTiff(data)) return false;
        bool little = ((ushort)((data[0] << 8) | data[1])) == LittleEndianOrder;
        try
        {
            Dictionary<ushort, TiffField> tags = ReadIfd(data, (int)ReadUInt32(data, 4, little), little, name);
            return Scalar(tags, TagSamplesPerPixel, 1) == 2;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private sealed record TiffField(ushort Type, uint Count, IReadOnlyList<long> Values);

    private static Dictionary<ushort, TiffField> ReadIfd(byte[] data, int offset, bool little, string name)
    {
        if (offset < 0 || offset + 2 > data.Length)
            throw new ArgumentException(name + ": TIFF IFD lies outside the file");
        int count = ReadUInt16(data, offset, little);
        Dictionary<ushort, TiffField> tags = [];
        for (int i = 0; i < count; i++)
        {
            int record = offset + 2 + i * 12;
            if (record + 12 > data.Length) break;
            ushort tag = ReadUInt16(data, record, little);
            ushort type = ReadUInt16(data, record + 2, little);
            uint length = ReadUInt32(data, record + 4, little);
            int size = TypeSize(type);
            if (size == 0) continue;

            long totalBytes = (long)size * length;
            long valueOffset = totalBytes <= 4 ? record + 8 : ReadUInt32(data, record + 8, little);
            if (valueOffset < 0 || valueOffset + totalBytes > data.Length) continue;

            // A ColorMap is 3 * 2^bits entries; reading every value of some unrelated huge
            // tag would be wasteful, so anything past a full 16-bit palette is left unread.
            int limit = (int)Math.Min(length, 4096);
            List<long> values = new(limit);
            for (int v = 0; v < limit; v++)
            {
                long at = valueOffset + (long)v * size;
                values.Add(type switch
                {
                    3 => ReadUInt16(data, (int)at, little),
                    4 => ReadUInt32(data, (int)at, little),
                    1 or 7 => data[at],
                    8 => (short)ReadUInt16(data, (int)at, little),
                    9 => (int)ReadUInt32(data, (int)at, little),
                    _ => 0,
                });
            }
            tags[tag] = new TiffField(type, length, values);
        }
        return tags;
    }

    private static long Scalar(Dictionary<ushort, TiffField> tags, ushort tag, long fallback) =>
        tags.TryGetValue(tag, out TiffField? field) && field.Values.Count > 0 ? field.Values[0] : fallback;

    private static int TypeSize(ushort type) => type switch
    {
        1 or 2 or 6 or 7 => 1,
        3 or 8 => 2,
        4 or 9 or 11 => 4,
        5 or 10 or 12 => 8,
        _ => 0,
    };

    // Read byte by byte rather than through BinaryPrimitives, which the .NET Framework leg
    // of this library does not have. Same idiom as TgaImageDecoder.
    private static ushort ReadUInt16(byte[] data, int offset, bool little) => (ushort)(little
        ? data[offset] | (data[offset + 1] << 8)
        : (data[offset] << 8) | data[offset + 1]);

    private static uint ReadUInt32(byte[] data, int offset, bool little) => little
        ? (uint)(data[offset] | (data[offset + 1] << 8) | (data[offset + 2] << 16) | (data[offset + 3] << 24))
        : (uint)((data[offset] << 24) | (data[offset + 1] << 16) | (data[offset + 2] << 8) | data[offset + 3]);
}
