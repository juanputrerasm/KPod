using KPod.Core.Compat;

namespace KPod.Core.Pods;

/// <summary>
/// Reads Terminal Reality POD archives. Supports classic POD1, the POD1-64 long-name
/// extension, POD2 and EPD; there is no POD3+ support and no checksum verification at
/// parse time, matching JPod.
///
/// <para>Only the header and the directory are read. A POD directory is a few
/// kilobytes even when the archive is hundreds of megabytes, so payloads stay on disk
/// behind an <see cref="IPodDataSource"/> until something actually asks for them. The
/// returned <see cref="PodArchive"/> owns a file handle and must be disposed.</para>
///
/// <para>Validation performed:</para>
/// <list type="bullet">
///   <item>Minimum header size check (4-byte count plus 80-byte comment)</item>
///   <item>Sanity-range check on the item count</item>
///   <item>Directory table bounds check</item>
///   <item>Per-entry data bounds check</item>
/// </list>
///
/// <para>POD version 1 has no magic value, so the directory layout is detected by
/// validating it. The classic 40-byte record is tried first and the 72-byte
/// POD1-64 record only if the classic table fails to validate, which keeps
/// ordinary archives from being reported as extended.</para>
/// </summary>
public static class PodArchiveReader
{
    private static readonly byte[] EpdMagic = [(byte)'d', (byte)'t', (byte)'x', (byte)'e'];
    private static readonly byte[] Pod2Magic = [(byte)'P', (byte)'O', (byte)'D', (byte)'2'];

    private const int PodCommentSize = 80;
    private const int PodEntryNameSize = 32;
    private const int Pod1EntrySize = 40;

    /// <summary>POD1-64 widens the directory name field to 64 bytes, giving 72-byte records.</summary>
    private const int Pod164NameSize = 64;
    private const int Pod164EntrySize = 72;

    private const int Pod2EntrySize = 20;
    private const int Pod2AuditSize = 312;
    private const int EpdEntrySize = 80;
    private const int Pod1HeaderSize = sizeof(int) + PodCommentSize;            // 84
    private const int Pod2HeaderSize = 8 + PodCommentSize + sizeof(int) + sizeof(int); // 96
    private const int Pod2CountOffset = 88;
    private const int Pod2AuditCountOffset = 92;
    private const int EpdCountOffset = 0x90;
    private const int EpdTableOffset = 0x110;
    private const int EpdTitleOffset = 4;
    private const int EpdTitleSize = 4;

    /// <summary>
    /// The widest a POD2 name can be, taken from the 256-byte entry-path field of an
    /// audit record. Used only to bound the name-blob read when an archive's payload
    /// offsets are too damaged to say where the blob ends.
    /// </summary>
    private const int Pod2MaxNameSize = 256;

    /// <summary>Archives claiming more entries than this are rejected as corrupt.</summary>
    public const int MaxReasonableItems = 8192;

    /// <summary>Longest name a POD1-64 directory record can hold, excluding the terminator.</summary>
    public const int MaxNameLength = Pod164NameSize - 1;

