using System.Globalization;
using KPod.Core.Pods;

namespace KPod.Core.Session;

/// <summary>One entry held in memory, editable until the archive is saved.</summary>
public sealed record EditableEntry(string Name, byte[] Data)
{
    /// <summary>
    /// The directory name field this entry was read from, carried so a re-save
    /// reproduces it byte for byte. Null for entries added from disk or from a
    /// manifest, which have no original field.
    /// </summary>
    public byte[]? RawNameField { get; init; }
    public string? EmbeddedPaletteName { get; init; }
    public uint Timestamp { get; init; } = unchecked((uint)DateTimeOffset.UtcNow.ToUnixTimeSeconds());
}

/// <summary>Which way a sorted column runs, or that no column is sorted.</summary>
public enum SortDirection
{
    None,
    Ascending,
    Descending,
}

/// <summary>
/// One visible row of the entry list: either a folder heading or a file.
/// </summary>
public sealed record BrowserRow(
    bool IsFolder,
    int SourceIndex,
    string FolderPath,
    string DisplayName,
    int Depth,
    long Size,
    string Description,
    bool Collapsed)
{
    internal static BrowserRow Folder(string folderPath, string displayName, int depth, bool collapsed) =>
        new(true, -1, folderPath, displayName, depth, 0L, "Folder", collapsed);

    internal static BrowserRow File(int sourceIndex, string displayName, int depth, long size, string description) =>
        new(false, sourceIndex, string.Empty, displayName, depth, size, description, false);

    /// <summary>Thousands-separated size, blank for a folder heading.</summary>
    public string SizeText => IsFolder ? string.Empty : Size.ToString("N0", CultureInfo.CurrentCulture);
}

/// <summary>
/// The collapsible folder view over the entry list: builds the visible rows from
/// entry names, tracks which folders are collapsed, applies the quick filter and
/// the column sort, and maps view rows back to entry indices.
///
/// <para>All of it is plain logic over names and sizes, so it lives here rather
/// than in the form and can be tested without Windows.</para>
/// </summary>
public sealed class EntryBrowser
{
    /// <summary>Column indices, matching the Name / Size / Description column order.</summary>
    public const int NameColumn = 0;
    public const int SizeColumn = 1;
    public const int DescriptionColumn = 2;

    private readonly List<EditableEntry> _entries = [];
    private readonly List<BrowserRow> _rows = [];
    private readonly HashSet<string> _collapsedFolders = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _knownFolders = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _explicitFolders = new(StringComparer.OrdinalIgnoreCase);

    private string _filter = string.Empty;

    /// <summary>The entries backing the view, in archive order.</summary>
    public IList<EditableEntry> Entries => _entries;

    /// <summary>Virtual empty folders created by the editor; POD stores them once files are added.</summary>
    public ISet<string> ExplicitFolders => _explicitFolders;

    /// <summary>The rows currently visible, rebuilt by <see cref="Refresh"/>.</summary>
    public IReadOnlyList<BrowserRow> Rows => _rows;

    public int SortColumn { get; private set; } = -1;

    public SortDirection SortDirection { get; private set; } = SortDirection.None;

    /// <summary>Quick-filter text; an empty string shows everything.</summary>
    public string Filter
    {
        get => _filter;
        set => _filter = value ?? string.Empty;
    }

    /// <summary>Total archive size the current entry list would produce on disk.</summary>
    public long ProjectedArchiveSize
    {
        get
        {
            IReadOnlyList<PodBlob> blobs = ToBlobs();
            int entrySize = PodArchiveWriter.FormatFor(blobs) == PodFormat.Pod1Extended ? 72 : 40;
            long total = 4 + 80 + ((long)_entries.Count * entrySize);
            foreach (EditableEntry entry in _entries)
            {
                total += entry.Data.Length;
            }

            return total;
        }
    }

    /// <summary>Drops every entry and all folder state.</summary>
    public void Clear()
    {
        _entries.Clear();
        _explicitFolders.Clear();
        ResetFolderState();
        _rows.Clear();
    }

