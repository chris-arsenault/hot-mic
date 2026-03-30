using System;
using SkiaSharp;
using HotMic.App.ViewModels;

namespace HotMic.App.UI;

internal sealed class MainMasterSectionRenderer
{
    private readonly MainPaintCache _paints;
    private readonly MainRenderPrimitives _primitives;
    private readonly MainMeterRenderer _meterRenderer;
    private readonly MainHitTargetRegistry _hitTargets;

    public MainMasterSectionRenderer(
        MainPaintCache paints,
        MainRenderPrimitives primitives,
        MainMeterRenderer meterRenderer,
        MainHitTargetRegistry hitTargets)
    {
        _paints = paints;
        _primitives = primitives;
        _meterRenderer = meterRenderer;
        _hitTargets = hitTargets;
    }

    public void Render(SKCanvas canvas, SKRect rect, MainViewModel viewModel)
    {
        var roundRect = new SKRoundRect(rect, 6f);
        canvas.DrawRoundRect(roundRect, MainRenderPrimitives.CreateFillPaint(_paints.Theme.MasterSection));
        canvas.DrawRoundRect(roundRect, _paints.BorderPaint);

        canvas.DrawText("MASTER", rect.Left + 6f, rect.Top + 12f, _paints.SmallTextPaint);
        bool lufsMode = viewModel.MasterMeterLufs;
        float? voxTargetDb = viewModel.MeterScaleVox
            ? (lufsMode ? -16f : -18f)
            : null;
        string modeLabel = lufsMode ? "LUFS" : "dB";
        if (voxTargetDb.HasValue)
        {
            modeLabel = $"{modeLabel} {voxTargetDb.Value:0}";
        }
        float modeWidth = _paints.TinyTextPaint.MeasureText(modeLabel);
        canvas.DrawText(modeLabel, rect.Right - modeWidth - 6f, rect.Top + 12f, _paints.TinyTextPaint);

        float meterY = rect.Top + 18f;
        float meterHeight = rect.Height - 44f;
        float leftMeterX = rect.Left + 6f;
        float rightMeterX = leftMeterX + MainLayoutMetrics.MeterWidth + 4f;

        canvas.DrawText("L", leftMeterX + 4f, meterY - 2f, _paints.TinyTextPaint);
        canvas.DrawText("R", rightMeterX + 4f, meterY - 2f, _paints.TinyTextPaint);

        float leftPeak;
        float rightPeak;
        float leftRms;
        float rightRms;
        float leftReadout;
        float rightReadout;

        if (lufsMode)
        {
            const float minLufs = -70f;
            float leftMomentary = viewModel.MasterLufsMomentaryLeft;
            float rightMomentary = viewModel.MasterLufsMomentaryRight;
            float leftShortTerm = viewModel.MasterLufsShortTermLeft;
            float rightShortTerm = viewModel.MasterLufsShortTermRight;

            if (viewModel.MasterMuted)
            {
                leftMomentary = rightMomentary = minLufs;
                leftShortTerm = rightShortTerm = minLufs;
            }

            float leftPeakLufs = MathF.Max(leftMomentary, leftShortTerm);
            float rightPeakLufs = MathF.Max(rightMomentary, rightShortTerm);

            leftRms = MainMeterRenderer.LufsToLinear(leftShortTerm);
            rightRms = MainMeterRenderer.LufsToLinear(rightShortTerm);
            leftPeak = MainMeterRenderer.LufsToLinear(leftPeakLufs);
            rightPeak = MainMeterRenderer.LufsToLinear(rightPeakLufs);
            leftReadout = leftShortTerm;
            rightReadout = rightShortTerm;
        }
        else
        {
            leftPeak = viewModel.MasterPeakLeft;
            rightPeak = viewModel.MasterPeakRight;
            leftRms = viewModel.MasterRmsLeft;
            rightRms = viewModel.MasterRmsRight;

            if (viewModel.MasterMuted)
            {
                leftPeak = rightPeak = leftRms = rightRms = 0f;
            }

            leftReadout = MainMeterRenderer.LinearToDb(leftPeak);
            rightReadout = MainMeterRenderer.LinearToDb(rightPeak);
        }

        _hitTargets.MasterMeterRect = new SKRect(leftMeterX, meterY, rightMeterX + MainLayoutMetrics.MeterWidth, meterY + meterHeight);

        _meterRenderer.DrawVerticalMeter(canvas, leftMeterX, meterY, MainLayoutMetrics.MeterWidth, meterHeight, leftPeak, leftRms, viewModel.MeterScaleVox, voxTargetDb);
        _meterRenderer.DrawVerticalMeter(canvas, rightMeterX, meterY, MainLayoutMetrics.MeterWidth, meterHeight, rightPeak, rightRms, viewModel.MeterScaleVox, voxTargetDb);

        float toggleX = rect.Right - MainLayoutMetrics.ToggleSize - 6f;
        float toggleSpacing = MainLayoutMetrics.ToggleSize + 4f;

        float muteY = rect.Top + 18f;
        var muteRect = new SKRect(toggleX, muteY, toggleX + MainLayoutMetrics.ToggleSize, muteY + MainLayoutMetrics.ToggleSize);
        _primitives.DrawToggleButton(canvas, muteRect, "M", viewModel.MasterMuted, _paints.MutePaint);
        _hitTargets.Toggles.Add(new ToggleRect(-1, ToggleType.MasterMute, muteRect));

        float vizY = muteY + toggleSpacing;
        _hitTargets.VisualizerButtonRect = new SKRect(toggleX, vizY, toggleX + MainLayoutMetrics.ToggleSize, vizY + MainLayoutMetrics.ToggleSize);
        DrawVisualizerButton(canvas, _hitTargets.VisualizerButtonRect);

        float settingsY = vizY + toggleSpacing;
        _hitTargets.AnalysisSettingsButtonRect = new SKRect(toggleX, settingsY, toggleX + MainLayoutMetrics.ToggleSize, settingsY + MainLayoutMetrics.ToggleSize);
        DrawSettingsButton(canvas, _hitTargets.AnalysisSettingsButtonRect);

        float waveformY = settingsY + toggleSpacing;
        _hitTargets.WaveformButtonRect = new SKRect(toggleX, waveformY, toggleX + MainLayoutMetrics.ToggleSize, waveformY + MainLayoutMetrics.ToggleSize);
        DrawWaveformButton(canvas, _hitTargets.WaveformButtonRect);

        float speechY = waveformY + toggleSpacing;
        _hitTargets.SpeechCoachButtonRect = new SKRect(toggleX, speechY, toggleX + MainLayoutMetrics.ToggleSize, speechY + MainLayoutMetrics.ToggleSize);
        DrawSpeechCoachButton(canvas, _hitTargets.SpeechCoachButtonRect);

        float dbY = rect.Bottom - 8f;
        string leftLabel = $"{leftReadout:0.0}";
        string rightLabel = $"{rightReadout:0.0}";
        canvas.DrawText(leftLabel, leftMeterX, dbY, _paints.TinyTextPaint);
        canvas.DrawText(rightLabel, rightMeterX, dbY, _paints.TinyTextPaint);
    }

