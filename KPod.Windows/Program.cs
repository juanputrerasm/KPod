using KPod.Windows.UI;

namespace KPod.Windows;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        // Before anything else, so it covers as much of startup as possible.
        StartJitProfile();

        // A WinExe has no console, so an unhandled exception would terminate the
        // process with no window and no message at all. Report it instead.
        AppDomain.CurrentDomain.UnhandledException += (_, e) => ReportFatal(e.ExceptionObject as Exception);
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) => ReportFatal(e.Exception);

        try
        {
#if NET5_0_OR_GREATER
            ApplicationConfiguration.Initialize();
#else
            // The source-generated initializer is .NET Core only. DPI awareness
            // comes from the embedded application manifest instead.
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
#endif
            // A path on the command line, or a file dropped on the exe, opens directly.
            string? startupFile = args.Length > 0 && File.Exists(args[0]) ? args[0] : null;
            Application.Run(new MainForm(startupFile));
        }
        catch (Exception ex)
        {
            ReportFatal(ex);
        }
    }

    /// <summary>
    /// Turns on multicore JIT. The first run records which methods startup actually
    /// compiles; later runs replay that list on a background thread, so the JIT works
    /// ahead of the code instead of being asked one method at a time.
    ///
    /// <para>This is the .NET Framework build's substitute for the ReadyToRun
    /// precompilation the .NET build gets at publish time. NGen would do more, but it
    /// needs an installer, and KPod is meant to be copied rather than installed.</para>
    ///
    /// <para>Entirely best effort: the profile lives beside the other per-machine
    /// state, and a machine that will not let it be written just starts as before.</para>
    /// </summary>
    private static void StartJitProfile()
    {
        try
        {
            string root = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                Dialogs.AppName);
            Directory.CreateDirectory(root);
            System.Runtime.ProfileOptimization.SetProfileRoot(root);
            System.Runtime.ProfileOptimization.StartProfile("startup.profile");
        }
        catch (Exception)
        {
            // A missing or unwritable profile only costs the speedup, never the app.
        }
    }

    private static void ReportFatal(Exception? ex)
    {
        try
        {
            MessageBox.Show(
                $"{Dialogs.AppName} could not start.\n\n{ex}",
                Dialogs.AppName,
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
        catch (Exception)
        {
            // Nothing further we can do; at least do not loop.
        }
    }
}