    /// <summary>
    /// Opens an archive from disk. The archive keeps the file open for as long as it
    /// lives, so dispose it when done.
    /// </summary>
    public static PodArchive Read(string path)
    {
        FilePodDataSource source = new(path);
        try
        {
            return Read(source, path);
        }
        catch
        {
            source.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Reads an archive already held in memory, for fixtures and for callers that
    /// built the bytes themselves.
    /// </summary>
    public static PodArchive Read(byte[] bytes, string name = "<memory>")
    {
        MemoryPodDataSource source = new(bytes);
        try
        {
            return Read(source, name);
        }
        catch
        {
            source.Dispose();
            throw;
        }
    }

    private static PodArchive Read(IPodDataSource source, string path)
    {
        if (source.Length < Pod1HeaderSize)
        {
            throw new PodFormatException("File too small to be a POD archive: " + path);
        }

        // Enough for any of the three headers; POD1's is the shortest at 84 bytes.
        byte[] head = source.ReadExact(0, (int)Math.Min(source.Length, Pod2HeaderSize));

        if (HasMagic(head, EpdMagic))
        {
            return ReadEpd(source, head, path);
        }

        if (HasMagic(head, Pod2Magic))
        {
            return ReadPod2(source, head, path);
        }

        // POD1 carries no magic, so it is the fallback.
        return ReadPod1(source, head, path);
    }

    private static PodArchive ReadPod1(IPodDataSource source, byte[] head, string path)
    {
        int itemCount = ReadInt32Le(head, 0);
        if (itemCount is < 1 or > MaxReasonableItems)
        {
            throw new PodFormatException("Suspicious POD item count: " + itemCount);
        }

        string comment = DecodeNullTerminated(head, sizeof(int), PodCommentSize);
        byte[] commentField = new byte[PodCommentSize];
        Array.Copy(head, sizeof(int), commentField, 0, PodCommentSize);

        // Read the widest table the two layouts could need, so the classic and the
        // POD1-64 attempts both work off this one buffer.
        long widest = Pod1HeaderSize + ((long)itemCount * Pod164EntrySize);
        byte[] directory = source.ReadExact(0, (int)Math.Min(widest, source.Length));

        List<PodEntry>? classic = TryReadPod1Directory(
            directory, source.Length, itemCount, PodEntryNameSize, Pod1EntrySize);
        if (classic is not null)
        {
            return new PodArchive(PodFormat.Pod1, comment, source, classic, commentField);
        }

        List<PodEntry>? extended = TryReadPod1Directory(
            directory, source.Length, itemCount, Pod164NameSize, Pod164EntrySize);
        if (extended is not null)
        {
            return new PodArchive(PodFormat.Pod1Extended, comment, source, extended, commentField);
        }

        throw new PodFormatException(
            "POD1 directory is neither a valid 40-byte nor 72-byte layout: " + path);
    }

    /// <summary>
    /// Attempts to read the whole POD1 directory table with one record layout.
    ///
    /// <para>The table is accepted only if every record decodes to a plausible
    /// non-empty archive path whose data range lies inside the file, which is what
    /// lets the classic and POD1-64 layouts be told apart without a magic value.</para>
    /// </summary>
    /// <param name="directory">Header and directory bytes, starting at file offset zero.</param>
    /// <param name="archiveLength">Size of the whole archive, for the payload bounds check.</param>
    /// <param name="itemCount">Directory entry count from the header.</param>
    /// <param name="nameSize">Width of the name field, 32 (classic) or 64 (POD1-64).</param>
    /// <param name="entrySize">Width of a whole record, 40 (classic) or 72 (POD1-64).</param>
    /// <returns>The parsed entries, or null if this layout does not validate.</returns>
    private static List<PodEntry>? TryReadPod1Directory(byte[] directory, long archiveLength,
        int itemCount, int nameSize, int entrySize)
    {
        long tableSize = (long)itemCount * entrySize;
        long tableEnd = Pod1HeaderSize + tableSize;
        if (tableEnd > archiveLength || tableEnd > directory.Length)
        {
            return null;
        }

        List<PodEntry> entries = new(itemCount);
        for (int i = 0; i < itemCount; i++)
        {
            int entryOffset = Pod1HeaderSize + (i * entrySize);
            string name = DecodeNullTerminated(directory, entryOffset, nameSize);
            long length = ToUnsigned(ReadInt32Le(directory, entryOffset + nameSize));
            long offset = ToUnsigned(ReadInt32Le(directory, entryOffset + nameSize + sizeof(int)));
            if (!IsPlausibleArchivePath(name) || offset < tableEnd
                || !IsInBounds(offset, length, archiveLength))
            {
                return null;
            }

            byte[] nameField = new byte[nameSize];
            Array.Copy(directory, entryOffset, nameField, 0, nameSize);
            entries.Add(new PodEntry(name, length, offset) { RawNameField = nameField });
        }

        return entries;
    }

    /// <summary>
    /// True when the name looks like a stored archive path: non-empty, free of
    /// control characters and drive separators, and short enough to be
    /// NUL-terminated inside a 64-byte field.
    /// </summary>
    private static bool IsPlausibleArchivePath(string name)
    {
        if (name.Length == 0 || name.Length > MaxNameLength)
        {
            return false;
        }

        foreach (char c in name)
        {
            if (c < 0x20 || c == ':')
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Overflow-safe test that an entry's byte range lies inside the archive.</summary>
    private static bool IsInBounds(long offset, long length, long fileSize) =>
        offset >= 0 && length >= 0 && offset <= fileSize && length <= fileSize - offset;

    private static PodArchive ReadPod2(IPodDataSource source, byte[] head, string path)
    {
        if (source.Length < Pod2HeaderSize)
        {
            throw new PodFormatException("File too small to be a POD2 archive: " + path);
        }

        string comment = DecodeNullTerminated(head, 8, PodCommentSize);
        uint archiveChecksum = unchecked((uint)ReadInt32Le(head, 4));
        int itemCount = ReadInt32Le(head, Pod2CountOffset);
        if (itemCount is < 1 or > MaxReasonableItems)
        {
            throw new PodFormatException("Suspicious POD2 item count: " + itemCount);
        }

        /*
          The audit trail is a different quantity from the directory - one record per edit
          over the archive's lifetime - so it legitimately outnumbers the entries, and the
          entry-count heuristic is the wrong bound for it. 4x4 Evolution 2's shipping
          TRUCK.POD carries 8,430 audit records for 1,126 entries, and every stock Evo
          archive has more records than entries.

          What the count has to satisfy is that the records fit in the file at all. Where
          they actually sit is checked against the end of the payloads once the directory
          has been read.
        */
        int auditCount = ReadInt32Le(head, Pod2AuditCountOffset);
        if (auditCount < 0 || (long)auditCount * Pod2AuditSize > source.Length)
        {
            throw new PodFormatException("Suspicious POD2 audit count: " + auditCount);
        }

        const int tableOffset = Pod2HeaderSize;
        long tableSize = (long)itemCount * Pod2EntrySize;
        if (tableOffset + tableSize > source.Length)
        {
            throw new PodFormatException("POD2 item table exceeds file size");
        }

        byte[] table = source.ReadExact(tableOffset, (int)tableSize);
        long nameTableOffset = tableOffset + tableSize;

        // Names live in a NUL-terminated blob between the entry table and the first
        // payload, so the table itself says where the blob ends.
        long firstPayload = long.MaxValue;
        long lastPayloadEnd = 0;
        for (int i = 0; i < itemCount; i++)
        {
            int entryOffset = i * Pod2EntrySize;
            long offset = ToUnsigned(ReadInt32Le(table, entryOffset + 8));
            long end = offset + ToUnsigned(ReadInt32Le(table, entryOffset + 4));
            if (offset < firstPayload)
            {
                firstPayload = offset;
            }

            if (end > lastPayloadEnd)
            {
                lastPayloadEnd = end;
            }
        }

        byte[] names = ReadPod2NameBlob(source, nameTableOffset, firstPayload, itemCount);

        List<PodEntry> entries = new(itemCount);
        for (int i = 0; i < itemCount; i++)
        {
            int entryOffset = i * Pod2EntrySize;
            long pathOffset = ToUnsigned(ReadInt32Le(table, entryOffset));
            long length = ToUnsigned(ReadInt32Le(table, entryOffset + 4));
            long offset = ToUnsigned(ReadInt32Le(table, entryOffset + 8));
            uint timestamp = unchecked((uint)ReadInt32Le(table, entryOffset + 12));
            uint checksum = unchecked((uint)ReadInt32Le(table, entryOffset + 16));
            string name = pathOffset >= names.Length
                ? string.Empty
                : DecodeNullTerminated(names, (int)pathOffset, names.Length - (int)pathOffset);
            ValidateEntryBounds(name, offset, length, source.Length);
            entries.Add(new PodEntry(name, length, offset)
                { Timestamp = timestamp, Checksum = checksum });
        }

        long auditOffset = lastPayloadEnd;
        if (auditOffset < 0 || auditOffset + ((long)auditCount * Pod2AuditSize) > source.Length)
        {
            throw new PodFormatException("POD2 audit trail exceeds file size");
        }

        List<PodAuditEntry> audits = ReadPod2Audits(source, auditOffset, auditCount);
        return new PodArchive(PodFormat.Pod2, comment, source, entries, null,
            archiveChecksum, audits);
    }

    /// <summary>
    /// The POD2 name blob, which runs from the end of the directory to the first
    /// payload. When the payload offsets are too damaged to bound it, falls back to
    /// the widest blob the entry count could justify rather than reading the archive.
    /// </summary>
    private static byte[] ReadPod2NameBlob(IPodDataSource source, long nameTableOffset,
        long firstPayload, int itemCount)
    {
        long end = firstPayload > nameTableOffset && firstPayload <= source.Length
            ? firstPayload
            : Math.Min(source.Length, nameTableOffset + ((long)itemCount * Pod2MaxNameSize));
        long size = end - nameTableOffset;
        return size <= 0 ? [] : source.ReadExact(nameTableOffset, (int)size);
    }

    private static List<PodAuditEntry> ReadPod2Audits(IPodDataSource source, long auditOffset,
        int auditCount)
    {
        List<PodAuditEntry> audits = new(auditCount);
        if (auditCount == 0)
        {
            return audits;
        }

        byte[] records = source.ReadExact(auditOffset, checked(auditCount * Pod2AuditSize));
        for (int i = 0; i < auditCount; i++)
        {
            int at = i * Pod2AuditSize;
            string user = DecodeNullTerminated(records, at, 32);
            uint timestamp = unchecked((uint)ReadInt32Le(records, at + 32));
            int action = ReadInt32Le(records, at + 36);
            if (action < 0 || action > 2)
            {
                throw new PodFormatException("Invalid POD2 audit action: " + action);
            }

            audits.Add(new PodAuditEntry(user, timestamp, (PodAuditAction)action,
                DecodeNullTerminated(records, at + 40, 256),
                unchecked((uint)ReadInt32Le(records, at + 296)),
                unchecked((uint)ReadInt32Le(records, at + 300)),
                unchecked((uint)ReadInt32Le(records, at + 304)),
                unchecked((uint)ReadInt32Le(records, at + 308))));
        }

        return audits;
    }

    private static PodArchive ReadEpd(IPodDataSource source, byte[] head, string path)
    {
        if (source.Length < EpdTableOffset)
        {
            throw new PodFormatException("File too small to be an EPD archive: " + path);
        }

        string comment = DecodeNullTerminated(head, EpdTitleOffset, EpdTitleSize);

        // The count sits past the 96 bytes already read, so take the fixed header.
        byte[] header = source.ReadExact(0, EpdTableOffset);
        int itemCount = ReadInt32Le(header, EpdCountOffset);
        if (itemCount is < 1 or > MaxReasonableItems)
        {
            throw new PodFormatException("Suspicious EPD item count: " + itemCount);
        }

        long tableSize = (long)itemCount * EpdEntrySize;
        if (EpdTableOffset + tableSize > source.Length)
        {
            throw new PodFormatException("EPD item table exceeds file size");
        }

        byte[] table = source.ReadExact(EpdTableOffset, (int)tableSize);
        List<PodEntry> entries = new(itemCount);
        for (int i = 0; i < itemCount; i++)
        {
            int entryOffset = i * EpdEntrySize;
            string name = DecodeEpdEntryName(table, entryOffset);
            long length = ToUnsigned(ReadInt32Le(table, entryOffset + 64));
            long offset = ToUnsigned(ReadInt32Le(table, entryOffset + 68));
            ValidateEntryBounds(name, offset, length, source.Length);
            entries.Add(new PodEntry(name, length, offset));
        }

        return new PodArchive(PodFormat.Epd, comment, source, entries);
    }

    /// <summary>
    /// EPD splits a name across a 4-byte prefix and a 60-byte suffix. When the
    /// suffix opens with a backslash and the prefix looks like a directory name,
    /// the two are joined; otherwise the suffix alone is the name.
    /// </summary>
    private static string DecodeEpdEntryName(byte[] bytes, int entryOffset)
    {
        string suffix = DecodeNullTerminated(bytes, entryOffset + 4, 60);
        string prefix = DecodeNullTerminated(bytes, entryOffset, 4);
        if (suffix.Length > 0 && suffix[0] == '\\' && IsLikelyPathPrefix(prefix))
        {
            return prefix + suffix;
        }

        return suffix.Length > 0 ? suffix : DecodeNullTerminated(bytes, entryOffset, 64);
    }

    /// <summary>Upper-case letters, digits and underscore only; lower case is rejected.</summary>
    private static bool IsLikelyPathPrefix(string prefix)
    {
        if (prefix.Length == 0)
        {
            return false;
        }

        foreach (char c in prefix)
        {
            if (c is not (>= 'A' and <= 'Z') and not (>= '0' and <= '9') and not '_')
            {
                return false;
            }
        }

        return true;
    }

    private static bool HasMagic(byte[] bytes, byte[] magic)
    {
        if (bytes.Length < magic.Length)
        {
            return false;
        }

        for (int i = 0; i < magic.Length; i++)
        {
            if (bytes[i] != magic[i])
            {
                return false;
            }
        }

        return true;
    }

    private static void ValidateEntryBounds(string name, long offset, long length, long fileSize)
    {
        if (!IsInBounds(offset, length, fileSize))
        {
            throw new PodFormatException("POD entry exceeds file size: " + name);
        }
    }

    private static int ReadInt32Le(byte[] bytes, int offset) =>
        bytes[offset]
        | (bytes[offset + 1] << 8)
        | (bytes[offset + 2] << 16)
        | (bytes[offset + 3] << 24);

    private static long ToUnsigned(int value) => (uint)value;

    /// <summary>
    /// Decodes a NUL-terminated Latin-1 string, trimming the way Java's
    /// <c>String.trim()</c> does. Deliberately defensive: an out-of-range window
    /// yields an empty string rather than throwing.
    /// </summary>
    private static string DecodeNullTerminated(byte[] bytes, int offset, int length)
    {
        if (offset < 0 || offset >= bytes.Length || length <= 0)
        {
            return string.Empty;
        }

        int limit = (int)Math.Min(offset + (long)length, bytes.Length);
        int terminator = Array.IndexOf(bytes, (byte)0, offset, limit - offset);
        int end = terminator < 0 ? limit : terminator;
        return PodText.Latin1.GetString(bytes, offset, end - offset).TrimAsciiControl();
    }
}
