using KPod.Core.Compat;

namespace KPod.Core.Pods;

/// <summary>
/// Strict POD1/POD1-64 and isolated POD2 archive writer.
///
/// <para>Archives are written straight to the output stream, one payload at a time
/// through a shared transfer buffer, so building a 200 MB archive costs the directory
/// plus that buffer rather than the archive twice over. <see cref="Write(string, string?,
/// IReadOnlyList{PodBlob}, PodWriteOptions)"/> builds beside the target and replaces
/// it, which keeps a failed write from destroying the original and makes it safe to
/// save over an archive that is currently open for reading.</para>
/// </summary>
public static class PodArchiveWriter
{
    private const int CommentSize = 80;
    private const int EntryNameSize = 32;
    private const int LongNameSize = 64;
    private const int Pod2HeaderSize = 96;
    private const int Pod2EntrySize = 20;
    private const int Pod2AuditSize = 312;
    private const int MaxItems = PodArchiveReader.MaxReasonableItems;
    public const int MaxNameLength = LongNameSize - 1;

    /// <summary>Suffix of the temporary file a write builds before replacing the target.</summary>
    private const string TempSuffix = ".kpodtmp";

    public static PodFormat Write(string path, string? comment, IReadOnlyList<PodBlob> blobs,
        byte[]? rawCommentField = null) =>
        Write(path, comment, blobs, new PodWriteOptions(PodFormat.Pod1, rawCommentField, []));

    /// <param name="beforeReplace">
    /// Runs once the new archive is complete on disk and before it takes the target's
    /// place. A caller that is reading from the target, which is what saving over the
    /// archive you have open means, releases its handle here: the payloads have all
    /// been streamed out by then, and the swap needs the target free.
    /// </param>
    public static PodFormat Write(string path, string? comment, IReadOnlyList<PodBlob> blobs,
        PodWriteOptions options, Action? beforeReplace = null)
    {
        ValidateInputs(comment, blobs, options.AllowDuplicateNames);
        PodFormat actual = ActualFormat(blobs, options.Format);
        string? parent = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(parent)) Directory.CreateDirectory(parent!);

        // Build beside the target and swap. Until the swap the target is untouched,
        // so a source archive being read from can also be the destination.
        string temp = path + TempSuffix;
        try
        {
            using (FileStream output = new(temp, FileMode.Create, FileAccess.ReadWrite,
                FileShare.None, PodDataSource.ScratchSize))
            {
                if (actual == PodFormat.Pod2)
                    WritePod2(output, comment, blobs, options.AuditEntries);
                else
                    WritePod1(output, comment, blobs, options.RawCommentField, actual);
            }
            beforeReplace?.Invoke();
            Replace(temp, path);
        }
        finally
        {
            DeleteQuietly(temp);
        }

