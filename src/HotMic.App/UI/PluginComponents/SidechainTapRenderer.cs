using HotMic.Core.Plugins;
using SkiaSharp;

namespace HotMic.App.UI.PluginComponents;

/// <summary>
/// Sidechain Tap plugin UI - debug/monitor panel showing all sidechain signals.
/// </summary>
public sealed class SidechainTapRenderer : IDisposable
{
    private const float TitleBarHeight = 40f;
    private const float Padding = 12f;
    private const float CornerRadius = 10f;
    private const float MeterHeight = 20f;
    private const float MeterSpacing = 8f;
    private const float LabelWidth = 110f;
    private const float IndicatorWidth = 36f;
    private const float IndicatorHeight = 14f;
    private const float IndicatorGap = 4f;
    private const float ToggleWidth = 32f;
    private const float ToggleHeight = 18f;
    private const float ToggleGap = 4f;
    private const float ValueWidth = 34f;
    private const float ColumnGap = 8f;
    private const int SignalCount = (int)SidechainSignalId.Count;

    private readonly PluginComponentTheme _theme;

    private readonly SKPaint _backgroundPaint;
    private readonly SKPaint _titleBarPaint;
    private readonly SKPaint _borderPaint;
    private readonly SkiaTextPaint _titlePaint;
    private readonly SkiaTextPaint _closeButtonPaint;
    private readonly SKPaint _bypassPaint;
    private readonly SKPaint _bypassActivePaint;
    private readonly SKPaint _meterBackgroundPaint;
    private readonly SKPaint _speechMeterPaint;
    private readonly SKPaint _voicedMeterPaint;
    private readonly SKPaint _unvoicedMeterPaint;
    private readonly SKPaint _sibilanceMeterPaint;
    private readonly SKPaint _toggleOnPaint;
    private readonly SKPaint _toggleOffPaint;
    private readonly SKPaint _modeUsePaint;
    private readonly SKPaint _modeDisablePaint;
    private readonly SKPaint _indicatorOnPaint;
    private readonly SKPaint _indicatorOffPaint;
    private readonly SkiaTextPaint _labelPaint;
    private readonly SkiaTextPaint _valuePaint;
    private readonly SkiaTextPaint _latencyPaint;

    private SKRect _closeButtonRect;
    private SKRect _bypassButtonRect;
    private SKRect _titleBarRect;
    private readonly SKRect[,] _modeToggleRects = new SKRect[SignalCount, 3];
    private static readonly SidechainTapMode[] ModeOrder = [SidechainTapMode.UseExisting, SidechainTapMode.Generate, SidechainTapMode.Disabled];

    public SidechainTapRenderer(PluginComponentTheme? theme = null)
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

        _speechMeterPaint = new SKPaint
        {
            Color = _theme.GateOpen,
            IsAntialias = true,
            Style = SKPaintStyle.Fill
        };

        _voicedMeterPaint = new SKPaint
        {
            Color = new SKColor(0x60, 0xC0, 0xFF), // Light blue
            IsAntialias = true,
            Style = SKPaintStyle.Fill
        };

        _unvoicedMeterPaint = new SKPaint
        {
            Color = new SKColor(0xFF, 0xA0, 0x40), // Orange
            IsAntialias = true,
            Style = SKPaintStyle.Fill
        };

        _sibilanceMeterPaint = new SKPaint
        {
            Color = new SKColor(0xFF, 0x60, 0x80), // Pink
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
        _valuePaint = new SkiaTextPaint(_theme.TextPrimary, 11f, SKFontStyle.Bold, SKTextAlign.Right);
        _latencyPaint = new SkiaTextPaint(_theme.TextMuted, 9f, SKFontStyle.Normal, SKTextAlign.Right);
    }

