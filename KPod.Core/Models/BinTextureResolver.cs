using KPod.Core.Pods;

namespace KPod.Core.Models;

public sealed record BinTextureEntries(PodEntry? Diffuse, PodEntry? Normal, PodEntry? SameNamePalette)
{
    /// <summary>
    /// The 4x4 Evolution .OPA opacity plane paired with this texture by stem, when the
    /// archive holds one.
    ///
    /// <para>Only meaningful beside an indexed .RAW: it is a real 0..255 gradient rather
    /// than a mask, so it is merged into the decoded alpha instead of being reduced to the
    /// MTM colour key.</para>
    /// </summary>
    public PodEntry? OpacityPlane { get; init; }
}

/// <summary>Resolves the archive assets referenced by a BIN texture name.</summary>
public static class BinTextureResolver
{
    private static readonly string[] SearchFolders = ["ART", "MODELS", "DATA", "TEXTURES"];

    public static BinTextureEntries Resolve(PodArchive archive, string textureName)
    {
        string stem = Stem(textureName);
        PodEntry? png = Find(archive, stem, ".PNG");
        PodEntry? tga = Find(archive, stem, ".TGA");
        // Evo 2 model art is palette-indexed .TIF, which the true-colour path cannot read.
        // It ranks below the HD forms and above .RAW, the way a material names it.
        PodEntry? tif = Find(archive, stem, ".TIF");
        PodEntry? raw = Find(archive, stem, ".RAW");
        PodEntry? diffuse = png ?? tga ?? tif ?? raw;
        PodEntry? normal = Find(archive, stem + "_N", ".PNG") ?? Find(archive, stem + "_N", ".TGA");
        PodEntry? act = raw is null ? null : FindBeside(archive, raw, ".ACT") ?? Find(archive, stem, ".ACT");
        PodEntry? opacity = raw is null ? null : FindBeside(archive, raw, ".OPA") ?? Find(archive, stem, ".OPA");
        return new BinTextureEntries(diffuse, normal, act) { OpacityPlane = opacity };
    }

    public static string Stem(string name)
    {
        string normalized = (name ?? string.Empty).Replace('\\', '/').Trim();
        int slash = normalized.LastIndexOf('/');
        string title = slash >= 0 ? normalized.Substring(slash + 1) : normalized;
        int dot = title.LastIndexOf('.');
        return (dot > 0 ? title.Substring(0, dot) : title).ToUpperInvariant();
    }

    private static PodEntry? Find(PodArchive archive, string stem, string extension)
    {
        string title = stem + extension;
        foreach (string folder in SearchFolders)
        {
            PodEntry? entry = archive.FindEntry(folder + "\\" + title)
                ?? archive.FindEntry(folder + "/" + title);
            if (entry is not null) return entry;
        }
        return archive.FindEntry(title) ?? archive.FindEntryByTitle(title);
    }

    private static PodEntry? FindBeside(PodArchive archive, PodEntry entry, string extension)
    {
        int dot = entry.Name.LastIndexOf('.');
        return dot < 0 ? null : archive.FindEntry(entry.Name.Substring(0, dot) + extension);
    }
}
