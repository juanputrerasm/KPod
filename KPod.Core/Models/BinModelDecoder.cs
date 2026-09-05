using System.Globalization;
using System.Text;

namespace KPod.Core.Models;

/// <summary>Bounds-checked decoder for classic, animated, and Extended BIN command streams.</summary>
public static class BinModelDecoder
{
    private const int SignatureLwo = 0x4D524F46;
    private const int SignatureAnimated = 0x20;
    private const int Magnify = 0x14;
    private const int Texture64 = 62;
    private const int Material = 63;
    private const int MaterialFacet = 64;
    private const int Keyframe64 = 65;
    private const int Material2 = 66;
    private const int MaxCorners = 256;
    private const float UvScale = 0xFF0000;

    public static BinModel Decode(byte[] data, string name)
    {
        MutableModel model = new(name);
        if (data.Length < 4) return model.Finish();

        Reader reader = new(data);
        try
        {
            int first = reader.ReadInt32();
            if (first == SignatureLwo)
            {
                model.Format = "LWO";
                return model.Finish();
            }

            if (first == Magnify)
            {
                model.Format = "BIN";
                if (reader.Remaining < 4) return model.Finish();
                int power = reader.ReadInt32();
                if (power > 0) model.MagnifyPower = power;
                DecodePayload(reader, model, 8, true);
            }
            else if (first == SignatureAnimated)
            {
                model.Format = "ANIMATED_BIN";
                model.FrameNames.AddRange(ReadFrameNames(data));
                DecodePayload(reader, model, 12, false);
            }
            else
            {
                model.Format = "0x" + unchecked((uint)first).ToString("X8", CultureInfo.InvariantCulture);
            }
        }
        catch (ArgumentException ex)
        {
            model.Warnings.Add(ex.Message);
        }

        return model.Finish();
    }

    /// <summary>
    /// The frame model names an animated BIN carries.
    ///
    /// <para>Layout, confirmed against <c>MODELS\REX.BIN</c> in the stock CRAZY98 pod:
    /// the header is <c>0x20</c>, a zero, the frame count, then the usual 65536
    /// magnify constant; a zero vertex count and a zero end-of-blocks token follow, so
    /// an animated BIN still parses as an ordinary model that happens to have no
    /// geometry. The names begin at byte 24 as NUL-padded ASCII in 16-byte slots.</para>
    /// </summary>
    private static string[] ReadFrameNames(byte[] data)
    {
        const int CountOffset = 8;
        const int NamesOffset = 24;
        const int SlotBytes = 16;
        if (data.Length < NamesOffset + SlotBytes) return [];

        int count = BitConverter.ToInt32(data, CountOffset);
        if (count <= 0) return [];

        // A truncated file is read as far as it goes rather than rejected outright:
        // the frames that are there still name real models.
        int available = (data.Length - NamesOffset) / SlotBytes;
        count = Math.Min(count, available);

        List<string> names = new(count);
        for (int i = 0; i < count; i++)
        {
            int start = NamesOffset + (i * SlotBytes);
            int length = 0;
            while (length < SlotBytes - 1 && data[start + length] != 0) length++;
            string name = Encoding.ASCII.GetString(data, start, length).Trim();
            if (name.Length > 0) names.Add(name.ToUpperInvariant());
        }

        return [.. names];
    }

