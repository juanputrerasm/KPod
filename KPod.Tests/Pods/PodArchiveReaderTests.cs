using KPod.Core.Compat;
using KPod.Core.Pods;

namespace KPod.Tests.Pods;

public class PodArchiveReaderTests
{
    [Fact]
    public void ReadsPod1EntriesAndPayloads()
    {
        using TempDir temp = new();
        string path = temp.WriteFile("combo.pod", PodFixture.BuildPod1(
            PodFile.Text("WORLD/LAGUNA.SIT", "!Race Track Name\nLaguna Seca\n"),
            PodFile.Text("TRUCK/BIGFOOT.TRK", "truckName\nBigfoot 15\n")));

        using PodArchive archive = PodArchiveReader.Read(path);

        Assert.Equal(PodFormat.Pod1, archive.Format);
        Assert.Equal(["WORLD/LAGUNA.SIT", "TRUCK/BIGFOOT.TRK"], archive.Entries.Select(e => e.Name));
        Assert.Equal(
            "!Race Track Name\nLaguna Seca\n",
            PodText.Latin1.GetString(archive.GetEntryBytes(archive.Entries[0])));
    }

    [Fact]
    public void ReadsPod2EntriesFromTheTrailingNameTable()
    {
        using TempDir temp = new();
        string path = temp.WriteFile("two.pod", PodFixture.BuildPod2(
            PodFile.Text("WORLD/AZTEC.SIT", "aztec"),
            PodFile.Text("TRUCK/SNAKE.TRK", "snake")));

        using PodArchive archive = PodArchiveReader.Read(path);

        Assert.Equal(PodFormat.Pod2, archive.Format);
        Assert.Equal("POD2", archive.FormatDisplayName);
        Assert.False(archive.IsPod1Family);
        Assert.Equal("fixture", archive.Comment);
        Assert.Equal(["WORLD/AZTEC.SIT", "TRUCK/SNAKE.TRK"], archive.Entries.Select(e => e.Name));
        Assert.Equal("snake", PodText.Latin1.GetString(archive.GetEntryBytes(archive.Entries[1])));
    }

    [Fact]
    public void EpdJoinsUppercasePrefixWithBackslashSuffix()
    {
        using TempDir temp = new();
        string path = temp.WriteFile(
            "art.epd",
            PodFixture.BuildEpd(("ART", @"\TRUCK.BMP", PodText.Latin1.GetBytes("bmp"))));

        using PodArchive archive = PodArchiveReader.Read(path);

        Assert.Equal(PodFormat.Epd, archive.Format);
        Assert.Equal(@"ART\TRUCK.BMP", archive.Entries[0].Name);
    }

    [Fact]
    public void EpdIgnoresLowercasePrefixAndKeepsTheSuffixAlone()
    {
        using TempDir temp = new();
        string path = temp.WriteFile(
            "art.epd",
            PodFixture.BuildEpd(("art", @"\TRUCK.BMP", PodText.Latin1.GetBytes("bmp"))));

        using PodArchive archive = PodArchiveReader.Read(path);
        Assert.Equal(@"\TRUCK.BMP", archive.Entries[0].Name);
    }

    [Fact]
    public void FileTooSmallIsRejected()
    {
        using TempDir temp = new();
        string path = temp.WriteFile("tiny.pod", new byte[10]);

        Assert.Throws<PodFormatException>(() => PodArchiveReader.Read(path));
    }

    [Fact]
    public void SuspiciousItemCountIsRejected()
    {
        using TempDir temp = new();
        byte[] bytes = PodFixture.BuildPod1(PodFile.Text("A.RAW", "x"));
        PodFixture.PatchInt32(bytes, 0, PodArchiveReader.MaxReasonableItems + 1);
        string path = temp.WriteFile("many.pod", bytes);

        Assert.Throws<PodFormatException>(() => PodArchiveReader.Read(path));
    }

    [Fact]
    public void LookupsAreCaseInsensitiveAndTitleAware()
    {
        using TempDir temp = new();
        string path = temp.WriteFile("look.pod", PodFixture.BuildPod1(
            PodFile.Text(@"ART\DEMO1.RAW", "raw"),
            PodFile.Text(@"ART\DEMO1.ACT", "act")));

        using PodArchive archive = PodArchiveReader.Read(path);

        Assert.NotNull(archive.FindEntry(@"art\demo1.raw"));
        Assert.NotNull(archive.FindEntryByTitle("DEMO1.ACT"));
        Assert.Equal("DEMO1.RAW", archive.Entries[0].Title);
        Assert.Single(archive.GetEntriesByExtension(".act"));
    }
}
