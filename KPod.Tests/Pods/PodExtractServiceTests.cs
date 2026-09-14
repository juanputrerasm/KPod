using KPod.Core.Compat;
using KPod.Core.Pods;
using KPod.Core.Session;

namespace KPod.Tests.Pods;

public class PodExtractServiceTests
{
    [Fact]
    public void ExtractAllRecreatesTheArchiveFolderStructure()
    {
        using TempDir temp = new();
        string pod = temp.WriteFile("game.pod", PodFixture.BuildPod1(
            PodFile.Text(@"ART\WALL.RAW", "wall"),
            PodFile.Text("README.TXT", "readme")));
        string destination = temp.CreateDirectory("out");

        PodSession session = NewSession(pod, destination, preserveFolders: true);
        List<int> reported = [];
        new PodExtractService(session).ExtractAll(new Progress<int>(reported.Add));

        Assert.Equal("wall", File.ReadAllText(Path.Combine(destination, "ART", "WALL.RAW")));
        Assert.Equal("readme", File.ReadAllText(Path.Combine(destination, "README.TXT")));
    }

    [Fact]
    public void FlatExtractionDropsTheFolderPart()
    {
        using TempDir temp = new();
        string pod = temp.WriteFile("game.pod", PodFixture.BuildPod1(PodFile.Text(@"ART\WALL.RAW", "wall")));
        string destination = temp.CreateDirectory("flat");

        PodSession session = NewSession(pod, destination, preserveFolders: false);
        PodArchive archive = session.OpenArchive!;
        new PodExtractService(session).ExtractSelected(archive.Entries, null);

        Assert.True(File.Exists(Path.Combine(destination, "WALL.RAW")));
        Assert.False(Directory.Exists(Path.Combine(destination, "ART")));
    }

    [Fact]
    public void ExtractsAFullBudgetNestedPathInFull()
    {
        using TempDir temp = new();
        // 31 characters, the most a POD1 directory field can hold.
        const string longName = @"MODELS\TRUCKS\BIGFOOTWHEEL1.RAW";
        string pod = temp.WriteFile("long.pod", PodFixture.BuildPod1(
            new PodFile(longName, PodText.Latin1.GetBytes("wheel"))));
        string destination = temp.CreateDirectory("long-out");

        PodSession session = NewSession(pod, destination, preserveFolders: true);
        Assert.Equal(PodFormat.Pod1, session.OpenArchive!.Format);
        new PodExtractService(session).ExtractAll(null);

        Assert.Equal(
            "wheel",
            File.ReadAllText(Path.Combine(destination, "MODELS", "TRUCKS", "BIGFOOTWHEEL1.RAW")));
    }

    [Fact]
    public void ExtractingWithNoArchiveOpenThrows()
    {
        PodSession session = new();
        Assert.Throws<InvalidOperationException>(() => new PodExtractService(session).ExtractAll(null));
    }

    private static PodSession NewSession(string podPath, string destination, bool preserveFolders)
    {
        using PodArchive archive = PodArchiveReader.Read(podPath);
        return new PodSession
        {
            SourceFolderPath = Path.GetDirectoryName(podPath),
            SourceFileName = Path.GetFileName(podPath),
            OpenArchive = archive,
            TargetFolderPath = destination,
            PreserveExtractFolderStructure = preserveFolders,
            ArchiveByteSize = new FileInfo(podPath).Length,
        };
    }
}
