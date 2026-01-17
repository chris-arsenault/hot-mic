using System;
using System.Collections.Generic;
using HotMic.Core.Analysis;
using HotMic.Core.Dsp;
using HotMic.Core.Dsp.Analysis;
using SkiaSharp;

namespace HotMic.App.UI.PluginComponents;

/// <summary>
/// Renderer for the standalone spectrogram visualizer window.
/// Provides spectrogram display with overlays, bloom, and color LUT.
/// </summary>
public sealed class SpectrogramRenderer : BaseVisualizerRenderer
{
    private const float ControlPanelHeight = 50f;
    private const float OverlayToggleWidth = 60f;
    private const float OverlayToggleHeight = 24f;
    private const float OverlayToggleSpacing = 4f;

    private readonly IAnalysisResultStore _store;
    private readonly DisplayPipeline _displayPipeline = new();

    // Rendering state
    private SKBitmap? _spectrogramBitmap;
    private int _bitmapWidth;
    private int _bitmapHeight;
    private int _bitmapRingStart;
    private uint[] _pixelBuffer = Array.Empty<uint>();
    private long _lastFrameId = -1;

    // Color mapping
    private SKColor[] _colorLut = Array.Empty<SKColor>();
    private int _colorMapIndex;
    private float _brightness = 1f;
    private float _gamma = 0.8f;
    private float _contrast = 1.2f;

    // Data buffers
    private float[] _spectrogramBuffer = Array.Empty<float>();
    private float[] _pitchBuffer = Array.Empty<float>();
    private float[] _confidenceBuffer = Array.Empty<float>();
    private byte[] _voicingBuffer = Array.Empty<byte>();
    private float[] _formantFrequencies = Array.Empty<float>();
    private float[] _formantBandwidths = Array.Empty<float>();
    private float[] _harmonicFrequencies = Array.Empty<float>();
    private float[] _harmonicMagnitudes = Array.Empty<float>();

    // Overlay toggles
    private bool _showPitch = true;
    private bool _showFormants = true;
    private bool _showHarmonics;
    private bool _showVoicing;
    private bool _showBloom = true;

    // Overlay paints
    private readonly SKPaint _pitchPaint;
    private readonly SKPaint _pitchLowConfPaint;
    private readonly SKPaint[] _formantPaints;
    private readonly SKPaint _harmonicPaint;
    private readonly SKPaint _voicedPaint;
    private readonly SKPaint _unvoicedPaint;

    // Toggle button rects for hit testing
    private readonly List<(SKRect rect, string name, Action toggle)> _toggleButtons = new();

    public SpectrogramRenderer(IAnalysisResultStore store, PluginComponentTheme? theme = null)
        : base(theme)
    {
        _store = store;

        // Initialize overlay paints
        _pitchPaint = new SKPaint
        {
            Color = new SKColor(0x00, 0xFF, 0x88),
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 2f
        };

        _pitchLowConfPaint = new SKPaint
        {
            Color = new SKColor(0x00, 0xFF, 0x88, 0x80),
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 2f,
            PathEffect = SKPathEffect.CreateDash(new[] { 6f, 4f }, 0)
        };

        _formantPaints = new[]
        {
            new SKPaint { Color = new SKColor(0xFF, 0x4B, 0x4B), IsAntialias = true, Style = SKPaintStyle.Fill },
            new SKPaint { Color = new SKColor(0xFF, 0x96, 0x2A), IsAntialias = true, Style = SKPaintStyle.Fill },
            new SKPaint { Color = new SKColor(0xFF, 0xD0, 0x3A), IsAntialias = true, Style = SKPaintStyle.Fill },
            new SKPaint { Color = new SKColor(0x4A, 0xFF, 0x4A), IsAntialias = true, Style = SKPaintStyle.Fill },
            new SKPaint { Color = new SKColor(0x4A, 0xD0, 0xFF), IsAntialias = true, Style = SKPaintStyle.Fill }
        };

        _harmonicPaint = new SKPaint
        {
            Color = new SKColor(0xAA, 0xFF, 0xAA, 0x99),
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 1f
        };

        _voicedPaint = new SKPaint
        {
            Color = new SKColor(0x00, 0xFF, 0x00, 0x28),
            Style = SKPaintStyle.Fill
        };

        _unvoicedPaint = new SKPaint
        {
            Color = new SKColor(0xFF, 0xA5, 0x00, 0x28),
            Style = SKPaintStyle.Fill
        };

        BuildColorLut();
    }