    private void DrawVisualizerButton(SKCanvas canvas, SKRect rect)
    {
        var roundRect = new SKRoundRect(rect, 3f);
        canvas.DrawRoundRect(roundRect, _paints.ButtonPaint);
        canvas.DrawRoundRect(roundRect, _paints.BorderPaint);

        float midY = rect.MidY;
        float left = rect.Left + 3f;
        float right = rect.Right - 3f;
        float amp = rect.Height * 0.25f;

        using var path = new SKPath();
        path.MoveTo(left, midY);
        path.LineTo(left + 3f, midY - amp);
        path.LineTo(left + 6f, midY + amp);
        path.LineTo(left + 9f, midY - amp * 0.5f);
        path.LineTo(right, midY);

        using var iconPaint = new SKPaint
        {
            Color = _paints.Theme.Accent,
            StrokeWidth = 1.5f,
            Style = SKPaintStyle.Stroke,
            IsAntialias = true
        };
        canvas.DrawPath(path, iconPaint);
    }

    private void DrawSettingsButton(SKCanvas canvas, SKRect rect)
    {
        var roundRect = new SKRoundRect(rect, 3f);
        canvas.DrawRoundRect(roundRect, _paints.ButtonPaint);
        canvas.DrawRoundRect(roundRect, _paints.BorderPaint);

        // Gear icon
        float cx = rect.MidX;
        float cy = rect.MidY;
        float outerRadius = rect.Width * 0.32f;
        float innerRadius = outerRadius * 0.5f;
        int teeth = 6;

        using var iconPaint = new SKPaint
        {
            Color = _paints.Theme.Accent,
            StrokeWidth = 1.5f,
            Style = SKPaintStyle.Stroke,
            IsAntialias = true
        };

        using var path = new SKPath();
        for (int i = 0; i < teeth * 2; i++)
        {
            float angle = (float)(i * Math.PI / teeth - Math.PI / 2);
            float r = (i % 2 == 0) ? outerRadius : outerRadius * 0.7f;
            float x = cx + MathF.Cos(angle) * r;
            float y = cy + MathF.Sin(angle) * r;

            if (i == 0)
                path.MoveTo(x, y);
            else
                path.LineTo(x, y);
        }
        path.Close();

        canvas.DrawPath(path, iconPaint);
        canvas.DrawCircle(cx, cy, innerRadius, iconPaint);
    }

