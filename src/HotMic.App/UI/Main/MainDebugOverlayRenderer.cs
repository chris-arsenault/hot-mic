using System;
using SkiaSharp;
using HotMic.App.ViewModels;

namespace HotMic.App.UI;

internal sealed class MainDebugOverlayRenderer
{
    private readonly MainPaintCache _paints;

    public MainDebugOverlayRenderer(MainPaintCache paints)
    {
        _paints = paints;
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
        int lineCount = 1 + 2 + 1 + inputLines + 1 + 4;
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

        canvas.DrawText("AUDIO ENGINE DIAGNOSTICS", col1X, textY, _paints.TextPaint);
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
                string line = $"Ch {input.ChannelId + 1}: {activeLabel} buf {input.BufferedSamples}/{input.BufferCapacity} ({bufPct:0}%) drop {input.DroppedSamples} under {input.UnderflowSamples} fmt {input.Channels}ch @{input.SampleRate}Hz";
                canvas.DrawText(line, col1X, textY, _paints.SmallTextPaint);
                textY += lineHeight;
            }
        }

        textY += 4f;

        canvas.DrawText("OUTPUT", col1X, textY, _paints.TextPaint);
        textY += lineHeight;

        var dropColor = MainRenderPrimitives.CreateTextPaint(_paints.Theme.MeterClip, 9f);
        var okColor = _paints.SmallTextPaint;
        string dropLine = $"Drops 30s: in {viewModel.InputDrops30Sec} out {viewModel.OutputUnderflowDrops30Sec} total {viewModel.Drops30Sec}";
        canvas.DrawText(dropLine, col1X, textY, viewModel.Drops30Sec > 0 ? dropColor : okColor);
        textY += lineHeight;

        canvas.DrawText($"Graph: {viewModel.RoutingGraphOrder}", col1X, textY, _paints.SmallTextPaint);
        textY += lineHeight;

        canvas.DrawText($"Output: {diag.OutputCallbackCount} ({diag.LastOutputFrames} frames) under {diag.OutputUnderflowSamples}", col1X, textY, _paints.SmallTextPaint);
        textY += lineHeight;

        canvas.DrawText($"Monitor: {diag.MonitorBufferedSamples}/{diag.MonitorBufferCapacity}", col1X, textY, _paints.SmallTextPaint);
    }
}
