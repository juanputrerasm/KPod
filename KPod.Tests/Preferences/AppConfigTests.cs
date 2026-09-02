using KPod.Core.Preferences;

namespace KPod.Tests.Preferences;

public class AppConfigTests
{
    [Fact]
    public void RoundTripsThroughTheJPodJsonSchema()
    {
        using TempDir temp = new();
        string first = temp.WriteFile("a.pod", "a");
        string second = temp.WriteFile("b.pod", "b");
        AppConfig config = AppConfig.Defaults()
            .WithRecentOpenedFile(second)
            .WithRecentOpenedFile(first);

        AppConfig parsed = AppConfig.Parse(config.ToJson());

        Assert.Equal([first, second], parsed.RecentOpenedFiles);
        Assert.Contains("\"recentOpenedFiles\"", config.ToJson(), StringComparison.Ordinal);
    }

    [Fact]
    public void TheMostRecentFileMovesToTheFrontWithoutDuplicating()
    {
        using TempDir temp = new();
        string first = temp.WriteFile("a.pod", "a");
        string second = temp.WriteFile("b.pod", "b");

        AppConfig config = AppConfig.Defaults()
            .WithRecentOpenedFile(first)
            .WithRecentOpenedFile(second)
            .WithRecentOpenedFile(first);

        Assert.Equal([first, second], config.RecentOpenedFiles);
    }

    [Fact]
    public void TheListIsCappedAtTenEntries()
    {
        using TempDir temp = new();
        AppConfig config = AppConfig.Defaults();
        for (int i = 0; i < 15; i++)
        {
            config = config.WithRecentOpenedFile(temp.WriteFile("pod" + i + ".pod", "x"));
        }

        Assert.Equal(AppConfig.MaxRecentFiles, config.RecentOpenedFiles.Count);
    }

    [Fact]
    public void RemovingAnEntryLeavesTheRestInOrder()
    {
        using TempDir temp = new();
        string first = temp.WriteFile("a.pod", "a");
        string second = temp.WriteFile("b.pod", "b");
        AppConfig config = AppConfig.Defaults()
            .WithRecentOpenedFile(second)
            .WithRecentOpenedFile(first);

        Assert.Equal([second], config.WithoutRecentOpenedFile(first).RecentOpenedFiles);
    }

    [Fact]
    public void MalformedOrEmptyJsonFallsBackToDefaults()
    {
        Assert.Empty(AppConfig.Parse("{ not json").RecentOpenedFiles);
        Assert.Empty(AppConfig.Parse(null).RecentOpenedFiles);
        Assert.Empty(AppConfig.Parse("{}").RecentOpenedFiles);
    }

    [Fact]
    public void ConfigLivesUnderTheKPodProductFolderAndKnowsTheJPodOne()
    {
        Assert.EndsWith(Path.Combine("KPod", "config.json"), ConfigStore.ConfigPath, StringComparison.Ordinal);
        Assert.EndsWith(Path.Combine("JPod", "config.json"), ConfigStore.LegacyConfigPath, StringComparison.Ordinal);
    }
}
