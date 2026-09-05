using KPod.Core.Pods;

namespace KPod.Core.Models;

public sealed record BinTextureEntries(PodEntry? Diffuse, PodEntry? Normal, PodEntry? SameNamePalette);

/// <summary>Resolves the archive assets referenced by a BIN texture name.</summary>
public static class BinTextureResolver
{
    private static readonly string[] SearchFolders = ["ART", "MODELS", "DATA", "TEXTURES"];

    public static BinTextureEntries Resolve(PodArchive archive, string textureName)
    {
        string stem = Stem(textureName);
        PodEntry? png = Find(archive, stem, ".PNG");
        PodEntry? tga = Find(archive, stem, ".TGA");
        PodEntry? raw = Find(archive, stem, ".RAW");
        PodEntry? diffuse = png ?? tga ?? raw;
        PodEntry? normal = Find(archive, stem + "_N", ".PNG") ?? Find(archive, stem + "_N", ".TGA");
        PodEntry? act = raw is null ? null : FindBeside(archive, raw, ".ACT") ?? Find(archive, stem, ".ACT");
        return new BinTextureEntries(diffuse, normal, act);
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
