using System.Globalization;
using System.Media;
using System.Runtime.InteropServices;
using System.Text;

namespace KPod.Windows.Platform;

/// <summary>
/// Plays a WAV entry with play, pause, resume, stop and a millisecond position.
///
/// <para>System.Media.SoundPlayer, the only thing in the box, can start and stop and
/// nothing else: no pause, no position. The MCI string interface in winmm.dll has
/// all of it and costs no package, which is the whole point of the .NET Framework
/// target. MCI needs a real file, so the entry's bytes are written to a temporary
/// one and deleted on close.</para>
///
/// <para>If MCI refuses the file, playback falls back to SoundPlayer and
/// <see cref="SupportsPauseAndPosition"/> goes false so the UI can disable what it
/// cannot offer.</para>
/// </summary>
internal sealed class WavePlayer : IAudioPlayer
{
    [DllImport("winmm.dll", CharSet = CharSet.Auto)]
    private static extern int mciSendString(string command, StringBuilder? result, int resultLength, IntPtr callback);

    private readonly string _alias = "kpodwave" + Guid.NewGuid().ToString("N").Substring(0, 8);
    private readonly string? _tempFile;
    private readonly SoundPlayer? _fallback;
    private bool _mciOpen;
    private bool _disposed;
    private AudioPlaybackState _fallbackState;

    internal WavePlayer(byte[] data)
    {
        try
        {
            _tempFile = Path.Combine(Path.GetTempPath(), _alias + ".wav");
            File.WriteAllBytes(_tempFile, data);
            _mciOpen = Send("open \"" + _tempFile + "\" type waveaudio alias " + _alias) == 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _mciOpen = false;
        }

        if (_mciOpen)
        {
            Duration = TimeSpan.FromMilliseconds(QueryInt("status " + _alias + " length"));
            return;
        }

        // MCI could not take it. SoundPlayer still plays ordinary PCM WAV data.
        try
        {
            _fallback = new SoundPlayer(new MemoryStream(data, writable: false));
            _fallback.Load();
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or TimeoutException)
        {
            _fallback = null;
        }
    }

    public bool IsAvailable => _mciOpen || _fallback is not null;
    public string? ErrorMessage => IsAvailable ? null : "Cannot play this WAV file.";
    public bool CanSeek => _mciOpen;
    public TimeSpan Duration { get; }
    public TimeSpan Position => _mciOpen
        ? TimeSpan.FromMilliseconds(QueryInt("status " + _alias + " position"))
        : TimeSpan.Zero;
    public AudioPlaybackState State
    {
        get
        {
            if (!_mciOpen) return _fallbackState;
            string mode = Query("status " + _alias + " mode");
            if (string.Equals(mode, "playing", StringComparison.Ordinal)) return AudioPlaybackState.Playing;
            if (string.Equals(mode, "paused", StringComparison.Ordinal)) return AudioPlaybackState.Paused;
            return AudioPlaybackState.Stopped;
        }
    }

    public void Play()
    {
        if (_mciOpen)
        {
            if (Duration > TimeSpan.Zero && Position >= Duration)
                Send("seek " + _alias + " to start");
            Send("play " + _alias);
            return;
        }

        _fallback?.Play();
        if (_fallback is not null) _fallbackState = AudioPlaybackState.Playing;
    }

    public void Pause()
    {
        if (_mciOpen)
        {
            Send("pause " + _alias);
            return;
        }

        _fallback?.Stop();
        if (_fallback is not null) _fallbackState = AudioPlaybackState.Paused;
    }

    public void Resume()
    {
        if (_mciOpen)
        {
            Send("resume " + _alias);
            return;
        }

        _fallback?.Play();
        if (_fallback is not null) _fallbackState = AudioPlaybackState.Playing;
    }

    public void Stop()
    {
        if (_mciOpen)
        {
            Send("stop " + _alias);
            Send("seek " + _alias + " to start");
            return;
        }

        _fallback?.Stop();
        _fallbackState = AudioPlaybackState.Stopped;
    }

    public void Seek(TimeSpan position)
    {
        if (!_mciOpen) return;
        int milliseconds = (int)Math.Max(0, Math.Min(Duration.TotalMilliseconds, position.TotalMilliseconds));
        AudioPlaybackState previous = State;
        Send("seek " + _alias + " to " + milliseconds.ToString(CultureInfo.InvariantCulture));
        if (previous is AudioPlaybackState.Playing or AudioPlaybackState.Paused)
        {
            Send("play " + _alias);
            if (previous == AudioPlaybackState.Paused) Send("pause " + _alias);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_mciOpen)
        {
            Send("stop " + _alias);
            Send("close " + _alias);
            _mciOpen = false;
        }

        _fallback?.Dispose();

        if (_tempFile is not null)
        {
            try
            {
                File.Delete(_tempFile);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // A leaked temp file in %TEMP% is not worth surfacing to the user.
            }
        }
    }

    private static int Send(string command) => mciSendString(command, null, 0, IntPtr.Zero);

    private static string Query(string command)
    {
        StringBuilder buffer = new(128);
        return mciSendString(command, buffer, buffer.Capacity, IntPtr.Zero) == 0
            ? buffer.ToString()
            : string.Empty;
    }

    private static int QueryInt(string command) =>
        int.TryParse(Query(command), NumberStyles.Integer, CultureInfo.InvariantCulture, out int value)
            ? value
            : 0;
}
