using KPod.Core.Compat;

namespace KPod.Core.Pods;

/// <summary>
/// Reads what the early Terminal Reality packer left in the spare bytes of a POD1
/// directory name field.
///
/// <para>The field is fixed width and holds a NUL-terminated path. In MTM1,
/// Terminal Velocity, Fury3 and Hellbender the packer also wrote a second
/// NUL-terminated string after it on every <c>.RAW</c> entry: the name of the
/// <c>.ACT</c> palette that art was authored against. MTM2 and CPR leave the
/// remainder zeroed.</para>
///
/// <para>Nothing upstream documents the second string, and readers in this family
/// have always stopped at the first terminator. It is used here only as a hint for
/// previewing art, never for locating entries.</para>
/// </summary>
public static class PodNameField
{
    /// <summary>
    /// The second NUL-terminated string in a directory name field, or null when
    /// the field holds only a path.
    /// </summary>
    public static string? SecondString(byte[]? field)
    {
        if (field is null)
        {
            return null;
        }

        int first = Array.IndexOf(field, (byte)0);
        if (first < 0 || first + 1 >= field.Length)
        {
            return null;
        }

        int start = first + 1;
        int end = start;
        while (end < field.Length && field[end] != 0)
        {
            end++;
        }

        if (end == start)
        {
            return null;
        }

        string value = PodText.Latin1.GetString(field, start, end - start).TrimAsciiControl();
        return value.Length == 0 ? null : value;
    }
}