    public static SKSize GetPreferredSize() => new(1200, 700);

    protected override string GetTitle() => "Spectrogram";

    protected override void OnRender(SKCanvas canvas, SKSize size)
    {
        float controlPanelY = size.Height - ControlPanelHeight;
        var spectrogramRect = new SKRect(0, TitleBarHeight, size.Width, controlPanelY);

        // Update data from store
        UpdateDataFromStore();

        // Draw spectrogram
        DrawSpectrogram(canvas, spectrogramRect);

        // Draw overlays
        if (_showVoicing) DrawVoicingOverlay(canvas, spectrogramRect);
        if (_showHarmonics) DrawHarmonicOverlay(canvas, spectrogramRect);
        if (_showFormants) DrawFormantOverlay(canvas, spectrogramRect);
        if (_showPitch) DrawPitchOverlay(canvas, spectrogramRect);

        // Draw control panel
        DrawControlPanel(canvas, new SKRect(0, controlPanelY, size.Width, size.Height));
    }

    private void UpdateDataFromStore()
    {
        var config = _store.Config;
        int frameCapacity = _store.FrameCapacity;
        int displayBins = _store.DisplayBins;

        // Ensure buffers
        int spectrogramSize = frameCapacity * displayBins;
        if (_spectrogramBuffer.Length < spectrogramSize)
        {
            _spectrogramBuffer = new float[spectrogramSize];
        }

        if (_pitchBuffer.Length < frameCapacity)
        {
            _pitchBuffer = new float[frameCapacity];
            _confidenceBuffer = new float[frameCapacity];
            _voicingBuffer = new byte[frameCapacity];
        }

        int maxFormants = AnalysisConfiguration.MaxFormants;
        int formantSize = frameCapacity * maxFormants;
        if (_formantFrequencies.Length < formantSize)
        {
            _formantFrequencies = new float[formantSize];
            _formantBandwidths = new float[formantSize];
        }

        int maxHarmonics = AnalysisConfiguration.MaxHarmonics;
        int harmonicSize = frameCapacity * maxHarmonics;
        if (_harmonicFrequencies.Length < harmonicSize)
        {
            _harmonicFrequencies = new float[harmonicSize];
            _harmonicMagnitudes = new float[harmonicSize];
        }

        // Copy spectrogram data
        _store.TryGetSpectrogramRange(_lastFrameId, _spectrogramBuffer, out long latestFrameId, out int availableFrames, out _);

        // Copy pitch data
        _store.TryGetPitchRange(_lastFrameId, _pitchBuffer, _confidenceBuffer, _voicingBuffer, out _, out _, out _);

        // Copy formant data
        _store.TryGetFormantRange(_lastFrameId, _formantFrequencies, _formantBandwidths, out _, out _, out _);

        // Copy harmonic data
        _store.TryGetHarmonicRange(_lastFrameId, _harmonicFrequencies, _harmonicMagnitudes, out _, out _, out _);

        _lastFrameId = latestFrameId;
    }

