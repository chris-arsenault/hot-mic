using System;
using HotMic.App.UI;
using HotMic.Core.Plugins;
using HotMic.Core.Plugins.BuiltIn;
using SkiaSharp;

namespace HotMic.App.UI.PluginComponents;

/// <summary>
/// Analysis Tap plugin UI - debug/monitor panel showing analysis signals.
/// </summary>
public sealed class AnalysisTapRenderer : IDisposable
{
    private const float TitleBarHeight = 40f;
    private const float Padding = 12f;
    private const float CornerRadius = 10f;
    private const float MeterHeight = 20f;
    private const float MeterSpacing = 8f;
    private const float LabelWidth = 150f;
    private const float IndicatorWidth = 36f;
    private const float IndicatorHeight = 14f;
    private const float IndicatorGap = 6f;
    private const float ToggleWidth = 36f;
    private const float ToggleHeight = 18f;
    private const float ToggleGap = 4f;
    private const float ValueWidth = 56f;
    private const float ColumnGap = 8f;

    private static readonly AnalysisTapMode[] ModeOrder =
    [
        AnalysisTapMode.UseExisting,
        AnalysisTapMode.Generate,
        AnalysisTapMode.Disabled
    ];

    private static readonly string[] ToggleLabels = ["USE", "GEN", "OFF"];
    private static readonly string[] SignalLabels = BuildSignalLabels();

    private readonly PluginComponentTheme _theme;

    private readonly SKPaint _backgroundPaint;
    private readonly SKPaint _titleBarPaint;
    private readonly SKPaint _borderPaint;
    private readonly SkiaTextPaint _titlePaint;
    private readonly SkiaTextPaint _closeButtonPaint;
    private readonly SKPaint _bypassPaint;
    private readonly SKPaint _bypassActivePaint;
    private readonly SKPaint _meterBackgroundPaint;
    private readonly SKPaint _toggleOnPaint;
    private readonly SKPaint _toggleOffPaint;
    private readonly SKPaint _modeUsePaint;
    private readonly SKPaint _modeDisablePaint;
    private readonly SKPaint _indicatorOnPaint;
    private readonly SKPaint _indicatorOffPaint;
    private readonly SkiaTextPaint _labelPaint;
    private readonly SkiaTextPaint _labelMutedPaint;
    private readonly SkiaTextPaint _valuePaint;
    private readonly SkiaTextPaint _indicatorTextPaint;
    private readonly SkiaTextPaint _toggleTextPaint;
    private readonly SkiaTextPaint _latencyPaint;

    private SKRect _closeButtonRect;
    private SKRect _bypassButtonRect;
    private SKRect _titleBarRect;
    private SKRect[,] _modeToggleRects = new SKRect[0, 0];

    public AnalysisTapRenderer(PluginComponentTheme? theme = null)
    {
        _theme = theme ?? PluginComponentTheme.Default;

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

        _titlePaint = new SkiaTextPaint(_theme.TextPrimary, 14f, SKFontStyle.Bold);
        _closeButtonPaint = new SkiaTextPaint(_theme.TextSecondary, 18f, SKFontStyle.Normal, SKTextAlign.Center);

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

        _meterBackgroundPaint = new SKPaint
        {
            Color = _theme.WaveformBackground,
            IsAntialias = true,
            Style = SKPaintStyle.Fill
        };

        _toggleOnPaint = new SKPaint
        {
            Color = new SKColor(0x40, 0xC0, 0x40),
            IsAntialias = true,
            Style = SKPaintStyle.Fill
        };

        _toggleOffPaint = new SKPaint
        {
            Color = _theme.GateClosed,
            IsAntialias = true,
            Style = SKPaintStyle.Fill
        };

        _modeUsePaint = new SKPaint
        {
            Color = new SKColor(0x40, 0x90, 0xFF),
            IsAntialias = true,
            Style = SKPaintStyle.Fill
        };

        _modeDisablePaint = new SKPaint
        {
            Color = new SKColor(0xD0, 0x50, 0x50),
            IsAntialias = true,
            Style = SKPaintStyle.Fill
        };

        _indicatorOnPaint = new SKPaint
        {
            Color = new SKColor(0x40, 0x90, 0xFF),
            IsAntialias = true,
            Style = SKPaintStyle.Fill
        };

        _indicatorOffPaint = new SKPaint
        {
            Color = _theme.GateClosedDim,
            IsAntialias = true,
            Style = SKPaintStyle.Fill
        };

        _labelPaint = new SkiaTextPaint(_theme.TextSecondary, 11f, SKFontStyle.Normal, SKTextAlign.Left);
        _labelMutedPaint = new SkiaTextPaint(_theme.TextMuted, 11f, SKFontStyle.Normal, SKTextAlign.Left);
        _valuePaint = new SkiaTextPaint(_theme.TextPrimary, 11f, SKFontStyle.Bold, SKTextAlign.Right);
        _indicatorTextPaint = new SkiaTextPaint(_theme.TextPrimary, 8.5f, SKFontStyle.Bold, SKTextAlign.Center);
        _toggleTextPaint = new SkiaTextPaint(_theme.TextPrimary, 8.5f, SKFontStyle.Bold, SKTextAlign.Center);
        _latencyPaint = new SkiaTextPaint(_theme.TextMuted, 9f, SKFontStyle.Normal, SKTextAlign.Right);
    }

