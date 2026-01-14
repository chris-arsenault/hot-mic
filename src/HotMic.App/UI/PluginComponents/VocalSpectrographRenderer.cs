using HotMic.Core.Dsp;
using SkiaSharp;
using System.Runtime.InteropServices;

namespace HotMic.App.UI.PluginComponents;

/// <summary>
/// Renderer for the Vocal Spectrograph plugin window.
/// </summary>
public sealed class VocalSpectrographRenderer : IDisposable
{
    private const float CornerRadius = 10f;
    private const float Padding = 14f;
    private const float TitleBarHeight = 36f;
    private const float ControlBarHeight = 56f;
    private const float KnobRadius = 24f;
    private const float AxisWidth = 50f;
    private const float ColorBarWidth = 22f;
    private const float TimeAxisHeight = 22f;
    private const float VoicingLaneHeight = 8f;
    private const float DisplayGamma = 0.8f;
    private const float DisplayContrast = 1.2f;
    private const int DisplayColorLevels = 32;

    private readonly PluginComponentTheme _theme;
    private readonly RotaryKnob _knob;

    private readonly SKPaint _backgroundPaint;
    private readonly SKPaint _panelPaint;
    private readonly SKPaint _borderPaint;
    private readonly SKPaint _titlePaint;
    private readonly SKPaint _textPaint;
    private readonly SKPaint _mutedTextPaint;
    private readonly SKPaint _gridPaint;
    private readonly SKPaint _buttonPaint;
    private readonly SKPaint _buttonActivePaint;
    private readonly SKPaint _buttonTextPaint;
    private readonly SKPaint _referencePaint;
    private readonly SKPaint _bitmapPaint;
    private readonly SKPaint _pitchPaint;
    private readonly SKPaint _formantPaint1;
    private readonly SKPaint _formantPaint2;
    private readonly SKPaint _formantPaint3;
    private readonly SKPaint _harmonicPaint;
    private readonly SKPaint _voicedPaint;
    private readonly SKPaint _unvoicedPaint;
    private readonly SKPaint _silencePaint;

    private readonly SKPoint[] _knobCenters = new SKPoint[6];

    private SKRect _titleBarRect;
    private SKRect _closeRect;
    private SKRect _bypassRect;
    private SKRect _fftRect;
    private SKRect _windowRect;
    private SKRect _overlapRect;
    private SKRect _scaleRect;
    private SKRect _colorRect;
    private SKRect _reassignRect;
    private SKRect _pauseRect;
    private SKRect _pitchToggleRect;
    private SKRect _formantToggleRect;
    private SKRect _harmonicToggleRect;
    private SKRect _voicingToggleRect;
    private SKRect _preEmphasisToggleRect;
    private SKRect _hpfToggleRect;
    private SKRect _spectrogramRect;

    private SKBitmap? _spectrogramBitmap;
    private int _bitmapWidth;
    private int _bitmapHeight;
    private int _lastDataVersion = -1;
    private int _lastColorMap = -1;
    private long _lastLatestFrameId = -1;
    private int _lastAvailableFrames = -1;
    private int _lastFrameCount = -1;
    private int _lastDisplayBins = -1;
    private int[] _pixelBuffer = Array.Empty<int>();
    private int[] _rowOffsets = Array.Empty<int>();
    private int[] _colorLut = Array.Empty<int>();
    private byte[] _colorIndexLut = Array.Empty<byte>();

