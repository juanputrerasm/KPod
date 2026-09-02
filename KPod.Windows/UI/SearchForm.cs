using System.Globalization;
using KPod.Core.Session;

namespace KPod.Windows.UI;

/// <summary>
/// Finds entries by name substring or by size, and jumps the main window's list to
/// whichever result the user picks.
/// </summary>
internal sealed class SearchForm : Form
{
    private readonly IReadOnlyList<EditableEntry> _entries;
    private readonly Action<int> _selectEntry;

    private readonly TextBox _query = new() { Dock = DockStyle.Fill };
    private readonly CheckBox _matchCase = new() { Text = "Match case", AutoSize = true };
    private readonly CheckBox _searchNames = new() { Text = "Names", AutoSize = true, Checked = true };
    private readonly CheckBox _searchSizes = new() { Text = "Sizes", AutoSize = true };
    private readonly ListView _results = new()
    {
        Dock = DockStyle.Fill,
        View = View.Details,
        FullRowSelect = true,
        MultiSelect = false,
        HideSelection = false,
    };

    private readonly Label _status = new() { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft };
    private readonly List<int> _matchIndices = [];

    /// <summary>
    /// The query and options the current result list was built from. Find Next
    /// re-runs the search when this no longer matches the form, and otherwise
    /// steps through the results it already has.
    /// </summary>
    private string? _resultsKey;

    internal SearchForm(IReadOnlyList<EditableEntry> entries, Action<int> selectEntry)
    {
        _entries = entries;
        _selectEntry = selectEntry;

        Text = "Search";
        Icon = AppIcon.Shared;
        StartPosition = FormStartPosition.CenterParent;
        ShowInTaskbar = false;
        MinimizeBox = false;
        MaximizeBox = false;
        AutoScaleMode = AutoScaleMode.Dpi;
        Font = SystemFonts.MessageBoxFont!;
        ClientSize = ScreenFit.Cap(540, 380);
        MinimumSize = new Size(460, 320);

        _results.Columns.Add("Name", 300);
        _results.Columns.Add("Size", 100, HorizontalAlignment.Right);
        _results.DoubleClick += (_, _) => JumpToSelected();

        Button findNext = NavButton("Find Next", () => Step(forward: true));
        Button findPrevious = NavButton("Find Previous", () => Step(forward: false));
        Button jump = NavButton("Jump To", JumpToSelected);
        Button close = NavButton("Close", Close);
        close.DialogResult = DialogResult.Cancel;

        TableLayoutPanel queryRow = new() { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 };
        queryRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        queryRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        queryRow.Controls.Add(
            new Label
            {
                Text = "Search for:",
                AutoSize = true,
                Anchor = AnchorStyles.Left,
                Margin = new Padding(0, 0, 8, 0),
                TextAlign = ContentAlignment.MiddleLeft,
            }, 0, 0);
        queryRow.Controls.Add(_query, 1, 0);

        FlowLayoutPanel options = new() { Dock = DockStyle.Fill, WrapContents = false };
        options.Controls.Add(_searchNames);
        options.Controls.Add(_searchSizes);
        options.Controls.Add(_matchCase);

        // A grid rather than a flow panel, so every button takes the width of the
        // widest one instead of shrinking to its own caption.
        TableLayoutPanel buttons = new()
        {
            Dock = DockStyle.Top,
            ColumnCount = 1,
            RowCount = 4,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Margin = new Padding(8, 0, 0, 0),
        };
        buttons.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        foreach (Button button in new[] { findNext, findPrevious, jump, close })
        {
            buttons.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            buttons.Controls.Add(button);
        }

        TableLayoutPanel layout = new()
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 4,
            Padding = new Padding(8),
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
        layout.Controls.Add(queryRow, 0, 0);
        layout.Controls.Add(options, 0, 1);
        layout.Controls.Add(_results, 0, 2);
        layout.Controls.Add(_status, 0, 3);
        layout.Controls.Add(buttons, 1, 0);
        layout.SetRowSpan(buttons, 3);

        Controls.Add(layout);
        AcceptButton = findNext;
        CancelButton = close;
    }

    private static Button NavButton(string text, Action action)
    {
        Button button = new()
        {
            Text = text,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 0, 0, 4),
        };
        button.Click += (_, _) => action();
        return button;
    }

    /// <summary>Identifies the search the current results belong to.</summary>
    private string CurrentKey() =>
        string.Join(
            " ",
            _query.Text,
            _matchCase.Checked,
            _searchNames.Checked,
            _searchSizes.Checked);

    /// <summary>
    /// Runs the search if the query or options changed, then moves one match in
    /// the requested direction, wrapping at either end, and jumps the main
    /// window's list to it.
    /// </summary>
    private void Step(bool forward)
    {
        if (_query.Text.Trim().Length == 0)
        {
            _results.Items.Clear();
            _matchIndices.Clear();
            _resultsKey = null;
            _status.Text = "Enter a search term.";
            return;
        }

        bool rebuilt = false;
        if (_resultsKey != CurrentKey())
        {
            Find();
            rebuilt = true;
        }

        if (_matchIndices.Count == 0)
        {
            return;
        }

        int next;
        if (rebuilt || _results.SelectedIndices.Count == 0)
        {
            // A fresh search starts at the first match going forward, and at the
            // last one going backward.
            next = forward ? 0 : _matchIndices.Count - 1;
        }
        else
        {
            int current = _results.SelectedIndices[0];
            next = forward
                ? (current + 1) % _matchIndices.Count
                : (current - 1 + _matchIndices.Count) % _matchIndices.Count;
        }

        _results.SelectedIndices.Clear();
        _results.SelectedIndices.Add(next);
        _results.EnsureVisible(next);
        _results.Focus();
        _status.Text = "Match " + (next + 1) + " of " + _matchIndices.Count + ".";
        _selectEntry(_matchIndices[next]);
    }

    private void Find()
    {
        _results.BeginUpdate();
        _results.Items.Clear();
        _matchIndices.Clear();

        string query = _query.Text;
        string needle = _matchCase.Checked ? query : query.ToLowerInvariant();
        for (int i = 0; i < _entries.Count; i++)
        {
            EditableEntry entry = _entries[i];
            string size = entry.Data.Length.ToString(CultureInfo.InvariantCulture);
            bool nameMatch = _searchNames.Checked && Matches(entry.Name, needle);
            bool sizeMatch = _searchSizes.Checked && Matches(size, needle);
            if (!nameMatch && !sizeMatch)
            {
                continue;
            }

            ListViewItem item = new(entry.Name);
            item.SubItems.Add(entry.Data.Length.ToString("N0", CultureInfo.CurrentCulture));
            _results.Items.Add(item);
            _matchIndices.Add(i);
        }

        _results.EndUpdate();
        _resultsKey = CurrentKey();
        _status.Text = _matchIndices.Count == 0
            ? "No matches."
            : _matchIndices.Count + " match(es).";
    }

    private bool Matches(string value, string needle)
    {
        string haystack = _matchCase.Checked ? value : value.ToLowerInvariant();
        return haystack.Contains(needle, StringComparison.Ordinal);
    }

    private void JumpToSelected()
    {
        if (_results.SelectedIndices.Count == 0)
        {
            return;
        }

        int row = _results.SelectedIndices[0];
        if (row >= 0 && row < _matchIndices.Count)
        {
            _selectEntry(_matchIndices[row]);
        }
    }
}