    public void Render(SKCanvas canvas, SKSize size, float dpiScale, AnalysisTapState state)
    {
        canvas.Clear(SKColors.Transparent);
        canvas.Save();
        canvas.Scale(dpiScale);
        size = new SKSize(size.Width / dpiScale, size.Height / dpiScale);

        var backgroundRect = new SKRect(0, 0, size.Width, size.Height);
        var roundRect = new SKRoundRect(backgroundRect, CornerRadius);
        canvas.DrawRoundRect(roundRect, _backgroundPaint);

        DrawTitleBar(canvas, size, state);

        int signalCount = state.Signals.Length;
        EnsureModeRectCapacity(signalCount);

        float indicatorGroupWidth = IndicatorWidth * 2 + IndicatorGap;
        float toggleGroupWidth = ToggleWidth * 3 + ToggleGap * 2;
        float meterX = Padding + LabelWidth + indicatorGroupWidth + ColumnGap;
        float meterWidth = size.Width - (Padding * 2 + LabelWidth + indicatorGroupWidth + ValueWidth + toggleGroupWidth + ColumnGap * 3);
        float indicatorX = Padding + LabelWidth;
        float valueX = meterX + meterWidth + ColumnGap;
        float toggleX = valueX + ValueWidth + ColumnGap;

        float y = TitleBarHeight + Padding;
        for (int i = 0; i < signalCount; i++)
        {
            var signal = state.Signals[i];
            string label = GetLabel(signal.Signal);
            DrawSignalRow(canvas, y, indicatorX, meterX, meterWidth, valueX, toggleX, label, signal, i);
            y += MeterHeight + MeterSpacing;
        }

        canvas.DrawRoundRect(roundRect, _borderPaint);
        canvas.Restore();
    }

    public AnalysisTapHitTest HitTest(float x, float y)
    {
        if (_closeButtonRect.Contains(x, y))
        {
            return new AnalysisTapHitTest(AnalysisTapHitArea.CloseButton, -1, AnalysisTapMode.Generate);
        }

        if (_bypassButtonRect.Contains(x, y))
        {
            return new AnalysisTapHitTest(AnalysisTapHitArea.BypassButton, -1, AnalysisTapMode.Generate);
        }

        int rows = _modeToggleRects.GetLength(0);
        for (int i = 0; i < rows; i++)
        {
            for (int j = 0; j < 3; j++)
            {
                if (_modeToggleRects[i, j].Contains(x, y))
                {
                    return new AnalysisTapHitTest(AnalysisTapHitArea.ModeToggle, i, ModeOrder[j]);
                }
            }
        }

        if (_titleBarRect.Contains(x, y))
        {
            return new AnalysisTapHitTest(AnalysisTapHitArea.TitleBar, -1, AnalysisTapMode.Generate);
        }

        return new AnalysisTapHitTest(AnalysisTapHitArea.None, -1, AnalysisTapMode.Generate);
    }

    public static SKSize GetPreferredSize(int signalCount)
    {
        float meterWidth = 200f;
        float indicatorGroupWidth = IndicatorWidth * 2 + IndicatorGap;
        float toggleGroupWidth = ToggleWidth * 3 + ToggleGap * 2;
        float width = Padding * 2 + LabelWidth + indicatorGroupWidth + ValueWidth + toggleGroupWidth + meterWidth + ColumnGap * 3;
        float height = TitleBarHeight + Padding * 2 + signalCount * MeterHeight + Math.Max(0, signalCount - 1) * MeterSpacing;
        return new SKSize(width, height);
    }

