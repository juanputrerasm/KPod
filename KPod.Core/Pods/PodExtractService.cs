using KPod.Core.Session;

namespace KPod.Core.Pods;

/// <summary>
/// Extracts entries from an open POD archive to disk, streaming from the source
/// file rather than holding a second copy in memory.
///
/// <para>POD entry names may contain backslash separators; these are normalized to
/// the host separator before writing, and missing directories are created.</para>
/// </summary>
public sealed class PodExtractService(PodSession session)
{
    private const int TransferBufferSize = 65536;

    /// <summary>Extracts every entry to the session's target folder.</summary>
    /// <param name="progress">Receives the 1-based index of each entry as it starts.</param>
    public void ExtractAll(IProgress<int>? progress)
    {
        PodArchive archive = RequireOpenArchive();
        ExtractSelected(archive.Entries, progress);
    }

    /// <summary>Extracts only the supplied subset of entries.</summary>
    /// <param name="selectedEntries">Entries to extract.</param>
    /// <param name="progress">Receives the 1-based index within the selection.</param>
    public void ExtractSelected(IReadOnlyList<PodEntry> selectedEntries, IProgress<int>? progress)
    {
        RequireOpenArchive();
        string targetRoot = RequireTargetFolder();
        string sourcePath = session.SourcePath
            ?? throw new InvalidOperationException("No source archive path is set.");

        using FileStream source = new(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        byte[] buffer = new byte[TransferBufferSize];
        for (int i = 0; i < selectedEntries.Count; i++)
        {
            progress?.Report(i + 1);
            ExtractEntry(source, buffer, selectedEntries[i], targetRoot);
        }
    }

    private void ExtractEntry(FileStream source, byte[] buffer, PodEntry entry, string targetRoot)
    {
        string destination = ResolveDestination(targetRoot, entry.Name, session.PreserveExtractFolderStructure);
        string? parent = Path.GetDirectoryName(destination);
        if (!string.IsNullOrEmpty(parent))
        {
            Directory.CreateDirectory(parent!);
        }

        source.Seek(entry.Offset, SeekOrigin.Begin);
        using FileStream target = new(destination, FileMode.Create, FileAccess.Write, FileShare.None);
        long remaining = entry.Length;
        while (remaining > 0)
        {
            int want = (int)Math.Min(buffer.Length, remaining);
            int read = source.Read(buffer, 0, want);
            if (read <= 0)
            {
                break;
            }

            target.Write(buffer, 0, read);
            remaining -= read;
        }
    }

    /// <summary>
    /// Resolves a POD entry name, which may use backslash separators, to a path
    /// under <paramref name="targetRoot"/>.
    /// </summary>
    public static string ResolveDestination(string targetRoot, string entryName, bool preserveFolders)
    {
        string clean = entryName.Replace('\0', ' ').Trim();
        string[] parts = clean.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
        {
            return targetRoot;
        }

        if (!preserveFolders)
        {
            return Path.Combine(targetRoot, parts[parts.Length - 1]);
        }

        string path = targetRoot;
        foreach (string part in parts)
        {
            path = Path.Combine(path, part);
        }

        return path;
    }

    private PodArchive RequireOpenArchive() =>
        session.OpenArchive ?? throw new InvalidOperationException("No archive is open.");

    private string RequireTargetFolder() =>
        session.TargetFolderPath ?? throw new InvalidOperationException("No target folder is set.");
}
