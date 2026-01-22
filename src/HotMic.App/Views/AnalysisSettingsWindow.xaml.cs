using System;
using System.Windows.Input;
using HotMic.App.UI.PluginComponents;
using HotMic.Core.Analysis;
using HotMic.Core.Dsp;
using HotMic.Core.Dsp.Analysis;
using HotMic.Core.Dsp.Spectrogram;
using SkiaSharp;
using SkiaSharp.Views.Desktop;

namespace HotMic.App.Views;

public partial class AnalysisSettingsWindow : AnalysisWindowBase
{
    private readonly AnalysisSettingsRenderer _renderer = new(PluginComponentTheme.BlueOnBlack);

    public AnalysisSettingsWindow(AnalysisOrchestrator orchestrator)
        : base(orchestrator)
    {
        InitializeComponent();
        InitializeSkiaSurface(SkiaHost);

        WireKnobHandlers();
        SyncKnobsFromConfig();
    }

    protected override AnalysisCapabilities ComputeRequiredCapabilities() => AnalysisCapabilities.None;

    protected override void OnRenderTick(object? sender, EventArgs e)
    {
        // Sync button states from config each tick (in case another window changes them)
        SyncButtonStatesFromConfig();
        InvalidateRenderSurface();
    }

    protected override void OnRender(SKCanvas canvas, int width, int height)
    {
        _renderer.Render(canvas, width, height);
    }

    private void WireKnobHandlers()
    {
        _renderer.MinFreqKnob.ValueChanged += v => Orchestrator.Config.MinFrequency = v;
        _renderer.MaxFreqKnob.ValueChanged += v => Orchestrator.Config.MaxFrequency = v;
        _renderer.TimeKnob.ValueChanged += v => Orchestrator.Config.TimeWindow = v;
        _renderer.HpfKnob.ValueChanged += v => Orchestrator.Config.HighPassCutoff = v;
        _renderer.ReassignThresholdKnob.ValueChanged += v => Orchestrator.Config.ReassignThreshold = v;
        _renderer.ReassignSpreadKnob.ValueChanged += v => Orchestrator.Config.ReassignSpread = v / 100f;
        _renderer.ClarityNoiseKnob.ValueChanged += v => Orchestrator.Config.ClarityNoise = v / 100f;
        _renderer.ClarityHarmonicKnob.ValueChanged += v => Orchestrator.Config.ClarityHarmonic = v / 100f;
        _renderer.ClaritySmoothingKnob.ValueChanged += v => Orchestrator.Config.ClaritySmoothing = v / 100f;
    }

    private void SyncKnobsFromConfig()
    {
        var config = Orchestrator.Config;
        _renderer.MinFreqKnob.Value = config.MinFrequency;
        _renderer.MaxFreqKnob.Value = config.MaxFrequency;
        _renderer.TimeKnob.Value = config.TimeWindow;
        _renderer.HpfKnob.Value = config.HighPassCutoff;
        _renderer.ReassignThresholdKnob.Value = config.ReassignThreshold;
        _renderer.ReassignSpreadKnob.Value = config.ReassignSpread * 100f;
        _renderer.ClarityNoiseKnob.Value = config.ClarityNoise * 100f;
        _renderer.ClarityHarmonicKnob.Value = config.ClarityHarmonic * 100f;
        _renderer.ClaritySmoothingKnob.Value = config.ClaritySmoothing * 100f;

        // Sync discrete selectors
        _renderer.FftSizeIndex = Array.IndexOf(AnalysisConfiguration.FftSizes, config.FftSize);
        _renderer.OverlapIndex = config.OverlapIndex;
        _renderer.WindowIndex = (int)config.WindowFunction;
        _renderer.ScaleIndex = (int)config.FrequencyScale;
        _renderer.TransformIndex = (int)config.TransformType;
        _renderer.ReassignIndex = (int)config.ReassignMode;
        _renderer.ClarityIndex = (int)config.ClarityMode;
        _renderer.SmoothingIndex = (int)config.SmoothingMode;
        _renderer.NormalizationIndex = (int)config.NormalizationMode;
        _renderer.PitchAlgorithmIndex = (int)config.PitchAlgorithm;
        _renderer.PreEmphasisEnabled = config.PreEmphasis;
        _renderer.HighPassEnabled = config.HighPassEnabled;
    }

