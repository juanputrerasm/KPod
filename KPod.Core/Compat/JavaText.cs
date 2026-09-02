namespace KPod.Core.Compat;

/// <summary>
/// String helpers that match Java semantics, so KPod decodes archive text
/// exactly as JPod does.
/// </summary>
public static class JavaText
{
    /// <summary>
    /// Trims like Java's <c>String.trim()</c>: every code point at or below
    /// U+0020 goes, control characters included. <c>String.Trim()</c> uses
    /// <c>char.IsWhiteSpace</c>, which leaves control bytes in place, and that
    /// difference would change how POD entry names decode.
    /// </summary>
    public static string TrimAsciiControl(this string value)
    {
        int start = 0;
        int end = value.Length;
        while (start < end && value[start] <= ' ')
        {
            start++;
        }

        while (end > start && value[end - 1] <= ' ')
        {
            end--;
        }

        return start == 0 && end == value.Length ? value : value.Substring(start, end - start);
    }
}
