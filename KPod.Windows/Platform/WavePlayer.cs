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
internal sealed class WavePlayer : IDisposable
{
    [DllImport("winmm.dll", CharSet = CharSet.Auto)]
    private static extern int mciSendString(string command, StringBuilder? result, int resultLength, IntPtr callback);

    private readonly string _alias = "kpodwave" + Guid.NewGuid().ToString("N").Substring(0, 8);
    private readonly string? _tempFile;
    private readonly SoundPlayer? _fallback;
    private bool _mciOpen;
    private bool _disposed;

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
            LengthMilliseconds = QueryInt("status " + _alias + " length");
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

    /// <summary>False when playback fell back to SoundPlayer, which offers neither.</summary>
    internal bool SupportsPauseAndPosition => _mciOpen;

    /// <summary>True when nothing could open the data at all.</summary>
    internal bool IsUnplayable => !_mciOpen && _fallback is null;

    /// <summary>Total length in milliseconds, or 0 when it cannot be known.</summary>
    internal int LengthMilliseconds { get; }

    /// <summary>Current position in milliseconds, or 0 without MCI.</summary>
    internal int PositionMilliseconds => _mciOpen ? QueryInt("status " + _alias + " position") : 0;

    /// <summary>True while audio is actually playing.</summary>
    internal bool IsPlaying =>
        _mciOpen && string.Equals(Query("status " + _alias + " mode"), "playing", StringComparison.Ordinal);

    internal void Play()
    {
        if (_mciOpen)
        {
            Send("play " + _alias);
            return;
        }

        _fallback?.Play();
    }

    internal void Pause()
    {
        if (_mciOpen)
        {
            Send("pause " + _alias);
            return;
        }

        _fallback?.Stop();
    }

    internal void Resume()
    {
        if (_mciOpen)
        {
            Send("resume " + _alias);
            return;
        }

        _fallback?.Play();
    }

    internal void Stop()
    {
        if (_mciOpen)
        {
            Send("stop " + _alias);
            Send("seek " + _alias + " to start");
            return;
        }

        _fallback?.Stop();
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
