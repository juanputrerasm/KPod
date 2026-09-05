namespace KPod.Windows.Platform;

public enum AudioPlaybackState
{
    Stopped,
    Playing,
    Paused,
}

/// <summary>Common transport used by the WAV and tracker-module players.</summary>
public interface IAudioPlayer : IDisposable
{
    bool IsAvailable { get; }
    string? ErrorMessage { get; }
    bool CanSeek { get; }
    TimeSpan Duration { get; }
    TimeSpan Position { get; }
    AudioPlaybackState State { get; }
    void Play();
    void Pause();
    void Resume();
    void Stop();
    void Seek(TimeSpan position);
}
