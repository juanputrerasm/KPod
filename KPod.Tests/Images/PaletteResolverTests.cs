using KPod.Core.Images;
using KPod.Core.Pods;
using KPod.Tests.Pods;

namespace KPod.Tests.Images;

public class PaletteResolverTests
{
    [Fact]
    public void TheSameNameActWinsOverEveryOtherPalette()
    {
        using TempDir temp = new();
        PodArchive archive = Archive(
            temp,
            new PodFile(@"ART\DEMO1.RAW", new byte[16]),
            new PodFile(@"ART\DEMO1.ACT", SolidAct(63, 0, 0)),
            new PodFile("VGA.ACT", SolidAct(0, 63, 0)));

        int[] palette = PaletteResolver.Resolve(@"ART\DEMO1.RAW", archive);

        Assert.Equal(unchecked((int)0xFFFF0000), palette[0]);
    }

    [Fact]
    public void ADifferentlyNamedActIsNeverGuessedAtButIsStillOffered()
    {
        using TempDir temp = new();
        PodArchive archive = Archive(
            temp,
            new PodFile(@"ART\DEMO1.RAW", new byte[16]),
            new PodFile(@"ART\OTHER.ACT", SolidAct(0, 0, 63)));

        Assert.Equal(
            RawImageDecoder.LoadResourcePalette(),
            PaletteResolver.Resolve(@"ART\DEMO1.RAW", archive));

        PaletteChoices choices = PaletteResolver.ResolveChoices(@"ART\DEMO1.RAW", archive);
        Assert.Equal("METALCR2 (MTM1)", choices.Choices[choices.DefaultIndex].Label);
        Assert.EndsWith(@"ART\OTHER.ACT", choices.Choices[^1].Label, StringComparison.Ordinal);
    }

    [Fact]
    public void TheArchivesOwnMetalcr2IsPreferredOverItsVgaAndOverTheBundledCopy()
    {
        // METALCR2 is not one palette: CPR ships a different one from MTM1 and MTM2,
        // so the copy the pod carries is the only one known to match its art.
        using TempDir temp = new();
        PodArchive archive = Archive(
            temp,
            new PodFile(@"ART\DEMO1.RAW", new byte[16]),
            new PodFile("METALCR2.ACT", SolidAct(63, 63, 0)),
            new PodFile("VGA.ACT", SolidAct(0, 63, 0)));

        Assert.Equal(unchecked((int)0xFFFFFF00), PaletteResolver.Resolve(@"ART\DEMO1.RAW", archive)[0]);
    }

    [Fact]
    public void AVgaActAloneMeansAFlightGameAndIsUsedAsIs()
    {
        using TempDir temp = new();
        PodArchive archive = Archive(
            temp,
            new PodFile(@"ART\DEMO1.RAW", new byte[16]),
            new PodFile("VGA.ACT", SolidAct(0, 63, 0)));

        Assert.Equal(unchecked((int)0xFF00FF00), PaletteResolver.Resolve(@"ART\DEMO1.RAW", archive)[0]);
    }

    [Fact]
    public void WithNoArchiveTheBundledPaletteIsUsed()
    {
        int[] palette = PaletteResolver.Resolve("LOOSE.RAW", null);

        Assert.Equal(RawImageDecoder.LoadResourcePalette(), palette);
    }

    [Fact]
    public void ChoicesPutTheSameNameActFirstAndAlwaysOfferGreyscale()
    {
        using TempDir temp = new();
        PodArchive archive = Archive(
            temp,
            new PodFile(@"ART\DEMO1.RAW", new byte[16]),
            new PodFile(@"ART\DEMO1.ACT", SolidAct(63, 0, 0)),
            new PodFile(@"ART\OTHER.ACT", SolidAct(0, 0, 63)));

        PaletteChoices choices = PaletteResolver.ResolveChoices(@"ART\DEMO1.RAW", archive);

        Assert.Equal(0, choices.DefaultIndex);
        Assert.StartsWith("Same-name ACT:", choices.Choices[0].Label, StringComparison.Ordinal);
        Assert.Contains(choices.Choices, c => c.Label == "Greyscale");
    }

    [Fact]
    public void WithNoArchiveMtm1IsTheDefaultAndEveryBundledPaletteIsOffered()
    {
        PaletteChoices choices = PaletteResolver.ResolveChoices("LOOSE.RAW", null);

        Assert.Equal("METALCR2 (MTM1)", choices.Choices[choices.DefaultIndex].Label);
        Assert.Contains(choices.Choices, choice => choice.Label == "METALCR2 (CPR)");
        Assert.Contains(choices.Choices, choice => choice.Label == "VGA (Hellbender)");
        Assert.Contains(choices.Choices, choice => choice.Label == "VGA (TV/F3)");
        Assert.Contains(choices.Choices, choice => choice.Label == "Greyscale");
    }