    public void Dispose()
    {
        _backgroundPaint.Dispose();
        _titleBarPaint.Dispose();
        _borderPaint.Dispose();
        _titlePaint.Dispose();
        _closeButtonPaint.Dispose();
        _bypassPaint.Dispose();
        _bypassActivePaint.Dispose();
        _meterBackgroundPaint.Dispose();
        _toggleOnPaint.Dispose();
        _toggleOffPaint.Dispose();
        _modeUsePaint.Dispose();
        _modeDisablePaint.Dispose();
        _indicatorOnPaint.Dispose();
        _indicatorOffPaint.Dispose();
        _labelPaint.Dispose();
        _labelMutedPaint.Dispose();
        _valuePaint.Dispose();
        _indicatorTextPaint.Dispose();
        _toggleTextPaint.Dispose();
        _latencyPaint.Dispose();
    }

    private void EnsureModeRectCapacity(int count)
    {
        if (_modeToggleRects.GetLength(0) == count)
        {
            return;
        }

        _modeToggleRects = new SKRect[count, 3];
    }

    private void DrawTitleBar(SKCanvas canvas, SKSize size, AnalysisTapState state)
    {
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

        canvas.DrawText("Analysis Tap", Padding, TitleBarHeight / 2f + 5, _titlePaint);

        float bypassWidth = 60f;
        _bypassButtonRect = new SKRect(
            size.Width - Padding - 30 - bypassWidth - 8,
            (TitleBarHeight - 24) / 2,
            size.Width - Padding - 30 - 8,
            (TitleBarHeight + 24) / 2);
        var bypassRound = new SKRoundRect(_bypassButtonRect, 4f);
        canvas.DrawRoundRect(bypassRound, state.IsBypassed ? _bypassActivePaint : _bypassPaint);
        canvas.DrawRoundRect(bypassRound, _borderPaint);

        using var bypassTextPaint = new SkiaTextPaint(state.IsBypassed ? _theme.TextPrimary : _theme.TextSecondary, 10f, SKFontStyle.Bold, SKTextAlign.Center);
        canvas.DrawText("BYPASS", _bypassButtonRect.MidX, _bypassButtonRect.MidY + 4, bypassTextPaint);

        if (state.LatencyMs >= 0f)
        {
            string latencyLabel = $"LAT {state.LatencyMs:0.0}ms";
            canvas.DrawText(latencyLabel, _bypassButtonRect.Left - 6f, TitleBarHeight / 2f + 4, _latencyPaint);
        }

        _closeButtonRect = new SKRect(size.Width - Padding - 30, (TitleBarHeight - 30) / 2, size.Width - Padding, (TitleBarHeight + 30) / 2);
        canvas.DrawText("X", _closeButtonRect.MidX, _closeButtonRect.MidY + 6, _closeButtonPaint);
    }

    private void DrawSignalRow(SKCanvas canvas, float y, float indicatorX, float meterX, float meterWidth, float valueX,
        float toggleX, string label, in AnalysisTapSignalState signal, int signalIndex)
    {
        var labelPaint = signal.Mode == AnalysisTapMode.Disabled ? _labelMutedPaint : _labelPaint;
        labelPaint.DrawText(canvas, label, Padding, y + MeterHeight - 5);

        float indicatorY = y + (MeterHeight - IndicatorHeight) / 2f;
        DrawIndicator(canvas, indicatorX, indicatorY, signal.HasUpstream, "PRE");
        DrawIndicator(canvas, indicatorX + IndicatorWidth + IndicatorGap, indicatorY, signal.UsedLater, "USED");

        var meterRect = new SKRect(meterX, y, meterX + meterWidth, y + MeterHeight);
        canvas.DrawRoundRect(new SKRoundRect(meterRect, 4f), _meterBackgroundPaint);

        float normalized = NormalizeSignal(signal.Signal, signal.Value);
        if (normalized > 0f)
        {
            float width = meterRect.Width * MathF.Min(1f, normalized);
            var fillRect = new SKRect(meterRect.Left, meterRect.Top, meterRect.Left + width, meterRect.Bottom);
            canvas.DrawRoundRect(new SKRoundRect(fillRect, 4f), GetMeterPaint(signal.Signal));
        }

        string valueLabel = FormatValue(signal.Signal, signal.Value);
        _valuePaint.DrawText(canvas, valueLabel, valueX + ValueWidth, y + MeterHeight - 5);

        for (int i = 0; i < 3; i++)
        {
            float toggleLeft = toggleX + i * (ToggleWidth + ToggleGap);
            var toggleRect = new SKRect(toggleLeft, y + (MeterHeight - ToggleHeight) / 2f, toggleLeft + ToggleWidth,
                y + (MeterHeight + ToggleHeight) / 2f);
            _modeToggleRects[signalIndex, i] = toggleRect;

            var mode = ModeOrder[i];
            var togglePaint = signal.Mode == mode ? GetModePaint(mode) : _toggleOffPaint;
            canvas.DrawRoundRect(new SKRoundRect(toggleRect, 4f), togglePaint);
            canvas.DrawRoundRect(new SKRoundRect(toggleRect, 4f), _borderPaint);
            _toggleTextPaint.DrawText(canvas, ToggleLabels[i], toggleRect.MidX, toggleRect.MidY + 3);
        }
    }

