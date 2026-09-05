using System.Runtime.InteropServices;

namespace KPod.Windows.Platform;

/// <summary>Streams 48 kHz stereo PCM decoded by libopenmpt through buffered waveOut.</summary>
internal sealed class ModulePlayer : IAudioPlayer
{
    private const int SampleRate = 48000;
    private const int FramesPerBuffer = 4096;
    private const int BufferCount = 4;
    private const uint WaveMapper = unchecked((uint)-1);
    private const uint WaveHeaderDone = 0x00000001;
    private readonly object _gate = new();
    private readonly AutoResetEvent _wake = new(false);
    private readonly List<OutputBuffer> _buffers = [];
    private ModuleDecoder? _decoder;
    private IntPtr _waveOut;
    private Thread? _worker;
    private AudioPlaybackState _state;
    private double _positionSeconds;
    private bool _endReached;
    private bool _disposed;
    private int _epoch;

    internal ModulePlayer(byte[] data)
    {
        try
        {
            _decoder = new ModuleDecoder(data);
            Duration = _decoder.Duration;

            WaveFormat format = WaveFormat.PcmStereo16(SampleRate);
            int result = waveOutOpen(out _waveOut, WaveMapper, ref format, IntPtr.Zero, IntPtr.Zero, 0);
            if (result != 0) throw new InvalidOperationException("waveOut could not open an audio device (error " + result + ").");
            for (int i = 0; i < BufferCount; i++) _buffers.Add(new OutputBuffer(FramesPerBuffer * 4));
            _worker = new Thread(PlaybackLoop) { IsBackground = true, Name = "KPod MOD playback" };
            _worker.Start();
            IsAvailable = true;
        }
        catch (ModPlaybackUnavailableException ex)
        {
            // The add-on is missing or unusable. Say so in its own words rather than
            // blaming the file; KPod and WAV playback are unaffected either way.
            ErrorMessage = ex.Message;
            CleanupNative();
        }
        catch (Exception ex) when (ex is ArgumentException or BadImageFormatException or DllNotFoundException
            or EntryPointNotFoundException or IOException or UnauthorizedAccessException or InvalidOperationException
            or OverflowException)
        {
            ErrorMessage = "Cannot play this MOD file: " + ex.Message;
            CleanupNative();
        }
    }

    public bool IsAvailable { get; }
    public string? ErrorMessage { get; }
    public bool CanSeek => IsAvailable;
    public TimeSpan Duration { get; }
    public TimeSpan Position
    {
        get { lock (_gate) return TimeSpan.FromSeconds(Math.Max(0, Math.Min(Duration.TotalSeconds, _positionSeconds))); }
    }
    public AudioPlaybackState State { get { lock (_gate) return _state; } }

    public void Play()
    {
        lock (_gate)
        {
            if (!IsAvailable || _disposed) return;
            if (_positionSeconds >= Duration.TotalSeconds - 0.001)
            {
                _epoch++;
                _decoder!.Seek(TimeSpan.Zero);
                _positionSeconds = 0;
                _endReached = false;
            }
            if (_state == AudioPlaybackState.Paused) waveOutRestart(_waveOut);
            _state = AudioPlaybackState.Playing;
        }
        _wake.Set();
    }

    public void Pause()
    {
        lock (_gate)
        {
            if (_state != AudioPlaybackState.Playing || _disposed) return;
            waveOutPause(_waveOut);
            _state = AudioPlaybackState.Paused;
        }
    }

    public void Resume() => Play();

    public void Stop()
    {
        lock (_gate)
        {
            if (!IsAvailable || _disposed) return;
            _epoch++;
            waveOutReset(_waveOut);
            _decoder!.Seek(TimeSpan.Zero);
            _positionSeconds = 0;
            _endReached = false;
            _state = AudioPlaybackState.Stopped;
        }
        _wake.Set();
    }

    public void Seek(TimeSpan position)
    {
        lock (_gate)
        {
            if (!IsAvailable || _disposed) return;
            double seconds = Math.Max(0, Math.Min(Duration.TotalSeconds, position.TotalSeconds));
            bool playing = _state == AudioPlaybackState.Playing;
            _epoch++;
            waveOutReset(_waveOut);
            _positionSeconds = _decoder!.Seek(TimeSpan.FromSeconds(seconds)).TotalSeconds;
            _endReached = _positionSeconds >= Duration.TotalSeconds - 0.001;
            if (playing) _state = AudioPlaybackState.Playing;
        }
        _wake.Set();
    }

