using KPod.Core.Pods;

namespace KPod.Tests.Pods;

public class PodEntryDisablingTests
{
    [Theory]
    [InlineData(@"TRUCKS\BIGFOOT.TRK", @"TRUCKS\BIGFOOT.TRX")]
    [InlineData(@"LAGUNA.SIT", "LAGUNA.SIX")]
    [InlineData(@"LAGUNA.SI2", "LAGUNA.SIY")]
    public void DisablingSwapsTheExtensionAndEnablingPutsItBack(string enabled, string disabled)
    {
        Assert.Equal(disabled, PodEntryDisabling.Disable(enabled));
        Assert.Equal(enabled, PodEntryDisabling.Enable(disabled));
        Assert.True(PodEntryDisabling.CanDisable(enabled));
        Assert.True(PodEntryDisabling.CanEnable(disabled));
    }

    [Fact]
    public void AnEntryIsOnlyEverInOneOfTheTwoStates()
    {
        Assert.False(PodEntryDisabling.CanEnable(@"TRUCKS\BIGFOOT.TRK"));
        Assert.False(PodEntryDisabling.CanDisable(@"TRUCKS\BIGFOOT.TRX"));
    }

    [Theory]
    [InlineData(@"ART\WALL.RAW")]
    [InlineData(@"DATA\LAGUNA.TXV")]
    [InlineData("BIGFOOT")]
    [InlineData("")]
    public void EverythingElseIsLeftAlone(string name)
    {
        Assert.Null(PodEntryDisabling.Disable(name));
        Assert.Null(PodEntryDisabling.Enable(name));
    }

    [Fact]
    public void TheArchivesOwnCasingSurvivesTheRename()
    {
        Assert.Equal(@"trucks\bigfoot.trx", PodEntryDisabling.Disable(@"trucks\bigfoot.trk"));
        Assert.Equal(@"trucks\bigfoot.trk", PodEntryDisabling.Enable(@"trucks\bigfoot.trx"));
    }
}