    private void DrawIndicator(SKCanvas canvas, float x, float y, bool active, string label)
    {
        var rect = new SKRect(x, y, x + IndicatorWidth, y + IndicatorHeight);
        canvas.DrawRoundRect(new SKRoundRect(rect, 4f), active ? _indicatorOnPaint : _indicatorOffPaint);
        _indicatorTextPaint.DrawText(canvas, label, rect.MidX, rect.MidY + 3);
    }

    private static string GetLabel(AnalysisSignalId signal)
    {
        int index = (int)signal;
        if ((uint)index < (uint)SignalLabels.Length)
        {
            return SignalLabels[index];
        }

        return signal.ToString();
    }

    private SKPaint GetModePaint(AnalysisTapMode mode)
    {
        return mode switch
        {
            AnalysisTapMode.UseExisting => _modeUsePaint,
            AnalysisTapMode.Generate => _toggleOnPaint,
            AnalysisTapMode.Disabled => _modeDisablePaint,
            _ => _toggleOffPaint
        };
    }

    private SKPaint GetMeterPaint(AnalysisSignalId signal)
    {
        return signal switch
        {
            AnalysisSignalId.SpeechPresence => _speechPaint,
            AnalysisSignalId.VoicingScore => _voicingPaint,
            AnalysisSignalId.VoicingState => _voicingPaint,
            AnalysisSignalId.FricativeActivity => _fricativePaint,
            AnalysisSignalId.SibilanceEnergy => _sibilancePaint,
            AnalysisSignalId.OnsetFluxHigh => _fluxPaint,
            AnalysisSignalId.PitchHz => _pitchPaint,
            AnalysisSignalId.PitchConfidence => _pitchPaint,
            AnalysisSignalId.FormantF1Hz => _formantPaint,
            AnalysisSignalId.FormantF2Hz => _formantPaint,
            AnalysisSignalId.FormantF3Hz => _formantPaint,
            AnalysisSignalId.FormantConfidence => _formantPaint,
            AnalysisSignalId.SpectralFlux => _fluxPaint,
            AnalysisSignalId.HnrDb => _hnrPaint,
            _ => _speechPaint
        };
    }

    private static float NormalizeSignal(AnalysisSignalId signal, float value)
    {
        return signal switch
        {
            AnalysisSignalId.SpeechPresence => Clamp01(value),
            AnalysisSignalId.VoicingScore => Clamp01(value),
            AnalysisSignalId.VoicingState => Clamp01(value / 2f),
            AnalysisSignalId.FricativeActivity => Clamp01(value),
            AnalysisSignalId.SibilanceEnergy => Clamp01(value),
            AnalysisSignalId.OnsetFluxHigh => Clamp01(value),
            AnalysisSignalId.PitchHz => NormalizeRange(value, 50f, 1000f),
            AnalysisSignalId.PitchConfidence => Clamp01(value),
            AnalysisSignalId.FormantF1Hz => NormalizeRange(value, 100f, 1000f),
            AnalysisSignalId.FormantF2Hz => NormalizeRange(value, 500f, 3500f),
            AnalysisSignalId.FormantF3Hz => NormalizeRange(value, 1500f, 4500f),
            AnalysisSignalId.FormantConfidence => Clamp01(value),
            AnalysisSignalId.SpectralFlux => NormalizeRange(value, 0f, 2f),
            AnalysisSignalId.HnrDb => NormalizeRange(value, -10f, 30f),
            _ => 0f
        };
    }

