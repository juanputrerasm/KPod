using System.Text;
using KPod.Core.Models;

namespace KPod.Tests.Models;

public class BinModelDecoderTests
{
    [Fact]
    public void ColorBlockDoesNotClearActiveTexture()
    {
        List<byte> data = StaticHeader();
        AddTexture(data, 13, "LOADER.RAW");
        AddMappedTriangle(data, 0x18, 0, 1, 2);
        AddInt(data, 0x0A); AddInt(data, 0x00332211);
        AddFlatTriangle(data, 0x19, 0, 1, 2);
        AddMappedTriangle(data, 0x18, 0, 1, 2);
        AddInt(data, 0);

        BinModel model = BinModelDecoder.Decode(data.ToArray(), "LOADER.BIN");

        Assert.Equal("BIN", model.Format);
        Assert.Equal(3, model.PolygonCount);
        Assert.Equal(["LOADER.RAW"], model.TextureNames);
        Assert.Equal(18, model.Meshes.Single(mesh => mesh.TextureName == "LOADER.RAW").Positions.Count);
        BinMesh solid = model.Meshes.Single(mesh => mesh.Solid);
        Assert.Equal(string.Empty, solid.TextureName);
        Assert.Equal(0x00332211, solid.Color);
    }

    [Fact]
    public void DecodesExtendedTextureMaterialAndNormalStrength()
    {
        List<byte> data = StaticHeader();
        AddTexture(data, 62, "A_VERY_LONG_TEXTURE_NAME.PNG", 64);
        AddInt(data, 63); AddInt(data, 0x248F);
        foreach (int value in new[] { 32768, 0, 65536, 32768, 16 * 65536, 16384, 65536, 32768, 65536 }) AddInt(data, value);
        AddInt(data, 0x8040);
        AddInt(data, 66); AddInt(data, 1); AddInt(data, 131072);
        for (int i = 0; i < 5; i++) AddInt(data, 0);
        AddMappedTriangle(data, 64, 1, 2, 3);
        AddInt(data, 0);

        BinModel model = BinModelDecoder.Decode(data.ToArray(), "HD.BIN");

        BinMesh mesh = Assert.Single(model.Meshes);
        Assert.Equal("A_VERY_LONG_TEXTURE_NAME.PNG", mesh.TextureName);
        Assert.NotNull(mesh.Material);
        Assert.Equal(0.5f, mesh.Material!.BaseAlpha);
        Assert.Equal(2f, mesh.Material2!.NormalStrength);
        Assert.Equal(9, mesh.Positions.Count);
    }

    [Fact]
    public void UnknownOpcodeTruncatesWithWarning()
    {
        List<byte> data = StaticHeader(); AddInt(data, 123456);
        BinModel model = BinModelDecoder.Decode(data.ToArray(), "BAD.BIN");
        Assert.Contains(model.Warnings, warning => warning.Contains("Unsupported BIN opcode"));
    }

    [Fact]
    public void AnAnimatedBinNamesItsFramesAndHasNoGeometryOfItsOwn()
    {
        List<byte> data = AnimatedHeader("rex1.bin", "rex2.bin", "rex3.bin", "rex4.bin");

        BinModel model = BinModelDecoder.Decode(data.ToArray(), "REX.BIN");

        Assert.Equal("ANIMATED_BIN", model.Format);
        Assert.Equal(["REX1.BIN", "REX2.BIN", "REX3.BIN", "REX4.BIN"], model.FrameNames);
        Assert.Equal(0, model.VertexCount);
        Assert.Empty(model.Meshes);
    }

    [Fact]
    public void ATruncatedFrameTableIsReadAsFarAsItGoes()
    {
        List<byte> data = AnimatedHeader("rex1.bin", "rex2.bin");
        // The count claims four frames but only two slots were written.
        data[8] = 4;

        BinModel model = BinModelDecoder.Decode(data.ToArray(), "REX.BIN");

        Assert.Equal(["REX1.BIN", "REX2.BIN"], model.FrameNames);
    }

    [Fact]
    public void AnOrdinaryModelHasNoFrames() =>
        Assert.Empty(BinModelDecoder.Decode(StaticHeader().ToArray(), "HAY.BIN").FrameNames);

    /// <summary>
    /// An animated BIN: the 0x20 signature, the frame count as the third DWORD, a
    /// zero vertex count where the shared 16-byte header ends, and then the frame
    /// names in 16-byte slots from byte 24.
    /// </summary>
    private static List<byte> AnimatedHeader(params string[] frames)
    {
        List<byte> data = [];
        AddInt(data, 0x20); AddInt(data, 0); AddInt(data, frames.Length); AddInt(data, 0);
        AddInt(data, 0);    // Vertex count, read by the shared payload parser.
        AddInt(data, 0);    // End-of-blocks token, so that parser stops before the names.
        foreach (string frame in frames)
        {
            byte[] encoded = Encoding.ASCII.GetBytes(frame);
            for (int i = 0; i < 16; i++) data.Add(i < encoded.Length ? encoded[i] : (byte)0);
        }
        return data;
    }

    private static List<byte> StaticHeader()
    {
        List<byte> data = [];
        AddInt(data, 0x14); AddInt(data, 65536); AddInt(data, 0); AddInt(data, 0); AddInt(data, 3);
        AddVertex(data, 0, 0, 0); AddVertex(data, 128, 0, 0); AddVertex(data, 0, 128, 0);
        return data;
    }

    private static void AddVertex(List<byte> data, int x, int y, int z)
    { AddInt(data, x * 2); AddInt(data, z * 2); AddInt(data, y * 2); }

    private static void AddTexture(List<byte> data, int opcode, string name, int nameBytes = 16)
    {
        AddInt(data, opcode); AddInt(data, 0);
        byte[] encoded = Encoding.ASCII.GetBytes(name);
        for (int i = 0; i < nameBytes; i++) data.Add(i < encoded.Length ? encoded[i] : (byte)0);
    }

    private static void AddMappedTriangle(List<byte> data, int opcode, params int[] indices)
    {
        AddInt(data, opcode); AddInt(data, indices.Length);
        for (int i = 0; i < 4; i++) AddInt(data, 0);
        foreach (int index in indices) { AddInt(data, index); AddInt(data, 0); AddInt(data, 0); }
    }

    private static void AddFlatTriangle(List<byte> data, int opcode, params int[] indices)
    {
        AddInt(data, opcode); AddInt(data, indices.Length);
        for (int i = 0; i < 4; i++) AddInt(data, 0);
        foreach (int index in indices) AddInt(data, index);
    }

    private static void AddInt(List<byte> data, int value) => data.AddRange(BitConverter.GetBytes(value));
}
