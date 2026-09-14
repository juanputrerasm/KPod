using KPod.Core.Session;

namespace KPod.Tests.Session;

public class EntryBrowserTests
{
    [Fact]
    public void FoldersStartCollapsedSoOnlyRootFilesAndHeadingsShow()
    {
        EntryBrowser browser = Browser(@"ART\WALL.RAW", @"ART\DOOR.RAW", "README.TXT");
        browser.Refresh();

        Assert.Equal(["ART", "README.TXT"], browser.Rows.Select(r => r.DisplayName));
        Assert.True(browser.Rows[0].IsFolder);
        Assert.True(browser.Rows[0].Collapsed);
    }

    [Fact]
    public void ExpandingAFolderRevealsItsFilesIndented()
    {
        EntryBrowser browser = Browser(@"ART\WALL.RAW", "README.TXT");
        browser.Refresh();
        browser.ToggleFolder(0);
        browser.Refresh();

        Assert.Equal(["ART", "WALL.RAW", "README.TXT"], browser.Rows.Select(r => r.DisplayName));
        Assert.Equal(1, browser.Rows[1].Depth);
        Assert.Equal(0, browser.Rows[2].Depth);
    }

    [Fact]
    public void NestedFoldersEachGetTheirOwnDepth()
    {
        EntryBrowser browser = Browser(@"MODELS\TRUCKS\BIGFOOT.BIN");
        browser.ExpandAllFolders();
        browser.Refresh();

        Assert.Equal(["MODELS", "TRUCKS", "BIGFOOT.BIN"], browser.Rows.Select(r => r.DisplayName));
        Assert.Equal([0, 1, 2], browser.Rows.Select(r => r.Depth));
    }

    [Fact]
    public void CollapseAllPutsEveryKnownFolderBack()
    {
        EntryBrowser browser = Browser(@"MODELS\TRUCKS\BIGFOOT.BIN");
        browser.ExpandAllFolders();
        browser.Refresh();
        browser.CollapseAllFolders();
        browser.Refresh();

        Assert.Single(browser.Rows);
        Assert.Equal("MODELS", browser.Rows[0].DisplayName);
    }

    [Fact]
    public void AFilterOverridesCollapseSoMatchesAreNeverHidden()
    {
        EntryBrowser browser = Browser(@"ART\WALL.RAW", @"ART\DOOR.RAW", "README.TXT");
        browser.Refresh();
        browser.Filter = "wall";
        browser.Refresh();

        Assert.Equal(["ART", "WALL.RAW"], browser.Rows.Select(r => r.DisplayName));
    }

    [Fact]
    public void SortCyclesAscendingDescendingAndBackToArchiveOrder()
    {
        EntryBrowser browser = Browser("C.TXT", "A.TXT", "B.TXT");

        browser.CycleSort(EntryBrowser.NameColumn);
        browser.Refresh();
        Assert.Equal(["A.TXT", "B.TXT", "C.TXT"], browser.Rows.Select(r => r.DisplayName));
        Assert.Equal("Name ↑", browser.HeaderText(EntryBrowser.NameColumn, "Name"));

        browser.CycleSort(EntryBrowser.NameColumn);
        browser.Refresh();
        Assert.Equal(["C.TXT", "B.TXT", "A.TXT"], browser.Rows.Select(r => r.DisplayName));
        Assert.Equal("Name ↓", browser.HeaderText(EntryBrowser.NameColumn, "Name"));

        browser.CycleSort(EntryBrowser.NameColumn);
        browser.Refresh();
        Assert.Equal(["C.TXT", "A.TXT", "B.TXT"], browser.Rows.Select(r => r.DisplayName));
        Assert.Equal("Name", browser.HeaderText(EntryBrowser.NameColumn, "Name"));
    }

    [Fact]
    public void SortingBySizeOrdersFilesAndKeepsFoldersOnTop()
    {
        EntryBrowser browser = new();
        browser.Entries.Add(new EditableEntry("BIG.TXT", new byte[100]));
        browser.Entries.Add(new EditableEntry("SMALL.TXT", new byte[1]));
        browser.Entries.Add(new EditableEntry(@"ART\WALL.RAW", new byte[4096]));

        browser.CycleSort(EntryBrowser.SizeColumn);
        browser.Refresh();

        Assert.Equal(["ART", "SMALL.TXT", "BIG.TXT"], browser.Rows.Select(r => r.DisplayName));
    }

    [Fact]
    public void SelectingAFolderHeadingSelectsEverythingUnderIt()
    {
        EntryBrowser browser = Browser(@"ART\WALL.RAW", @"ART\DOOR.RAW", "README.TXT");
        browser.Refresh();

        Assert.Equal([0, 1], browser.SelectedSourceIndices([0]));
        Assert.Equal([2], browser.SelectedSourceIndices([1]));
    }

    [Fact]
    public void ViewRowsMapBackToEntryIndicesAndFolderHeadingsMapToNothing()
    {
        EntryBrowser browser = Browser(@"ART\WALL.RAW", "README.TXT");
        browser.ExpandAllFolders();
        browser.Refresh();

        Assert.Equal(-1, browser.ToSourceIndex(0));
        Assert.Equal(0, browser.ToSourceIndex(1));
        Assert.Equal(1, browser.ToSourceIndex(2));
        Assert.Equal(2, browser.ToViewRow(1));
    }

    [Fact]
    public void ExpandFoldersForRevealsADeeplyBuriedEntry()
    {
        EntryBrowser browser = Browser(@"MODELS\TRUCKS\BIGFOOT.BIN");
        browser.Refresh();
        Assert.Equal(-1, browser.ToViewRow(0));

        browser.ExpandFoldersFor(0);
        browser.Refresh();

        Assert.Equal(2, browser.ToViewRow(0));
    }

    [Fact]
    public void FolderRowsShowNoSizeAndFilesAreThousandsSeparated()
    {
        EntryBrowser browser = new();
        browser.Entries.Add(new EditableEntry(@"ART\WALL.RAW", new byte[4096]));
        browser.ExpandAllFolders();
        browser.Refresh();

        Assert.Equal(string.Empty, browser.Rows[0].SizeText);
        Assert.Equal("Folder", browser.Rows[0].Description);
        Assert.Equal(4096.ToString("N0"), browser.Rows[1].SizeText);
        Assert.Equal("RAW image data (64x64)", browser.Rows[1].Description);
    }

    [Fact]
    public void ProjectedSizeAlwaysUsesTheFortyByteDirectory()
    {
        // There is one POD1 directory record and it is 40 bytes, whatever the names are.
        EntryBrowser browser = new();
        browser.Entries.Add(new EditableEntry("SHORT.RAW", new byte[10]));
        Assert.Equal(4 + 80 + 40 + 10, browser.ProjectedArchiveSize);

        EntryBrowser oversized = new();
        oversized.Entries.Add(new EditableEntry(new string('A', 40) + ".RAW", new byte[10]));
        Assert.Equal(4 + 80 + 40 + 10, oversized.ProjectedArchiveSize);
        Assert.Single(oversized.OversizedNames);
    }

    private static EntryBrowser Browser(params string[] names)
    {
        EntryBrowser browser = new();
        foreach (string name in names)
        {
            browser.Entries.Add(new EditableEntry(name, new byte[4]));
        }

        return browser;
    }
}
