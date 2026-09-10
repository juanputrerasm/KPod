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

    /// <summary>
    /// Evo 2 model art is palette-indexed .TIF. It ranks below the HD replacements and
    /// above .RAW, which is the order a material names them in.
    /// </summary>
    [Fact]
    public void PrefersTiffOverRawAndHdOverTiff()
    {
        using PodArchive tiffOnly = PodArchiveReader.Read(PodFixture.BuildPod1(
            new PodFile(@"ART\PALMFAN.RAW", [1]), new PodFile(@"ART\PALMFAN.TIF", [2])));
        Assert.Equal(@"ART\PALMFAN.TIF", BinTextureResolver.Resolve(tiffOnly, "PALMFAN.TIF").Diffuse!.Name);

        using PodArchive withHd = PodArchiveReader.Read(PodFixture.BuildPod1(
            new PodFile(@"ART\PALMFAN.RAW", [1]), new PodFile(@"ART\PALMFAN.TIF", [2]),
            new PodFile(@"ART\PALMFAN.PNG", [3])));
        Assert.Equal(@"ART\PALMFAN.PNG", BinTextureResolver.Resolve(withHd, "PALMFAN.TIF").Diffuse!.Name);
    }

    /// <summary>
    /// A 4x4 Evolution .OPA pairs with its texture by stem and only alongside an indexed
    /// .RAW; there is nothing for it to modulate on a true-colour source.
    /// </summary>
    [Fact]
    public void FindsTheOpacityPlaneBesideAnIndexedTexture()
    {
        using PodArchive archive = PodArchiveReader.Read(PodFixture.BuildPod1(
            new PodFile(@"ART\AS3PINE1.RAW", [1]), new PodFile(@"ART\AS3PINE1.ACT", new byte[768]),
            new PodFile(@"ART\AS3PINE1.OPA", [2])));

        BinTextureEntries result = BinTextureResolver.Resolve(archive, "AS3PINE1.RAW");

        Assert.Equal(@"ART\AS3PINE1.OPA", result.OpacityPlane!.Name);
    }

    [Fact]
    public void HasNoOpacityPlaneWhenTheArchiveCarriesNone()
    {
        using PodArchive archive = PodArchiveReader.Read(PodFixture.BuildPod1(
            new PodFile(@"ART\BODY.RAW", [1]), new PodFile(@"ART\BODY.ACT", new byte[768])));

        Assert.Null(BinTextureResolver.Resolve(archive, "BODY.RAW").OpacityPlane);
    }
}
