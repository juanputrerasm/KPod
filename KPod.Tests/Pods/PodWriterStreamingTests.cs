using KPod.Core.Compat;
using KPod.Core.Pods;

namespace KPod.Tests.Pods;

/// <summary>
/// Covers the parts of writing and validating that stopped materializing payloads:
/// the checksum table, the layout-only validation, and building beside the target so
/// an archive can be saved over the one it is being read from.
/// </summary>
public class PodWriterStreamingTests
{
    /// <summary>
    /// The bit-serial checksum the table-driven one replaced, kept here so the
    /// replacement is pinned to it rather than to a value someone copied across.
    /// </summary>
    private static uint BitSerialCrc32Mpeg2(byte[] bytes, int offset, int length)
    {
        uint crc = 0xffffffff;
        for (int i = offset; i < offset + length; i++)
        {
            crc ^= (uint)bytes[i] << 24;
            for (int bit = 0; bit < 8; bit++)
                crc = (crc << 1) ^ ((crc & 0x80000000) != 0 ? 0x04C11DB7u : 0);
        }
        return crc;
    }

    [Fact]
    public void ChecksumMatchesTheCanonicalCheckValue()
    {
        byte[] check = PodText.Latin1.GetBytes("123456789");
        Assert.Equal(0x0376E6E7u, PodArchiveWriter.Crc32Mpeg2(check));
    }

    [Fact]
    public void ChecksumMatchesTheBitSerialImplementationItReplaced()
    {
        byte[] data = PodFixture.Payload(4096);
        Assert.Equal(BitSerialCrc32Mpeg2(data, 0, data.Length), PodArchiveWriter.Crc32Mpeg2(data));
        Assert.Equal(BitSerialCrc32Mpeg2(data, 8, data.Length - 8),
            PodArchiveWriter.Crc32Mpeg2(data, 8, data.Length - 8));
        Assert.Equal(BitSerialCrc32Mpeg2([], 0, 0), PodArchiveWriter.Crc32Mpeg2([]));
    }

    [Fact]
    public void SeededChecksumOverChunksMatchesOneWholePass()
    {
        byte[] data = PodFixture.Payload(5000);
        uint whole = PodArchiveWriter.Crc32Mpeg2(data);

        uint running = PodArchiveWriter.Crc32Seed;
        running = PodArchiveWriter.Crc32Mpeg2(data, 0, 1234, running);
        running = PodArchiveWriter.Crc32Mpeg2(data, 1234, 2000, running);
        running = PodArchiveWriter.Crc32Mpeg2(data, 3234, data.Length - 3234, running);

        Assert.Equal(whole, running);
    }

    [Fact]
    public void ChecksumOverADataSourceMatchesTheArrayOverload()
    {
        byte[] data = PodFixture.Payload(200000);
        using MemoryPodDataSource source = new(data);

        Assert.Equal(PodArchiveWriter.Crc32Mpeg2(data),
            PodArchiveWriter.Crc32Mpeg2(source, 0, data.Length));
        Assert.Equal(PodArchiveWriter.Crc32Mpeg2(data, 96, 1000),
            PodArchiveWriter.Crc32Mpeg2(source, 96, 1000));
    }

    [Fact]
    public void SavingOverTheArchiveBeingReadProducesTheEditedArchive()
    {
        using TempDir temp = new();
        string path = temp.WriteFile("self.pod", PodFixture.BuildPod1(
            PodFile.Text("A.TXT", "alpha"),
            PodFile.Text("B.TXT", "beta")));

        PodArchive open = PodArchiveReader.Read(path);
        List<PodBlob> blobs = [];
        foreach (PodEntry entry in open.Entries)
        {
            blobs.Add(new PodBlob(entry.Name, open.Data, entry.Offset, (int)entry.Length,
                entry.RawNameField));
        }
        blobs.Add(new PodBlob("C.TXT", PodText.Latin1.GetBytes("gamma")));

        // Every payload of the first two entries is streamed out of the file that is
        // about to be replaced, which is the case the temp-and-swap exists for.
        PodArchiveWriter.Write(path, "self", blobs,
            new PodWriteOptions(PodFormat.Pod1, null, []),
            beforeReplace: open.Dispose);

        using PodArchive reopened = PodArchiveReader.Read(path);
        Assert.Equal(["A.TXT", "B.TXT", "C.TXT"], reopened.Entries.Select(e => e.Name));
        Assert.Equal("alpha", PodText.Latin1.GetString(reopened.GetEntryBytes(reopened.Entries[0])));
        Assert.Equal("beta", PodText.Latin1.GetString(reopened.GetEntryBytes(reopened.Entries[1])));
        Assert.Equal("gamma", PodText.Latin1.GetString(reopened.GetEntryBytes(reopened.Entries[2])));
        Assert.Equal("self", reopened.Comment);
    }