    public void Render(SKCanvas canvas, SKSize size, float dpiScale, SidechainTapState state)
    {
        canvas.Clear(SKColors.Transparent);
        canvas.Save();
        canvas.Scale(dpiScale);
        size = new SKSize(size.Width / dpiScale, size.Height / dpiScale);

        var backgroundRect = new SKRect(0, 0, size.Width, size.Height);
        var roundRect = new SKRoundRect(backgroundRect, CornerRadius);
        canvas.DrawRoundRect(roundRect, _backgroundPaint);

        DrawTitleBar(canvas, size, state);

        float y = TitleBarHeight + Padding;
        float indicatorGroupWidth = IndicatorWidth * 2 + IndicatorGap;
        float toggleGroupWidth = ToggleWidth * 3 + ToggleGap * 2;
        float meterX = Padding + LabelWidth + indicatorGroupWidth + ColumnGap;
        float meterWidth = size.Width - (Padding * 2 + LabelWidth + indicatorGroupWidth + ValueWidth + toggleGroupWidth + ColumnGap * 3);
        float indicatorX = Padding + LabelWidth;
        float valueX = meterX + meterWidth + ColumnGap;
        float toggleX = valueX + ValueWidth + ColumnGap;

        // Speech Presence
        DrawSignalRow(canvas, y, indicatorX, meterX, meterWidth, valueX, toggleX, "Speech Presence",
            state.Speech, _speechMeterPaint, 0);
        y += MeterHeight + MeterSpacing;

        // Voiced Probability
        DrawSignalRow(canvas, y, indicatorX, meterX, meterWidth, valueX, toggleX, "Voiced Prob.",
            state.Voiced, _voicedMeterPaint, 1);
        y += MeterHeight + MeterSpacing;

        // Unvoiced Energy
        DrawSignalRow(canvas, y, indicatorX, meterX, meterWidth, valueX, toggleX, "Unvoiced Energy",
            state.Unvoiced, _unvoicedMeterPaint, 2);
        y += MeterHeight + MeterSpacing;

        // Sibilance Energy
        DrawSignalRow(canvas, y, indicatorX, meterX, meterWidth, valueX, toggleX, "Sibilance",
            state.Sibilance, _sibilanceMeterPaint, 3);

        canvas.DrawRoundRect(roundRect, _borderPaint);
        canvas.Restore();
    }

    private void DrawTitleBar(SKCanvas canvas, SKSize size, SidechainTapState state)
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

        canvas.DrawText("Sidechain Tap", Padding, TitleBarHeight / 2f + 5, _titlePaint);

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

