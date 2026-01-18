using HotMic.App.ViewModels;
using HotMic.Core.Dsp;
using SkiaSharp;

namespace HotMic.App.UI;

/// <summary>
/// Renders a plugin slot shell and owns hit testing for the slot and its knobs.
/// </summary>
public sealed class PluginShellRenderer
{
    private const float DeltaStripHeight = 18f;
    private const float PluginKnobSize = 24f;

    private readonly HotMicTheme _theme = HotMicTheme.Default;
    private readonly SKPaint _pluginSlotEmptyPaint;
    private readonly SKPaint _pluginSlotFilledPaint;
    private readonly SKPaint _pluginSlotBypassedPaint;
    private readonly SKPaint _borderPaint;
    private readonly SKPaint _iconPaint;
    private readonly SkiaTextPaint _tinyTextPaint;
    private readonly SKPaint _clipPaint;

    private readonly List<PluginSlotRect> _pluginSlots = new();
    private readonly List<PluginKnobRect> _pluginKnobs = new();

    public PluginShellRenderer()
    {
        _pluginSlotEmptyPaint = CreateFillPaint(_theme.PluginSlotEmpty);
        _pluginSlotFilledPaint = CreateFillPaint(_theme.PluginSlotFilled);
        _pluginSlotBypassedPaint = CreateFillPaint(_theme.PluginSlotBypassed);
        _borderPaint = CreateStrokePaint(_theme.Border, 1f);
        _iconPaint = CreateStrokePaint(_theme.TextSecondary, 1.5f);
        _tinyTextPaint = CreateTextPaint(_theme.TextMuted, 7f);
        _clipPaint = CreateFillPaint(_theme.MeterClip);
    }

    public void ClearHitTargets()
    {
        _pluginSlots.Clear();
        _pluginKnobs.Clear();
    }