    /// <summary>Forgets which folders are known and collapsed, keeping the entries.</summary>
    public void ResetFolderState()
    {
        _rows.Clear();
        _collapsedFolders.Clear();
        _knownFolders.Clear();
    }

    /// <summary>Rebuilds <see cref="Rows"/> from the entries, filter and sort state.</summary>
    public void Refresh()
    {
        FolderNode root = BuildFolderTree();
        EnsureKnownFoldersCollapsed(root);
        _rows.Clear();
        AppendRows(root, 0, _filter.Trim().ToLowerInvariant(), _rows);
    }

    /// <summary>
    /// Advances the sort on a column: ascending, then descending, then back to
    /// archive order on the third click.
    /// </summary>
    public void CycleSort(int column)
    {
        if (SortColumn != column)
        {
            SortColumn = column;
            SortDirection = SortDirection.Ascending;
        }
        else if (SortDirection == SortDirection.Ascending)
        {
            SortDirection = SortDirection.Descending;
        }
        else if (SortDirection == SortDirection.Descending)
        {
            SortColumn = -1;
            SortDirection = SortDirection.None;
        }
        else
        {
            SortDirection = SortDirection.Ascending;
        }
    }

    /// <summary>The header text for a column, with the sort arrow when it is sorted.</summary>
    public string HeaderText(int column, string baseName)
    {
        if (column != SortColumn)
        {
            return baseName;
        }

        return SortDirection switch
        {
            SortDirection.Ascending => baseName + " ↑",
            SortDirection.Descending => baseName + " ↓",
            _ => baseName,
        };
    }

    /// <summary>Collapses or expands the folder on the given view row.</summary>
    public void ToggleFolder(int viewRow)
    {
        if (viewRow < 0 || viewRow >= _rows.Count)
        {
            return;
        }

        BrowserRow row = _rows[viewRow];
        if (!row.IsFolder)
        {
            return;
        }

        if (!_collapsedFolders.Remove(row.FolderPath))
        {
            _collapsedFolders.Add(row.FolderPath);
        }
    }

    public void ExpandAllFolders()
    {
        RegisterFolders();
        _collapsedFolders.Clear();
    }

    public void CollapseAllFolders()
    {
        RegisterFolders();
        _collapsedFolders.Clear();
        foreach (string path in _knownFolders)
        {
            _collapsedFolders.Add(path);
        }
    }

    /// <summary>Expands every folder on the path to an entry, so it can be revealed.</summary>
    public void ExpandFoldersFor(int sourceIndex)
    {
        if (sourceIndex < 0 || sourceIndex >= _entries.Count)
        {
            return;
        }

        RegisterFolders();
        string normalized = NormalizeArchivePath(_entries[sourceIndex].Name);
        int slash = normalized.LastIndexOf('/');
        while (slash > 0)
        {
            _collapsedFolders.Remove(normalized.Substring(0, slash));
            slash = normalized.LastIndexOf('/', slash - 1);
        }
    }

    /// <summary>The entry index a view row points at, or -1 for a folder heading.</summary>
    public int ToSourceIndex(int viewRow)
    {
        if (viewRow < 0 || viewRow >= _rows.Count)
        {
            return -1;
        }

        BrowserRow row = _rows[viewRow];
        return row.IsFolder ? -1 : row.SourceIndex;
    }

