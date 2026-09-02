using System.Text;

namespace KPod.Core.Preferences;

/// <summary>
/// Loads and saves <see cref="AppConfig"/> at
/// <c>%APPDATA%\KPod\config.json</c>. On first run the equivalent JPod file is
/// imported, so someone moving from the Java build keeps their recent-file list.
/// </summary>
public static class ConfigStore
{
    private const string FileName = "config.json";

    /// <summary>Where KPod's own config lives.</summary>
    public static string ConfigPath => PathFor("KPod");

    /// <summary>Where JPod's config lives, imported once if KPod has none yet.</summary>
    public static string LegacyConfigPath => PathFor("JPod");

    public static AppConfig Load()
    {
        string path = ConfigPath;
        if (!File.Exists(path))
        {
            path = LegacyConfigPath;
            if (!File.Exists(path))
            {
                return AppConfig.Defaults();
            }
        }

        try
        {
            return AppConfig.Parse(File.ReadAllText(path, Encoding.UTF8));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return AppConfig.Defaults();
        }
    }

    public static void Save(AppConfig config)
    {
        string path = ConfigPath;
        string? parent = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(parent))
        {
            Directory.CreateDirectory(parent!);
        }

        File.WriteAllText(path, config.ToJson(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    private static string PathFor(string productFolder)
    {
        string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        if (string.IsNullOrEmpty(appData))
        {
            appData = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "AppData",
                "Roaming");
        }

        return Path.Combine(appData, productFolder, FileName);
    }
}
