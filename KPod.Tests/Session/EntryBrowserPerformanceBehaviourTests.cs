using KPod.Core.Pods;
using KPod.Core.Session;
using KPod.Tests.Pods;

namespace KPod.Tests.Session;

/// <summary>
/// Pins the behaviour that changed shape when the browser stopped recomputing per
/// entry inside its comparator and its filter: the sort order, the filter matching,
/// folder selection, and the row index. These all used to be derived by rescanning,
/// so the results have to stay identical now that they come from caches.
/// </summary>
public class EntryBrowserPerformanceBehaviourTests
{
    private static EntryBrowser WithFiles(params string[] names)
    {
        EntryBrowser browser = new();
        foreach (string name in names)
        {
            browser.Entries.Add(new EditableEntry(name, new byte[4]));
        }

        browser.Refresh();
        return browser;
    }

    [Fact]
    public void NameSortFoldsCaseDownwardsSoUnderscoreStillSortsBeforeLetters()
    {
        // Folding case up instead of down would put AA.TXT first, because '_' is
        // 0x5F: above 'A' but below 'a'. Real POD names use underscores, so the
        // direction of the fold is observable and has to stay as it was.
        EntryBrowser browser = WithFiles("AA.TXT", "A_.TXT");
        browser.CycleSort(EntryBrowser.NameColumn);
        browser.Refresh();

        Assert.Equal(["A_.TXT", "AA.TXT"], browser.Rows.Select(r => r.DisplayName));
    }

    [Fact]
    public void NameSortIsCaseInsensitiveAndReversesOnTheSecondClick()
    {
        EntryBrowser browser = WithFiles("beta.txt", "Alpha.txt", "GAMMA.txt");

        browser.CycleSort(EntryBrowser.NameColumn);
        browser.Refresh();
        Assert.Equal(["Alpha.txt", "beta.txt", "GAMMA.txt"], browser.Rows.Select(r => r.DisplayName));

        browser.CycleSort(EntryBrowser.NameColumn);
        browser.Refresh();
        Assert.Equal(["GAMMA.txt", "beta.txt", "Alpha.txt"], browser.Rows.Select(r => r.DisplayName));
    }

    [Fact]
    public void DescriptionSortOrdersByTheSameTextTheRowsShow()
    {
        EntryBrowser browser = WithFiles("A.WAV", "B.ACT", "C.TXT");
        browser.CycleSort(EntryBrowser.DescriptionColumn);
        browser.Refresh();

        Assert.Equal(
            ["Palette file", "Text file", "Wave audio file"],
            browser.Rows.Select(r => r.Description));
    }

    [Fact]
    public void FilterMatchesRegardlessOfCaseAndPullsMatchesOutOfCollapsedFolders()
    {
        EntryBrowser browser = WithFiles(@"ART\WALL01.RAW", @"ART\DOOR.RAW", @"SOUND\HORN.WAV");

        browser.Filter = "wall";
        browser.Refresh();

        Assert.Equal(["ART", "WALL01.RAW"], browser.Rows.Select(r => r.DisplayName));
    }

    [Fact]
    public void FilterKeepsAFolderWhoseOwnNameMatchesEvenWhenNoFileDoes()
    {
        EntryBrowser browser = WithFiles(@"SOUND\HORN.WAV", "READ.TXT");

        browser.Filter = "sound";
        browser.Refresh();

        Assert.Equal(["SOUND", "HORN.WAV"], browser.Rows.Select(r => r.DisplayName));
    }

    [Fact]
    public void FilterKeepsAnAncestorWhoseDeepDescendantMatches()
    {
        EntryBrowser browser = WithFiles(@"A\B\C\DEEP.RAW", @"OTHER\SHALLOW.RAW");

        browser.Filter = "deep";
        browser.Refresh();

        Assert.Equal(["A", "B", "C", "DEEP.RAW"], browser.Rows.Select(r => r.DisplayName));
    }

