using System.Collections;
using System.Globalization;
using KPod.Core.Pods;

namespace KPod.Core.Session;

/// <summary>
/// One entry held by the editor, editable until the archive is saved.
///
/// <para>The payload is either held directly, for an entry added from disk or from a
/// manifest, or referenced in the open archive and read on demand. An archive entry
/// that is only browsed, sorted and filtered never has its bytes read at all, which is
/// what keeps opening a 200 MB POD from costing 200 MB.</para>
///
/// <para>A class rather than a record: a lazily-read payload has no business taking
/// part in value equality.</para>
/// </summary>
public sealed class EditableEntry
{
    private readonly byte[]? _data;
    private readonly IPodDataSource? _source;
    private readonly long _offset;

    /// <summary>An entry whose bytes are already in hand.</summary>
    public EditableEntry(string name, byte[] data)
    {
        Name = name;
        _data = data ?? throw new ArgumentNullException(nameof(data));
        Length = data.Length;
    }

    /// <summary>An entry that reads its bytes from an open archive when asked.</summary>
    public EditableEntry(string name, IPodDataSource source, long offset, int length)
    {
        Name = name;
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _offset = offset;
        Length = length;
    }

    private EditableEntry(EditableEntry other, string name)
    {
        Name = name;
        _data = other._data;
        _source = other._source;
        _offset = other._offset;
        Length = other.Length;
        RawNameField = other.RawNameField;
        EmbeddedPaletteName = other.EmbeddedPaletteName;
        Timestamp = other.Timestamp;
    }

    public string Name { get; }

    /// <summary>Payload size. Always known, and never reads the payload.</summary>
    public int Length { get; }

    /// <summary>
    /// The payload, read from the archive each time for an entry that has not been
    /// materialized. Deliberately uncached: caching would refill memory with the
    /// whole archive over a session of previewing entries, and every caller here
    /// either writes the bytes straight out or hands them to a preview.
    /// </summary>
    public byte[] Data => _data ?? _source!.ReadExact(_offset, Length);

    /// <summary>
    /// The directory name field this entry was read from, carried so a re-save
    /// reproduces it byte for byte. Null for entries added from disk or from a
    /// manifest, which have no original field.
    /// </summary>
    public byte[]? RawNameField { get; init; }

    public string? EmbeddedPaletteName { get; init; }

    public uint Timestamp { get; init; } = unchecked((uint)DateTimeOffset.UtcNow.ToUnixTimeSeconds());

    /// <summary>
    /// The same entry under a different name. Keeps the payload reference as it is,
    /// so renaming an entry in a large archive stays free.
    /// </summary>
    public EditableEntry WithName(string name) => new(this, name);

    /// <summary>
    /// Writes the payload to a stream without holding it in memory. The editor's
    /// entry list can mix archive-backed and disk-backed entries, so extraction goes
    /// through here rather than seeking in one source file.
    /// </summary>
    public void CopyTo(Stream target, byte[] scratch)
    {
        if (_data is not null)
        {
            target.Write(_data, 0, _data.Length);
            return;
        }

        _source!.CopyTo(_offset, Length, target, scratch);
    }

    /// <summary>The entry as a writer blob, without materializing the payload.</summary>
    public PodBlob ToBlob() => _data is not null
        ? new PodBlob(Name, _data, RawNameField, EmbeddedPaletteName, Timestamp)
        : new PodBlob(Name, _source!, _offset, Length, RawNameField, EmbeddedPaletteName, Timestamp);
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
///
/// <para>Everything a refresh needs per entry, its split path, its base name and its
/// description, is computed once into <see cref="EnsureCaches"/> arrays and reused.
/// These run on every keystroke in the filter box, and the descriptions in particular
/// used to be recomputed inside the sort comparator, so an archive of a few thousand
/// entries recomputed them tens of thousands of times per sort.</para>
/// </summary>
public sealed class EntryBrowser
{
    /// <summary>Column indices, matching the Name / Size / Description column order.</summary>
    public const int NameColumn = 0;
    public const int SizeColumn = 1;
    public const int DescriptionColumn = 2;

