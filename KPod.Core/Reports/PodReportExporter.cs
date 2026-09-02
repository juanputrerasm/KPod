using System.Text;
using KPod.Core.Pods;
using KPod.Core.Session;

namespace KPod.Core.Reports;

/// <summary>
/// Exports human-readable reports about the open POD archive.
///
/// <list type="bullet">
///   <item><c>.inf</c>: fixed-column text report with the file name, byte size,
///   entry count, archive comment, and one padded line per entry showing name,
///   size and byte offset.</item>
///   <item><c>.lst</c>: plain entry-name list, one name per line, suitable as
///   input for the manifest parser.</item>
/// </list>
/// </summary>
public sealed class PodReportExporter(PodSession session)
{
    private const int SizeColumn = 30;
    private const int OffsetColumn = 45;

    /// <summary>
    /// UTF-8 with no byte-order mark. Java writes text files without one, and a
    /// BOM would put three bytes in front of "Pod Filename" that every other tool
    /// reading these reports would have to know about.
    /// </summary>
    private static readonly Encoding ReportEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    /// <summary>
    /// Writes a fixed-column <c>.inf</c> report.
    ///
    /// <code>
    ///   Pod Filename = name
    ///   Pod Size     = bytes
    ///   Pod Entries  = count
    ///   Pod Title    = comment
    ///
    ///   Filename                     File Size      File Offset
    ///   name padded to col 30        size col 30    offset col 45
    /// </code>
    /// <para>A name wider than its column shifts the later columns right.</para>
    /// </summary>
    public void WriteInfoReport(string outputPath)
    {
        PodArchive archive = RequireOpenArchive();
        StringBuilder text = new();
        AppendLine(text, "Pod Filename = " + session.SourceFileName);
        AppendLine(text, "Pod Size     = " + session.ArchiveByteSize);
        AppendLine(text, "Pod Entries  = " + archive.Entries.Count);
        AppendLine(text, "Pod Title    = " + archive.Comment);
        AppendLine(text, string.Empty);
        AppendLine(text, "Filename                     File Size      File Offset");

        foreach (PodEntry entry in archive.Entries)
        {
            // Name left-justified, size at col 30, offset at col 45. A POD1-64
            // name can be wider than its column, in which case the remaining
            // columns shift right instead of overwriting the name.
            StringBuilder line = new(96);
            line.Append(entry.Name.Replace('\0', ' ').Trim());
            PadTo(line, SizeColumn);
            line.Append(entry.Length);
            PadTo(line, OffsetColumn);
            line.Append(entry.Offset);
            AppendLine(text, line.ToString().TrimEnd());
        }

        File.WriteAllText(outputPath, text.ToString(), ReportEncoding);
    }

    /// <summary>Writes a plain-text <c>.lst</c> entry-name list, one name per line.</summary>
    public void WriteListFile(string outputPath)
    {
        PodArchive archive = RequireOpenArchive();
        StringBuilder text = new();
        foreach (PodEntry entry in archive.Entries)
        {
            AppendLine(text, entry.Name);
        }

        File.WriteAllText(outputPath, text.ToString(), ReportEncoding);
    }

    /// <summary>
    /// Pads with spaces up to <paramref name="column"/>, or appends a single
    /// separating space when the content already reaches past it.
    /// </summary>
    private static void PadTo(StringBuilder line, int column)
    {
        if (line.Length >= column)
        {
            line.Append(' ');
            return;
        }

        line.Append(' ', column - line.Length);
    }

    private static void AppendLine(StringBuilder text, string value) =>
        text.Append(value).Append(Environment.NewLine);

    private PodArchive RequireOpenArchive() =>
        session.OpenArchive ?? throw new InvalidOperationException("No archive is open.");
}
