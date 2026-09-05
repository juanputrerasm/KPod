using System.ComponentModel;
using System.Runtime.InteropServices;
using KPod.Windows.Platform;
using Silk.NET.OpenGL;

namespace KPod.Windows.UI;

/// <summary>A small WinForms/WGL host that creates and owns an OpenGL 3.3 core context.</summary>
internal sealed class OpenGlSurface : Control
{
    private const int PfdDrawToWindow = 0x00000004;
    private const int PfdSupportOpenGl = 0x00000020;
    private const int PfdDoubleBuffer = 0x00000001;
    private const byte PfdTypeRgba = 0;
    private const int CsOwnDc = 0x0020;
    private const int WsClipSiblings = 0x04000000;
    private const int WsClipChildren = 0x02000000;
    private const uint PfdGenericFormat = 0x00000040;
    private const uint PfdGenericAccelerated = 0x00001000;
    private const uint GlRenderer = 0x1F01;
    private const uint GlVersion = 0x1F02;
    private IntPtr _dc;
    private IntPtr _context;
    private IntPtr _openGlLibrary;
    private PixelFormatProvider? _formats;

    internal GL? Gl { get; private set; }
    internal string? InitializationError { get; private set; }
    internal event Action<GL>? RenderFrame;

    /// <summary>
    /// Raised once the context has been built, or has failed to build. HandleCreated is
    /// too early: it fires from inside OnHandleCreated before there is any context to
    /// use, so anything listening for it sees <see cref="Gl"/> still null.
    /// </summary>
    internal event EventHandler? ContextInitialized;

    /// <summary>
    /// The window styles OpenGL requires of a rendering window. WS_CLIPCHILDREN and
    /// WS_CLIPSIBLINGS are documented prerequisites for SetPixelFormat, and CS_OWNDC
    /// gives the window a private device context: taken from the shared cache instead,
    /// the pixel format set at startup can be quietly lost and SwapBuffers ends up
    /// presenting to nothing.
    /// </summary>
    protected override CreateParams CreateParams
    {
        get
        {
            CreateParams parameters = base.CreateParams;
            parameters.ClassStyle |= CsOwnDc;
            parameters.Style |= WsClipChildren | WsClipSiblings;
            return parameters;
        }
    }

    internal bool MakeCurrent() => _context != IntPtr.Zero && wglMakeCurrent(_dc, _context);

    internal OpenGlSurface()
    {
        SetStyle(ControlStyles.Opaque | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint, true);
        BackColor = Color.FromArgb(42, 42, 46);
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        try { CreateContext(); }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or NotSupportedException)
        {
            InitializationError = ex.Message;
        }
        ContextInitialized?.Invoke(this, EventArgs.Empty);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        if (Gl is null || _context == IntPtr.Zero)
        {
            e.Graphics.Clear(BackColor);
            TextRenderer.DrawText(e.Graphics,
                InitializationError ?? "OpenGL is not initialized.", Font, ClientRectangle,
                Color.White, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak);
            return;
        }