    /// <summary>The view row showing an entry, or -1 when it is hidden.</summary>
    public int ToViewRow(int sourceIndex)
    {
        for (int i = 0; i < _rows.Count; i++)
        {
            if (!_rows[i].IsFolder && _rows[i].SourceIndex == sourceIndex)
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>
    /// The entry indices behind a selection of view rows, sorted ascending.
    /// Selecting a folder heading selects everything underneath it.
    /// </summary>
    public int[] SelectedSourceIndices(IEnumerable<int> viewRows)
    {
        SortedSet<int> indices = [];
        foreach (int viewRow in viewRows)
        {
            if (viewRow < 0 || viewRow >= _rows.Count)
            {
                continue;
            }

            BrowserRow row = _rows[viewRow];
            if (!row.IsFolder)
            {
                indices.Add(row.SourceIndex);
                continue;
            }

            string prefix = row.FolderPath + "/";
            for (int i = 0; i < _entries.Count; i++)
            {
                if (NormalizeArchivePath(_entries[i].Name).StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    indices.Add(i);
                }
            }
        }

        return [.. indices];
    }

    /// <summary>The entries as writer blobs, in archive order.</summary>
    public IReadOnlyList<PodBlob> ToBlobs()
    {
        List<PodBlob> blobs = new(_entries.Count);
        foreach (EditableEntry entry in _entries)
        {
            blobs.Add(new PodBlob(entry.Name, entry.Data, entry.RawNameField,
                entry.EmbeddedPaletteName, entry.Timestamp));
        }

        return blobs;
    }

    /// <summary>
    /// Marks every folder as seen, collapsing the ones that are new. Expanding or
    /// collapsing before the first <see cref="Refresh"/> would otherwise be undone
    /// by that refresh, which treats an unseen folder as collapsed by default.
    /// </summary>
    private void RegisterFolders() => EnsureKnownFoldersCollapsed(BuildFolderTree());

    private FolderNode BuildFolderTree()
    {
        FolderNode root = new(string.Empty, string.Empty, -1);
        foreach (string folderPath in _explicitFolders)
        {
            FolderNode cursor = root;
            foreach (string part in SplitEntryName(folderPath))
                cursor = cursor.ChildFolder(part, int.MaxValue);
        }
        for (int i = 0; i < _entries.Count; i++)
        {
            string[] parts = SplitEntryName(_entries[i].Name);
            FolderNode cursor = root;
            for (int part = 0; part < parts.Length - 1; part++)
            {
                cursor = cursor.ChildFolder(parts[part], i);
            }

            cursor.FileIndices.Add(i);
        }

        return root;
    }

    private static string[] SplitEntryName(string entryName) =>
        entryName.Replace('\0', ' ').Trim().Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries);

    /// <summary>
    /// Folders start collapsed the first time they are seen, and keep whatever
    /// state the user has given them afterwards.
    /// </summary>
    private void EnsureKnownFoldersCollapsed(FolderNode node)
    {
        foreach (FolderNode child in node.Folders)
        {
            if (_knownFolders.Add(child.Path))
            {
                _collapsedFolders.Add(child.Path);
            }

            EnsureKnownFoldersCollapsed(child);
        }
    }

    private void AppendRows(FolderNode node, int depth, string filter, List<BrowserRow> rows)
    {
        foreach (FolderItem item in OrderedItems(node, filter))
        {
            if (item.Folder is not null)
            {
                FolderNode folder = item.Folder;
                bool collapsed = _collapsedFolders.Contains(folder.Path);
                rows.Add(BrowserRow.Folder(folder.Path, folder.Name, depth, collapsed));

                // A filter overrides collapse, so matches are never hidden.
                if (!collapsed || filter.Length > 0)
                {
                    AppendRows(folder, depth + 1, filter, rows);
                }
            }
            else
            {
                EditableEntry entry = _entries[item.FileIndex];
                rows.Add(BrowserRow.File(
                    item.FileIndex,
                    item.FileName!,
                    depth,
                    entry.Data.Length,
                    PodEntryDescriber.Describe(entry.Name, entry.Data.Length)));
            }
        }
    }

    private List<FolderItem> OrderedItems(FolderNode node, string filter)
    {
        List<FolderItem> items = [];
        foreach (FolderNode folder in node.Folders)
        {
            if (MatchesFolderOrDescendant(folder, filter))
            {
                items.Add(new FolderItem(folder, -1, null));
            }
        }

        foreach (int fileIndex in node.FileIndices)
        {
            EditableEntry entry = _entries[fileIndex];
            if (filter.Length == 0
                || entry.Name.ToLowerInvariant().Contains(filter, StringComparison.Ordinal))
            {
                items.Add(new FolderItem(null, fileIndex, BaseName(entry.Name)));
            }
        }

        items.Sort(CompareItems);
        return items;
    }

    private int CompareItems(FolderItem left, FolderItem right)
    {
        if (SortDirection == SortDirection.None || SortColumn < 0)
        {
            return OrderKey(left).CompareTo(OrderKey(right));
        }

        // Folders stay above files whichever way the value sort runs.
        int byKind = FolderLastFlag(left).CompareTo(FolderLastFlag(right));
        if (byKind != 0)
        {
            return byKind;
        }

        int byValue = SortColumn switch
        {
            NameColumn => string.CompareOrdinal(
                SortName(left).ToLowerInvariant(), SortName(right).ToLowerInvariant()),
            SizeColumn => SortSize(left).CompareTo(SortSize(right)),
            DescriptionColumn => string.CompareOrdinal(
                SortDescription(left).ToLowerInvariant(), SortDescription(right).ToLowerInvariant()),
            _ => 0,
        };

        return SortDirection == SortDirection.Ascending ? byValue : -byValue;
    }

    private static int OrderKey(FolderItem item) =>
        item.Folder is not null ? item.Folder.FirstSourceIndex : item.FileIndex;

    private static int FolderLastFlag(FolderItem item) => item.Folder is not null ? 0 : 1;

    private static string SortName(FolderItem item) =>
        item.Folder is not null ? item.Folder.Name : item.FileName!;

    private long SortSize(FolderItem item) =>
        item.Folder is not null ? 0L : _entries[item.FileIndex].Data.Length;

    private string SortDescription(FolderItem item) =>
        item.Folder is not null
            ? "Folder"
            : PodEntryDescriber.Describe(_entries[item.FileIndex].Name, _entries[item.FileIndex].Data.Length);

    private bool MatchesFolderOrDescendant(FolderNode folder, string filter)
    {
        if (filter.Length == 0 || folder.Name.ToLowerInvariant().Contains(filter, StringComparison.Ordinal))
        {
            return true;
        }

        foreach (int fileIndex in folder.FileIndices)
        {
            if (_entries[fileIndex].Name.ToLowerInvariant().Contains(filter, StringComparison.Ordinal))
            {
                return true;
            }
        }

        foreach (FolderNode child in folder.Folders)
        {
            if (MatchesFolderOrDescendant(child, filter))
            {
                return true;
            }
        }

        return false;
    }

    private static string BaseName(string entryName)
    {
        string clean = entryName.Replace('\0', ' ').Trim();
        int slash = Math.Max(clean.LastIndexOf('/'), clean.LastIndexOf('\\'));
        return slash >= 0 ? clean.Substring(slash + 1) : clean;
    }

    private static string NormalizeArchivePath(string path) =>
        path.Replace('\\', '/').Replace('\0', ' ').Trim();

    /// <summary>One folder in the tree built from entry names.</summary>
    private sealed class FolderNode(string name, string path, int firstSourceIndex)
    {
        public string Name { get; } = name;

        public string Path { get; } = path;

        public int FirstSourceIndex { get; private set; } = firstSourceIndex;

        private readonly Dictionary<string, FolderNode> _byName = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Child folders in the order they were first seen, which is archive order.
        /// Dictionary enumeration makes no such promise, so the order is kept here
        /// explicitly rather than relied on.
        /// </summary>
        public List<FolderNode> Folders { get; } = [];

        public List<int> FileIndices { get; } = [];

        public FolderNode ChildFolder(string childName, int sourceIndex)
        {
            if (_byName.TryGetValue(childName, out FolderNode? existing))
            {
                existing.FirstSourceIndex = Math.Min(existing.FirstSourceIndex, sourceIndex);
                return existing;
            }

            string childPath = Path.Length == 0 ? childName : Path + "/" + childName;
            FolderNode child = new(childName, childPath, sourceIndex);
            _byName.Add(childName, child);
            Folders.Add(child);
            return child;
        }
    }

    /// <summary>A folder or a file awaiting ordering inside one parent folder.</summary>
    private sealed class FolderItem(FolderNode? folder, int fileIndex, string? fileName)
    {
        public FolderNode? Folder { get; } = folder;

        public int FileIndex { get; } = fileIndex;

        public string? FileName { get; } = fileName;
    }
}
