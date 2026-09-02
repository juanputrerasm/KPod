using KPod.Core.Compat;

namespace KPod.Core.Pods;

/// <summary>Strict POD1/POD1-64 and isolated POD2 archive writer.</summary>
public static class PodArchiveWriter
{
    private const int CommentSize = 80;
    private const int EntryNameSize = 32;
    private const int LongNameSize = 64;
    private const int Pod2HeaderSize = 96;
    private const int Pod2EntrySize = 20;
    private const int MaxItems = PodArchiveReader.MaxReasonableItems;
    public const int MaxNameLength = LongNameSize - 1;

    public static PodFormat Write(string path, string? comment, IReadOnlyList<PodBlob> blobs,
        byte[]? rawCommentField = null) =>
        Write(path, comment, blobs, new PodWriteOptions(PodFormat.Pod1, rawCommentField, []));

    public static PodFormat Write(string path, string? comment, IReadOnlyList<PodBlob> blobs,
        PodWriteOptions options)
    {
        PodFormat actual = ActualFormat(blobs, options.Format);
        byte[] bytes = BuildBytes(comment, blobs, options);
        string? parent = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(parent)) Directory.CreateDirectory(parent!);
        File.WriteAllBytes(path, bytes);
        return actual;
    }

    public static PodFormat FormatFor(IReadOnlyList<PodBlob> blobs) =>
        ActualFormat(blobs, PodFormat.Pod1);

    public static PodFormat ActualFormat(IReadOnlyList<PodBlob> blobs, PodFormat requested)
    {
        if (requested == PodFormat.Pod2) return requested;
        if (requested is not (PodFormat.Pod1 or PodFormat.Pod1Extended))
            throw new ArgumentException("Unsupported output format: " + requested, nameof(requested));
        if (requested == PodFormat.Pod1Extended) return requested;
        return blobs.Any(blob => RequiredNameFieldLength(blob) > EntryNameSize)
            ? PodFormat.Pod1Extended : PodFormat.Pod1;
    }

    public static byte[] BuildBytes(string? comment, IReadOnlyList<PodBlob> blobs,
        byte[]? rawCommentField = null) =>
        BuildBytes(comment, blobs, new PodWriteOptions(PodFormat.Pod1, rawCommentField, []));

    public static byte[] BuildBytes(string? comment, IReadOnlyList<PodBlob> blobs,
        PodWriteOptions options)
    {
        ValidateInputs(comment, blobs, options.AllowDuplicateNames);
        PodFormat actual = ActualFormat(blobs, options.Format);
        return actual == PodFormat.Pod2
            ? BuildPod2(comment, blobs, options.AuditEntries)
            : BuildPod1(comment, blobs, options.RawCommentField, actual);
    }

    private static byte[] BuildPod1(string? comment, IReadOnlyList<PodBlob> blobs,
        byte[]? rawCommentField, PodFormat actual)
    {
        int nameSize = actual == PodFormat.Pod1Extended ? LongNameSize : EntryNameSize;
        foreach (PodBlob blob in blobs)
        {
            if (RequiredNameFieldLength(blob) > nameSize)
                throw new PodFormatException(
                    $"POD entry name/palette exceeds {nameSize - 1} usable bytes: {blob.Name}");
        }
        int headerSize = checked(sizeof(int) + CommentSize + (blobs.Count * (nameSize + 8)));
        int[] offsets = OffsetsFor(blobs, headerSize);
        using MemoryStream output = new();
        WriteInt32(output, blobs.Count);
        WriteCommentField(output, comment, rawCommentField);
        for (int i = 0; i < blobs.Count; i++)
        {
            WriteNameField(output, blobs[i], nameSize);
            WriteInt32(output, blobs[i].Data.Length);
            WriteInt32(output, offsets[i]);
        }
        foreach (PodBlob blob in blobs) Write(output, blob.Data);
        return output.ToArray();
    }

    private static byte[] BuildPod2(string? comment, IReadOnlyList<PodBlob> blobs,
        IReadOnlyList<PodAuditEntry> audits)
    {
        if (audits.Count > MaxItems)
            throw new PodFormatException("Too many POD2 audit records: " + audits.Count);
        using MemoryStream names = new();
        int[] nameOffsets = new int[blobs.Count];
        for (int i = 0; i < blobs.Count; i++)
        {
            nameOffsets[i] = checked((int)names.Length);
            Write(names, PodText.Latin1.GetBytes(blobs[i].Name));
            names.WriteByte(0);
        }
        int dataStart = checked(Pod2HeaderSize + (blobs.Count * Pod2EntrySize) + (int)names.Length);
        int[] offsets = OffsetsFor(blobs, dataStart);
        using MemoryStream output = new();
        Write(output, [(byte)'P', (byte)'O', (byte)'D', (byte)'2']);
        WriteInt32(output, 0);
        WriteFixedStrict(output, comment, CommentSize, "POD2 comment");
        WriteInt32(output, blobs.Count);
        WriteInt32(output, audits.Count);
        for (int i = 0; i < blobs.Count; i++)
        {
            WriteInt32(output, nameOffsets[i]);
            WriteInt32(output, blobs[i].Data.Length);
            WriteInt32(output, offsets[i]);
            WriteInt32(output, unchecked((int)blobs[i].Timestamp));
            WriteInt32(output, unchecked((int)Crc32Mpeg2(blobs[i].Data)));
        }
        Write(output, names.ToArray());
        foreach (PodBlob blob in blobs) Write(output, blob.Data);
        foreach (PodAuditEntry audit in audits) WriteAudit(output, audit);
        byte[] result = output.ToArray();
        WriteInt32At(result, 4, unchecked((int)Crc32Mpeg2(result, 8, result.Length - 8)));
        return result;
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
            cursor = checked(cursor + blobs[i].Data.Length);
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
        NameByteLength(blob.Name) + 1 +
        (string.IsNullOrWhiteSpace(blob.EmbeddedPaletteName)
            ? 0 : NameByteLength(blob.EmbeddedPaletteName) + 1);

    private static int NameByteLength(string? value) =>
        value is null ? 0 : PodText.Latin1.GetByteCount(value);

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

    /// <summary>
    /// The POD2 checksum: CRC-32/MPEG-2, i.e. polynomial 0x04C11DB7, initial
    /// 0xffffffff, MSB-first, and no final XOR. Check value for "123456789" is
    /// 0x0376E6E7. Not CRC-32/CCITT, despite what the shape of it suggests.
    /// </summary>
    public static uint Crc32Mpeg2(byte[] bytes) => Crc32Mpeg2(bytes, 0, bytes.Length);

    public static uint Crc32Mpeg2(byte[] bytes, int offset, int length)
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

    private static void Write(Stream target, byte[] bytes) => target.Write(bytes, 0, bytes.Length);
    private static void WriteInt32(Stream target, int value)
    {
        target.WriteByte((byte)value); target.WriteByte((byte)(value >> 8));
        target.WriteByte((byte)(value >> 16)); target.WriteByte((byte)(value >> 24));
    }
    private static void WriteInt32At(byte[] bytes, int at, int value)
    {
        bytes[at] = (byte)value; bytes[at + 1] = (byte)(value >> 8);
        bytes[at + 2] = (byte)(value >> 16); bytes[at + 3] = (byte)(value >> 24);
    }
}

public sealed record PodBlob
{
    public PodBlob(string name, byte[] data, byte[]? rawNameField = null,
        string? embeddedPaletteName = null, uint? timestamp = null)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("POD entry name is required.", nameof(name));
        Name = PodArchiveWriter.NormalizeName(name);
        Data = data ?? throw new ArgumentNullException(nameof(data));
        RawNameField = rawNameField;
        EmbeddedPaletteName = embeddedPaletteName ?? PodNameField.SecondString(rawNameField);
        Timestamp = timestamp ?? unchecked((uint)DateTimeOffset.UtcNow.ToUnixTimeSeconds());
    }
    public string Name { get; }
    public byte[] Data { get; }
    public byte[]? RawNameField { get; }
    public string? EmbeddedPaletteName { get; }
    public uint Timestamp { get; }
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