    [Fact]
    public void WritingLeavesNoTemporaryFileBehind()
    {
        using TempDir temp = new();
        string path = temp.Resolve("clean.pod");

        PodArchiveWriter.Write(path, string.Empty,
            [new PodBlob("A.TXT", PodText.Latin1.GetBytes("alpha"))]);

        Assert.True(File.Exists(path));
        Assert.Empty(Directory.GetFiles(temp.Path, "*.kpodtmp"));
    }

    [Fact]
    public void StreamedWriteAndBuiltBytesAgreeByteForByte()
    {
        using TempDir temp = new();
        PodBlob[] blobs =
        [
            new PodBlob(@"ART\ONE.RAW", PodFixture.Payload(4096)),
            new PodBlob(@"ART\TWO.RAW", PodFixture.Payload(70000)),
            new PodBlob("READ.TXT", PodText.Latin1.GetBytes("notes")),
        ];
        string path = temp.Resolve("streamed.pod");

        PodArchiveWriter.Write(path, "same", blobs);

        Assert.Equal(PodArchiveWriter.BuildBytes("same", blobs), File.ReadAllBytes(path));
    }

    [Fact]
    public void StreamedPod2WriteAndBuiltBytesAgreeByteForByte()
    {
        using TempDir temp = new();
        PodBlob[] blobs =
        [
            new PodBlob("WORLD/AZTEC.SIT", PodFixture.Payload(1000), timestamp: 111u),
            new PodBlob("TRUCK/SNAKE.TRK", PodFixture.Payload(90000), timestamp: 222u),
        ];
        PodWriteOptions options = new(PodFormat.Pod2, null,
            [new PodAuditEntry("tester", 5u, PodAuditAction.Add, "WORLD/AZTEC.SIT", 0, 0, 5u, 1000)]);
        string path = temp.Resolve("streamed2.pod");

        PodArchiveWriter.Write(path, "two", blobs, options);

        // The archive checksum is accumulated during the streamed write and patched
        // in afterwards, so it has to land on the same value the buffered build gets.
        Assert.Equal(PodArchiveWriter.BuildBytes("two", blobs, options), File.ReadAllBytes(path));

        using PodArchive reopened = PodArchiveReader.Read(path);
        Assert.True(reopened.VerifyArchiveChecksum());
        Assert.True(reopened.IsEntryChecksumValid(reopened.Entries[1]));
    }

    [Fact]
    public void ValidatingForSaveNeverReadsAPayload()
    {
        byte[] archive = PodFixture.BuildPod1(new PodFile("A.TXT", PodFixture.Payload(64)));
        MemoryPodDataSource source = new(archive);
        using PodArchive open = PodArchiveReader.Read(archive);
        PodEntry entry = open.Entries[0];

        // A blob pointing past the end of its source: reading it would throw, so a
        // clean validation is proof that validation looks only at names and lengths.
        PodBlob unreadable = new("A.TXT", new ThrowingDataSource(), entry.Offset,
            (int)entry.Length, entry.RawNameField);

        PodValidationResult result = PodArchiveValidator.ValidateForSave(
            "fine", [unreadable], PodFormat.Pod1);

        Assert.True(result.IsValid);
        Assert.Equal(PodFormat.Pod1, result.OutputFormat);
        GC.KeepAlive(source);
    }

    [Fact]
    public void ValidatingForSaveRefusesAnOversizedNameInsteadOfWidening()
    {
        PodBlob blob = new(new string('A', 40) + ".RAW", PodFixture.Payload(4));

        PodValidationResult result = PodArchiveValidator.ValidateForSave(
            string.Empty, [blob], PodFormat.Pod1);

        Assert.False(result.IsValid);
        Assert.Equal(PodFormat.Pod1, result.OutputFormat);
        Assert.Contains(result.Errors, e => e.Contains("31", StringComparison.Ordinal));
    }

    [Fact]
    public void ValidatingForSaveReportsAnEmptyEntryList()
    {
        PodValidationResult result = PodArchiveValidator.ValidateForSave(
            string.Empty, [], PodFormat.Pod1);

        Assert.False(result.IsValid);
    }

    /// <summary>A source that fails any read, to prove a code path never reads.</summary>
    private sealed class ThrowingDataSource : IPodDataSource
    {
        public long Length => long.MaxValue;

        public byte[] ReadExact(long offset, int count) =>
            throw new InvalidOperationException("payload was read");

        public int Read(long offset, byte[] buffer, int bufferOffset, int count) =>
            throw new InvalidOperationException("payload was read");

        public void CopyTo(long offset, long count, Stream target, byte[] scratch) =>
            throw new InvalidOperationException("payload was read");

        public void Dispose()
        {
        }
    }
}