    public void DrawSlot(SKCanvas canvas, SKRect rect, PluginViewModel slot, int channelIndex, int slotIndex)
    {
        float x = rect.Left;
        float y = rect.Top;
        float width = rect.Width;
        float height = rect.Height;
        bool isPinned = slot.PluginId == "builtin:bus-input";

        var roundRect = new SKRoundRect(rect, 3f);

        SKPaint bgPaint = slot.IsEmpty ? _pluginSlotEmptyPaint :
                          slot.IsBypassed ? _pluginSlotBypassedPaint : _pluginSlotFilledPaint;
        canvas.DrawRoundRect(roundRect, bgPaint);
        canvas.DrawRoundRect(roundRect, _borderPaint);

        if (slot.IsEmpty)
        {
            canvas.DrawText($"{slotIndex + 1}", x + 3f, y + 10f, _tinyTextPaint);

            float centerX = x + width / 2f;
            float centerY = y + height / 2f;
            canvas.DrawLine(centerX - 6f, centerY, centerX + 6f, centerY, _iconPaint);
            canvas.DrawLine(centerX, centerY - 6f, centerX, centerY + 6f, _iconPaint);
        }
        else
        {
            float topRowY = y + 2f;
            float topRowH = 12f;

            float bypassW = 20f;
            float bypassX = x + 3f;
            var bypassRect = new SKRect(bypassX, topRowY, bypassX + bypassW, topRowY + topRowH);
            var bypassColor = slot.IsBypassed ? _theme.Bypass : _theme.Surface;
            canvas.DrawRoundRect(new SKRoundRect(bypassRect, 2f), CreateFillPaint(bypassColor));
            var bypassTextPaint = slot.IsBypassed
                ? CreateCenteredTextPaint(new SKColor(0x12, 0x12, 0x14), 7f, SKFontStyle.Bold)
                : CreateCenteredTextPaint(_theme.TextMuted, 7f);
            canvas.DrawText("BYP", bypassRect.MidX, bypassRect.MidY + 2.5f, bypassTextPaint);

            float removeSize = 8f;
            float removeX = x + width - removeSize - 4f;
            float removeY = topRowY + (topRowH - removeSize) / 2f;
            if (!isPinned)
            {
                var removeIconPaint = CreateStrokePaint(_theme.TextMuted, 1.2f);
                canvas.DrawLine(removeX, removeY, removeX + removeSize, removeY + removeSize, removeIconPaint);
                canvas.DrawLine(removeX + removeSize, removeY, removeX, removeY + removeSize, removeIconPaint);
            }

            if (slot.IsClipping)
            {
                float clipSize = 6f;
                float clipX = (isPinned ? x + width - clipSize - 4f : removeX - clipSize - 4f);
                float clipY = topRowY + (topRowH - clipSize) / 2f;
                canvas.DrawCircle(clipX + clipSize / 2f, clipY + clipSize / 2f, clipSize / 2f, _clipPaint);
            }

            string displayText = $"{slotIndex + 1}. {slot.DisplayName}";
            var namePaint = slot.IsBypassed
                ? CreateCenteredTextPaint(_theme.TextMuted, 8f)
                : CreateCenteredTextPaint(_theme.TextSecondary, 8f);
            float nameY = topRowY + topRowH - 2f;
            float nameLeftEdge = bypassX + bypassW + 4f;
            float nameRightEdge = removeX - 4f;
            float maxNameWidth = nameRightEdge - nameLeftEdge;
            float nameCenterX = nameLeftEdge + maxNameWidth / 2f;

            if (namePaint.MeasureText(displayText) > maxNameWidth)
            {
                int len = displayText.Length;
                while (len > 0 && namePaint.MeasureText(displayText[..len] + "..") > maxNameWidth)
                    len--;
                displayText = len > 0 ? displayText[..len] + ".." : "..";
            }
            canvas.DrawText(displayText, nameCenterX, nameY, namePaint);

            float largerKnobSize = PluginKnobSize + 4f;
            if (slot.ElevatedParams is { Length: > 0 } elevParams)
            {
                float knobRadius = largerKnobSize / 2f - 2f;
                float knobY = y + 20f;
                float knobSpacing = 14f;
                float totalKnobWidth = (largerKnobSize * 2) + knobSpacing;
                float knobStartX = x + (width - totalKnobWidth) / 2f;

                if (elevParams.Length > 0)
                {
                    float knob0X = knobStartX + largerKnobSize / 2f;
                    var def0 = elevParams[0];
                    float norm0 = (slot.Param0Value - def0.Min) / (def0.Max - def0.Min);
                    norm0 = Math.Clamp(norm0, 0f, 1f);
                    DrawPluginKnob(canvas, knob0X, knobY + knobRadius, knobRadius, norm0, def0.Name, slot.IsBypassed);
                    var knobRect0 = new SKRect(knob0X - knobRadius - 2f, knobY, knob0X + knobRadius + 2f, knobY + largerKnobSize);
                    _pluginKnobs.Add(new PluginKnobRect(channelIndex, slot.InstanceId, 0, knobRect0, def0.Min, def0.Max));
                }

                if (elevParams.Length > 1)
                {
                    float knob1X = knobStartX + largerKnobSize + knobSpacing + largerKnobSize / 2f;
                    var def1 = elevParams[1];
                    float norm1 = (slot.Param1Value - def1.Min) / (def1.Max - def1.Min);
                    norm1 = Math.Clamp(norm1, 0f, 1f);
                    DrawPluginKnob(canvas, knob1X, knobY + knobRadius, knobRadius, norm1, def1.Name, slot.IsBypassed);
                    var knobRect1 = new SKRect(knob1X - knobRadius - 2f, knobY, knob1X + knobRadius + 2f, knobY + largerKnobSize);
                    _pluginKnobs.Add(new PluginKnobRect(channelIndex, slot.InstanceId, 1, knobRect1, def1.Min, def1.Max));
                }
            }

            float deltaY = y + height - DeltaStripHeight - 2f;
            string modeChar = slot.DeltaDisplayMode == DeltaDisplayMode.VocalRange ? "V" : "F";
            var modePaint = CreateCenteredTextPaint(_theme.TextMuted, 7f);
            float modeX = x + 8f;
            float modeY = deltaY - 3f;
            canvas.DrawText(modeChar, modeX, modeY, modePaint);

            float deltaWidth = width - 4f;
            DrawDeltaStrip(canvas, x + 2f, deltaY, deltaWidth, DeltaStripHeight, slot.SpectralDelta, slot.DeltaDisplayMode, slot.IsBypassed);
        }

        var bypassHitRect = slot.IsEmpty ? SKRect.Empty : new SKRect(x + 1f, y + 1f, x + 26f, y + 16f);
        var removeHitRect = slot.IsEmpty || isPinned ? SKRect.Empty : new SKRect(x + width - 16f, y + 1f, x + width - 1f, y + 16f);
        var deltaHitRect = slot.IsEmpty ? SKRect.Empty : new SKRect(x + 2f, y + height - DeltaStripHeight - 2f, x + width - 2f, y + height - 2f);
        _pluginSlots.Add(new PluginSlotRect(channelIndex, slot.InstanceId, slotIndex, rect, bypassHitRect, removeHitRect, deltaHitRect));
    }