    private readonly EntryList _entries = new();
    private readonly List<BrowserRow> _rows = [];
    private readonly HashSet<string> _collapsedFolders = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _knownFolders = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _explicitFolders = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Every file index under a folder path, so selecting a folder heading does not rescan.</summary>
    private readonly Dictionary<string, int[]> _folderFiles = new(StringComparer.OrdinalIgnoreCase);

    private FolderNode? _lastTree;
    private int _cacheVersion = -1;
    private string[][] _pathParts = [];
    private string[] _baseNames = [];
    private string[] _descriptions = [];
    private int[] _viewRowOf = [];

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

    /// <summary>
    /// Total archive size the current entry list would produce on disk.
    ///
    /// <para>Read on every list refresh, so it walks the entries directly. Building a
    /// <see cref="PodBlob"/> per entry to ask the writer, as this used to, normalized
    /// and re-measured every name on every keystroke.</para>
    /// </summary>
    public long ProjectedArchiveSize
    {
        get
        {
            int entrySize = ProjectedFormat == PodFormat.Pod1Extended
                ? PodArchiveWriter.LongNameFieldSize + 8
                : PodArchiveWriter.ClassicNameFieldSize + 8;
            long total = 4 + 80 + ((long)_entries.Count * entrySize);
            foreach (EditableEntry entry in _entries)
            {
                total += entry.Length;
            }

            return total;
        }
    }

    /// <summary>The POD1 layout the current names would require.</summary>
    public PodFormat ProjectedFormat
    {
        get
        {
            foreach (EditableEntry entry in _entries)
            {
                if (PodArchiveWriter.RequiredNameFieldBytes(entry.Name, entry.EmbeddedPaletteName)
                    > PodArchiveWriter.ClassicNameFieldSize)
                {
                    return PodFormat.Pod1Extended;
                }
            }

            return PodFormat.Pod1;
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
        _folderFiles.Clear();
        _lastTree = null;
    }

    /// <summary>Rebuilds <see cref="Rows"/> from the entries, filter and sort state.</summary>
    public void Refresh()
    {
        EnsureCaches();
        FolderNode root = BuildFolderTree();
        EnsureKnownFoldersCollapsed(root);

        string filter = _filter.Trim();
        if (filter.Length > 0)
        {
            MarkMatches(root, filter);
        }

        _rows.Clear();
        AppendRows(root, 0, filter, _rows);

        // Kept so a folder selection can ask what is underneath a heading. Indexed
        // on demand rather than here: most refreshes are filter keystrokes that
        // never select a folder.
        _lastTree = root;
        _folderFiles.Clear();
        IndexViewRows();
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

    /// <summary>
    /// The view row showing an entry, or -1 when it is hidden.
    ///
    /// <para>Answered from an index built by <see cref="Refresh"/>. Reapplying a
    /// selection after a refresh calls this once per selected entry, and scanning the
    /// rows each time made that quadratic in the size of a selected folder.</para>
    /// </summary>
    public int ToViewRow(int sourceIndex) =>
        sourceIndex >= 0 && sourceIndex < _viewRowOf.Length ? _viewRowOf[sourceIndex] : -1;

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

            // The folder tree already knows what is underneath each folder, so this
            // no longer re-normalizes every entry name for every selected heading.
            EnsureFolderFiles();
            if (_folderFiles.TryGetValue(row.FolderPath, out int[]? files))
            {
                foreach (int index in files)
                {
                    indices.Add(index);
                }
            }
        }

        return [.. indices];
    }

    /// <summary>The entries as writer blobs, in archive order, without reading payloads.</summary>
    public IReadOnlyList<PodBlob> ToBlobs()
    {
        List<PodBlob> blobs = new(_entries.Count);
        foreach (EditableEntry entry in _entries)
        {
            blobs.Add(entry.ToBlob());
        }

        return blobs;
    }

    /// <summary>
    /// Marks every folder as seen, collapsing the ones that are new. Expanding or
    /// collapsing before the first <see cref="Refresh"/> would otherwise be undone
    /// by that refresh, which treats an unseen folder as collapsed by default.
    /// </summary>
    private void RegisterFolders()
    {
        EnsureCaches();
        EnsureKnownFoldersCollapsed(BuildFolderTree());
    }

