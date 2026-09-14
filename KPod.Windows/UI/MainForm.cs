using System.Globalization;
using KPod.Core.Compat;
using KPod.Core.Manifests;
using KPod.Core.PodIni;
using KPod.Core.Pods;
using KPod.Core.Preferences;
using KPod.Core.Reports;
using KPod.Core.Session;

namespace KPod.Windows.UI;

/// <summary>
/// The main window. The entry list is the single source of truth for the archive
/// being edited: opening a POD fills it, add, remove and replace change it, and
/// nothing reaches disk until Save As.
///
/// <para>The list runs in virtual mode, because Hellbender's GAME.POD alone holds
/// over four thousand entries and a populated ListView is unusable at that size.
/// The rows themselves come from <see cref="EntryBrowser"/>, which owns the folder
/// tree, the filter and the sort.</para>
/// </summary>
internal sealed class MainForm : Form
{
    private static readonly string[] ColumnNames = ["Name", "Size", "Description"];

    /// <summary>
    /// What the status bar says when there is nothing loaded. The counters are hidden
    /// in that state rather than shown as zeroes: an empty list has no size worth
    /// reporting, and the projected size of nothing is the 84-byte POD1 header, which
    /// is arithmetic rather than information.
    /// </summary>
    private const string EmptyStateHint =
        "No archive open. Open a POD file, or drag files in to start a new one.";

    private readonly EntryBrowser _browser = new();
    private readonly PodSession _session = new();

    private AppConfig? _configCache;

    /// <summary>
    /// Stored preferences, read on first use. Nothing needs them until a menu is
    /// opened or a file is, so reading them in the constructor only put a file read
    /// and a parse in front of the first paint.
    /// </summary>
    private AppConfig Config
    {
        get => _configCache ??= ConfigStore.Load();
        set => _configCache = value;
    }
    private PodArchive? _openedArchive;
    /// <summary>
    /// Set when the archive on disk already repeated an entry name. Those archives
    /// predate KPod and must stay savable, so the duplicate rule is relaxed for them
    /// while still applying to anything the user adds.
    /// </summary>
    private bool _openedWithDuplicateNames;

