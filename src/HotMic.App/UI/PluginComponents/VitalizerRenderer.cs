using SkiaSharp;

namespace HotMic.App.UI.PluginComponents;

/// <summary>
/// Vitalizer Mk2-T plugin UI with multi-stage controls and toggle switches.
/// </summary>
public sealed class VitalizerRenderer : IDisposable
{
    private const float TitleBarHeight = 40f;
    private const float Padding = 12f;
    private const float KnobRadius = 24f;
    private const float KnobSpacing = 78f;
    private const float CornerRadius = 10f;
    private const float ToggleWidth = 64f;
    private const float ToggleHeight = 20f;
    private const float ToggleSpacing = 10f;

    private readonly PluginComponentTheme _theme;
    private readonly PluginPresetBar _presetBar;

    public KnobWidget DriveKnob { get; }
    public KnobWidget BassKnob { get; }
    public KnobWidget BassCompKnob { get; }
    public KnobWidget MidHiKnob { get; }
    public KnobWidget ProcessKnob { get; }
    public KnobWidget HighFreqKnob { get; }
    public KnobWidget IntensityKnob { get; }
    public KnobWidget HighCompKnob { get; }
    public KnobWidget OutputKnob { get; }

    private readonly SKPaint _backgroundPaint;
    private readonly SKPaint _titleBarPaint;
    private readonly SKPaint _borderPaint;
    private readonly SkiaTextPaint _titlePaint;
    private readonly SkiaTextPaint _closeButtonPaint;
    private readonly SKPaint _bypassPaint;
    private readonly SKPaint _bypassActivePaint;
    private readonly SkiaTextPaint _latencyPaint;

    private readonly SKPaint _toggleFillPaint;
    private readonly SKPaint _toggleActivePaint;
    private readonly SKPaint _toggleBorderPaint;
    private readonly SkiaTextPaint _toggleTextPaint;

    private SKRect _closeButtonRect;
    private SKRect _bypassButtonRect;
    private SKRect _titleBarRect;
    private SKRect _bassLcRect;
    private SKRect _highLcRect;
    private SKRect _tubeRect;
    private SKRect _limitRect;

    public VitalizerRenderer(PluginComponentTheme? theme = null)
    {
        _theme = theme ?? PluginComponentTheme.Default;
        _presetBar = new PluginPresetBar(_theme);

        DriveKnob = new KnobWidget(KnobRadius, -20f, 6f, "DRIVE", "dB", KnobStyle.Standard, _theme)
        {
            ValueFormat = "0.0",
            ShowPositiveSign = true
        };
        BassKnob = new KnobWidget(KnobRadius, -1f, 1f, "BASS", string.Empty, KnobStyle.Bipolar, _theme)
        {
            ValueFormat = "0.00",
            ShowPositiveSign = true
        };
        BassCompKnob = new KnobWidget(KnobRadius, 1f, 10f, "BASS COMP", ":1", KnobStyle.Standard, _theme)
        {
            ValueFormat = "0.0"
        };
        MidHiKnob = new KnobWidget(KnobRadius, 1100f, 22000f, "MID-HI", "Hz", KnobStyle.Standard, _theme)
        {
            ValueFormat = "0",
            IsLogarithmic = true
        };
        ProcessKnob = new KnobWidget(KnobRadius, 0f, 1f, "PROCESS", string.Empty, KnobStyle.Standard, _theme)
        {
            ValueFormat = "0.00"
        };

        HighFreqKnob = new KnobWidget(KnobRadius, 2000f, 20000f, "HIGH FREQ", "Hz", KnobStyle.Standard, _theme)
        {
            ValueFormat = "0",
            IsLogarithmic = true
        };
        IntensityKnob = new KnobWidget(KnobRadius, 0f, 1f, "INTENSITY", string.Empty, KnobStyle.Standard, _theme)
        {
            ValueFormat = "0.00"
        };
        HighCompKnob = new KnobWidget(KnobRadius, 1f, 10f, "HIGH COMP", ":1", KnobStyle.Standard, _theme)
        {
            ValueFormat = "0.0"
        };
        OutputKnob = new KnobWidget(KnobRadius, -20f, 6f, "OUTPUT", "dB", KnobStyle.Standard, _theme)
        {
            ValueFormat = "0.0",
            ShowPositiveSign = true
        };

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

        _latencyPaint = new SkiaTextPaint(_theme.TextMuted, 9f, SKFontStyle.Normal, SKTextAlign.Right);

        _toggleFillPaint = new SKPaint
        {
            Color = _theme.LabelBackground,
            IsAntialias = true,
            Style = SKPaintStyle.Fill
        };
        _toggleActivePaint = new SKPaint
        {
            Color = _theme.AccentSecondary,
            IsAntialias = true,
            Style = SKPaintStyle.Fill
        };
        _toggleBorderPaint = new SKPaint
        {
            Color = _theme.LabelBorder,
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 1f
        };
        _toggleTextPaint = new SkiaTextPaint(_theme.TextPrimary, 9f, SKFontStyle.Bold, SKTextAlign.Center);
    }

