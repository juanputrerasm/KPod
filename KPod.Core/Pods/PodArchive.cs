using KPod.Core.Compat;

namespace KPod.Core.Pods;

/// <summary>Terminal Reality POD archive variants KPod can read.</summary>
public enum PodFormat
{
    /// <summary>Classic POD version 1: 84-byte header, 40-byte records, 31-byte names.</summary>
    Pod1,

    Pod2,

    Epd,
}

/// <summary>
/// Immutable in-memory representation of a Terminal Reality POD archive.
///
/// <para>POD binary layout (all integers little-endian):</para>
/// <code>
///   Offset   Size   Description
///   ------   ----   -----------
///        0      4   Item count (int32)
///        4     80   Archive comment (NUL-terminated Latin-1 string)
///       84  N x 40  Directory table, one record per item:
///                     +  0  32 bytes  Entry name (NUL-padded)
///                     + 32   4 bytes  Data length (uint32)
///                     + 36   4 bytes  Data offset from file start (uint32)
///   84+Nx40   ...    Raw file data, concatenated in directory order
/// </code>
///
/// <para>That 40-byte record is the only POD1 directory layout. A directory table
/// that does not validate as one is a malformed archive and is refused.</para>
/// </summary>
public sealed class PodArchive : IDisposable
{
    private readonly IPodDataSource _data;

    /// <summary>
    /// Name lookups, built on first use. An archive with thousands of entries is
    /// searched repeatedly while previewing art, and a linear scan there costs a
    /// <see cref="PodEntry.Title"/> comparison per entry per lookup.
    /// </summary>
    private Dictionary<string, PodEntry>? _byName;
    private Dictionary<string, PodEntry>? _byTitle;

    internal PodArchive(
        PodFormat format,
        string comment,
        IPodDataSource data,
        IReadOnlyList<PodEntry> entries,
        byte[]? rawCommentField = null,
        uint checksum = 0,
        IReadOnlyList<PodAuditEntry>? auditEntries = null)
    {
        Format = format;
        Comment = comment;
        _data = data;
        Entries = entries;
        RawCommentField = rawCommentField;
        Checksum = checksum;
        AuditEntries = auditEntries ?? [];
    }

    /// <summary>
    /// The bytes behind the archive. Held open for as long as the archive is, so
    /// callers must dispose the archive when they are done with it.
    /// </summary>
    public IPodDataSource Data => _data;

    public PodFormat Format { get; }

    /// <summary>The 80-byte archive comment from the POD header, trimmed.</summary>
    public string Comment { get; }

    /// <summary>
    /// The 80-byte comment field exactly as read, or null when the archive did not
    /// come from a POD1 header. Like a directory name field it can hold bytes after
    /// the terminator, so it is kept for writing back verbatim.
    /// </summary>
    public byte[]? RawCommentField { get; }

    public IReadOnlyList<PodEntry> Entries { get; }

    public uint Checksum { get; }

    public IReadOnlyList<PodAuditEntry> AuditEntries { get; }

    /// <summary>
    /// Verifies the POD2 whole-archive checksum, streaming the file rather than
    /// holding it. Non-POD2 archives carry no checksum and always pass.
    ///
    /// <para>A method rather than a property: it reads every byte of the archive, and
    /// a property that does that invites being called from a UI binding or a loop.</para>
    /// </summary>
    public bool VerifyArchiveChecksum() => Format != PodFormat.Pod2
        || Checksum == PodArchiveWriter.Crc32Mpeg2(_data, 8, _data.Length - 8);

    /// <summary>Verifies one POD2 entry's checksum, streaming its payload.</summary>
    public bool IsEntryChecksumValid(PodEntry entry) => Format != PodFormat.Pod2
        || entry.Checksum == PodArchiveWriter.Crc32Mpeg2(_data, entry.Offset, entry.Length);

    public string FormatDisplayName => Format switch
    {
        PodFormat.Pod1 => "POD1",
        PodFormat.Pod2 => "POD2",
        PodFormat.Epd => "EPD",
        _ => Format.ToString(),
    };

    /// <summary>True for POD version 1 archives.</summary>
    public bool IsPod1Family => Format is PodFormat.Pod1;

