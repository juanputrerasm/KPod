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
    IReadOnlyList<float> TextureCoordinates)
{
    /// <summary>
    /// Whether both faces of this mesh are drawn.
    ///
    /// <para>A .BIN is wound so that back-face culling is what the renderer wants, and a
    /// material's TWOSIDED flag is what turns that off. A 4x4 Evolution .SMF states nothing:
    /// its foliage, fences and banners are single-sided sheets meant to be seen from behind,
    /// so the .SMF decoder sets this and culling would otherwise hide half of them.</para>
    /// </summary>
    public bool DoubleSided { get; init; }

    /// <summary>
    /// The reduced-detail group name this mesh came from, when the source format names its
    /// groups. Empty for .BIN, which has no group names.
    /// </summary>
    public string GroupName { get; init; } = string.Empty;
}

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

    /// <summary>
    /// Which axis this model treats as up, as a single upper-case letter.
    ///
    /// <para>.BIN is authored Z-up and the renderer swaps its axes into view space. A 4x4
    /// Evolution .SMF is authored Y-up and must not be swapped: a pine tree is tall in Y, a
    /// fallen log is long in Z, and the Evo 2 .SIT `size` field equals the model's XYZ extent
    /// for all 57 distinct stock models, which is what settles the order. The model says
    /// which so the renderer never has to infer it.</para>
    /// </summary>
    public string UpAxis { get; init; } = "Z";

    /// <summary>
    /// Where this model's texture V origin sits, "bottom-left" or "top-left".
    ///
    /// <para>Evo's V runs top-down, so .SMF art is uploaded without the vertical flip .BIN
    /// art needs. Flipping it - which Blender importers do, because Blender's V is bottom-up
    /// - renders every texture upside down.</para>
    /// </summary>
    public string UvOrigin { get; init; } = "bottom-left";
}
