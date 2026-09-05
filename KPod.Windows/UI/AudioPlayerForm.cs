using KPod.Windows.Platform;

namespace KPod.Windows.UI;

/// <summary>Shared WAV/MOD transport with seeking and formatted elapsed time.</summary>
internal sealed class AudioPlayerForm : Form
{
    private readonly IAudioPlayer _player;
    private readonly Label _time = new() { AutoSize = false, TextAlign = ContentAlignment.MiddleCenter, Dock = DockStyle.Fill };
    private readonly TrackBar _seek = new() { Dock = DockStyle.Fill, Minimum = 0, Maximum = 1000, TickStyle = TickStyle.None };
    private readonly Button _play = new() { Text = "Play", AutoSize = true };
    private readonly Button _pause = new() { Text = "Pause", AutoSize = true };
    private readonly Button _stop = new() { Text = "Stop", AutoSize = true };
    private readonly System.Windows.Forms.Timer _ticker = new() { Interval = 100 };
    private bool _draggingSeek;

    internal AudioPlayerForm(string entryName, byte[] data)
    {
        Text = "Audio Player - " + entryName;
        Icon = AppIcon.Shared;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MaximizeBox = false;
        ShowInTaskbar = false;
        AutoScaleMode = AutoScaleMode.Dpi;
        Font = SystemFonts.MessageBoxFont!;
        ClientSize = new Size(410, 158);

        _player = entryName.EndsWith(".MOD", StringComparison.OrdinalIgnoreCase)
            ? new ModulePlayer(data)
            : new WavePlayer(data);
        _seek.Enabled = _player.IsAvailable && _player.CanSeek && _player.Duration > TimeSpan.Zero;
        SetTransportEnabled(_player.IsAvailable);

        if (!_player.IsAvailable)
        {
            _time.Text = "N/A";
            Shown += (_, _) => Dialogs.Warn(this, _player.ErrorMessage ?? "Cannot play this audio file.");
        }

        _play.Click += (_, _) =>
        {
            if (_player.State == AudioPlaybackState.Paused) _player.Resume();
            else _player.Play();
            _ticker.Start();
            UpdateTransport();
        };
        _pause.Click += (_, _) =>
        {
            if (_player.State == AudioPlaybackState.Paused) _player.Resume();
            else _player.Pause();
            _ticker.Start();
            UpdateTransport();
        };
        _stop.Click += (_, _) =>
        {
            _player.Stop();
            _ticker.Stop();
            UpdateTransport();
        };
        _ticker.Tick += (_, _) => UpdateTransport();
        _seek.MouseDown += (_, _) => _draggingSeek = true;
        _seek.MouseUp += (_, _) =>
        {
            if (_player.CanSeek && _player.Duration > TimeSpan.Zero)
                _player.Seek(TimeSpan.FromTicks(_player.Duration.Ticks * _seek.Value / _seek.Maximum));
            _draggingSeek = false;
            UpdateTransport();
        };

        Button close = new() { Text = "Close", DialogResult = DialogResult.Cancel, AutoSize = true };
        close.Click += (_, _) => Close();
        FlowLayoutPanel buttons = new() { Dock = DockStyle.Fill, WrapContents = false, FlowDirection = FlowDirection.LeftToRight };
        buttons.Controls.AddRange([_play, _pause, _stop, close]);

        TableLayoutPanel layout = new()
        {
            Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Padding = new Padding(12),
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        layout.Controls.Add(_time, 0, 0);
        layout.Controls.Add(_seek, 0, 1);
        layout.Controls.Add(buttons, 0, 2);
        Controls.Add(layout);
        CancelButton = close;
        UpdateTransport();
    }

    private void SetTransportEnabled(bool enabled)
    {
        _play.Enabled = enabled;
        _pause.Enabled = enabled && _player.CanSeek;
        _stop.Enabled = enabled;
    }

    private void UpdateTransport()
    {
        if (!_player.IsAvailable)
        {
            _time.Text = "N/A";
            return;
        }
        TimeSpan position = _player.Position;
        TimeSpan duration = _player.Duration;
        _time.Text = Format(position) + " / " + (duration > TimeSpan.Zero ? Format(duration) : "unknown");
        if (!_draggingSeek && duration > TimeSpan.Zero)
        {
            double ratio = Math.Max(0, Math.Min(1, position.TotalSeconds / duration.TotalSeconds));
            _seek.Value = (int)Math.Round(ratio * _seek.Maximum);
        }
        _pause.Text = _player.State == AudioPlaybackState.Paused ? "Resume" : "Pause";
        if (_player.State == AudioPlaybackState.Stopped) _ticker.Stop();
    }

    internal static string Format(TimeSpan value) => value.TotalHours >= 1
        ? string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0}:{1:00}:{2:00.000}", (int)value.TotalHours, value.Minutes, value.Seconds + value.Milliseconds / 1000d)
        : string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0}:{1:00.000}", (int)value.TotalMinutes, value.Seconds + value.Milliseconds / 1000d);

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _ticker.Stop();
        _ticker.Dispose();
        _player.Dispose();
        base.OnFormClosed(e);
    }
}
