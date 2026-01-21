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
}
