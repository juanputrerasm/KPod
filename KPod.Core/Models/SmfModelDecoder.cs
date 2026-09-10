using System.Globalization;
using KPod.Core.Compat;

namespace KPod.Core.Models;

/// <summary>
/// Decoder for .SMF v2-v4, the static model format of 4x4 Evolution 1 and 2 ("C3DModel").
///
/// <para>The grammar is:</para>
/// <code>
/// "C3DModel"
/// fileVersion
/// objectCount
/// if fileVersion &gt;= 4: lodEnabled,lodSwitchHeight
///
/// repeat objectCount:
///     objectName
///     if fileVersion &gt;= 2: visible
///     objectVersion
///     vertexCount,frameCount,faceCount,unknown0
///     ["v1"]                                        optional Evo 2 material marker
///     material0,material1,material2,transparent,reflective,textureName
///     if v1: bumpTextureName
///     repeat frameCount:
///         repeat vertexCount: x,y,z,nx,ny,nz,u,v
///     repeat faceCount: i0,i1,i2
/// </code>
///
/// <para>Read as a counted state machine rather than by sniffing where the vertex block
/// ends: a vertex line and a face line are both just comma-separated numbers, and the counts
/// are the only thing that distinguishes them. Every count and every face index is
/// validated against the block it addresses.</para>
///
/// <para>Verified against all 118 models in the ASPEN, THEHILL, BAJBEACH and PEAK stock
/// tracks - 115 v4, 2 v2, 1 v3 - every file consumed exactly to its trailing blank line.
/// That corpus covers the Evo 2 "v1" bump-material form, a genuine 30-frame animated group,
/// and the v2/v3 files that carry no LOD header.</para>
///
/// <para>Output is a <see cref="BinModel"/> so that one model viewer draws both formats.
/// Faces are expanded to a triangle soup because that is the shape the .BIN path already
/// emits; these models are small enough that the duplication costs nothing.</para>
/// </summary>
public static class SmfModelDecoder
{
    private const string Magic = "C3DModel";
    private const int MaxObjects = 4096;
    private const int MaxVertices = 1 << 20;
    private const int MaxFaces = 1 << 20;

    /// <summary>
    /// A reduced-detail group is its full-detail partner's name suffixed with "L":
    /// OPAQUE/OPAQUEL, TRANSP/TRANSPL. Observed stock spellings are case variants of OPAQUE,
    /// OPAQUEL, TRANSP, TRANSPI, TRANSPE and TRANSPL, so TRANSPI and TRANSPE are full-detail
    /// groups and only the trailing L is significant.
    /// </summary>
    private static bool IsReducedDetail(string groupName) =>
        groupName.Equals("OPAQUEL", StringComparison.OrdinalIgnoreCase)
        || groupName.Equals("TRANSPL", StringComparison.OrdinalIgnoreCase);

    /// <summary>True when these bytes begin a C3DModel, whatever the entry is called.</summary>
    public static bool IsSmfModel(byte[] data)
    {
        if (data is null || data.Length < Magic.Length) return false;
        for (int i = 0; i < Magic.Length; i++)
        {
            if (data[i] != (byte)Magic[i]) return false;
        }
        return true;
    }

    public static BinModel Decode(byte[] data, string name)
    {
        BinModel model = Decode(data, name, keepAllGroups: false);
        /*
          Dropping the hidden and reduced-detail groups must never empty a model. A model
          that carries only a low-detail or hidden group is still better drawn than reported
          as having no geometry, so it is walked a second time keeping everything.
        */
        if (model.Meshes.Count > 0) return model;
        BinModel everything = Decode(data, name, keepAllGroups: true);
        return everything.Meshes.Count == 0 ? model : everything with
        {
            Warnings = everything.Warnings
                .Append("model has only reduced-detail or hidden groups; drawing them anyway")
                .ToArray(),
        };
    }

