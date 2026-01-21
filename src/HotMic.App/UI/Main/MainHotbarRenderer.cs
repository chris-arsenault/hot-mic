using System;
using SkiaSharp;
using HotMic.App.ViewModels;
using HotMic.Common.Configuration;

namespace HotMic.App.UI;

internal sealed class MainHotbarRenderer
{
    private readonly MainPaintCache _paints;
    private readonly MainRenderPrimitives _primitives;
    private readonly MainHitTargetRegistry _hitTargets;

    public MainHotbarRenderer(MainPaintCache paints, MainRenderPrimitives primitives, MainHitTargetRegistry hitTargets)
    {
        _paints = paints;
        _primitives = primitives;
        _hitTargets = hitTargets;
    }

    public void Render(SKCanvas canvas, MainLayoutFrame layout, MainViewModel viewModel)
    {
        var rect = layout.HotbarRect;
        canvas.DrawRect(rect, _paints.HotbarPaint);
        canvas.DrawLine(0, rect.Bottom, rect.Width, rect.Bottom, _paints.BorderPaint);

        float toggleX = MainLayoutMetrics.Padding;
        float toggleY = rect.Top + (rect.Height - 16f) / 2f;
        _hitTargets.MeterScaleToggleRect = new SKRect(toggleX, toggleY, toggleX + 40f, toggleY + 16f);

        bool voxActive = viewModel.MeterScaleVox;
        var toggleBg = voxActive ? _paints.AccentPaint : _paints.ButtonPaint;
        canvas.DrawRoundRect(new SKRoundRect(_hitTargets.MeterScaleToggleRect, 3f), toggleBg);
        canvas.DrawRoundRect(new SKRoundRect(_hitTargets.MeterScaleToggleRect, 3f), _paints.BorderPaint);

        string scaleLabel = voxActive ? "VOX" : "dB";
        var scalePaint = voxActive
            ? MainRenderPrimitives.CreateCenteredTextPaint(new SKColor(0x12, 0x12, 0x14), 9f, SKFontStyle.Bold)
            : MainRenderPrimitives.CreateCenteredTextPaint(_paints.Theme.TextSecondary, 9f);
        canvas.DrawText(scaleLabel, _hitTargets.MeterScaleToggleRect.MidX, _hitTargets.MeterScaleToggleRect.MidY + 3f, scalePaint);

        float qualityX = _hitTargets.MeterScaleToggleRect.Right + 6f;
        _hitTargets.QualityToggleRect = new SKRect(qualityX, toggleY, qualityX + 56f, toggleY + 16f);
        bool qualityActive = viewModel.QualityMode == AudioQualityMode.QualityPriority;
        var qualityBg = qualityActive ? _paints.AccentPaint : _paints.ButtonPaint;
        canvas.DrawRoundRect(new SKRoundRect(_hitTargets.QualityToggleRect, 3f), qualityBg);
        canvas.DrawRoundRect(new SKRoundRect(_hitTargets.QualityToggleRect, 3f), _paints.BorderPaint);

        string qualityLabel = qualityActive ? "QUAL" : "LAT";
        var qualityPaint = qualityActive
            ? MainRenderPrimitives.CreateCenteredTextPaint(new SKColor(0x12, 0x12, 0x14), 9f, SKFontStyle.Bold)
            : MainRenderPrimitives.CreateCenteredTextPaint(_paints.Theme.TextSecondary, 9f);
        canvas.DrawText(qualityLabel, _hitTargets.QualityToggleRect.MidX, _hitTargets.QualityToggleRect.MidY + 3f, qualityPaint);

        float presetX = _hitTargets.QualityToggleRect.Right + 12f;
        string presetLabel = $"CH {Math.Max(1, viewModel.ActiveChannelIndex + 1)}";
        DrawPresetSelector(canvas, presetX, toggleY, presetLabel, viewModel.ActiveChannelPresetName,
            out var dropdownRect, out var saveRect);
        _hitTargets.PresetDropdownRect = dropdownRect;
        _hitTargets.TopButtons[MainButton.SavePreset] = saveRect;

        float statsRightX = layout.Size.Width - MainLayoutMetrics.Padding;
        float resetWidth = 28f;
        float resetHeight = 16f;
        float resetX = statsRightX - resetWidth;
        float resetY = toggleY;
        _hitTargets.ReinitializeAudioRect = new SKRect(resetX, resetY, resetX + resetWidth, resetY + resetHeight);

        canvas.DrawRoundRect(new SKRoundRect(_hitTargets.ReinitializeAudioRect, 3f), _paints.ButtonPaint);
        canvas.DrawRoundRect(new SKRoundRect(_hitTargets.ReinitializeAudioRect, 3f), _paints.BorderPaint);
        var resetPaint = MainRenderPrimitives.CreateCenteredTextPaint(_paints.Theme.TextSecondary, 8.5f, SKFontStyle.Bold);
        canvas.DrawText("RST", _hitTargets.ReinitializeAudioRect.MidX, _hitTargets.ReinitializeAudioRect.MidY + 3f, resetPaint);

        statsRightX = resetX - 10f;
        float statsY = rect.Top + rect.Height / 2f + 3f;
        float statsX = statsRightX;

        string dropsText = $"Drops: {viewModel.Drops30Sec}";
        float dropsWidth = _paints.SmallTextPaint.MeasureText(dropsText);
        var dropsPaint = viewModel.Drops30Sec > 0 ? MainRenderPrimitives.CreateTextPaint(_paints.Theme.MeterClip, 8f) : _paints.SmallTextPaint;
        canvas.DrawText(dropsText, statsX - dropsWidth, statsY, dropsPaint);
        statsX -= dropsWidth + 16f;

        string latencyText = $"{viewModel.LatencyMs:0.0}ms";
        float latencyWidth = _paints.SmallTextPaint.MeasureText(latencyText);
        canvas.DrawText(latencyText, statsX - latencyWidth, statsY, _paints.SmallTextPaint);
        statsX -= latencyWidth + 16f;

        string cpuText = $"CPU: {viewModel.CpuUsage:0}%";
        float cpuWidth = _paints.SmallTextPaint.MeasureText(cpuText);
        canvas.DrawText(cpuText, statsX - cpuWidth, statsY, _paints.SmallTextPaint);

        _hitTargets.StatsAreaRect = new SKRect(statsX - cpuWidth - 4f, rect.Top, statsRightX + 4f, rect.Bottom);
    }