    public VocalSpectrographRenderer(PluginComponentTheme? theme = null)
    {
        _theme = theme ?? PluginComponentTheme.Default;
        _knob = new RotaryKnob(_theme);

        _backgroundPaint = new SKPaint { Color = _theme.PanelBackground, IsAntialias = true, Style = SKPaintStyle.Fill };
        _panelPaint = new SKPaint { Color = _theme.PanelBackgroundLight, IsAntialias = true, Style = SKPaintStyle.Fill };
        _borderPaint = new SKPaint { Color = _theme.PanelBorder, IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1f };
        _titlePaint = new SKPaint
        {
            Color = _theme.TextPrimary,
            IsAntialias = true,
            TextSize = 14f,
            Typeface = SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Bold)
        };
        _textPaint = new SKPaint
        {
            Color = _theme.TextSecondary,
            IsAntialias = true,
            TextSize = 11f,
            Typeface = SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Normal)
        };
        _mutedTextPaint = new SKPaint
        {
            Color = _theme.TextMuted,
            IsAntialias = true,
            TextSize = 10f,
            Typeface = SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Normal)
        };
        _gridPaint = new SKPaint
        {
            Color = _theme.PanelBorder.WithAlpha(50),
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 1f
        };
        _buttonPaint = new SKPaint { Color = _theme.PanelBackgroundLight, IsAntialias = true, Style = SKPaintStyle.Fill };
        _buttonActivePaint = new SKPaint { Color = _theme.KnobArc.WithAlpha(120), IsAntialias = true, Style = SKPaintStyle.Fill };
        _buttonTextPaint = new SKPaint
        {
            Color = _theme.TextPrimary,
            IsAntialias = true,
            TextSize = 10f,
            TextAlign = SKTextAlign.Center,
            Typeface = SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Bold)
        };
        _referencePaint = new SKPaint
        {
            Color = _theme.TextPrimary.WithAlpha(180),
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 1.5f
        };
        _bitmapPaint = new SKPaint
        {
            FilterQuality = SKFilterQuality.None,
            IsAntialias = false
        };
        _pitchPaint = new SKPaint { Color = new SKColor(0x00, 0xFF, 0x88), IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 2f };
        _formantPaint1 = new SKPaint { Color = new SKColor(0xFF, 0x4B, 0x4B), IsAntialias = true, Style = SKPaintStyle.Fill };
        _formantPaint2 = new SKPaint { Color = new SKColor(0xFF, 0x96, 0x2A), IsAntialias = true, Style = SKPaintStyle.Fill };
        _formantPaint3 = new SKPaint { Color = new SKColor(0xFF, 0xD0, 0x3A), IsAntialias = true, Style = SKPaintStyle.Fill };
        _harmonicPaint = new SKPaint { Color = new SKColor(0xE0, 0xE0, 0xE6, 0x90), IsAntialias = true, Style = SKPaintStyle.Fill };
        _voicedPaint = new SKPaint { Color = new SKColor(0x00, 0xD4, 0xAA, 0x50), Style = SKPaintStyle.Fill };
        _unvoicedPaint = new SKPaint { Color = new SKColor(0x80, 0x80, 0x90, 0x40), Style = SKPaintStyle.Fill };
        _silencePaint = new SKPaint { Color = new SKColor(0x00, 0x00, 0x00, 0x60), Style = SKPaintStyle.Fill };
    }

    public static SKSize GetPreferredSize() => new(920, 640);

    public void Render(SKCanvas canvas, SKSize size, float dpiScale, VocalSpectrographState state)
    {
        canvas.Clear(SKColors.Transparent);
        canvas.Save();
        canvas.Scale(dpiScale);
        size = new SKSize(size.Width / dpiScale, size.Height / dpiScale);

        var outerRect = new SKRect(0, 0, size.Width, size.Height);
        canvas.DrawRoundRect(new SKRoundRect(outerRect, CornerRadius), _backgroundPaint);
        canvas.DrawRoundRect(new SKRoundRect(outerRect, CornerRadius), _borderPaint);

        DrawTitleBar(canvas, size, state);
        DrawControlBar(canvas, size, state);
        DrawSpectrogram(canvas, size, state);
        DrawKnobs(canvas, size, state);

        canvas.Restore();
    }

    public VocalSpectrographHitTest HitTest(float x, float y)
    {
        if (_closeRect.Contains(x, y))
        {
            return new VocalSpectrographHitTest(SpectrographHitArea.CloseButton, -1);
        }

        if (_bypassRect.Contains(x, y))
        {
            return new VocalSpectrographHitTest(SpectrographHitArea.BypassButton, -1);
        }

        if (_titleBarRect.Contains(x, y))
        {
            return new VocalSpectrographHitTest(SpectrographHitArea.TitleBar, -1);
        }

        if (_fftRect.Contains(x, y)) return new VocalSpectrographHitTest(SpectrographHitArea.FftButton, -1);
        if (_windowRect.Contains(x, y)) return new VocalSpectrographHitTest(SpectrographHitArea.WindowButton, -1);
        if (_overlapRect.Contains(x, y)) return new VocalSpectrographHitTest(SpectrographHitArea.OverlapButton, -1);
        if (_scaleRect.Contains(x, y)) return new VocalSpectrographHitTest(SpectrographHitArea.ScaleButton, -1);
        if (_colorRect.Contains(x, y)) return new VocalSpectrographHitTest(SpectrographHitArea.ColorButton, -1);
        if (_reassignRect.Contains(x, y)) return new VocalSpectrographHitTest(SpectrographHitArea.ReassignButton, -1);
        if (_pauseRect.Contains(x, y)) return new VocalSpectrographHitTest(SpectrographHitArea.PauseButton, -1);
        if (_pitchToggleRect.Contains(x, y)) return new VocalSpectrographHitTest(SpectrographHitArea.PitchToggle, -1);
        if (_formantToggleRect.Contains(x, y)) return new VocalSpectrographHitTest(SpectrographHitArea.FormantToggle, -1);
        if (_harmonicToggleRect.Contains(x, y)) return new VocalSpectrographHitTest(SpectrographHitArea.HarmonicToggle, -1);
        if (_voicingToggleRect.Contains(x, y)) return new VocalSpectrographHitTest(SpectrographHitArea.VoicingToggle, -1);
        if (_preEmphasisToggleRect.Contains(x, y)) return new VocalSpectrographHitTest(SpectrographHitArea.PreEmphasisToggle, -1);
        if (_hpfToggleRect.Contains(x, y)) return new VocalSpectrographHitTest(SpectrographHitArea.HpfToggle, -1);
        if (_spectrogramRect.Contains(x, y)) return new VocalSpectrographHitTest(SpectrographHitArea.Spectrogram, -1);

        for (int i = 0; i < _knobCenters.Length; i++)
        {
            if (Distance(_knobCenters[i], x, y) <= KnobRadius)
            {
                return new VocalSpectrographHitTest(SpectrographHitArea.Knob, i);
            }
        }

        return new VocalSpectrographHitTest(SpectrographHitArea.None, -1);
    }

    private void DrawTitleBar(SKCanvas canvas, SKSize size, VocalSpectrographState state)
    {
        _titleBarRect = new SKRect(0, 0, size.Width, TitleBarHeight);
        canvas.DrawRect(_titleBarRect, _panelPaint);
        canvas.DrawLine(0, TitleBarHeight, size.Width, TitleBarHeight, _borderPaint);
        canvas.DrawText("Vocal Spectrograph", Padding, TitleBarHeight / 2f + 5f, _titlePaint);

        float buttonSize = 18f;
        float right = size.Width - Padding;
        _closeRect = new SKRect(right - buttonSize, TitleBarHeight / 2f - buttonSize / 2f, right, TitleBarHeight / 2f + buttonSize / 2f);
        DrawIconButton(canvas, _closeRect, "X", _theme.TextPrimary);
        right -= buttonSize + 8f;

        _bypassRect = new SKRect(right - 48f, TitleBarHeight / 2f - 9f, right, TitleBarHeight / 2f + 9f);
        DrawPillButton(canvas, _bypassRect, state.IsBypassed ? "BYP" : "ON", state.IsBypassed);
    }

    private void DrawControlBar(SKCanvas canvas, SKSize size, VocalSpectrographState state)
    {
        float top = TitleBarHeight + Padding;
        var controlRect = new SKRect(Padding, top, size.Width - Padding, top + ControlBarHeight);
        canvas.DrawRoundRect(new SKRoundRect(controlRect, 6f), _panelPaint);

        float buttonWidth = 82f;
        float buttonHeight = 22f;
        float y = controlRect.Top + 6f;
        float x = controlRect.Left + 8f;

        _fftRect = new SKRect(x, y, x + buttonWidth, y + buttonHeight);
        DrawPillButton(canvas, _fftRect, $"FFT {state.FftSize}", false);
        x = _fftRect.Right + 8f;

        _windowRect = new SKRect(x, y, x + buttonWidth, y + buttonHeight);
        DrawPillButton(canvas, _windowRect, state.WindowFunction.ToString(), false);
        x = _windowRect.Right + 8f;

        _overlapRect = new SKRect(x, y, x + buttonWidth, y + buttonHeight);
        DrawPillButton(canvas, _overlapRect, $"{state.Overlap * 100f:0.#}%", false);
        x = _overlapRect.Right + 8f;

        _scaleRect = new SKRect(x, y, x + buttonWidth, y + buttonHeight);
        DrawPillButton(canvas, _scaleRect, state.Scale.ToString(), false);
        x = _scaleRect.Right + 8f;

        _colorRect = new SKRect(x, y, x + buttonWidth, y + buttonHeight);
        DrawPillButton(canvas, _colorRect, ((SpectrogramColorMap)state.ColorMap).ToString(), false);
        x = _colorRect.Right + 8f;

        _reassignRect = new SKRect(x, y, x + buttonWidth, y + buttonHeight);
        DrawPillButton(canvas, _reassignRect, FormatReassignLabel(state.ReassignMode),
            state.ReassignMode != SpectrogramReassignMode.Off);
        x = _reassignRect.Right + 8f;

        _pauseRect = new SKRect(x, y, x + buttonWidth, y + buttonHeight);
        DrawPillButton(canvas, _pauseRect, state.IsPaused ? "PAUSED" : "PAUSE", state.IsPaused);

        float toggleY = controlRect.Top + buttonHeight + 10f;
        float toggleWidth = 70f;
        float toggleHeight = 20f;
        float toggleX = controlRect.Left + 8f;

        _pitchToggleRect = new SKRect(toggleX, toggleY, toggleX + toggleWidth, toggleY + toggleHeight);
        DrawPillButton(canvas, _pitchToggleRect, "Pitch", state.ShowPitch);
        toggleX = _pitchToggleRect.Right + 6f;

        _formantToggleRect = new SKRect(toggleX, toggleY, toggleX + toggleWidth, toggleY + toggleHeight);
        DrawPillButton(canvas, _formantToggleRect, "Formant", state.ShowFormants);
        toggleX = _formantToggleRect.Right + 6f;

        _harmonicToggleRect = new SKRect(toggleX, toggleY, toggleX + toggleWidth, toggleY + toggleHeight);
        DrawPillButton(canvas, _harmonicToggleRect, "Harm", state.ShowHarmonics);
        toggleX = _harmonicToggleRect.Right + 6f;

        _voicingToggleRect = new SKRect(toggleX, toggleY, toggleX + toggleWidth, toggleY + toggleHeight);
        DrawPillButton(canvas, _voicingToggleRect, "Voice", state.ShowVoicing);
        toggleX = _voicingToggleRect.Right + 6f;

        _preEmphasisToggleRect = new SKRect(toggleX, toggleY, toggleX + toggleWidth, toggleY + toggleHeight);
        DrawPillButton(canvas, _preEmphasisToggleRect, "Emph", state.PreEmphasisEnabled);
        toggleX = _preEmphasisToggleRect.Right + 6f;

        _hpfToggleRect = new SKRect(toggleX, toggleY, toggleX + toggleWidth, toggleY + toggleHeight);
        DrawPillButton(canvas, _hpfToggleRect, "HPF", state.HighPassEnabled);
    }

    private void DrawSpectrogram(SKCanvas canvas, SKSize size, VocalSpectrographState state)
    {
        float top = TitleBarHeight + Padding + ControlBarHeight + Padding;
        float bottom = size.Height - Padding - 140f;
        var spectrumRect = new SKRect(Padding + AxisWidth, top, size.Width - Padding - ColorBarWidth, bottom);
        var axisRect = new SKRect(Padding, top, Padding + AxisWidth, bottom);
        var colorRect = new SKRect(spectrumRect.Right + 6f, top, spectrumRect.Right + 6f + ColorBarWidth, bottom);
        _spectrogramRect = spectrumRect;

        canvas.DrawRoundRect(new SKRoundRect(spectrumRect, 6f), _panelPaint);

        UpdateSpectrogramBitmap(state);
        if (_spectrogramBitmap is not null)
        {
            canvas.DrawBitmap(_spectrogramBitmap, spectrumRect, _bitmapPaint);
        }

        DrawFrequencyAxis(canvas, axisRect, state);
        DrawColorBar(canvas, colorRect, state);
        DrawTimeAxis(canvas, spectrumRect, state);
        DrawOverlays(canvas, spectrumRect, state);
        DrawReferenceLine(canvas, spectrumRect, state);
    }

    private void DrawKnobs(SKCanvas canvas, SKSize size, VocalSpectrographState state)
    {
        float knobsTop = size.Height - Padding - 120f;
        float knobSpacing = (size.Width - Padding * 2f) / (_knobCenters.Length + 1);
        for (int i = 0; i < _knobCenters.Length; i++)
        {
            float rowOffset = i >= 3 ? 46f : 0f;
            int rowIndex = i >= 3 ? i - 3 : i;
            _knobCenters[i] = new SKPoint(Padding + knobSpacing * (rowIndex + 1), knobsTop + KnobRadius + rowOffset);
        }

        float minFreqNorm = Normalize(state.MinFrequency, 20f, 2000f);
        _knob.Render(canvas, _knobCenters[0], KnobRadius, minFreqNorm, "MIN FREQ", FormatHz(state.MinFrequency), "Hz", state.HoveredKnob == 0);

        float maxFreqNorm = Normalize(state.MaxFrequency, 2000f, 12000f);
        _knob.Render(canvas, _knobCenters[1], KnobRadius, maxFreqNorm, "MAX FREQ", FormatHz(state.MaxFrequency), "Hz", state.HoveredKnob == 1);

        float minDbNorm = Normalize(state.MinDb, -120f, -20f);
        _knob.Render(canvas, _knobCenters[2], KnobRadius, minDbNorm, "MIN dB", $"{state.MinDb:0}", "dB", state.HoveredKnob == 2);

        float maxDbNorm = Normalize(state.MaxDb, -40f, 0f);
        _knob.Render(canvas, _knobCenters[3], KnobRadius, maxDbNorm, "MAX dB", $"{state.MaxDb:0}", "dB", state.HoveredKnob == 3);

        float timeNorm = Normalize(state.TimeWindowSeconds, 1f, 30f);
        _knob.Render(canvas, _knobCenters[4], KnobRadius, timeNorm, "TIME", $"{state.TimeWindowSeconds:0.0}", "s", state.HoveredKnob == 4);

        float hpfNorm = Normalize(state.HighPassCutoff, 20f, 120f);
        _knob.Render(canvas, _knobCenters[5], KnobRadius, hpfNorm, "HPF", $"{state.HighPassCutoff:0}", "Hz", state.HoveredKnob == 5);
    }

    private void UpdateSpectrogramBitmap(VocalSpectrographState state)
    {
        if (state.Spectrogram is null || state.FrameCount <= 0 || state.DisplayBins <= 0)
        {
            return;
        }

        bool sizeChanged = _spectrogramBitmap is null || state.FrameCount != _bitmapWidth || state.DisplayBins != _bitmapHeight;
        if (sizeChanged)
        {
            _spectrogramBitmap?.Dispose();
            _spectrogramBitmap = new SKBitmap(state.FrameCount, state.DisplayBins, SKColorType.Bgra8888, SKAlphaType.Premul);
            _bitmapWidth = state.FrameCount;
            _bitmapHeight = state.DisplayBins;
            _lastDataVersion = -1;
            _lastLatestFrameId = -1;
            _lastAvailableFrames = -1;
            _lastFrameCount = state.FrameCount;
            _lastDisplayBins = state.DisplayBins;
            _pixelBuffer = new int[_bitmapWidth * _bitmapHeight];
            UpdateRowOffsets(_bitmapWidth, _bitmapHeight);
        }

        if (state.DataVersion == _lastDataVersion && state.ColorMap == _lastColorMap
            && state.LatestFrameId == _lastLatestFrameId && state.AvailableFrames == _lastAvailableFrames)
        {
            return;
        }

        int width = _bitmapWidth;
        int height = _bitmapHeight;
        int bins = state.DisplayBins;
        if (state.Spectrogram.Length < width * bins)
        {
            return;
        }

        bool colorChanged = state.ColorMap != _lastColorMap;
        if (colorChanged || _colorLut.Length != 256)
        {
            UpdateColorLut(state.ColorMap);
        }

        if (state.AvailableFrames <= 0 || state.LatestFrameId < 0)
        {
            if (_pixelBuffer.Length > 0)
            {
                Array.Clear(_pixelBuffer, 0, _pixelBuffer.Length);
            }

            var emptyPtr = _spectrogramBitmap!.GetPixels();
            if (emptyPtr != IntPtr.Zero)
            {
                Marshal.Copy(_pixelBuffer, 0, emptyPtr, _pixelBuffer.Length);
                _spectrogramBitmap.NotifyPixelsChanged();
            }

            _lastDataVersion = state.DataVersion;
            _lastColorMap = state.ColorMap;
            _lastLatestFrameId = state.LatestFrameId;
            _lastAvailableFrames = state.AvailableFrames;
            _lastFrameCount = state.FrameCount;
            _lastDisplayBins = state.DisplayBins;
            return;
        }

        bool fullRebuild = sizeChanged
            || colorChanged
            || _lastLatestFrameId < 0
            || state.LatestFrameId < 0
            || state.FrameCount != _lastFrameCount
            || state.DisplayBins != _lastDisplayBins
            || state.AvailableFrames < _lastAvailableFrames;

        long deltaFrames = state.LatestFrameId - _lastLatestFrameId;
        if (!fullRebuild)
        {
            if (deltaFrames <= 0 || deltaFrames >= width)
            {
                fullRebuild = true;
            }
        }

        if (fullRebuild)
        {
            RenderFullSpectrogram(state, width, bins);
        }
        else
        {
            ShiftSpectrogramLeft((int)deltaFrames, width, height);
            UpdateSpectrogramColumns(state, width, bins, (int)deltaFrames);
        }

        var pixelsPtr = _spectrogramBitmap!.GetPixels();
        if (pixelsPtr != IntPtr.Zero)
        {
            Marshal.Copy(_pixelBuffer, 0, pixelsPtr, _pixelBuffer.Length);
            _spectrogramBitmap.NotifyPixelsChanged();
        }

        _lastDataVersion = state.DataVersion;
        _lastColorMap = state.ColorMap;
        _lastLatestFrameId = state.LatestFrameId;
        _lastAvailableFrames = state.AvailableFrames;
        _lastFrameCount = state.FrameCount;
        _lastDisplayBins = state.DisplayBins;
    }

    private void RenderFullSpectrogram(VocalSpectrographState state, int width, int bins)
    {
        int totalFrames = width;
        for (int frame = 0; frame < totalFrames; frame++)
        {
            int frameOffset = frame * bins;
            for (int bin = 0; bin < bins; bin++)
            {
                float value = state.Spectrogram![frameOffset + bin];
                int colorIndex = value <= 0f ? 0 : value >= 1f ? 255 : (int)(value * 255f);
                int mappedIndex = _colorIndexLut[colorIndex];
                _pixelBuffer[_rowOffsets[bin] + frame] = _colorLut[mappedIndex];
            }
        }
    }

    private void ShiftSpectrogramLeft(int deltaFrames, int width, int height)
    {
        if (deltaFrames <= 0 || deltaFrames >= width)
        {
            return;
        }

        int tail = width - deltaFrames;
        for (int row = 0; row < height; row++)
        {
            int rowStart = row * width;
            Array.Copy(_pixelBuffer, rowStart + deltaFrames, _pixelBuffer, rowStart, tail);
        }
    }

    private void UpdateSpectrogramColumns(VocalSpectrographState state, int width, int bins, int deltaFrames)
    {
        if (deltaFrames <= 0)
        {
            return;
        }

        int startColumn = Math.Max(0, width - deltaFrames);
        for (int frame = startColumn; frame < width; frame++)
        {
            int frameOffset = frame * bins;
            for (int bin = 0; bin < bins; bin++)
            {
                float value = state.Spectrogram![frameOffset + bin];
                int colorIndex = value <= 0f ? 0 : value >= 1f ? 255 : (int)(value * 255f);
                int mappedIndex = _colorIndexLut[colorIndex];
                _pixelBuffer[_rowOffsets[bin] + frame] = _colorLut[mappedIndex];
            }
        }
    }

    private void UpdateRowOffsets(int width, int height)
    {
        if (_rowOffsets.Length != height)
        {
            _rowOffsets = new int[height];
        }

        for (int bin = 0; bin < height; bin++)
        {
            int y = height - 1 - bin;
            _rowOffsets[bin] = y * width;
        }
    }

    private void UpdateColorLut(int colorMap)
    {
        var colors = SpectrogramColorMaps.GetColors((SpectrogramColorMap)colorMap);
        if (_colorLut.Length != 256)
        {
            _colorLut = new int[256];
        }
        if (_colorIndexLut.Length != 256)
        {
            _colorIndexLut = new byte[256];
        }

        for (int i = 0; i < 256; i++)
        {
            var color = colors[i];
            _colorLut[i] = color.Blue | (color.Green << 8) | (color.Red << 16) | (color.Alpha << 24);
        }

        int levels = Math.Max(2, DisplayColorLevels);
        float invLevels = 1f / (levels - 1);
        for (int i = 0; i < 256; i++)
        {
            float value = i / 255f;
            value = MathF.Pow(value, DisplayGamma);
            value = (value - 0.5f) * DisplayContrast + 0.5f;
            value = Math.Clamp(value, 0f, 1f);
            int quant = (int)MathF.Round(value * (levels - 1));
            float quantValue = quant * invLevels;
            int mappedIndex = (int)MathF.Round(quantValue * 255f);
            _colorIndexLut[i] = (byte)Math.Clamp(mappedIndex, 0, 255);
        }
    }

    private void DrawFrequencyAxis(SKCanvas canvas, SKRect axisRect, VocalSpectrographState state)
    {
        canvas.DrawRect(axisRect, _panelPaint);
        float[] labels = { 80f, 150f, 250f, 500f, 1000f, 2000f, 4000f, 8000f };
        foreach (float freq in labels)
        {
            if (freq < state.MinFrequency || freq > state.MaxFrequency)
            {
                continue;
            }

            float norm = FrequencyScaleUtils.Normalize(state.Scale, freq, state.MinFrequency, state.MaxFrequency);
            float y = axisRect.Bottom - norm * axisRect.Height;
            canvas.DrawLine(axisRect.Right - 6f, y, axisRect.Right, y, _gridPaint);
            canvas.DrawText(FormatHz(freq), axisRect.Left + 4f, y + 4f, _mutedTextPaint);
        }
    }

    private void DrawColorBar(SKCanvas canvas, SKRect rect, VocalSpectrographState state)
    {
        var colors = SpectrogramColorMaps.GetColors((SpectrogramColorMap)state.ColorMap);
        using var paint = new SKPaint
        {
            Shader = SKShader.CreateLinearGradient(
                new SKPoint(rect.Left, rect.Top),
                new SKPoint(rect.Left, rect.Bottom),
                new[] { colors[255], colors[0] },
                null,
                SKShaderTileMode.Clamp)
        };
        canvas.DrawRoundRect(new SKRoundRect(rect, 4f), paint);
        canvas.DrawRoundRect(new SKRoundRect(rect, 4f), _borderPaint);
    }

    private void DrawTimeAxis(SKCanvas canvas, SKRect rect, VocalSpectrographState state)
    {
        float bottom = rect.Bottom + 6f;
        canvas.DrawLine(rect.Left, rect.Bottom, rect.Right, rect.Bottom, _gridPaint);
        canvas.DrawText("0s", rect.Left, bottom + 12f, _mutedTextPaint);
        canvas.DrawText($"{state.TimeWindowSeconds:0.0}s", rect.Right - 24f, bottom + 12f, _mutedTextPaint);
    }

    private void DrawOverlays(SKCanvas canvas, SKRect rect, VocalSpectrographState state)
    {
        if (state.PitchTrack is { Length: > 0 } && state.ShowPitch)
        {
            int frames = Math.Min(state.FrameCount, state.PitchTrack.Length);
            int step = GetOverlayStep(rect, frames);
            using var path = new SKPath();
            bool started = false;
            for (int frame = 0; frame < frames; frame += step)
            {
                float pitch = state.PitchTrack[frame];
                if (pitch <= 0f)
                {
                    started = false;
                    continue;
                }

                float norm = FrequencyScaleUtils.Normalize(state.Scale, pitch, state.MinFrequency, state.MaxFrequency);
                float x = rect.Left + rect.Width * frame / Math.Max(1, state.FrameCount - 1);
                float y = rect.Bottom - norm * rect.Height;
                if (!started)
                {
                    path.MoveTo(x, y);
                    started = true;
                }
                else
                {
                    path.LineTo(x, y);
                }
            }
            canvas.DrawPath(path, _pitchPaint);
        }

        if (state.FormantFrequencies is { Length: > 0 } && state.ShowFormants)
        {
            int maxFormants = state.MaxFormants;
            if (maxFormants > 0)
            {
                int frames = Math.Min(state.FrameCount, state.FormantFrequencies.Length / maxFormants);
                int step = GetOverlayStep(rect, frames);
                for (int frame = 0; frame < frames; frame += step)
                {
                    float x = rect.Left + rect.Width * frame / Math.Max(1, state.FrameCount - 1);
                    int offset = frame * maxFormants;
                    for (int f = 0; f < maxFormants; f++)
                    {
                        float freq = state.FormantFrequencies[offset + f];
                        if (freq <= 0f)
                        {
                            continue;
                        }
                        float norm = FrequencyScaleUtils.Normalize(state.Scale, freq, state.MinFrequency, state.MaxFrequency);
                        float y = rect.Bottom - norm * rect.Height;
                        var paint = f switch
                        {
                            0 => _formantPaint1,
                            1 => _formantPaint2,
                            _ => _formantPaint3
                        };
                        canvas.DrawCircle(x, y, 2.5f, paint);
                    }
                }
            }
        }

        if (state.HarmonicFrequencies is { Length: > 0 } && state.ShowHarmonics)
        {
            int maxHarmonics = state.MaxHarmonics;
            if (maxHarmonics > 0)
            {
                int frames = Math.Min(state.FrameCount, state.HarmonicFrequencies.Length / maxHarmonics);
                int step = GetOverlayStep(rect, frames);
                for (int frame = 0; frame < frames; frame += step)
                {
                    float x = rect.Left + rect.Width * frame / Math.Max(1, state.FrameCount - 1);
                    int offset = frame * maxHarmonics;
                    for (int h = 0; h < maxHarmonics; h++)
                    {
                        float freq = state.HarmonicFrequencies[offset + h];
                        if (freq <= 0f)
                        {
                            continue;
                        }
                        float norm = FrequencyScaleUtils.Normalize(state.Scale, freq, state.MinFrequency, state.MaxFrequency);
                        float y = rect.Bottom - norm * rect.Height;
                        canvas.DrawCircle(x, y, 1.5f, _harmonicPaint);
                    }
                }
            }
        }

        if (state.VoicingStates is { Length: > 0 } && state.ShowVoicing)
        {
            int frames = Math.Min(state.FrameCount, state.VoicingStates.Length);
            float laneTop = rect.Bottom - VoicingLaneHeight;
            int step = GetOverlayStep(rect, frames);
            float frameWidth = rect.Width / Math.Max(1, state.FrameCount);
            for (int frame = 0; frame < frames; frame += step)
            {
                byte stateValue = state.VoicingStates[frame];
                for (int i = 1; i < step && frame + i < frames; i++)
                {
                    byte next = state.VoicingStates[frame + i];
                    if (next > stateValue)
                    {
                        stateValue = next;
                    }
                }
                SKPaint paint = stateValue switch
                {
                    2 => _voicedPaint,
                    1 => _unvoicedPaint,
                    _ => _silencePaint
                };
                float x = rect.Left + frameWidth * frame;
                float width = frameWidth * step;
                canvas.DrawRect(new SKRect(x, laneTop, x + width, rect.Bottom), paint);
            }
        }
    }

    private void DrawReferenceLine(SKCanvas canvas, SKRect rect, VocalSpectrographState state)
    {
        if (state.ReferenceFrameId is null || state.AvailableFrames <= 0 || state.FrameCount <= 1)
        {
            return;
        }

        long latestFrame = state.LatestFrameId;
        long referenceFrame = state.ReferenceFrameId.Value;
        int availableFrames = state.AvailableFrames;
        int padFrames = Math.Max(0, state.FrameCount - availableFrames);
        long oldestFrame = latestFrame - availableFrames + 1;
        long offset = referenceFrame - oldestFrame;
        if (offset < 0 || offset >= availableFrames)
        {
            return;
        }

        int columnIndex = padFrames + (int)offset;
        float x = rect.Left + rect.Width * columnIndex / Math.Max(1, state.FrameCount - 1);
        canvas.DrawLine(x, rect.Top, x, rect.Bottom, _referencePaint);
    }

    private void DrawPillButton(SKCanvas canvas, SKRect rect, string label, bool active)
    {
        canvas.DrawRoundRect(new SKRoundRect(rect, rect.Height / 2f), active ? _buttonActivePaint : _buttonPaint);
        canvas.DrawRoundRect(new SKRoundRect(rect, rect.Height / 2f), _borderPaint);
        canvas.DrawText(label, rect.MidX, rect.MidY + 4f, _buttonTextPaint);
    }

    private void DrawIconButton(SKCanvas canvas, SKRect rect, string label, SKColor color)
    {
        using var paint = new SKPaint { Color = color, IsAntialias = true, TextSize = 12f, TextAlign = SKTextAlign.Center };
        canvas.DrawText(label, rect.MidX, rect.MidY + 4f, paint);
    }

    private static float Normalize(float value, float min, float max)
    {
        if (max <= min)
        {
            return 0f;
        }
        return Math.Clamp((value - min) / (max - min), 0f, 1f);
    }

    private static string FormatHz(float hz)
    {
        return hz >= 1000f ? $"{hz / 1000f:0.#}k" : $"{hz:0}";
    }

    private static string FormatReassignLabel(SpectrogramReassignMode mode)
    {
        return mode switch
        {
            SpectrogramReassignMode.Frequency => "R:F",
            SpectrogramReassignMode.Time => "R:T",
            SpectrogramReassignMode.TimeFrequency => "R:TF",
            _ => "R:Off"
        };
    }

    private static float Distance(SKPoint center, float x, float y)
    {
        float dx = center.X - x;
        float dy = center.Y - y;
        return MathF.Sqrt(dx * dx + dy * dy);
    }

    private static int GetOverlayStep(SKRect rect, int frameCount)
    {
        int pixels = Math.Max(1, (int)MathF.Round(rect.Width));
        if (frameCount <= pixels)
        {
            return 1;
        }

        return Math.Max(1, frameCount / pixels);
    }

    public bool TryGetSpectrogramRect(out SKRect rect)
    {
        rect = _spectrogramRect;
        return rect.Width > 0 && rect.Height > 0;
    }

    public void Dispose()
    {
        _knob.Dispose();
        _backgroundPaint.Dispose();
        _panelPaint.Dispose();
        _borderPaint.Dispose();
        _titlePaint.Dispose();
        _textPaint.Dispose();
        _mutedTextPaint.Dispose();
        _gridPaint.Dispose();
        _buttonPaint.Dispose();
        _buttonActivePaint.Dispose();
        _buttonTextPaint.Dispose();
        _referencePaint.Dispose();
        _bitmapPaint.Dispose();
        _pitchPaint.Dispose();
        _formantPaint1.Dispose();
        _formantPaint2.Dispose();
        _formantPaint3.Dispose();
        _harmonicPaint.Dispose();
        _voicedPaint.Dispose();
        _unvoicedPaint.Dispose();
        _silencePaint.Dispose();
        _spectrogramBitmap?.Dispose();
    }
}