    private static void DecodePayload(Reader reader, MutableModel model, int headerBytes, bool applyMagnify)
    {
        if (!reader.CanRead(headerBytes + 4))
        {
            model.Warnings.Add("Truncated BIN header");
            return;
        }

        reader.Skip(headerBytes);
        int vertexCount = reader.ReadInt32();
        if (vertexCount < 0 || vertexCount > 200000 || !reader.CanRead((long)vertexCount * 12))
        {
            model.Warnings.Add("Invalid or truncated BIN vertex list");
            return;
        }

        List<(int X, int Y, int Z)> raw = new(vertexCount);
        int minX = int.MaxValue, maxX = int.MinValue;
        int minY = int.MaxValue, maxY = int.MinValue;
        int minZ = int.MaxValue, maxZ = int.MinValue;
        int baseZ = 0;
        for (int i = 0; i < vertexCount; i++)
        {
            int x = reader.ReadInt32() >> 1;
            int z = reader.ReadInt32() >> 1;
            int y = reader.ReadInt32() >> 1;
            raw.Add((x, y, z));
            minX = Math.Min(minX, x); maxX = Math.Max(maxX, x);
            minY = Math.Min(minY, y); maxY = Math.Max(maxY, y);
            minZ = Math.Min(minZ, z); maxZ = Math.Max(maxZ, z);
            baseZ = Math.Min(baseZ, z);
        }

        int adjustedBaseZ = baseZ - 31;
        model.RawBounds = new BinBounds(vertexCount, adjustedBaseZ, minX, maxX, minY, maxY, minZ, maxZ);
        float scale = applyMagnify ? 1024f / model.MagnifyPower : 1f / 64f;
        foreach ((int x, int y, int z) in raw)
        {
            model.Vertices.Add(new BinVector3(x * scale, y * scale, z * scale));
        }
        model.BaseZ = adjustedBaseZ * scale;

        string texture = string.Empty;
        int solidColor = 0;
        BinMaterial? material = null;
        BinMaterial2? material2 = null;
        int materialId = 0;
        bool stop = false;
        while (!stop && reader.Remaining >= 4)
        {
            int tokenOffset = reader.Position;
            int token = reader.ReadInt32();
            switch (token)
            {
                case 0: stop = true; break;
                case 2:
                    if (!reader.CanRead(8)) { stop = true; break; }
                    reader.Skip(4);
                    int stripVertices = reader.ReadInt32();
                    long stripBytes = (long)stripVertices * 12 + 80;
                    if (stripVertices >= 0 && stripVertices <= MaxCorners && reader.CanRead(stripBytes)) reader.Skip((int)stripBytes);
                    else if (reader.CanRead(136)) reader.Skip(136);
                    else stop = true;
                    break;
                case 3:
                    if (!reader.CanRead(8L + ((long)vertexCount * 12))) { stop = true; break; }
                    reader.Skip(8 + (vertexCount * 12));
                    break;
                case 4:
                    if (!reader.CanRead(8)) { stop = true; break; }
                    reader.Skip(4);
                    int count4 = reader.ReadInt32();
                    if (count4 < 0 || count4 > 4096 || !reader.CanRead((long)count4 * 8)) { stop = true; break; }
                    reader.Skip(count4 * 8);
                    break;
                case 0x0D:
                    if (!reader.CanRead(20)) { stop = true; break; }
                    reader.Skip(4); texture = reader.ReadAscii(16).Trim().ToUpperInvariant();
                    break;
                case Texture64:
                    if (!reader.CanRead(68)) { model.Warnings.Add("Truncated MRGL_TEXTURE64 record"); stop = true; break; }
                    reader.Skip(4); texture = reader.ReadAscii(64).Trim().ToUpperInvariant();
                    break;
                case Material:
                    if (!reader.CanRead(44)) { model.Warnings.Add("Truncated MRGL_MATERIAL record"); stop = true; break; }
                    material = ReadMaterial(reader, ++materialId);
                    break;
                case Material2:
                    if (!reader.CanRead(28)) { model.Warnings.Add("Truncated MRGL_MATERIAL2 record"); stop = true; break; }
                    material2 = ReadMaterial2(reader);
                    if (material2.Reserved.Any(value => value != 0)) model.Warnings.Add("MRGL_MATERIAL2 has non-zero reserved fields");
                    break;
                case Keyframe64:
                    if (!reader.CanRead(4372)) { model.Warnings.Add("Truncated MRGL_KEYFRAME64 record"); stop = true; break; }
                    reader.Skip(4372);
                    break;
                case 0x1D:
                    if (!reader.CanRead(24)) { stop = true; break; }
                    reader.Skip(4); int frames = reader.ReadInt32(); reader.Skip(16);
                    if (frames < 0 || frames > 1024) { stop = true; break; }
                    if (reader.CanRead((long)frames * 32))
                    {
                        for (int i = 0; i < frames; i++)
                        {
                            string frame = reader.ReadAscii(32).Trim().ToUpperInvariant();
                            if (i == 0) texture = frame;
                        }
                    }
                    else if (reader.CanRead((long)frames * 8)) reader.Skip(frames * 8);
                    else stop = true;
                    break;
                case 0x0A:
                    if (!reader.CanRead(4)) { stop = true; break; }
                    solidColor = reader.ReadInt32() & 0xFFFFFF;
                    break;
                case 0x0C: if (reader.CanRead(24)) reader.Skip(24); else stop = true; break;
                case 0x12: if (reader.CanRead(4)) reader.Skip(4); else stop = true; break;
                case Magnify: if (reader.CanRead(4)) model.MagnifyPower = reader.ReadInt32(); else stop = true; break;
                case 0x16: if (reader.CanRead(12)) reader.Skip(12); else stop = true; break;
                case 0x17: if (reader.CanRead(8)) reader.Skip(8); else stop = true; break;
                case 0x1F:
                    if (!reader.CanRead(8)) { stop = true; break; }
                    reader.Skip(4); int count1F = reader.ReadInt32();
                    if (count1F < 0 || count1F > 200000 || !reader.CanRead((long)count1F * 4)) stop = true;
                    else reader.Skip(count1F * 4);
                    break;
                case 0x11: case 0x18: case 0x22: case 0x29: case 0x33: case 0x34: case 0x0E:
                    Polygon? mapped = ReadFace(reader, token, texture, vertexCount, true, null, null, solidColor);
                    if (mapped is null) stop = true; else model.Polygons.Add(mapped);
                    break;
                case 5: case 0x19: case 6: case 0x0F:
                    string faceTexture = token == 0x19 ? string.Empty : texture;
                    Polygon? flat = ReadFace(reader, token, faceTexture, vertexCount, false, null, null, solidColor);
                    if (flat is null) stop = true; else model.Polygons.Add(flat);
                    break;
                case MaterialFacet:
                    Polygon? mat = ReadFace(reader, token, texture, vertexCount, true, material, material2, solidColor);
                    if (mat is null) { model.Warnings.Add("Invalid MRGL_MATFACET at byte " + reader.Position); stop = true; }
                    else model.Polygons.Add(mat);
                    break;
                default:
                    model.Warnings.Add("Unsupported BIN opcode " + token + " (0x" + unchecked((uint)token).ToString("X", CultureInfo.InvariantCulture) + ") at byte " + tokenOffset + "; model truncated");
                    stop = true;
                    break;
            }
        }
    }

