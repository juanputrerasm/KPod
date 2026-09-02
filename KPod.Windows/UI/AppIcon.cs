namespace KPod.Windows.UI;

/// <summary>
/// The application icon, loaded once from the embedded resource. Forms do not
/// pick up the executable's icon on their own, so each one sets this.
/// </summary>
internal static class AppIcon
{
    private const string ResourceName = "KPod.Windows.Assets.KPod.ico";

    private static Icon? _shared;
    private static bool _loaded;

    internal static Icon? Shared
    {
        get
        {
            if (!_loaded)
            {
                _loaded = true;
                _shared = Load();
            }

            return _shared;
        }
    }

    private static Icon? Load()
    {
        try
        {
            using Stream? stream = typeof(AppIcon).Assembly.GetManifestResourceStream(ResourceName);
            return stream is null ? null : new Icon(stream);
        }
        catch (Exception ex) when (ex is IOException or ArgumentException)
        {
            // A missing icon is not worth failing a window over.
            return null;
        }
    }
}
