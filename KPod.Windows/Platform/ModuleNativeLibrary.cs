using System.Runtime.InteropServices;

namespace KPod.Windows.Platform;

/// <summary>Raised when the optional MOD playback add-on is absent or unusable.</summary>
internal sealed class ModPlaybackUnavailableException(string message) : Exception(message);

/// <summary>
/// Finds the optional libopenmpt add-on beside the executable and loads it by explicit
/// full path. Nothing ships inside KPod.exe and nothing is written to disk: the five
/// DLLs are either sitting next to the program or MOD playback is simply not available.
/// </summary>
internal static class ModuleNativeLibrary
{
    private const uint LoadWithAlteredSearchPath = 0x00000008;
    private const int ErrorBadExeFormat = 193;

    /// <summary>The head of every failure message, so one wording covers every way this can go wrong.</summary>
    internal const string NotInstalled = "MOD playback is not installed.";

    /// <summary>libopenmpt.dll last: the four companions must be on disk before it is loaded.</summary>
    internal static readonly string[] RequiredFiles =
    [
        "openmpt-mpg123.dll", "openmpt-ogg.dll", "openmpt-vorbis.dll", "openmpt-zlib.dll", "libopenmpt.dll",
    ];

    private static readonly object Gate = new();
    private static OpenMptApi? _api;

    internal static OpenMptApi Api
    {
        get
        {
            // Only a successful load is cached, so dropping the DLLs in and reopening the
            // file works without restarting KPod.
            lock (Gate) return _api ??= Load();
        }
    }

    /// <summary>The add-on folder: whatever directory the running executable lives in.</summary>
    internal static string Folder => AppContext.BaseDirectory;

    /// <summary>The first required DLL not present in <paramref name="folder"/>, or null when all five are.</summary>
    internal static string? MissingFile(string folder)
    {
        foreach (string name in RequiredFiles)
        {
            if (!File.Exists(Path.Combine(folder, name))) return name;
        }
        return null;
    }

    private static OpenMptApi Load()
    {
        string folder = Folder;
        string? missing = MissingFile(folder);
        if (missing is not null)
            throw new ModPlaybackUnavailableException(NotInstalled + " Copy " + missing
                + " and the rest of the libopenmpt add-on next to KPod.exe.");

        // The altered search path makes libopenmpt's own folder the first place Windows
        // looks for its four companions, which is why they only need to be beside it.
        string path = Path.Combine(folder, "libopenmpt.dll");
        IntPtr library = LoadLibraryEx(path, IntPtr.Zero, LoadWithAlteredSearchPath);
        if (library != IntPtr.Zero) return new OpenMptApi(library);

        int error = Marshal.GetLastWin32Error();
        throw new ModPlaybackUnavailableException(NotInstalled + (error == ErrorBadExeFormat
            ? " The libopenmpt.dll next to KPod.exe is not a 32-bit build."
            : " Windows could not load libopenmpt.dll (error " + error + ")."));
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr LoadLibraryEx(string fileName, IntPtr file, uint flags);
}

/// <summary>Strongly typed C ABI entry points resolved from an explicit library handle.</summary>
internal sealed class OpenMptApi
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate IntPtr CreateDelegate(IntPtr data, UIntPtr size, IntPtr log, IntPtr user, IntPtr controls);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void DestroyDelegate(IntPtr module);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate double DurationDelegate(IntPtr module);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate double PositionDelegate(IntPtr module);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate double SeekDelegate(IntPtr module, double seconds);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate UIntPtr ReadDelegate(IntPtr module, int sampleRate, UIntPtr frames, IntPtr destination);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int RepeatDelegate(IntPtr module, int repeatCount);

    private readonly CreateDelegate _create;
    private readonly DestroyDelegate _destroy;
    private readonly DurationDelegate _duration;
    private readonly PositionDelegate _position;
    private readonly SeekDelegate _seek;
    private readonly ReadDelegate _read;
    private readonly RepeatDelegate _repeat;

    internal OpenMptApi(IntPtr library)
    {
        _create = Function<CreateDelegate>(library, "openmpt_module_create_from_memory");
        _destroy = Function<DestroyDelegate>(library, "openmpt_module_destroy");
        _duration = Function<DurationDelegate>(library, "openmpt_module_get_duration_seconds");
        _position = Function<PositionDelegate>(library, "openmpt_module_get_position_seconds");
        _seek = Function<SeekDelegate>(library, "openmpt_module_set_position_seconds");
        _read = Function<ReadDelegate>(library, "openmpt_module_read_interleaved_stereo");
        _repeat = Function<RepeatDelegate>(library, "openmpt_module_set_repeat_count");
    }

    internal IntPtr Create(byte[] data)
    {
        GCHandle pin = GCHandle.Alloc(data, GCHandleType.Pinned);
        try { return _create(pin.AddrOfPinnedObject(), new UIntPtr((uint)data.Length), IntPtr.Zero, IntPtr.Zero, IntPtr.Zero); }
        finally { pin.Free(); }
    }
    internal void Destroy(IntPtr module) => _destroy(module);
    internal double Duration(IntPtr module) => _duration(module);
    internal double Position(IntPtr module) => _position(module);
    internal double Seek(IntPtr module, double seconds) => _seek(module, seconds);
    internal int Read(IntPtr module, int sampleRate, int frames, IntPtr destination) => checked((int)_read(module, sampleRate, new UIntPtr((uint)frames), destination).ToUInt64());
    internal void SetRepeatCount(IntPtr module, int count) => _repeat(module, count);

    private static T Function<T>(IntPtr library, string name) where T : Delegate
    {
        IntPtr address = GetProcAddress(library, name);
        if (address == IntPtr.Zero)
            throw new ModPlaybackUnavailableException(ModuleNativeLibrary.NotInstalled
                + " The libopenmpt.dll next to KPod.exe has no " + name + " entry point.");
        return (T)Marshal.GetDelegateForFunctionPointer(address, typeof(T));
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Ansi)]
    private static extern IntPtr GetProcAddress(IntPtr module, string name);
}
