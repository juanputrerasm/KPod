namespace KPod.Core.Images;

/// <summary>
/// The derived-map suffixes CommPatch 26 resolves beside an HD texture stem.
///
/// <para>A texture is looked up by stem, so <c>FOO.PNG</c> carries <c>FOO_N</c>,
/// <c>FOO_AO</c>, <c>FOO_DTL</c> and <c>FOO_MASK</c> into a pod with it. Those
/// companions are ordinary art files, and without the suffix spelled out the entry
/// list gives no hint that they are maps rather than pictures.</para>
/// </summary>
public static class HdArtMaps
{
    // Longest first, so a stem is never matched by a suffix that is only part of
    // the one it actually carries.
    private static readonly (string Suffix, string Role)[] Roles =
    [
        ("_MASK", "terrain detail mask"),
        ("_DTL", "terrain detail normal"),
        ("_AO", "ambient occlusion"),
        ("_N", "normal map"),
    ];

    /// <summary>
    /// The map role the file name's stem declares, or null when the name is a plain
    /// texture. The extension is ignored: the suffix sits on the stem.
    /// </summary>
    public static string? Role(string name)
    {
        string stem = Stem(name);
        foreach ((string suffix, string role) in Roles)
        {
            // A bare "_N.PNG" has no stem to be a map of, so the suffix has to be
            // carried by something.
            if (stem.Length > suffix.Length && stem.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            {
                return role;
            }
        }

        return null;
    }

    /// <summary>
    /// The description with the map role folded in. A description that already ends
    /// in a parenthetical gains the role inside it, so a RAW keeps one set of
    /// brackets rather than two.
    /// </summary>
    public static string Annotate(string description, string name)
    {
        string? role = Role(name);
        if (role is null)
        {
            return description;
        }

        return description.EndsWith(")", StringComparison.Ordinal)
            ? description.Substring(0, description.Length - 1) + ", " + role + ")"
            : description + " (" + role + ")";
    }

    private static string Stem(string name)
    {
        string normalized = name ?? string.Empty;
        int slash = Math.Max(normalized.LastIndexOf('/'), normalized.LastIndexOf('\\'));
        string title = slash >= 0 ? normalized.Substring(slash + 1) : normalized;
        int dot = title.LastIndexOf('.');
        return dot > 0 ? title.Substring(0, dot) : title;
    }
}