        _closeButtonRect = new SKRect(size.Width - Padding - 24, (TitleBarHeight - 24) / 2,
            size.Width - Padding, (TitleBarHeight + 24) / 2);
        canvas.DrawText("\u00D7", _closeButtonRect.MidX, _closeButtonRect.MidY + 6, _closeButtonPaint);
    }

    private void DrawSignalRow(SKCanvas canvas, float y, float indicatorX, float meterX, float meterWidth, float valueX, float toggleX,
        string label, in SidechainTapSignalState signal, SKPaint meterPaint, int signalIndex)
    {
        // Label
        canvas.DrawText(label, Padding, y + MeterHeight / 2 + 4, _labelPaint);

        float indicatorY = y + (MeterHeight - IndicatorHeight) / 2f;
        var preRect = new SKRect(indicatorX, indicatorY, indicatorX + IndicatorWidth, indicatorY + IndicatorHeight);
        DrawIndicator(canvas, preRect, "PRE", signal.HasUpstream);

        var usedRect = new SKRect(indicatorX + IndicatorWidth + IndicatorGap, indicatorY,
            indicatorX + IndicatorWidth * 2 + IndicatorGap, indicatorY + IndicatorHeight);
        DrawIndicator(canvas, usedRect, "USED", signal.UsedLater);

        // Meter
        var meterRect = new SKRect(meterX, y, meterX + meterWidth, y + MeterHeight);
        var meterRound = new SKRoundRect(meterRect, 4f);
        canvas.DrawRoundRect(meterRound, _meterBackgroundPaint);

        float fillWidth = (meterRect.Width - 4) * Math.Clamp(signal.Value, 0f, 1f);
        if (fillWidth > 1 && signal.Mode != SidechainTapMode.Disabled)
        {
            var fillRect = new SKRect(meterRect.Left + 2, meterRect.Top + 2,
                meterRect.Left + 2 + fillWidth, meterRect.Bottom - 2);
            canvas.DrawRoundRect(new SKRoundRect(fillRect, 2f), meterPaint);
        }

        canvas.DrawRoundRect(meterRound, _borderPaint);

        // Value display
        canvas.DrawText($"{signal.Value:0.00}", valueX + ValueWidth, y + MeterHeight / 2 + 4, _valuePaint);

        // Mode toggles
        float toggleY = y + (MeterHeight - ToggleHeight) / 2f;
        for (int i = 0; i < ModeOrder.Length; i++)
        {
            float x = toggleX + i * (ToggleWidth + ToggleGap);
            var toggleRect = new SKRect(x, toggleY, x + ToggleWidth, toggleY + ToggleHeight);
            _modeToggleRects[signalIndex, i] = toggleRect;
            DrawModeToggle(canvas, toggleRect, ModeOrder[i], signal.Mode == ModeOrder[i]);
        }
    }

    private void DrawIndicator(SKCanvas canvas, SKRect rect, string label, bool active)
    {
        var round = new SKRoundRect(rect, 3f);
        canvas.DrawRoundRect(round, active ? _indicatorOnPaint : _indicatorOffPaint);
        canvas.DrawRoundRect(round, _borderPaint);

        using var textPaint = new SkiaTextPaint(active ? _theme.PanelBackground : _theme.TextMuted, 8f, SKFontStyle.Bold, SKTextAlign.Center);
        canvas.DrawText(label, rect.MidX, rect.MidY + 3, textPaint);
    }

    private void DrawModeToggle(SKCanvas canvas, SKRect rect, SidechainTapMode mode, bool selected)
    {
        var paint = selected ? GetModePaint(mode) : _toggleOffPaint;
        var round = new SKRoundRect(rect, 4f);
        canvas.DrawRoundRect(round, paint);
        canvas.DrawRoundRect(round, _borderPaint);

        string label = mode switch
        {
            SidechainTapMode.UseExisting => "USE",
            SidechainTapMode.Generate => "GEN",
            SidechainTapMode.Disabled => "OFF",
            _ => "?"
        };

        using var textPaint = new SkiaTextPaint(selected ? _theme.PanelBackground : _theme.TextSecondary, 8f, SKFontStyle.Bold, SKTextAlign.Center);
        canvas.DrawText(label, rect.MidX, rect.MidY + 3, textPaint);
    }

    private SKPaint GetModePaint(SidechainTapMode mode)
    {
        return mode switch
        {
            SidechainTapMode.UseExisting => _modeUsePaint,
            SidechainTapMode.Generate => _toggleOnPaint,
            SidechainTapMode.Disabled => _modeDisablePaint,
            _ => _toggleOnPaint
        };
    }

    public SidechainTapHitTest HitTest(float x, float y)
    {
        if (_closeButtonRect.Contains(x, y))
            return new SidechainTapHitTest(SidechainTapHitArea.CloseButton, -1, SidechainTapMode.Generate);

        if (_bypassButtonRect.Contains(x, y))
            return new SidechainTapHitTest(SidechainTapHitArea.BypassButton, -1, SidechainTapMode.Generate);

        for (int signalIndex = 0; signalIndex < SignalCount; signalIndex++)
        {
            for (int modeIndex = 0; modeIndex < ModeOrder.Length; modeIndex++)
            {
                if (_modeToggleRects[signalIndex, modeIndex].Contains(x, y))
                {
                    return new SidechainTapHitTest(SidechainTapHitArea.ModeToggle, signalIndex, ModeOrder[modeIndex]);
                }
            }
        }

        if (_titleBarRect.Contains(x, y))
            return new SidechainTapHitTest(SidechainTapHitArea.TitleBar, -1, SidechainTapMode.Generate);

        return new SidechainTapHitTest(SidechainTapHitArea.None, -1, SidechainTapMode.Generate);
    }

    public static SKSize GetPreferredSize() => new(520, 210);

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
        _speechMeterPaint.Dispose();
        _voicedMeterPaint.Dispose();
        _unvoicedMeterPaint.Dispose();
        _sibilanceMeterPaint.Dispose();
        _toggleOnPaint.Dispose();
        _toggleOffPaint.Dispose();
        _modeUsePaint.Dispose();
        _modeDisablePaint.Dispose();
        _indicatorOnPaint.Dispose();
        _indicatorOffPaint.Dispose();
        _labelPaint.Dispose();
        _valuePaint.Dispose();
        _latencyPaint.Dispose();
    }
}

public readonly record struct SidechainTapSignalState(
    float Value,
    SidechainTapMode Mode,
    bool HasUpstream,
    bool UsedLater);

public record struct SidechainTapState(
    SidechainTapSignalState Speech,
    SidechainTapSignalState Voiced,
    SidechainTapSignalState Unvoiced,
    SidechainTapSignalState Sibilance,
    float LatencyMs,
    bool IsBypassed);

public enum SidechainTapHitArea
{
    None,
    TitleBar,
    CloseButton,
    BypassButton,
    ModeToggle
}

public record struct SidechainTapHitTest(SidechainTapHitArea Area, int SignalIndex, SidechainTapMode Mode);