    private void DrawWaveformButton(SKCanvas canvas, SKRect rect)
    {
        var roundRect = new SKRoundRect(rect, 3f);
        canvas.DrawRoundRect(roundRect, _paints.ButtonPaint);
        canvas.DrawRoundRect(roundRect, _paints.BorderPaint);

        // Waveform icon (sine-like wave)
        float midY = rect.MidY;
        float left = rect.Left + 4f;
        float width = rect.Width - 8f;
        float amp = rect.Height * 0.25f;

        using var iconPaint = new SKPaint
        {
            Color = _paints.Theme.Accent,
            StrokeWidth = 1.5f,
            Style = SKPaintStyle.Stroke,
            IsAntialias = true
        };

        using var path = new SKPath();
        path.MoveTo(left, midY);
        for (int i = 0; i <= 20; i++)
        {
            float t = i / 20f;
            float x = left + t * width;
            float y = midY - amp * MathF.Sin(t * MathF.PI * 2);
            if (i == 0)
                path.MoveTo(x, y);
            else
                path.LineTo(x, y);
        }

        canvas.DrawPath(path, iconPaint);
    }

    private void DrawSpeechCoachButton(SKCanvas canvas, SKRect rect)
    {
        var roundRect = new SKRoundRect(rect, 3f);
        canvas.DrawRoundRect(roundRect, _paints.ButtonPaint);
        canvas.DrawRoundRect(roundRect, _paints.BorderPaint);

        // Speech bubble icon
        float cx = rect.MidX;
        float cy = rect.MidY - 1f;
        float w = rect.Width * 0.4f;
        float h = rect.Height * 0.3f;

        using var iconPaint = new SKPaint
        {
            Color = _paints.Theme.Accent,
            StrokeWidth = 1.5f,
            Style = SKPaintStyle.Stroke,
            IsAntialias = true
        };

        // Bubble body
        var bubbleRect = new SKRect(cx - w, cy - h, cx + w, cy + h);
        canvas.DrawRoundRect(new SKRoundRect(bubbleRect, 3f), iconPaint);

        // Tail
        using var tailPath = new SKPath();
        tailPath.MoveTo(cx - 2f, cy + h);
        tailPath.LineTo(cx - 4f, cy + h + 4f);
        tailPath.LineTo(cx + 2f, cy + h);
        canvas.DrawPath(tailPath, iconPaint);
    }
}
