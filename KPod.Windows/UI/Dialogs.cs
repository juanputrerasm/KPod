namespace KPod.Windows.UI;

/// <summary>Message box helpers, so every dialog shares one caption.</summary>
internal static class Dialogs
{
    internal const string AppName = "KPod";

    internal static void Warn(IWin32Window owner, string message) =>
        MessageBox.Show(owner, message, AppName, MessageBoxButtons.OK, MessageBoxIcon.Warning);

    internal static void Info(IWin32Window owner, string message) =>
        MessageBox.Show(owner, message, AppName, MessageBoxButtons.OK, MessageBoxIcon.Information);

    /// <summary>Reports a failed operation with the exception's own message.</summary>
    internal static void Error(IWin32Window owner, string context, Exception ex) =>
        MessageBox.Show(
            owner, context + ":\n" + ex.Message, AppName, MessageBoxButtons.OK, MessageBoxIcon.Error);

    /// <summary>Yes/No confirmation. Returns true when the user chose Yes.</summary>
    internal static bool Confirm(IWin32Window owner, string message) =>
        MessageBox.Show(owner, message, AppName, MessageBoxButtons.YesNo, MessageBoxIcon.Warning)
            == DialogResult.Yes;
}
