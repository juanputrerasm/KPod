namespace KPod.Core.Compat;

/// <summary>
/// OS checks. <c>OperatingSystem.IsWindows()</c> and friends are .NET 5 and above.
/// </summary>
public static class OsCompat
{
#if NET5_0_OR_GREATER
    public static bool IsWindows() => OperatingSystem.IsWindows();

    public static bool IsMacOS() => OperatingSystem.IsMacOS();
#else
    // The .NET Framework build only ever runs on Windows.
    public static bool IsWindows() => true;

    public static bool IsMacOS() => false;
#endif
}
