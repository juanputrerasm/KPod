namespace KPod.Core.Compat;

/// <summary>Path helpers that .NET Framework does not provide.</summary>
public static class PathCompat
{
    /// <summary>
    /// The path of <paramref name="path"/> relative to <paramref name="baseFolder"/>.
    /// Written by hand rather than using Uri, which mangles spaces and is exactly
    /// the case POD folders hit.
    /// </summary>
    public static string GetRelativePath(string baseFolder, string path)
    {
#if NET5_0_OR_GREATER
        return Path.GetRelativePath(baseFolder, path);
#else
        string root = TrimEndingDirectorySeparator(Path.GetFullPath(baseFolder));
        string full = Path.GetFullPath(path);

        if (!full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            // Not underneath the base folder, so an absolute path is the answer.
            return full;
        }

        return full.Substring(root.Length + 1);
#endif
    }

    /// <summary>Drops a single trailing directory separator, if present.</summary>
    public static string TrimEndingDirectorySeparator(string path)
    {
#if NET5_0_OR_GREATER
        return Path.TrimEndingDirectorySeparator(path);
#else
        if (path.Length > 1
            && (path[path.Length - 1] == Path.DirectorySeparatorChar
                || path[path.Length - 1] == Path.AltDirectorySeparatorChar))
        {
            return path.Substring(0, path.Length - 1);
        }

        return path;
#endif
    }
}
