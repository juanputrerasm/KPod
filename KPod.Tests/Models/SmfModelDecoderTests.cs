using KPod.Core.Compat;
using KPod.Core.Models;

namespace KPod.Tests.Models;

public class SmfModelDecoderTests
{
    [Fact]
    public void DecodesAVersionFourModelIntoTriangleSoup()
    {
        BinModel model = SmfModelDecoder.Decode(Build(4, Quad("OPAQUE", "WALL.RAW")), "WALL.SMF");

        Assert.Equal("SMF v4", model.Format);
        Assert.Equal(["WALL.RAW"], model.TextureNames);
        BinMesh mesh = Assert.Single(model.Meshes);
        // Two triangles, three corners each, expanded rather than indexed.
        Assert.Equal(18, mesh.Positions.Count);
        Assert.Equal(18, mesh.Normals.Count);
        Assert.Equal(12, mesh.TextureCoordinates.Count);
        Assert.Equal(2, model.PolygonCount);
        Assert.Equal(4, model.VertexCount);
        Assert.Empty(model.Warnings);
    }

    [Fact]
    public void GroupsAreDrawnFromBothSides()
    {
        BinModel model = SmfModelDecoder.Decode(Build(4, Quad("OPAQUE", "WALL.RAW")), "WALL.SMF");

        Assert.True(Assert.Single(model.Meshes).DoubleSided);
    }

    /// <summary>
    /// Evo is Y-up and .BIN is Z-up, so Y and Z are swapped into the .BIN convention and the
    /// normals are negated with them, matching JSTruckViewer's reference parser.
    /// </summary>
    [Fact]
    public void SwapsEvoAxesIntoTheBinConvention()
    {
        BinModel model = SmfModelDecoder.Decode(Build(4, Quad("OPAQUE", "WALL.RAW")), "WALL.SMF");
        BinMesh mesh = Assert.Single(model.Meshes);

        // Vertex 0 is Evo (0, 10, 0) with normal (0, 0, 1); the first face corner is it.
        Assert.Equal(0f, mesh.Positions[0]);
        Assert.Equal(0f, mesh.Positions[1]);     // Evo Z
        Assert.Equal(10f, mesh.Positions[2]);    // Evo Y
        Assert.Equal(0f, mesh.Normals[0]);
        Assert.Equal(-1f, mesh.Normals[1]);      // -(Evo nz)
        Assert.Equal(0f, mesh.Normals[2]);       // -(Evo ny)
    }

    /// <summary>
    /// Evo's V runs top-down and the renderer uploads every texture flipped, so V is
    /// inverted here and the two cancel.
    /// </summary>
    [Fact]
    public void InvertsVToCancelTheRenderersTextureFlip()
    {
        BinModel model = SmfModelDecoder.Decode(Build(4, Quad("OPAQUE", "WALL.RAW")), "WALL.SMF");
        BinMesh mesh = Assert.Single(model.Meshes);

        Assert.Equal(0f, mesh.TextureCoordinates[0]);
        Assert.Equal(0.75f, mesh.TextureCoordinates[1]);   // 1 - 0.25
    }

    /// <summary>
    /// The invariant that actually matters, and the one whose absence mirrored every model:
    /// composed with the renderer's own (x, z, -y), Evo's Z comes out negated and nothing
    /// else moves. A model that is not Z-negated is its own mirror image, which reads as the
    /// texture being mirrored because the mesh still carries its own UVs.
    /// </summary>
    [Fact]
    public void ComposesWithTheRendererToNegateEvoZ()
    {
        BinModel model = SmfModelDecoder.Decode(Build(4, Quad("OPAQUE", "WALL.RAW")), "WALL.SMF");
        BinMesh mesh = Assert.Single(model.Meshes);

        // BinOpenGlRenderer.UploadMesh maps (x, y, z) to (x, z, -y) for every model alike.
        static (float X, float Y, float Z) ToView(IReadOnlyList<float> p, int i) =>
            (p[i], p[i + 2], -p[i + 1]);

        // Evo vertices of the fixture quad, in file order.
        (float X, float Y, float Z)[] evo = [(0, 10, 0), (1, 10, 0), (1, 0, 0), (0, 0, 0)];
        int[] cornerToVertex = [0, 1, 2, 0, 2, 3];

        for (int corner = 0; corner < cornerToVertex.Length; corner++)
        {
            (float X, float Y, float Z) source = evo[cornerToVertex[corner]];
            (float X, float Y, float Z) view = ToView(mesh.Positions, corner * 3);
            Assert.Equal(source.X, view.X);
            Assert.Equal(source.Y, view.Y);
            Assert.Equal(-source.Z, view.Z);
        }
    }