    public PluginKnobHit? HitTestKnob(float x, float y)
    {
        foreach (var knob in _pluginKnobs)
        {
            if (knob.Rect.Contains(x, y))
            {
                return new PluginKnobHit(knob.ChannelIndex, knob.PluginInstanceId, knob.ParamIndex, knob.MinValue, knob.MaxValue);
            }
        }

        return null;
    }

    public PluginSlotHit? HitTestSlot(float x, float y, out PluginSlotRegion region)
    {
        foreach (var slot in _pluginSlots)
        {
            if (!slot.Rect.Contains(x, y))
            {
                continue;
            }

            if (slot.BypassRect.Contains(x, y))
            {
                region = PluginSlotRegion.Bypass;
                return new PluginSlotHit(slot.ChannelIndex, slot.PluginInstanceId, slot.SlotIndex);
            }
            if (slot.RemoveRect.Contains(x, y))
            {
                region = PluginSlotRegion.Remove;
                return new PluginSlotHit(slot.ChannelIndex, slot.PluginInstanceId, slot.SlotIndex);
            }
            if (slot.DeltaStripRect.Contains(x, y))
            {
                region = PluginSlotRegion.DeltaStrip;
                return new PluginSlotHit(slot.ChannelIndex, slot.PluginInstanceId, slot.SlotIndex);
            }

            region = PluginSlotRegion.Action;
            return new PluginSlotHit(slot.ChannelIndex, slot.PluginInstanceId, slot.SlotIndex);
        }

        region = PluginSlotRegion.None;
        return null;
    }

    public PluginSlotHit? HitTestSlot(float x, float y, out PluginSlotRegion region, out SKRect rect)
    {
        foreach (var slot in _pluginSlots)
        {
            if (!slot.Rect.Contains(x, y))
            {
                continue;
            }

            rect = slot.Rect;

            if (slot.BypassRect.Contains(x, y))
            {
                region = PluginSlotRegion.Bypass;
                return new PluginSlotHit(slot.ChannelIndex, slot.PluginInstanceId, slot.SlotIndex);
            }
            if (slot.RemoveRect.Contains(x, y))
            {
                region = PluginSlotRegion.Remove;
                return new PluginSlotHit(slot.ChannelIndex, slot.PluginInstanceId, slot.SlotIndex);
            }
            if (slot.DeltaStripRect.Contains(x, y))
            {
                region = PluginSlotRegion.DeltaStrip;
                return new PluginSlotHit(slot.ChannelIndex, slot.PluginInstanceId, slot.SlotIndex);
            }

            region = PluginSlotRegion.Action;
            return new PluginSlotHit(slot.ChannelIndex, slot.PluginInstanceId, slot.SlotIndex);
        }

        region = PluginSlotRegion.None;
        rect = SKRect.Empty;
        return null;
    }

    public SKRect GetSlotRectByIndex(int channelIndex, int slotIndex)
    {
        foreach (var slot in _pluginSlots)
        {
            if (slot.ChannelIndex == channelIndex && slot.SlotIndex == slotIndex)
            {
                return slot.Rect;
            }
        }
        return SKRect.Empty;
    }