    /// <summary>
    /// Recomputes the per-entry values a refresh needs, but only when the entry list
    /// has actually changed. Filtering and sorting do not change it, so a burst of
    /// keystrokes reuses one set of descriptions.
    /// </summary>
    private void EnsureCaches()
    {
        if (_cacheVersion == _entries.Version)
        {
            return;
        }

        int count = _entries.Count;
        _pathParts = new string[count][];
        _baseNames = new string[count];
        _descriptions = new string[count];
        for (int i = 0; i < count; i++)
        {
            EditableEntry entry = _entries[i];
            string clean = entry.Name.Replace('\0', ' ').Trim();
            _pathParts[i] = clean.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries);
            int slash = Math.Max(clean.LastIndexOf('/'), clean.LastIndexOf('\\'));
            _baseNames[i] = slash >= 0 ? clean.Substring(slash + 1) : clean;
            _descriptions[i] = PodEntryDescriber.Describe(entry.Name, entry.Length);
        }

        _cacheVersion = _entries.Version;
    }

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
            string[] parts = _pathParts[i];
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

    /// <summary>
    /// Stamps each folder with whether it or anything under it matches the filter, in
    /// one pass. Asking that question per folder used to re-walk the whole subtree
    /// underneath it, so a deep tree was walked once per ancestor.
    /// </summary>
    private bool MarkMatches(FolderNode node, string filter)
    {
        bool matched = Contains(node.Name, filter);
        foreach (int fileIndex in node.FileIndices)
        {
            if (Contains(_entries[fileIndex].Name, filter))
            {
                matched = true;
                break;
            }
        }

        foreach (FolderNode child in node.Folders)
        {
            // Not short-circuited: every child still has to be stamped.
            if (MarkMatches(child, filter))
            {
                matched = true;
            }
        }

        node.Matches = matched;
        return matched;
    }