        return actual;
    }

    /// <summary>Swaps the finished temporary file onto the target path.</summary>
    private static void Replace(string temp, string path)
    {
        if (File.Exists(path))
        {
            // Keeps the destination's identity and attributes, and needs only the
            // delete access that FilePodDataSource's share mode already permits.
            File.Replace(temp, path, null);
            return;
        }

        File.Move(temp, path);
    }

    private static void DeleteQuietly(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A leftover temp file is not worth failing a completed save over.
        }
    }

    public static PodFormat FormatFor(IReadOnlyList<PodBlob> blobs) =>
        ActualFormat(blobs, PodFormat.Pod1);

    public static PodFormat ActualFormat(IReadOnlyList<PodBlob> blobs, PodFormat requested)
    {
        if (requested == PodFormat.Pod2) return requested;
        if (requested is not (PodFormat.Pod1 or PodFormat.Pod1Extended))
            throw new ArgumentException("Unsupported output format: " + requested, nameof(requested));
        if (requested == PodFormat.Pod1Extended) return requested;
        foreach (PodBlob blob in blobs)
        {
            if (RequiredNameFieldLength(blob) > EntryNameSize) return PodFormat.Pod1Extended;
        }

        return PodFormat.Pod1;
    }

    /// <summary>Width of a classic POD1 directory name field.</summary>
    public const int ClassicNameFieldSize = EntryNameSize;

    /// <summary>Width of a POD1-64 directory name field.</summary>
    public const int LongNameFieldSize = LongNameSize;

    /// <summary>
    /// Bytes a directory name field must hold for this name and palette name,
    /// terminators included. Exposed so callers can decide the output format without
    /// building a <see cref="PodBlob"/> per entry: the browser asks that on every
    /// list refresh, which is every filter keystroke.
    /// </summary>
    public static int RequiredNameFieldBytes(string name, string? embeddedPaletteName) =>
        RequiredNameFieldLength(name, embeddedPaletteName);

    /// <summary>
    /// Every check a write performs, without producing the archive.
    ///
    /// <para>Pre-save validation used to call <see cref="BuildBytes(string?,
    /// IReadOnlyList{PodBlob}, PodWriteOptions)"/> and throw the result away, which
    /// meant renaming one entry serialized the whole archive. Nothing it was catching
    /// needs the payloads: the directory constraints come from the names, and the
    /// overflow checks come from the lengths.</para>
    /// </summary>
    public static void ValidateLayout(string? comment, IReadOnlyList<PodBlob> blobs,
        PodWriteOptions options)
    {
        ValidateInputs(comment, blobs, options.AllowDuplicateNames);
        PodFormat actual = ActualFormat(blobs, options.Format);
        if (actual == PodFormat.Pod2)
        {
            Pod2Layout(comment, blobs, options.AuditEntries);
        }
        else
        {
            Pod1Layout(blobs, actual);
        }
    }

    public static byte[] BuildBytes(string? comment, IReadOnlyList<PodBlob> blobs,
        byte[]? rawCommentField = null) =>
        BuildBytes(comment, blobs, new PodWriteOptions(PodFormat.Pod1, rawCommentField, []));

    /// <summary>
    /// The archive as one array. Kept for fixtures and in-memory callers;
    /// <see cref="Write(string, string?, IReadOnlyList{PodBlob}, PodWriteOptions)"/>
    /// streams instead and does not go through here.
    /// </summary>
    public static byte[] BuildBytes(string? comment, IReadOnlyList<PodBlob> blobs,
        PodWriteOptions options)
    {
        ValidateInputs(comment, blobs, options.AllowDuplicateNames);
        PodFormat actual = ActualFormat(blobs, options.Format);

        // The size is known exactly before a byte is written, so presize and skip the
        // doubling reallocations a growing MemoryStream would do.
        using MemoryStream output = new(ExactSize(comment, blobs, options, actual));
        if (actual == PodFormat.Pod2)
            WritePod2(output, comment, blobs, options.AuditEntries);
        else
            WritePod1(output, comment, blobs, options.RawCommentField, actual);
        return output.ToArray();
    }

    private static int ExactSize(string? comment, IReadOnlyList<PodBlob> blobs,
        PodWriteOptions options, PodFormat actual)
    {
        if (actual == PodFormat.Pod2)
        {
            Pod2Plan plan = Pod2Layout(comment, blobs, options.AuditEntries);
            return checked(plan.DataStart + TotalPayload(blobs)
                + (options.AuditEntries.Count * Pod2AuditSize));
        }

        Pod1Plan pod1 = Pod1Layout(blobs, actual);
        return checked(pod1.HeaderSize + TotalPayload(blobs));
    }

    private static int TotalPayload(IReadOnlyList<PodBlob> blobs)
    {
        int total = 0;
        foreach (PodBlob blob in blobs) total = checked(total + blob.Length);
        return total;
    }

    // -------------------------------------------------------------------------
    // Layout: everything that can be decided from names and lengths alone
    // -------------------------------------------------------------------------

    private readonly struct Pod1Plan(int nameSize, int headerSize, int[] offsets)
    {
        public int NameSize { get; } = nameSize;
        public int HeaderSize { get; } = headerSize;
        public int[] Offsets { get; } = offsets;
    }

    private readonly struct Pod2Plan(byte[] names, int[] nameOffsets, int dataStart, int[] offsets)
    {
        public byte[] Names { get; } = names;
        public int[] NameOffsets { get; } = nameOffsets;
        public int DataStart { get; } = dataStart;
        public int[] Offsets { get; } = offsets;
    }

    private static Pod1Plan Pod1Layout(IReadOnlyList<PodBlob> blobs, PodFormat actual)
    {
        int nameSize = actual == PodFormat.Pod1Extended ? LongNameSize : EntryNameSize;
        foreach (PodBlob blob in blobs)
        {
            if (RequiredNameFieldLength(blob) > nameSize)
                throw new PodFormatException(
                    $"POD entry name/palette exceeds {nameSize - 1} usable bytes: {blob.Name}");
        }
        int headerSize = checked(sizeof(int) + CommentSize + (blobs.Count * (nameSize + 8)));
        return new Pod1Plan(nameSize, headerSize, OffsetsFor(blobs, headerSize));
    }

    private static Pod2Plan Pod2Layout(string? comment, IReadOnlyList<PodBlob> blobs,
        IReadOnlyList<PodAuditEntry> audits)
    {
        if (audits.Count > MaxItems)
            throw new PodFormatException("Too many POD2 audit records: " + audits.Count);
        CheckFixedField(comment, CommentSize, "POD2 comment");
        foreach (PodAuditEntry audit in audits)
        {
            CheckFixedField(audit.User, 32, "Audit user");
            CheckFixedField(audit.EntryPath, 256, "Audit entry path");
        }

        using MemoryStream names = new();
        int[] nameOffsets = new int[blobs.Count];
        for (int i = 0; i < blobs.Count; i++)
        {
            nameOffsets[i] = checked((int)names.Length);
            Write(names, PodText.Latin1.GetBytes(blobs[i].Name));
            names.WriteByte(0);
        }

        byte[] nameBlob = names.ToArray();
        int dataStart = checked(Pod2HeaderSize + (blobs.Count * Pod2EntrySize) + nameBlob.Length);
        return new Pod2Plan(nameBlob, nameOffsets, dataStart, OffsetsFor(blobs, dataStart));
    }

    // -------------------------------------------------------------------------
    // Writing
    // -------------------------------------------------------------------------

    private static void WritePod1(Stream output, string? comment, IReadOnlyList<PodBlob> blobs,
        byte[]? rawCommentField, PodFormat actual)
    {
        Pod1Plan plan = Pod1Layout(blobs, actual);
        WriteInt32(output, blobs.Count);
        WriteCommentField(output, comment, rawCommentField);
        for (int i = 0; i < blobs.Count; i++)
        {
            WriteNameField(output, blobs[i], plan.NameSize);
            WriteInt32(output, blobs[i].Length);
            WriteInt32(output, plan.Offsets[i]);
        }

        byte[] scratch = PodDataSource.NewScratch();
        foreach (PodBlob blob in blobs) blob.CopyTo(output, scratch);
    }

    private static void WritePod2(Stream output, string? comment, IReadOnlyList<PodBlob> blobs,
        IReadOnlyList<PodAuditEntry> audits)
    {
        Pod2Plan plan = Pod2Layout(comment, blobs, audits);
        byte[] scratch = PodDataSource.NewScratch();
        long start = output.Position;

        // The archive checksum covers everything after it, so accumulate it as the
        // bytes go past rather than reading the finished archive back.
        Write(output, [(byte)'P', (byte)'O', (byte)'D', (byte)'2']);
        WriteInt32(output, 0);
        Crc32Stream hashed = new(output);
        WriteFixedStrict(hashed, comment, CommentSize, "POD2 comment");
        WriteInt32(hashed, blobs.Count);
        WriteInt32(hashed, audits.Count);
        for (int i = 0; i < blobs.Count; i++)
        {
            WriteInt32(hashed, plan.NameOffsets[i]);
            WriteInt32(hashed, blobs[i].Length);
            WriteInt32(hashed, plan.Offsets[i]);
            WriteInt32(hashed, unchecked((int)blobs[i].Timestamp));
            WriteInt32(hashed, unchecked((int)blobs[i].Crc32Mpeg2(scratch)));
        }
        Write(hashed, plan.Names);
        foreach (PodBlob blob in blobs) blob.CopyTo(hashed, scratch);
        foreach (PodAuditEntry audit in audits) WriteAudit(hashed, audit);

        long end = output.Position;
        output.Seek(start + 4, SeekOrigin.Begin);
        WriteInt32(output, unchecked((int)hashed.Value));
        output.Seek(end, SeekOrigin.Begin);
    }

    /// <summary>
    /// Rejects what the directory cannot encode, plus unsafe paths and, unless
    /// <paramref name="allowDuplicateNames"/>, names that collide case-insensitively.
    /// <para>
    /// Duplicates are a genuine authoring mistake but not a malformed archive:
    /// shipped and community PODs repeat a name so the later copy shadows the
    /// earlier one. Refusing to write them would make an opened archive unsavable,
    /// so the caller grandfathers the ones it read in and PodArchiveValidator
    /// reports them instead.
    /// </para>
    /// </summary>
    private static void ValidateInputs(string? comment, IReadOnlyList<PodBlob> blobs,
        bool allowDuplicateNames)
    {
        if (blobs is null || blobs.Count == 0)
            throw new ArgumentException("At least one POD entry is required.", nameof(blobs));
        if (blobs.Count > MaxItems) throw new PodFormatException("Too many POD entries: " + blobs.Count);
        if (NameByteLength(comment) > CommentSize - 1)
            throw new PodFormatException("POD comment exceeds 79 bytes");
        HashSet<string> names = new(StringComparer.OrdinalIgnoreCase);
        foreach (PodBlob blob in blobs)
        {
            ValidatePath(blob.Name);
            if (!names.Add(blob.Name) && !allowDuplicateNames)
                throw new PodFormatException("Duplicate POD entry name: " + blob.Name);
        }
    }

    public static string NormalizeName(string name) => name.Replace('/', '\\');

    public static void ValidatePath(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.StartsWith('\\') || name.EndsWith('\\')
            || name.Contains(':') || name.Contains('\0'))
            throw new PodFormatException("Unsafe POD entry path: " + name);
        foreach (string part in name.Split('\\'))
        {
            if (part.Length == 0 || part is "." or ".." || part.Any(c => c < 0x20))
                throw new PodFormatException("Unsafe POD entry path: " + name);
        }
    }

    private static int[] OffsetsFor(IReadOnlyList<PodBlob> blobs, int start)
    {
        int[] offsets = new int[blobs.Count];
        int cursor = start;
        for (int i = 0; i < blobs.Count; i++)
        {
            offsets[i] = cursor;
            cursor = checked(cursor + blobs[i].Length);
        }
        return offsets;
    }

    private static void WriteCommentField(Stream target, string? comment, byte[]? original)
    {
        if (original is { Length: CommentSize } && DecodeField(original) == (comment ?? string.Empty))
        {
            Write(target, original);
            return;
        }
        WriteFixedStrict(target, comment, CommentSize, "POD comment");
    }

    private static void WriteNameField(Stream target, PodBlob blob, int nameSize)
    {
        byte[]? original = blob.RawNameField;
        if (original is not null && original.Length == nameSize && DecodeField(original) == blob.Name)
        {
            Write(target, original);
            return;
        }
        byte[] field = new byte[nameSize];
        byte[] name = PodText.Latin1.GetBytes(blob.Name);
        Array.Copy(name, field, name.Length);
        if (!string.IsNullOrWhiteSpace(blob.EmbeddedPaletteName))
        {
            byte[] palette = PodText.Latin1.GetBytes(blob.EmbeddedPaletteName!);
            Array.Copy(palette, 0, field, name.Length + 1, palette.Length);
        }
        Write(target, field);
    }

    private static string DecodeField(byte[] field)
    {
        int end = Array.IndexOf(field, (byte)0);
        if (end < 0) end = field.Length;
        return PodText.Latin1.GetString(field, 0, end).TrimAsciiControl();
    }

    private static int RequiredNameFieldLength(PodBlob blob) =>
        RequiredNameFieldLength(blob.Name, blob.EmbeddedPaletteName);

    private static int RequiredNameFieldLength(string name, string? embeddedPaletteName) =>
        NameByteLength(name) + 1 +
        (string.IsNullOrWhiteSpace(embeddedPaletteName)
            ? 0 : NameByteLength(embeddedPaletteName) + 1);

    private static int NameByteLength(string? value) =>
        value is null ? 0 : PodText.Latin1.GetByteCount(value);

    private static void CheckFixedField(string? value, int size, string label)
    {
        if (NameByteLength(value) > size - 1)
            throw new PodFormatException($"{label} exceeds {size - 1} bytes");
    }

    private static void WriteFixedStrict(Stream target, string? value, int size, string label)
    {
        byte[] bytes = value is null ? [] : PodText.Latin1.GetBytes(value);
        if (bytes.Length > size - 1) throw new PodFormatException($"{label} exceeds {size - 1} bytes");
        Write(target, bytes);
        Write(target, new byte[size - bytes.Length]);
    }

    private static void WriteAudit(Stream target, PodAuditEntry audit)
    {
        WriteFixedStrict(target, audit.User, 32, "Audit user");
        WriteInt32(target, unchecked((int)audit.Timestamp));
        WriteInt32(target, (int)audit.Action);
        WriteFixedStrict(target, audit.EntryPath, 256, "Audit entry path");
        WriteInt32(target, unchecked((int)audit.OldTimestamp));
        WriteInt32(target, unchecked((int)audit.OldSize));
        WriteInt32(target, unchecked((int)audit.NewTimestamp));
        WriteInt32(target, unchecked((int)audit.NewSize));
    }

    // -------------------------------------------------------------------------
    // CRC-32/MPEG-2
    // -------------------------------------------------------------------------

    /// <summary>Initial value of a POD2 checksum, and the seed to start one with.</summary>
    public const uint Crc32Seed = 0xffffffff;

    /// <summary>
    /// One entry per leading byte value, so a byte costs a table lookup and a shift
    /// instead of eight shift-and-branch steps.
    /// </summary>
    private static readonly uint[] Crc32Table = BuildCrc32Table();

    private static uint[] BuildCrc32Table()
    {
        uint[] table = new uint[256];
        for (int value = 0; value < 256; value++)
        {
            uint crc = (uint)value << 24;
            for (int bit = 0; bit < 8; bit++)
            {
                crc = (crc << 1) ^ ((crc & 0x80000000) != 0 ? 0x04C11DB7u : 0);
            }

            table[value] = crc;
        }

        return table;
    }

    /// <summary>
    /// The POD2 checksum: CRC-32/MPEG-2, i.e. polynomial 0x04C11DB7, initial
    /// 0xffffffff, MSB-first, and no final XOR. Check value for "123456789" is
    /// 0x0376E6E7. Not CRC-32/CCITT, despite what the shape of it suggests.
    ///
    /// <para>Note for anyone tempted to swap in a library: <c>System.IO.Hashing.Crc32</c>
    /// is the reflected IEEE polynomial, which is a different checksum and would
    /// silently invalidate every POD2 archive KPod writes.</para>
    /// </summary>
    public static uint Crc32Mpeg2(byte[] bytes) => Crc32Mpeg2(bytes, 0, bytes.Length);

    public static uint Crc32Mpeg2(byte[] bytes, int offset, int length) =>
        Crc32Mpeg2(bytes, offset, length, Crc32Seed);

    /// <summary>
    /// Continues a running checksum, so a whole-archive CRC can be accumulated while
    /// the archive is written rather than in a second pass over it.
    /// </summary>
    public static uint Crc32Mpeg2(byte[] bytes, int offset, int length, uint seed)
    {
        uint crc = seed;
        int end = offset + length;
        for (int i = offset; i < end; i++)
        {
            crc = (crc << 8) ^ Crc32Table[((crc >> 24) ^ bytes[i]) & 0xff];
        }

        return crc;
    }

    /// <summary>Checksums a range of a data source without holding it in memory.</summary>
    public static uint Crc32Mpeg2(IPodDataSource source, long offset, long count,
        uint seed = Crc32Seed)
    {
        byte[] scratch = PodDataSource.NewScratch();
        uint crc = seed;
        long remaining = count;
        long at = offset;
        while (remaining > 0)
        {
            int want = (int)Math.Min(scratch.Length, remaining);
            int read = source.Read(at, scratch, 0, want);
            if (read <= 0) break;
            crc = Crc32Mpeg2(scratch, 0, read, crc);
            at += read;
            remaining -= read;
        }

        return crc;
    }

    /// <summary>
    /// Passes writes straight through while folding them into a running checksum, so
    /// the POD2 archive CRC costs nothing beyond the write that was happening anyway.
    /// </summary>
    private sealed class Crc32Stream(Stream inner) : Stream
    {
        public uint Value { get; private set; } = Crc32Seed;

        public override void Write(byte[] buffer, int offset, int count)
        {
            Value = Crc32Mpeg2(buffer, offset, count, Value);
            inner.Write(buffer, offset, count);
        }

        public override void WriteByte(byte value)
        {
            Value = (Value << 8) ^ Crc32Table[((Value >> 24) ^ value) & 0xff];
            inner.WriteByte(value);
        }

        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => inner.Length;
        public override void Flush() => inner.Flush();

        public override long Position
        {
            get => inner.Position;
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();

        public override long Seek(long offset, SeekOrigin origin) =>
            throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();
    }

    private static void Write(Stream target, byte[] bytes) => target.Write(bytes, 0, bytes.Length);
    private static void WriteInt32(Stream target, int value)
    {
        target.WriteByte((byte)value); target.WriteByte((byte)(value >> 8));
        target.WriteByte((byte)(value >> 16)); target.WriteByte((byte)(value >> 24));
    }
}

