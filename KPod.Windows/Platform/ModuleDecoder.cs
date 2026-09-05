using System.Runtime.InteropServices;

namespace KPod.Windows.Platform;

/// <summary>Owns one libopenmpt module and exposes deterministic PCM rendering for playback and tests.</summary>
internal sealed class ModuleDecoder : IDisposable
{
    private readonly OpenMptApi _api;
    private IntPtr _module;

    internal ModuleDecoder(byte[] data)
    {
        _api = ModuleNativeLibrary.Api;
        _module = _api.Create(data);
        if (_module == IntPtr.Zero) throw new ArgumentException("libopenmpt rejected this module file.");
        _api.SetRepeatCount(_module, 0);
        double duration = _api.Duration(_module);
        if (double.IsNaN(duration) || double.IsInfinity(duration) || duration <= 0
            || duration > TimeSpan.MaxValue.TotalSeconds)
        {
            Dispose();
            throw new ArgumentException("The module has no playable duration.");
        }
        Duration = TimeSpan.FromSeconds(duration);
    }

    internal TimeSpan Duration { get; }
    internal TimeSpan Position => TimeSpan.FromSeconds(_api.Position(_module));
    internal TimeSpan Seek(TimeSpan position) => TimeSpan.FromSeconds(
        _api.Seek(_module, Math.Max(0, Math.Min(Duration.TotalSeconds, position.TotalSeconds))));
    internal int Read(int sampleRate, int frames, IntPtr destination) => _api.Read(_module, sampleRate, frames, destination);

    internal int Read(int sampleRate, short[] stereo, int frames)
    {
        if (frames < 0 || frames * 2 > stereo.Length) throw new ArgumentOutOfRangeException(nameof(frames));
        GCHandle pin = GCHandle.Alloc(stereo, GCHandleType.Pinned);
        try { return Read(sampleRate, frames, pin.AddrOfPinnedObject()); }
        finally { pin.Free(); }
    }

    public void Dispose()
    {
        if (_module == IntPtr.Zero) return;
        _api.Destroy(_module);
        _module = IntPtr.Zero;
    }
}
