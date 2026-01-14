using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using HotMic.App.UI.PluginComponents;
using HotMic.Core.Dsp;
using HotMic.Core.Plugins.BuiltIn;
using SkiaSharp;
using SkiaSharp.Views.WPF;
using WpfToolTip = System.Windows.Controls.ToolTip;

namespace HotMic.App.Views;

public partial class VocalSpectrographWindow : Window
{
    private readonly VocalSpectrographRenderer _renderer = new(PluginComponentTheme.BlueOnBlack);
    private readonly VocalSpectrographPlugin _plugin;
    private readonly Action<int, float> _parameterCallback;
    private readonly Action<bool> _bypassCallback;
    private readonly DispatcherTimer _renderTimer;

    private int _activeKnob = -1;
    private float _dragStartY;
    private float _dragStartValue;
    private int _hoveredKnob = -1;
    private bool _isPaused;
    private long _latestFrameId = -1;
    private int _availableFrames;
    private long? _referenceFrameId;
    private WpfToolTip? _spectrogramToolTip;
    private string _currentTooltip = string.Empty;

    private int _lastDataVersion = -1;
    private float[] _spectrogram = Array.Empty<float>();
    private float[] _pitchTrack = Array.Empty<float>();
    private float[] _pitchConfidence = Array.Empty<float>();
    private float[] _formantFrequencies = Array.Empty<float>();
    private float[] _formantBandwidths = Array.Empty<float>();
    private byte[] _voicingStates = Array.Empty<byte>();
    private float[] _harmonicFrequencies = Array.Empty<float>();
    private int _bufferFrameCount;
    private int _bufferBins;
    private int _bufferMaxFormants;
    private int _bufferMaxHarmonics;

    private static readonly int[] FftSizes = { 1024, 2048, 4096, 8192 };
    private static readonly WindowFunction[] WindowFunctions =
    {
        WindowFunction.Hann,
        WindowFunction.Hamming,
        WindowFunction.BlackmanHarris,
        WindowFunction.Gaussian,
        WindowFunction.Kaiser
    };
    private static readonly float[] OverlapOptions = { 0.5f, 0.75f, 0.875f };
    private static readonly FrequencyScale[] Scales =
    {
        FrequencyScale.Linear,
        FrequencyScale.Logarithmic,
        FrequencyScale.Mel,
        FrequencyScale.Erb,
        FrequencyScale.Bark
    };
    private static readonly string[] NoteNames =
    {
        "C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B"
    };

    private static readonly (int paramIndex, float min, float max)[] KnobParams =
    {
        (VocalSpectrographPlugin.MinFrequencyIndex, 20f, 2000f),
        (VocalSpectrographPlugin.MaxFrequencyIndex, 2000f, 12000f),
        (VocalSpectrographPlugin.MinDbIndex, -120f, -20f),
        (VocalSpectrographPlugin.MaxDbIndex, -40f, 0f),
        (VocalSpectrographPlugin.TimeWindowIndex, 1f, 30f),
        (VocalSpectrographPlugin.HighPassCutoffIndex, 20f, 120f)
    };

    public VocalSpectrographWindow(VocalSpectrographPlugin plugin, Action<int, float> parameterCallback, Action<bool> bypassCallback)
    {
        InitializeComponent();
        _plugin = plugin;
        _parameterCallback = parameterCallback;
        _bypassCallback = bypassCallback;

        var preferredSize = VocalSpectrographRenderer.GetPreferredSize();
        Width = preferredSize.Width;
        Height = preferredSize.Height;

        _spectrogramToolTip = new WpfToolTip
        {
            Placement = PlacementMode.Relative,
            PlacementTarget = SkiaCanvas,
            StaysOpen = true
        };
        ToolTipService.SetInitialShowDelay(SkiaCanvas, 0);
        ToolTipService.SetShowDuration(SkiaCanvas, int.MaxValue);
        SkiaCanvas.ToolTip = _spectrogramToolTip;

        _renderTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        _renderTimer.Tick += OnRenderTick;
        Loaded += (_, _) =>
        {
            _plugin.SetVisualizationActive(true);
            _renderTimer.Start();
        };
        Closed += (_, _) =>
        {
            _renderTimer.Stop();
            _plugin.SetVisualizationActive(false);
            _renderer.Dispose();
        };
    }

