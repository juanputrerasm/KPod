using KPod.Core.Compat;

namespace KPod.Core.Pods;

/// <summary>
/// Reads Terminal Reality POD archives into memory. Supports classic POD1, the
/// POD1-64 long-name extension, POD2 and EPD; there is no POD3+ support and no
/// checksum verification, matching JPod.
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
    private const int EpdCountOffset = 0x90;
    private const int EpdTableOffset = 0x110;
    private const int EpdTitleOffset = 4;
    private const int EpdTitleSize = 4;

    /// <summary>Archives claiming more entries than this are rejected as corrupt.</summary>
    public const int MaxReasonableItems = 8192;

    /// <summary>Longest name a POD1-64 directory record can hold, excluding the terminator.</summary>
    public const int MaxNameLength = Pod164NameSize - 1;

    public static PodArchive Read(string path)
    {
        byte[] bytes = File.ReadAllBytes(path);
        if (bytes.Length < Pod1HeaderSize)
        {
            throw new PodFormatException("File too small to be a POD archive: " + path);
        }

        if (HasMagic(bytes, EpdMagic))
        {
            return ReadEpd(bytes, path);
        }

        if (HasMagic(bytes, Pod2Magic))
        {
            return ReadPod2(bytes, path);
        }

        // POD1 carries no magic, so it is the fallback.
        return ReadPod1(bytes, path);
    }

    private static PodArchive ReadPod1(byte[] bytes, string path)
    {
        int itemCount = ReadInt32Le(bytes, 0);
        if (itemCount is < 1 or > MaxReasonableItems)
        {
            throw new PodFormatException("Suspicious POD item count: " + itemCount);
        }

        string comment = DecodeNullTerminated(bytes, sizeof(int), PodCommentSize);
        byte[] commentField = new byte[PodCommentSize];
        Array.Copy(bytes, sizeof(int), commentField, 0, PodCommentSize);

        List<PodEntry>? classic = TryReadPod1Directory(bytes, itemCount, PodEntryNameSize, Pod1EntrySize);
        if (classic is not null)
        {
            return new PodArchive(PodFormat.Pod1, comment, bytes, classic, commentField);
        }

        List<PodEntry>? extended = TryReadPod1Directory(bytes, itemCount, Pod164NameSize, Pod164EntrySize);
        if (extended is not null)
        {
            return new PodArchive(PodFormat.Pod1Extended, comment, bytes, extended, commentField);
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
    /// <param name="bytes">Complete archive contents.</param>
    /// <param name="itemCount">Directory entry count from the header.</param>
    /// <param name="nameSize">Width of the name field, 32 (classic) or 64 (POD1-64).</param>
    /// <param name="entrySize">Width of a whole record, 40 (classic) or 72 (POD1-64).</param>
    /// <returns>The parsed entries, or null if this layout does not validate.</returns>
    private static List<PodEntry>? TryReadPod1Directory(byte[] bytes, int itemCount, int nameSize, int entrySize)
    {
        long tableSize = (long)itemCount * entrySize;
        if (Pod1HeaderSize + tableSize > bytes.Length)
        {
            return null;
        }

        List<PodEntry> entries = new(itemCount);
        long dataFloor = Pod1HeaderSize + tableSize;
        for (int i = 0; i < itemCount; i++)
        {
            int entryOffset = Pod1HeaderSize + (i * entrySize);
            string name = DecodeNullTerminated(bytes, entryOffset, nameSize);
            long length = ToUnsigned(ReadInt32Le(bytes, entryOffset + nameSize));
            long offset = ToUnsigned(ReadInt32Le(bytes, entryOffset + nameSize + sizeof(int)));
            if (!IsPlausibleArchivePath(name) || offset < dataFloor
                || !IsInBounds(offset, length, bytes.Length))
            {
                return null;
            }

            byte[] nameField = new byte[nameSize];
            Array.Copy(bytes, entryOffset, nameField, 0, nameSize);
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
    private static bool IsInBounds(long offset, long length, int fileSize) =>
        offset >= 0 && length >= 0 && offset <= fileSize && length <= fileSize - offset;

    private static PodArchive ReadPod2(byte[] bytes, string path)
    {
        if (bytes.Length < Pod2HeaderSize)
        {
            throw new PodFormatException("File too small to be a POD2 archive: " + path);
        }

        string comment = DecodeNullTerminated(bytes, 8, PodCommentSize);
        uint archiveChecksum = unchecked((uint)ReadInt32Le(bytes, 4));
        int itemCount = ReadInt32Le(bytes, Pod2CountOffset);
        if (itemCount is < 1 or > MaxReasonableItems)
        {
            throw new PodFormatException("Suspicious POD2 item count: " + itemCount);
        }

        const int tableOffset = Pod2HeaderSize;
        long tableSize = (long)itemCount * Pod2EntrySize;
        if (tableOffset + tableSize > bytes.Length)
        {
            throw new PodFormatException("POD2 item table exceeds file size");
        }

        // Names live in a NUL-terminated blob right after the entry table.
        int nameTableOffset = tableOffset + (int)tableSize;
        int auditCount = ReadInt32Le(bytes, 92);
        if (auditCount is < 0 or > MaxReasonableItems)
        {
            throw new PodFormatException("Suspicious POD2 audit count: " + auditCount);
        }
        List<PodEntry> entries = new(itemCount);
        for (int i = 0; i < itemCount; i++)
        {
            int entryOffset = tableOffset + (i * Pod2EntrySize);
            int pathOffset = ReadInt32Le(bytes, entryOffset);
            long length = ToUnsigned(ReadInt32Le(bytes, entryOffset + 4));
            long offset = ToUnsigned(ReadInt32Le(bytes, entryOffset + 8));
            uint timestamp = unchecked((uint)ReadInt32Le(bytes, entryOffset + 12));
            uint checksum = unchecked((uint)ReadInt32Le(bytes, entryOffset + 16));
            long nameStart = (long)nameTableOffset + pathOffset;
            string name = nameStart is < 0 or > int.MaxValue
                ? string.Empty
                : DecodeNullTerminated(bytes, (int)nameStart, bytes.Length - (int)nameStart);
            ValidateEntryBounds(name, offset, length, bytes.Length);
            entries.Add(new PodEntry(name, length, offset)
                { Timestamp = timestamp, Checksum = checksum });
        }

        long auditOffset = entries.Max(e => e.Offset + e.Length);
        if (auditOffset < 0 || auditOffset + ((long)auditCount * Pod2AuditSize) > bytes.Length)
        {
            throw new PodFormatException("POD2 audit trail exceeds file size");
        }
        List<PodAuditEntry> audits = new(auditCount);
        for (int i = 0; i < auditCount; i++)
        {
            int at = checked((int)(auditOffset + ((long)i * Pod2AuditSize)));
            string user = DecodeNullTerminated(bytes, at, 32);
            uint timestamp = unchecked((uint)ReadInt32Le(bytes, at + 32));
            int action = ReadInt32Le(bytes, at + 36);
            if (action < 0 || action > 2)
            {
                throw new PodFormatException("Invalid POD2 audit action: " + action);
            }
            audits.Add(new PodAuditEntry(user, timestamp, (PodAuditAction)action,
                DecodeNullTerminated(bytes, at + 40, 256),
                unchecked((uint)ReadInt32Le(bytes, at + 296)),
                unchecked((uint)ReadInt32Le(bytes, at + 300)),
                unchecked((uint)ReadInt32Le(bytes, at + 304)),
                unchecked((uint)ReadInt32Le(bytes, at + 308))));
        }

        return new PodArchive(PodFormat.Pod2, comment, bytes, entries, null,
            archiveChecksum, audits);
    }

    private static PodArchive ReadEpd(byte[] bytes, string path)
    {
        if (bytes.Length < EpdTableOffset)
        {
            throw new PodFormatException("File too small to be an EPD archive: " + path);
        }

        string comment = DecodeNullTerminated(bytes, EpdTitleOffset, EpdTitleSize);
        int itemCount = ReadInt32Le(bytes, EpdCountOffset);
        if (itemCount is < 1 or > MaxReasonableItems)
        {
            throw new PodFormatException("Suspicious EPD item count: " + itemCount);
        }

        long tableSize = (long)itemCount * EpdEntrySize;
        if (EpdTableOffset + tableSize > bytes.Length)
        {
            throw new PodFormatException("EPD item table exceeds file size");
        }

        List<PodEntry> entries = new(itemCount);
        for (int i = 0; i < itemCount; i++)
        {
            int entryOffset = EpdTableOffset + (i * EpdEntrySize);
            string name = DecodeEpdEntryName(bytes, entryOffset);
            long length = ToUnsigned(ReadInt32Le(bytes, entryOffset + 64));
            long offset = ToUnsigned(ReadInt32Le(bytes, entryOffset + 68));
            ValidateEntryBounds(name, offset, length, bytes.Length);
            entries.Add(new PodEntry(name, length, offset));
        }

        return new PodArchive(PodFormat.Epd, comment, bytes, entries);
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

    private static void ValidateEntryBounds(string name, long offset, long length, int fileSize)
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
        int end = offset;
        while (end < limit && bytes[end] != 0)
        {
            end++;
        }

        return PodText.Latin1.GetString(bytes, offset, end - offset).TrimAsciiControl();
    }
}
