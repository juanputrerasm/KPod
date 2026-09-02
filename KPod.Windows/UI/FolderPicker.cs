namespace KPod.Windows.UI;

/// <summary>
/// One folder chooser for the whole app. FolderBrowserDialog gained
/// <c>InitialDirectory</c> only on .NET Core, so the Framework leg falls back to
/// <c>SelectedPath</c>, which is the closest equivalent it has.
/// </summary>
internal static class FolderPicker
{
    /// <summary>Returns the chosen folder, or null when the user cancelled.</summary>
    internal static string? Choose(IWin32Window owner, string description, string? startFolder)
    {
        using FolderBrowserDialog dialog = new()
        {
            Description = description,
            ShowNewFolderButton = true,
        };

        if (!string.IsNullOrWhiteSpace(startFolder) && Directory.Exists(startFolder))
        {
#if NET5_0_OR_GREATER
            dialog.UseDescriptionForTitle = true;
            dialog.InitialDirectory = startFolder!;
#else
            dialog.SelectedPath = startFolder!;
#endif
        }

        return dialog.ShowDialog(owner) == DialogResult.OK ? dialog.SelectedPath : null;
    }
}