    private static Polygon? ReadFace(Reader reader, int type, string texture, int vertexCount, bool mapped,
        BinMaterial? material, BinMaterial2? material2, int solidColor)
    {
        if (vertexCount < 1 || !reader.CanRead(4)) return null;
        int count = reader.ReadInt32();
        long bytes = 16L + ((long)count * (mapped ? 12 : 4));
        if (count < 3 || count > MaxCorners || !reader.CanRead(bytes)) return null;
        reader.Skip(16);
        int[] indices = new int[count];
        int[] u = new int[count];
        int[] v = new int[count];
        for (int i = 0; i < count; i++)
        {
            indices[i] = reader.ReadInt32();
            if (mapped) { u[i] = reader.ReadInt32(); v[i] = reader.ReadInt32(); }
        }

        if (indices.Any(index => index < 0 || index >= vertexCount))
        {
            if (indices.Any(index => index - 1 < 0 || index - 1 >= vertexCount)) return null;
            for (int i = 0; i < indices.Length; i++) indices[i]--;
        }
        return new Polygon(type, texture, indices, u, v, material, material2, solidColor);
    }

    private static BinMaterial ReadMaterial(Reader reader, int id)
    {
        uint flags = unchecked((uint)reader.ReadInt32());
        float reflectivity = Fixed(reader.ReadInt32());
        float fresnelBias = Fixed(reader.ReadInt32());
        float fresnelStrength = Fixed(reader.ReadInt32());
        float baseAlpha = Fixed(reader.ReadInt32());
        float specPower = Fixed(reader.ReadInt32());
        float emissive = Fixed(reader.ReadInt32());
        float r = Fixed(reader.ReadInt32()), g = Fixed(reader.ReadInt32()), b = Fixed(reader.ReadInt32());
        uint foliage = unchecked((uint)reader.ReadInt32());
        return new BinMaterial(id, flags, reflectivity, fresnelBias, fresnelStrength, baseAlpha,
            specPower, emissive, r, g, b, (int)(foliage & 0xFFFF), (int)(foliage >> 16));
    }

