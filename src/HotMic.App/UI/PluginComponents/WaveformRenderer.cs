using System;
using System.Collections.Generic;
using HotMic.Core.Analysis;
using SkiaSharp;

namespace HotMic.App.UI.PluginComponents;

/// <summary>
/// Renderer for the waveform visualizer window.
/// Displays oscilloscope-style waveform with level meter and voicing state.
/// </summary>
public sealed class WaveformRenderer : BaseVisualizerRenderer
{
    private const float MeterWidth = 40f;
    private const float MeterPadding = 10f;
    private const float WaveformPadding = 8f;

    private readonly IAnalysisResultStore _store;
    private readonly LevelMeter _levelMeter;

    // Waveform data
    private float[] _waveformBuffer = Array.Empty<float>();
    private int _waveformLength;

    // Smoothed display values
    private float _displayLevel;
    private float _displayPeak;

    // Rendering
    private readonly SKPaint _waveformPaint;
    private readonly SKPaint _waveformFillPaint;
    private readonly SKPaint _gridPaint;
    private readonly SKPaint _zeroLinePaint;
    private readonly SKPaint _voicedIndicatorPaint;
    private readonly SKPaint _unvoicedIndicatorPaint;

    // Toggle buttons
    private bool _showFill = true;
    private bool _showVoicing = true;
    private readonly List<(SKRect rect, string name, Action toggle)> _toggleButtons = new();

    public WaveformRenderer(IAnalysisResultStore store, PluginComponentTheme? theme = null)
        : base(theme)
    {
        _store = store;
        _levelMeter = new LevelMeter(-60f, 0f, -6f, -3f);

        _waveformPaint = new SKPaint
        {
            Color = new SKColor(0x00, 0xCC, 0xFF),
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 1.5f,
            StrokeJoin = SKStrokeJoin.Round
        };

        _waveformFillPaint = new SKPaint
        {
            Color = new SKColor(0x00, 0xCC, 0xFF, 0x40),
            IsAntialias = true,
            Style = SKPaintStyle.Fill
        };

        _gridPaint = new SKPaint
        {
            Color = new SKColor(0x40, 0x40, 0x40),
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 1f
        };

        _zeroLinePaint = new SKPaint
        {
            Color = new SKColor(0x60, 0x60, 0x60),
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 1f
        };

        _voicedIndicatorPaint = new SKPaint
        {
            Color = new SKColor(0x00, 0xFF, 0x00, 0x60),
            IsAntialias = true,
            Style = SKPaintStyle.Fill
        };

        _unvoicedIndicatorPaint = new SKPaint
        {
            Color = new SKColor(0xFF, 0xA5, 0x00, 0x60),
            IsAntialias = true,
            Style = SKPaintStyle.Fill
        };
    }

    public static SKSize GetPreferredSize() => new(800, 400);

    protected override string GetTitle() => "Waveform";

    protected override void OnRender(SKCanvas canvas, SKSize size)
    {
        float controlPanelHeight = 40f;
        float controlPanelY = size.Height - controlPanelHeight;

        // Layout: [Waveform][Meter]
        float meterLeft = size.Width - MeterWidth - MeterPadding;
        var waveformRect = new SKRect(WaveformPadding, TitleBarHeight + WaveformPadding, meterLeft - WaveformPadding, controlPanelY - WaveformPadding);
        var meterRect = new SKRect(meterLeft, TitleBarHeight + MeterPadding, size.Width - MeterPadding, controlPanelY - MeterPadding);

        // Update data from store
        UpdateDataFromStore();

        // Draw grid
        DrawGrid(canvas, waveformRect);

        // Draw voicing background
        if (_showVoicing)
        {
            DrawVoicingBackground(canvas, waveformRect);
        }

        // Draw waveform
        DrawWaveform(canvas, waveformRect);

        // Draw level meter
        _levelMeter.Render(canvas, meterRect, MeterOrientation.Vertical);

        // Draw control panel
        DrawControlPanel(canvas, new SKRect(0, controlPanelY, size.Width, size.Height));
    }