    [Fact]
    public void SelectingAFolderHeadingStillPicksUpEverythingBelowIt()
    {
        EntryBrowser browser = WithFiles(
            @"ART\WALL.RAW", @"ART\SUB\DEEP.RAW", "ROOT.TXT", @"ARTWORK\OTHER.RAW");
        browser.ExpandAllFolders();
        browser.Refresh();

        int artRow = browser.Rows
            .Select((row, index) => (row, index))
            .First(pair => pair.row.IsFolder && pair.row.FolderPath == "ART").index;

        // ARTWORK shares a prefix with ART but is not underneath it.
        Assert.Equal([0, 1], browser.SelectedSourceIndices([artRow]));
    }

    [Fact]
    public void ViewRowsStayCorrectAfterFilteringAndSorting()
    {
        EntryBrowser browser = WithFiles("B.TXT", "A.TXT", "C.TXT");
        browser.CycleSort(EntryBrowser.NameColumn);
        browser.Refresh();

        // Sorted A, B, C while the entries are still in archive order B, A, C.
        Assert.Equal(1, browser.ToViewRow(0));
        Assert.Equal(0, browser.ToViewRow(1));
        Assert.Equal(2, browser.ToViewRow(2));

        browser.Filter = "c.txt";
        browser.Refresh();

        Assert.Equal(-1, browser.ToViewRow(0));
        Assert.Equal(0, browser.ToViewRow(2));
    }

    [Fact]
    public void EditingTheEntryListRefreshesTheCachedRowText()
    {
        EntryBrowser browser = WithFiles("A.TXT");
        Assert.Equal("Text file", browser.Rows[0].Description);

        browser.Entries[0] = new EditableEntry("A.WAV", new byte[8]);
        browser.Refresh();

        Assert.Equal("Wave audio file", browser.Rows[0].Description);
        Assert.Equal(8, browser.Rows[0].Size);
    }

    [Fact]
    public void ProjectedSizeMatchesWhatTheWriterActuallyProduces()
    {
        EntryBrowser browser = WithFiles(@"ART\WALL.RAW", "READ.TXT");

        byte[] written = PodArchiveWriter.BuildBytes(string.Empty, browser.ToBlobs());

        Assert.Equal(written.Length, browser.ProjectedArchiveSize);
    }

    [Fact]
    public void ProjectedSizeMatchesTheWriterForTheLongDirectoryToo()
    {
        EntryBrowser browser = WithFiles(new string('A', 40) + ".RAW", "READ.TXT");

        byte[] written = PodArchiveWriter.BuildBytes(string.Empty, browser.ToBlobs());

        Assert.Equal(PodFormat.Pod1Extended, browser.ProjectedFormat);
        Assert.Equal(written.Length, browser.ProjectedArchiveSize);
    }

    [Fact]
    public void AnArchiveBackedEntryReportsItsLengthWithoutReadingThePayload()
    {
        byte[] archive = PodFixture.BuildPod1(new PodFile("A.TXT", PodFixture.Payload(1234)));
        using PodArchive open = PodArchiveReader.Read(archive);
        PodEntry entry = open.Entries[0];

        EditableEntry lazy = new(entry.Name, open.Data, entry.Offset, (int)entry.Length);

        Assert.Equal(1234, lazy.Length);
        Assert.Equal(PodFixture.Payload(1234), lazy.Data);
    }

    [Fact]
    public void RenamingAnArchiveBackedEntryKeepsItArchiveBacked()
    {
        byte[] archive = PodFixture.BuildPod1(new PodFile("A.TXT", PodFixture.Payload(64)));
        using PodArchive open = PodArchiveReader.Read(archive);
        PodEntry entry = open.Entries[0];

        EditableEntry lazy = new(entry.Name, open.Data, entry.Offset, (int)entry.Length)
        {
            RawNameField = entry.RawNameField,
            Timestamp = 42u,
        };
        EditableEntry renamed = lazy.WithName(@"ART\B.TXT");

        Assert.Equal(@"ART\B.TXT", renamed.Name);
        Assert.Equal(64, renamed.Length);
        Assert.Equal(42u, renamed.Timestamp);
        Assert.Equal(lazy.RawNameField, renamed.RawNameField);
        Assert.Equal(PodFixture.Payload(64), renamed.Data);
    }
}
