using KPod.Core.Compat;
using KPod.Core.Pods;

namespace KPod.Tests.Pods;

/// <summary>
/// Pins the behaviour the lazy reader must preserve. Payloads are no longer copied
/// out of the file at parse time, so every format has to hand back exactly the bytes
/// it used to, and the archive has to keep working after the entry list has been
/// walked without touching a payload.
/// </summary>
public class PodLazyReadTests
{
    [Fact]
    public void Pod1PayloadsReadBackExactly()
    {
        using TempDir temp = new();
        byte[] first = PodFixture.Payload(1000);
        byte[] second = PodFixture.Payload(37);
        string path = temp.WriteFile("lazy1.pod", PodFixture.BuildPod1(
            new PodFile(@"ART\ONE.RAW", first),
            new PodFile(@"ART\TWO.RAW", second)));

        using PodArchive archive = PodArchiveReader.Read(path);

        Assert.Equal(first, archive.GetEntryBytes(archive.Entries[0]));
        Assert.Equal(second, archive.GetEntryBytes(archive.Entries[1]));
    }

    [Fact]
    public void Pod164PayloadsReadBackExactly()
    {
        using TempDir temp = new();
        byte[] payload = PodFixture.Payload(2048);
        string name = new string('A', 40) + ".RAW";
        string path = temp.WriteFile("lazy164.pod",
            PodFixture.BuildPod164(new PodFile(name, payload)));

        using PodArchive archive = PodArchiveReader.Read(path);

        Assert.Equal(PodFormat.Pod1Extended, archive.Format);
        Assert.Equal(payload, archive.GetEntryBytes(archive.Entries[0]));
    }

    [Fact]
    public void Pod2PayloadsAndNamesReadBackExactly()
    {
        using TempDir temp = new();
        byte[] payload = PodFixture.Payload(500);
        string path = temp.WriteFile("lazy2.pod", PodFixture.BuildPod2(
            new PodFile("WORLD/AZTEC.SIT", payload),
            PodFile.Text("TRUCK/SNAKE.TRK", "snake")));

        using PodArchive archive = PodArchiveReader.Read(path);

        Assert.Equal(["WORLD/AZTEC.SIT", "TRUCK/SNAKE.TRK"], archive.Entries.Select(e => e.Name));
        Assert.Equal(payload, archive.GetEntryBytes(archive.Entries[0]));
        Assert.Equal("snake", PodText.Latin1.GetString(archive.GetEntryBytes(archive.Entries[1])));
    }

    [Fact]
    public void EpdPayloadsReadBackExactly()
    {
        using TempDir temp = new();
        byte[] payload = PodFixture.Payload(64);
        string path = temp.WriteFile("lazy.epd",
            PodFixture.BuildEpd(("ART", @"\TRUCK.BMP", payload)));

        using PodArchive archive = PodArchiveReader.Read(path);

        Assert.Equal(@"ART\TRUCK.BMP", archive.Entries[0].Name);
        Assert.Equal(payload, archive.GetEntryBytes(archive.Entries[0]));
    }

    [Fact]
    public void CopyEntryToStreamsTheSameBytesGetEntryBytesReturns()
    {
        using TempDir temp = new();
        byte[] payload = PodFixture.Payload(200000);
        string path = temp.WriteFile("big.pod",
            PodFixture.BuildPod1(new PodFile("BIG.RAW", payload)));

        using PodArchive archive = PodArchiveReader.Read(path);
        using MemoryStream streamed = new();
        archive.CopyEntryTo(archive.Entries[0], streamed, PodDataSource.NewScratch());

        Assert.Equal(payload, streamed.ToArray());
    }

    [Fact]
    public void ReadingFromMemoryMatchesReadingFromDisk()
    {
        using TempDir temp = new();
        byte[] bytes = PodFixture.BuildPod1(
            new PodFile(@"ART\ONE.RAW", PodFixture.Payload(300)),
            PodFile.Text("READ.TXT", "hello"));
        string path = temp.WriteFile("both.pod", bytes);

        using PodArchive fromDisk = PodArchiveReader.Read(path);
        using PodArchive fromMemory = PodArchiveReader.Read(bytes);

        Assert.Equal(
            fromDisk.Entries.Select(e => e.Name),
            fromMemory.Entries.Select(e => e.Name));
        Assert.Equal(
            fromDisk.GetEntryBytes(fromDisk.Entries[0]),
            fromMemory.GetEntryBytes(fromMemory.Entries[0]));
    }

    [Fact]
    public void ArchiveIsStillReadableAfterTheWholeDirectoryHasBeenWalked()
    {
        using TempDir temp = new();
        PodFile[] files = new PodFile[64];
        for (int i = 0; i < files.Length; i++)
        {
            files[i] = new PodFile($@"ART\E{i:D3}.RAW", PodFixture.Payload(i + 1));
        }
        string path = temp.WriteFile("many.pod", PodFixture.BuildPod1(files));

        using PodArchive archive = PodArchiveReader.Read(path);

        // Names, lengths and offsets must all be answerable without a payload read.
        long total = 0;
        foreach (PodEntry entry in archive.Entries) total += entry.Length;
        Assert.Equal(64 * 65 / 2, total);

        for (int i = 0; i < files.Length; i++)
        {
            Assert.Equal(files[i].Bytes, archive.GetEntryBytes(archive.Entries[i]));
        }
    }

    [Fact]
    public void ReadingATitleDoesNotDisturbEntryValueEquality()
    {
        PodEntry a = new(@"ART\WALL.RAW", 10, 200);
        PodEntry b = new(@"ART\WALL.RAW", 10, 200);

        Assert.Equal(a, b);
        Assert.Equal("WALL.RAW", a.Title);

        // PodEntry is a record, so any field caching the title would join its value
        // equality and make these two stop matching once one had been read.
        Assert.Equal(a, b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }

    [Fact]
    public void EntryLookupsStillFindDuplicatesFirstOccurrenceFirst()
    {
        using TempDir temp = new();
        string path = temp.WriteFile("dupes.pod", PodFixture.BuildPod1(
            PodFile.Text(@"ART\DEMO.RAW", "first"),
            PodFile.Text(@"ART\DEMO.RAW", "second")));

        using PodArchive archive = PodArchiveReader.Read(path);

        Assert.Equal("first", PodText.Latin1.GetString(
            archive.GetEntryBytes(archive.FindEntry(@"art\demo.raw")!)));
        Assert.Equal("first", PodText.Latin1.GetString(
            archive.GetEntryBytes(archive.FindEntryByTitle("demo.raw")!)));
    }
}
