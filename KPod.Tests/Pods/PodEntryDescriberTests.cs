using KPod.Core.Pods;

namespace KPod.Tests.Pods;

public class PodEntryDescriberTests
{
    [Theory]
    [InlineData("VGA.ACT", "VGA palette")]
    [InlineData(@"ART\WALL.BMP", "Bitmap image")]
    [InlineData("SONG.MOD", "Module audio (MOD)")]
    [InlineData("SKY.TGA", "Targa image")]
    [InlineData("CAR.200", "320x200 cockpit data")]
    [InlineData("SCRIPT.JSIN", "JSON instrument data")]
    [InlineData("NOTES", "Binary data")]
    [InlineData("THING.QQQ", "QQQ file")]
    [InlineData(@"DATA\LAGUNA.SI2", "Track situation file (CommPatch 3+ engine)")]
    [InlineData(@"DATA\LAGUNA.SIX", "Track situation file (disabled)")]
    [InlineData(@"DATA\LAGUNA.SIY", "Track situation file (CommPatch 3+ engine, disabled)")]
    [InlineData(@"DATA\LAGUNA.TXV", "Track version manifest")]
    public void DescribesByExtension(string name, string expected) =>
        Assert.Equal(expected, PodEntryDescriber.Describe(name, 0));

    [Fact]
    public void TrkIsATruckOutsideDataAndATrackInsideIt()
    {
        Assert.Equal("Truck definition file", PodEntryDescriber.Describe(@"TRUCKS\BIGFOOT.TRK", 0));
        Assert.Equal("CPR track definition file", PodEntryDescriber.Describe(@"DATA\LAGUNA.TRK", 0));
    }

    [Fact]
    public void TrxIsATruckOutsideDataAndATrackInsideIt()
    {
        Assert.Equal("Truck definition file (disabled)",
            PodEntryDescriber.Describe(@"TRUCKS\BIGFOOT.TRX", 0));
        Assert.Equal("CPR track definition file (disabled)",
            PodEntryDescriber.Describe(@"DATA\LAGUNA.TRX", 0));
    }

    [Theory]
    [InlineData(@"ART\ROCK_N.TGA", "Targa image (normal map)")]
    [InlineData(@"ART\ROCK_AO.PNG", "PNG image (ambient occlusion)")]
    [InlineData(@"ART\ROCK_DTL.PNG", "PNG image (terrain detail normal)")]
    [InlineData(@"ART\ROCK_MASK.PNG", "PNG image (terrain detail mask)")]
    [InlineData(@"ART\ROCK.PNG", "PNG image")]
    [InlineData(@"ART\_N.PNG", "PNG image")]
    public void ArtNamesCarryTheirMapRole(string name, string expected) =>
        Assert.Equal(expected, PodEntryDescriber.Describe(name, 0));

    [Fact]
    public void ARawMapRoleJoinsTheDimensionsInsideOneBracket() =>
        Assert.Equal("RAW image data (256x256, normal map)",
            PodEntryDescriber.Describe(@"ART\ROCK_N.RAW", 65536));

    [Theory]
    [InlineData(4096, "RAW image data (64x64)")]
    [InlineData(65536, "RAW image data (256x256)")]
    [InlineData(1024, "RAW image data (non-standard 32x32)")]
    [InlineData(64000, "RAW image data (320x200)")]
    [InlineData(256000, "RAW image data (640x400)")]
    [InlineData(307200, "RAW image data (640x480)")]
    public void RawSizeDrivesTheRawDescription(int byteCount, string expected) =>
        Assert.Equal(expected, PodEntryDescriber.Describe("TEX.RAW", byteCount));
}
