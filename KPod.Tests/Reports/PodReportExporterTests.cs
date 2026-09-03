using KPod.Core.Pods;
using KPod.Core.Reports;
using KPod.Core.Session;
using KPod.Tests.Pods;

namespace KPod.Tests.Reports;

public class PodReportExporterTests
{
    [Fact]
    public void InfoReportUsesTheFixedColumnLayout()
    {
        using TempDir temp = new();
        string pod = temp.WriteFile("game.pod", PodFixture.BuildPod1(
            32,
            "test comment",
            PodFile.Text(@"ART\WALL.RAW", "wall"),
            PodFile.Text("README.TXT", "readme")));
        string report = temp.Resolve("game.inf");

        NewExporter(pod).WriteInfoReport(report);
        string[] lines = File.ReadAllLines(report);

        Assert.Equal("Pod Filename = game.pod", lines[0]);
        Assert.Equal("Pod Entries  = 2", lines[2]);
        Assert.Equal("Pod Title    = test comment", lines[3]);
        Assert.Equal("Filename                     File Size      File Offset", lines[5]);

        // Name at column 0, size at column 30, offset at column 45.
        Assert.StartsWith(@"ART\WALL.RAW", lines[6], StringComparison.Ordinal);
        Assert.Equal('4', lines[6][30]);
        Assert.Equal("164", lines[6].Substring(45));
    }

    [Fact]
    public void APod164NameWiderThanItsColumnShiftsTheLaterColumnsRight()
    {
        using TempDir temp = new();
        const string longName = @"MODELS\TRUCKS\CUSTOM_BIGFOOT_WHEEL01.RAW";
        string pod = temp.WriteFile("long.pod", PodFixture.BuildPod164(
            new PodFile(longName, PodFixture.Payload(4))));
        string report = temp.Resolve("long.inf");

        NewExporter(pod).WriteInfoReport(report);
        string line = File.ReadAllLines(report)[6];

        // The whole name survives, and one space separates it from the size.
        Assert.StartsWith(longName + " 4 ", line, StringComparison.Ordinal);
    }

    [Fact]
    public void ListFileIsOneEntryNamePerLine()
    {
        using TempDir temp = new();
        string pod = temp.WriteFile("game.pod", PodFixture.BuildPod1(
            PodFile.Text(@"ART\WALL.RAW", "wall"),
            PodFile.Text("README.TXT", "readme")));
        string list = temp.Resolve("game.lst");

        NewExporter(pod).WriteListFile(list);

        Assert.Equal([@"ART\WALL.RAW", "README.TXT"], File.ReadAllLines(list));
    }

    [Fact]
    public void ReportsAreWrittenWithoutAByteOrderMark()
    {
        using TempDir temp = new();
        string pod = temp.WriteFile("game.pod", PodFixture.BuildPod1(PodFile.Text("A.TXT", "a")));
        string report = temp.Resolve("game.inf");
        string list = temp.Resolve("game.lst");

        PodReportExporter exporter = NewExporter(pod);
        exporter.WriteInfoReport(report);
        exporter.WriteListFile(list);

        // JPod writes these with no BOM; three stray bytes in front of the first
        // line would break every tool that reads the reports positionally.
        Assert.Equal((byte)'P', File.ReadAllBytes(report)[0]);
        Assert.Equal((byte)'A', File.ReadAllBytes(list)[0]);
    }

    [Fact]
    public void ExportingWithNoArchiveOpenThrows()
    {
        using TempDir temp = new();
        PodReportExporter exporter = new(new PodSession());

        Assert.Throws<InvalidOperationException>(() => exporter.WriteListFile(temp.Resolve("x.lst")));
    }

    private static PodReportExporter NewExporter(string podPath) =>
        new(new PodSession
        {
            SourceFolderPath = Path.GetDirectoryName(podPath),
            SourceFileName = Path.GetFileName(podPath),
            OpenArchive = PodArchiveReader.Read(File.ReadAllBytes(podPath)),
            ArchiveByteSize = new FileInfo(podPath).Length,
        });
}