    private void DrawSpectrogram(SKCanvas canvas, SKRect rect)
    {
        int availableFrames = _store.AvailableFrames;
        int displayBins = _store.DisplayBins;
        int frameCapacity = _store.FrameCapacity;
        long latestFrameId = _store.LatestFrameId;

        if (availableFrames <= 0 || displayBins <= 0)
        {
            canvas.DrawRect(rect, BackgroundPaint);
            return;
        }

        // Ensure bitmap
        int bitmapWidth = availableFrames;
        int bitmapHeight = displayBins;

        if (_spectrogramBitmap is null || _bitmapWidth != bitmapWidth || _bitmapHeight != bitmapHeight)
        {
            _spectrogramBitmap?.Dispose();
            _spectrogramBitmap = new SKBitmap(bitmapWidth, bitmapHeight, SKColorType.Rgba8888, SKAlphaType.Opaque);
            _bitmapWidth = bitmapWidth;
            _bitmapHeight = bitmapHeight;
            _pixelBuffer = new uint[bitmapWidth * bitmapHeight];
        }

        // Fill pixel buffer
        var config = _store.Config;
        float minDb = config.MinDb;
        float maxDb = config.MaxDb;
        float dbRange = MathF.Max(1f, maxDb - minDb);
        float invDbRange = 1f / dbRange;

        for (int frame = 0; frame < availableFrames; frame++)
        {
            int frameIndex = (int)((latestFrameId - availableFrames + 1 + frame) % frameCapacity);
            int srcOffset = frameIndex * displayBins;

            for (int bin = 0; bin < displayBins; bin++)
            {
                float magnitude = _spectrogramBuffer[srcOffset + bin];
                float db = magnitude > 1e-12f ? 20f * MathF.Log10(magnitude) : minDb;
                float normalized = Math.Clamp((db - minDb) * invDbRange, 0f, 1f);

                // Apply gamma and contrast
                normalized = MathF.Pow(normalized, _gamma);
                normalized = Math.Clamp((normalized - 0.5f) * _contrast + 0.5f, 0f, 1f);
                normalized = Math.Clamp(normalized * _brightness, 0f, 1f);

                int colorIndex = Math.Clamp((int)(normalized * 255), 0, 255);
                var color = _colorLut[colorIndex];

                // Y is inverted (low freq at bottom)
                int y = displayBins - 1 - bin;
                int pixelIndex = y * bitmapWidth + frame;
                _pixelBuffer[pixelIndex] = (uint)((color.Alpha << 24) | (color.Blue << 16) | (color.Green << 8) | color.Red);
            }
        }

        // Copy to bitmap
        unsafe
        {
            fixed (uint* src = _pixelBuffer)
            {
                var dstPtr = _spectrogramBitmap.GetPixels();
                Buffer.MemoryCopy(src, (void*)dstPtr, _pixelBuffer.Length * 4, _pixelBuffer.Length * 4);
            }
        }

        // Draw bitmap
        canvas.DrawBitmap(_spectrogramBitmap, rect);

        // Apply bloom if enabled
        if (_showBloom)
        {
            canvas.DrawBitmap(_spectrogramBitmap, rect, BloomPaint);
        }
    }

    private void DrawPitchOverlay(SKCanvas canvas, SKRect rect)
    {
        int availableFrames = _store.AvailableFrames;
        int frameCapacity = _store.FrameCapacity;
        long latestFrameId = _store.LatestFrameId;

        if (availableFrames <= 0) return;

        var config = _store.Config;
        float minFreq = config.MinFrequency;
        float maxFreq = config.MaxFrequency;
        float logMin = MathF.Log2(minFreq);
        float logMax = MathF.Log2(maxFreq);
        float logRange = logMax - logMin;

        float frameWidth = rect.Width / availableFrames;

        using var path = new SKPath();
        bool pathStarted = false;
        float lastConfidence = 0f;

        for (int i = 0; i < availableFrames; i++)
        {
            int frameIndex = (int)((latestFrameId - availableFrames + 1 + i) % frameCapacity);
            float pitch = _pitchBuffer[frameIndex];
            float confidence = _confidenceBuffer[frameIndex];
            var voicing = (VoicingState)_voicingBuffer[frameIndex];

            if (pitch <= 0f || voicing != VoicingState.Voiced)
            {
                if (pathStarted)
                {
                    var paint = lastConfidence > 0.5f ? _pitchPaint : _pitchLowConfPaint;
                    canvas.DrawPath(path, paint);
                    path.Reset();
                    pathStarted = false;
                }
                continue;
            }

            float logPitch = MathF.Log2(pitch);
            float normalized = (logPitch - logMin) / logRange;
            float y = rect.Bottom - normalized * rect.Height;
            float x = rect.Left + (i + 0.5f) * frameWidth;

            y = Math.Clamp(y, rect.Top, rect.Bottom);

            if (!pathStarted)
            {
                path.MoveTo(x, y);
                pathStarted = true;
            }
            else
            {
                path.LineTo(x, y);
            }

            lastConfidence = confidence;
        }

        if (pathStarted)
        {
            var paint = lastConfidence > 0.5f ? _pitchPaint : _pitchLowConfPaint;
            canvas.DrawPath(path, paint);
        }
    }

