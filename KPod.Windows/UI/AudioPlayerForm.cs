using KPod.Windows.Platform;

namespace KPod.Windows.UI;

/// <summary>
/// WAV playback with length and position readouts. Pause and position come from
/// MCI; when only the SoundPlayer fallback is available those controls are
/// disabled rather than lying about what they do.
/// </summary>
internal sealed class AudioPlayerForm : Form
{
    private readonly WavePlayer _player;
    private readonly Label _length = new() { AutoSize = true, Text = "0" };
    private readonly Label _position = new() { AutoSize = true, Text = "0" };
    private readonly Button _play = new() { Text = "Play", AutoSize = true };
    private readonly Button _pause = new() { Text = "Pause", AutoSize = true };
    private readonly Button _stop = new() { Text = "Stop", AutoSize = true };
    private readonly System.Windows.Forms.Timer _ticker = new() { Interval = 250 };
    private bool _paused;

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
        ClientSize = new Size(340, 150);

        _player = new WavePlayer(data);
        _length.Text = _player.LengthMilliseconds > 0 ? _player.LengthMilliseconds + " ms" : "unknown";

        if (_player.IsUnplayable)
        {
            _play.Enabled = false;
            _pause.Enabled = false;
            _stop.Enabled = false;
            _length.Text = "N/A";
            _position.Text = "N/A";
            Shown += (_, _) => Dialogs.Warn(this, "Cannot play this audio file.");
        }
        else if (!_player.SupportsPauseAndPosition)
        {
            _pause.Enabled = false;
            _position.Text = "not available";
        }

        _play.Click += (_, _) =>
        {
            if (_paused)
            {
                _player.Resume();
                _paused = false;
                _pause.Text = "Pause";
            }
            else
            {
                _player.Play();
            }

            _ticker.Start();
        };

        _pause.Click += (_, _) =>
        {
            if (_paused)
            {
                _player.Resume();
                _paused = false;
                _pause.Text = "Pause";
                _ticker.Start();
            }
            else
            {
                _player.Pause();
                _paused = true;
                _pause.Text = "Resume";
                _ticker.Stop();
            }
        };

        _stop.Click += (_, _) =>
        {
            _player.Stop();
            _paused = false;
            _pause.Text = "Pause";
            _ticker.Stop();
            UpdatePosition();
        };

        _ticker.Tick += (_, _) => UpdatePosition();

        Button close = new() { Text = "Close", DialogResult = DialogResult.Cancel, AutoSize = true };
        close.Click += (_, _) => Close();

        FlowLayoutPanel buttons = new() { Dock = DockStyle.Fill, WrapContents = false };
        buttons.Controls.Add(_play);
        buttons.Controls.Add(_pause);
        buttons.Controls.Add(_stop);
        buttons.Controls.Add(close);

        // The captions live in this grid rather than a nested panel, which would size
        // its cells from them and then paint nothing. See MainForm's centre panel.
        TableLayoutPanel layout = new()
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 3,
            Padding = new Padding(12),
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        layout.Controls.Add(DialogLayout.Caption("Length:"), 0, 0);
        layout.Controls.Add(_length, 1, 0);
        layout.Controls.Add(DialogLayout.Caption("Position:"), 0, 1);
        layout.Controls.Add(_position, 1, 1);
        layout.Controls.Add(buttons, 0, 2);
        layout.SetColumnSpan(buttons, 2);

        Controls.Add(layout);
        CancelButton = close;
    }

    private void UpdatePosition()
    {
        if (!_player.SupportsPauseAndPosition)
        {
            return;
        }

        _position.Text = _player.PositionMilliseconds + " ms";
        if (!_player.IsPlaying && !_paused)
        {
            _ticker.Stop();
        }
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _ticker.Stop();
        _ticker.Dispose();
        _player.Dispose();
        base.OnFormClosed(e);
    }
}
