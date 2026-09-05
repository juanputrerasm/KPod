using System.Runtime.InteropServices;

namespace KPod.Windows.Platform;

/// <summary>
/// Optional software OpenGL. Windows' own opengl32.dll falls back to a 1.1 renderer when
/// the display driver publishes no ICD, which is not enough for the BIN viewer. Dropping
/// a software implementation's opengl32.dll next to KPod.exe, Mesa's llvmpipe build for
/// instance, replaces it for this process only: no installation, no registry, and no
/// effect on any other program on the machine.
/// </summary>
internal static class OpenGlNativeLibrary
{
    private const uint LoadWithAlteredSearchPath = 0x00000008;
    private const int ErrorModuleNotFound = 126;
    private const int ErrorBadExeFormat = 193;
    private static readonly object Gate = new();
    private static bool _attempted;

    /// <summary>Where a replacement renderer has to sit to be picked up.</summary>
    internal static string CandidatePath => Path.Combine(AppContext.BaseDirectory, "opengl32.dll");

    /// <summary>True once a local opengl32.dll has been loaded in place of the system one.</summary>
    internal static bool UsingLocalRenderer { get; private set; }

    /// <summary>
    /// What became of the local renderer, in one sentence, or null when there was no file
    /// to try. A DLL that is present and refused is the case worth shouting about: it
    /// looks installed, so silence there leaves the user with nothing to go on.
    /// </summary>
    internal static string? Status { get; private set; }

    /// <summary>
    /// Loads the opengl32.dll beside the executable, if there is one, before anything
    /// binds to the system copy. The loader keys modules by file name, so once this one
    /// is in, every later reference to "opengl32.dll" resolves to it, including the
    /// implicit ones behind the wgl imports. Called before the first OpenGL call; with no
    /// such file present it does nothing at all and the system renderer is used.
    /// </summary>
    internal static void PreferLocalRenderer()
    {
        lock (Gate)
        {
            if (_attempted) return;
            _attempted = true;
            string path = CandidatePath;
            if (!File.Exists(path)) return;
            // A companion the renderer needs, such as Mesa's libgallium_wgl.dll, resolves
            // out of that same folder because of the altered search path.
            if (LoadLibraryEx(path, IntPtr.Zero, LoadWithAlteredSearchPath) != IntPtr.Zero)
            {
                UsingLocalRenderer = true;
                Status = "Using the opengl32.dll next to KPod.exe.";
                return;
            }

            int error = Marshal.GetLastWin32Error();
            Status = "The opengl32.dll next to KPod.exe could not be loaded (error " + error + ")."
                + error switch
                {
                    ErrorModuleNotFound => " A DLL it depends on is missing: Mesa's WGL build"
                        + " needs the rest of its package, libgallium_wgl.dll included, in the same folder.",
                    ErrorBadExeFormat => " It is not a 32-bit build, and KPod is a 32-bit program.",
                    _ => string.Empty,
                };
        }
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr LoadLibraryEx(string fileName, IntPtr file, uint flags);
}