    public void Render(SKCanvas canvas, SKSize size, float dpiScale, VitalizerState state)
    {
        canvas.Clear(SKColors.Transparent);
        canvas.Save();
        canvas.Scale(dpiScale);
        size = new SKSize(size.Width / dpiScale, size.Height / dpiScale);

        var backgroundRect = new SKRect(0, 0, size.Width, size.Height);
        var roundRect = new SKRoundRect(backgroundRect, CornerRadius);
        canvas.DrawRoundRect(roundRect, _backgroundPaint);

        DrawTitleBar(canvas, size, state);

        float row1Y = TitleBarHeight + Padding + KnobRadius + 6f;
        float row1Width = 5 * KnobSpacing;
        float row1StartX = (size.Width - row1Width) / 2f + KnobSpacing / 2f;

        DriveKnob.Center = new SKPoint(row1StartX, row1Y);
        DriveKnob.Value = state.DriveDb;
        DriveKnob.Render(canvas);

        BassKnob.Center = new SKPoint(row1StartX + KnobSpacing, row1Y);
        BassKnob.Value = state.Bass;
        BassKnob.Render(canvas);

        BassCompKnob.Center = new SKPoint(row1StartX + KnobSpacing * 2, row1Y);
        BassCompKnob.Value = state.BassCompRatio;
        BassCompKnob.Render(canvas);

        MidHiKnob.Center = new SKPoint(row1StartX + KnobSpacing * 3, row1Y);
        MidHiKnob.Value = state.MidHiTuneHz;
        MidHiKnob.Render(canvas);

        ProcessKnob.Center = new SKPoint(row1StartX + KnobSpacing * 4, row1Y);
        ProcessKnob.Value = state.Process;
        ProcessKnob.Render(canvas);

        float toggleRowY = row1Y + KnobRadius + 26f;
        float togglesWidth = 4 * ToggleWidth + 3 * ToggleSpacing;
        float toggleStartX = (size.Width - togglesWidth) / 2f;

        _bassLcRect = new SKRect(toggleStartX, toggleRowY, toggleStartX + ToggleWidth, toggleRowY + ToggleHeight);
        DrawToggle(canvas, _bassLcRect, "BASS LC", state.BassLcEnabled);

        _highLcRect = new SKRect(_bassLcRect.Right + ToggleSpacing, toggleRowY, _bassLcRect.Right + ToggleSpacing + ToggleWidth, toggleRowY + ToggleHeight);
        DrawToggle(canvas, _highLcRect, "HIGH LC", state.HighLcEnabled);

        _tubeRect = new SKRect(_highLcRect.Right + ToggleSpacing, toggleRowY, _highLcRect.Right + ToggleSpacing + ToggleWidth, toggleRowY + ToggleHeight);
        DrawToggle(canvas, _tubeRect, "TUBE", state.TubeEnabled);

        _limitRect = new SKRect(_tubeRect.Right + ToggleSpacing, toggleRowY, _tubeRect.Right + ToggleSpacing + ToggleWidth, toggleRowY + ToggleHeight);
        DrawToggle(canvas, _limitRect, "LIMIT", state.LimitEnabled);

        float row2Y = toggleRowY + ToggleHeight + 20f + KnobRadius;
        float row2Width = 4 * KnobSpacing;
        float row2StartX = (size.Width - row2Width) / 2f + KnobSpacing / 2f;

        HighFreqKnob.Center = new SKPoint(row2StartX, row2Y);
        HighFreqKnob.Value = state.HighFreqHz;
        HighFreqKnob.Render(canvas);

        IntensityKnob.Center = new SKPoint(row2StartX + KnobSpacing, row2Y);
        IntensityKnob.Value = state.Intensity;
        IntensityKnob.Render(canvas);

        HighCompKnob.Center = new SKPoint(row2StartX + KnobSpacing * 2, row2Y);
        HighCompKnob.Value = state.HighCompRatio;
        HighCompKnob.Render(canvas);

        OutputKnob.Center = new SKPoint(row2StartX + KnobSpacing * 3, row2Y);
        OutputKnob.Value = state.OutputDb;
        OutputKnob.Render(canvas);

        canvas.DrawRoundRect(roundRect, _borderPaint);
        canvas.Restore();
    }

