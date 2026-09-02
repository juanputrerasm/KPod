using KPod.Core.Pods;

namespace KPod.Core.Manifests;

/// <summary>
/// Parses a plain-text manifest (<c>.lst</c>) file into a list of POD blobs.
///
/// <para>Manifest format, one entry per non-blank line:</para>
/// <code>
///   filename              stored in the archive under that name
///   filename,archiveName  file read as filename, stored as archiveName
/// </code>
///
/// <para>Archive entry names are upper-cased, the Terminal Reality convention.</para>
/// </summary>
public static class PodManifestParser
{
    public sealed record Manifest(string PodFileName, string VolumeName,
        IReadOnlyList<PodBlob> Blobs);

    /// <summary>
    /// Parses <paramref name="manifestPath"/> and resolves each entry's bytes from
    /// disk. Files are looked up relative to <paramref name="sourceFolder"/>; if a
    /// file is not there, the immediate parent is tried as a fallback, preserving
    /// compatibility with archives whose sources span two directory levels.
    /// </summary>
    public static IReadOnlyList<PodBlob> Parse(string manifestPath, string sourceFolder)
        => ParseManifest(manifestPath, sourceFolder).Blobs;

    public static Manifest ParseManifest(string manifestPath, string sourceFolder)
    {
        string[] lines = File.ReadAllLines(manifestPath);
        List<PodBlob> blobs = [];
        string podFileName = "noname.pod";
        string volumeName = string.Empty;
        string? parentFolder = Path.GetDirectoryName(
            Path.GetFullPath(sourceFolder).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));

        foreach (string raw in lines)
        {
            string line = raw.Trim();
            if (line.Length == 0 || line.StartsWith("//", StringComparison.Ordinal))
            {
                continue;
            }
            if (line.StartsWith("podFilename:", StringComparison.OrdinalIgnoreCase))
            {
                podFileName = line.Substring(12).Trim();
                continue;
            }
            if (line.StartsWith("volumeName:", StringComparison.OrdinalIgnoreCase))
            {
                volumeName = line.Substring(11).Trim();
                continue;
            }

            string fileName;
            string entryName;
            int comma = line.IndexOf(',');
            if (comma > 0)
            {
                fileName = line.Substring(0, comma).Trim();
                entryName = line.Substring(comma + 1).Trim();
            }
            else
            {
                fileName = line;
                entryName = line;
            }

            byte[] data = ResolveFileBytes(fileName, sourceFolder, parentFolder);
            blobs.Add(new PodBlob(entryName.ToUpperInvariant(), data));
        }

        return new Manifest(podFileName, volumeName, blobs);
    }

    private static byte[] ResolveFileBytes(string fileName, string sourceFolder, string? parentFolder)
    {
        string candidate = Path.Combine(sourceFolder, fileName);
        if (File.Exists(candidate))
        {
            return File.ReadAllBytes(candidate);
        }

        if (parentFolder is not null)
        {
            string fallback = Path.Combine(parentFolder, fileName);
            if (File.Exists(fallback))
            {
                return File.ReadAllBytes(fallback);
            }
        }

        throw new FileNotFoundException("Manifest entry not found: " + fileName, fileName);
    }
}
