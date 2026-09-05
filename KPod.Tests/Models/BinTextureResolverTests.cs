using KPod.Core.Models;
using KPod.Core.Pods;
using KPod.Tests.Pods;

namespace KPod.Tests.Models;

public class BinTextureResolverTests
{
    [Fact]
    public void PrefersPngThenTgaThenRawAndFindsNormalMap()
    {
        using PodArchive archive = PodArchiveReader.Read(PodFixture.BuildPod1(
            new PodFile(@"ART\BODY.RAW", [1]), new PodFile(@"ART\BODY.TGA", [2]),
            new PodFile(@"ART\BODY.PNG", [3]), new PodFile(@"ART\BODY.ACT", new byte[768]),
            new PodFile(@"ART\BODY_N.TGA", [4]), new PodFile(@"ART\BODY_N.PNG", [5])));

        BinTextureEntries result = BinTextureResolver.Resolve(archive, "models/body.raw");

        Assert.Equal(@"ART\BODY.PNG", result.Diffuse!.Name);
        Assert.Equal(@"ART\BODY_N.PNG", result.Normal!.Name);
        Assert.Equal(@"ART\BODY.ACT", result.SameNamePalette!.Name);
    }

    [Fact]
    public void SearchesKnownFoldersBeforeTitleFallback()
    {
        using PodArchive archive = PodArchiveReader.Read(PodFixture.BuildPod1(
            new PodFile(@"OTHER\STONE.TGA", [1]), new PodFile(@"DATA\STONE.TGA", [2])));

        Assert.Equal(@"DATA\STONE.TGA", BinTextureResolver.Resolve(archive, "STONE").Diffuse!.Name);
    }

    [Fact]
    public void SameDirectoryPaletteWinsOverAnotherFoldersSameTitle()
    {
        using PodArchive archive = PodArchiveReader.Read(PodFixture.BuildPod1(
            new PodFile(@"ART\BODY.ACT", new byte[768]),
            new PodFile(@"DATA\BODY.RAW", [1]),
            new PodFile(@"DATA\BODY.ACT", new byte[768])));

        BinTextureEntries result = BinTextureResolver.Resolve(archive, "BODY");

        Assert.Equal(@"DATA\BODY.RAW", result.Diffuse!.Name);
        Assert.Equal(@"DATA\BODY.ACT", result.SameNamePalette!.Name);
    }
}