    private void DrawPluginKnob(SKCanvas canvas, float cx, float cy, float radius, float normalizedValue, string label, bool dimmed)
    {
        var bgColor = dimmed ? _theme.Surface.WithAlpha(100) : _theme.Surface;
        canvas.DrawCircle(cx, cy, radius, CreateFillPaint(bgColor));
        canvas.DrawCircle(cx, cy, radius, _borderPaint);

        float startAngle = 135f;
        float sweepAngle = 270f * normalizedValue;
        using var arc = new SKPath();
        arc.AddArc(new SKRect(cx - radius + 2f, cy - radius + 2f, cx + radius - 2f, cy + radius - 2f), startAngle, sweepAngle);
        var arcColor = dimmed ? _theme.Accent.WithAlpha(100) : _theme.Accent;
        canvas.DrawPath(arc, CreateStrokePaint(arcColor, 2f));

        float angle = (startAngle + sweepAngle) * MathF.PI / 180f;
        float innerR = radius * 0.3f;
        float outerR = radius * 0.7f;
        var pointerColor = dimmed ? _theme.TextPrimary.WithAlpha(100) : _theme.TextPrimary;
        canvas.DrawLine(
            cx + MathF.Cos(angle) * innerR, cy + MathF.Sin(angle) * innerR,
            cx + MathF.Cos(angle) * outerR, cy + MathF.Sin(angle) * outerR,
            CreateStrokePaint(pointerColor, 1f));

        var labelColor = dimmed ? _theme.TextMuted.WithAlpha(100) : _theme.TextMuted;
        var labelPaint = CreateCenteredTextPaint(labelColor, 6f);
        canvas.DrawText(label, cx, cy + radius + 7f, labelPaint);
    }

    private void DrawDeltaStrip(SKCanvas canvas, float x, float y, float width, float height,
        float[]? deltas, DeltaDisplayMode mode, bool bypassed)
    {
        var bgColor = bypassed ? _theme.DeltaNeutral.WithAlpha(100) : _theme.DeltaNeutral;
        canvas.DrawRect(new SKRect(x, y, x + width, y + height), CreateFillPaint(bgColor));

        if (deltas is null || bypassed)
        {
            return;
        }

        const int numBands = 32;
        float bandWidth = width / numBands;
        float centerY = y + height / 2f;
        float maxBarHeight = (height - 2f) / 2f;

        canvas.DrawLine(x, centerY, x + width, centerY, CreateStrokePaint(_theme.DeltaCenterLine, 0.5f));

        for (int i = 0; i < numBands && i < deltas.Length; i++)
        {
            float delta = deltas[i];
            if (MathF.Abs(delta) < 0.5f)
            {
                continue;
            }

            float barHeight = MathF.Min(MathF.Abs(delta) / 12f, 1f) * maxBarHeight;
            float barX = x + i * bandWidth + 0.5f;
            float barW = bandWidth - 1f;

            SKColor color;
            float barY;

            if (delta > 0)
            {
                color = _theme.DeltaBoost;
                barY = centerY - barHeight;
            }
            else
            {
                color = _theme.DeltaCut;
                barY = centerY;
            }

            canvas.DrawRect(new SKRect(barX, barY, barX + barW, barY + barHeight), CreateFillPaint(color));
        }
    }

    private static SKPaint CreateFillPaint(SKColor color) => new() { Color = color, IsAntialias = true, Style = SKPaintStyle.Fill };

    private static SKPaint CreateStrokePaint(SKColor color, float width) => new() { Color = color, IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = width };

    private static SkiaTextPaint CreateTextPaint(SKColor color, float size, SKFontStyle? style = null) =>
        new(color, size, style, SKTextAlign.Left);

    private static SkiaTextPaint CreateCenteredTextPaint(SKColor color, float size, SKFontStyle? style = null) =>
        new(color, size, style, SKTextAlign.Center);

    private sealed record PluginSlotRect(int ChannelIndex, int PluginInstanceId, int SlotIndex, SKRect Rect, SKRect BypassRect, SKRect RemoveRect, SKRect DeltaStripRect);

    private sealed record PluginKnobRect(int ChannelIndex, int PluginInstanceId, int ParamIndex, SKRect Rect, float MinValue, float MaxValue);
}
