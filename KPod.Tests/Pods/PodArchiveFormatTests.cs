using KPod.Core.Compat;
using KPod.Core.Pods;

namespace KPod.Tests.Pods;

/// <summary>
/// Covers POD1 directory detection, round-tripping, and the rejection rules from the
/// POD1 format hand-over: 40-byte records, a 31-character name budget, and a refusal
/// rather than a truncation or a wider directory when a name overruns it. Ported test
/// for test from JPod's PodArchiveFormatTest.
/// </summary>
public class PodArchiveFormatTests
{
    private const string ShortName = @"ART\WALL01.RAW";

    /// <summary>40 bytes: past the 31-byte budget a POD1 directory field can hold.</summary>
    private const string LongName = @"MODELS\TRUCKS\CUSTOM_BIGFOOT_WHEEL01.RAW";

    [Fact]
    public void ShortNamesProduceClassicPod1()
    {
        using TempDir temp = new();
        string file = temp.Resolve("classic.pod");

        PodFormat written = PodArchiveWriter.Write(
            file, "classic archive", [Blob(ShortName, 10), Blob("DEMO1.RAW", 20)]);

        Assert.Equal(PodFormat.Pod1, written);

        using PodArchive archive = PodArchiveReader.Read(file);
        Assert.Equal(PodFormat.Pod1, archive.Format);
        Assert.Equal("POD1", archive.FormatDisplayName);
        Assert.Equal("classic archive", archive.Comment);
        Assert.Equal([ShortName, "DEMO1.RAW"], archive.Entries.Select(e => e.Name));
        Assert.Equal(84 + (2 * 40), archive.Entries[0].Offset);
        Assert.Equal(PodFixture.Payload(10), archive.GetEntryBytes(archive.Entries[0]));
    }