    private static BinModel Decode(byte[] data, string name, bool keepAllGroups)
    {
        string[] lines = PodText.Latin1.GetString(data)
            .Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');

        int cursor = 0;
        List<string> warnings = [];
        List<BinMesh> meshes = [];
        List<string> textureNames = [];
        HashSet<string> seenTextures = new(StringComparer.OrdinalIgnoreCase);
        List<string> hiddenOrReduced = [];
        int totalVertices = 0;
        int totalPolygons = 0;

        string? Next() => cursor < lines.Length ? lines[cursor++].Trim() : null;
        string? Peek() => cursor < lines.Length ? lines[cursor].Trim() : null;

        if (Next() != Magic) throw new ArgumentException(name + ": not a C3DModel");
        int fileVersion = ParseInt(Next());
        if (fileVersion is < 1 or > 4)
            throw new ArgumentException(name + ": unsupported .SMF version " + fileVersion);
        int objectCount = ParseInt(Next());
        if (objectCount is < 0 || objectCount > MaxObjects)
            throw new ArgumentException(name + ": implausible object count " + objectCount);

        bool lodEnabled = false;
        if (fileVersion >= 4)
        {
            string[] lod = (Next() ?? string.Empty).Split(',');
            lodEnabled = lod.Length > 0 && lod[0].Trim() != "0";
        }

        for (int o = 0; o < objectCount; o++)
        {
            string? groupName = Next();
            if (groupName is null)
            {
                warnings.Add("ran out of lines at object " + (o + 1) + " of " + objectCount);
                break;
            }
            bool visible = fileVersion < 2 || Next() != "0";
            Next(); // objectVersion; every stock model writes 1

            string[] counts = (Next() ?? string.Empty).Split(',');
            int vertexCount = ParseInt(counts.ElementAtOrDefault(0));
            int frameCount = Math.Max(1, ParseInt(counts.ElementAtOrDefault(1)));
            int faceCount = ParseInt(counts.ElementAtOrDefault(2));
            if (vertexCount is < 0 || vertexCount > MaxVertices || faceCount is < 0 || faceCount > MaxFaces)
                throw new ArgumentException(name + ": implausible counts in group \"" + groupName
                    + "\" (" + vertexCount + " verts, " + faceCount + " faces)");

            // The Evo 2 bump form announces itself with a bare "v1" line before the material.
            bool bumpForm = Peek() == "v1";
            if (bumpForm) Next();

            string[] material = (Next() ?? string.Empty).Split(',');
            string textureName = material.ElementAtOrDefault(5)?.Trim() ?? string.Empty;
            if (bumpForm) Next(); // bump texture name

            // Frame 0 is what gets drawn. Later frames are still walked line by line so the
            // cursor stays aligned with the face block; their position is counted, so
            // skipping them cannot desynchronise the read.
            float[] vx = new float[vertexCount * 3];
            float[] vn = new float[vertexCount * 3];
            float[] vt = new float[vertexCount * 2];
            for (int f = 0; f < frameCount; f++)
            {
                for (int v = 0; v < vertexCount; v++)
                {
                    string? line = Next();
                    if (line is null)
                        throw new ArgumentException(name + ": truncated vertex block in \"" + groupName + "\"");
                    if (f != 0) continue;
                    string[] p = line.Split(',');
                    vx[v * 3] = ParseFloat(p.ElementAtOrDefault(0));
                    vx[v * 3 + 1] = ParseFloat(p.ElementAtOrDefault(1));
                    vx[v * 3 + 2] = ParseFloat(p.ElementAtOrDefault(2));
                    vn[v * 3] = ParseFloat(p.ElementAtOrDefault(3));
                    vn[v * 3 + 1] = ParseFloat(p.ElementAtOrDefault(4));
                    vn[v * 3 + 2] = ParseFloat(p.ElementAtOrDefault(5));
                    vt[v * 2] = ParseFloat(p.ElementAtOrDefault(6));
                    vt[v * 2 + 1] = ParseFloat(p.ElementAtOrDefault(7));
                }
            }

            List<float> positions = new(faceCount * 9);
            List<float> normals = new(faceCount * 9);
            List<float> uvs = new(faceCount * 6);
            int triangles = 0;
            for (int f = 0; f < faceCount; f++)
            {
                string? line = Next();
                if (line is null)
                    throw new ArgumentException(name + ": truncated face block in \"" + groupName + "\"");
                string[] parts = line.Split(',');
                int[] tri =
                [
                    ParseInt(parts.ElementAtOrDefault(0)),
                    ParseInt(parts.ElementAtOrDefault(1)),
                    ParseInt(parts.ElementAtOrDefault(2)),
                ];
                if (tri.Any(index => index < 0 || index >= vertexCount))
                {
                    warnings.Add("\"" + groupName + "\" face " + f + " indexes outside its "
                        + vertexCount + " vertices");
                    continue;
                }
                foreach (int src in tri)
                {
                    positions.Add(vx[src * 3]); positions.Add(vx[src * 3 + 1]); positions.Add(vx[src * 3 + 2]);
                    normals.Add(vn[src * 3]); normals.Add(vn[src * 3 + 1]); normals.Add(vn[src * 3 + 2]);
                    uvs.Add(vt[src * 2]); uvs.Add(vt[src * 2 + 1]);
                }
                triangles++;
            }

            if (textureName.Length > 0 && seenTextures.Add(textureName))
                textureNames.Add(textureName.ToUpperInvariant());
            totalVertices += vertexCount;
            totalPolygons += triangles;

            // Material fields 3 and 4 are the transparency and reflectivity flags. Fields 0-2
            // are three scalars of unknown meaning; every stock model writes 1.0, 1.0, 32.0.
            bool transparent = (material.ElementAtOrDefault(3)?.Trim() ?? "0") != "0";
            BinMesh mesh = new(
                textureName.ToUpperInvariant(), transparent, null, null, false, 0xBFBFBF,
                positions, normals, uvs)
            {
                DoubleSided = true,
                GroupName = groupName,
            };
            if (positions.Count == 0) continue;
            if (keepAllGroups || (visible && !IsReducedDetail(groupName))) meshes.Add(mesh);
            else hiddenOrReduced.Add(groupName);
        }

        string format = "SMF v" + fileVersion.ToString(CultureInfo.InvariantCulture)
            + (lodEnabled ? " (LOD)" : string.Empty);
        return new BinModel(name, format, 65536, 0f, null, default, totalVertices, totalPolygons,
            textureNames, meshes, warnings)
        {
            UpAxis = "Y",
            UvOrigin = "top-left",
        };
    }

    private static int ParseInt(string? value) =>
        int.TryParse((value ?? string.Empty).Trim(), NumberStyles.Integer,
            CultureInfo.InvariantCulture, out int parsed) ? parsed : 0;

    private static float ParseFloat(string? value) =>
        float.TryParse((value ?? string.Empty).Trim(), NumberStyles.Float,
            CultureInfo.InvariantCulture, out float parsed) ? parsed : 0f;
}