    /// <summary>Entries whose name ends with <paramref name="extension"/>, compared case-insensitively.</summary>
    public IReadOnlyList<PodEntry> GetEntriesByExtension(string extension)
    {
        List<PodEntry> matches = [];
        foreach (PodEntry entry in Entries)
        {
            if (entry.Name.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
            {
                matches.Add(entry);
            }
        }

        return matches;
    }

    /// <summary>Full-name lookup, case-insensitive. First match wins on duplicates.</summary>
    public PodEntry? FindEntry(string name)
    {
        _byName ??= BuildIndex(static entry => entry.Name);
        return _byName.TryGetValue(name, out PodEntry? entry) ? entry : null;
    }

    /// <summary>File-name-only lookup, case-insensitive. First match wins on duplicates.</summary>
    public PodEntry? FindEntryByTitle(string name)
    {
        _byTitle ??= BuildIndex(static entry => entry.Title);
        return _byTitle.TryGetValue(name, out PodEntry? entry) ? entry : null;
    }

    /// <summary>
    /// Indexes the entries by one of their names. Shipped and community archives do
    /// repeat a name, so the first occurrence is kept, matching what the linear scan
    /// these replaced used to return.
    /// </summary>
    private Dictionary<string, PodEntry> BuildIndex(Func<PodEntry, string> key)
    {
        Dictionary<string, PodEntry> index = new(Entries.Count, StringComparer.OrdinalIgnoreCase);
        foreach (PodEntry entry in Entries)
        {
            string name = key(entry);
            if (!index.ContainsKey(name))
            {
                index.Add(name, entry);
            }
        }

        return index;
    }

    /// <summary>
    /// The entry's payload, read from the archive on demand. Bounds were validated at
    /// parse time, so the range is always inside the archive.
    ///
    /// <para>Prefer <see cref="CopyEntryTo"/> when the bytes are only going to be
    /// written somewhere: this materializes the whole payload as one array.</para>
    /// </summary>
    public byte[] GetEntryBytes(PodEntry entry) =>
        _data.ReadExact(entry.Offset, checked((int)entry.Length));

    /// <summary>Streams an entry's payload to a stream without holding it in memory.</summary>
    public void CopyEntryTo(PodEntry entry, Stream target, byte[] scratch) =>
        _data.CopyTo(entry.Offset, entry.Length, target, scratch);

    /// <summary>Releases the archive file handle.</summary>
    public void Dispose() => _data.Dispose();
}

/// <summary>One file inside a POD archive.</summary>
public sealed record PodEntry(string Name, long Length, long Offset)
{
    public uint Timestamp { get; init; }

    public uint Checksum { get; init; }
    /// <summary>
    /// The directory name field exactly as it was read, terminator and trailing
    /// bytes included, or null for entries not read from a POD1 directory.
    ///
    /// <para>Most archives leave nothing after the terminator but zeros. Some do
    /// not: Fury3's <c>FURYSE.POD</c> packs a second NUL-terminated string, an
    /// <c>.ACT</c> palette name, into the spare bytes of every <c>ART\*.RAW</c>
    /// entry. Nothing documents that and no reader here looks past the first
    /// terminator, but re-saving the archive should not silently drop bytes that
    /// were in the file, so the field is kept and written back verbatim.</para>
    /// </summary>
    public byte[]? RawNameField { get; init; }

    /// <summary>
    /// The palette this entry's art was authored against, read from the second
    /// string in the directory field, or null when there is none.
    ///
    /// <para>The early Terminal Reality packer wrote it for every <c>.RAW</c>
    /// entry in MTM1, Terminal Velocity, Fury3 and Hellbender; MTM2 and CPR
    /// dropped the practice. Where it is present it is authoritative, and it
    /// names an <c>.ACT</c> that exists in the same archive.</para>
    /// </summary>
    public string? EmbeddedPaletteName => PodNameField.SecondString(RawNameField);

    /// <summary>
    /// File name without any directory part, upper-cased.
    ///
    /// <para>Deliberately not cached in a field. This is a record, so a field would
    /// join its value equality, and two otherwise identical entries would stop
    /// comparing equal as soon as one of them had its title read. The repeated cost
    /// this would have saved is gone anyway: <see cref="PodArchive.FindEntryByTitle"/>
    /// now indexes the titles once instead of recomputing them per lookup.</para>
    /// </summary>
    public string Title
    {
        get
        {
            int slash = Math.Max(Name.LastIndexOf('/'), Name.LastIndexOf('\\'));
            return (slash >= 0 ? Name.Substring(slash + 1) : Name).ToUpperInvariant();
        }
    }
}

public enum PodAuditAction { Add, Remove, Change }

/// <summary>One 312-byte POD2 audit record.</summary>
public sealed record PodAuditEntry(
    string User,
    uint Timestamp,
    PodAuditAction Action,
    string EntryPath,
    uint OldTimestamp,
    uint OldSize,
    uint NewTimestamp,
    uint NewSize);
