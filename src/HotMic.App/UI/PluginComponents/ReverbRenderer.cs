using System.IO;
using SkiaSharp;

namespace HotMic.App.UI.PluginComponents;

/// <summary>
/// Reverb plugin UI renderer with impulse response visualization.
/// Shows IR waveform, wet/dry mix, and convolution processing status.
/// </summary>
public sealed class ReverbRenderer : IDisposable
{
    private const float TitleBarHeight = 40f;
    private const float Padding = 16f;
    private const float WaveformHeight = 100f;
    private const float KnobRadius = 22f;
    private const float KnobSpacing = 90f;
    private const float CornerRadius = 10f;
    private const int KnobCount = 3;
    private const int PresetCount = 6;

    private readonly PluginComponentTheme _theme;
    private readonly WaveformDisplay _waveformDisplay;
    private readonly RotaryKnob _knob;

    private readonly SKPaint _backgroundPaint;
    private readonly SKPaint _titleBarPaint;
    private readonly SKPaint _borderPaint;
    private readonly SKPaint _titlePaint;
    private readonly SKPaint _closeButtonPaint;
    private readonly SKPaint _bypassPaint;
    private readonly SKPaint _bypassActivePaint;
    private readonly SKPaint _sectionLabelPaint;
    private readonly SKPaint _statusPaint;
    private readonly SKPaint _presetPaint;
    private readonly SKPaint _presetSelectedPaint;
    private readonly SKPaint _loadButtonPaint;
    private readonly SKPaint _irDisplayPaint;

    private SKRect _closeButtonRect;
    private SKRect _bypassButtonRect;
    private SKRect _titleBarRect;
    private SKRect _loadButtonRect;
    private readonly SKRect[] _knobRects = new SKRect[KnobCount];
    private readonly SKPoint[] _knobCenters = new SKPoint[KnobCount];
    private readonly SKRect[] _presetRects = new SKRect[PresetCount];

    public WaveformBuffer IrWaveformBuffer { get; } = new(256);

    public ReverbRenderer(PluginComponentTheme? theme = null)
    {
        _theme = theme ?? PluginComponentTheme.Default;
        _waveformDisplay = new WaveformDisplay(_theme);
        _knob = new RotaryKnob(_theme);

        _backgroundPaint = new SKPaint
        {
            Color = _theme.PanelBackground,
            IsAntialias = true,
            Style = SKPaintStyle.Fill
        };

        _titleBarPaint = new SKPaint
        {
            Color = _theme.PanelBackgroundLight,
            IsAntialias = true,
            Style = SKPaintStyle.Fill
        };

        _borderPaint = new SKPaint
        {
            Color = _theme.PanelBorder,
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 1f
        };

        _titlePaint = new SKPaint
        {
            Color = _theme.TextPrimary,
            IsAntialias = true,
            TextSize = 14f,
            Typeface = SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Bold)
        };

