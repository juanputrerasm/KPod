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
            "act" => "VGA palette",
            "ani" => "Animated texture table",
            "bin" => "3D model file",
            "bmp" => "Bitmap image",
            "cl0" => "Ground box face textures layer 0",
            "cl1" => "Ground box face textures layer 1",
            "cl2" => "Ground box face textures layer 2",
            "cl3" => "Ground box face textures layer 3",
            "cl4" => "Ground box face textures layer 4",
            "cl5" => "Ground box face textures layer 5",
            "clr" => "Terrain color layer",
            "crs" => "LVL auxiliary resource",
            "def" => "Object placement definition",
            "dmo" => "Demo manifest",
            "gif" => "GIF image",
            "glt" => "Glow/light table",
            "grm" => "Terrain geometry data",
            "fog" => "Fog density/color index",
            "jpeg" or "jpg" => "JPEG image",
            "json" => "JSON data",
            "klp" => "Audio clip data",
            "lte" => "Terrain lighting grid/Fog grid",
            "lvl" => "Level definition file",
            "lwo" => "LightWave 3D object",
            "map" => "Map palette data",
            "mic" => "TV/F3 Intro text",
            "mix" => "MIX file",
            "mod" => "Module audio (MOD)",
            "pit" => "CPR Pits file",
            "png" => HdArtMaps.Annotate("PNG image", name),
            "pod" => "POD archive",
            "zip" => "ZIP archive",
            "ra0" => "Ground box height layer 0",
            "ra1" => "Ground box height layer 1",
            "ra2" => "Ground box height layer 2",
            "ra3" => "Ground box height layer 3",
            "ra4" => "Ground box height layer 4",
            "ra5" => "Ground box height layer 5",
            "raw" => HdArtMaps.Annotate(RawDescription(byteCount), name),
            "sit" => "Track situation file",
            "si2" => "Track situation file (CommPatch 3+ engine)",
            "six" => "Track situation file (disabled)",
            "siy" => "Track situation file (CommPatch 3+ engine, disabled)",
            "txv" => "Track version manifest",
            "smk" => "Smacker video",
            "tga" => HdArtMaps.Annotate("Targa image", name),
            "aai" => "Anti Alias information",
            "car" => "CPR Vehicle definition file",
            "cmd" => "CPR car data",
            "dvp" => "Developer/debug data",
            "jsin" => "JSON instrument data",
            "loc" => "Localization file",
            "lvo" => "Level object data",
            "nav" => "Navigation data",
            "ndx" => "Font data",
            "rgn" => "CPR dialog data",
            "200" => "320x200 cockpit data",
            "400" => "640x400 cockpit data",
            "480" => "640x480 cockpit data",
            "tex" => "Texture manifest",
            "tnl" => "Tunnel definition file",
            "trk" => IsDataSubfolderEntry(name) ? "CPR track definition file" : "Truck definition file",
            "trx" => IsDataSubfolderEntry(name)
                ? "CPR track definition file (disabled)"
                : "Truck definition file (disabled)",
            "trn" => "Tournament definition file",
            "ttx" => "CPR Race-track texture table",
            "txx" => "Traxx track file",
            "txp" => "Texture pattern library",
            "txt" => "Text file",
            "tty" => "Texture data",
            "tvi" => "TRI video info",
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
            return "RAW image data (" + width + "x" + height + ")";
        }

        if ((width == 320 && height == 200)
            || (width == 640 && height == 400)
            || (width == 640 && height == 480))
        {
            return "RAW image data (" + width + "x" + height + ")";
        }

        return "RAW image data (non-standard " + width + "x" + height + ")";
    }

    /// <summary>
    /// True when the entry lives under a <c>DATA</c> folder, which is how a CPR
    /// track definition is told apart from an MTM truck definition.
    ///
    /// <para>Scans the name in place rather than upper-casing and splitting it, which
    /// allocated three objects per call on a path that runs per entry.</para>
    /// </summary>
    private static bool IsDataSubfolderEntry(string entryName)
    {
        int start = 0;
        for (int i = 0; i < entryName.Length; i++)
        {
            char c = entryName[i];
            if (c is not ('/' or '\\'))
            {
                continue;
            }

            // Only segments with a separator after them are folders, which is why
            // the scan stops at the last separator and never sees the file name.
            if (i - start == 4
                && string.Compare(entryName, start, "DATA", 0, 4, StringComparison.OrdinalIgnoreCase) == 0)
            {
                return true;
            }

            start = i + 1;
        }

        return false;
    }
}