    private void UpdateDataFromStore()
    {
        // Get waveform data
        int frameCapacity = _store.FrameCapacity;
        if (_waveformBuffer.Length < frameCapacity * 256)
        {
            _waveformBuffer = new float[frameCapacity * 256];
        }

        // Get latest waveform samples (for now, derive from spectrogram magnitudes or use audio input)
        // For simplicity, we'll visualize the most recent frame's audio
        var config = _store.Config;
        int sampleRate = config.SampleRate;

        // Calculate level from spectrogram (approximation)
        int displayBins = _store.DisplayBins;
        if (displayBins > 0 && _store.AvailableFrames > 0)
        {
            var spectrogramBuffer = new float[displayBins];
            _store.TryGetSpectrogramRange(_store.LatestFrameId, spectrogramBuffer, out _, out _, out _);

            // Sum energy
            float totalEnergy = 0f;
            for (int i = 0; i < displayBins; i++)
            {
                totalEnergy += spectrogramBuffer[i] * spectrogramBuffer[i];
            }
            float rms = MathF.Sqrt(totalEnergy / displayBins);
            _levelMeter.Update(rms);
        }
    }

    private void DrawGrid(SKCanvas canvas, SKRect rect)
    {
        // Horizontal grid lines
        float centerY = rect.MidY;
        canvas.DrawLine(rect.Left, centerY, rect.Right, centerY, _zeroLinePaint);

        // +/- 0.5 lines
        float halfHeight = rect.Height / 4f;
        canvas.DrawLine(rect.Left, centerY - halfHeight, rect.Right, centerY - halfHeight, _gridPaint);
        canvas.DrawLine(rect.Left, centerY + halfHeight, rect.Right, centerY + halfHeight, _gridPaint);

        // Border
        canvas.DrawRect(rect, BorderPaint);
    }

    private void DrawVoicingBackground(SKCanvas canvas, SKRect rect)
    {
        int availableFrames = _store.AvailableFrames;
        int frameCapacity = _store.FrameCapacity;
        long latestFrameId = _store.LatestFrameId;

        if (availableFrames <= 0) return;

        // Get voicing state
        var voicingBuffer = new byte[frameCapacity];
        _store.TryGetPitchRange(0, null!, null!, voicingBuffer, out _, out _, out _);

        float frameWidth = rect.Width / availableFrames;

        for (int i = 0; i < availableFrames; i++)
        {
            int frameIndex = (int)((latestFrameId - availableFrames + 1 + i) % frameCapacity);
            var voicing = (VoicingState)voicingBuffer[frameIndex];

            if (voicing == VoicingState.Silence) continue;

            float x = rect.Left + i * frameWidth;
            var paint = voicing == VoicingState.Voiced ? _voicedIndicatorPaint : _unvoicedIndicatorPaint;
            canvas.DrawRect(x, rect.Top, frameWidth, rect.Height, paint);
        }
    }