    private static BinMaterial2 ReadMaterial2(Reader reader)
    {
        uint flags = unchecked((uint)reader.ReadInt32());
        float strength = Fixed(reader.ReadInt32());
        int[] reserved = new int[5];
        for (int i = 0; i < reserved.Length; i++) reserved[i] = reader.ReadInt32();
        return new BinMaterial2(flags, (flags & 1) != 0 ? strength : 1, reserved);
    }

    private static float Fixed(int value) => value / 65536f;

    private sealed record Polygon(int Type, string Texture, int[] Indices, int[] U, int[] V,
        BinMaterial? Material, BinMaterial2? Material2, int SolidColor);

    private sealed class MutableModel
    {
        internal MutableModel(string name) => Name = name;
        internal string Name { get; }
        internal string Format { get; set; } = "UNKNOWN";
        internal int MagnifyPower { get; set; } = 65536;
        internal float BaseZ { get; set; }
        internal BinBounds? RawBounds { get; set; }
        internal List<BinVector3> Vertices { get; } = [];
        internal List<Polygon> Polygons { get; } = [];
        internal List<string> Warnings { get; } = [];
        internal List<string> FrameNames { get; } = [];

        internal BinModel Finish()
        {
            float minX = float.MaxValue, maxX = float.MinValue;
            float minY = float.MaxValue, maxY = float.MinValue, minZ = float.MaxValue;
            foreach (BinVector3 vertex in Vertices)
            {
                minX = Math.Min(minX, vertex.X); maxX = Math.Max(maxX, vertex.X);
                minY = Math.Min(minY, vertex.Y); maxY = Math.Max(maxY, vertex.Y);
                minZ = Math.Min(minZ, vertex.Z);
            }
            BinVector3 anchor = Vertices.Count == 0 ? default : new((minX + maxX) / 2, (minY + maxY) / 2, minZ);
            Dictionary<string, MeshBuilder> groups = new(StringComparer.OrdinalIgnoreCase);
            HashSet<string> textureNames = new(StringComparer.OrdinalIgnoreCase);
            foreach (Polygon polygon in Polygons)
            {
                if (polygon.Texture.Length > 0) textureNames.Add(polygon.Texture);
                uint flags = polygon.Material?.Flags ?? 0;
                bool transparent = polygon.Material is not null
                    ? (flags & (0x0004u | 0x0008u | 0x2000u)) != 0
                    : polygon.Type is 0x11 or 0x33;
                bool solid = polygon.Type == 0x19;
                string materialKey = polygon.Material is null ? "legacy" : "material:" + polygon.Material.Id + ":" + (polygon.Material2?.NormalStrength ?? 1);
                string key = polygon.Texture + "|" + transparent + "|" + (solid ? "solid:" + polygon.SolidColor : "textured") + "|" + materialKey;
                if (!groups.TryGetValue(key, out MeshBuilder? group))
                {
                    group = new MeshBuilder(polygon.Texture, transparent, polygon.Material, polygon.Material2, solid, polygon.SolidColor);
                    groups.Add(key, group);
                }
                Triangulate(polygon, group, anchor);
            }
            return new BinModel(Name, Format, MagnifyPower, BaseZ, RawBounds, anchor, Vertices.Count,
                Polygons.Count, textureNames.ToArray(), groups.Values.Select(group => group.Build()).ToArray(), Warnings.ToArray())
            {
                FrameNames = FrameNames.ToArray(),
            };
        }

