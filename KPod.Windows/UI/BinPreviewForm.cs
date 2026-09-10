using System.Numerics;
using KPod.Core.Images;
using KPod.Windows.Platform;
using KPod.Core.Models;
using KPod.Core.Pods;

namespace KPod.Windows.UI;

/// <summary>Native, archive-aware OpenGL preview for classic and Extended BIN models.</summary>
internal sealed class BinPreviewForm : Form
{
    private readonly string _entryName;
    private readonly PodArchive? _archive;
    private readonly BinModel _animation;
    private readonly IReadOnlyList<BinFrame> _frames;
    // Every frame decoded once, so stepping the animation neither re-reads the archive
    // nor re-uploads geometry. An ordinary model is a single-entry list.
    private readonly IReadOnlyList<BinModel> _frameModels;
    private int _frameIndex;
    private BinModel _model => _frameModels[Math.Min(_frameIndex, _frameModels.Count - 1)];
    private readonly OpenGlSurface _surface = new() { Dock = DockStyle.Fill, TabStop = true };
    private readonly FlowLayoutPanel _textures = new()
    {
        Dock = DockStyle.Bottom, Height = 112, AutoScroll = true, WrapContents = false,
        FlowDirection = FlowDirection.LeftToRight, BackColor = Color.FromArgb(35, 35, 38), Padding = new Padding(4),
    };
    private readonly TextBox _diagnostics = new()
    {
        Dock = DockStyle.Bottom, Height = 54, ReadOnly = true, Multiline = true, ScrollBars = ScrollBars.Vertical,
        BackColor = SystemColors.Control, BorderStyle = BorderStyle.FixedSingle,
    };
    private readonly Label _statistics = new() { Dock = DockStyle.Top, AutoSize = false, Height = 24, Padding = new Padding(6, 4, 0, 0) };
    private readonly CheckBox _textureToggle = Toggle("Textures", true);
    private readonly CheckBox _wireframeToggle = Toggle("Wireframe", false);
    private readonly CheckBox _gridToggle = Toggle("Grid", true);
    private readonly CheckBox _smoothToggle = Toggle("Smoothing", false);
    private readonly CheckBox _lightingToggle = Toggle("Lighting", false);
    private readonly ComboBox _lightDirection = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 108 };
    private readonly ComboBox _palette = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 220 };
    private readonly Label _paletteLabel = new() { Text = "RAW palette:", AutoSize = true, Margin = new Padding(8, 7, 0, 0) };
    private readonly Button _backgroundButton = new() { Text = "Background…", AutoSize = true };
    private readonly Button _resetButton = new() { Text = "Reset view", AutoSize = true };
    private readonly ToolTip _toolTip = new();
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 16 };
    // One second a frame, the rate the stock REX animation is documented to run at.
    // It is not read from the file: every header word in REX.BIN past the frame count
    // and the magnify constant is zero, so there is no rate field there to read.
    private readonly System.Windows.Forms.Timer _frameTimer = new() { Interval = 1000 };
    private readonly Label _frameBanner = new()
    {
        Dock = DockStyle.Top, AutoSize = false, Height = 22, Visible = false,
        Padding = new Padding(6, 3, 0, 0), BackColor = Color.FromArgb(48, 44, 24), ForeColor = Color.Gold,
    };
    private readonly Button _playButton = new() { Text = "Play (A)", AutoSize = true, Visible = false };
    private PaletteChoices _paletteChoices = new([], 0);
    private IReadOnlyList<IReadOnlyList<LoadedBinTexture>> _loaded = [];
    private BinOpenGlRenderer? _renderer;
    private Point? _dragPoint;

    internal BinPreviewForm(string entryName, byte[] data, PodArchive? archive)
    {
        _entryName = entryName;
        _archive = archive;
        /*
          .SMF is 4x4 Evolution's static model format and shares nothing with .BIN but the
          job. It is detected by its "C3DModel" magic rather than by extension, so a
          mis-named entry still opens and a .BIN is never handed to the text parser.
        */
        _animation = SmfModelDecoder.IsSmfModel(data)
            ? SmfModelDecoder.Decode(data, entryName)
            : BinModelDecoder.Decode(data, entryName);
        _frames = BinFrameResolver.Resolve(archive, _animation);
        // An animated BIN carries no geometry of its own, so there is nothing to show
        // until its frames are loaded. C-POD opens on frame 1 and says so, and this
        // does the same rather than presenting an empty viewport.
        _frameModels = _frames.Count > 0
            ? [.. Enumerable.Range(0, _frames.Count).Select(index => LoadFrame(index) ?? _animation)]
            : [_animation];
        Text = (_animation.Format.StartsWith("SMF", StringComparison.Ordinal) ? "SMF Preview - " : "BIN Preview - ") + entryName;
        Icon = AppIcon.Shared;
        StartPosition = FormStartPosition.CenterParent;
        AutoScaleMode = AutoScaleMode.Dpi;
        Font = SystemFonts.MessageBoxFont!;
        ClientSize = new Size(1000, 720);
        MinimumSize = new Size(700, 500);
        KeyPreview = true;

        FlowLayoutPanel controls = new()
        {
            Dock = DockStyle.Top, AutoSize = true, WrapContents = true, Padding = new Padding(4),
            FlowDirection = FlowDirection.LeftToRight,
        };
        controls.Controls.AddRange([_resetButton, _textureToggle, _wireframeToggle, _gridToggle,
            _smoothToggle, _lightingToggle, new Label { Text = "Light:", AutoSize = true, Margin = new Padding(8, 7, 0, 0) },
            _lightDirection, _backgroundButton, _paletteLabel, _palette, _playButton]);
        Controls.Add(_surface);
        // Added straight after the viewport so it docks against it: the banner has to
        // sit across the top of what is being drawn, not up with the toolbar.
        Controls.Add(_frameBanner);
        Controls.Add(_textures);
        Controls.Add(_diagnostics);
        Controls.Add(_statistics);
        Controls.Add(controls);

        _lightDirection.Items.AddRange(["Top", "Front left", "Front right", "Rear left", "Rear right"]);
        _lightDirection.SelectedIndex = 0;
        UpdateStatistics();
        if (_frames.Count > 0)
        {
            _frameBanner.Visible = true;
            _playButton.Visible = true;
            UpdateFrameBanner();
            _playButton.Click += (_, _) => TogglePlayback();
            _frameTimer.Tick += (_, _) => ShowFrame(_frameIndex + 1);
        }

        BuildPaletteChoices();
        _surface.ContextInitialized += SurfaceContextInitialized;
        _surface.RenderFrame += _ => _renderer?.Render(_surface.ClientSize.Width, _surface.ClientSize.Height, CurrentOptions());
        _surface.MouseDown += (_, e) => { if (e.Button == MouseButtons.Left) { _dragPoint = e.Location; _surface.Focus(); } };
        _surface.MouseUp += (_, _) => _dragPoint = null;
        _surface.MouseMove += SurfaceMouseMove;
        _surface.MouseWheel += (_, e) => { _renderer?.Zoom(e.Delta); _surface.Invalidate(); };
        _resetButton.Click += (_, _) => { _renderer?.ResetView(); _surface.Invalidate(); };
        _backgroundButton.Click += ChooseBackground;
        _smoothToggle.CheckedChanged += (_, _) => { if (_surface.MakeCurrent()) _renderer?.SetSmoothing(_smoothToggle.Checked); _surface.Invalidate(); };
        _palette.SelectedIndexChanged += (_, _) => ReloadTextures();
        foreach (CheckBox toggle in new[] { _textureToggle, _wireframeToggle, _gridToggle, _lightingToggle })
            toggle.CheckedChanged += (_, _) => _surface.Invalidate();
        _lightDirection.SelectedIndexChanged += (_, _) => _surface.Invalidate();
        _timer.Tick += (_, _) => _surface.Invalidate();
        _timer.Start();
    }

    private static CheckBox Toggle(string text, bool value) => new()
    {
        Text = text, Checked = value, AutoSize = true, Margin = new Padding(6, 6, 0, 0),
    };

    private void UpdateStatistics() => _statistics.Text = string.Format(
        System.Globalization.CultureInfo.InvariantCulture,
        "{0} · {1:N0} vertices · {2:N0} polygons · {3:N0} meshes ({4:N0} transparent) · {5:N0} textures · magnification {6} · base Z {7:0.###}",
        _model.Format, _model.VertexCount, _model.PolygonCount, _model.Meshes.Count,
        _model.Meshes.Count(mesh => mesh.Transparent), _model.TextureNames.Count, _model.MagnifyPower, _model.BaseZ);

    /// <summary>Decodes one animation frame, or null when it is not in this archive.</summary>
    private BinModel? LoadFrame(int index)
    {
        if (_archive is null || index < 0 || index >= _frames.Count) return null;
        PodEntry? entry = _frames[index].Entry;
        if (entry is null) return null;
        try
        {
            byte[] bytes = _archive.GetEntryBytes(entry);
            return SmfModelDecoder.IsSmfModel(bytes)
                ? SmfModelDecoder.Decode(bytes, entry.Name)
                : BinModelDecoder.Decode(bytes, entry.Name);
        }
        catch (Exception ex) when (ex is IOException or PodFormatException)
        {
            return null;
        }
    }

    /// <summary>
    /// Shows a different frame of the animation. Every frame is already on the GPU, so
    /// this touches no buffers, rebuilds no grid and leaves the camera exactly where
    /// the viewer put it. A frame that does not resolve draws the empty parent model
    /// and says so in the banner, which is the case worth catching before sharing the
    /// pod.
    /// </summary>
    private void ShowFrame(int index)
    {
        if (_frames.Count == 0) return;
        _frameIndex = ((index % _frames.Count) + _frames.Count) % _frames.Count;
        _renderer?.SelectFrame(_frameIndex);
        UpdateFrameBanner();
        UpdateStatistics();
        _surface.Invalidate();
    }

    private void UpdateFrameBanner()
    {
        if (_frames.Count == 0) return;
        BinFrame frame = _frames[_frameIndex];
        _frameBanner.Text = string.Format(System.Globalization.CultureInfo.CurrentCulture,
            "Animated BIN · frame {0} of {1} · {2} {3}",
            _frameIndex + 1, _frames.Count, frame.Name,
            frame.Entry is null ? "(not in this archive)" : "from " + frame.Entry.Name);
    }

    private void TogglePlayback()
    {
        if (_frames.Count == 0) return;
        if (_frameTimer.Enabled)
        {
            _frameTimer.Stop();
            _playButton.Text = "Play (A)";
            return;
        }

        _frameTimer.Start();
        _playButton.Text = "Stop (A)";
    }

    private void BuildPaletteChoices()
    {
        PodEntry? firstRawWithoutPalette = null;
        if (_archive is not null)
        {
            foreach (string name in _frameModels.SelectMany(frame => frame.TextureNames).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                BinTextureEntries resolved = BinTextureResolver.Resolve(_archive, name);
                if (resolved.Diffuse is not null
                    && resolved.Diffuse.Name.EndsWith(".RAW", StringComparison.OrdinalIgnoreCase)
                    && resolved.SameNamePalette is null)
                {
                    firstRawWithoutPalette = resolved.Diffuse;
                    break;
                }
            }
        }

        _paletteChoices = PaletteResolver.ResolveChoices(firstRawWithoutPalette?.Name ?? _entryName, _archive, firstRawWithoutPalette?.RawNameField);
        _palette.Items.Clear();
        foreach (PaletteChoice choice in _paletteChoices.Choices) _palette.Items.Add(choice.Label);
        _palette.SelectedIndex = Math.Max(0, Math.Min(_paletteChoices.DefaultIndex, _palette.Items.Count - 1));
        _palette.Visible = firstRawWithoutPalette is not null;
        _paletteLabel.Visible = _palette.Visible;
    }

    private void SurfaceContextInitialized(object? sender, EventArgs e)
    {
        if (_surface.Gl is null || !_surface.MakeCurrent())
        {
            UpdateDiagnostics();
            return;
        }

        try
        {
            _renderer = new BinOpenGlRenderer(_surface.Gl);
            LoadTexturesAndUpload();
        }
        catch (Exception ex) when (ex is NotSupportedException or InvalidOperationException)
        {
            _renderer?.Dispose();
            _renderer = null;
            _diagnostics.Text = "BIN renderer initialization failed: " + ex.Message;
        }
    }

    private void ReloadTextures()
    {
        if (_renderer is null || _palette.SelectedIndex < 0 || !_surface.MakeCurrent()) return;
        LoadTexturesAndUpload(resetView: false);
        _surface.Invalidate();
    }

    private void LoadTexturesAndUpload(bool resetView = true)
    {
        int selected = Math.Max(0, Math.Min(_palette.SelectedIndex, _paletteChoices.Choices.Count - 1));
        int[] palette = _paletteChoices.Choices[selected].Palette;
        List<IReadOnlyList<LoadedBinTexture>> replacement = [];
        List<BinFrameGeometry> geometry = [];
        foreach (BinModel frame in _frameModels)
        {
            IReadOnlyList<LoadedBinTexture> textures = BinTextureLoader.Load(frame, _archive, palette);
            replacement.Add(textures);
            geometry.Add(new BinFrameGeometry(frame, textures));
        }

        _renderer!.SetFrames(geometry, _smoothToggle.Checked, resetView);
        _renderer.SelectFrame(_frameIndex);
        IReadOnlyList<IReadOnlyList<LoadedBinTexture>> old = _loaded;
        _loaded = replacement;
        bool usesFallback = replacement.Any(frame => frame.Any(item => item.UsedFallback));
        _palette.Visible = usesFallback;
        _paletteLabel.Visible = usesFallback;
        BuildTextureStrip();
        DisposeTextures(old);
        UpdateDiagnostics();
    }

    /// <summary>
    /// The textures of every frame, one entry per name. Frames of one animation are the
    /// same object in different poses and share their art, so the strip is the union
    /// rather than one strip per frame flickering past during playback.
    /// </summary>
    private IEnumerable<LoadedBinTexture> DistinctTextures()
    {
        HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);
        foreach (LoadedBinTexture texture in _loaded.SelectMany(frame => frame))
        {
            if (seen.Add(texture.Name)) yield return texture;
        }
    }

    private void BuildTextureStrip()
    {
        _textures.SuspendLayout();
        foreach (Control control in _textures.Controls.Cast<Control>().ToArray()) control.Dispose();
        _textures.Controls.Clear();
        foreach (LoadedBinTexture texture in DistinctTextures())
        {
            Panel item = new() { Width = 92, Height = 96, Margin = new Padding(3), Cursor = texture.Entry is null ? Cursors.Default : Cursors.Hand };
            PictureBox picture = new()
            {
                Left = 14, Top = 2, Width = 64, Height = 64, SizeMode = PictureBoxSizeMode.Zoom,
                BorderStyle = BorderStyle.FixedSingle, BackColor = Color.Magenta, Image = texture.Diffuse,
            };
            Label label = new()
            {
                Left = 1, Top = 68, Width = 90, Height = 27, TextAlign = ContentAlignment.TopCenter,
                AutoEllipsis = true, ForeColor = Color.White, Text = texture.Entry?.Name ?? texture.Name + " (missing)",
            };
            _toolTip.SetToolTip(picture, texture.Warning ?? texture.Entry?.Name ?? "Missing texture");
            item.Controls.Add(picture); item.Controls.Add(label);
            if (texture.Entry is not null)
            {
                LoadedBinTexture captured = texture;
                EventHandler open = (_, _) => OpenTexture(captured);
                item.Click += open; picture.Click += open; label.Click += open;
            }
            _textures.Controls.Add(item);
        }
        _textures.ResumeLayout();
    }

    private void OpenTexture(LoadedBinTexture texture)
    {
        if (_archive is null || texture.Entry is null) return;
        PreviewForm.Open(this, texture.Entry.Name, _archive.GetEntryBytes(texture.Entry), _archive, texture.Entry.RawNameField);
    }

    private void UpdateDiagnostics()
    {
        List<string> messages = [.. _frameModels.SelectMany(frame => frame.Warnings).Distinct()];
        messages.AddRange(DistinctTextures().Where(item => item.Warning is not null).Select(item => item.Warning!));
        foreach (BinFrame frame in _frames.Where(item => item.Entry is null))
        {
            // Naming these is the whole point: the animation plays for whoever built
            // the pod and stops dead for anyone without the pod the frame came from.
            messages.Add("Animation frame " + frame.Name + " resolves from outside this archive.");
        }
        if (_surface.InitializationError is not null) messages.Insert(0, _surface.InitializationError);
        // Worth saying even when the preview works: it names which renderer drew this.
        else if (OpenGlNativeLibrary.Status is not null) messages.Insert(0, OpenGlNativeLibrary.Status);
        _diagnostics.Text = messages.Count == 0 ? "No parser or texture warnings." : string.Join(Environment.NewLine, messages);
    }

    private void SurfaceMouseMove(object? sender, MouseEventArgs e)
    {
        if (_dragPoint is not Point previous || _renderer is null) return;
        _renderer.Orbit(e.X - previous.X, e.Y - previous.Y);
        _dragPoint = e.Location;
        _surface.Invalidate();
    }

    private void ChooseBackground(object? sender, EventArgs e)
    {
        using ColorDialog dialog = new() { Color = _surface.BackColor, FullOpen = true };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        _surface.BackColor = dialog.Color;
        _surface.Invalidate();
    }

    private BinRenderOptions CurrentOptions() => new(
        _textureToggle.Checked, _wireframeToggle.Checked, _gridToggle.Checked, _smoothToggle.Checked,
        _lightingToggle.Checked, LightPosition(), _surface.BackColor);

    private Vector3 LightPosition() => _lightDirection.SelectedIndex switch
    {
        1 => new Vector3(-2, 2, 2), 2 => new Vector3(2, 2, 2),
        3 => new Vector3(-2, 2, -2), 4 => new Vector3(2, 2, -2),
        _ => new Vector3(0, 3, 0),
    };

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == Keys.A && _frames.Count > 0)
        {
            TogglePlayback();
            return true;
        }
        if (keyData == Keys.Left || keyData == Keys.Right)
        {
            _renderer?.Strafe(keyData == Keys.Left ? -1 : 1);
            _surface.Invalidate();
            return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _timer.Stop();
        _frameTimer.Stop();
        if (_surface.MakeCurrent()) _renderer?.Dispose();
        _renderer = null;
        DisposeTextures(_loaded);
        _loaded = [];
        base.OnFormClosed(e);
    }

    private static void DisposeTextures(IReadOnlyList<IReadOnlyList<LoadedBinTexture>> frames)
    {
        foreach (LoadedBinTexture texture in frames.SelectMany(frame => frame))
        {
            texture.Diffuse?.Dispose();
            texture.Normal?.Dispose();
        }
    }
}