    public static SKSize GetPreferredSize() => new(560, 360);

    public VitalizerHitTest HitTest(float x, float y)
    {
        if (_closeButtonRect.Contains(x, y))
        {
            return new VitalizerHitTest(VitalizerHitArea.CloseButton, -1);
        }

        if (_bypassButtonRect.Contains(x, y))
        {
            return new VitalizerHitTest(VitalizerHitArea.BypassButton, -1);
        }

        var presetHit = _presetBar.HitTest(x, y);
        if (presetHit == PresetBarHitArea.Dropdown)
        {
            return new VitalizerHitTest(VitalizerHitArea.PresetDropdown, -1);
        }

        if (presetHit == PresetBarHitArea.SaveButton)
        {
            return new VitalizerHitTest(VitalizerHitArea.PresetSave, -1);
        }

        if (_bassLcRect.Contains(x, y))
        {
            return new VitalizerHitTest(VitalizerHitArea.BassLcToggle, -1);
        }

        if (_highLcRect.Contains(x, y))
        {
            return new VitalizerHitTest(VitalizerHitArea.HighLcToggle, -1);
        }

        if (_tubeRect.Contains(x, y))
        {
            return new VitalizerHitTest(VitalizerHitArea.TubeToggle, -1);
        }

        if (_limitRect.Contains(x, y))
        {
            return new VitalizerHitTest(VitalizerHitArea.LimitToggle, -1);
        }

        if (_titleBarRect.Contains(x, y))
        {
            return new VitalizerHitTest(VitalizerHitArea.TitleBar, -1);
        }

        return new VitalizerHitTest(VitalizerHitArea.None, -1);
    }

    public SKRect GetPresetDropdownRect() => _presetBar.GetDropdownRect();

    public void Dispose()
    {
        DriveKnob.Dispose();
        BassKnob.Dispose();
        BassCompKnob.Dispose();
        MidHiKnob.Dispose();
        ProcessKnob.Dispose();
        HighFreqKnob.Dispose();
        IntensityKnob.Dispose();
        HighCompKnob.Dispose();
        OutputKnob.Dispose();

        _backgroundPaint.Dispose();
        _titleBarPaint.Dispose();
        _borderPaint.Dispose();
        _bypassPaint.Dispose();
        _bypassActivePaint.Dispose();
        _toggleFillPaint.Dispose();
        _toggleActivePaint.Dispose();
        _toggleBorderPaint.Dispose();
        _titlePaint.Dispose();
        _closeButtonPaint.Dispose();
        _latencyPaint.Dispose();
        _toggleTextPaint.Dispose();
        _presetBar.Dispose();
    }

    private void DrawTitleBar(SKCanvas canvas, SKSize size, VitalizerState state)
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

        canvas.DrawText("Vitalizer Mk2-T", Padding, TitleBarHeight / 2f + 5, _titlePaint);

        float presetBarX = 140f;
        float presetBarY = (TitleBarHeight - PluginPresetBar.TotalHeight) / 2f;
        _presetBar.Render(canvas, presetBarX, presetBarY, state.PresetName);

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

    private void DrawToggle(SKCanvas canvas, SKRect rect, string label, bool isOn)
    {
        var fill = isOn ? _toggleActivePaint : _toggleFillPaint;
        canvas.DrawRoundRect(new SKRoundRect(rect, 6f), fill);
        canvas.DrawRoundRect(new SKRoundRect(rect, 6f), _toggleBorderPaint);

        _toggleTextPaint.Color = isOn ? _theme.TextPrimary : _theme.TextMuted;
        _toggleTextPaint.DrawText(canvas, label, rect.MidX, rect.MidY + 3f, SKTextAlign.Center);
    }
}

public readonly record struct VitalizerState(
    float DriveDb,
    float Bass,
    float BassCompRatio,
    bool BassLcEnabled,
    float MidHiTuneHz,
    float Process,
    float HighFreqHz,
    float Intensity,
    float HighCompRatio,
    bool HighLcEnabled,
    bool TubeEnabled,
    float OutputDb,
    bool LimitEnabled,
    float LatencyMs,
    bool IsBypassed,
    string PresetName);

public readonly record struct VitalizerHitTest(VitalizerHitArea Area, int KnobIndex);

public enum VitalizerHitArea
{
    None,
    TitleBar,
    CloseButton,
    BypassButton,
    PresetDropdown,
    PresetSave,
    BassLcToggle,
    HighLcToggle,
    TubeToggle,
    LimitToggle
}
