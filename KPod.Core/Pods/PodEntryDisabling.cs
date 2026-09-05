namespace KPod.Core.Pods;

/// <summary>
/// CommPatch 26 hides a track or truck from the game by renaming it to an extension
/// the engine does not recognise, and the engine passes over what it cannot read.
///
/// <para>Nothing is deleted: <c>.TRK</c>, <c>.SIT</c> and <c>.SI2</c> become
/// <c>.TRX</c>, <c>.SIX</c> and <c>.SIY</c>, and renaming them back restores the
/// addon exactly as it was. Changing the status of a track pod can trigger the
/// multiplayer <em>Different Version</em> message, which is why this is a rename
/// rather than a removal.</para>
/// </summary>
public static class PodEntryDisabling
{
    private static readonly (string Enabled, string Disabled)[] Pairs =
    [
        (".TRK", ".TRX"),
        (".SIT", ".SIX"),
        (".SI2", ".SIY"),
    ];

    /// <summary>True when the entry is a track or truck the game currently reads.</summary>
    public static bool CanDisable(string name) => Disable(name) is not null;

    /// <summary>True when the entry is a track or truck that has been renamed out of the way.</summary>
    public static bool CanEnable(string name) => Enable(name) is not null;

    /// <summary>
    /// The disabled name for a track or truck entry, or null when the entry is
    /// neither.
    /// </summary>
    public static string? Disable(string name) => Swap(name, enable: false);

    /// <summary>
    /// The enabled name for a disabled track or truck entry, or null when the entry
    /// is not a disabled one.
    /// </summary>
    public static string? Enable(string name) => Swap(name, enable: true);

    private static string? Swap(string name, bool enable)
    {
        string entry = name ?? string.Empty;
        foreach ((string enabled, string disabled) in Pairs)
        {
            string from = enable ? disabled : enabled;
            if (entry.EndsWith(from, StringComparison.OrdinalIgnoreCase))
            {
                string to = enable ? enabled : disabled;
                // The archive's own casing is kept: a pod written in lower case stays
                // that way, which matters because the rename has to survive a
                // round trip through the entry list unchanged in every other respect.
                string replacement = char.IsLower(entry[entry.Length - 1]) ? to.ToLowerInvariant() : to;
                return entry.Substring(0, entry.Length - from.Length) + replacement;
            }
        }

        return null;
    }
}
