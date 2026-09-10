using KPod.Core.Compat;

namespace KPod.Tests.Pods;

/// <summary>A file to place inside a synthesized POD archive.</summary>
public sealed record PodFile(string Name, byte[] Bytes)
{
    public static PodFile Text(string name, string contents) => new(name, PodText.Latin1.GetBytes(contents));
}

/// <summary>
/// Builds POD1, POD1-64, POD2 and EPD archives byte-for-byte so the reader can be
/// tested without shipping binary fixtures.
/// </summary>
public static class PodFixture
{
    public const int ClassicNameSize = 32;
    public const int ExtendedNameSize = 64;

    /// <summary>Classic POD1: 32-byte name field, 40-byte records.</summary>
    public static byte[] BuildPod1(params PodFile[] files) =>
        BuildPod1(ClassicNameSize, "hand written", files);

    /// <summary>POD1-64: 64-byte name field, 72-byte records.</summary>
    public static byte[] BuildPod164(params PodFile[] files) =>
        BuildPod1(ExtendedNameSize, "hand written", files);

    /// <summary>
    /// A POD1 archive with an explicit directory-record width, so a test can build
    /// either layout, or a deliberately mismatched one.
    /// </summary>
    public static byte[] BuildPod1(int nameSize, string comment, params PodFile[] files)
    {
        const int headerSize = 84;
        int entrySize = nameSize + 8;
        int dataOffset = headerSize + (files.Length * entrySize);

        MemoryStream output = new();
        WriteInt32(output, files.Length);
        Write(output, FixedField(comment, 80));

        int offset = dataOffset;
        foreach (PodFile file in files)
        {
            Write(output, FixedField(file.Name, nameSize));
            WriteInt32(output, file.Bytes.Length);
            WriteInt32(output, offset);
            offset += file.Bytes.Length;
        }

        foreach (PodFile file in files)
        {
            Write(output, file.Bytes);
        }

        return output.ToArray();
    }

    public static byte[] BuildPod2(params PodFile[] files) => BuildPod2(0, files);

    /// <summary>
    /// Builds a POD2 archive carrying <paramref name="auditCount"/> audit records after the
    /// payloads, which is where a real archive keeps them. Shipping archives accumulate one
    /// record per edit, so the trail routinely outnumbers the directory.
    /// </summary>
    public static byte[] BuildPod2(int auditCount, params PodFile[] files)
    {
        const int headerSize = 96;
        const int entrySize = 20;
        int tableOffset = headerSize;
        int nameTableOffset = tableOffset + (files.Length * entrySize);

        // Lay the name blob out first so each entry can point at its own name.
        MemoryStream nameTable = new();
        List<int> nameOffsets = [];
        foreach (PodFile file in files)
        {
            nameOffsets.Add((int)nameTable.Length);
            Write(nameTable, PodText.Latin1.GetBytes(file.Name));
            nameTable.WriteByte(0);
        }

        byte[] names = nameTable.ToArray();
        int dataOffset = nameTableOffset + names.Length;

        MemoryStream output = new();
        Write(output, PodText.Latin1.GetBytes("POD2"));
        WriteInt32(output, 0);                            // checksum, ignored by the reader
        Write(output, FixedField("fixture", 80));         // comment
        WriteInt32(output, files.Length);
        WriteInt32(output, auditCount);

        int offset = dataOffset;
        for (int i = 0; i < files.Length; i++)
        {
            WriteInt32(output, nameOffsets[i]);
            WriteInt32(output, files[i].Bytes.Length);
            WriteInt32(output, offset);
            WriteInt32(output, 0);                        // timestamp, not read
            WriteInt32(output, 0);                        // checksum, not read
            offset += files[i].Bytes.Length;
        }

        Write(output, names);
        foreach (PodFile file in files)
        {
            Write(output, file.Bytes);
        }

        // The trail sits after the payloads. Each record is 312 bytes: a 32-byte user, a
        // timestamp, an action (0-2), a 256-byte path and four trailing words.
        for (int i = 0; i < auditCount; i++)
        {
            Write(output, FixedField("fixture", 32));
            WriteInt32(output, 0);                        // timestamp
            WriteInt32(output, i % 3);                    // action
            Write(output, FixedField("ART\\TEST.RAW", 256));
            WriteInt32(output, 0);
            WriteInt32(output, 0);
            WriteInt32(output, 0);
            WriteInt32(output, 0);
        }

        return output.ToArray();
    }

    /// <summary>
    /// EPD stores each name as a 4-byte prefix plus a 60-byte suffix. Pass a name
    /// already split as PREFIX plus a backslash-led rest to exercise the join rule.
    /// </summary>
    public static byte[] BuildEpd(params (string Prefix, string Suffix, byte[] Bytes)[] files)
    {
        const int tableOffset = 0x110;
        const int entrySize = 80;
        int dataOffset = tableOffset + (files.Length * entrySize);

        MemoryStream output = new();
        Write(output, PodText.Latin1.GetBytes("dtxe"));
        Write(output, FixedField("EPD", 4));               // 4-byte title
        Write(output, new byte[0x90 - 8]);                 // pad to the count field
        WriteInt32(output, files.Length);
        Write(output, new byte[tableOffset - 0x94]);       // pad to the entry table

        int offset = dataOffset;
        foreach ((string prefix, string suffix, byte[] bytes) in files)
        {
            Write(output, FixedField(prefix, 4));
            Write(output, FixedField(suffix, 60));
            WriteInt32(output, bytes.Length);
            WriteInt32(output, offset);
            Write(output, new byte[8]);                    // trailing unread bytes
            offset += bytes.Length;
        }

        foreach ((_, _, byte[] bytes) in files)
        {
            Write(output, bytes);
        }

        return output.ToArray();
    }

    /// <summary>Overwrites a little-endian int32 in place, for corrupting a fixture.</summary>
    public static void PatchInt32(byte[] bytes, int offset, int value)
    {
        bytes[offset] = (byte)value;
        bytes[offset + 1] = (byte)(value >> 8);
        bytes[offset + 2] = (byte)(value >> 16);
        bytes[offset + 3] = (byte)(value >> 24);
    }

    /// <summary>A payload whose bytes are 1, 2, 3 and so on, so slices are checkable.</summary>
    public static byte[] Payload(int size)
    {
        byte[] data = new byte[size];
        for (int i = 0; i < size; i++)
        {
            data[i] = (byte)(i + 1);
        }

        return data;
    }

    /// <summary>Latin-1 text in a fixed-width NUL-padded field, truncated if too long.</summary>
    private static byte[] FixedField(string text, int size)
    {
        byte[] field = new byte[size];
        byte[] encoded = PodText.Latin1.GetBytes(text);
        Array.Copy(encoded, field, Math.Min(encoded.Length, size));
        return field;
    }

    /// <summary>Stream.Write(byte[]) alone is not available on every target.</summary>
    private static void Write(Stream stream, byte[] bytes) => stream.Write(bytes, 0, bytes.Length);

    private static void WriteInt32(Stream stream, int value)
    {
        stream.WriteByte((byte)value);
        stream.WriteByte((byte)(value >> 8));
        stream.WriteByte((byte)(value >> 16));
        stream.WriteByte((byte)(value >> 24));
    }
}
