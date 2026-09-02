using KPod.Core.Compat;

namespace KPod.Core.Preferences;

/// <summary>
/// Persisted application config, stored as JSON in the user's preferences space.
/// The schema matches JPod's <c>config.json</c> exactly, so the file is portable
/// between the two builds.
/// </summary>
public sealed class AppConfig
{
    /// <summary>How many recently opened files are remembered.</summary>
    public const int MaxRecentFiles = 10;

    public AppConfig(IReadOnlyList<string> recentOpenedFiles) =>
        RecentOpenedFiles = recentOpenedFiles;

    public IReadOnlyList<string> RecentOpenedFiles { get; }

    public static AppConfig Defaults() => new([]);

    /// <summary>
    /// Returns a copy with <paramref name="path"/> at the front, de-duplicated and
    /// capped at <see cref="MaxRecentFiles"/>.
    /// </summary>
    public AppConfig WithRecentOpenedFile(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return this;
        }

        string head = Normalize(path!);
        List<string> updated = [head];
        foreach (string existing in RecentOpenedFiles)
        {
            if (string.IsNullOrWhiteSpace(existing))
            {
                continue;
            }

            string normalized = Normalize(existing);
            if (!string.Equals(normalized, head, StringComparison.OrdinalIgnoreCase))
            {
                updated.Add(normalized);
            }

            if (updated.Count == MaxRecentFiles)
            {
                break;
            }
        }

        return new AppConfig(updated);
    }

    /// <summary>Returns a copy without <paramref name="path"/>.</summary>
    public AppConfig WithoutRecentOpenedFile(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return this;
        }

        string target = Normalize(path!);
        List<string> updated = [];
        foreach (string existing in RecentOpenedFiles)
        {
            if (string.IsNullOrWhiteSpace(existing))
            {
                continue;
            }

            string normalized = Normalize(existing);
            if (!string.Equals(normalized, target, StringComparison.OrdinalIgnoreCase))
            {
                updated.Add(normalized);
            }
        }

        return new AppConfig(updated);
    }

    /// <summary>Reads the JPod-compatible JSON schema; anything malformed yields defaults.</summary>
    public static AppConfig Parse(string? json)
    {
        Dictionary<string, object?>? root = Json.ParseObject(json);
        if (root is null)
        {
            return Defaults();
        }

        List<string>? recents = Json.GetStringList(root, "recentOpenedFiles");
        return recents is null ? Defaults() : new AppConfig(recents);
    }

    /// <summary>Serializes to the same JSON shape JPod writes.</summary>
    public string ToJson() =>
        "{\n  \"recentOpenedFiles\": " + Json.StringArray(RecentOpenedFiles) + "\n}\n";

    private static string Normalize(string path)
    {
        try
        {
            return Path.GetFullPath(path);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            // A path the OS cannot normalize is still worth remembering verbatim.
            return path;
        }
    }
}
