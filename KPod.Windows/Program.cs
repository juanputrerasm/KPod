using KPod.Windows.UI;

namespace KPod.Windows;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
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