    private void SyncButtonStatesFromConfig()
    {
        var config = Orchestrator.Config;
        _renderer.PreEmphasisEnabled = config.PreEmphasis;
        _renderer.HighPassEnabled = config.HighPassEnabled;
    }

    #region WPF Mouse Handlers (CPU mode)

    protected override void OnSkiaMouseDown(object sender, MouseButtonEventArgs e)
    {
        var element = sender as System.Windows.FrameworkElement;
        if (element is null) return;

        var pos = e.GetPosition(element);
        float x = (float)pos.X;
        float y = (float)pos.Y;

        // Let knobs handle their own input (drag, right-click edit)
        foreach (var knob in _renderer.AllKnobs)
        {
            if (knob.HandleMouseDown(x, y, e.ChangedButton, element))
            {
                if (knob.IsDragging)
                    element.CaptureMouse();
                e.Handled = true;
                return;
            }
        }

        if (e.ChangedButton != MouseButton.Left)
            return;

        // Close button (check before title bar drag since it's inside the title bar)
        if (_renderer.CloseButtonRect.Contains(x, y))
        {
            Close();
            e.Handled = true;
            return;
        }

        // Title bar drag
        if (y < 40)
        {
            DragMove();
            e.Handled = true;
            return;
        }

        // Button clicks
        HandleButtonClick(x, y);
    }

    protected override void OnSkiaMouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        var element = sender as System.Windows.FrameworkElement;
        if (element is null) return;

        var pos = e.GetPosition(element);
        float x = (float)pos.X;
        float y = (float)pos.Y;

