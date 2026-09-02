using System.Globalization;
using System.Text;

namespace KPod.Core.PodIni;

/// <summary>Raised when the POD being mounted is already listed in pod.ini.</summary>
public sealed class AlreadyMountedException(string message) : IOException(message);

/// <summary>Raised when no pod.ini can be found near the search root.</summary>
public sealed class PodIniNotFoundException(string message) : IOException(message);

/// <summary>The outcome of a successful mount.</summary>
/// <param name="RecommendedLimitExceeded">
/// True when pod.ini already held at least the recommended number of entries. The
/// POD is still mounted; the caller decides whether to warn.
/// </param>
public sealed record MountResult(bool RecommendedLimitExceeded);

/// <summary>
/// Adds a POD file name to the game's <c>pod.ini</c> mount list.
///
/// <code>
///   count          integer on line 1: how many PODs are mounted
///   FILENAME.POD   one mounted POD per following line
/// </code>
///
/// <para>The recommended mount count is 99. If pod.ini is absent in the search
/// root, the parent directory is tried. The file is updated through a
/// <c>pod.wrk</c> temporary file so a failed write cannot truncate the original.</para>
/// </summary>
public static class PodIniMounter
{
    private const int RecommendedMaxMountedPods = 99;
    private const string PodIniName = "pod.ini";
    private const string PodWrkName = "pod.wrk";

    /// <summary>Mounts <paramref name="podFileName"/> under its own name.</summary>
    public static MountResult Mount(string podFileName, string searchRoot) =>
        Mount(podFileName, searchRoot, podFileName);

    /// <summary>
    /// Mounts <paramref name="podFileName"/>, writing <paramref name="entryLabel"/>
    /// as the pod.ini line, which lets a POD in a subfolder be mounted as
    /// <c>subfolder\name.pod</c>.
    /// </summary>
    public static MountResult Mount(string podFileName, string searchRoot, string entryLabel)
    {
        string iniPath = LocatePodIni(searchRoot);
        List<string> lines = [.. File.ReadAllLines(iniPath)];
        if (lines.Count == 0)
        {
            lines.Add("0");
        }

        int count = ParseCount(lines[0]);
        bool recommendedLimitExceeded = count >= RecommendedMaxMountedPods;

        for (int i = 1; i < lines.Count; i++)
        {
            if (string.Equals(lines[i].Trim(), entryLabel.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                throw new AlreadyMountedException("POD already mounted: " + podFileName);
            }
        }

        List<string> newLines = new(lines.Count + 1)
        {
            (count + 1).ToString(CultureInfo.InvariantCulture),
        };
        for (int i = 1; i < lines.Count; i++)
        {
            newLines.Add(lines[i]);
        }

        newLines.Add(entryLabel);

        string workPath = Path.Combine(Path.GetDirectoryName(iniPath) ?? ".", PodWrkName);
        // UTF-8 with CRLF and no BOM, which is what the games and every other
        // pod.ini tool expect.
        File.WriteAllText(
            workPath,
            string.Join("\r\n", newLines) + "\r\n",
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        File.Copy(workPath, iniPath, overwrite: true);
        File.Delete(workPath);
        return new MountResult(recommendedLimitExceeded);
    }

    private static string LocatePodIni(string directory)
    {
        string candidate = Path.Combine(directory, PodIniName);
        if (File.Exists(candidate))
        {
            return candidate;
        }

        string? parent = Path.GetDirectoryName(
            Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if (parent is not null)
        {
            candidate = Path.Combine(parent, PodIniName);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new PodIniNotFoundException("pod.ini cannot be located near: " + directory);
    }

    private static int ParseCount(string line) =>
        int.TryParse(line.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) ? value : 0;
}