        private void Triangulate(Polygon polygon, MeshBuilder mesh, BinVector3 anchor)
        {
            for (int i = 1; i < polygon.Indices.Length - 1; i++)
            {
                int[] corners = [0, i, i + 1];
                BinVector3[] points = corners.Select(c => Vertices[polygon.Indices[c]]).ToArray();
                BinVector3 normal = Normal(points[0], points[1], points[2]);
                for (int j = 0; j < 3; j++)
                {
                    int corner = corners[j]; BinVector3 point = points[j];
                    mesh.Positions.Add(point.X - anchor.X); mesh.Positions.Add(point.Y - anchor.Y); mesh.Positions.Add(point.Z - anchor.Z);
                    mesh.Normals.Add(normal.X); mesh.Normals.Add(normal.Y); mesh.Normals.Add(normal.Z);
                    mesh.Uvs.Add(polygon.U[corner] / UvScale); mesh.Uvs.Add(1 - (polygon.V[corner] / UvScale));
                }
            }
        }

        private static BinVector3 Normal(BinVector3 a, BinVector3 b, BinVector3 c)
        {
            float abx = b.X - a.X, aby = b.Y - a.Y, abz = b.Z - a.Z;
            float acx = c.X - a.X, acy = c.Y - a.Y, acz = c.Z - a.Z;
            float x = (aby * acz) - (abz * acy), y = (abz * acx) - (abx * acz), z = (abx * acy) - (aby * acx);
            float length = (float)Math.Sqrt((x * x) + (y * y) + (z * z));
            return length == 0 ? new BinVector3(0, 1, 0) : new BinVector3(x / length, y / length, z / length);
        }
    }

    private sealed class MeshBuilder
    {
        internal MeshBuilder(string texture, bool transparent, BinMaterial? material, BinMaterial2? material2, bool solid, int color)
        { Texture = texture; Transparent = transparent; Material = material; Material2 = material2; Solid = solid; Color = color; }
        internal string Texture { get; }
        internal bool Transparent { get; }
        internal BinMaterial? Material { get; }
        internal BinMaterial2? Material2 { get; }
        internal bool Solid { get; }
        internal int Color { get; }
        internal List<float> Positions { get; } = [];
        internal List<float> Normals { get; } = [];
        internal List<float> Uvs { get; } = [];
        internal BinMesh Build() => new(Texture, Transparent, Material, Material2, Solid, Color,
            Positions.ToArray(), Normals.ToArray(), Uvs.ToArray());
    }

    private sealed class Reader
    {
        private readonly byte[] _data;
        internal Reader(byte[] data) => _data = data;
        internal int Position { get; private set; }
        internal int Remaining => _data.Length - Position;
        internal bool CanRead(long length) => length >= 0 && Position + length <= _data.Length;
        internal void Skip(int length) { Require(length); Position += length; }
        internal int ReadInt32()
        {
            Require(4);
            int value = _data[Position] | (_data[Position + 1] << 8) | (_data[Position + 2] << 16) | (_data[Position + 3] << 24);
            Position += 4; return value;
        }
        internal string ReadAscii(int length)
        {
            Require(length); int end = Position;
            while (end < Position + length && _data[end] != 0) end++;
            string value = Encoding.GetEncoding(28591).GetString(_data, Position, end - Position);
            Position += length; return value;
        }
        private void Require(int length)
        {
            if (!CanRead(length)) throw new ArgumentException("Unexpected end of BIN payload at byte " + Position);
        }
    }
}