    private void OnRenderTick(object? sender, EventArgs e)
    {
        EnsureBuffers();

        if (!_isPaused)
        {
            int dataVersion = _plugin.DataVersion;
            if (dataVersion != _lastDataVersion)
            {
                if (_plugin.CopySpectrogramData(_spectrogram, _pitchTrack, _pitchConfidence,
                        _formantFrequencies, _formantBandwidths, _voicingStates, _harmonicFrequencies))
                {
                    _lastDataVersion = dataVersion;
                }
            }

            _latestFrameId = _plugin.LatestFrameId;
            _availableFrames = _plugin.AvailableFrames;
            CullReferenceLine();
        }

        SkiaCanvas.InvalidateVisual();
    }

    private void EnsureBuffers()
    {
        int frames = Math.Max(1, _plugin.FrameCount);
        int bins = Math.Max(1, _plugin.DisplayBins);
        int maxFormants = _plugin.MaxFormantCount;
        int maxHarmonics = _plugin.MaxHarmonicCount;

        _bufferFrameCount = frames;
        _bufferBins = bins;
        _bufferMaxFormants = maxFormants;
        _bufferMaxHarmonics = maxHarmonics;

        int spectrogramLength = frames * bins;
        if (_spectrogram.Length != spectrogramLength)
        {
            _spectrogram = new float[spectrogramLength];
            _lastDataVersion = -1;
        }

        if (_pitchTrack.Length != frames)
        {
            _pitchTrack = new float[frames];
            _pitchConfidence = new float[frames];
            _voicingStates = new byte[frames];
            _lastDataVersion = -1;
        }

        int formantLength = frames * maxFormants;
        if (_formantFrequencies.Length != formantLength)
        {
            _formantFrequencies = new float[formantLength];
            _formantBandwidths = new float[formantLength];
            _lastDataVersion = -1;
        }

        int harmonicLength = frames * maxHarmonics;
        if (_harmonicFrequencies.Length != harmonicLength)
        {
            _harmonicFrequencies = new float[harmonicLength];
            _lastDataVersion = -1;
        }
    }

    private void SkiaCanvas_PaintSurface(object sender, SKPaintSurfaceEventArgs e)
    {
        var canvas = e.Surface.Canvas;
        var size = new SKSize(e.Info.Width, e.Info.Height);
        float dpiScale = GetDpiScale();

        var state = new VocalSpectrographState(
            FftSize: _plugin.FftSize,
            WindowFunction: _plugin.WindowFunction,
            Overlap: _plugin.Overlap,
            Scale: _plugin.Scale,
            MinFrequency: _plugin.MinFrequency,
            MaxFrequency: _plugin.MaxFrequency,
            MinDb: _plugin.MinDb,
            MaxDb: _plugin.MaxDb,
            TimeWindowSeconds: _plugin.TimeWindowSeconds,
            DisplayBins: _bufferBins,
            FrameCount: _bufferFrameCount,
            ColorMap: _plugin.ColorMap,
            ReassignMode: _plugin.ReassignMode,
            IsBypassed: _plugin.IsBypassed,
            IsPaused: _isPaused,
            ShowPitch: _plugin.ShowPitch,
            ShowFormants: _plugin.ShowFormants,
            ShowHarmonics: _plugin.ShowHarmonics,
            ShowVoicing: _plugin.ShowVoicing,
            PreEmphasisEnabled: _plugin.PreEmphasisEnabled,
            HighPassEnabled: _plugin.HighPassEnabled,
            HighPassCutoff: _plugin.HighPassCutoff,
            LatestFrameId: _latestFrameId,
            AvailableFrames: _availableFrames,
            ReferenceFrameId: _referenceFrameId,
            HoveredKnob: _hoveredKnob,
            DataVersion: _lastDataVersion,
            Spectrogram: _spectrogram,
            PitchTrack: _pitchTrack,
            FormantFrequencies: _formantFrequencies,
            FormantBandwidths: _formantBandwidths,
            VoicingStates: _voicingStates,
            HarmonicFrequencies: _harmonicFrequencies,
            MaxFormants: _bufferMaxFormants,
            MaxHarmonics: _bufferMaxHarmonics
        );

        _renderer.Render(canvas, size, dpiScale, state);
    }

