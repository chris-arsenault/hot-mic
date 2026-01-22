using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using HotMic.App.UI.PluginComponents;
using HotMic.Core.Analysis;
using HotMic.Core.Dsp;
using HotMic.Core.Dsp.Analysis;
using HotMic.Core.Dsp.Spectrogram;
using SkiaSharp;

namespace HotMic.App.Views;

public partial class AnalysisSettingsWindow : AnalysisWindowBase
{
    private const int WmNcLButtonDown = 0x00A1;
    private const int HtCaption = 0x0002;

    private readonly AnalysisSettingsRenderer _renderer;

    public AnalysisSettingsWindow(AnalysisOrchestrator orchestrator)
        : base(orchestrator)
    {
        InitializeComponent();
        _renderer = new AnalysisSettingsRenderer(PluginComponentTheme.BlueOnBlack);
        InitializeSkiaSurface(SkiaHost);
        WireKnobHandlers();
        SyncKnobsFromConfig();
    }

    private void WireKnobHandlers()
    {
        _renderer.MinFreqKnob.ValueChanged += v => SetConfigValue(cfg => cfg.MinFrequency = v);
        _renderer.MaxFreqKnob.ValueChanged += v => SetConfigValue(cfg => cfg.MaxFrequency = v);
        _renderer.TimeKnob.ValueChanged += v => SetConfigValue(cfg => cfg.TimeWindow = v);
        _renderer.HpfKnob.ValueChanged += v => SetConfigValue(cfg => cfg.HighPassCutoff = v);
        _renderer.ReassignThresholdKnob.ValueChanged += v => SetConfigValue(cfg => cfg.ReassignThreshold = v);
        _renderer.ReassignSpreadKnob.ValueChanged += v => SetConfigValue(cfg => cfg.ReassignSpread = v / 100f);
        _renderer.ClarityNoiseKnob.ValueChanged += v => SetConfigValue(cfg => cfg.ClarityNoise = v / 100f);
        _renderer.ClarityHarmonicKnob.ValueChanged += v => SetConfigValue(cfg => cfg.ClarityHarmonic = v / 100f);
        _renderer.ClaritySmoothingKnob.ValueChanged += v => SetConfigValue(cfg => cfg.ClaritySmoothing = v / 100f);
    }

    private void SetConfigValue(Action<AnalysisConfiguration> setter)
    {
        setter(Orchestrator.Config);
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

    protected override AnalysisCapabilities ComputeRequiredCapabilities()
    {
        // Settings window doesn't need to subscribe to analysis data
        return AnalysisCapabilities.None;
    }

    protected override void OnRenderTick(object? sender, EventArgs e)
    {
        // Sync button states from config each tick (in case another window changes them)
        SyncButtonStatesFromConfig();
        InvalidateRenderSurface();
    }

    private void SyncButtonStatesFromConfig()
    {
        var config = Orchestrator.Config;
        _renderer.PreEmphasisEnabled = config.PreEmphasis;
        _renderer.HighPassEnabled = config.HighPassEnabled;
    }

    protected override void OnRender(SKCanvas canvas, int width, int height)
    {
        _renderer.Render(canvas, width, height);
    }

    #region Mouse Handling

    protected override void OnSkiaMouseDown(object sender, MouseButtonEventArgs e)
    {
        var pos = e.GetPosition(SkiaCanvas);
        float x = (float)pos.X;
        float y = (float)pos.Y;

        // Close button (check before title bar drag since it's inside the title bar)
        if (_renderer.CloseButtonRect.Contains(x, y))
        {
            Close();
            return;
        }

        // Title bar drag
        if (y < 40 && e.LeftButton == MouseButtonState.Pressed)
        {
            DragWindow();
            return;
        }

        // Check knob interaction
        if (TryHandleKnobMouseDown(x, y, e.ChangedButton))
        {
            return;
        }

        // Check button clicks
        HandleButtonClick(x, y);
    }

    protected override void OnSkiaMouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        var pos = e.GetPosition(SkiaCanvas);
        float x = (float)pos.X;
        float y = (float)pos.Y;
        bool leftDown = e.LeftButton == MouseButtonState.Pressed;
        bool shiftHeld = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;

        HandleKnobMouseMove(x, y, leftDown, shiftHeld);
    }

    protected override void OnSkiaMouseUp(object sender, MouseButtonEventArgs e)
    {
        HandleKnobMouseUp(e.ChangedButton);
    }

    protected override void OnSkiaMouseDownWinForms(object? sender, System.Windows.Forms.MouseEventArgs e)
    {
        float x = e.X;
        float y = e.Y;

        // Close button (check before title bar drag since it's inside the title bar)
        if (_renderer.CloseButtonRect.Contains(x, y))
        {
            Close();
            return;
        }

        if (y < 40 && e.Button == System.Windows.Forms.MouseButtons.Left)
        {
            DragWindow();
            return;
        }

        var button = ToWpfMouseButton(e.Button);
        if (button.HasValue && TryHandleKnobMouseDown(x, y, button.Value))
        {
            return;
        }

        HandleButtonClick(x, y);
    }

    protected override void OnSkiaMouseMoveWinForms(object? sender, System.Windows.Forms.MouseEventArgs e)
    {
        float x = e.X;
        float y = e.Y;
        bool leftDown = (e.Button & System.Windows.Forms.MouseButtons.Left) != 0;
        bool shiftHeld = (System.Windows.Forms.Control.ModifierKeys & System.Windows.Forms.Keys.Shift) != 0;

        HandleKnobMouseMove(x, y, leftDown, shiftHeld);
    }

    protected override void OnSkiaMouseUpWinForms(object? sender, System.Windows.Forms.MouseEventArgs e)
    {
        var button = ToWpfMouseButton(e.Button);
        if (button.HasValue)
        {
            HandleKnobMouseUp(button.Value);
        }
    }

    private bool TryHandleKnobMouseDown(float x, float y, MouseButton button)
    {
        if (SkiaCanvas is null) return false;

        foreach (var knob in _renderer.AllKnobs)
        {
            if (knob.HandleMouseDown(x, y, button, SkiaCanvas))
            {
                return true;
            }
        }
        return false;
    }

    private void HandleKnobMouseMove(float x, float y, bool leftDown, bool shiftHeld)
    {
        foreach (var knob in _renderer.AllKnobs)
        {
            knob.HandleMouseMove(x, y, leftDown, shiftHeld);
        }
    }

    private void HandleKnobMouseUp(MouseButton button)
    {
        foreach (var knob in _renderer.AllKnobs)
        {
            knob.HandleMouseUp(button);
        }
    }

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
            return;
        }
    }

    [DllImport("user32.dll")]
    private static extern bool ReleaseCapture();

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    private void DragWindow()
    {
        ReleaseCapture();
        var helper = new System.Windows.Interop.WindowInteropHelper(this);
        SendMessage(helper.Handle, WmNcLButtonDown, (IntPtr)HtCaption, IntPtr.Zero);
    }

    #endregion

    public override void Dispose()
    {
        _renderer.Dispose();
        base.Dispose();
    }
}