public enum SpectrographHitArea
{
    None,
    TitleBar,
    CloseButton,
    BypassButton,
    FftButton,
    WindowButton,
    OverlapButton,
    ScaleButton,
    ColorButton,
    ReassignButton,
    PauseButton,
    PitchToggle,
    FormantToggle,
    HarmonicToggle,
    VoicingToggle,
    PreEmphasisToggle,
    HpfToggle,
    Spectrogram,
    Knob
}

public record struct VocalSpectrographHitTest(SpectrographHitArea Area, int KnobIndex);

public record struct VocalSpectrographState(
    int FftSize,
    WindowFunction WindowFunction,
    float Overlap,
    FrequencyScale Scale,
    float MinFrequency,
    float MaxFrequency,
    float MinDb,
    float MaxDb,
    float TimeWindowSeconds,
    int DisplayBins,
    int FrameCount,
    int ColorMap,
    SpectrogramReassignMode ReassignMode,
    bool IsBypassed,
    bool IsPaused,
    bool ShowPitch,
    bool ShowFormants,
    bool ShowHarmonics,
    bool ShowVoicing,
    bool PreEmphasisEnabled,
    bool HighPassEnabled,
    float HighPassCutoff,
    long LatestFrameId,
    int AvailableFrames,
    long? ReferenceFrameId,
    int HoveredKnob,
    int DataVersion,
    float[]? Spectrogram,
    float[]? PitchTrack,
    float[]? FormantFrequencies,
    float[]? FormantBandwidths,
    byte[]? VoicingStates,
    float[]? HarmonicFrequencies,
    int MaxFormants,
    int MaxHarmonics);