    private void SkiaCanvas_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left)
        {
            return;
        }

        var pos = e.GetPosition(SkiaCanvas);
        float x = (float)pos.X;
        float y = (float)pos.Y;

        var hit = _renderer.HitTest(x, y);
        switch (hit.Area)
        {
            case SpectrographHitArea.TitleBar:
                DragMove();
                e.Handled = true;
                break;
            case SpectrographHitArea.CloseButton:
                Close();
                e.Handled = true;
                break;
            case SpectrographHitArea.BypassButton:
                _bypassCallback(!_plugin.IsBypassed);
                e.Handled = true;
                break;
            case SpectrographHitArea.FftButton:
                CycleFftSize();
                e.Handled = true;
                break;
            case SpectrographHitArea.WindowButton:
                CycleWindow();
                e.Handled = true;
                break;
            case SpectrographHitArea.OverlapButton:
                CycleOverlap();
                e.Handled = true;
                break;
            case SpectrographHitArea.ScaleButton:
                CycleScale();
                e.Handled = true;
                break;
            case SpectrographHitArea.ColorButton:
                CycleColorMap();
                e.Handled = true;
                break;
            case SpectrographHitArea.ReassignButton:
                CycleReassignMode();
                e.Handled = true;
                break;
            case SpectrographHitArea.PauseButton:
                TogglePause();
                e.Handled = true;
                break;
            case SpectrographHitArea.PitchToggle:
                ToggleParameter(VocalSpectrographPlugin.ShowPitchIndex, _plugin.ShowPitch);
                e.Handled = true;
                break;
            case SpectrographHitArea.FormantToggle:
                ToggleParameter(VocalSpectrographPlugin.ShowFormantsIndex, _plugin.ShowFormants);
                e.Handled = true;
                break;
            case SpectrographHitArea.HarmonicToggle:
                ToggleParameter(VocalSpectrographPlugin.ShowHarmonicsIndex, _plugin.ShowHarmonics);
                e.Handled = true;
                break;
            case SpectrographHitArea.VoicingToggle:
                ToggleParameter(VocalSpectrographPlugin.ShowVoicingIndex, _plugin.ShowVoicing);
                e.Handled = true;
                break;
            case SpectrographHitArea.PreEmphasisToggle:
                ToggleParameter(VocalSpectrographPlugin.PreEmphasisIndex, _plugin.PreEmphasisEnabled);
                e.Handled = true;
                break;
            case SpectrographHitArea.HpfToggle:
                ToggleParameter(VocalSpectrographPlugin.HighPassEnabledIndex, _plugin.HighPassEnabled);
                e.Handled = true;
                break;
            case SpectrographHitArea.Spectrogram:
                if (TrySetReferenceLine(x))
                {
                    e.Handled = true;
                }
                break;
            case SpectrographHitArea.Knob:
                _activeKnob = hit.KnobIndex;
                _dragStartY = y;
                _dragStartValue = GetKnobNormalizedValue(hit.KnobIndex);
                SkiaCanvas.CaptureMouse();
                e.Handled = true;
                break;
        }
    }

    private void SkiaCanvas_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        var pos = e.GetPosition(SkiaCanvas);
        float x = (float)pos.X;
        float y = (float)pos.Y;

        if (_activeKnob >= 0 && e.LeftButton == MouseButtonState.Pressed)
        {
            float deltaY = _dragStartY - y;
            float newNormalized = RotaryKnob.CalculateValueFromDrag(_dragStartValue, -deltaY, 0.004f);
            ApplyKnobValue(_activeKnob, newNormalized);
            SetTooltip(null);
            e.Handled = true;
        }
        else
        {
            var hit = _renderer.HitTest(x, y);
            _hoveredKnob = hit.Area == SpectrographHitArea.Knob ? hit.KnobIndex : -1;
            if (hit.Area == SpectrographHitArea.Spectrogram)
            {
                UpdateSpectrogramTooltip(x, y);
            }
            else
            {
                SetTooltip(null);
            }
        }
    }

    private void SkiaCanvas_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
    {
        _hoveredKnob = -1;
        SetTooltip(null);
    }

    private void SkiaCanvas_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left)
        {
            return;
        }

        _activeKnob = -1;
        SkiaCanvas.ReleaseMouseCapture();
    }

    private float GetKnobNormalizedValue(int knobIndex)
    {
        if (knobIndex < 0 || knobIndex >= KnobParams.Length)
        {
            return 0f;
        }

        float value = knobIndex switch
        {
            0 => _plugin.MinFrequency,
            1 => _plugin.MaxFrequency,
            2 => _plugin.MinDb,
            3 => _plugin.MaxDb,
            4 => _plugin.TimeWindowSeconds,
            5 => _plugin.HighPassCutoff,
            _ => 0f
        };

        var (_, min, max) = KnobParams[knobIndex];
        return (value - min) / (max - min);
    }

    private void ApplyKnobValue(int knobIndex, float normalizedValue)
    {
        if (knobIndex < 0 || knobIndex >= KnobParams.Length)
        {
            return;
        }

        normalizedValue = Math.Clamp(normalizedValue, 0f, 1f);
        var (paramIndex, min, max) = KnobParams[knobIndex];
        float value = min + normalizedValue * (max - min);
        _parameterCallback(paramIndex, value);
    }

    private void ToggleParameter(int index, bool current)
    {
        _parameterCallback(index, current ? 0f : 1f);
    }

    private void CycleFftSize()
    {
        int current = _plugin.FftSize;
        int index = Array.IndexOf(FftSizes, current);
        int next = FftSizes[(index + 1) % FftSizes.Length];
        _parameterCallback(VocalSpectrographPlugin.FftSizeIndex, next);
    }

    private void CycleWindow()
    {
        var current = _plugin.WindowFunction;
        int index = Array.IndexOf(WindowFunctions, current);
        int nextIndex = (index + 1) % WindowFunctions.Length;
        _parameterCallback(VocalSpectrographPlugin.WindowFunctionIndex, (float)WindowFunctions[nextIndex]);
    }

    private void CycleOverlap()
    {
        float current = _plugin.Overlap;
        int index = Array.IndexOf(OverlapOptions, current);
        int nextIndex = (index + 1) % OverlapOptions.Length;
        _parameterCallback(VocalSpectrographPlugin.OverlapIndex, OverlapOptions[nextIndex]);
    }

    private void CycleScale()
    {
        var current = _plugin.Scale;
        int index = Array.IndexOf(Scales, current);
        int nextIndex = (index + 1) % Scales.Length;
        _parameterCallback(VocalSpectrographPlugin.ScaleIndex, (float)Scales[nextIndex]);
    }

    private void CycleColorMap()
    {
        int current = _plugin.ColorMap;
        int count = Enum.GetValues<SpectrogramColorMap>().Length;
        int next = (current + 1) % count;
        _parameterCallback(VocalSpectrographPlugin.ColorMapIndex, next);
    }

    private void CycleReassignMode()
    {
        SpectrogramReassignMode next = _plugin.ReassignMode switch
        {
            SpectrogramReassignMode.Off => SpectrogramReassignMode.Frequency,
            SpectrogramReassignMode.Frequency => SpectrogramReassignMode.Time,
            SpectrogramReassignMode.Time => SpectrogramReassignMode.TimeFrequency,
            _ => SpectrogramReassignMode.Off
        };
        _parameterCallback(VocalSpectrographPlugin.ReassignModeIndex, (float)next);
    }

    private void TogglePause()
    {
        _isPaused = !_isPaused;
        if (!_isPaused)
        {
            _plugin.SetVisualizationActive(true);
            ClearLocalBuffers();
        }
        else
        {
            SetTooltip(null);
        }
    }

    private void ClearLocalBuffers()
    {
        Array.Clear(_spectrogram, 0, _spectrogram.Length);
        Array.Clear(_pitchTrack, 0, _pitchTrack.Length);
        Array.Clear(_pitchConfidence, 0, _pitchConfidence.Length);
        Array.Clear(_formantFrequencies, 0, _formantFrequencies.Length);
        Array.Clear(_formantBandwidths, 0, _formantBandwidths.Length);
        Array.Clear(_voicingStates, 0, _voicingStates.Length);
        Array.Clear(_harmonicFrequencies, 0, _harmonicFrequencies.Length);
        _lastDataVersion = -1;
        _latestFrameId = -1;
        _availableFrames = 0;
        _referenceFrameId = null;
    }

    private bool TrySetReferenceLine(float x)
    {
        if (_availableFrames <= 0 || _latestFrameId < 0)
        {
            return false;
        }

        if (!_renderer.TryGetSpectrogramRect(out var rect))
        {
            return false;
        }

        float t = Math.Clamp((x - rect.Left) / rect.Width, 0f, 1f);
        int columnIndex = (int)MathF.Round(t * Math.Max(1, _bufferFrameCount - 1));
        int padFrames = Math.Max(0, _bufferFrameCount - _availableFrames);
        int visibleIndex = columnIndex - padFrames;
        if (visibleIndex < 0 || visibleIndex >= _availableFrames)
        {
            return false;
        }

        long oldestFrameId = _latestFrameId - _availableFrames + 1;
        _referenceFrameId = oldestFrameId + visibleIndex;
        return true;
    }

    private void CullReferenceLine()
    {
        if (_referenceFrameId is null || _availableFrames <= 0 || _latestFrameId < 0)
        {
            return;
        }

        long oldestFrameId = _latestFrameId - _availableFrames + 1;
        if (_referenceFrameId.Value < oldestFrameId)
        {
            _referenceFrameId = null;
        }
    }

    private void UpdateSpectrogramTooltip(float x, float y)
    {
        if (_spectrogramToolTip is null)
        {
            return;
        }

        if (!_renderer.TryGetSpectrogramRect(out var rect) || !rect.Contains(x, y))
        {
            SetTooltip(null);
            return;
        }

        float frequency = GetFrequencyAtPosition(y, rect);
        string note = GetNearestNoteName(frequency);
        string text = $"{FormatFrequency(frequency)} ({note})";
        if (!string.Equals(_currentTooltip, text, StringComparison.Ordinal))
        {
            _currentTooltip = text;
            _spectrogramToolTip.Content = text;
        }

        _spectrogramToolTip.HorizontalOffset = x + 12f;
        _spectrogramToolTip.VerticalOffset = y + 12f;
        _spectrogramToolTip.IsOpen = true;
    }

    private void SetTooltip(string? text)
    {
        if (_spectrogramToolTip is null)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            _spectrogramToolTip.IsOpen = false;
            _currentTooltip = string.Empty;
            return;
        }

        if (!string.Equals(_currentTooltip, text, StringComparison.Ordinal))
        {
            _currentTooltip = text;
            _spectrogramToolTip.Content = text;
        }
    }

    private float GetFrequencyAtPosition(float y, SkiaSharp.SKRect rect)
    {
        float norm = Math.Clamp((rect.Bottom - y) / rect.Height, 0f, 1f);
        float minHz = _plugin.MinFrequency;
        float maxHz = _plugin.MaxFrequency;
        float scaledMin = FrequencyScaleUtils.ToScale(_plugin.Scale, minHz);
        float scaledMax = FrequencyScaleUtils.ToScale(_plugin.Scale, maxHz);
        float range = scaledMax - scaledMin;
        if (MathF.Abs(range) < 1e-6f)
        {
            return minHz;
        }

        float scaled = scaledMin + range * norm;
        return FrequencyScaleUtils.FromScale(_plugin.Scale, scaled);
    }

    private static string FormatFrequency(float frequency)
    {
        return frequency >= 1000f ? $"{frequency / 1000f:0.#} kHz" : $"{frequency:0} Hz";
    }

    private static string GetNearestNoteName(float frequency)
    {
        if (frequency <= 0f || float.IsNaN(frequency) || float.IsInfinity(frequency))
        {
            return "--";
        }

        float note = 69f + 12f * MathF.Log2(frequency / 440f);
        int midi = Math.Clamp((int)MathF.Round(note), 0, 127);
        int octave = midi / 12 - 1;
        string name = NoteNames[midi % 12];
        return $"{name}{octave}";
    }

    private float GetDpiScale()
    {
        var source = PresentationSource.FromVisual(this);
        return (float)(source?.CompositionTarget?.TransformToDevice.M11 ?? 1.0);
    }
}