    private void DrawWaveform(SKCanvas canvas, SKRect rect)
    {
        int availableFrames = _store.AvailableFrames;
        int displayBins = _store.DisplayBins;
        int frameCapacity = _store.FrameCapacity;
        long latestFrameId = _store.LatestFrameId;

        if (availableFrames <= 0 || displayBins <= 0) return;

        // Use spectrogram energy for visualization
        var spectrogramBuffer = new float[frameCapacity * displayBins];
        _store.TryGetSpectrogramRange(0, spectrogramBuffer, out _, out _, out _);

        float frameWidth = rect.Width / availableFrames;
        float centerY = rect.MidY;
        float maxAmplitude = rect.Height / 2f - 4f;

        using var path = new SKPath();
        using var fillPath = new SKPath();

        path.MoveTo(rect.Left, centerY);
        fillPath.MoveTo(rect.Left, centerY);

        for (int i = 0; i < availableFrames; i++)
        {
            int frameIndex = (int)((latestFrameId - availableFrames + 1 + i) % frameCapacity);
            int srcOffset = frameIndex * displayBins;

            // Calculate RMS for this frame
            float sumSq = 0f;
            for (int bin = 0; bin < displayBins; bin++)
            {
                float val = spectrogramBuffer[srcOffset + bin];
                sumSq += val * val;
            }
            float rms = MathF.Sqrt(sumSq / displayBins);

            // Convert to visual amplitude (logarithmic)
            float db = rms > 1e-12f ? 20f * MathF.Log10(rms) : -100f;
            float normalizedDb = Math.Clamp((db + 60f) / 60f, 0f, 1f); // -60dB to 0dB range

            float amplitude = normalizedDb * maxAmplitude;
            float x = rect.Left + (i + 0.5f) * frameWidth;

            path.LineTo(x, centerY - amplitude);
            fillPath.LineTo(x, centerY - amplitude);
        }

        // Complete paths
        path.LineTo(rect.Right, centerY);
        fillPath.LineTo(rect.Right, centerY);
        fillPath.Close();

        // Draw fill first
        if (_showFill)
        {
            canvas.DrawPath(fillPath, _waveformFillPaint);

            // Mirror fill below zero line
            using var mirrorFill = new SKPath();
            mirrorFill.MoveTo(rect.Left, centerY);
            for (int i = 0; i < availableFrames; i++)
            {
                int frameIndex = (int)((latestFrameId - availableFrames + 1 + i) % frameCapacity);
                int srcOffset = frameIndex * displayBins;

                float sumSq = 0f;
                for (int bin = 0; bin < displayBins; bin++)
                {
                    float val = spectrogramBuffer[srcOffset + bin];
                    sumSq += val * val;
                }
                float rms = MathF.Sqrt(sumSq / displayBins);
                float db = rms > 1e-12f ? 20f * MathF.Log10(rms) : -100f;
                float normalizedDb = Math.Clamp((db + 60f) / 60f, 0f, 1f);
                float amplitude = normalizedDb * maxAmplitude;
                float x = rect.Left + (i + 0.5f) * frameWidth;

                mirrorFill.LineTo(x, centerY + amplitude);
            }
            mirrorFill.LineTo(rect.Right, centerY);
            mirrorFill.Close();
            canvas.DrawPath(mirrorFill, _waveformFillPaint);
        }

        // Draw line
        canvas.DrawPath(path, _waveformPaint);

        // Mirror waveform below zero
        using var mirrorPath = new SKPath();
        mirrorPath.MoveTo(rect.Left, centerY);
        for (int i = 0; i < availableFrames; i++)
        {
            int frameIndex = (int)((latestFrameId - availableFrames + 1 + i) % frameCapacity);
            int srcOffset = frameIndex * displayBins;

            float sumSq = 0f;
            for (int bin = 0; bin < displayBins; bin++)
            {
                float val = spectrogramBuffer[srcOffset + bin];
                sumSq += val * val;
            }
            float rms = MathF.Sqrt(sumSq / displayBins);
            float db = rms > 1e-12f ? 20f * MathF.Log10(rms) : -100f;
            float normalizedDb = Math.Clamp((db + 60f) / 60f, 0f, 1f);
            float amplitude = normalizedDb * maxAmplitude;
            float x = rect.Left + (i + 0.5f) * frameWidth;

            mirrorPath.LineTo(x, centerY + amplitude);
        }
        mirrorPath.LineTo(rect.Right, centerY);
        canvas.DrawPath(mirrorPath, _waveformPaint);
    }

    private void DrawControlPanel(SKCanvas canvas, SKRect rect)
    {
        canvas.DrawRect(rect, TitleBarPaint);
        canvas.DrawLine(rect.Left, rect.Top, rect.Right, rect.Top, BorderPaint);

        _toggleButtons.Clear();

        float buttonY = rect.Top + (rect.Height - ToggleHeight) / 2f;
        float buttonX = PanelPadding;

        DrawToggleButtonAndTrack(canvas, ref buttonX, buttonY, "Fill", _showFill, () => _showFill = !_showFill);
        DrawToggleButtonAndTrack(canvas, ref buttonX, buttonY, "Voice", _showVoicing, () => _showVoicing = !_showVoicing);

        // Level readout
        buttonX += PanelPadding * 2;
        float levelDb = _levelMeter.CurrentNormalized * 60f - 60f;
        LabelTextPaint.DrawText(canvas, $"Level: {levelDb:0.0} dB", buttonX, buttonY + 15f);
    }

    private void DrawToggleButtonAndTrack(SKCanvas canvas, ref float x, float y, string label, bool active, Action toggle)
    {
        var rect = new SKRect(x, y, x + ToggleWidth, y + ToggleHeight);
        DrawToggle(canvas, rect, label, active);
        _toggleButtons.Add((rect, label, toggle));
        x += ToggleWidth + ButtonSpacing;
    }

    public void HandleClick(float x, float y)
    {
        foreach (var (rect, _, toggle) in _toggleButtons)
        {
            if (rect.Contains(x, y))
            {
                toggle();
                return;
            }
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _waveformPaint.Dispose();
            _waveformFillPaint.Dispose();
            _gridPaint.Dispose();
            _zeroLinePaint.Dispose();
            _voicedIndicatorPaint.Dispose();
            _unvoicedIndicatorPaint.Dispose();
            _levelMeter.Dispose();
        }

        base.Dispose(disposing);
    }
}