    /// <summary>A v2 and a v3 model carry no LOD header line; reading one would desynchronise.</summary>
    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    public void OlderVersionsHaveNoLodHeader(int version)
    {
        BinModel model = SmfModelDecoder.Decode(Build(version, Quad("transp", "GLASS.RAW")), "GLASS.SMF");

        Assert.Equal("SMF v" + version, model.Format);
        Assert.Equal(["GLASS.RAW"], model.TextureNames);
        Assert.Equal(18, Assert.Single(model.Meshes).Positions.Count);
    }

    /// <summary>A v1 material inserts a marker line before it and a bump name after it.</summary>
    [Fact]
    public void ReadsTheEvoTwoBumpMaterialForm()
    {
        string group = string.Join("\n",
            "OPAQUE", "1", "1", "4,1,2,0",
            "v1",
            "1,1,32,0,0,PALM.TIF",
            "PALM_BUMP.TIF",
            Vertices(), Faces());

        BinModel model = SmfModelDecoder.Decode(Build(4, group), "PALM.SMF");

        Assert.Equal(["PALM.TIF"], model.TextureNames);
        Assert.Equal(18, Assert.Single(model.Meshes).Positions.Count);
        Assert.Empty(model.Warnings);
    }

    /// <summary>
    /// Only frame 0 is drawn, but every frame's lines must still be walked or the face
    /// block that follows is read as vertices.
    /// </summary>
    [Fact]
    public void SkipsExtraAnimationFramesWithoutLosingTheFaceBlock()
    {
        string group = string.Join("\n",
            "OPAQUE", "1", "1", "4,3,2,0",
            "1,1,32,0,0,FLAG.RAW",
            Vertices(), Vertices(), Vertices(), Faces());

        BinModel model = SmfModelDecoder.Decode(Build(4, group), "FLAG.SMF");

        BinMesh mesh = Assert.Single(model.Meshes);
        Assert.Equal(18, mesh.Positions.Count);
        Assert.Empty(model.Warnings);
        // Frame 0's data, not a later frame's. Evo Y lands in Z after the axis swap.
        Assert.Equal(10f, mesh.Positions[2]);
    }

    [Fact]
    public void AFaceIndexOutsideTheVertexBlockIsDroppedAndReported()
    {
        string group = string.Join("\n",
            "OPAQUE", "1", "1", "4,1,2,0",
            "1,1,32,0,0,WALL.RAW",
            Vertices(),
            "0,1,2", "0,2,99");

        BinModel model = SmfModelDecoder.Decode(Build(4, group), "BROKEN.SMF");

        Assert.Equal(9, Assert.Single(model.Meshes).Positions.Count);
        Assert.Contains(model.Warnings, warning => warning.Contains("indexes outside"));
    }

    /// <summary>The transparency flag is material field 3.</summary>
    [Fact]
    public void ReadsTheMaterialTransparencyFlag()
    {
        string group = string.Join("\n",
            "TRANSP", "1", "1", "4,1,2,0",
            "1,1,32,1,0,GLASS.RAW",
            Vertices(), Faces());

        Assert.True(Assert.Single(SmfModelDecoder.Decode(Build(4, group), "G.SMF").Meshes).Transparent);
    }

    /// <summary>
    /// A reduced-detail group is dropped when its full-detail partner is present: the pair
    /// is a screen-height switch, and drawing both puts one copy inside the other.
    /// </summary>
    [Fact]
    public void HidesAReducedDetailGroupBesideItsFullDetailPartner()
    {
        string groups = Quad("OPAQUE", "HI.RAW") + "\n" + Quad("OPAQUEL", "LO.RAW");

        BinModel model = SmfModelDecoder.Decode(Build(4, groups, objectCount: 2), "TREE.SMF");

        Assert.Equal("OPAQUE", Assert.Single(model.Meshes).GroupName);
    }