    private void DrawFormantOverlay(SKCanvas canvas, SKRect rect)
    {
        int availableFrames = _store.AvailableFrames;
        int frameCapacity = _store.FrameCapacity;
        long latestFrameId = _store.LatestFrameId;
        int maxFormants = AnalysisConfiguration.MaxFormants;

        if (availableFrames <= 0) return;

        var config = _store.Config;
        float minFreq = config.MinFrequency;
        float maxFreq = config.MaxFrequency;
        float logMin = MathF.Log2(minFreq);
        float logMax = MathF.Log2(maxFreq);
        float logRange = logMax - logMin;

        float frameWidth = rect.Width / availableFrames;
        int displayFormants = Math.Min(5, maxFormants);

        for (int i = 0; i < availableFrames; i++)
        {
            int frameIndex = (int)((latestFrameId - availableFrames + 1 + i) % frameCapacity);
            int formantOffset = frameIndex * maxFormants;
            float x = rect.Left + (i + 0.5f) * frameWidth;

            for (int f = 0; f < displayFormants; f++)
            {
                float freq = _formantFrequencies[formantOffset + f];
                if (freq <= 0f || freq < minFreq || freq > maxFreq) continue;

                float logFreq = MathF.Log2(freq);
                float normalized = (logFreq - logMin) / logRange;
                float y = rect.Bottom - normalized * rect.Height;

                canvas.DrawCircle(x, y, 3f, _formantPaints[f]);
            }
        }
    }

    private void DrawHarmonicOverlay(SKCanvas canvas, SKRect rect)
    {
        int availableFrames = _store.AvailableFrames;
        int frameCapacity = _store.FrameCapacity;
        long latestFrameId = _store.LatestFrameId;
        int maxHarmonics = AnalysisConfiguration.MaxHarmonics;

        if (availableFrames <= 0) return;

        var config = _store.Config;
        float minFreq = config.MinFrequency;
        float maxFreq = config.MaxFrequency;
        float logMin = MathF.Log2(minFreq);
        float logMax = MathF.Log2(maxFreq);
        float logRange = logMax - logMin;

        float frameWidth = rect.Width / availableFrames;

        for (int h = 0; h < Math.Min(12, maxHarmonics); h++)
        {
            using var path = new SKPath();
            bool pathStarted = false;

            for (int i = 0; i < availableFrames; i++)
            {
                int frameIndex = (int)((latestFrameId - availableFrames + 1 + i) % frameCapacity);
                int harmonicOffset = frameIndex * maxHarmonics;

                float freq = _harmonicFrequencies[harmonicOffset + h];
                if (freq <= 0f || freq < minFreq || freq > maxFreq)
                {
                    if (pathStarted)
                    {
                        canvas.DrawPath(path, _harmonicPaint);
                        path.Reset();
                        pathStarted = false;
                    }
                    continue;
                }

                float logFreq = MathF.Log2(freq);
                float normalized = (logFreq - logMin) / logRange;
                float y = rect.Bottom - normalized * rect.Height;
                float x = rect.Left + (i + 0.5f) * frameWidth;

                if (!pathStarted)
                {
                    path.MoveTo(x, y);
                    pathStarted = true;
                }
                else
                {
                    path.LineTo(x, y);
                }
            }

            if (pathStarted)
            {
                canvas.DrawPath(path, _harmonicPaint);
            }
        }
    }