    [Fact]
    public void BundledPalettesAreCompleteAndDistinct()
    {
        int[][] palettes =
        [
            BundledPalettes.MetalCr2Mtm1(), BundledPalettes.MetalCr2Cpr(),
            BundledPalettes.VgaHellbender(), BundledPalettes.VgaTerminalVelocity(),
        ];

        Assert.All(palettes, palette => Assert.Equal(256, palette.Length));
        Assert.Equal(4, palettes.Select(palette => string.Join(",", palette)).Distinct().Count());
    }

    [Fact]
    public void ThePaletteStoredInTheDirectoryFieldBeatsEveryGuess()
    {
        // MTM1, Terminal Velocity, Fury3 and Hellbender record the palette a RAW
        // was authored against in the spare bytes of its directory field. It wins
        // over the same-name rule, which those archives contradict wholesale.
        using TempDir temp = new();
        PodArchive archive = ArchiveWithStoredPalette(
            temp,
            @"ART\DEMO1.RAW",
            "MORBOS.ACT",
            new PodFile(@"ART\DEMO1.ACT", SolidAct(63, 0, 0)),
            new PodFile(@"ART\MORBOS.ACT", SolidAct(0, 63, 0)));

        PodEntry raw = archive.Entries[0];
        Assert.Equal("MORBOS.ACT", raw.EmbeddedPaletteName);
        Assert.Equal(
            unchecked((int)0xFF00FF00),
            PaletteResolver.Resolve(raw.Name, archive, raw.RawNameField)[0]);
    }

    [Fact]
    public void TheStoredPaletteLeadsTheChoiceListAndIsSelected()
    {
        using TempDir temp = new();
        PodArchive archive = ArchiveWithStoredPalette(
            temp,
            @"ART\DEMO1.RAW",
            "MORBOS.ACT",
            new PodFile(@"ART\DEMO1.ACT", SolidAct(63, 0, 0)),
            new PodFile(@"ART\MORBOS.ACT", SolidAct(0, 63, 0)));

        PodEntry raw = archive.Entries[0];
        PaletteChoices choices = PaletteResolver.ResolveChoices(raw.Name, archive, raw.RawNameField);

        Assert.Equal(0, choices.DefaultIndex);
        Assert.StartsWith("Stored in the archive:", choices.Choices[0].Label, StringComparison.Ordinal);
        Assert.Contains(choices.Choices, c => c.Label.StartsWith("Same-name ACT:", StringComparison.Ordinal));
    }

    [Fact]
    public void AStoredNameThatIsNotInTheArchiveFallsBackToTheHeuristic()
    {
        using TempDir temp = new();
        PodArchive archive = ArchiveWithStoredPalette(
            temp,
            @"ART\DEMO1.RAW",
            "ABSENT.ACT",
            new PodFile(@"ART\DEMO1.ACT", SolidAct(63, 0, 0)));

        PodEntry raw = archive.Entries[0];
        Assert.Equal(
            unchecked((int)0xFFFF0000),
            PaletteResolver.Resolve(raw.Name, archive, raw.RawNameField)[0]);
    }

    [Fact]
    public void ArchivesWithNoStoredPaletteAreUnaffected()
    {
        using TempDir temp = new();
        PodArchive archive = Archive(
            temp,
            new PodFile(@"ART\DEMO1.RAW", new byte[16]),
            new PodFile(@"ART\DEMO1.ACT", SolidAct(63, 0, 0)));

        PodEntry raw = archive.Entries[0];
        Assert.Null(raw.EmbeddedPaletteName);
        Assert.Equal(
            unchecked((int)0xFFFF0000),
            PaletteResolver.Resolve(raw.Name, archive, raw.RawNameField)[0]);
    }

    /// <summary>Builds an archive whose first entry carries a stored palette name.</summary>
    private static PodArchive ArchiveWithStoredPalette(
        TempDir temp, string rawName, string paletteName, params PodFile[] rest)
    {
        PodFile[] files = [new PodFile(rawName, new byte[16]), .. rest];
        byte[] bytes = PodFixture.BuildPod1(files);
        byte[] stored = KPod.Core.Compat.PodText.Latin1.GetBytes(paletteName);
        Array.Copy(stored, 0, bytes, 84 + rawName.Length + 1, stored.Length);
        return PodArchiveReader.Read(bytes);
    }

    /// <summary>An ACT whose 256 entries are all the one 6-bit VGA colour.</summary>
    private static byte[] SolidAct(byte r, byte g, byte b)
    {
        byte[] act = new byte[RawImageDecoder.ActPaletteBytes];
        for (int i = 0; i < 256; i++)
        {
            act[i * 3] = r;
            act[(i * 3) + 1] = g;
            act[(i * 3) + 2] = b;
        }

        return act;
    }

    private static PodArchive Archive(TempDir temp, params PodFile[] files) =>
        PodArchiveReader.Read(PodFixture.BuildPod1(files));
}
