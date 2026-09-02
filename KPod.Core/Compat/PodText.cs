using System.Text;

namespace KPod.Core.Compat;

/// <summary>Text encodings used when reading game data.</summary>
public static class PodText
{
    /// <summary>
    /// ISO-8859-1, the encoding of names and text inside POD archives.
    /// Looked up by code page rather than <c>Encoding.Latin1</c>, which
    /// .NET Framework does not have.
    /// </summary>
    public static readonly Encoding Latin1 = Encoding.GetEncoding(28591);
}
