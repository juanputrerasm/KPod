namespace KPod.Windows.UI;

/// <summary>
/// Keeps generous default window sizes usable on small displays.
/// </summary>
internal static class ScreenFit
{
    /// <summary>Leaves this much of the working area free around a window.</summary>
    private const int Margin = 60;

    /// <summary>
    /// Caps a desired size to what the screen can actually show. Without this a
    /// comfortable default on a large monitor opens partly off-screen on a
    /// laptop, and an oversized MinimumSize would make the window unshrinkable.
    /// </summary>
    internal static Size Cap(int width, int height)
    {
        Rectangle work = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1280, 800);
        return new Size(
            Math.Min(width, Math.Max(640, work.Width - Margin)),
            Math.Min(height, Math.Max(480, work.Height - Margin)));
    }
}
