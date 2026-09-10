using KPod.Core.Images;

namespace KPod.Tests.Images;

public class RawImageDecoderTests
{
    [Fact]
    public void SixBitVgaPaletteScalesZeroToZeroAndSixtyThreeToFullWhite()
    {
        byte[] act = new byte[RawImageDecoder.ActPaletteBytes];
        act[0] = 0;
        act[1] = 0;
        act[2] = 0;
        act[3] = 63;
        act[4] = 63;
        act[5] = 63;

        int[] palette = RawImageDecoder.DecodeAct(act);

        Assert.Equal(unchecked((int)0xFF000000), palette[0]);
        Assert.Equal(unchecked((int)0xFFFFFFFF), palette[1]);
    }

    [Fact]
    public void AnyChannelOverSixtyThreeMeansAnEightBitAdobeAct()
    {
        byte[] act = new byte[RawImageDecoder.ActPaletteBytes];
        act[0] = 200;
        act[1] = 100;
        act[2] = 50;

        int[] palette = RawImageDecoder.DecodeAct(act);

        Assert.Equal(unchecked((int)0xFFC86432), palette[0]);
    }

    [Fact]
    public void ShortActFileIsRejected() =>
        Assert.Throws<ArgumentException>(() => RawImageDecoder.DecodeAct(new byte[16]));

    [Fact]
    public void GreyscalePaletteRunsBlackToWhite()
    {
        int[] palette = RawImageDecoder.GreyscalePalette();

        Assert.Equal(unchecked((int)0xFF000000), palette[0]);
        Assert.Equal(unchecked((int)0xFF7F7F7F), palette[127]);
        Assert.Equal(unchecked((int)0xFFFFFFFF), palette[255]);
    }

    [Fact]
    public void DecodeRawMapsEveryIndexThroughThePalette()
    {
        int[] palette = RawImageDecoder.GreyscalePalette();
        byte[] raw = [0, 1, 2, 3];

        DecodedImage image = RawImageDecoder.DecodeRaw(raw, palette, 2, 2);

        Assert.Equal(2, image.Width);
        Assert.Equal(2, image.Height);
        Assert.Equal(palette[3], image.Pixels[3]);
    }

    [Fact]
    public void DecodeRawRejectsATooShortPayload() =>
        Assert.Throws<ArgumentException>(
            () => RawImageDecoder.DecodeRaw(new byte[3], RawImageDecoder.GreyscalePalette(), 2, 2));

    [Theory]
    [InlineData(4096, 64, 64)]
    [InlineData(65536, 256, 256)]
    [InlineData(1024, 32, 32)]
    [InlineData(64000, 320, 200)]
    [InlineData(256000, 640, 400)]
    [InlineData(307200, 640, 480)]
    public void SquarePayloadsDetectTheirOwnDimensions(int byteCount, int width, int height)
    {
        (int Width, int Height)? dims = RawImageDecoder.DetectDimensions(byteCount);

        Assert.NotNull(dims);
        Assert.Equal((width, height), dims!.Value);
    }

    [Fact]
    public void UnknownNonSquarePayloadsHaveNoDetectedDimensions() =>
        Assert.Null(RawImageDecoder.DetectDimensions(12345));

    [Theory]
    [InlineData(64000, 320, 200)]
    [InlineData(256000, 640, 400)]
    [InlineData(307200, 640, 480)]
    public void CommonScreenSizesArePreselected(int byteCount, int width, int height) =>
        Assert.Equal((width, height), RawImageDecoder.PreferredDimensions(byteCount));

    [Fact]
    public void SuggestionsRunFromMostSquareToMostElongated()
    {
        IReadOnlyList<(int Width, int Height)> suggestions = RawImageDecoder.SuggestDimensions(64);

        Assert.Equal((8, 8), suggestions[0]);
        Assert.Equal((1, 64), suggestions[suggestions.Count - 1]);
        Assert.All(suggestions, pair => Assert.Equal(64, pair.Width * pair.Height));
    }

    [Theory]
    [InlineData("ART/WALL.RAW", true)]
    [InlineData("data/terrain.clr", true)]
    [InlineData("VGA.ACT", false)]
    public void RawExtensionsAreRecognizedCaseInsensitively(string name, bool expected) =>
        Assert.Equal(expected, RawImageDecoder.IsRawImage(name));

    [Fact]
    public void TextExtensionsCoverTheGameDataFormats()
    {
        Assert.True(RawImageDecoder.IsTextFile("LAGUNA.SIT"));
        Assert.True(RawImageDecoder.IsTextFile("readme.txt"));
        Assert.False(RawImageDecoder.IsTextFile("WALL.RAW"));
        // The CommPatch extensions carry the same text payload their enabled
        // counterparts do, so the preview has to keep reading them.
        foreach (string name in new[] { "LAGUNA.SI2", "LAGUNA.SIX", "LAGUNA.SIY", "BIGFOOT.TRX", "LAGUNA.TXV" })
        {
            Assert.True(RawImageDecoder.IsTextFile(name), name);
        }
    }

    [Fact]
    public void TheBundledPaletteLoadsAndIsNotGreyscale()
    {
        int[] palette = RawImageDecoder.LoadResourcePalette();

        Assert.Equal(256, palette.Length);
        Assert.NotEqual(RawImageDecoder.GreyscalePalette(), palette);
    }

    /// <summary>
    /// A 4x4 Evolution .OPA is a real 0..255 gradient, not a mask, so every level has to
    /// survive into the alpha channel; reducing it to a key hardens soft foliage edges.
    /// </summary>
    [Fact]
    public void OpacityPlaneReachesTheAlphaChannelIntact()
    {
        DecodedImage image = new(2, 2, [unchecked((int)0xFF102030), unchecked((int)0xFF405060),
            unchecked((int)0xFF708090), unchecked((int)0xFFA0B0C0)]);

        DecodedImage merged = RawImageDecoder.ApplyOpacityPlane(image, [0x00, 0x7F, 0x80, 0xFF]);

        Assert.Equal(0x00, (merged.Pixels[0] >> 24) & 0xFF);
        Assert.Equal(0x7F, (merged.Pixels[1] >> 24) & 0xFF);
        Assert.Equal(0x80, (merged.Pixels[2] >> 24) & 0xFF);
        Assert.Equal(0xFF, (merged.Pixels[3] >> 24) & 0xFF);
        // Colour is untouched: only the alpha byte is replaced.
        Assert.Equal(0x102030, merged.Pixels[0] & 0x00FFFFFF);
    }

    /// <summary>
    /// A plane whose length does not match the image means the stem pairing found the wrong
    /// file, not that the plane needs resampling, so it is ignored rather than stretched.
    /// </summary>
    [Fact]
    public void AMismatchedOpacityPlaneIsIgnored()
    {
        DecodedImage image = new(2, 2, [-1, -1, -1, -1]);

        Assert.Same(image, RawImageDecoder.ApplyOpacityPlane(image, [0x10, 0x20]));
        Assert.Same(image, RawImageDecoder.ApplyOpacityPlane(image, null));
    }
}
