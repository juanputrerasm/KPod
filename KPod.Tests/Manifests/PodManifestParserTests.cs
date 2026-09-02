using KPod.Core.Compat;
using KPod.Core.Manifests;
using KPod.Core.Pods;

namespace KPod.Tests.Manifests;

public class PodManifestParserTests
{
    [Fact]
    public void EachListedFileBecomesAnUpperCasedEntry()
    {
        using TempDir temp = new();
        string folder = temp.CreateDirectory("src");
        File.WriteAllText(Path.Combine(folder, "wall.raw"), "wall");
        File.WriteAllText(Path.Combine(folder, "demo.txt"), "demo");
        string manifest = temp.WriteFile("src/build.lst", "wall.raw\n\ndemo.txt\n");

        IReadOnlyList<PodBlob> blobs = PodManifestParser.Parse(manifest, folder);

        Assert.Equal(["WALL.RAW", "DEMO.TXT"], blobs.Select(b => b.Name));
        Assert.Equal("wall", PodText.Latin1.GetString(blobs[0].Data));
    }

    [Fact]
    public void TheCommaFormRenamesTheEntry()
    {
        using TempDir temp = new();
        string folder = temp.CreateDirectory("src");
        File.WriteAllText(Path.Combine(folder, "wall.raw"), "wall");
        string manifest = temp.WriteFile("src/build.lst", @"wall.raw,ART\WALL01.RAW");

        IReadOnlyList<PodBlob> blobs = PodManifestParser.Parse(manifest, folder);

        Assert.Equal(@"ART\WALL01.RAW", blobs[0].Name);
    }

    [Fact]
    public void AFileMissingFromTheFolderIsResolvedFromTheParent()
    {
        using TempDir temp = new();
        string folder = temp.CreateDirectory("src");
        File.WriteAllText(temp.Resolve("shared.raw"), "shared");
        string manifest = temp.WriteFile("src/build.lst", "shared.raw");

        Assert.Equal("SHARED.RAW", PodManifestParser.Parse(manifest, folder)[0].Name);
    }

    [Fact]
    public void AnUnresolvableEntryIsReportedByName()
    {
        using TempDir temp = new();
        string folder = temp.CreateDirectory("src");
        string manifest = temp.WriteFile("src/build.lst", "missing.raw");

        FileNotFoundException ex = Assert.Throws<FileNotFoundException>(
            () => PodManifestParser.Parse(manifest, folder));
        Assert.Contains("missing.raw", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ReadsPodToolDirectivesCommentsAndRelativeFiles()
    {
        using TempDir temp = new();
        string folder = temp.CreateDirectory("src");
        Directory.CreateDirectory(Path.Combine(folder, "ART"));
        File.WriteAllText(Path.Combine(folder, "ART", "WALL.RAW"), "wall");
        string response = temp.WriteFile("src/build.rsp",
            "// response\npodFilename: demo.pod\nvolumeName: Demo volume\nART/WALL.RAW\n");

        PodManifestParser.Manifest parsed = PodManifestParser.ParseManifest(response, folder);
        Assert.Equal("demo.pod", parsed.PodFileName);
        Assert.Equal("Demo volume", parsed.VolumeName);
        Assert.Equal(@"ART\WALL.RAW", parsed.Blobs[0].Name);
    }
}