    /// <summary>
    /// Case-insensitive substring test. POD names are ASCII, so folding case up
    /// rather than down, as the previous <c>ToLowerInvariant</c> pair did, cannot
    /// change the answer, and this allocates nothing.
    /// </summary>
    private static bool Contains(string text, string filter) =>
        text.Length > 0 && text.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0;

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
                rows.Add(BrowserRow.File(
                    item.FileIndex,
                    item.FileName!,
                    depth,
                    _entries[item.FileIndex].Length,
                    _descriptions[item.FileIndex]));
            }
        }
    }

    private List<FolderItem> OrderedItems(FolderNode node, string filter)
    {
        List<FolderItem> items = [];
        foreach (FolderNode folder in node.Folders)
        {
            if (filter.Length == 0 || folder.Matches)
            {
                items.Add(new FolderItem(folder, -1, null));
            }
        }

        foreach (int fileIndex in node.FileIndices)
        {
            if (filter.Length == 0 || Contains(_entries[fileIndex].Name, filter))
            {
                items.Add(new FolderItem(null, fileIndex, _baseNames[fileIndex]));
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
            NameColumn => CompareLowerOrdinal(SortName(left), SortName(right)),
            SizeColumn => SortSize(left).CompareTo(SortSize(right)),
            DescriptionColumn => CompareLowerOrdinal(SortDescription(left), SortDescription(right)),
            _ => 0,
        };

        return SortDirection == SortDirection.Ascending ? byValue : -byValue;
    }

    /// <summary>
    /// Ordinal comparison of two strings folded to lower case, which is exactly what
    /// comparing their <c>ToLowerInvariant</c> forms produced, without allocating a
    /// lower-cased copy of both operands for every one of the O(n log n) comparisons
    /// a sort performs.
    /// </summary>
    private static int CompareLowerOrdinal(string left, string right)
    {
        int shared = Math.Min(left.Length, right.Length);
        for (int i = 0; i < shared; i++)
        {
            char a = char.ToLowerInvariant(left[i]);
            char b = char.ToLowerInvariant(right[i]);
            if (a != b)
            {
                return a - b;
            }
        }

        return left.Length - right.Length;
    }

    private static int OrderKey(FolderItem item) =>
        item.Folder is not null ? item.Folder.FirstSourceIndex : item.FileIndex;

    private static int FolderLastFlag(FolderItem item) => item.Folder is not null ? 0 : 1;

    private static string SortName(FolderItem item) =>
        item.Folder is not null ? item.Folder.Name : item.FileName!;

    private long SortSize(FolderItem item) =>
        item.Folder is not null ? 0L : _entries[item.FileIndex].Length;

    private string SortDescription(FolderItem item) =>
        item.Folder is not null ? "Folder" : _descriptions[item.FileIndex];

    /// <summary>
    /// Records every file index under each folder path, the first time a folder
    /// selection asks for one after a refresh.
    /// </summary>
    private void EnsureFolderFiles()
    {
        if (_folderFiles.Count > 0 || _lastTree is null)
        {
            return;
        }

        List<int> collected = [];
        foreach (FolderNode child in _lastTree.Folders)
        {
            CollectFolderFiles(child, collected);
        }
    }

    private void CollectFolderFiles(FolderNode node, List<int> scratch)
    {
        int start = scratch.Count;
        scratch.AddRange(node.FileIndices);
        foreach (FolderNode child in node.Folders)
        {
            CollectFolderFiles(child, scratch);
        }

        int[] own = new int[scratch.Count - start];
        scratch.CopyTo(start, own, 0, own.Length);
        _folderFiles[node.Path] = own;
    }

    /// <summary>Maps each entry index to the view row showing it, or -1 when hidden.</summary>
    private void IndexViewRows()
    {
        if (_viewRowOf.Length != _entries.Count)
        {
            _viewRowOf = new int[_entries.Count];
        }

        for (int i = 0; i < _viewRowOf.Length; i++)
        {
            _viewRowOf[i] = -1;
        }

        for (int i = 0; i < _rows.Count; i++)
        {
            BrowserRow row = _rows[i];
            if (!row.IsFolder && row.SourceIndex >= 0 && row.SourceIndex < _viewRowOf.Length)
            {
                _viewRowOf[row.SourceIndex] = i;
            }
        }
    }

    private static string NormalizeArchivePath(string path) =>
        path.Replace('\\', '/').Replace('\0', ' ').Trim();

    /// <summary>
    /// The entry list, counting its own mutations so the browser's per-entry caches
    /// know when they are stale. The list is handed out for the form to edit
    /// directly, so there is no other place to notice a change.
    /// </summary>
    private sealed class EntryList : IList<EditableEntry>
    {
        private readonly List<EditableEntry> _items = [];

        /// <summary>Bumped by every mutation.</summary>
        public int Version { get; private set; }

        public EditableEntry this[int index]
        {
            get => _items[index];
            set { _items[index] = value; Version++; }
        }

        public int Count => _items.Count;

        public bool IsReadOnly => false;

        public void Add(EditableEntry item) { _items.Add(item); Version++; }

        public void Clear() { _items.Clear(); Version++; }

        public bool Contains(EditableEntry item) => _items.Contains(item);

        public void CopyTo(EditableEntry[] array, int arrayIndex) => _items.CopyTo(array, arrayIndex);

        public IEnumerator<EditableEntry> GetEnumerator() => _items.GetEnumerator();

        public int IndexOf(EditableEntry item) => _items.IndexOf(item);

        public void Insert(int index, EditableEntry item) { _items.Insert(index, item); Version++; }

        public bool Remove(EditableEntry item)
        {
            bool removed = _items.Remove(item);
            if (removed) Version++;
            return removed;
        }

        public void RemoveAt(int index) { _items.RemoveAt(index); Version++; }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    /// <summary>One folder in the tree built from entry names.</summary>
    private sealed class FolderNode(string name, string path, int firstSourceIndex)
    {
        public string Name { get; } = name;

        public string Path { get; } = path;

        public int FirstSourceIndex { get; private set; } = firstSourceIndex;

        /// <summary>Set by <see cref="MarkMatches"/> when a filter is active.</summary>
        public bool Matches { get; set; }

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