        if (!MakeCurrent()) return;
        RenderFrame?.Invoke(Gl);
        // Through the same provider the format came from: a drop-in renderer presents its
        // own buffers, and GDI's SwapBuffers knows nothing about them.
        (_formats?.Swap ?? SwapBuffers)(_dc);
    }

    protected override void OnPaintBackground(PaintEventArgs pevent) { }

    protected override void Dispose(bool disposing)
    {
        if (_context != IntPtr.Zero)
        {
            wglMakeCurrent(IntPtr.Zero, IntPtr.Zero);
            wglDeleteContext(_context);
            _context = IntPtr.Zero;
        }
        Gl?.Dispose(); Gl = null;
        if (_dc != IntPtr.Zero && IsHandleCreated) ReleaseDC(Handle, _dc);
        _dc = IntPtr.Zero;
        if (_openGlLibrary != IntPtr.Zero) FreeLibrary(_openGlLibrary);
        _openGlLibrary = IntPtr.Zero;
        base.Dispose(disposing);
    }

    private void CreateContext()
    {
        // Before anything touches opengl32, so a replacement renderer beside the exe is
        // the module every later reference binds to.
        OpenGlNativeLibrary.PreferLocalRenderer();
        _dc = GetDC(Handle);
        if (_dc == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not acquire the OpenGL window DC.");

        // Resolved up front: the wgl entry points come out of whichever opengl32.dll is
        // loaded, which is the drop-in renderer when there is one.
        _openGlLibrary = LoadLibrary("opengl32.dll");

        int format = 0;
        PixelFormatDescriptor chosen = default;
        foreach (PixelFormatProvider provider in PixelFormatProviders())
        {
            format = SelectPixelFormat(provider, _dc, out chosen);
            if (format == 0) continue;
            _formats = provider;
            break;
        }
        if (format == 0 || _formats is null)
            throw new NotSupportedException("This system offers no OpenGL-capable pixel format." + SoftwareRendererHint());
        // Only once: a window's pixel format cannot be set twice, so the provider has to
        // be settled by enumeration, which only reads, before anything is applied.
        if (!_formats.SetFormat(_dc, format, ref chosen))
            throw new Win32Exception(Marshal.GetLastWin32Error(),
                "Could not apply OpenGL pixel format " + format + " from " + _formats.Name + ".");

        IntPtr temporary = wglCreateContext(_dc);
        if (temporary == IntPtr.Zero || !wglMakeCurrent(_dc, temporary))
        {
            int error = Marshal.GetLastWin32Error();
            throw new Win32Exception(error, "Could not create an OpenGL context (error " + error + ")." + SoftwareRendererHint());
        }

        // Read these while the legacy context is still current: naming the driver turns
        // "it did not work" into something the user can act on, and distinguishes a real
        // GPU driver from the Windows software renderer.
        string driver = LegacyString(GlRenderer) + ", OpenGL " + LegacyString(GlVersion);

        IntPtr address = wglGetProcAddress("wglCreateContextAttribsARB");
        if (InvalidAddress(address))
        {
            wglMakeCurrent(IntPtr.Zero, IntPtr.Zero); wglDeleteContext(temporary);
            throw new NotSupportedException("BIN preview requires an OpenGL 3.3-capable display driver."
                + " This system offers " + driver + "." + SoftwareRendererHint());
        }

        CreateContextAttribs create = Marshal.GetDelegateForFunctionPointer<CreateContextAttribs>(address);
        int[] attributes = [0x2091, 3, 0x2092, 3, 0x9126, 0x00000001, 0];
        _context = create(_dc, IntPtr.Zero, attributes);
        wglMakeCurrent(IntPtr.Zero, IntPtr.Zero); wglDeleteContext(temporary);
        if (_context == IntPtr.Zero || !wglMakeCurrent(_dc, _context))
            throw new NotSupportedException("The display driver could not create the required OpenGL 3.3 core context."
                + " This system offers " + driver + "." + SoftwareRendererHint());

        Gl = GL.GetApi(LoadProcedure);
        string version = Gl.GetStringS(StringName.Version);
        if (string.IsNullOrEmpty(version)) throw new NotSupportedException("The OpenGL driver did not report a version.");
    }

    /// <summary>
    /// Picks a pixel format by asking what the device actually publishes, rather than
    /// describing an ideal one and trusting ChoosePixelFormat to match it.
    /// ChoosePixelFormat never fails: handed a descriptor no OpenGL format satisfies, it
    /// scores the whole list against it and can return a plain GDI format that has no
    /// OpenGL support at all, which then shows up as an unexplained wglCreateContext
    /// failure several calls later. Software renderers, which publish a short list with
    /// no alpha or stencil, are exactly where that misfires.
    /// </summary>
    private static int SelectPixelFormat(PixelFormatProvider provider, IntPtr dc, out PixelFormatDescriptor chosen)
    {
        chosen = default;
        ushort size = (ushort)Marshal.SizeOf(typeof(PixelFormatDescriptor));
        PixelFormatDescriptor candidate = default;
        candidate.Size = size;
        candidate.Version = 1;
        int count = provider.Describe(dc, 1, size, ref candidate);
        int best = 0;
        long bestScore = -1;
        for (int format = 1; format <= count; format++)
        {
            if (provider.Describe(dc, format, size, ref candidate) == 0) continue;
            const uint required = PfdDrawToWindow | PfdSupportOpenGl | PfdDoubleBuffer;
            if ((candidate.Flags & required) != required || candidate.PixelType != PfdTypeRgba) continue;
            if (candidate.ColorBits < 16 || candidate.DepthBits < 16) continue;

            // A full ICD beats a driver-assisted one, which beats the software renderer.
            // Below that, more colour and depth wins; alpha and stencil are preferences,
            // not requirements, because insisting on them is what excluded every usable
            // format on machines that publish none.
            long score = (candidate.Flags & PfdGenericFormat) == 0 ? 4000
                : (candidate.Flags & PfdGenericAccelerated) != 0 ? 2000 : 0;
            score += (candidate.ColorBits * 4) + candidate.DepthBits;
            if (candidate.AlphaBits > 0) score += 32;
            if (candidate.StencilBits > 0) score += 16;
            if (score <= bestScore) continue;
            bestScore = score;
            best = format;
            chosen = candidate;
        }
        return best;
    }

    /// <summary>
    /// The one remedy that does not depend on the machine having a usable display driver.
    /// Only offered when there is not already a local renderer in play, since repeating
    /// the advice to someone who has taken it is no help.
    /// </summary>
    private static string SoftwareRendererHint() => " "
        + (OpenGlNativeLibrary.Status
           ?? "A software OpenGL 3.3 renderer put next to KPod.exe as opengl32.dll also works.");

    /// <summary>
    /// Where to ask about pixel formats, best candidate first. GDI's ChoosePixelFormat
    /// family answers for the system's display driver, so it reports nothing usable when
    /// a drop-in opengl32.dll is the renderer; that one publishes its formats through its
    /// own wgl exports instead. Both are tried, because on a normal machine with a real
    /// driver either will do and the GDI path is the long-established one.
    /// </summary>
    private IEnumerable<PixelFormatProvider> PixelFormatProviders()
    {
        PixelFormatProvider? viaOpenGl = OpenGlProvider();
        if (OpenGlNativeLibrary.UsingLocalRenderer && viaOpenGl is not null) yield return viaOpenGl;
        yield return new PixelFormatProvider("GDI",
            (IntPtr dc, int format, uint size, ref PixelFormatDescriptor pfd) => DescribePixelFormat(dc, format, size, ref pfd),
            (IntPtr dc, int format, ref PixelFormatDescriptor pfd) => SetPixelFormat(dc, format, ref pfd),
            SwapBuffers);
        if (!OpenGlNativeLibrary.UsingLocalRenderer && viaOpenGl is not null) yield return viaOpenGl;
    }

    /// <summary>The wgl pixel-format entry points of whichever opengl32.dll is loaded.</summary>
    private PixelFormatProvider? OpenGlProvider()
    {
        if (_openGlLibrary == IntPtr.Zero) return null;
        IntPtr describe = GetProcAddress(_openGlLibrary, "wglDescribePixelFormat");
        IntPtr set = GetProcAddress(_openGlLibrary, "wglSetPixelFormat");
        IntPtr swap = GetProcAddress(_openGlLibrary, "wglSwapBuffers");
        if (describe == IntPtr.Zero || set == IntPtr.Zero || swap == IntPtr.Zero) return null;
        return new PixelFormatProvider("opengl32",
            Marshal.GetDelegateForFunctionPointer<DescribePixelFormatDelegate>(describe),
            Marshal.GetDelegateForFunctionPointer<SetPixelFormatDelegate>(set),
            Marshal.GetDelegateForFunctionPointer<SwapBuffersDelegate>(swap));
    }

    private sealed class PixelFormatProvider(
        string name, DescribePixelFormatDelegate describe, SetPixelFormatDelegate set, SwapBuffersDelegate swap)
    {
        internal string Name { get; } = name;
        internal DescribePixelFormatDelegate Describe { get; } = describe;
        internal SetPixelFormatDelegate SetFormat { get; } = set;
        internal SwapBuffersDelegate Swap { get; } = swap;
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int DescribePixelFormatDelegate(IntPtr dc, int format, uint size, ref PixelFormatDescriptor pfd);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate bool SetPixelFormatDelegate(IntPtr dc, int format, ref PixelFormatDescriptor pfd);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate bool SwapBuffersDelegate(IntPtr dc);

    /// <summary>One of the legacy glGetString values, read through a current context.</summary>
    private static string LegacyString(uint name)
    {
        IntPtr text = glGetString(name);
        return text == IntPtr.Zero ? "an unnamed renderer" : Marshal.PtrToStringAnsi(text) ?? "an unnamed renderer";
    }

    private IntPtr LoadProcedure(string name)
    {
        IntPtr address = wglGetProcAddress(name);
        return InvalidAddress(address) ? GetProcAddress(_openGlLibrary, name) : address;
    }

    private static bool InvalidAddress(IntPtr address) =>
        address == IntPtr.Zero || address == new IntPtr(1) || address == new IntPtr(2)
        || address == new IntPtr(3) || address == new IntPtr(-1);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate IntPtr CreateContextAttribs(IntPtr dc, IntPtr shareContext, int[] attributes);

    [StructLayout(LayoutKind.Sequential)]
    private struct PixelFormatDescriptor
    {
        internal ushort Size, Version;
        internal uint Flags;
        internal byte PixelType, ColorBits, RedBits, RedShift, GreenBits, GreenShift, BlueBits, BlueShift;
        internal byte AlphaBits, AlphaShift, AccumBits, AccumRedBits, AccumGreenBits, AccumBlueBits, AccumAlphaBits;
        internal byte DepthBits, StencilBits, AuxBuffers, LayerType, Reserved;
        internal uint LayerMask, VisibleMask, DamageMask;
    }

    [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr GetDC(IntPtr window);
    [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr window, IntPtr dc);
    [DllImport("gdi32.dll", SetLastError = true)] private static extern bool SetPixelFormat(IntPtr dc, int format, ref PixelFormatDescriptor pfd);
    [DllImport("gdi32.dll", SetLastError = true)] private static extern int DescribePixelFormat(IntPtr dc, int format, uint size, ref PixelFormatDescriptor pfd);
    [DllImport("gdi32.dll")] private static extern bool SwapBuffers(IntPtr dc);
    // SetLastError matters on the two that are reported on failure: without it the CLR
    // captures nothing and Marshal.GetLastWin32Error hands back a stale code from some
    // earlier call, which is worse than no code at all.
    [DllImport("opengl32.dll", SetLastError = true)] private static extern IntPtr wglCreateContext(IntPtr dc);
    [DllImport("opengl32.dll")] private static extern bool wglDeleteContext(IntPtr context);
    [DllImport("opengl32.dll", SetLastError = true)] private static extern bool wglMakeCurrent(IntPtr dc, IntPtr context);
    [DllImport("opengl32.dll", CharSet = CharSet.Ansi)] private static extern IntPtr wglGetProcAddress(string name);
    [DllImport("opengl32.dll")] private static extern IntPtr glGetString(uint name);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern IntPtr LoadLibrary(string name);
    [DllImport("kernel32.dll", CharSet = CharSet.Ansi)] private static extern IntPtr GetProcAddress(IntPtr module, string name);
    [DllImport("kernel32.dll")] private static extern bool FreeLibrary(IntPtr module);
}