    private void PlaybackLoop()
    {
        while (true)
        {
            lock (_gate)
            {
                if (_disposed) return;
                ReapCompletedBuffers();
                if (_state == AudioPlaybackState.Playing)
                {
                    foreach (OutputBuffer buffer in _buffers)
                    {
                        if (buffer.Queued || _endReached) continue;
                        int frames = _decoder!.Read(SampleRate, FramesPerBuffer, buffer.Data);
                        if (frames == 0)
                        {
                            _endReached = true;
                            break;
                        }
                        buffer.Queue(_waveOut, frames * 4, _decoder.Position.TotalSeconds, _epoch);
                    }
                    if (_endReached && !_buffers.Any(buffer => buffer.Queued))
                    {
                        _positionSeconds = Duration.TotalSeconds;
                        _state = AudioPlaybackState.Stopped;
                    }
                }
            }
            _wake.WaitOne(10);
        }
    }

    private void ReapCompletedBuffers()
    {
        foreach (OutputBuffer buffer in _buffers)
        {
            if (!buffer.Queued || !buffer.IsDone) continue;
            waveOutUnprepareHeader(_waveOut, buffer.Header, (uint)Marshal.SizeOf(typeof(WaveHeader)));
            buffer.Queued = false;
            if (buffer.Epoch == _epoch) _positionSeconds = buffer.EndPosition;
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            if (_waveOut != IntPtr.Zero) waveOutReset(_waveOut);
        }
        _wake.Set();
        _worker?.Join(1000);
        CleanupNative();
        _wake.Dispose();
    }

    private void CleanupNative()
    {
        if (_waveOut != IntPtr.Zero)
        {
            waveOutReset(_waveOut);
            foreach (OutputBuffer buffer in _buffers)
            {
                if (buffer.Queued) waveOutUnprepareHeader(_waveOut, buffer.Header, (uint)Marshal.SizeOf(typeof(WaveHeader)));
                buffer.Dispose();
            }
            _buffers.Clear();
            waveOutClose(_waveOut);
            _waveOut = IntPtr.Zero;
        }
        _decoder?.Dispose();
        _decoder = null;
    }

    private sealed class OutputBuffer : IDisposable
    {
        internal IntPtr Data { get; }
        internal IntPtr Header { get; }
        internal bool Queued { get; set; }
        internal double EndPosition { get; private set; }
        internal int Epoch { get; private set; }
        internal bool IsDone => (Marshal.PtrToStructure<WaveHeader>(Header).Flags & WaveHeaderDone) != 0;

        internal OutputBuffer(int byteCount)
        {
            Data = Marshal.AllocHGlobal(byteCount);
            Header = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(WaveHeader)));
        }

        internal void Queue(IntPtr waveOut, int byteCount, double endPosition, int epoch)
        {
            WaveHeader header = new() { Data = Data, BufferLength = (uint)byteCount };
            Marshal.StructureToPtr(header, Header, false);
            int size = Marshal.SizeOf(typeof(WaveHeader));
            int prepared = waveOutPrepareHeader(waveOut, Header, (uint)size);
            if (prepared != 0) return;
            int written = waveOutWrite(waveOut, Header, (uint)size);
            if (written != 0)
            {
                waveOutUnprepareHeader(waveOut, Header, (uint)size);
                return;
            }
            EndPosition = endPosition;
            Epoch = epoch;
            Queued = true;
        }

        public void Dispose()
        {
            Marshal.FreeHGlobal(Header);
            Marshal.FreeHGlobal(Data);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WaveFormat
    {
        internal ushort FormatTag, Channels;
        internal uint SamplesPerSecond, AverageBytesPerSecond;
        internal ushort BlockAlign, BitsPerSample, ExtraSize;
        internal static WaveFormat PcmStereo16(int rate) => new()
        {
            FormatTag = 1, Channels = 2, SamplesPerSecond = (uint)rate,
            AverageBytesPerSecond = (uint)(rate * 4), BlockAlign = 4, BitsPerSample = 16,
        };
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WaveHeader
    {
        internal IntPtr Data;
        internal uint BufferLength, BytesRecorded;
        internal IntPtr User;
        internal uint Flags, Loops;
        internal IntPtr Next, Reserved;
    }

    [DllImport("winmm.dll")] private static extern int waveOutOpen(out IntPtr handle, uint deviceId, ref WaveFormat format, IntPtr callback, IntPtr instance, uint flags);
    [DllImport("winmm.dll")] private static extern int waveOutPrepareHeader(IntPtr handle, IntPtr header, uint size);
    [DllImport("winmm.dll")] private static extern int waveOutUnprepareHeader(IntPtr handle, IntPtr header, uint size);
    [DllImport("winmm.dll")] private static extern int waveOutWrite(IntPtr handle, IntPtr header, uint size);
    [DllImport("winmm.dll")] private static extern int waveOutPause(IntPtr handle);
    [DllImport("winmm.dll")] private static extern int waveOutRestart(IntPtr handle);
    [DllImport("winmm.dll")] private static extern int waveOutReset(IntPtr handle);
    [DllImport("winmm.dll")] private static extern int waveOutClose(IntPtr handle);
}