    private static string FormatValue(AnalysisSignalId signal, float value)
    {
        return signal switch
        {
            AnalysisSignalId.VoicingState => FormatVoicingState(value),
            AnalysisSignalId.SpeechPresence => $"{value * 100f:0}%",
            AnalysisSignalId.VoicingScore => $"{value * 100f:0}%",
            AnalysisSignalId.FricativeActivity => $"{value * 100f:0}%",
            AnalysisSignalId.SibilanceEnergy => $"{value * 100f:0}%",
            AnalysisSignalId.OnsetFluxHigh => $"{value * 100f:0}%",
            AnalysisSignalId.PitchHz => value > 0f ? $"{value:0} Hz" : "-",
            AnalysisSignalId.PitchConfidence => $"{value * 100f:0}%",
            AnalysisSignalId.FormantF1Hz => value > 0f ? $"{value:0} Hz" : "-",
            AnalysisSignalId.FormantF2Hz => value > 0f ? $"{value:0} Hz" : "-",
            AnalysisSignalId.FormantF3Hz => value > 0f ? $"{value:0} Hz" : "-",
            AnalysisSignalId.FormantConfidence => $"{value * 100f:0}%",
            AnalysisSignalId.SpectralFlux => $"{value:0.00}",
            AnalysisSignalId.HnrDb => $"{value:0.0} dB",
            _ => $"{value:0.00}"
        };
    }

    private static string FormatVoicingState(float value)
    {
        int state = (int)MathF.Round(value);
        return state switch
        {
            1 => "UNV",
            2 => "VOI",
            _ => "SIL"
        };
    }

    private static float NormalizeRange(float value, float min, float max)
    {
        if (max <= min)
        {
            return 0f;
        }

        return Clamp01((value - min) / (max - min));
    }

    private static float Clamp01(float value) => Math.Clamp(value, 0f, 1f);

    private static string[] BuildSignalLabels()
    {
        var labels = new string[(int)AnalysisSignalId.Count];
        foreach (var info in AnalysisTapPlugin.Signals)
        {
            labels[(int)info.Signal] = info.Label;
        }
        return labels;
    }

    private static readonly SKPaint _speechPaint = new()
    {
        Color = new SKColor(0x40, 0xC0, 0x70),
        IsAntialias = true,
        Style = SKPaintStyle.Fill
    };

    private static readonly SKPaint _voicingPaint = new()
    {
        Color = new SKColor(0x60, 0xC0, 0xFF),
        IsAntialias = true,
        Style = SKPaintStyle.Fill
    };

    private static readonly SKPaint _fricativePaint = new()
    {
        Color = new SKColor(0xFF, 0xA0, 0x40),
        IsAntialias = true,
        Style = SKPaintStyle.Fill
    };

    private static readonly SKPaint _sibilancePaint = new()
    {
        Color = new SKColor(0xFF, 0x60, 0x80),
        IsAntialias = true,
        Style = SKPaintStyle.Fill
    };

    private static readonly SKPaint _fluxPaint = new()
    {
        Color = new SKColor(0xF0, 0xC0, 0x40),
        IsAntialias = true,
        Style = SKPaintStyle.Fill
    };

    private static readonly SKPaint _pitchPaint = new()
    {
        Color = new SKColor(0x40, 0xB0, 0xD0),
        IsAntialias = true,
        Style = SKPaintStyle.Fill
    };

    private static readonly SKPaint _formantPaint = new()
    {
        Color = new SKColor(0x60, 0xA0, 0x60),
        IsAntialias = true,
        Style = SKPaintStyle.Fill
    };

    private static readonly SKPaint _hnrPaint = new()
    {
        Color = new SKColor(0x70, 0xA0, 0xD0),
        IsAntialias = true,
        Style = SKPaintStyle.Fill
    };

    // Static paints for per-signal meters (not theme-sensitive).
}

public readonly record struct AnalysisTapSignalState(
    AnalysisSignalId Signal,
    float Value,
    AnalysisTapMode Mode,
    bool HasUpstream,
    bool UsedLater);

public readonly record struct AnalysisTapState(
    AnalysisTapSignalState[] Signals,
    float LatencyMs,
    bool IsBypassed);

public enum AnalysisTapHitArea
{
    None = 0,
    TitleBar = 1,
    CloseButton = 2,
    BypassButton = 3,
    ModeToggle = 4
}

public readonly record struct AnalysisTapHitTest(AnalysisTapHitArea Area, int SignalIndex, AnalysisTapMode Mode);