    /// <summary>
    /// Dropping them must never empty a model, though: one that carries only a low-detail
    /// group is still better drawn than reported as having nothing to show.
    /// </summary>
    [Fact]
    public void KeepsAReducedDetailGroupThatIsAllThereIs()
    {
        BinModel model = SmfModelDecoder.Decode(Build(4, Quad("OPAQUEL", "LO.RAW")), "STUMP.SMF");

        Assert.Equal("OPAQUEL", Assert.Single(model.Meshes).GroupName);
        Assert.Contains(model.Warnings, warning => warning.Contains("reduced-detail"));
    }

    /// <summary>TRANSPI and TRANSPE are full-detail groups; only a trailing L marks a LOD.</summary>
    [Theory]
    [InlineData("TRANSPI")]
    [InlineData("TRANSPE")]
    public void OnlyATrailingLMarksAReducedDetailGroup(string groupName)
    {
        string groups = Quad("OPAQUE", "HI.RAW") + "\n" + Quad(groupName, "OTHER.RAW");

        BinModel model = SmfModelDecoder.Decode(Build(4, groups, objectCount: 2), "PROP.SMF");

        Assert.Equal(2, model.Meshes.Count);
    }

    [Fact]
    public void AHiddenGroupIsNotDrawn()
    {
        string groups = Quad("OPAQUE", "HI.RAW") + "\n"
            + string.Join("\n", "TRANSP", "0", "1", "4,1,2,0", "1,1,32,0,0,X.RAW", Vertices(), Faces());

        BinModel model = SmfModelDecoder.Decode(Build(4, groups, objectCount: 2), "PROP.SMF");

        Assert.Equal("OPAQUE", Assert.Single(model.Meshes).GroupName);
    }

    [Fact]
    public void RecognisesAModelByItsMagicRatherThanItsName() =>
        Assert.True(SmfModelDecoder.IsSmfModel(Build(4, Quad("OPAQUE", "W.RAW"))));

    [Theory]
    [InlineData("")]
    [InlineData("C3D")]
    [InlineData("NotAModelAtAll")]
    public void RejectsWhatIsNotAModel(string text) =>
        Assert.False(SmfModelDecoder.IsSmfModel(PodText.Latin1.GetBytes(text)));

    [Fact]
    public void RefusesAnUnknownFileVersion() =>
        Assert.Throws<ArgumentException>(() =>
            SmfModelDecoder.Decode(Build(9, Quad("OPAQUE", "W.RAW")), "FUTURE.SMF"));

    [Fact]
    public void RefusesSomethingThatIsNotAModel() =>
        Assert.Throws<ArgumentException>(() =>
            SmfModelDecoder.Decode(PodText.Latin1.GetBytes("nope\n"), "NOPE.SMF"));

    [Fact]
    public void RefusesATruncatedVertexBlock()
    {
        string group = string.Join("\n", "OPAQUE", "1", "1", "4,1,2,0", "1,1,32,0,0,W.RAW", "0,10,0,0,0,1,0,0.25");
        Assert.Throws<ArgumentException>(() => SmfModelDecoder.Decode(Build(4, group), "SHORT.SMF"));
    }

    // ── Fixtures ─────────────────────────────────────────────────────────────
    private static byte[] Build(int version, string groups, int objectCount = 1)
    {
        List<string> lines = ["C3DModel", version.ToString(), objectCount.ToString()];
        if (version >= 4) lines.Add("0,50.000000");
        lines.Add(groups);
        lines.Add(string.Empty);
        return PodText.Latin1.GetBytes(string.Join("\n", lines));
    }

    private static string Quad(string groupName, string textureName) => string.Join("\n",
        groupName, "1", "1", "4,1,2,0", "1,1,32,0,0," + textureName, Vertices(), Faces());

    private static string Vertices() => string.Join("\n",
        "0,10,0,0,0,1,0,0.25",
        "1,10,0,0,0,1,1,0.25",
        "1,0,0,0,0,1,1,0.75",
        "0,0,0,0,0,1,0,0.75");

    private static string Faces() => string.Join("\n", "0,1,2", "0,2,3");
}
