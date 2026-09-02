using KPod.Core.Compat;

namespace KPod.Core.Pods;

/// <summary>Terminal Reality POD archive variants KPod can read.</summary>
public enum PodFormat
{
    /// <summary>Classic POD version 1: 84-byte header, 40-byte records, 31-byte names.</summary>
    Pod1,

    /// <summary>POD1-64: 84-byte header, 72-byte records, 63-byte names.</summary>
    Pod1Extended,

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
/// <para>The Community Patch 3 extension known as POD1-64 keeps the same 84-byte
/// header but widens the directory name field from 32 to 64 bytes, so each record
/// is 72 bytes instead of 40:</para>
/// <code>
///       84  N x 72  Directory table, one record per item:
///                     +  0  64 bytes  Entry name (NUL-padded)
///                     + 64   4 bytes  Data length (uint32)
///                     + 68   4 bytes  Data offset from file start (uint32)
/// </code>
/// <para>Nothing else changes: the payload is still a concatenation of byte ranges
/// addressed by each entry's offset and length.</para>
/// </summary>
public sealed class PodArchive
{
    private readonly byte[] _bytes;

    internal PodArchive(
        PodFormat format,
        string comment,
        byte[] bytes,
        IReadOnlyList<PodEntry> entries,
        byte[]? rawCommentField = null,
        uint checksum = 0,
        IReadOnlyList<PodAuditEntry>? auditEntries = null)
    {
        Format = format;
        Comment = comment;
        _bytes = bytes;
        Entries = entries;
        RawCommentField = rawCommentField;
        Checksum = checksum;
        AuditEntries = auditEntries ?? [];
    }

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

    public bool IsChecksumValid => Format != PodFormat.Pod2
        || Checksum == PodArchiveWriter.Crc32Mpeg2(_bytes, 8, _bytes.Length - 8);

    public bool IsEntryChecksumValid(PodEntry entry) => Format != PodFormat.Pod2
        || entry.Checksum == PodArchiveWriter.Crc32Mpeg2(GetEntryBytes(entry));

    public string FormatDisplayName => Format switch
    {
        PodFormat.Pod1 => "POD1",
        PodFormat.Pod1Extended => "Extended POD1",
        PodFormat.Pod2 => "POD2",
        PodFormat.Epd => "EPD",
        _ => Format.ToString(),
    };

    /// <summary>True for the POD version 1 family: classic and POD1-64 alike.</summary>
    public bool IsPod1Family => Format is PodFormat.Pod1 or PodFormat.Pod1Extended;

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

    /// <summary>Full-name lookup, case-insensitive.</summary>
    public PodEntry? FindEntry(string name)
    {
        foreach (PodEntry entry in Entries)
        {
            if (string.Equals(entry.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                return entry;
            }
        }

        return null;
    }

    /// <summary>File-name-only lookup, case-insensitive.</summary>
    public PodEntry? FindEntryByTitle(string name)
    {
        foreach (PodEntry entry in Entries)
        {
            if (string.Equals(entry.Title, name, StringComparison.OrdinalIgnoreCase))
            {
                return entry;
            }
        }

        return null;
    }

    /// <summary>
    /// The entry's payload. Bounds were validated at parse time, so the range is
    /// always inside the archive. Returns a copy rather than a span so the same
    /// code compiles for .NET Framework, where Encoding has no span overloads.
    /// </summary>
    public byte[] GetEntryBytes(PodEntry entry)
    {
        int offset = checked((int)entry.Offset);
        int length = checked((int)entry.Length);
        byte[] payload = new byte[length];
        Array.Copy(_bytes, offset, payload, 0, length);
        return payload;
    }
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

    /// <summary>File name without any directory part, upper-cased.</summary>
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
