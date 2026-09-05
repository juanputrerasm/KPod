namespace KPod.Core.Models;

public readonly record struct BinVector3(float X, float Y, float Z);

public sealed record BinBounds(
    int VertexCount, int BaseZ, int MinX, int MaxX, int MinY, int MaxY, int MinZ, int MaxZ);

public sealed record BinMaterial(
    int Id, uint Flags, float Reflectivity, float FresnelBias, float FresnelStrength,
    float BaseAlpha, float SpecPower, float Emissive, float TintR, float TintG,
    float TintB, int AlphaReference, int Translucency);

public sealed record BinMaterial2(uint Flags, float NormalStrength, IReadOnlyList<int> Reserved);

public sealed record BinMesh(
    string TextureName,
    bool Transparent,
    BinMaterial? Material,
    BinMaterial2? Material2,
    bool Solid,
    int Color,
    IReadOnlyList<float> Positions,
    IReadOnlyList<float> Normals,
    IReadOnlyList<float> TextureCoordinates);

public sealed record BinModel(
    string Name,
    string Format,
    int MagnifyPower,
    float BaseZ,
    BinBounds? RawVertexBounds,
    BinVector3 Anchor,
    int VertexCount,
    int PolygonCount,
    IReadOnlyList<string> TextureNames,
    IReadOnlyList<BinMesh> Meshes,
    IReadOnlyList<string> Warnings)
{
    /// <summary>
    /// The models this BIN names as its animation frames, upper-cased, in order.
    ///
    /// <para>An animated BIN holds no geometry of its own: it is a list of the other
    /// BINs that are its frames, and the game cycles them in place. Empty for every
    /// ordinary model.</para>
    /// </summary>
    public IReadOnlyList<string> FrameNames { get; init; } = [];
}
