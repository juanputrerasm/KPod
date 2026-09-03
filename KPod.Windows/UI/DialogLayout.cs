namespace KPod.Windows.UI;

/// <summary>
/// Shared pieces of the option dialogs' layout, so a caption reads and aligns the
/// same way in every one of them.
/// </summary>
internal static class DialogLayout
{
    /// <summary>
    /// The label in front of an input, sized to its text and left-aligned in the
    /// table's first column.
    ///
    /// <para>Belongs directly in the outer table rather than inside a nested row
    /// panel: the first column is what lines every caption in a dialog up with the
    /// others, and a caption tucked into a nested panel is outside that column.</para>
    /// </summary>
    internal static Label Caption(string text) => new()
    {
        Text = text,
        AutoSize = true,
        Anchor = AnchorStyles.Left,
        Margin = new Padding(0, 6, 8, 0),
    };
}
