using KPod.Core.Pods;

namespace KPod.Core.Session;

/// <summary>
/// Holds the mutable state shared across services for the currently loaded POD
/// archive, so extraction, reporting and mounting can read source and target
/// paths without taking a parameter for every value.
/// </summary>
public sealed class PodSession
{
    private string _archiveComment = string.Empty;

    /// <summary>Folder that contains the open source POD file.</summary>
    public string? SourceFolderPath { get; set; }

    /// <summary>File name of the open source POD, for example <c>GAME.POD</c>.</summary>
    public string? SourceFileName { get; set; }

    /// <summary>The parsed archive, or null when none is open.</summary>
    public PodArchive? OpenArchive { get; set; }

    /// <summary>Destination folder for extract, build and report operations.</summary>
    public string? TargetFolderPath { get; set; }

    /// <summary>Output file name for build and export operations.</summary>
    public string? TargetFileName { get; set; }

    /// <summary>Total byte size of the open POD file on disk.</summary>
    public long ArchiveByteSize { get; set; }

    /// <summary>When true, extracted entries keep their POD folder structure.</summary>
    public bool PreserveExtractFolderStructure { get; set; } = true;

    /// <summary>True if a non-fatal issue occurred during the last operation.</summary>
    public bool HadOperationIssue { get; set; }

    /// <summary>Comment stored in the 80-byte POD header field.</summary>
    public string ArchiveComment
    {
        get => _archiveComment;
        set => _archiveComment = value ?? string.Empty;
    }

    public bool IsArchiveOpen => OpenArchive is not null;

    public string? SourcePath =>
        SourceFolderPath is null || SourceFileName is null
            ? null
            : Path.Combine(SourceFolderPath, SourceFileName);

    /// <summary>Clears all transient state; called when a new archive is opened.</summary>
    public void Reset()
    {
        OpenArchive = null;
        SourceFolderPath = null;
        SourceFileName = null;
        TargetFolderPath = null;
        TargetFileName = null;
        ArchiveComment = string.Empty;
        ArchiveByteSize = 0;
        PreserveExtractFolderStructure = true;
        HadOperationIssue = false;
    }
}
