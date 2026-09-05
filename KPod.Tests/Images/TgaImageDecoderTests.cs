using KPod.Core.Images;

namespace KPod.Tests.Images;

public class TgaImageDecoderTests
{
    [Theory]
    [InlineData(0x20, 1, 2)]
    [InlineData(0x30, 2, 1)]
    [InlineData(0x00, 3, 4)]
    [InlineData(0x10, 4, 3)]
    public void AppliesAllOriginFlags(int descriptor, int first, int second)
    {
        byte[] data = Header(2, 2, 2, 24, descriptor, 12);
        AddPixels(data, (1, 0, 0), (2, 0, 0), (3, 0, 0), (4, 0, 0));

        DecodedImage image = TgaImageDecoder.Decode(data);

        Assert.Equal(first, (image.Pixels[0] >> 16) & 0xFF);
        Assert.Equal(second, (image.Pixels[1] >> 16) & 0xFF);
    }

    [Fact]
    public void DecodesRleAndAlpha()
    {
        byte[] data = Header(10, 3, 1, 32, 0x20, 6);
        int offset = 18;
        data[offset++] = 0x82;
        data[offset++] = 30; data[offset++] = 20; data[offset++] = 10; data[offset] = 40;

        DecodedImage image = TgaImageDecoder.Decode(data, "alpha.tga");

        Assert.All(image.Pixels, pixel => Assert.Equal(unchecked((int)0x280A141E), pixel));
    }

    [Fact]
    public void RejectsUnsupportedAndTruncatedPayloads()
    {
        byte[] mapped = Header(1, 1, 1, 24, 0x20);
        Assert.Throws<ArgumentException>(() => TgaImageDecoder.Decode(mapped));
        Assert.Throws<ArgumentException>(() => TgaImageDecoder.Decode(new byte[17]));
        Assert.Throws<ArgumentException>(() => TgaImageDecoder.Decode(Header(2, 1, 1, 24, 0x20)));
    }

    private static byte[] Header(int type, int width, int height, int bits, int descriptor, int extra = 0)
    {
        byte[] data = new byte[18 + extra];
        data[2] = (byte)type;
        data[12] = (byte)width; data[13] = (byte)(width >> 8);
        data[14] = (byte)height; data[15] = (byte)(height >> 8);
        data[16] = (byte)bits; data[17] = (byte)descriptor;
        return data;
    }

    private static void AddPixels(byte[] data, params (byte R, byte G, byte B)[] pixels)
    {
        int offset = 18;
        foreach ((byte r, byte g, byte b) in pixels)
        {
            data[offset++] = b; data[offset++] = g; data[offset++] = r;
        }
    }
}