        foreach (var knob in _renderer.AllKnobs)
        {
            knob.HandleMouseMove(x, y, e.LeftButton == MouseButtonState.Pressed);
        }
    }

    protected override void OnSkiaMouseUp(object sender, MouseButtonEventArgs e)
    {
        var element = sender as System.Windows.FrameworkElement;
        if (element is null) return;

        foreach (var knob in _renderer.AllKnobs)
        {
            knob.HandleMouseUp(e.ChangedButton);
        }

        if (e.ChangedButton == MouseButton.Left)
            element.ReleaseMouseCapture();
    }

    #endregion

    #region WinForms Mouse Handlers (GPU mode)

    protected override void OnSkiaMouseDownWinForms(object? sender, System.Windows.Forms.MouseEventArgs e)
    {
        float x = e.X;
        float y = e.Y;

        var wpfButton = ToWpfMouseButton(e.Button);

        // Let knobs handle their own input (drag, right-click edit)
        foreach (var knob in _renderer.AllKnobs)
        {
            if (wpfButton.HasValue && knob.HandleMouseDown(x, y, wpfButton.Value, SkiaCanvas))
            {
                return;
            }
        }

        if (e.Button != System.Windows.Forms.MouseButtons.Left)
            return;

        // Close button (check before title bar drag since it's inside the title bar)
        if (_renderer.CloseButtonRect.Contains(x, y))
        {
            Close();
            return;
        }

        // Title bar drag
        if (y < 40)
        {
            DragWindow();
            return;
        }

        // Button clicks
        HandleButtonClick(x, y);
    }

    protected override void OnSkiaMouseMoveWinForms(object? sender, System.Windows.Forms.MouseEventArgs e)
    {
        float x = e.X;
        float y = e.Y;
        bool leftPressed = (e.Button & System.Windows.Forms.MouseButtons.Left) != 0;

        foreach (var knob in _renderer.AllKnobs)
        {
            knob.HandleMouseMove(x, y, leftPressed);
        }
    }

    protected override void OnSkiaMouseUpWinForms(object? sender, System.Windows.Forms.MouseEventArgs e)
    {
        var wpfButton = ToWpfMouseButton(e.Button);
        if (wpfButton.HasValue)
        {
            foreach (var knob in _renderer.AllKnobs)
            {
                knob.HandleMouseUp(wpfButton.Value);
            }
        }
    }

    #endregion

    private void HandleButtonClick(float x, float y)
    {
        var config = Orchestrator.Config;

        // FFT Size buttons
        for (int i = 0; i < _renderer.FftSizeButtonRects.Length; i++)
        {
            if (_renderer.FftSizeButtonRects[i].Contains(x, y))
            {
                config.FftSize = AnalysisConfiguration.FftSizes[i];
                _renderer.FftSizeIndex = i;
                return;
            }
        }

        // Overlap buttons
        for (int i = 0; i < _renderer.OverlapButtonRects.Length; i++)
        {
            if (_renderer.OverlapButtonRects[i].Contains(x, y))
            {
                config.OverlapIndex = i;
                _renderer.OverlapIndex = i;
                return;
            }
        }

        // Window function buttons
        for (int i = 0; i < _renderer.WindowButtonRects.Length; i++)
        {
            if (_renderer.WindowButtonRects[i].Contains(x, y))
            {
                config.WindowFunction = (WindowFunction)i;
                _renderer.WindowIndex = i;
                return;
            }
        }

        // Scale buttons
        for (int i = 0; i < _renderer.ScaleButtonRects.Length; i++)
        {
            if (_renderer.ScaleButtonRects[i].Contains(x, y))
            {
                config.FrequencyScale = (FrequencyScale)i;
                _renderer.ScaleIndex = i;
                return;
            }
        }

        // Transform type buttons
        for (int i = 0; i < _renderer.TransformButtonRects.Length; i++)
        {
            if (_renderer.TransformButtonRects[i].Contains(x, y))
            {
                config.TransformType = (SpectrogramTransformType)i;
                _renderer.TransformIndex = i;
                return;
            }
        }

        // Reassignment mode buttons
        for (int i = 0; i < _renderer.ReassignButtonRects.Length; i++)
        {
            if (_renderer.ReassignButtonRects[i].Contains(x, y))
            {
                config.ReassignMode = (SpectrogramReassignMode)i;
                _renderer.ReassignIndex = i;
                return;
            }
        }

        // Clarity mode buttons
        for (int i = 0; i < _renderer.ClarityButtonRects.Length; i++)
        {
            if (_renderer.ClarityButtonRects[i].Contains(x, y))
            {
                config.ClarityMode = (ClarityProcessingMode)i;
                _renderer.ClarityIndex = i;
                return;
            }
        }

        // Smoothing mode buttons
        for (int i = 0; i < _renderer.SmoothingButtonRects.Length; i++)
        {
            if (_renderer.SmoothingButtonRects[i].Contains(x, y))
            {
                config.SmoothingMode = (SpectrogramSmoothingMode)i;
                _renderer.SmoothingIndex = i;
                return;
            }
        }

        // Normalization mode buttons
        for (int i = 0; i < _renderer.NormalizationButtonRects.Length; i++)
        {
            if (_renderer.NormalizationButtonRects[i].Contains(x, y))
            {
                config.NormalizationMode = (SpectrogramNormalizationMode)i;
                _renderer.NormalizationIndex = i;
                return;
            }
        }

        // Pitch algorithm buttons
        for (int i = 0; i < _renderer.PitchAlgorithmButtonRects.Length; i++)
        {
            if (_renderer.PitchAlgorithmButtonRects[i].Contains(x, y))
            {
                config.PitchAlgorithm = (PitchDetectorType)i;
                _renderer.PitchAlgorithmIndex = i;
                return;
            }
        }

        // Toggle buttons
        if (_renderer.PreEmphasisButtonRect.Contains(x, y))
        {
            config.PreEmphasis = !config.PreEmphasis;
            _renderer.PreEmphasisEnabled = config.PreEmphasis;
            return;
        }

        if (_renderer.HighPassButtonRect.Contains(x, y))
        {
            config.HighPassEnabled = !config.HighPassEnabled;
            _renderer.HighPassEnabled = config.HighPassEnabled;
        }
    }

    public override void Dispose()
    {
        if (IsDisposed) return;
        _renderer.Dispose();
        base.Dispose();
        GC.SuppressFinalize(this);
    }
}
