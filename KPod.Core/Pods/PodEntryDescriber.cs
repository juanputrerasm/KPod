using KPod.Core.Images;

namespace KPod.Core.Pods;

/// <summary>
/// Turns an entry name and payload into the human-readable description shown in
/// the third column of the entry list.
/// </summary>
public static class PodEntryDescriber
{
    public static string Describe(string name, int byteCount)
    {
        int dot = name.LastIndexOf('.');
        string ext = dot >= 0 ? name.Substring(dot + 1).ToLowerInvariant() : string.Empty;
        return ext switch
        {
            "act" => "Palette file",
            "ani" => "Animation file",
            "bin" => "3D model file",
            "bmp" => "Bitmap image",
            "cl0" or "cl1" or "cl2" or "cl3" or "cl4" or "cl5" => "Ground-box collision layer",
            "clr" => "Terrain color layer",
            "crs" => "LVL auxiliary resource",
            "def" => "Object placement definition",
            "dmo" => "Demo manifest",
            "gif" => "GIF image",
            "glt" => "Ground/resource file",
            "jpeg" or "jpg" => "JPEG image",
            "json" => "JSON data",
            "klp" => "Music metadata",
            "lte" => "Extended terrain layer",
            "lvl" => "Level manifest",
            "lwo" => "LightWave object file",
            "map" => "Fog map",
            "mic" => "TV/F3 Intro text",
            "mix" or "mod" => "Music data",
            "pit" => "CPR Pits file",
            "png" => "PNG image",
            "pod" => "POD archive",
            "ra0" or "ra1" or "ra2" or "ra3" or "ra4" or "ra5" => "Ground-box layer",
            "raw" => RawDescription(byteCount),
            "sit" => "Track file",
            "smk" => "Smacker video",
            "tex" => "Texture manifest",
            "tnl" => "Tunnel definition file",
            "trk" => IsDataSubfolderEntry(name) ? "CPR track definition file" : "Truck definition file",
            "trn" => "Tournament definition file",
            "ttx" => "CPR Race-track texture table",
            "txx" => "Traxx track file",
            "txp" => "Texture pattern library",
            "txt" => "Text file",
            "tty" => "Texture metadata",
            "tvi" => "Video file",
            "wav" => "Wave audio file",
            "webp" => "WebP image",
            "lst" => "Response list file",
            "inf" => "Info report",
            "ini" or "cfg" => "Configuration file",
            _ => ext.Length == 0 ? "Binary data" : ext.ToUpperInvariant() + " file",
        };
    }

    private static string RawDescription(int byteCount)
    {
        (int Width, int Height)? dims = RawImageDecoder.DetectDimensions(byteCount);
        if (dims is null)
        {
            return "RAW image data (non-standard size)";
        }

        (int width, int height) = dims.Value;
        if ((width == 64 && height == 64) || (width == 256 && height == 256))
        {
            return "RAW image data";
        }

        return "RAW image data (non-standard " + width + "x" + height + ")";
    }

    /// <summary>
    /// True when the entry lives under a <c>DATA</c> folder, which is how a CPR
    /// track definition is told apart from an MTM truck definition.
    /// </summary>
    private static bool IsDataSubfolderEntry(string entryName)
    {
        string[] parts = entryName.Replace('\\', '/').ToUpperInvariant().Split('/');
        for (int i = 0; i < parts.Length - 1; i++)
        {
            if (parts[i] == "DATA")
            {
                return true;
            }
        }

        return false;
    }
}
