namespace KPod.Core.Pods;

public sealed record PodValidationResult(
    PodFormat OutputFormat,
    IReadOnlyList<string> Errors,
    IReadOnlyList<string> Warnings)
{
    public bool IsValid => Errors.Count == 0;
}

/// <summary>Shared pre-save and opened-archive checks.</summary>
public static class PodArchiveValidator
{
    public static PodValidationResult ValidateForSave(string? comment,
        IReadOnlyList<PodBlob> blobs, PodFormat requested)
    {
        List<string> errors = [];
        List<string> warnings = [];
        PodFormat actual = requested;
        try
        {
            actual = PodArchiveWriter.ActualFormat(blobs, requested);

            // Checks the layout only. This used to build the whole archive and throw
            // the bytes away, which made renaming one entry cost a full serialization
            // of every payload.
            PodArchiveWriter.ValidateLayout(comment, blobs, new PodWriteOptions(requested, null, []));
        }
        // PodFormatException is an IOException, and an overlong name is now reported
        // through it rather than absorbed by a wider directory.
        catch (Exception ex) when (ex is ArgumentException or OverflowException or IOException)
        {
            errors.Add(ex.Message);
        }
        return new(actual, errors, warnings);
    }

    public static PodValidationResult ValidateOpened(PodArchive archive)
    {
        List<string> errors = [];
        List<string> warnings = [];
        HashSet<string> names = new(StringComparer.OrdinalIgnoreCase);
        foreach (PodEntry entry in archive.Entries)
        {
            try { PodArchiveWriter.ValidatePath(PodArchiveWriter.NormalizeName(entry.Name)); }
            catch (PodFormatException ex) { errors.Add(ex.Message); }
            if (!names.Add(entry.Name)) errors.Add("Duplicate POD entry name: " + entry.Name);
            if (entry.EmbeddedPaletteName is string palette
                && !palette.EndsWith(".ACT", StringComparison.OrdinalIgnoreCase))
                warnings.Add($"Malformed embedded palette name for {entry.Name}: {palette}");
            if (!archive.IsEntryChecksumValid(entry))
                errors.Add("POD2 entry checksum mismatch: " + entry.Name);
        }
        if (!archive.VerifyArchiveChecksum()) errors.Add("POD2 archive checksum mismatch");
        PodEntry[] byOffset = archive.Entries.OrderBy(e => e.Offset).ToArray();
        for (int i = 1; i < byOffset.Length; i++)
        {
            if (byOffset[i - 1].Offset + byOffset[i - 1].Length > byOffset[i].Offset)
                errors.Add($"Overlapping POD entries: {byOffset[i - 1].Name} and {byOffset[i].Name}");
        }
        return new(archive.Format, errors, warnings);
    }
}