    private void DrawPresetSelector(SKCanvas canvas, float x, float y, string label, string presetName,
        out SKRect dropdownRect, out SKRect saveRect)
    {
        canvas.DrawText(label, x, y + 11f, _paints.SmallTextPaint);
        float labelWidth = _paints.SmallTextPaint.MeasureText(label);

        float dropdownX = x + labelWidth + 4f;
        float dropdownWidth = 80f;
        dropdownRect = new SKRect(dropdownX, y, dropdownX + dropdownWidth, y + 16f);

        canvas.DrawRoundRect(new SKRoundRect(dropdownRect, 3f), _paints.ButtonPaint);
        canvas.DrawRoundRect(new SKRoundRect(dropdownRect, 3f), _paints.BorderPaint);

        string displayName = presetName.Length > 10 ? presetName[..10] + ".." : presetName;
        canvas.DrawText(displayName, dropdownX + 4f, y + 11f, _paints.SmallTextPaint);

        float arrowX = dropdownX + dropdownWidth - 10f;
        float arrowY = y + 6f;
        using var arrowPath = new SKPath();
        arrowPath.MoveTo(arrowX - 3f, arrowY);
        arrowPath.LineTo(arrowX + 3f, arrowY);
        arrowPath.LineTo(arrowX, arrowY + 4f);
        arrowPath.Close();
        canvas.DrawPath(arrowPath, MainRenderPrimitives.CreateFillPaint(_paints.Theme.TextMuted));

        float saveX = dropdownRect.Right + 4f;
        saveRect = new SKRect(saveX, y, saveX + 18f, y + 16f);
        canvas.DrawRoundRect(new SKRoundRect(saveRect, 3f), _paints.ButtonPaint);
        canvas.DrawRoundRect(new SKRoundRect(saveRect, 3f), _paints.BorderPaint);

        float iconCx = saveRect.MidX;
        float iconCy = saveRect.MidY;
        canvas.DrawRect(new SKRect(iconCx - 4f, iconCy - 4f, iconCx + 4f, iconCy + 4f),
            MainRenderPrimitives.CreateStrokePaint(_paints.Theme.TextSecondary, 1f));
        canvas.DrawRect(new SKRect(iconCx - 2f, iconCy - 4f, iconCx + 2f, iconCy - 1f),
            MainRenderPrimitives.CreateFillPaint(_paints.Theme.TextSecondary));
    }
}
