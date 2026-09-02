using KPod.Core.Pods;

namespace KPod.Tests.Pods;

public class PodEntryDescriberTests
{
    [Theory]
    [InlineData("VGA.ACT", "Palette file")]
    [InlineData(@"ART\WALL.BMP", "Bitmap image")]
    [InlineData("SONG.MOD", "Music data")]
    [InlineData("NOTES", "Binary data")]
    [InlineData("THING.QQQ", "QQQ file")]
    public void DescribesByExtension(string name, string expected) =>
        Assert.Equal(expected, PodEntryDescriber.Describe(name, 0));

    [Fact]
    public void TrkIsATruckOutsideDataAndATrackInsideIt()
    {
        Assert.Equal("Truck definition file", PodEntryDescriber.Describe(@"TRUCKS\BIGFOOT.TRK", 0));
        Assert.Equal("CPR track definition file", PodEntryDescriber.Describe(@"DATA\LAGUNA.TRK", 0));
    }

    [Theory]
    [InlineData(4096, "RAW image data")]
    [InlineData(65536, "RAW image data")]
    [InlineData(1024, "RAW image data (non-standard 32x32)")]
    [InlineData(64000, "RAW image data (non-standard size)")]
    public void RawSizeDrivesTheRawDescription(int byteCount, string expected) =>
        Assert.Equal(expected, PodEntryDescriber.Describe("TEX.RAW", byteCount));
}
