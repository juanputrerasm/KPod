using KPod.Core.Images;

namespace KPod.Tests.Images;

public class TiffImageDecoderTests
{
    [Fact]
    public void DecodesASingleSamplePaletteImageAsFullyOpaque()
    {
        DecodedImage image = TiffImageDecoder.Decode(BuildTiff(samplesPerPixel: 1), "ROCK.TIF");

        Assert.Equal(2, image.Width);
        Assert.Equal(2, image.Height);
        // Palette entry 1 is pure red, entry 0 is black; the sample grid is 0,1 / 1,0.
        Assert.Equal(unchecked((int)0xFF000000), image.Pixels[0]);
        Assert.Equal(unchecked((int)0xFFFF0000), image.Pixels[1]);
        Assert.False(TiffImageDecoder.HasAlphaSample(BuildTiff(samplesPerPixel: 1), "ROCK.TIF"));
    }

    /// <summary>
    /// The second sample of an Evo 2 texture is its opacity plane. It is a real gradient, so
    /// it reaches the alpha channel intact rather than being reduced to a mask.
    /// </summary>
    [Fact]
    public void UsesTheSecondSampleAsAlpha()
    {
        DecodedImage image = TiffImageDecoder.Decode(BuildTiff(samplesPerPixel: 2), "PALM.TIF");

        Assert.Equal(0x00, (image.Pixels[0] >> 24) & 0xFF);
        Assert.Equal(0x40, (image.Pixels[1] >> 24) & 0xFF);
        Assert.Equal(0x80, (image.Pixels[2] >> 24) & 0xFF);
        Assert.Equal(0xFF, (image.Pixels[3] >> 24) & 0xFF);
        Assert.True(TiffImageDecoder.HasAlphaSample(BuildTiff(samplesPerPixel: 2), "PALM.TIF"));
    }

    /// <summary>
    /// A ColorMap is three consecutive runs of 16-bit values - all reds, then greens, then
    /// blues - not interleaved triples. Reading it as triples swaps channels.
    /// </summary>
    [Fact]
    public void ReadsTheColorMapAsThreeRunsRatherThanInterleavedTriples()
    {
        DecodedImage image = TiffImageDecoder.Decode(BuildTiff(samplesPerPixel: 1), "ROCK.TIF");

        Assert.Equal(0xFF, (image.Pixels[1] >> 16) & 0xFF);
        Assert.Equal(0x00, (image.Pixels[1] >> 8) & 0xFF);
        Assert.Equal(0x00, image.Pixels[1] & 0xFF);
    }

    [Fact]
    public void RefusesCompressedData() =>
        Assert.Throws<ArgumentException>(() =>
            TiffImageDecoder.Decode(BuildTiff(samplesPerPixel: 1, compression: 5), "LZW.TIF"));

    [Fact]
    public void RefusesANonPaletteImage() =>
        Assert.Throws<ArgumentException>(() =>
            TiffImageDecoder.Decode(BuildTiff(samplesPerPixel: 1, photometric: 2), "RGB.TIF"));

    [Fact]
    public void RefusesAPlanarImage() =>
        Assert.Throws<ArgumentException>(() =>
            TiffImageDecoder.Decode(BuildTiff(samplesPerPixel: 2, planar: 2), "PLANAR.TIF"));

    [Fact]
    public void RefusesWhatIsNotATiff()
    {
        Assert.False(TiffImageDecoder.IsTiff([0, 1, 2, 3, 4, 5, 6, 7]));
        Assert.Throws<ArgumentException>(() => TiffImageDecoder.Decode([0, 1, 2, 3, 4, 5, 6, 7], "NOPE.TIF"));
    }

    [Fact]
    public void RecognisesALittleEndianTiffHeader() =>
        Assert.True(TiffImageDecoder.IsTiff(BuildTiff(samplesPerPixel: 1)));

    /// <summary>
    /// A 2x2 palette TIFF built by hand: header, pixel data, palette, then the IFD. Samples
    /// are palette indices 0,1 / 1,0, and with two samples per pixel the alpha run is
    /// 0x00, 0x40, 0x80, 0xFF.
    /// </summary>
    private static byte[] BuildTiff(int samplesPerPixel, int compression = 1, int photometric = 3, int planar = 1)
    {
        const int width = 2, height = 2;
        byte[] indices = [0, 1, 1, 0];
        byte[] alpha = [0x00, 0x40, 0x80, 0xFF];

        List<byte> pixels = [];
        for (int i = 0; i < indices.Length; i++)
        {
            pixels.Add(indices[i]);
            if (samplesPerPixel == 2) pixels.Add(alpha[i]);
        }

        // 256 entries per channel, stored as all reds, then all greens, then all blues.
        ushort[] colorMap = new ushort[256 * 3];
        colorMap[1] = 0xFFFF;                 // entry 1: red

        const int headerSize = 8;
        int pixelOffset = headerSize;
        int mapOffset = pixelOffset + pixels.Count;
        int ifdOffset = mapOffset + colorMap.Length * 2;

        (ushort Tag, ushort Type, uint Count, uint Value)[] tags =
        [
            (256, 3, 1, width),
            (257, 3, 1, height),
            (258, 3, (uint)samplesPerPixel, 8),
            (259, 3, 1, (uint)compression),
            (262, 3, 1, (uint)photometric),
            (273, 4, 1, (uint)pixelOffset),
            (277, 3, 1, (uint)samplesPerPixel),
            (278, 3, 1, height),
            (279, 4, 1, (uint)pixels.Count),
            (284, 3, 1, (uint)planar),
            (320, 3, (uint)colorMap.Length, (uint)mapOffset),
        ];

        byte[] data = new byte[ifdOffset + 2 + tags.Length * 12 + 4];
        data[0] = (byte)'I'; data[1] = (byte)'I';
        WriteUInt16(data, 2, 42);
        WriteUInt32(data, 4, (uint)ifdOffset);
        pixels.CopyTo(data, pixelOffset);
        for (int i = 0; i < colorMap.Length; i++) WriteUInt16(data, mapOffset + i * 2, colorMap[i]);

        WriteUInt16(data, ifdOffset, (ushort)tags.Length);
        for (int i = 0; i < tags.Length; i++)
        {
            int record = ifdOffset + 2 + i * 12;
            WriteUInt16(data, record, tags[i].Tag);
            WriteUInt16(data, record + 2, tags[i].Type);
            WriteUInt32(data, record + 4, tags[i].Count);
            /*
              Values small enough to fit are stored in the 4-byte value field itself, packed
              from its start. Two SHORTs therefore occupy both halves - which is how a real
              two-sample BitsPerSample is written, and writing only the first leaves the
              second reading as 0.
            */
            if (tags[i].Type == 3 && tags[i].Count <= 2)
            {
                for (uint v = 0; v < tags[i].Count; v++)
                    WriteUInt16(data, record + 8 + (int)v * 2, (ushort)tags[i].Value);
            }
            else
            {
                WriteUInt32(data, record + 8, tags[i].Value);
            }
        }
        return data;
    }

    // Written byte by byte rather than through BinaryPrimitives, which the .NET Framework
    // leg of this test project does not have.
    private static void WriteUInt16(byte[] data, int offset, ushort value)
    {
        data[offset] = (byte)value;
        data[offset + 1] = (byte)(value >> 8);
    }

    private static void WriteUInt32(byte[] data, int offset, uint value)
    {
        data[offset] = (byte)value;
        data[offset + 1] = (byte)(value >> 8);
        data[offset + 2] = (byte)(value >> 16);
        data[offset + 3] = (byte)(value >> 24);
    }
}