    [Fact]
    public void OneLongNameIsRefused()
    {
        using TempDir temp = new();
        string file = temp.Resolve("overlong.pod");

        PodFormatException ex = Assert.Throws<PodFormatException>(() => PodArchiveWriter.Write(
            file, "overlong archive", [Blob(ShortName, 10), Blob(LongName, 20)]));

        Assert.Contains(LongName, ex.Message, StringComparison.Ordinal);
        Assert.Contains("31", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void NameOfExactly31BytesFits()
    {
        using TempDir temp = new();
        Assert.Equal(
            PodFormat.Pod1,
            PodArchiveWriter.Write(temp.Resolve("edge31.pod"), string.Empty, [Blob(new string('A', 31), 4)]));
    }

    [Fact]
    public void NameOfExactly32BytesIsRejected()
    {
        // 32 characters leaves no room for the terminator the engine scans for.
        PodFormatException ex = Assert.Throws<PodFormatException>(
            () => PodArchiveWriter.BuildBytes(string.Empty, [Blob(new string('A', 32), 4)]));

        Assert.Contains("32 bytes", ex.Message, StringComparison.Ordinal);
        Assert.Contains("31", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AnOverlongNameIsNeverSilentlyTruncated()
    {
        // A truncated name packs without error and then simply never resolves in game,
        // which is far harder to diagnose than a refusal.
        using TempDir temp = new();
        string file = temp.Resolve("nothing-written.pod");
        string name = @"ART" + new string('A', 40) + ".RAW";

        Assert.Throws<PodFormatException>(
            () => PodArchiveWriter.BuildBytes(string.Empty, [Blob(name, 4)]));
        Assert.Throws<PodFormatException>(
            () => PodArchiveWriter.Write(file, string.Empty, [Blob(name, 4)]));
        Assert.False(File.Exists(file), "a refused write must not leave a file behind");
    }

    [Fact]
    public void ASeventyTwoByteDirectoryIsRefused()
    {
        // A directory of 72-byte records is not a POD1 directory. The engine walks a POD1
        // directory in 40-byte steps and refuses the volume, and so does this reader.
        using TempDir temp = new();
        string file = temp.WriteFile(
            "handmade.pod",
            PodFixture.BuildSeventyTwoByteDirectory(new PodFile(LongName, PodFixture.Payload(6))));

        Assert.Throws<PodFormatException>(() => PodArchiveReader.Read(file));
    }

    [Fact]
    public void ASeventyTwoByteDirectoryIsRefusedEvenWhenItsNamesWouldFit()
    {
        // Nothing about the names makes the record stride right.
        using TempDir temp = new();
        string file = temp.WriteFile(
            "shortnames72.pod",
            PodFixture.BuildSeventyTwoByteDirectory(new PodFile(ShortName, PodFixture.Payload(6))));

        Assert.Throws<PodFormatException>(() => PodArchiveReader.Read(file));
    }

    [Fact]
    public void TheFortyByteLayoutWinsWhenBothCouldParse()
    {
        using TempDir temp = new();
        string file = temp.WriteFile(
            "ambiguous.pod",
            PodFixture.BuildPod1(new PodFile(ShortName, PodFixture.Payload(6))));

        using PodArchive archive = PodArchiveReader.Read(file);
        Assert.Equal(PodFormat.Pod1, archive.Format);
    }

    [Fact]
    public void PayloadOutsideTheFileIsRejected()
    {
        using TempDir temp = new();
        byte[] bytes = PodFixture.BuildPod1(new PodFile(ShortName, PodFixture.Payload(6)));

        // Point the single entry past the end of the archive.
        PodFixture.PatchInt32(bytes, 84 + 32 + 4, bytes.Length + 1);
        string file = temp.WriteFile("truncated.pod", bytes);

        Assert.Throws<PodFormatException>(() => PodArchiveReader.Read(file));
    }

    [Fact]
    public void DuplicateNamesSurviveWhenTheArchiveAlreadyHadThem()
    {
        // Shipped and community PODs repeat a name so the later copy shadows the
        // earlier one. Authoring still rejects it, but an archive read from disk
        // has to stay savable.
        using TempDir temp = new();
        PodBlob[] blobs =
        [
            new PodBlob(ShortName, PodFixture.Payload(4)),
            new PodBlob(ShortName, PodFixture.Payload(6)),
        ];

        Assert.Throws<PodFormatException>(() => PodArchiveWriter.BuildBytes("", blobs));

        byte[] bytes = PodArchiveWriter.BuildBytes("", blobs,
            new PodWriteOptions(PodFormat.Pod1, null, [], AllowDuplicateNames: true));
        string file = temp.WriteFile("dupes.pod", bytes);

        using PodArchive archive = PodArchiveReader.Read(file);
        Assert.Equal(2, archive.Entries.Count);
        Assert.Equal(ShortName, archive.Entries[0].Name);
        Assert.Equal(ShortName, archive.Entries[1].Name);
        Assert.Equal(4, archive.Entries[0].Length);
        Assert.Equal(6, archive.Entries[1].Length);
    }

    [Fact]
    public void UnsafePathsAreRejectedEvenWhenDuplicatesAreAllowed()
    {
        // Relaxing the duplicate rule must not relax path safety with it.
        PodWriteOptions lenient = new(PodFormat.Pod1, null, [], AllowDuplicateNames: true);

        Assert.Throws<PodFormatException>(() => PodArchiveWriter.BuildBytes("",
            [new PodBlob("..\\ESCAPE.RAW", PodFixture.Payload(4))], lenient));
        Assert.Throws<PodFormatException>(() => PodArchiveWriter.BuildBytes("",
            [new PodBlob("\\ROOTED.RAW", PodFixture.Payload(4))], lenient));
    }

    [Fact]
    public void PayloadOverlappingTheDirectoryIsRejected()
    {
        using TempDir temp = new();
        byte[] bytes = PodFixture.BuildPod1(new PodFile(ShortName, PodFixture.Payload(6)));

        // The first payload byte can never precede the end of the directory. An
        // offset inside the header or the directory means this is not a classic
        // POD1 layout, whatever the name field happens to decode to.
        PodFixture.PatchInt32(bytes, 84 + 32 + 4, 84);
        string file = temp.WriteFile("underflow.pod", bytes);

        Assert.Throws<PodFormatException>(() => PodArchiveReader.Read(file));
    }

    [Fact]
    public void APayloadStartingExactlyAtTheDirectoryEndIsAccepted()
    {
        using TempDir temp = new();
        // 84-byte header plus one 40-byte record puts the floor at 124, and the
        // fixture already lays the payload down there.
        byte[] bytes = PodFixture.BuildPod1(new PodFile(ShortName, PodFixture.Payload(6)));
        string file = temp.WriteFile("floor.pod", bytes);

        using PodArchive archive = PodArchiveReader.Read(file);
        Assert.Equal(PodFormat.Pod1, archive.Format);
        Assert.Equal(124, archive.Entries[0].Offset);
    }

    [Fact]
    public void ControlCharactersInsideANameAreRejected()
    {
        using TempDir temp = new();
        byte[] bytes = PodFixture.BuildPod1(new PodFile(ShortName, PodFixture.Payload(6)));

        // A control byte inside the name is what a misparsed directory looks like.
        bytes[84 + 5] = 0x01;
        string file = temp.WriteFile("garbage.pod", bytes);

        Assert.Throws<PodFormatException>(() => PodArchiveReader.Read(file));
    }

    [Fact]
    public void ReSavingKeepsWhateverFollowedTheNameTerminator()
    {
        // Fury3's FURYSE.POD packs a second NUL-terminated string into the spare
        // bytes of each RAW entry's name field. Nothing here reads past the first
        // terminator, but re-saving must not throw those bytes away.
        using TempDir temp = new();
        byte[] bytes = PodFixture.BuildPod1(new PodFile(ShortName, PodFixture.Payload(6)));
        byte[] extra = PodText.Latin1.GetBytes("VGA.ACT");
        Array.Copy(extra, 0, bytes, 84 + ShortName.Length + 1, extra.Length);
        string file = temp.WriteFile("withextra.pod", bytes);

        using PodArchive archive = PodArchiveReader.Read(file);
        Assert.Equal(ShortName, archive.Entries[0].Name);

        List<PodBlob> blobs = [];
        foreach (PodEntry entry in archive.Entries)
        {
            blobs.Add(new PodBlob(entry.Name, archive.GetEntryBytes(entry), entry.RawNameField));
        }

        Assert.Equal(bytes, PodArchiveWriter.BuildBytes(archive.Comment, blobs));
    }

    [Fact]
    public void AnEntryAddedFromDiskGetsAFreshlyBuiltNameField()
    {
        byte[] rebuilt = PodArchiveWriter.BuildBytes(string.Empty, [Blob("NEW.RAW", 4)]);

        // No original field to preserve, so the padding is all zeros.
        for (int i = "NEW.RAW".Length; i < 32; i++)
        {
            Assert.Equal(0, rebuilt[84 + i]);
        }
    }

    [Fact]
    public void APreservedClassicFieldStillFitsWhenTheArchiveGoesExtended()
    {
        using TempDir temp = new();
        byte[] bytes = PodFixture.BuildPod1(new PodFile(ShortName, PodFixture.Payload(6)));
        byte[] extra = PodText.Latin1.GetBytes("VGA.ACT");
        Array.Copy(extra, 0, bytes, 84 + ShortName.Length + 1, extra.Length);
        string file = temp.WriteFile("mixed.pod", bytes);

        using PodArchive archive = PodArchiveReader.Read(file);
        PodEntry original = archive.Entries[0];

        List<PodBlob> blobs =
        [
            new PodBlob(original.Name, archive.GetEntryBytes(original), original.RawNameField),
            Blob(@"ART\ADDED.RAW", 4),
        ];
        string target = temp.Resolve("mixed-out.pod");
        Assert.Equal(PodFormat.Pod1, PodArchiveWriter.Write(target, archive.Comment, blobs));

        byte[] written = File.ReadAllBytes(target);
        Assert.Equal(
            "VGA.ACT",
            PodText.Latin1.GetString(written, 84 + ShortName.Length + 1, extra.Length));
    }

    [Fact]
    public void EmptyNameFieldIsRejected()
    {
        using TempDir temp = new();
        byte[] bytes = PodFixture.BuildPod1(new PodFile(ShortName, PodFixture.Payload(6)));
        Array.Clear(bytes, 84, 32);
        string file = temp.WriteFile("noname.pod", bytes);

        Assert.Throws<PodFormatException>(() => PodArchiveReader.Read(file));
    }

    [Fact]
    public void TruncatedDirectoryTableIsRejected()
    {
        using TempDir temp = new();
        byte[] bytes = PodFixture.BuildPod1(new PodFile(ShortName, PodFixture.Payload(6)));

        // Claim four entries in a file that only has room for one.
        PodFixture.PatchInt32(bytes, 0, 4);
        string file = temp.WriteFile("short-table.pod", bytes);

        Assert.Throws<PodFormatException>(() => PodArchiveReader.Read(file));
    }

    [Fact]
    public void LeadingControlBytesAreTrimmedTheWayJavaTrimDoes()
    {
        using TempDir temp = new();
        byte[] bytes = PodFixture.BuildPod1(new PodFile(ShortName, PodFixture.Payload(6)));

        // Java's String.trim() strips every code point at or below U+0020, so a
        // leading control byte is trimmed rather than rejected. KPod must agree
        // with JPod here, or the same archive would open in one and not the other.
        bytes[84] = 0x01;
        string file = temp.WriteFile("leading-control.pod", bytes);

        using PodArchive archive = PodArchiveReader.Read(file);
        Assert.Equal(@"RT\WALL01.RAW", archive.Entries[0].Name);
    }

    [Fact]
    public void ClassicRoundTripIsByteIdentical()
    {
        byte[] original = PodFixture.BuildPod1(
            32,
            "round trip",
            new PodFile(ShortName, PodFixture.Payload(10)),
            new PodFile("DEMO1.RAW", PodFixture.Payload(20)));

        using TempDir temp = new();
        string file = temp.WriteFile("original.pod", original);
        using PodArchive archive = PodArchiveReader.Read(file);

        List<PodBlob> blobs = [];
        foreach (PodEntry entry in archive.Entries)
        {
            blobs.Add(new PodBlob(entry.Name, archive.GetEntryBytes(entry)));
        }

        Assert.Equal(original, PodArchiveWriter.BuildBytes(archive.Comment, blobs));
    }

    [Fact]
    public void EmptyBlobListIsRejected()
    {
        Assert.Throws<ArgumentException>(() => PodArchiveWriter.BuildBytes("x", []));
    }

    [Fact]
    public void APod1ArchiveWithAPaletteRoundTripsByteForByte()
    {
        // The regression guard that matters most: a legacy pod must come back out
        // byte-identical, spare bytes after the terminator included.
        byte[] original = PodFixture.BuildPod1(new PodFile(ShortName, PodFixture.Payload(7)));
        byte[] palette = PodText.Latin1.GetBytes("VGA.ACT");
        Array.Copy(palette, 0, original, 84 + ShortName.Length + 1, palette.Length);
        using TempDir temp = new();
        using PodArchive archive = PodArchiveReader.Read(temp.WriteFile("pod1-palette.pod", original));
        PodEntry entry = archive.Entries[0];
        PodBlob blob = new(entry.Name, archive.GetEntryBytes(entry), entry.RawNameField,
            entry.EmbeddedPaletteName, 0);
        byte[] rebuilt = PodArchiveWriter.BuildBytes(archive.Comment, [blob],
            new PodWriteOptions(archive.Format, archive.RawCommentField, []));
        Assert.Equal(original, rebuilt);
    }

    [Fact]
    public void RenamingRawRebuildsItsFieldAndKeepsPalette()
    {
        byte[] field = new byte[32];
        byte[] oldName = PodText.Latin1.GetBytes("OLD.RAW");
        byte[] palette = PodText.Latin1.GetBytes("METALCR2.ACT");
        Array.Copy(oldName, field, oldName.Length);
        Array.Copy(palette, 0, field, oldName.Length + 1, palette.Length);
        PodBlob renamed = new(@"ART\RENAMED.RAW", PodFixture.Payload(4),
            field, "METALCR2.ACT", 0);
        using TempDir temp = new();
        string path = temp.WriteFile("renamed.pod", PodArchiveWriter.BuildBytes("", [renamed]));
        using PodArchive archive = PodArchiveReader.Read(path);
        Assert.Equal(PodFormat.Pod1, archive.Format);
        Assert.Equal(@"ART\RENAMED.RAW", archive.Entries[0].Name);
        Assert.Equal("METALCR2.ACT", archive.Entries[0].EmbeddedPaletteName);
    }

    [Fact]
    public void ARenameWhosePaletteRecordNoLongerFitsIsRefused()
    {
        // Name plus terminator plus palette plus terminator must fit the 32-byte field.
        // Refusing names the palette too, because shortening the path is not the only
        // remedy - the caller can also clear the hint.
        byte[] field = new byte[32];
        byte[] oldName = PodText.Latin1.GetBytes("OLD.RAW");
        Array.Copy(oldName, field, oldName.Length);
        PodBlob renamed = new(@"ART\A_MUCH_LONGER_NAME.RAW", PodFixture.Payload(4),
            field, "METALCR2.ACT", 0);

        PodFormatException ex = Assert.Throws<PodFormatException>(
            () => PodArchiveWriter.BuildBytes("", [renamed]));

        Assert.Contains("METALCR2.ACT", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Pod2WriterRoundTripsChecksumsTimestampsAndAudits()
    {
        byte[] payload = PodFixture.Payload(9);
        PodBlob file = new(@"ART\TEST.RAW", payload, timestamp: 1_700_000_000);
        PodAuditEntry audit = new("tester", 1_700_000_001, PodAuditAction.Add,
            @"ART\TEST.RAW", 0, 0, 1_700_000_000, 9);
        byte[] bytes = PodArchiveWriter.BuildBytes("pod2", [file],
            new PodWriteOptions(PodFormat.Pod2, null, [audit]));
        using TempDir temp = new();
        using PodArchive archive = PodArchiveReader.Read(temp.WriteFile("two.pod", bytes));
        Assert.Equal(PodFormat.Pod2, archive.Format);
        Assert.Equal(1_700_000_000u, archive.Entries[0].Timestamp);
        Assert.Equal(PodArchiveWriter.Crc32Mpeg2(payload), archive.Entries[0].Checksum);
        Assert.Equal(PodArchiveWriter.Crc32Mpeg2(bytes, 8, bytes.Length - 8), archive.Checksum);
        Assert.Equal([audit], archive.AuditEntries);
    }

    [Fact]
    public void CrossPortAuthoringVectorsHaveStableBinaryOutput()
    {
        byte[] classic = PodArchiveWriter.BuildBytes("vector",
            [new PodBlob("A.TXT", [1, 2, 3], timestamp: 0)]);
        byte[] palette = PodArchiveWriter.BuildBytes("vector",
            [new PodBlob(@"ART\T.RAW", [4, 5], embeddedPaletteName: "METALCR2.ACT", timestamp: 0)]);
        byte[] pod2 = PodArchiveWriter.BuildBytes("vector",
            [new PodBlob("A.TXT", [1, 2, 3], timestamp: 1_700_000_000)],
            new PodWriteOptions(PodFormat.Pod2, null, []));

        Assert.Equal("a909fc89a0f2b8d1bb4c4e28b4288b8440c7bc67bd2a8258d0438dce2f008cb8", Sha256(classic));
        Assert.Equal("730fc5da3fed95209929bbb98389b0bfb979df9e88d526cb0630a7669c0562ee", Sha256(palette));
        Assert.Equal("a1679199ed5bb19ba310ae18ea7c0bc6a8c7a55a9244320fb8dbf4cebf5d1c80", Sha256(pod2));
        Assert.Equal(0x0376E6E7u, PodArchiveWriter.Crc32Mpeg2(PodText.Latin1.GetBytes("123456789")));
    }

    [Fact]
    public void WriterRejectsDuplicatesUnsafePathsAndLongComments()
    {
        Assert.Throws<PodFormatException>(() => PodArchiveWriter.BuildBytes("", [
            Blob("A.RAW", 1), Blob("a.raw", 1)]));
        Assert.Throws<PodFormatException>(() => PodArchiveWriter.BuildBytes("", [Blob(@"..\A.RAW", 1)]));
        Assert.Throws<PodFormatException>(() => PodArchiveWriter.BuildBytes(new string('X', 80), [Blob("A.RAW", 1)]));
    }

    private static PodBlob Blob(string name, int size) => new(name, PodFixture.Payload(size));

    private static string Sha256(byte[] bytes)
    {
        using System.Security.Cryptography.SHA256 hash = System.Security.Cryptography.SHA256.Create();
        return BitConverter.ToString(hash.ComputeHash(bytes)).Replace("-", string.Empty).ToLowerInvariant();
    }
}
