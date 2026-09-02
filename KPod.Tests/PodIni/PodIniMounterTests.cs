using KPod.Core.PodIni;

namespace KPod.Tests.PodIni;

public class PodIniMounterTests
{
    [Fact]
    public void MountingAppendsTheEntryAndBumpsTheCount()
    {
        using TempDir temp = new();
        string game = temp.CreateDirectory("game");
        string ini = Path.Combine(game, "pod.ini");
        File.WriteAllText(ini, "1\r\nFixes/startup.pod\r\n");

        MountResult result = PodIniMounter.Mount("MYTRACK.POD", game);

        Assert.False(result.RecommendedLimitExceeded);
        Assert.Equal(["2", "Fixes/startup.pod", "MYTRACK.POD"], File.ReadAllLines(ini));
    }

    [Fact]
    public void PodIniIsFoundInTheParentFolderToo()
    {
        using TempDir temp = new();
        string game = temp.CreateDirectory("game");
        string tracks = temp.CreateDirectory("game/tracks");
        File.WriteAllText(Path.Combine(game, "pod.ini"), "0\r\n");

        PodIniMounter.Mount("MYTRACK.POD", tracks, @"tracks\MYTRACK.POD");

        Assert.Equal(["1", @"tracks\MYTRACK.POD"], File.ReadAllLines(Path.Combine(game, "pod.ini")));
    }

    [Fact]
    public void MountingTheSamePodTwiceIsRefused()
    {
        using TempDir temp = new();
        string game = temp.CreateDirectory("game");
        File.WriteAllText(Path.Combine(game, "pod.ini"), "1\r\nMYTRACK.POD\r\n");

        Assert.Throws<AlreadyMountedException>(() => PodIniMounter.Mount("mytrack.pod", game));
    }

    [Fact]
    public void PassingTheRecommendedLimitIsReportedButStillMounts()
    {
        using TempDir temp = new();
        string game = temp.CreateDirectory("game");
        List<string> lines = ["99"];
        for (int i = 0; i < 99; i++)
        {
            lines.Add("pod" + i + ".pod");
        }

        string ini = Path.Combine(game, "pod.ini");
        File.WriteAllText(ini, string.Join("\r\n", lines) + "\r\n");

        MountResult result = PodIniMounter.Mount("EXTRA.POD", game);

        Assert.True(result.RecommendedLimitExceeded);
        Assert.Equal("100", File.ReadAllLines(ini)[0]);
    }

    [Fact]
    public void MissingPodIniIsReportedAsSuch()
    {
        using TempDir temp = new();
        string game = temp.CreateDirectory("game/deep");

        Assert.Throws<PodIniNotFoundException>(() => PodIniMounter.Mount("X.POD", game));
    }

    [Fact]
    public void TheWorkFileIsNotLeftBehind()
    {
        using TempDir temp = new();
        string game = temp.CreateDirectory("game");
        File.WriteAllText(Path.Combine(game, "pod.ini"), "0\r\n");

        PodIniMounter.Mount("X.POD", game);

        Assert.False(File.Exists(Path.Combine(game, "pod.wrk")));
    }
}