/// <summary>
/// One entry handed to the writer: a name plus the bytes to store under it.
///
/// <para>The bytes are either held directly, for an entry added from disk or built in
/// memory, or referenced in an <see cref="IPodDataSource"/>, for an entry that came
/// from an archive and has never needed materializing. <see cref="Length"/> is always
/// known either way, which is what lets the writer plan the whole layout before
/// touching a payload.</para>
/// </summary>
public sealed record PodBlob
{
    private readonly byte[]? _data;
    private readonly IPodDataSource? _source;
    private readonly long _offset;

    public PodBlob(string name, byte[] data, byte[]? rawNameField = null,
        string? embeddedPaletteName = null, uint? timestamp = null)
        : this(name, rawNameField, embeddedPaletteName, timestamp)
    {
        _data = data ?? throw new ArgumentNullException(nameof(data));
        Length = data.Length;
    }

    /// <summary>A blob that reads its payload from an archive only when asked.</summary>
    public PodBlob(string name, IPodDataSource source, long offset, int length,
        byte[]? rawNameField = null, string? embeddedPaletteName = null, uint? timestamp = null)
        : this(name, rawNameField, embeddedPaletteName, timestamp)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _offset = offset;
        Length = length;
    }

    private PodBlob(string name, byte[]? rawNameField, string? embeddedPaletteName, uint? timestamp)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("POD entry name is required.", nameof(name));
        Name = PodArchiveWriter.NormalizeName(name);
        RawNameField = rawNameField;
        EmbeddedPaletteName = embeddedPaletteName ?? PodNameField.SecondString(rawNameField);
        Timestamp = timestamp ?? unchecked((uint)DateTimeOffset.UtcNow.ToUnixTimeSeconds());
    }

    public string Name { get; }

    /// <summary>Payload size, known without reading the payload.</summary>
    public int Length { get; }

    /// <summary>
    /// The payload as one array, read from the source if it is not already held.
    /// Prefer <see cref="CopyTo"/> where the bytes are only being written onward.
    /// </summary>
    public byte[] Data => _data ?? _source!.ReadExact(_offset, Length);

    public byte[]? RawNameField { get; }
    public string? EmbeddedPaletteName { get; }
    public uint Timestamp { get; }

    /// <summary>Writes the payload to a stream without holding it in memory.</summary>
    public void CopyTo(Stream target, byte[] scratch)
    {
        if (_data is not null)
        {
            target.Write(_data, 0, _data.Length);
            return;
        }

        _source!.CopyTo(_offset, Length, target, scratch);
    }

    /// <summary>The payload's CRC-32/MPEG-2, streamed when the payload is not held.</summary>
    public uint Crc32Mpeg2(byte[] scratch)
    {
        if (_data is not null) return PodArchiveWriter.Crc32Mpeg2(_data);

        uint crc = PodArchiveWriter.Crc32Seed;
        long remaining = Length;
        long at = _offset;
        while (remaining > 0)
        {
            int want = (int)Math.Min(scratch.Length, remaining);
            int read = _source!.Read(at, scratch, 0, want);
            if (read <= 0) break;
            crc = PodArchiveWriter.Crc32Mpeg2(scratch, 0, read, crc);
            at += read;
            remaining -= read;
        }

        return crc;
    }
}

/// <param name="AllowDuplicateNames">
/// Permits entry names that collide case-insensitively. Authoring always rejects
/// them, but real archives use a repeated name as an override and must stay
/// re-savable, so opening one sets this.
/// </param>
public sealed record PodWriteOptions(
    PodFormat Format,
    byte[]? RawCommentField,
    IReadOnlyList<PodAuditEntry> AuditEntries,
    bool AllowDuplicateNames = false);
