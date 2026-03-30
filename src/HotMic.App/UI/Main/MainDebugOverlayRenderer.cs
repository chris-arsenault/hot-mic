using System;
using SkiaSharp;
using HotMic.App.ViewModels;

namespace HotMic.App.UI;

internal sealed class MainDebugOverlayRenderer
{
    private readonly MainPaintCache _paints;
    private readonly MainHitTargetRegistry _hitTargets;
    private const long CopyIndicatorDurationMs = 1500;

    public MainDebugOverlayRenderer(MainPaintCache paints, MainHitTargetRegistry hitTargets)
    {
        _paints = paints;
        _hitTargets = hitTargets;
    }

    public void Render(SKCanvas canvas, MainLayoutFrame layout, MainViewModel viewModel)
    {
        var diag = viewModel.Diagnostics;
        var inputs = diag.Inputs ?? Array.Empty<HotMic.Core.Engine.InputDiagnosticsSnapshot>();
        int inputCount = inputs.Count;

        int activeInputs = 0;
        for (int i = 0; i < inputCount; i++)
        {
            if (inputs[i].IsActive)
            {
                activeInputs++;
            }
        }

        float lineHeight = 14f;
        int inputLines = Math.Max(1, inputCount);
        int outputLines = 10;
        int lineCount = 1 + 2 + 1 + inputLines + 1 + outputLines;
        float overlayHeight = lineCount * lineHeight + 32f;

        float overlayX = MainLayoutMetrics.Padding;
        float overlayY = MainLayoutMetrics.TitleBarHeight + MainLayoutMetrics.HotbarHeight + MainLayoutMetrics.Padding;
        float overlayWidth = layout.Size.Width - MainLayoutMetrics.Padding * 2f - MainLayoutMetrics.MasterWidth - MainLayoutMetrics.Padding;

        var overlayRect = new SKRect(overlayX, overlayY, overlayX + overlayWidth, overlayY + overlayHeight);
        var overlayRound = new SKRoundRect(overlayRect, 6f);

        using var overlayBg = new SKPaint { Color = new SKColor(0x00, 0x00, 0x00, 0xE8), IsAntialias = true };
        canvas.DrawRoundRect(overlayRound, overlayBg);
        canvas.DrawRoundRect(overlayRound, _paints.BorderPaint);

        float col1X = overlayX + 12f;
        float col2X = overlayX + overlayWidth / 2f;
        float textY = overlayY + 18f;

        string headerLabel = "AUDIO ENGINE DIAGNOSTICS";
        bool copyActive = IsCopyIndicatorActive(viewModel.DebugOverlayCopyTicks);
        var iconRect = DrawCopyIcon(canvas, col1X, textY, lineHeight);
        float headerTextX = iconRect.Right + 6f;
        canvas.DrawText(headerLabel, headerTextX, textY, _paints.TextPaint);
        if (copyActive)
        {
            float labelWidth = _paints.TextPaint.MeasureText(headerLabel);
            float copiedX = headerTextX + labelWidth + 8f;
            canvas.DrawText("COPIED", copiedX, textY, MainRenderPrimitives.CreateTextPaint(_paints.Theme.Accent, 9f, SKFontStyle.Bold));
        }
        textY += lineHeight + 4f;

        string outputStatus = diag.OutputActive ? "ACTIVE" : "INACTIVE";
        string monitorStatus = diag.MonitorActive ? "ACTIVE" : "INACTIVE";

        var activeColor = MainRenderPrimitives.CreateTextPaint(new SKColor(0x00, 0xFF, 0x00), 9f);
        var inactiveColor = MainRenderPrimitives.CreateTextPaint(new SKColor(0xFF, 0x66, 0x66), 9f);

        canvas.DrawText("Output:", col1X, textY, _paints.SmallTextPaint);
        canvas.DrawText(outputStatus, col1X + 50f, textY, diag.OutputActive ? activeColor : inactiveColor);
        canvas.DrawText("Monitor:", col2X, textY, _paints.SmallTextPaint);
        canvas.DrawText(monitorStatus, col2X + 55f, textY, diag.MonitorActive ? activeColor : inactiveColor);
        textY += lineHeight;

        canvas.DrawText($"Inputs: {activeInputs}/{inputCount} active", col1X, textY, _paints.SmallTextPaint);
        if (diag.IsRecovering)
        {
            canvas.DrawText("RECOVERING...", col2X, textY, MainRenderPrimitives.CreateTextPaint(new SKColor(0xFF, 0xFF, 0x00), 9f, SKFontStyle.Bold));
        }
        textY += lineHeight + 6f;

        canvas.DrawText("INPUTS", col1X, textY, _paints.TextPaint);
        textY += lineHeight;

        if (inputCount == 0)
        {
            canvas.DrawText("No inputs configured", col1X, textY, _paints.SmallTextPaint);
            textY += lineHeight;
        }
        else
        {
            for (int i = 0; i < inputCount; i++)
            {
                var input = inputs[i];
                float bufPct = input.BufferCapacity > 0 ? 100f * input.BufferedSamples / input.BufferCapacity : 0f;
                string activeLabel = input.IsActive ? "ACTIVE" : "INACTIVE";
                string line = $"Ch {input.ChannelId + 1}: {activeLabel} buf {input.BufferedSamples}/{input.BufferCapacity} ({bufPct:0}%) drop {input.DroppedSamples} over {input.OverflowSamples} under {input.UnderflowSamples} fmt {input.Channels}ch @{input.SampleRate}Hz";
                canvas.DrawText(line, col1X, textY, _paints.SmallTextPaint);
                textY += lineHeight;
            }
        }

        textY += 4f;

        canvas.DrawText("OUTPUT", col1X, textY, _paints.TextPaint);
        textY += lineHeight;

        var dropColor = MainRenderPrimitives.CreateTextPaint(_paints.Theme.MeterClip, 9f);
        var okColor = _paints.SmallTextPaint;
        string dropLine = $"Drops 30s: in-drop {viewModel.InputDrops30Sec} in-under {viewModel.InputUnderflowDrops30Sec} out {viewModel.OutputUnderflowDrops30Sec} total {viewModel.Drops30Sec}";
        canvas.DrawText(dropLine, col1X, textY, viewModel.Drops30Sec > 0 ? dropColor : okColor);
        textY += lineHeight;

        var procPaint = diag.BlockOverrunCount > 0 ? dropColor : okColor;
        canvas.DrawText(viewModel.ProfilingLine, col1X, textY, procPaint);
        textY += lineHeight;

        canvas.DrawText(viewModel.WorstPluginLine, col1X, textY, procPaint);
        textY += lineHeight;

        canvas.DrawText(viewModel.AnalysisTapProfilingLine, col1X, textY, procPaint);
        textY += lineHeight;

        canvas.DrawText(viewModel.AnalysisTapPitchProfilingLine, col1X, textY, procPaint);
        textY += lineHeight;

        canvas.DrawText(viewModel.AnalysisTapGateLine, col1X, textY, procPaint);
        textY += lineHeight;

        canvas.DrawText(viewModel.AnalysisTapCaptureLine, col1X, textY, procPaint);
        textY += lineHeight;

        canvas.DrawText(viewModel.AnalysisOrchestratorLine, col1X, textY, procPaint);
        textY += lineHeight;

        canvas.DrawText(viewModel.VitalizerLine, col1X, textY, _paints.SmallTextPaint);
        textY += lineHeight;

        canvas.DrawText($"Graph: {viewModel.RoutingGraphOrder}", col1X, textY, _paints.SmallTextPaint);
        textY += lineHeight;

        canvas.DrawText($"Output: {diag.OutputCallbackCount} ({diag.LastOutputFrames} frames) under {diag.OutputUnderflowSamples}", col1X, textY, _paints.SmallTextPaint);
        textY += lineHeight;

        canvas.DrawText($"Monitor: {diag.MonitorBufferedSamples}/{diag.MonitorBufferCapacity}", col1X, textY, _paints.SmallTextPaint);
    }

    private SKRect DrawCopyIcon(SKCanvas canvas, float iconX, float textBaselineY, float lineHeight)
    {
        float iconSize = 10f;
        float headerTop = textBaselineY - lineHeight + 2f;
        float iconY = headerTop + (lineHeight - iconSize) * 0.5f;
        var iconRect = new SKRect(iconX, iconY, iconX + iconSize, iconY + iconSize);
        _hitTargets.DebugOverlayCopyRect = iconRect;

        float offset = 2.5f;
        var backRect = new SKRect(iconRect.Left + offset, iconRect.Top + offset, iconRect.Right, iconRect.Bottom);
        canvas.DrawRect(backRect, _paints.IconPaint);
        canvas.DrawRect(iconRect, _paints.IconPaint);
        return iconRect;
    }

    private static bool IsCopyIndicatorActive(long lastCopyTicks)
    {
        if (lastCopyTicks <= 0)
        {
            return false;
        }

        long now = Environment.TickCount64;
        return now - lastCopyTicks <= CopyIndicatorDurationMs;
    }
}