    private void DrawVoicingOverlay(SKCanvas canvas, SKRect rect)
    {
        int availableFrames = _store.AvailableFrames;
        int frameCapacity = _store.FrameCapacity;
        long latestFrameId = _store.LatestFrameId;

        if (availableFrames <= 0) return;

        float frameWidth = rect.Width / availableFrames;

        for (int i = 0; i < availableFrames; i++)
        {
            int frameIndex = (int)((latestFrameId - availableFrames + 1 + i) % frameCapacity);
            var voicing = (VoicingState)_voicingBuffer[frameIndex];

            if (voicing == VoicingState.Silence) continue;

            float x = rect.Left + i * frameWidth;
            var paint = voicing == VoicingState.Voiced ? _voicedPaint : _unvoicedPaint;
            canvas.DrawRect(x, rect.Top, frameWidth, rect.Height, paint);
        }
    }

    private void DrawControlPanel(SKCanvas canvas, SKRect rect)
    {
        // Background
        canvas.DrawRect(rect, TitleBarPaint);
        canvas.DrawLine(rect.Left, rect.Top, rect.Right, rect.Top, BorderPaint);

        // Clear previous toggle buttons
        _toggleButtons.Clear();

        // Draw toggle buttons
        float buttonY = rect.Top + (rect.Height - OverlayToggleHeight) / 2f;
        float buttonX = PanelPadding;

        DrawToggleButtonAndTrack(canvas, ref buttonX, buttonY, "Pitch", _showPitch, () => _showPitch = !_showPitch);
        DrawToggleButtonAndTrack(canvas, ref buttonX, buttonY, "F1-5", _showFormants, () => _showFormants = !_showFormants);
        DrawToggleButtonAndTrack(canvas, ref buttonX, buttonY, "Harm", _showHarmonics, () => _showHarmonics = !_showHarmonics);
        DrawToggleButtonAndTrack(canvas, ref buttonX, buttonY, "Voice", _showVoicing, () => _showVoicing = !_showVoicing);

        buttonX += PanelPadding;
        DrawToggleButtonAndTrack(canvas, ref buttonX, buttonY, "Bloom", _showBloom, () => _showBloom = !_showBloom);

        // Color map selector (text display)
        buttonX += PanelPadding * 2;
        string[] colorMapNames = { "Vocal", "VocalWarm", "Gray", "Inferno", "Viridis", "Magma", "Blue" };
        string currentMap = colorMapNames[_colorMapIndex];
        LabelTextPaint.DrawText(canvas, $"Color: {currentMap}", buttonX, buttonY + 16f);
    }

    private void DrawToggleButtonAndTrack(SKCanvas canvas, ref float x, float y, string label, bool active, Action toggle)
    {
        var rect = new SKRect(x, y, x + OverlayToggleWidth, y + OverlayToggleHeight);
        DrawToggle(canvas, rect, label, active);
        _toggleButtons.Add((rect, label, toggle));
        x += OverlayToggleWidth + OverlayToggleSpacing;
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

    private void BuildColorLut()
    {
        _colorLut = SpectrogramColorMaps.GetColorMap(_colorMapIndex, 256);
    }

    public void SetColorMap(int index)
    {
        _colorMapIndex = Math.Clamp(index, 0, 6);
        BuildColorLut();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _spectrogramBitmap?.Dispose();
            _spectrogramBitmap = null;
            _pitchPaint.Dispose();
            _pitchLowConfPaint.Dispose();
            foreach (var paint in _formantPaints) paint.Dispose();
            _harmonicPaint.Dispose();
            _voicedPaint.Dispose();
            _unvoicedPaint.Dispose();
        }

        base.Dispose(disposing);
    }
}