    /// <summary>True when the archive as read already collides two entry names.</summary>
    private static bool HasDuplicateNames(PodArchive archive)
    {
        HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);
        foreach (PodEntry entry in archive.Entries)
        {
            if (!seen.Add(entry.Name)) return true;
        }
        return false;
    }
    private string? _currentArchivePath;
    private PodFormat _outputFormat = PodFormat.Pod1;
    private readonly List<PodAuditEntry> _auditEntries = [];
    private bool _auditEnabled = true;
    private string _auditAuthor = Environment.UserName;
    private bool _dirty;
    private string _archiveComment = string.Empty;
    private bool _suppressCommentEvents;

    /// <summary>
    /// Set while the list's selection is being rebuilt in bulk. Every
    /// <c>SelectedIndices.Add</c> raises SelectedIndexChanged, and answering it
    /// means re-reading the whole selection, so restoring a folder's worth of rows
    /// one at a time would be quadratic. The count is updated once at the end.
    /// </summary>
    private bool _suppressSelectionEvents;

    /// <summary>
    /// Coalesces selection changes. Dragging or shift-clicking across a large range
    /// raises SelectedIndexChanged once per row, and each answer has to re-read the
    /// whole selection and expand any folder heading in it, so recomputing on every
    /// one of them is quadratic in the size of the range being selected.
    /// </summary>
    private readonly System.Windows.Forms.Timer _selectionCountTimer = new() { Interval = 50 };

    private readonly ListView _list = new()
    {
        Dock = DockStyle.Fill,
        View = View.Details,
        FullRowSelect = true,
        MultiSelect = true,
        HideSelection = false,
        VirtualMode = true,
        AllowDrop = true,
    };

    private readonly TextBox _comment = new() { Dock = DockStyle.Fill, MaxLength = 79 };
    private readonly TextBox _filter = new() { Dock = DockStyle.Fill };
    private readonly ToolStripMenuItem _recentMenu = new("Open &Recent");
    private readonly ToolStripStatusLabel _progress = new() { Spring = true, TextAlign = ContentAlignment.MiddleLeft };
    private readonly ToolStripStatusLabel _sizeLabel = new("Size: 0");
    private readonly ToolStripStatusLabel _countLabel = new("Files: 0");
    private readonly ToolStripStatusLabel _selectedLabel = new("Selected: 0");
    private readonly ToolStripStatusLabel _dirtyLabel = new(" ") { ForeColor = Color.Firebrick };
    private readonly StatusStrip _statusStrip = new();
    private readonly ToolStripStatusLabel _activity = new("  ")
    {
        BackColor = Color.FromArgb(0x00, 0xCC, 0x00),
        AutoSize = false,
        Width = 16,
    };

    internal MainForm(string? startupFile)
    {
        Text = Dialogs.AppName;
        Icon = AppIcon.Shared;
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.Dpi;
        Font = SystemFonts.MessageBoxFont!;
        ClientSize = ScreenFit.Cap(900, 640);
        MinimumSize = new Size(640, 420);

        BuildList();
        Controls.Add(BuildCentre());
        Controls.Add(BuildToolBar());
        Controls.Add(BuildMenu());
        Controls.Add(BuildStatusBar());

        RefreshList();
        _progress.Text = EmptyStateHint;

        if (startupFile is not null)
        {
            Shown += (_, _) => OpenPath(startupFile);
        }
    }

    // -------------------------------------------------------------------------
    // Layout
    // -------------------------------------------------------------------------

    private void BuildList()
    {
        _list.SmallImageList = RowIcons.CreateImageList(16);
        foreach (string name in ColumnNames)
        {
            _list.Columns.Add(name);
        }

        _list.Columns[0].Width = 360;
        _list.Columns[1].Width = 110;
        _list.Columns[1].TextAlign = HorizontalAlignment.Right;
        _list.Columns[2].Width = 260;

        _list.RetrieveVirtualItem += OnRetrieveVirtualItem;
        _selectionCountTimer.Tick += (_, _) =>
        {
            _selectionCountTimer.Stop();
            UpdateSelectedCount();
        };
        _list.SelectedIndexChanged += (_, _) =>
        {
            if (_suppressSelectionEvents)
            {
                return;
            }

            // Restarting the timer means a run of changes settles into one recompute.
            _selectionCountTimer.Stop();
            _selectionCountTimer.Start();
        };
        _list.ColumnClick += (_, e) =>
        {
            _browser.CycleSort(e.Column);
            UpdateColumnHeaders();
            RefreshList();
        };
        _list.MouseDoubleClick += (_, e) => ActivateRow(_list.HitTest(e.Location).Item?.Index ?? -1);
        _list.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter && _list.SelectedIndices.Count > 0)
            {
                ActivateRow(_list.SelectedIndices[0]);
                e.Handled = true;
            }
        };
        _list.MouseUp += (_, e) =>
        {
            if (e.Button == MouseButtons.Right)
            {
                ShowContextMenu(e.Location);
            }
        };

        const string entryDragFormat = "KPod.EntryIndices";
        _list.ItemDrag += (_, _) =>
        {
            int[] indices = SelectedSourceIndices();
            if (indices.Length > 0)
            {
                _list.DoDragDrop(new DataObject(entryDragFormat,
                    new EntryDragPayload(indices, SelectedFolderRoot())), DragDropEffects.Move);
            }
        };

        // Files dropped on the list are appended as new entries.
        _list.DragEnter += (_, e) =>
            e.Effect = e.Data?.GetDataPresent(DataFormats.FileDrop) == true
                ? DragDropEffects.Copy
                : e.Data?.GetDataPresent(entryDragFormat) == true
                    ? DragDropEffects.Move : DragDropEffects.None;
        _list.DragOver += (_, e) =>
        {
            if (e.Data?.GetDataPresent(entryDragFormat) == true) e.Effect = DragDropEffects.Move;
        };
        _list.DragDrop += (_, e) =>
        {
            if (e.Data?.GetData(DataFormats.FileDrop) is string[] paths)
            {
                AddFiles(paths);
            }
            else if (e.Data?.GetData(entryDragFormat) is EntryDragPayload payload)
            {
                Point client = _list.PointToClient(new Point(e.X, e.Y));
                int rowIndex = _list.HitTest(client).Item?.Index ?? -1;
                string destination = string.Empty;
                if (rowIndex >= 0 && rowIndex < _browser.Rows.Count)
                {
                    BrowserRow row = _browser.Rows[rowIndex];
                    if (row.IsFolder) destination = row.FolderPath.Replace('/', '\\');
                    else
                    {
                        string name = _browser.Entries[row.SourceIndex].Name;
                        int slash = Math.Max(name.LastIndexOf('/'), name.LastIndexOf('\\'));
                        destination = slash >= 0 ? name.Substring(0, slash) : string.Empty;
                    }
                }
                MoveEntries(payload.Indices, destination, payload.SourceFolder);
            }
        };
    }

    private Control BuildCentre()
    {
        _comment.LostFocus += (_, _) => _archiveComment = _comment.Text;
        _comment.TextChanged += (_, _) =>
        {
            if (_suppressCommentEvents)
            {
                return;
            }

            _archiveComment = _comment.Text;
            MarkDirty();
        };

        _filter.TextChanged += (_, _) =>
        {
            _browser.Filter = _filter.Text;
            RefreshList();
        };

        Button advanced = new()
        {
            Text = "Advanced",
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(8, 2, 0, 0),
        };
        advanced.Click += (_, _) => OnSearch();

        // One grid for both rows rather than a nested panel each. The nested
        // layout sized its cells from the captions but never painted them, and
        // Dock inside a TableLayoutPanel cell fights the cell's own sizing.
        // A single grid also lines the two captions up with each other.
        TableLayoutPanel layout = new()
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 3,
            Padding = new Padding(6, 4, 6, 4),
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        layout.Controls.Add(Caption("Comment:"), 0, 0);
        layout.Controls.Add(_comment, 1, 0);
        layout.SetColumnSpan(_comment, 2);

        layout.Controls.Add(Caption("File filter:"), 0, 1);
        layout.Controls.Add(_filter, 1, 1);
        layout.Controls.Add(advanced, 2, 1);

        layout.Controls.Add(_list, 0, 2);
        layout.SetColumnSpan(_list, 3);

        _comment.AccessibleDescription = "POD archive comment, up to 80 characters, written on Save As";
        return layout;
    }

    /// <summary>
    /// The toolbar: the actions worth a label carry one, the rest are icon-only with
    /// a tooltip, and the ones that come in families sit behind a split button whose
    /// face runs the most common of them.
    ///
    /// <para>Every action here is also in the menus, so nothing is only reachable
    /// through a dropdown.</para>
    /// </summary>
    private ToolStrip BuildToolBar()
    {
        // 16px at 100%, scaled for the monitor so the glyphs stay sharp rather than
        // being stretched by the ToolStrip.
        int side = (int)Math.Round(16 * (DeviceDpi / 96.0));
        ToolStrip bar = new()
        {
            GripStyle = ToolStripGripStyle.Hidden,
            ImageScalingSize = new Size(side, side),
        };

        bar.Items.Add(SplitButton("New", "Start a new archive", ToolbarIcons.New, side, OnNew,
            ("&New Archive", OnNew),
            ("Open Response &List File...", OnOpenManifest)));
        bar.Items.Add(BuildOpenButton(side));
        bar.Items.Add(LabelledButton("Save", "Save over the open archive", ToolbarIcons.Save, side, OnSave));
        bar.Items.Add(LabelledButton("Save As", "Write the entry list to a new archive",
            ToolbarIcons.SaveAs, side, OnSaveAs));
        bar.Items.Add(new ToolStripSeparator());

        bar.Items.Add(SplitButton("Add", "Add files to the archive", ToolbarIcons.AddFiles, side, OnAddFiles,
            ("Add &Files...", OnAddFiles),
            ("Add F&older...", OnAddFolder),
            ("&Create Folder...", OnCreateFolder)));
        bar.Items.Add(SplitButton("Extract", "Extract the selected entries", ToolbarIcons.Extract,
            side, OnExtractSelected,
            ("Extract &Selected...", OnExtractSelected),
            ("Extract &All...", OnExtractAll)));
        bar.Items.Add(IconButton("Remove", "Remove the selected entries", ToolbarIcons.Remove,
            side, OnRemoveSelected));
        bar.Items.Add(new ToolStripSeparator());

        bar.Items.Add(IconButton("Expand All", "Open every folder", ToolbarIcons.Expand, side,
            () => WithBusy("Expanding folders...", _browser.ExpandAllFolders)));
        bar.Items.Add(IconButton("Collapse All", "Close every folder", ToolbarIcons.Collapse, side,
            () => WithBusy("Collapsing folders...", _browser.CollapseAllFolders)));
        bar.Items.Add(IconButton("Search", "Find entries by name or size", ToolbarIcons.Search, side, OnSearch));

        // Right-aligned items are laid out from the right edge inwards.
        ToolStripButton about = IconButton("About", "About KPod", ToolbarIcons.About, side, OnAbout);
        about.Alignment = ToolStripItemAlignment.Right;
        bar.Items.Add(about);
        return bar;
    }

    /// <summary>
    /// Open, with the recent-file list hanging off its arrow. The entries are built
    /// each time the dropdown opens rather than kept in step with the File menu's
    /// copy, because a ToolStripItem can only belong to one parent.
    /// </summary>
    private ToolStripSplitButton BuildOpenButton(int side)
    {
        ToolStripSplitButton open = SplitButton("Open", "Open a POD or EPD archive",
            ToolbarIcons.Open, side, OnOpen);
        open.DropDownOpening += (_, _) =>
        {
            open.DropDownItems.Clear();
            open.DropDownItems.Add(MenuItem("&Open POD...", OnOpen));
            open.DropDownItems.Add(MenuItem("Open Response &List File...", OnOpenManifest));
            open.DropDownItems.Add(new ToolStripSeparator());
            if (Config.RecentOpenedFiles.Count == 0)
            {
                open.DropDownItems.Add(new ToolStripMenuItem("(no recent files)") { Enabled = false });
                return;
            }

            foreach (string path in Config.RecentOpenedFiles)
            {
                string capture = path;
                ToolStripMenuItem item = new(Path.GetFileName(capture)) { ToolTipText = capture };
                item.Click += (_, _) => OpenRecentFile(capture);
                open.DropDownItems.Add(item);
            }
        };
        return open;
    }

    private MenuStrip BuildMenu()
    {
        ToolStripMenuItem file = new("&File");
        file.DropDownItems.Add(MenuItem("&Open POD...", OnOpen));
        file.DropDownItems.Add(MenuItem("&New Archive", OnNew));
        file.DropDownItems.Add(MenuItem("Open Response &List File...", OnOpenManifest));
        _recentMenu.DropDownOpening += (_, _) => RefreshRecentMenu();
        file.DropDownItems.Add(_recentMenu);
        file.DropDownItems.Add(new ToolStripSeparator());
        file.DropDownItems.Add(MenuItem("&Add Files...", OnAddFiles));
        file.DropDownItems.Add(MenuItem("Add &Folder...", OnAddFolder));
        file.DropDownItems.Add(MenuItem("&Create Folder...", OnCreateFolder));
        file.DropDownItems.Add(MenuItem("&Remove Selected", OnRemoveSelected));
        file.DropDownItems.Add(MenuItem("&Save", OnSave));
        file.DropDownItems.Add(MenuItem("Save &As...", OnSaveAs));
        file.DropDownItems.Add(new ToolStripSeparator());
        file.DropDownItems.Add(MenuItem("&Extract All...", OnExtractAll));
        file.DropDownItems.Add(MenuItem("Extract &Selected...", OnExtractSelected));
        file.DropDownItems.Add(new ToolStripSeparator());
        file.DropDownItems.Add(MenuItem("Save .&inf Report...", OnExportInfo));
        file.DropDownItems.Add(MenuItem("Save .l&st List...", OnExportList));
        file.DropDownItems.Add(new ToolStripSeparator());
        file.DropDownItems.Add(MenuItem("E&xit", Close));

        ToolStripMenuItem tools = new("&Tools");
        tools.DropDownItems.Add(MenuItem("&Mount in pod.ini...", OnMount));
        tools.DropDownItems.Add(MenuItem("&Search...", OnSearch));
        tools.DropDownItems.Add(MenuItem("&Validate Archive...", OnValidate));
        ToolStripMenuItem audit = new("Record POD2 audit history") { Checked = _auditEnabled, CheckOnClick = true };
        audit.CheckedChanged += (_, _) => _auditEnabled = audit.Checked;
        tools.DropDownItems.Add(audit);
        tools.DropDownItems.Add(MenuItem("Set Audit &Author...", OnSetAuditAuthor));
        tools.DropDownItems.Add(MenuItem("View POD2 Audit &History...", OnViewAuditHistory));

        ToolStripMenuItem help = new("&Help");
        help.DropDownItems.Add(MenuItem("&About " + Dialogs.AppName + "...", OnAbout));

        MenuStrip menu = new();
        menu.Items.Add(file);
        menu.Items.Add(tools);
        menu.Items.Add(help);
        MainMenuStrip = menu;
        return menu;
    }

    private StatusStrip BuildStatusBar()
    {
        StatusStrip strip = _statusStrip;
        strip.Items.Add(_sizeLabel);
        strip.Items.Add(_countLabel);
        strip.Items.Add(_selectedLabel);
        strip.Items.Add(_dirtyLabel);
        strip.Items.Add(_progress);
        strip.Items.Add(_activity);
        return strip;
    }

    // -------------------------------------------------------------------------
    // List plumbing
    // -------------------------------------------------------------------------

    private void OnRetrieveVirtualItem(object? sender, RetrieveVirtualItemEventArgs e)
    {
        if (e.ItemIndex < 0 || e.ItemIndex >= _browser.Rows.Count)
        {
            e.Item = new ListViewItem(string.Empty);
            return;
        }

        BrowserRow row = _browser.Rows[e.ItemIndex];
        ListViewItem item = new(row.DisplayName)
        {
            // IndentCount indents by one small-image width per level, which is
            // exactly the tree indentation the Swing renderer drew by hand.
            IndentCount = row.Depth,
            ImageIndex = row.IsFolder ? RowIcons.FolderIndex : RowIcons.FileIndex,
        };
        item.SubItems.Add(row.SizeText);
        item.SubItems.Add(row.Description);
        e.Item = item;
    }

    /// <summary>
    /// Rebuilds the visible rows, keeping the same entries selected. Virtual mode
    /// drops the selection whenever the row count changes, so it is captured by
    /// entry index first and reapplied afterwards.
    /// </summary>
    /// <param name="preserveSelection">
    /// False when the caller sets the selection itself straight afterwards, so the
    /// rows are not re-selected once only to be replaced. Expanding a folder is the
    /// case that matters: its heading stands for every entry under it, so preserving
    /// that selection would re-select a thousand rows and then discard them.
    /// </param>
    private void RefreshList(bool preserveSelection = true)
    {
        int[] selected = preserveSelection
            ? _browser.SelectedSourceIndices(_list.SelectedIndices.Cast<int>())
            : [];

        _browser.Refresh();
        _list.BeginUpdate();
        _suppressSelectionEvents = true;
        try
        {
            // Clear before resizing: a selected index past the new end throws.
            _list.SelectedIndices.Clear();
            _list.VirtualListSize = _browser.Rows.Count;
            foreach (int sourceIndex in selected)
            {
                int viewRow = _browser.ToViewRow(sourceIndex);
                if (viewRow >= 0)
                {
                    _list.SelectedIndices.Add(viewRow);
                }
            }
        }
        finally
        {
            _suppressSelectionEvents = false;
            _list.EndUpdate();
        }

        _list.Invalidate();

        bool empty = _browser.Entries.Count == 0;
        _countLabel.Text = empty ? string.Empty : "Files: " + _browser.Entries.Count;
        _sizeLabel.Text = empty
            ? string.Empty
            : "Size: " + _browser.ProjectedArchiveSize.ToString("N0", CultureInfo.CurrentCulture);
        _selectionCountTimer.Stop();
        UpdateSelectedCount();
        UpdateDirtyLabel();
    }

    private void UpdateColumnHeaders()
    {
        for (int i = 0; i < ColumnNames.Length; i++)
        {
            _list.Columns[i].Text = _browser.HeaderText(i, ColumnNames[i]);
        }
    }

    private void UpdateSelectedCount() =>
        _selectedLabel.Text = _browser.Entries.Count == 0
            ? string.Empty
            : "Selected: " + SelectedSourceIndices().Length;

    private int[] SelectedSourceIndices() =>
        _browser.SelectedSourceIndices(_list.SelectedIndices.Cast<int>());

    private void ActivateRow(int viewRow)
    {
        if (viewRow < 0 || viewRow >= _browser.Rows.Count)
        {
            return;
        }

        if (_browser.Rows[viewRow].IsFolder)
        {
            // The heading keeps the selection, so there is no point restoring the
            // rows it stands for: the folder row itself is reselected below, and on a
            // folder of a thousand files restoring them first was the whole cost of
            // expanding it.
            _browser.ToggleFolder(viewRow);
            RefreshList(preserveSelection: false);
            if (viewRow < _list.VirtualListSize)
            {
                _list.SelectedIndices.Add(viewRow);
            }
        }
        else
        {
            OnPreview();
        }
    }

    private void ShowContextMenu(Point location)
    {
        ListViewHitTestInfo hit = _list.HitTest(location);
        int viewRow = hit.Item?.Index ?? -1;
        if (viewRow >= 0 && !_list.SelectedIndices.Contains(viewRow))
        {
            _list.SelectedIndices.Clear();
            _list.SelectedIndices.Add(viewRow);
        }

        BrowserRow? row = viewRow >= 0 && viewRow < _browser.Rows.Count ? _browser.Rows[viewRow] : null;
        ContextMenuStrip menu = new();
        int[] selection = SelectedSourceIndices();
        bool anyEnabled = selection.Any(index => PodEntryDisabling.CanDisable(_browser.Entries[index].Name));
        bool anyDisabled = selection.Any(index => PodEntryDisabling.CanEnable(_browser.Entries[index].Name));
        if (row is not null && row.IsFolder)
        {
            menu.Items.Add(MenuItem(row.Collapsed ? "Expand" : "Collapse", () => ActivateRow(viewRow)));
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(MenuItem("Rename Folder...", () => OnRename(viewRow)));
            menu.Items.Add(MenuItem("Move To Folder...", OnMoveSelected));
            menu.Items.Add(MenuItem("Remove", OnRemoveSelected));
            AddDisablingItems(menu, anyEnabled, anyDisabled);
            menu.Items.Add(MenuItem("Extract Selected", OnExtractSelected));
        }
        else
        {
            menu.Items.Add(MenuItem("Preview", OnPreview));
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(MenuItem("Rename...", () => OnRename(viewRow)));
            menu.Items.Add(MenuItem("Move To Folder...", OnMoveSelected));
            menu.Items.Add(MenuItem("Replace with File...", OnReplaceEntry));
            menu.Items.Add(MenuItem("Remove", OnRemoveSelected));
            AddDisablingItems(menu, anyEnabled, anyDisabled);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(MenuItem("Extract Selected", OnExtractSelected));
        }

        menu.Show(_list, location);
    }

    /// <summary>
    /// Offers the CommPatch 26 disable and enable renames, but only for a selection
    /// that has something to rename. A pod of art has neither item.
    /// </summary>
    private void AddDisablingItems(ContextMenuStrip menu, bool anyEnabled, bool anyDisabled)
    {
        if (!anyEnabled && !anyDisabled) return;
        menu.Items.Add(new ToolStripSeparator());
        if (anyEnabled) menu.Items.Add(MenuItem("Disable in Game", () => ApplyDisabling(disable: true)));
        if (anyDisabled) menu.Items.Add(MenuItem("Enable in Game", () => ApplyDisabling(disable: false)));
    }

    // -------------------------------------------------------------------------
    // Open, new, manifest
    // -------------------------------------------------------------------------

    private void OnOpen()
    {
        if (!ConfirmDiscardChanges())
        {
            return;
        }

        using OpenFileDialog dialog = new()
        {
            Title = "Open POD Archive",
            Filter = "POD Archive files (*.pod;*.epd)|*.pod;*.epd|All files (*.*)|*.*",
            InitialDirectory = RecentFolder() ?? string.Empty,
        };
        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            OpenPodPath(dialog.FileName);
        }
    }

    private void OnNew()
    {
        if (!ConfirmDiscardChanges())
        {
            return;
        }

        _browser.Clear();
        SetComment(string.Empty);
        CloseOpenedArchive();
        _openedWithDuplicateNames = false;
        _currentArchivePath = null;
        _outputFormat = PodFormat.Pod1;
        _auditEntries.Clear();
        _dirty = false;
        _session.Reset();
        RefreshList();
        Text = Dialogs.AppName + " - New Archive";
        _progress.Text = "New archive. Add files or drag them in, then use Save As.";
    }

    private void OnOpenManifest()
    {
        if (!ConfirmDiscardChanges())
        {
            return;
        }

        using OpenFileDialog dialog = new()
        {
            Title = "Open Response File (.lst, .rsp)",
            Filter = "Response files (*.lst;*.rsp)|*.lst;*.rsp|All files (*.*)|*.*",
            InitialDirectory = RecentFolder() ?? string.Empty,
        };
        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            OpenManifestPath(dialog.FileName);
        }
    }

    /// <summary>Opens whichever of the two supported file types the path names.</summary>
    private void OpenPath(string path)
    {
        if (path.EndsWith(".lst", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith(".rsp", StringComparison.OrdinalIgnoreCase))
        {
            OpenManifestPath(path);
        }
        else
        {
            OpenPodPath(path);
        }
    }

    private async void OpenPodPath(string path)
    {
        _session.SourceFolderPath = Path.GetDirectoryName(path);
        _session.SourceFileName = Path.GetFileName(path);
        SetBusy(true, "Opening " + Path.GetFileName(path) + "...");
        try
        {
            PodArchive archive = await Task.Run(() => PodArchiveReader.Read(path));
            CloseOpenedArchive();
            _openedArchive = archive;
            _openedWithDuplicateNames = HasDuplicateNames(archive);
            _currentArchivePath = path;
            _outputFormat = archive.Format == PodFormat.Pod2 ? PodFormat.Pod2 : PodFormat.Pod1;
            _auditEntries.Clear();
            _auditEntries.AddRange(archive.AuditEntries);
            _session.OpenArchive = archive;
            _session.ArchiveByteSize = archive.Data.Length;
            SetComment(archive.Comment);
            _session.ArchiveComment = archive.Comment;

            _browser.Clear();
            LoadEntriesFrom(archive);

            _dirty = false;
            RefreshList();
            RememberOpenedFile(path);
            Text = Dialogs.AppName + " - " + Path.GetFileName(path) + " (" + archive.FormatDisplayName + ")";
            _progress.Text = archive.Entries.Count + " entries loaded.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or OverflowException)
        {
            Dialogs.Error(this, "Failed to open archive", ex);
        }
        finally
        {
            SetBusy(false);
        }
    }

    /// <summary>
    /// Fills the entry list from an open archive, referencing its payloads rather
    /// than copying them. Browsing, sorting and filtering need only names and
    /// lengths, so the bytes stay on disk until a preview, an extract or a save
    /// actually asks for them.
    /// </summary>
    private void LoadEntriesFrom(PodArchive archive)
    {
        foreach (PodEntry entry in archive.Entries)
        {
            _browser.Entries.Add(new EditableEntry(
                entry.Name, archive.Data, entry.Offset, checked((int)entry.Length))
            {
                RawNameField = entry.RawNameField,
                EmbeddedPaletteName = entry.EmbeddedPaletteName,
                Timestamp = entry.Timestamp,
            });
        }
    }

    /// <summary>
    /// Releases the open archive's file handle. The entry list references that
    /// archive's bytes, so nothing may read an entry's payload after this until the
    /// list is rebuilt.
    /// </summary>
    private void CloseOpenedArchive()
    {
        _openedArchive?.Dispose();
        _openedArchive = null;
        _session.OpenArchive = null;
    }

    /// <summary>
    /// Points the entry list at an archive on disk, dropping whatever it referenced
    /// before. Used after a save so the editor reads from the file it just wrote
    /// rather than the one that file replaced.
    /// </summary>
    private async Task RebindTo(string path)
    {
        PodArchive archive = await Task.Run(() => PodArchiveReader.Read(path));
        CloseOpenedArchive();
        _openedArchive = archive;
        _session.OpenArchive = archive;
        _openedWithDuplicateNames = HasDuplicateNames(archive);
        _session.SourceFolderPath = Path.GetDirectoryName(path);
        _session.SourceFileName = Path.GetFileName(path);
        _session.ArchiveByteSize = archive.Data.Length;

        // Only the entries are replaced: the collapse state and the selection belong
        // to the view and should survive a save.
        _browser.Entries.Clear();
        LoadEntriesFrom(archive);
        RefreshList();
    }

    private async void OpenManifestPath(string manifestPath)
    {
        string sourceFolder = Path.GetDirectoryName(manifestPath) ?? ".";
        SetBusy(true, "Reading " + Path.GetFileName(manifestPath) + "...");
        try
        {
            PodManifestParser.Manifest manifest =
                await Task.Run(() => PodManifestParser.ParseManifest(manifestPath, sourceFolder));
            IReadOnlyList<PodBlob> blobs = manifest.Blobs;

            _browser.Clear();
            foreach (PodBlob blob in blobs)
            {
                _browser.Entries.Add(new EditableEntry(blob.Name, blob.Data)
                {
                    RawNameField = blob.RawNameField,
                    EmbeddedPaletteName = blob.EmbeddedPaletteName,
                    Timestamp = blob.Timestamp,
                });
            }

            SetComment(manifest.VolumeName);
            CloseOpenedArchive();
            _currentArchivePath = null;
            _outputFormat = PodFormat.Pod1;
            _auditEntries.Clear();
            _session.OpenArchive = null;
            _session.SourceFolderPath = sourceFolder;
            _session.TargetFileName = manifest.PodFileName;
            _dirty = true;
            RefreshList();
            RememberOpenedFile(manifestPath);
            Text = Dialogs.AppName + " - " + Path.GetFileName(manifestPath) + " (manifest)";
            _progress.Text = blobs.Count + " entries loaded from manifest.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            Dialogs.Error(this, "Failed to load manifest", ex);
        }
        finally
        {
            SetBusy(false);
        }
    }

    // -------------------------------------------------------------------------
    // Editing
    // -------------------------------------------------------------------------

    private void OnAddFiles()
    {
        using OpenFileDialog dialog = new()
        {
            Title = "Add Files to Archive",
            Multiselect = true,
            Filter = "All files (*.*)|*.*",
            InitialDirectory = _session.SourceFolderPath ?? string.Empty,
        };
        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            AddFiles(dialog.FileNames);
        }
    }

    private void OnAddFolder()
    {
        string? folder = FolderPicker.Choose(this, "Add Folder to Archive", RecentFolder());
        if (folder is null) return;
        AddFiles(Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories),
            Path.GetDirectoryName(folder));
    }

    private void OnCreateFolder()
    {
        string parent = SelectedFolderPath();
        string? name = Prompt("Folder name:", string.Empty, "Create Folder");
        if (string.IsNullOrWhiteSpace(name)) return;
        string path = parent.Length == 0 ? name.Trim() : parent + "\\" + name.Trim();
        try
        {
            PodArchiveWriter.ValidatePath(path);
            _browser.ExplicitFolders.Add(path.Replace('\\', '/'));
            MarkDirty(); RefreshList();
        }
        catch (PodFormatException ex) { Dialogs.Warn(this, ex.Message); }
    }

    private void AddFiles(IEnumerable<string> paths, string? relativeRoot = null)
    {
        int added = 0;
        foreach (string path in paths)
        {
            if (!File.Exists(path))
            {
                continue;
            }

            try
            {
                byte[] data = File.ReadAllBytes(path);
                string name = relativeRoot is null ? Path.GetFileName(path)
                    : PathCompat.GetRelativePath(relativeRoot, path);
                name = PodArchiveWriter.NormalizeName(name);
                int existingIndex = FindEntryIndex(name);
                uint timestamp = unchecked((uint)new DateTimeOffset(File.GetLastWriteTimeUtc(path)).ToUnixTimeSeconds());
                EditableEntry addedEntry = new(name, data) { Timestamp = timestamp };
                if (existingIndex >= 0)
                {
                    if (!Dialogs.Confirm(this, "Entry already exists: " + name + "\nReplace it?")) continue;
                    EditableEntry old = _browser.Entries[existingIndex];
                    _browser.Entries[existingIndex] = addedEntry;
                    AddAudit(PodAuditAction.Change, name, old.Timestamp, (uint)old.Length,
                        timestamp, (uint)data.Length);
                }
                else
                {
                    _browser.Entries.Add(addedEntry);
                    AddAudit(PodAuditAction.Add, name, 0, 0, timestamp, (uint)data.Length);
                }
                added++;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Dialogs.Warn(this, "Could not read: " + Path.GetFileName(path) + "\n" + ex.Message);
            }
        }

        if (added > 0)
        {
            MarkDirty();
            RefreshList();
            _progress.Text = added + (added == 1 ? " file added." : " files added.");
        }
    }

    private void OnRemoveSelected()
    {
        int[] indices = SelectedSourceIndices();
        if (indices.Length == 0)
        {
            Dialogs.Warn(this, "Select entries to remove.");
            return;
        }

        // Remove from the back so the earlier indices stay valid.
        for (int i = indices.Length - 1; i >= 0; i--)
        {
            EditableEntry removed = _browser.Entries[indices[i]];
            AddAudit(PodAuditAction.Remove, removed.Name, removed.Timestamp,
                (uint)removed.Length, 0, 0);
            _browser.Entries.RemoveAt(indices[i]);
        }

        MarkDirty();
        RefreshList();
        _progress.Text = indices.Length + (indices.Length == 1 ? " entry removed." : " entries removed.");
    }

    private void OnReplaceEntry()
    {
        int[] indices = SelectedSourceIndices();
        if (indices.Length != 1)
        {
            Dialogs.Warn(this, "Select exactly one file to replace.");
            return;
        }

        int index = indices[0];
        EditableEntry existing = _browser.Entries[index];
        using OpenFileDialog dialog = new()
        {
            Title = "Replace '" + existing.Name + "' with...",
            Filter = "All files (*.*)|*.*",
        };
        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        try
        {
            byte[] data = File.ReadAllBytes(dialog.FileName);

            // Replace swaps the payload and keeps the archive name, so the
            // original directory field still describes this entry.
            _browser.Entries[index] = new EditableEntry(existing.Name, data)
            {
                RawNameField = existing.RawNameField,
                EmbeddedPaletteName = existing.EmbeddedPaletteName,
                Timestamp = unchecked((uint)new DateTimeOffset(File.GetLastWriteTimeUtc(dialog.FileName)).ToUnixTimeSeconds()),
            };
            EditableEntry replacement = _browser.Entries[index];
            AddAudit(PodAuditAction.Change, existing.Name, existing.Timestamp,
                (uint)existing.Length, replacement.Timestamp, (uint)data.Length);
            MarkDirty();
            RefreshList();
            SelectEntry(index);
            _progress.Text = "Entry '" + existing.Name + "' replaced (" + data.Length + " bytes).";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Dialogs.Error(this, "Replace failed", ex);
        }
    }

    private async void OnSaveAs()
    {
        if (_browser.Entries.Count == 0)
        {
            Dialogs.Warn(this, "Nothing to save - add files first.");
            return;
        }

        PodFormat? requested = ChooseOutputFormat();
        if (requested is null) return;
        string? target = ChooseSaveFile("Save Archive As", "pod");
        if (target is null)
        {
            return;
        }

        await SaveTo(target, requested.Value);
    }

    private async void OnSave()
    {
        if (_currentArchivePath is null) { OnSaveAs(); return; }
        await SaveTo(_currentArchivePath, _outputFormat);
    }

    private async Task SaveTo(string target, PodFormat requested)
    {
        _archiveComment = _comment.Text;
        string comment = _archiveComment;
        IReadOnlyList<PodBlob> blobs = _browser.ToBlobs();
        PodValidationResult validation = PodArchiveValidator.ValidateForSave(comment, blobs, requested);
        if (!validation.IsValid) { ShowValidation(validation); return; }
        if (_openedArchive?.Format == PodFormat.Pod2 && validation.OutputFormat != PodFormat.Pod2
            && !Dialogs.Confirm(this, "Saving as " + DisplayName(validation.OutputFormat)
                + " discards POD2 timestamps and audit history. Continue?")) return;
        if (!Dialogs.Confirm(this, "Write " + DisplayName(validation.OutputFormat) + " to:\n" + target + "?")) return;

        SetBusy(true, "Saving " + Path.GetFileName(target) + "...");
        bool released = false;
        try
        {
            byte[]? commentField = _openedArchive?.IsPod1Family == true ? _openedArchive.RawCommentField : null;
            IReadOnlyList<PodAuditEntry> audits = validation.OutputFormat == PodFormat.Pod2 && _auditEnabled
                ? _auditEntries : [];

            // The blobs stream their payloads out of the archive being read, which may
            // be the very file being written. The handle is released only once the new
            // archive is complete beside the target and everything has been read.
            PodArchive? reading = _openedArchive;
            PodFormat written = await Task.Run(
                () => PodArchiveWriter.Write(target, comment, blobs,
                    new PodWriteOptions(validation.OutputFormat, commentField, audits,
                        _openedWithDuplicateNames),
                    beforeReplace: () => { released = true; reading?.Dispose(); }));
            _dirty = false;
            _currentArchivePath = target;
            _outputFormat = written;
            UpdateDirtyLabel();
            _session.TargetFolderPath = Path.GetDirectoryName(target);
            _session.TargetFileName = Path.GetFileName(target);
            _progress.Text = "Saved: " + Path.GetFileName(target) + " (" + DisplayName(written) + ")";
            Text = Dialogs.AppName + " - " + Path.GetFileName(target);

            // The entry list referenced the archive that was just replaced, and any
            // entry added from disk still held its bytes. Rebinding to what is now on
            // disk fixes both, and costs one directory read.
            await RebindTo(target);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or OverflowException)
        {
            Dialogs.Error(this, "Save failed", ex);

            // The source handle was already released, so the entries point at bytes
            // that are no longer reachable. Leaving them on display would fail on the
            // next preview or extract.
            if (released)
            {
                _openedArchive = null;
                _session.OpenArchive = null;
                _browser.Clear();
                SetComment(string.Empty);
                _currentArchivePath = null;
                RefreshList();
                _progress.Text = "Save failed and the open archive was closed. Reopen it to continue.";
            }
        }
        finally
        {
            SetBusy(false);
        }
    }

    // -------------------------------------------------------------------------
    // Extract
    // -------------------------------------------------------------------------

    private void OnExtractAll()
    {
        if (_browser.Entries.Count == 0)
        {
            ShowNoArchive();
            return;
        }

        string? destination = FolderPicker.Choose(this, "Extract All - Choose Destination", RecentFolder());
        if (destination is null)
        {
            return;
        }

        int[] all = new int[_browser.Entries.Count];
        for (int i = 0; i < all.Length; i++)
        {
            all[i] = i;
        }

        ExtractEntries(all, destination, _session.PreserveExtractFolderStructure);
    }

    private void OnExtractSelected()
    {
        int[] indices = SelectedSourceIndices();
        if (indices.Length == 0)
        {
            Dialogs.Warn(this, "Select at least one entry.");
            return;
        }

        using ExtractOptionsForm dialog = new(_session);
        if (dialog.ShowDialog(this) != DialogResult.OK || _session.TargetFolderPath is null)
        {
            return;
        }

        ExtractEntries(indices, _session.TargetFolderPath, _session.PreserveExtractFolderStructure);
    }

    private async void ExtractEntries(int[] indices, string destinationRoot, bool preserveFolders)
    {
        List<EditableEntry> selection = new(indices.Length);
        foreach (int index in indices)
        {
            selection.Add(_browser.Entries[index]);
        }

        SetBusy(true, "Extracting " + selection.Count
            + (selection.Count == 1 ? " entry..." : " entries..."));
        Progress<int> progress = new(done =>
            _progress.Text = "Extracting " + done + " of " + selection.Count + "...");

        try
        {
            await Task.Run(() =>
            {
                // Streamed through one buffer rather than materialized: the entry
                // list can mix archive-backed and disk-backed entries, and extracting
                // a whole archive should not need the whole archive in memory.
                byte[] scratch = PodDataSource.NewScratch();
                for (int i = 0; i < selection.Count; i++)
                {
                    ((IProgress<int>)progress).Report(i + 1);
                    EditableEntry entry = selection[i];
                    string destination =
                        PodExtractService.ResolveDestination(destinationRoot, entry.Name, preserveFolders);
                    string? parent = Path.GetDirectoryName(destination);
                    if (!string.IsNullOrEmpty(parent))
                    {
                        Directory.CreateDirectory(parent!);
                    }

                    using FileStream target = new(destination, FileMode.Create, FileAccess.Write,
                        FileShare.None, PodDataSource.ScratchSize);
                    entry.CopyTo(target, scratch);
                }
            });
            _progress.Text = "Extraction complete.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Dialogs.Error(this, "Extraction failed", ex);
        }
        finally
        {
            SetBusy(false);
        }
    }

    // -------------------------------------------------------------------------
    // Reports and tools
    // -------------------------------------------------------------------------

    private void OnExportInfo()
    {
        if (_openedArchive is null)
        {
            Dialogs.Warn(this, "Open a POD file first to export an .inf report.");
            return;
        }

        string? output = ChooseSaveFile("Save .inf Report", "inf");
        if (output is null)
        {
            return;
        }

        try
        {
            new PodReportExporter(_session).WriteInfoReport(output);
            _progress.Text = "Report saved: " + Path.GetFileName(output);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Dialogs.Error(this, "Export failed", ex);
        }
    }

    private void OnExportList()
    {
        if (_browser.Entries.Count == 0)
        {
            ShowNoArchive();
            return;
        }

        string? output = ChooseSaveFile("Save .lst List", "lst");
        if (output is null)
        {
            return;
        }

        try
        {
            List<string> names = new(_browser.Entries.Count);
            foreach (EditableEntry entry in _browser.Entries)
            {
                names.Add(entry.Name);
            }

            File.WriteAllLines(output, names);
            _progress.Text = "List saved: " + Path.GetFileName(output);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Dialogs.Error(this, "Export failed", ex);
        }
    }

    private void OnMount()
    {
        if (!_session.IsArchiveOpen || _session.SourceFolderPath is null || _session.SourceFileName is null)
        {
            ShowNoArchive();
            return;
        }

        try
        {
            MountResult result = PodIniMounter.Mount(_session.SourceFileName, _session.SourceFolderPath);
            if (result.RecommendedLimitExceeded)
            {
                Dialogs.Warn(
                    this,
                    "POD mounted successfully.\n"
                    + "Warning: pod.ini now exceeds the recommended 99-entry size.");
            }
            else
            {
                Dialogs.Info(this, "POD mounted successfully.");
            }
        }
        catch (AlreadyMountedException)
        {
            Dialogs.Warn(this, "POD already mounted.");
        }
        catch (PodIniNotFoundException)
        {
            Dialogs.Warn(this, "File POD.INI cannot be located.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Dialogs.Error(this, "Mount failed", ex);
        }
    }

    private void OnSearch()
    {
        if (_browser.Entries.Count == 0)
        {
            ShowNoArchive();
            return;
        }

        // ShowDialog, unlike Show, does not dispose the form on close.
        using SearchForm search = new([.. _browser.Entries], SelectEntry);
        search.ShowDialog(this);
    }

    private void OnValidate()
    {
        PodValidationResult result = _openedArchive is not null && !_dirty
            ? PodArchiveValidator.ValidateOpened(_openedArchive)
            : PodArchiveValidator.ValidateForSave(_comment.Text, _browser.ToBlobs(), _outputFormat);
        ShowValidation(result);
    }

    private void OnSetAuditAuthor()
    {
        string? value = Prompt("POD2 audit author:", _auditAuthor, "Audit Author");
        if (!string.IsNullOrWhiteSpace(value)) _auditAuthor = value.Trim();
    }

    private void OnViewAuditHistory()
    {
        using Form dialog = new()
        {
            Text = "POD2 Audit History",
            Icon = AppIcon.Shared,
            StartPosition = FormStartPosition.CenterParent,
            MinimizeBox = false,
            MaximizeBox = false,
            ShowInTaskbar = false,
            ClientSize = ScreenFit.Cap(860, 420),
        };
        ListView list = new() { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true };
        foreach (string column in new[] { "Time", "User", "Action", "Entry", "Old size", "New size" })
            list.Columns.Add(column, column == "Entry" ? 280 : 100);
        foreach (PodAuditEntry audit in _auditEntries)
        {
            ListViewItem item = new(DateTimeOffset.FromUnixTimeSeconds(audit.Timestamp).ToString("u"));
            item.SubItems.Add(audit.User); item.SubItems.Add(audit.Action.ToString());
            item.SubItems.Add(audit.EntryPath); item.SubItems.Add(audit.OldSize.ToString());
            item.SubItems.Add(audit.NewSize.ToString()); list.Items.Add(item);
        }
        dialog.Controls.Add(list);
        dialog.ShowDialog(this);
    }

    private void OnRename(int viewRow)
    {
        if (viewRow < 0 || viewRow >= _browser.Rows.Count) return;
        BrowserRow row = _browser.Rows[viewRow];
        string oldLabel = row.DisplayName;
        string? replacement = Prompt("New name:", oldLabel, "Rename");
        if (string.IsNullOrWhiteSpace(replacement) || replacement == oldLabel) return;
        replacement = replacement.Trim();
        if (replacement.IndexOfAny(['/', '\\']) >= 0)
        {
            Dialogs.Warn(this, "Enter one file or folder name, not a path.");
            return;
        }

        List<EditableEntry> changed = [.. _browser.Entries];
        List<(EditableEntry Old, string NewName)> renamed = [];
        for (int i = 0; i < changed.Count; i++)
        {
            EditableEntry entry = changed[i];
            string newName;
            if (row.IsFolder)
            {
                string oldPrefix = row.FolderPath.Replace('/', '\\');
                if (!entry.Name.Replace('\\', '/').StartsWith(row.FolderPath + "/",
                    StringComparison.OrdinalIgnoreCase)) continue;
                int slash = oldPrefix.LastIndexOf('\\');
                string parent = slash >= 0 ? oldPrefix.Substring(0, slash + 1) : string.Empty;
                newName = parent + replacement + entry.Name.Substring(oldPrefix.Length);
            }
            else
            {
                if (i != row.SourceIndex) continue;
                int slash = Math.Max(entry.Name.LastIndexOf('/'), entry.Name.LastIndexOf('\\'));
                newName = (slash >= 0 ? entry.Name.Substring(0, slash + 1) : string.Empty) + replacement;
            }
            changed[i] = RenamedEntry(entry, newName);
            renamed.Add((entry, newName));
        }
        if (!ValidateEditedEntries(changed)) return;
        _browser.Entries.Clear();
        foreach (EditableEntry entry in changed) _browser.Entries.Add(entry);
        foreach ((EditableEntry old, string name) in renamed) AddMoveAudit(old.Name, name, old);
        MarkDirty();
        RefreshList();
    }

    /// <summary>
    /// Renames the selected tracks and trucks between the extensions the game reads
    /// and the ones it passes over. The payload is untouched, so a disabled addon
    /// still costs its space in the pod and comes back byte for byte.
    /// </summary>
    private void ApplyDisabling(bool disable)
    {
        int[] indices = SelectedSourceIndices();
        if (indices.Length == 0) return;
        List<EditableEntry> changed = [.. _browser.Entries];
        List<(EditableEntry Old, string NewName)> renamed = [];
        foreach (int index in indices)
        {
            EditableEntry entry = changed[index];
            string? newName = disable
                ? PodEntryDisabling.Disable(entry.Name)
                : PodEntryDisabling.Enable(entry.Name);
            if (newName is null) continue;
            changed[index] = RenamedEntry(entry, newName);
            renamed.Add((entry, newName));
        }
        if (renamed.Count == 0) return;
        if (!ValidateEditedEntries(changed)) return;
        _browser.Entries.Clear();
        foreach (EditableEntry entry in changed) _browser.Entries.Add(entry);
        foreach ((EditableEntry old, string name) in renamed) AddMoveAudit(old.Name, name, old);
        MarkDirty();
        RefreshList();
        // Worth saying every time: the rename is invisible in single player, but the
        // other end of a multiplayer game sees a pod that no longer matches theirs.
        _progress.Text = string.Format(System.Globalization.CultureInfo.CurrentCulture,
            "{0} {1} entr{2}. Changing a track pod's status can trigger the multiplayer Different Version message.",
            disable ? "Disabled" : "Enabled", renamed.Count, renamed.Count == 1 ? "y" : "ies");
    }

    private void OnMoveSelected()
    {
        int[] indices = SelectedSourceIndices();
        if (indices.Length == 0) return;
        string? sourceFolder = SelectedFolderRoot();
        HashSet<string> folderSet = new(StringComparer.OrdinalIgnoreCase) { "(archive root)" };
        foreach (EditableEntry entry in _browser.Entries)
        {
            string normalized = entry.Name.Replace('\\', '/');
            int slash = normalized.LastIndexOf('/');
            while (slash > 0)
            {
                folderSet.Add(normalized.Substring(0, slash));
                slash = normalized.LastIndexOf('/', slash - 1);
            }
        }
        string[] folders = [.. folderSet.OrderBy(x => x, StringComparer.OrdinalIgnoreCase)];
        string? selected = ChooseValue("Destination folder:", "Move Entries", folders, folders[0]);
        if (selected is null) return;
        string destination = selected == "(archive root)" ? string.Empty : selected.Replace('/', '\\');
        MoveEntries(indices, destination, sourceFolder);
    }

    private void MoveEntries(int[] indices, string destination)
        => MoveEntries(indices, destination, null);

    private void MoveEntries(int[] indices, string destination, string? sourceFolder)
    {
        string? sourcePrefix = sourceFolder?.Replace('/', '\\');
        if (sourcePrefix is not null
            && (destination.Equals(sourcePrefix, StringComparison.OrdinalIgnoreCase)
                || destination.StartsWith(sourcePrefix + "\\", StringComparison.OrdinalIgnoreCase)))
        {
            MessageBox.Show(this, "A folder cannot be moved into itself.", Dialogs.AppName,
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        List<EditableEntry> changed = [.. _browser.Entries];
        List<(EditableEntry Old, string NewName)> moved = [];
        foreach (int index in indices)
        {
            EditableEntry entry = changed[index];
            string newName;
            if (sourcePrefix is not null && entry.Name.StartsWith(sourcePrefix + "\\",
                    StringComparison.OrdinalIgnoreCase))
            {
                string targetPrefix = destination.Length == 0 ? ArchiveBaseName(sourcePrefix)
                    : destination + "\\" + ArchiveBaseName(sourcePrefix);
                newName = targetPrefix + entry.Name.Substring(sourcePrefix.Length);
            }
            else
            {
                string baseName = ArchiveBaseName(entry.Name);
                newName = destination.Length == 0 ? baseName : destination + "\\" + baseName;
            }
            changed[index] = RenamedEntry(entry, newName);
            moved.Add((entry, newName));
        }
        if (!ValidateEditedEntries(changed)) return;
        _browser.Entries.Clear();
        foreach (EditableEntry entry in changed) _browser.Entries.Add(entry);
        foreach ((EditableEntry old, string name) in moved) AddMoveAudit(old.Name, name, old);
        MarkDirty();
        RefreshList();
    }

    private string? SelectedFolderRoot()
    {
        if (_list.SelectedIndices.Count != 1) return null;
        int rowIndex = _list.SelectedIndices[0];
        if (rowIndex < 0 || rowIndex >= _browser.Rows.Count) return null;
        BrowserRow row = _browser.Rows[rowIndex];
        return row.IsFolder ? row.FolderPath : null;
    }

    private void OnPreview()
    {
        int[] indices = SelectedSourceIndices();
        if (indices.Length == 0)
        {
            return;
        }

        EditableEntry entry = _browser.Entries[indices[0]];
        PreviewForm.Open(this, entry.Name, entry.Data, _openedArchive, entry.RawNameField);
    }

    private void OnAbout()
    {
        using AboutForm about = new();
        about.ShowDialog(this);
    }

    // -------------------------------------------------------------------------
    // Recent files
    // -------------------------------------------------------------------------

    private void RefreshRecentMenu()
    {
        _recentMenu.DropDownItems.Clear();
        if (Config.RecentOpenedFiles.Count == 0)
        {
            _recentMenu.DropDownItems.Add(new ToolStripMenuItem("(none)") { Enabled = false });
            return;
        }

        foreach (string path in Config.RecentOpenedFiles)
        {
            string capture = path;
            ToolStripMenuItem item = new(Path.GetFileName(capture)) { ToolTipText = capture };
            item.Click += (_, _) => OpenRecentFile(capture);
            _recentMenu.DropDownItems.Add(item);
        }
    }

    private void OpenRecentFile(string path)
    {
        if (!File.Exists(path))
        {
            Dialogs.Warn(this, "File no longer exists:\n" + path);
            Config = Config.WithoutRecentOpenedFile(path);
            SaveConfigQuietly();
            RefreshRecentMenu();
            return;
        }

        if (!ConfirmDiscardChanges())
        {
            return;
        }

        if (path.EndsWith(".pod", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith(".epd", StringComparison.OrdinalIgnoreCase))
        {
            OpenPodPath(path);
            return;
        }

        if (path.EndsWith(".lst", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith(".rsp", StringComparison.OrdinalIgnoreCase))
        {
            OpenManifestPath(path);
            return;
        }

        Dialogs.Warn(this, "Recent file type is not supported anymore:\n" + path);
        Config = Config.WithoutRecentOpenedFile(path);
        SaveConfigQuietly();
        RefreshRecentMenu();
    }

    private void RememberOpenedFile(string path)
    {
        Config = Config.WithRecentOpenedFile(path);
        SaveConfigQuietly();
        RefreshRecentMenu();
    }

    private void SaveConfigQuietly()
    {
        try
        {
            ConfigStore.Save(Config);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _progress.Text = "Could not save recent files config.";
        }
    }

    /// <summary>The folder a file chooser should open in: the archive's, else the newest recent one.</summary>
    private string? RecentFolder()
    {
        if (_session.SourceFolderPath is not null && Directory.Exists(_session.SourceFolderPath))
        {
            return _session.SourceFolderPath;
        }

        foreach (string recent in Config.RecentOpenedFiles)
        {
            string? parent = Directory.Exists(recent) ? recent : Path.GetDirectoryName(recent);
            if (parent is not null && Directory.Exists(parent))
            {
                return parent;
            }
        }

        return null;
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private void SelectEntry(int sourceIndex)
    {
        _browser.ExpandFoldersFor(sourceIndex);
        RefreshList();
        int viewRow = _browser.ToViewRow(sourceIndex);
        if (viewRow < 0)
        {
            return;
        }

        _list.SelectedIndices.Clear();
        _list.SelectedIndices.Add(viewRow);
        _list.EnsureVisible(viewRow);
        _list.Focus();
    }

    private string? ChooseSaveFile(string title, string extension)
    {
        using SaveFileDialog dialog = new()
        {
            Title = title,
            Filter = extension.ToUpperInvariant() + " files (*." + extension + ")|*." + extension
                   + "|All files (*.*)|*.*",
            DefaultExt = extension,
            AddExtension = true,
            InitialDirectory = RecentFolder() ?? string.Empty,
        };
        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return null;
        }

        string path = dialog.FileName;
        return path.EndsWith("." + extension, StringComparison.OrdinalIgnoreCase)
            ? path
            : path + "." + extension;
    }

    private PodFormat? ChooseOutputFormat()
    {
        // EPD is read-only, so POD1 and POD2 are the writable formats.
        string[] labels = ["POD1", "POD2"];
        string initial = DisplayName(_outputFormat);
        string? selected = ChooseValue("Archive format:", "Save Archive As", labels, initial);
        return selected switch
        {
            "POD1" => PodFormat.Pod1,
            "POD2" => PodFormat.Pod2,
            _ => null,
        };
    }

    private static string? ChooseValue(string prompt, string title, string[] values, string initial)
    {
        using Form dialog = new() { Text = title, Icon = AppIcon.Shared,
            StartPosition = FormStartPosition.CenterParent, ShowInTaskbar = false,
            FormBorderStyle = FormBorderStyle.FixedDialog, MinimizeBox = false, MaximizeBox = false,
            ClientSize = new Size(380, 120) };
        Label label = new() { Text = prompt, Left = 12, Top = 14, AutoSize = true };
        ComboBox combo = new() { Left = 12, Top = 38, Width = 350, DropDownStyle = ComboBoxStyle.DropDownList };
        combo.Items.AddRange(values);
        combo.SelectedItem = values.Contains(initial) ? initial : values[0];
        Button ok = new() { Text = "OK", DialogResult = DialogResult.OK, Left = 206, Top = 78, Width = 75 };
        Button cancel = new() { Text = "Cancel", DialogResult = DialogResult.Cancel, Left = 287, Top = 78, Width = 75 };
        dialog.Controls.AddRange([label, combo, ok, cancel]);
        dialog.AcceptButton = ok; dialog.CancelButton = cancel;
        return dialog.ShowDialog() == DialogResult.OK ? combo.SelectedItem?.ToString() : null;
    }

    private static string? Prompt(string prompt, string value, string title)
    {
        using Form dialog = new() { Text = title, Icon = AppIcon.Shared,
            StartPosition = FormStartPosition.CenterParent, ShowInTaskbar = false,
            FormBorderStyle = FormBorderStyle.FixedDialog, MinimizeBox = false, MaximizeBox = false,
            ClientSize = new Size(400, 120) };
        Label label = new() { Text = prompt, Left = 12, Top = 14, AutoSize = true };
        TextBox box = new() { Text = value, Left = 12, Top = 38, Width = 370 };
        Button ok = new() { Text = "OK", DialogResult = DialogResult.OK, Left = 226, Top = 78, Width = 75 };
        Button cancel = new() { Text = "Cancel", DialogResult = DialogResult.Cancel, Left = 307, Top = 78, Width = 75 };
        dialog.Controls.AddRange([label, box, ok, cancel]);
        dialog.AcceptButton = ok; dialog.CancelButton = cancel;
        return dialog.ShowDialog() == DialogResult.OK ? box.Text : null;
    }

    private void ShowValidation(PodValidationResult result)
    {
        List<string> lines = ["Output format: " + DisplayName(result.OutputFormat)];
        lines.AddRange(result.Errors.Select(error => "ERROR: " + error));
        lines.AddRange(result.Warnings.Select(warning => "Warning: " + warning));
        if (lines.Count == 1) lines.Add("No problems found.");
        if (result.IsValid) Dialogs.Info(this, string.Join("\n", lines));
        else Dialogs.Warn(this, string.Join("\n", lines));
    }

    private int FindEntryIndex(string name)
    {
        for (int i = 0; i < _browser.Entries.Count; i++)
            if (string.Equals(_browser.Entries[i].Name, name, StringComparison.OrdinalIgnoreCase)) return i;
        return -1;
    }

    private static string ArchiveBaseName(string name)
    {
        int slash = Math.Max(name.LastIndexOf('/'), name.LastIndexOf('\\'));
        return slash >= 0 ? name.Substring(slash + 1) : name;
    }

    private string SelectedFolderPath()
    {
        if (_list.SelectedIndices.Count == 0) return string.Empty;
        int view = _list.SelectedIndices[0];
        if (view < 0 || view >= _browser.Rows.Count) return string.Empty;
        BrowserRow row = _browser.Rows[view];
        if (row.IsFolder) return row.FolderPath.Replace('/', '\\');
        string name = _browser.Entries[row.SourceIndex].Name;
        int slash = Math.Max(name.LastIndexOf('/'), name.LastIndexOf('\\'));
        return slash >= 0 ? name.Substring(0, slash) : string.Empty;
    }

    /// <summary>
    /// The same entry under a new name. Keeps the payload where it is, so renaming in
    /// a large archive costs nothing beyond the name.
    /// </summary>
    private static EditableEntry RenamedEntry(EditableEntry entry, string newName) =>
        entry.WithName(PodArchiveWriter.NormalizeName(newName));

    private bool ValidateEditedEntries(IReadOnlyList<EditableEntry> entries)
    {
        IReadOnlyList<PodBlob> blobs = entries.Select(e => e.ToBlob()).ToArray();
        PodValidationResult validation = PodArchiveValidator.ValidateForSave(_comment.Text, blobs, _outputFormat);
        if (!validation.IsValid) ShowValidation(validation);
        return validation.IsValid;
    }

    private void AddMoveAudit(string oldName, string newName, EditableEntry entry)
    {
        AddAudit(PodAuditAction.Remove, oldName, entry.Timestamp, (uint)entry.Length, 0, 0);
        AddAudit(PodAuditAction.Add, newName, 0, 0, entry.Timestamp, (uint)entry.Length);
    }

    private void AddAudit(PodAuditAction action, string path, uint oldTimestamp, uint oldSize,
        uint newTimestamp, uint newSize)
    {
        if (!_auditEnabled) return;
        _auditEntries.Add(new PodAuditEntry(_auditAuthor,
            unchecked((uint)DateTimeOffset.UtcNow.ToUnixTimeSeconds()), action, path,
            oldTimestamp, oldSize, newTimestamp, newSize));
    }

    /// <summary>Sets the comment box without the change being read back as an edit.</summary>
    private void SetComment(string comment)
    {
        _archiveComment = comment;
        _suppressCommentEvents = true;
        _comment.Text = comment;
        _suppressCommentEvents = false;
    }

    /// <summary>
    /// Runs a synchronous view change with the activity light on, so a rebuild of the
    /// whole row list says what it is doing rather than looking like a freeze.
    /// </summary>
    private void WithBusy(string message, Action action)
    {
        SetBusy(true, message);
        try
        {
            action();
            RefreshList();
        }
        finally
        {
            SetBusy(false, string.Empty);
        }
    }

    /// <summary>
    /// Turns the activity light red, shows the wait cursor, and says what is running.
    /// </summary>
    /// <param name="message">
    /// What to show in the status bar while busy. Null leaves whatever is there,
    /// which is what the finishing call wants so its result is not overwritten.
    /// </param>
    private void SetBusy(bool busy, string? message = null)
    {
        _activity.BackColor = busy ? Color.FromArgb(0xFF, 0x00, 0x00) : Color.FromArgb(0x00, 0xCC, 0x00);
        _activity.ToolTipText = busy ? "Working" : "Idle";
        UseWaitCursor = busy;
        if (message is not null)
        {
            _progress.Text = message;
        }

        // Without this the status bar only repaints once the operation lets go of the
        // UI thread, which is exactly when the message stops being useful.
        _statusStrip.Refresh();
    }

    private void MarkDirty()
    {
        _dirty = true;
        UpdateDirtyLabel();
    }

    private void UpdateDirtyLabel() => _dirtyLabel.Text = _dirty ? "unsaved" : " ";

    private void ShowNoArchive() => Dialogs.Warn(this, "No archive entries loaded.");

    private bool ConfirmDiscardChanges() =>
        !_dirty || Dialogs.Confirm(this, "You have unsaved changes. Discard them?");

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (!ConfirmDiscardChanges())
        {
            e.Cancel = true;
            return;
        }

        // The open archive holds a file handle for as long as it lives.
        CloseOpenedArchive();
        _selectionCountTimer.Dispose();
        base.OnFormClosing(e);
    }

    private static string DisplayName(PodFormat format) => format switch
    {
        PodFormat.Pod1 => "POD1",
        PodFormat.Pod2 => "POD2",
        PodFormat.Epd => "EPD",
        _ => format.ToString(),
    };

    private static Label Caption(string text) =>
        new()
        {
            Text = text,
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(0, 0, 8, 0),
            TextAlign = ContentAlignment.MiddleLeft,
        };

    /// <summary>A toolbar button showing its icon and its label.</summary>
    private static ToolStripButton LabelledButton(string text, string tooltip, string glyph,
        int side, Action action)
    {
        ToolStripButton button = new(text)
        {
            DisplayStyle = ToolStripItemDisplayStyle.ImageAndText,
            AutoToolTip = false,
            ToolTipText = tooltip,
        };
        ApplyIcon(button, glyph, side);
        button.Click += (_, _) => action();
        return button;
    }

    /// <summary>
    /// A toolbar button showing only its icon. The text is still set, because that is
    /// what screen readers announce, and it is what the button falls back to when the
    /// icon font is missing.
    /// </summary>
    private static ToolStripButton IconButton(string text, string tooltip, string glyph,
        int side, Action action)
    {
        ToolStripButton button = new(text)
        {
            DisplayStyle = ToolStripItemDisplayStyle.Image,
            AutoToolTip = false,
            ToolTipText = tooltip,
        };
        ApplyIcon(button, glyph, side);
        button.Click += (_, _) => action();
        return button;
    }

    /// <summary>
    /// A toolbar button whose face runs <paramref name="defaultAction"/> and whose
    /// arrow opens the rest.
    /// </summary>
    private static ToolStripSplitButton SplitButton(string text, string tooltip, string glyph,
        int side, Action defaultAction, params (string Text, Action Action)[] entries)
    {
        ToolStripSplitButton button = new(text)
        {
            DisplayStyle = ToolStripItemDisplayStyle.ImageAndText,
            AutoToolTip = false,
            ToolTipText = tooltip,
        };
        ApplyIcon(button, glyph, side);
        button.ButtonClick += (_, _) => defaultAction();
        foreach ((string entryText, Action entryAction) in entries)
        {
            button.DropDownItems.Add(MenuItem(entryText, entryAction));
        }

        return button;
    }

    /// <summary>
    /// Gives an item its glyph, or leaves it as a text button when Segoe MDL2 Assets
    /// is not installed, which is the Windows 7 and 8.1 case.
    /// </summary>
    private static void ApplyIcon(ToolStripItem item, string glyph, int side)
    {
        Bitmap? icon = ToolbarIcons.Render(glyph, side);
        if (icon is null)
        {
            item.DisplayStyle = ToolStripItemDisplayStyle.Text;
            return;
        }

        // The ToolStrip does not own item images, but these live for as long as the
        // window does and there are a dozen of them at 16px, so they are left to the
        // finalizer rather than tracked.
        item.Image = icon;
    }

    private static ToolStripMenuItem MenuItem(string text, Action action)
    {
        ToolStripMenuItem item = new(text);
        item.Click += (_, _) => action();
        return item;
    }

    private sealed record EntryDragPayload(int[] Indices, string? SourceFolder);
}