        _closeButtonPaint = new SKPaint
        {
            Color = _theme.TextSecondary,
            IsAntialias = true,
            TextSize = 18f,
            TextAlign = SKTextAlign.Center,
            Typeface = SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Normal)
        };

        _bypassPaint = new SKPaint
        {
            Color = _theme.PanelBackgroundLight,
            IsAntialias = true,
            Style = SKPaintStyle.Fill
        };

        _bypassActivePaint = new SKPaint
        {
            Color = new SKColor(0xFF, 0x50, 0x50),
            IsAntialias = true,
            Style = SKPaintStyle.Fill
        };

        _sectionLabelPaint = new SKPaint
        {
            Color = _theme.TextMuted,
            IsAntialias = true,
            TextSize = 9f,
            Typeface = SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Normal)
        };

        _statusPaint = new SKPaint
        {
            Color = _theme.TextSecondary,
            IsAntialias = true,
            TextSize = 10f,
            TextAlign = SKTextAlign.Center,
            Typeface = SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Normal)
        };

        _presetPaint = new SKPaint
        {
            Color = _theme.PanelBackgroundLight,
            IsAntialias = true,
            Style = SKPaintStyle.Fill
        };

        _presetSelectedPaint = new SKPaint
        {
            Color = new SKColor(0x40, 0x80, 0xFF),
            IsAntialias = true,
            Style = SKPaintStyle.Fill
        };

        _loadButtonPaint = new SKPaint
        {
            Color = new SKColor(0x50, 0x50, 0x60),
            IsAntialias = true,
            Style = SKPaintStyle.Fill
        };

        _irDisplayPaint = new SKPaint
        {
            Color = new SKColor(0x30, 0x40, 0x50),
            IsAntialias = true,
            Style = SKPaintStyle.Fill
        };
    }

    private static readonly string[] PresetNames = ["None", "Sm. Room", "Med. Hall", "Lg. Hall", "Plate", "Custom"];

    public void Render(SKCanvas canvas, SKSize size, float dpiScale, ReverbState state)
    {
        canvas.Clear(SKColors.Transparent);
        canvas.Save();
        canvas.Scale(dpiScale);
        size = new SKSize(size.Width / dpiScale, size.Height / dpiScale);

        // Main background
        var backgroundRect = new SKRect(0, 0, size.Width, size.Height);
        var roundRect = new SKRoundRect(backgroundRect, CornerRadius);
        canvas.DrawRoundRect(roundRect, _backgroundPaint);

        // Title bar
        _titleBarRect = new SKRect(0, 0, size.Width, TitleBarHeight);
        using (var titleClip = new SKPath())
        {
            titleClip.AddRoundRect(new SKRoundRect(_titleBarRect, CornerRadius, CornerRadius));
            titleClip.AddRect(new SKRect(0, CornerRadius, size.Width, TitleBarHeight));
            canvas.Save();
            canvas.ClipPath(titleClip);
            canvas.DrawRect(_titleBarRect, _titleBarPaint);
            canvas.Restore();
        }
        canvas.DrawLine(0, TitleBarHeight, size.Width, TitleBarHeight, _borderPaint);

        // Title
        canvas.DrawText("Convolution Reverb", Padding, TitleBarHeight / 2f + 5, _titlePaint);

        // Bypass button
        float bypassWidth = 60f;
        _bypassButtonRect = new SKRect(
            size.Width - Padding - 30 - bypassWidth - 8,
            (TitleBarHeight - 24) / 2,
            size.Width - Padding - 30 - 8,
            (TitleBarHeight + 24) / 2);
        var bypassRound = new SKRoundRect(_bypassButtonRect, 4f);
        canvas.DrawRoundRect(bypassRound, state.IsBypassed ? _bypassActivePaint : _bypassPaint);
        canvas.DrawRoundRect(bypassRound, _borderPaint);

        using var bypassTextPaint = new SKPaint
        {
            Color = state.IsBypassed ? _theme.TextPrimary : _theme.TextSecondary,
            IsAntialias = true,
            TextSize = 10f,
            TextAlign = SKTextAlign.Center,
            Typeface = SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Bold)
        };
        canvas.DrawText("BYPASS", _bypassButtonRect.MidX, _bypassButtonRect.MidY + 4, bypassTextPaint);

        // Close button
        _closeButtonRect = new SKRect(size.Width - Padding - 24, (TitleBarHeight - 24) / 2,
            size.Width - Padding, (TitleBarHeight + 24) / 2);
        canvas.DrawText("\u00D7", _closeButtonRect.MidX, _closeButtonRect.MidY + 6, _closeButtonPaint);

        float y = TitleBarHeight + Padding;

        // IR Preset selection
        canvas.DrawText("IMPULSE RESPONSE", Padding, y + 10, _sectionLabelPaint);
        y += 20;

        float presetWidth = (size.Width - Padding * 2 - (PresetCount - 1) * 4) / PresetCount;
        for (int i = 0; i < PresetCount; i++)
        {
            float px = Padding + i * (presetWidth + 4);
            _presetRects[i] = new SKRect(px, y, px + presetWidth, y + 28);
            bool isSelected = state.IrPreset == i;

            var presetRound = new SKRoundRect(_presetRects[i], 4f);
            canvas.DrawRoundRect(presetRound, isSelected ? _presetSelectedPaint : _presetPaint);
            canvas.DrawRoundRect(presetRound, _borderPaint);

            using var presetTextPaint = new SKPaint
            {
                Color = isSelected ? _theme.TextPrimary : _theme.TextSecondary,
                IsAntialias = true,
                TextSize = 9f,
                TextAlign = SKTextAlign.Center,
                Typeface = SKTypeface.FromFamilyName("Segoe UI", isSelected ? SKFontStyle.Bold : SKFontStyle.Normal)
            };
            canvas.DrawText(PresetNames[i], _presetRects[i].MidX, _presetRects[i].MidY + 3, presetTextPaint);
        }

        y += 36;

        // Load button (for custom IR)
        if (state.IrPreset == 5)
        {
            _loadButtonRect = new SKRect(Padding, y, Padding + 120, y + 28);
            var loadRound = new SKRoundRect(_loadButtonRect, 4f);
            canvas.DrawRoundRect(loadRound, _loadButtonPaint);
            canvas.DrawRoundRect(loadRound, _borderPaint);

            using var loadTextPaint = new SKPaint
            {
                Color = _theme.TextPrimary,
                IsAntialias = true,
                TextSize = 10f,
                TextAlign = SKTextAlign.Center,
                Typeface = SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Bold)
            };
            canvas.DrawText("Load IR File...", _loadButtonRect.MidX, _loadButtonRect.MidY + 4, loadTextPaint);

            // Show loaded file name
            if (!string.IsNullOrEmpty(state.LoadedIrPath))
            {
                string fileName = Path.GetFileName(state.LoadedIrPath);
                if (fileName.Length > 30) fileName = fileName[..27] + "...";
                using var fileNamePaint = new SKPaint
                {
                    Color = _theme.TextSecondary,
                    IsAntialias = true,
                    TextSize = 9f,
                    Typeface = SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Normal)
                };
                canvas.DrawText(fileName, _loadButtonRect.Right + 10, _loadButtonRect.MidY + 3, fileNamePaint);
            }
            y += 36;
        }
        else
        {
            _loadButtonRect = SKRect.Empty;
        }

        // IR Status
        _statusPaint.Color = state.IsIrLoaded ? new SKColor(0x40, 0xC0, 0x40) : new SKColor(0xFF, 0xA0, 0x40);
        canvas.DrawText(state.StatusMessage, size.Width / 2, y + 12, _statusPaint);
        y += 28;

        // IR Waveform visualization
        canvas.DrawText("IR WAVEFORM", Padding, y + 10, _sectionLabelPaint);
        y += 14;

        var irDisplayRect = new SKRect(Padding, y, size.Width - Padding, y + WaveformHeight);
        canvas.DrawRoundRect(new SKRoundRect(irDisplayRect, 4f), _irDisplayPaint);

        if (state.IsIrLoaded)
        {
            // Draw decay visualization
            DrawIrVisualization(canvas, irDisplayRect, state);
        }
        else
        {
            // No IR loaded message
            using var noIrPaint = new SKPaint
            {
                Color = _theme.TextMuted,
                IsAntialias = true,
                TextSize = 12f,
                TextAlign = SKTextAlign.Center,
                Typeface = SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Italic)
            };
            canvas.DrawText("No impulse response loaded", irDisplayRect.MidX, irDisplayRect.MidY + 4, noIrPaint);
        }

        y += WaveformHeight + Padding;

        // Knobs section
        float knobsY = y + KnobRadius + 8;
        float knobsTotalWidth = KnobCount * KnobSpacing;
        float knobsStartX = Padding + (size.Width - Padding * 2 - knobsTotalWidth) / 2 + KnobSpacing / 2;

        // Dry/Wet knob
        _knobCenters[0] = new SKPoint(knobsStartX, knobsY);
        _knob.Render(canvas, _knobCenters[0], KnobRadius, state.DryWet,
            "DRY/WET", $"{state.DryWet * 100:0}", "%", state.HoveredKnob == 0);
        _knobRects[0] = _knob.GetHitRect(_knobCenters[0], KnobRadius);

        // Decay knob
        _knobCenters[1] = new SKPoint(knobsStartX + KnobSpacing, knobsY);
        float decayNorm = (state.Decay - 0.1f) / (2f - 0.1f);
        _knob.Render(canvas, _knobCenters[1], KnobRadius, decayNorm,
            "DECAY", $"{state.Decay:0.0}", "x", state.HoveredKnob == 1);
        _knobRects[1] = _knob.GetHitRect(_knobCenters[1], KnobRadius);

        // Pre-delay knob
        _knobCenters[2] = new SKPoint(knobsStartX + KnobSpacing * 2, knobsY);
        float preDelayNorm = state.PreDelayMs / 100f;
        _knob.Render(canvas, _knobCenters[2], KnobRadius, preDelayNorm,
            "PRE-DLY", $"{state.PreDelayMs:0}", "ms", state.HoveredKnob == 2);
        _knobRects[2] = _knob.GetHitRect(_knobCenters[2], KnobRadius);

        // Dry/Wet meter bar at bottom
        float barHeight = 20f;
        float barY = size.Height - Padding - barHeight;
        var mixBarRect = new SKRect(Padding, barY, size.Width - Padding, barY + barHeight);
        DrawMixBar(canvas, mixBarRect, state.DryWet);

        // Outer border
        canvas.DrawRoundRect(roundRect, _borderPaint);
        canvas.Restore();
    }

    private void DrawIrVisualization(SKCanvas canvas, SKRect rect, ReverbState state)
    {
        // Draw decay curve
        float margin = 4f;
        var innerRect = new SKRect(rect.Left + margin, rect.Top + margin,
            rect.Right - margin, rect.Bottom - margin);

        using var decayCurvePaint = new SKPaint
        {
            Color = new SKColor(0x60, 0x90, 0xC0, 0xC0),
            IsAntialias = true,
            Style = SKPaintStyle.Fill
        };

        using var decayLinePaint = new SKPaint
        {
            Color = new SKColor(0x80, 0xC0, 0xFF),
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 1.5f
        };

        // Exponential decay visualization
        int points = (int)innerRect.Width;
        using var path = new SKPath();
        using var fillPath = new SKPath();

        fillPath.MoveTo(innerRect.Left, innerRect.Bottom);

        for (int i = 0; i < points; i++)
        {
            float t = (float)i / points;
            float decay = MathF.Exp(-t * 3f / state.Decay); // Decay curve
            float y = innerRect.Bottom - innerRect.Height * decay * 0.8f;
            float x = innerRect.Left + i;

            if (i == 0)
            {
                path.MoveTo(x, y);
            }
            else
            {
                path.LineTo(x, y);
            }
            fillPath.LineTo(x, y);
        }

        fillPath.LineTo(innerRect.Right, innerRect.Bottom);
        fillPath.Close();

        canvas.DrawPath(fillPath, decayCurvePaint);
        canvas.DrawPath(path, decayLinePaint);

        // Early reflections visualization (dots)
        using var dotPaint = new SKPaint
        {
            Color = new SKColor(0xFF, 0xFF, 0x80, 0xC0),
            IsAntialias = true,
            Style = SKPaintStyle.Fill
        };

        float earlyWidth = innerRect.Width * 0.15f;
        for (int i = 0; i < 6; i++)
        {
            float x = innerRect.Left + i * earlyWidth / 6 + 5;
            float amp = 1f - (float)i / 8;
            float y = innerRect.Bottom - innerRect.Height * amp * 0.7f;
            canvas.DrawCircle(x, y, 2f, dotPaint);
        }
    }

    private void DrawMixBar(SKCanvas canvas, SKRect rect, float dryWet)
    {
        // Background
        using var bgPaint = new SKPaint
        {
            Color = new SKColor(0x25, 0x25, 0x30),
            IsAntialias = true,
            Style = SKPaintStyle.Fill
        };
        canvas.DrawRoundRect(new SKRoundRect(rect, 4f), bgPaint);

        // Dry portion (left)
        float dryWidth = rect.Width * (1f - dryWet);
        if (dryWidth > 0)
        {
            var dryRect = new SKRect(rect.Left, rect.Top, rect.Left + dryWidth, rect.Bottom);
            using var dryPaint = new SKPaint
            {
                Color = new SKColor(0x40, 0xA0, 0x40, 0xC0),
                IsAntialias = true,
                Style = SKPaintStyle.Fill
            };
            using var dryClip = new SKPath();
            dryClip.AddRoundRect(new SKRoundRect(rect, 4f));
            canvas.Save();
            canvas.ClipPath(dryClip);
            canvas.DrawRect(dryRect, dryPaint);
            canvas.Restore();
        }

        // Wet portion (right)
        if (dryWet > 0)
        {
            var wetRect = new SKRect(rect.Left + dryWidth, rect.Top, rect.Right, rect.Bottom);
            using var wetPaint = new SKPaint
            {
                Color = new SKColor(0x40, 0x80, 0xFF, 0xC0),
                IsAntialias = true,
                Style = SKPaintStyle.Fill
            };
            using var wetClip = new SKPath();
            wetClip.AddRoundRect(new SKRoundRect(rect, 4f));
            canvas.Save();
            canvas.ClipPath(wetClip);
            canvas.DrawRect(wetRect, wetPaint);
            canvas.Restore();
        }

        // Labels
        using var labelPaint = new SKPaint
        {
            Color = _theme.TextPrimary,
            IsAntialias = true,
            TextSize = 9f,
            Typeface = SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Bold)
        };
        canvas.DrawText("DRY", rect.Left + 8, rect.MidY + 3, labelPaint);
        labelPaint.TextAlign = SKTextAlign.Right;
        canvas.DrawText("WET", rect.Right - 8, rect.MidY + 3, labelPaint);

        // Percentage in center
        labelPaint.TextAlign = SKTextAlign.Center;
        labelPaint.TextSize = 10f;
        canvas.DrawText($"{(1 - dryWet) * 100:0}% / {dryWet * 100:0}%", rect.MidX, rect.MidY + 3, labelPaint);

        // Border
        canvas.DrawRoundRect(new SKRoundRect(rect, 4f), _borderPaint);
    }

    public ReverbHitTest HitTest(float x, float y)
    {
        if (_closeButtonRect.Contains(x, y))
            return new ReverbHitTest(ReverbHitArea.CloseButton, -1);

        if (_bypassButtonRect.Contains(x, y))
            return new ReverbHitTest(ReverbHitArea.BypassButton, -1);

        if (!_loadButtonRect.IsEmpty && _loadButtonRect.Contains(x, y))
            return new ReverbHitTest(ReverbHitArea.LoadButton, -1);

        for (int i = 0; i < KnobCount; i++)
        {
            float dx = x - _knobCenters[i].X;
            float dy = y - _knobCenters[i].Y;
            if (dx * dx + dy * dy <= KnobRadius * KnobRadius * 1.5f)
            {
                return new ReverbHitTest(ReverbHitArea.Knob, i);
            }
        }

        for (int i = 0; i < PresetCount; i++)
        {
            if (_presetRects[i].Contains(x, y))
            {
                return new ReverbHitTest(ReverbHitArea.Preset, i);
            }
        }

        if (_titleBarRect.Contains(x, y))
            return new ReverbHitTest(ReverbHitArea.TitleBar, -1);

        return new ReverbHitTest(ReverbHitArea.None, -1);
    }

    public static SKSize GetPreferredSize() => new(450, 450);

    public void Dispose()
    {
        _waveformDisplay.Dispose();
        _knob.Dispose();
        _backgroundPaint.Dispose();
        _titleBarPaint.Dispose();
        _borderPaint.Dispose();
        _titlePaint.Dispose();
        _closeButtonPaint.Dispose();
        _bypassPaint.Dispose();
        _bypassActivePaint.Dispose();
        _sectionLabelPaint.Dispose();
        _statusPaint.Dispose();
        _presetPaint.Dispose();
        _presetSelectedPaint.Dispose();
        _loadButtonPaint.Dispose();
        _irDisplayPaint.Dispose();
    }
}

public record struct ReverbState(
    float DryWet,
    float Decay,
    float PreDelayMs,
    int IrPreset,
    bool IsIrLoaded,
    string StatusMessage,
    string? LoadedIrPath,
    bool IsBypassed,
    int HoveredKnob = -1);

public enum ReverbHitArea
{
    None,
    TitleBar,
    CloseButton,
    BypassButton,
    Knob,
    Preset,
    LoadButton
}

public record struct ReverbHitTest(ReverbHitArea Area, int Index);
