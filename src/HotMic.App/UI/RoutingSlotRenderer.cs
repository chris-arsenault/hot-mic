using System;
using System.Collections.Generic;
using HotMic.App.ViewModels;
using SkiaSharp;

namespace HotMic.App.UI;

/// <summary>
/// Renders compact routing plugin slots (Input, BusInput, OutputSend, Copy, Merge).
/// These have specialized layouts with inline controls and reduced widths.
/// </summary>
public sealed class RoutingSlotRenderer
{
    // Slot widths for different routing plugin types
    public const float InputSlotWidth = 56f;
    public const float OutputSlotWidth = 48f;
    public const float CopySlotWidth = 36f;
    public const float MergeSlotWidth = 64f;

    private const float KnobSize = 20f;
    private const float MeterWidth = 8f;

    private readonly HotMicTheme _theme = HotMicTheme.Default;
    private readonly SKPaint _inputBgPaint;
    private readonly SKPaint _outputBgPaint;
    private readonly SKPaint _copyBgPaint;
    private readonly SKPaint _mergeBgPaint;
    private readonly SKPaint _borderPaint;
    private readonly SKPaint _accentBorderPaint;
    private readonly SKPaint _iconPaint;
    private readonly SkiaTextPaint _labelPaint;
    private readonly SkiaTextPaint _valuePaint;
    private readonly SkiaTextPaint _badgePaint;

    private readonly List<RoutingSlotRect> _routingSlots = new();
    private readonly List<RoutingKnobRect> _routingKnobs = new();
    private readonly List<RoutingBadgeRect> _routingBadges = new();

    public RoutingSlotRenderer()
    {
        _inputBgPaint = CreateFillPaint(_theme.ChannelInput);
        _outputBgPaint = CreateFillPaint(_theme.ChannelOutput);
        _copyBgPaint = CreateFillPaint(_theme.PluginSlotFilled.WithAlpha(180));
        _mergeBgPaint = CreateFillPaint(_theme.ChannelInput.WithAlpha(200));
        _borderPaint = CreateStrokePaint(_theme.Border, 1f);
        _accentBorderPaint = CreateStrokePaint(_theme.BorderLight, 1f);
        _iconPaint = CreateStrokePaint(_theme.TextSecondary, 1.5f);
        _labelPaint = CreateTextPaint(_theme.TextMuted, 7f);
        _valuePaint = CreateTextPaint(_theme.TextSecondary, 8f);
        _badgePaint = CreateCenteredTextPaint(_theme.TextPrimary, 7f, SKFontStyle.Bold);
    }

    public void ClearHitTargets()
    {
        _routingSlots.Clear();
        _routingKnobs.Clear();
        _routingBadges.Clear();
    }

    /// <summary>
    /// Returns the appropriate slot width for a routing plugin, or 0 if not a routing plugin.
    /// </summary>
    public static float GetRoutingSlotWidth(string pluginId)
    {
        return pluginId switch
        {
            "builtin:input" or "builtin:bus-input" => InputSlotWidth,
            "builtin:output-send" => OutputSlotWidth,
            "builtin:copy" => CopySlotWidth,
            "builtin:merge" => MergeSlotWidth,
            _ => 0f
        };
    }

    /// <summary>
    /// Returns true if the plugin ID is a routing plugin that should use compact rendering.
    /// </summary>
    public static bool IsRoutingPlugin(string pluginId)
    {
        return pluginId is "builtin:input" or "builtin:bus-input" or "builtin:output-send"
                       or "builtin:copy" or "builtin:merge";
    }

    /// <summary>
    /// Draws a routing plugin slot with compact layout and inline controls.
    /// </summary>
    public void DrawRoutingSlot(SKCanvas canvas, SKRect rect, PluginViewModel slot,
        int channelIndex, int slotIndex, ChannelStripViewModel channel, bool voxScale)
    {
        switch (slot.PluginId)
        {
            case "builtin:input":
                DrawInputSlot(canvas, rect, slot, channelIndex, slotIndex, channel, isBusInput: false, voxScale);
                break;
            case "builtin:bus-input":
                DrawInputSlot(canvas, rect, slot, channelIndex, slotIndex, channel, isBusInput: true, voxScale);
                break;
            case "builtin:output-send":
                DrawOutputSlot(canvas, rect, slot, channelIndex, slotIndex, channel, voxScale);
                break;
            case "builtin:copy":
                DrawCopySlot(canvas, rect, slot, channelIndex, slotIndex);
                break;
            case "builtin:merge":
                DrawMergeSlot(canvas, rect, slot, channelIndex, slotIndex);
                break;
        }
    }

    private void DrawInputSlot(SKCanvas canvas, SKRect rect, PluginViewModel slot,
        int channelIndex, int slotIndex, ChannelStripViewModel channel, bool isBusInput, bool voxScale)
    {
        float x = rect.Left;
        float y = rect.Top;
        float width = rect.Width;
        float height = rect.Height;

        // Background
        var roundRect = new SKRoundRect(rect, 4f);
        canvas.DrawRoundRect(roundRect, _inputBgPaint);
        canvas.DrawRoundRect(roundRect, _accentBorderPaint);

        // Device label at top (truncated)
        string deviceLabel = isBusInput ? "Bus In" : TruncateText(channel.InputDeviceLabel, width - 4f, _valuePaint);
        canvas.DrawText(deviceLabel, x + 3f, y + 10f, _valuePaint);

        // Input gain knob
        float knobX = x + 4f;
        float knobY = y + 18f;
        float knobRadius = KnobSize / 2f - 1f;
        float normalized = (channel.InputGainDb + 60f) / 72f; // -60 to +12
        normalized = Math.Clamp(normalized, 0f, 1f);
        DrawMiniKnob(canvas, knobX + knobRadius, knobY + knobRadius, knobRadius, normalized, false);
        _routingKnobs.Add(new RoutingKnobRect(channelIndex, slot.InstanceId, RoutingKnobType.InputGain,
            new SKRect(knobX, knobY, knobX + KnobSize, knobY + KnobSize), -60f, 12f));

        // Input meter on right side
        float meterX = x + width - MeterWidth - 3f;
        float meterY = y + 16f;
        float meterHeight = height - 32f;
        DrawMiniMeter(canvas, meterX, meterY, MeterWidth, meterHeight, channel.InputRmsLevel, voxScale);

        SKRect removeRect = SKRect.Empty;
        if (!isBusInput)
        {
            float removeSize = 8f;
            float removeX = x + width - removeSize - 2f;
            float removeY = y + 2f;
            removeRect = new SKRect(removeX - 2f, removeY - 2f, removeX + removeSize + 2f, removeY + removeSize + 2f);
            canvas.DrawLine(removeX, removeY, removeX + removeSize, removeY + removeSize, _iconPaint);
            canvas.DrawLine(removeX + removeSize, removeY, removeX, removeY + removeSize, _iconPaint);
        }

        // Gain label at bottom
        string gainLabel = $"{channel.InputGainDb:+0.0;-0.0;0}";
        canvas.DrawText(gainLabel, x + 3f, y + height - 4f, _labelPaint);

        // Hit region for the entire slot (opens popup)
        var actionRect = new SKRect(x, y, x + width - MeterWidth - 2f, y + height);
        _routingSlots.Add(new RoutingSlotRect(channelIndex, slot.InstanceId, slotIndex,
            slot.PluginId, rect, actionRect, removeRect, SKRect.Empty));
    }

    private void DrawOutputSlot(SKCanvas canvas, SKRect rect, PluginViewModel slot,
        int channelIndex, int slotIndex, ChannelStripViewModel channel, bool voxScale)
    {
        float x = rect.Left;
        float y = rect.Top;
        float width = rect.Width;
        float height = rect.Height;

        // Background
        var roundRect = new SKRoundRect(rect, 4f);
        canvas.DrawRoundRect(roundRect, _outputBgPaint);
        canvas.DrawRoundRect(roundRect, _accentBorderPaint);

        // Output gain knob at top
        float knobX = x + 3f;
        float knobY = y + 4f;
        float knobRadius = KnobSize / 2f - 1f;
        float normalized = (channel.OutputGainDb + 60f) / 72f; // -60 to +12
        normalized = Math.Clamp(normalized, 0f, 1f);
        DrawMiniKnob(canvas, knobX + knobRadius, knobY + knobRadius, knobRadius, normalized, false);
        _routingKnobs.Add(new RoutingKnobRect(channelIndex, slot.InstanceId, RoutingKnobType.OutputGain,
            new SKRect(knobX, knobY, knobX + KnobSize, knobY + KnobSize), -60f, 12f));

        // Send mode badge below knob (clickable to cycle)
        string modeLabel = slot.Param0Value switch
        {
            0f => "L",
            1f => "R",
            _ => "L+R"
        };
        float badgeX = x + 2f;
        float badgeY = knobY + KnobSize + 2f;
        float badgeWidth = KnobSize + 2f;
        var badgeRect = new SKRect(badgeX, badgeY, badgeX + badgeWidth, badgeY + 12f);
        canvas.DrawRoundRect(new SKRoundRect(badgeRect, 2f), CreateFillPaint(_theme.Accent));
        canvas.DrawText(modeLabel, badgeRect.MidX, badgeRect.MidY + 3f, _badgePaint);
        _routingBadges.Add(new RoutingBadgeRect(channelIndex, slot.InstanceId, RoutingBadgeType.OutputSendMode, badgeRect));

        // Output meter on right side
        float meterX = x + width - MeterWidth - 2f;
        float meterY = y + 4f;
        float meterHeight = height - 20f;
        DrawMiniMeter(canvas, meterX, meterY, MeterWidth, meterHeight, channel.OutputRmsLevel, voxScale);

        // dB label at bottom
        float db = LinearToDb(channel.OutputPeakLevel);
        string dbLabel = $"{db:0.0}";
        canvas.DrawText(dbLabel, x + 3f, y + height - 4f, _labelPaint);

        // No action rect - clicks on badge cycle mode, knob adjusts gain
        _routingSlots.Add(new RoutingSlotRect(channelIndex, slot.InstanceId, slotIndex,
            slot.PluginId, rect, SKRect.Empty, SKRect.Empty, SKRect.Empty));
    }

    private void DrawCopySlot(SKCanvas canvas, SKRect rect, PluginViewModel slot,
        int channelIndex, int slotIndex)
    {
        float x = rect.Left;
        float y = rect.Top;
        float width = rect.Width;
        float height = rect.Height;

        // Background - subtle, as the bridge line is the main visual
        var roundRect = new SKRoundRect(rect, 3f);
        canvas.DrawRoundRect(roundRect, _copyBgPaint);
        canvas.DrawRoundRect(roundRect, slot.IsBypassed ? _borderPaint : _accentBorderPaint);

        // Arrow icon pointing right
        float arrowY = y + height / 2f - 8f;
        float arrowX = x + (width - 12f) / 2f;
        var arrowColor = slot.IsBypassed ? _theme.TextMuted : _theme.Accent;
        var arrowPaint = CreateStrokePaint(arrowColor, 2f);
        canvas.DrawLine(arrowX, arrowY + 4f, arrowX + 10f, arrowY + 4f, arrowPaint);
        canvas.DrawLine(arrowX + 6f, arrowY, arrowX + 10f, arrowY + 4f, arrowPaint);
        canvas.DrawLine(arrowX + 6f, arrowY + 8f, arrowX + 10f, arrowY + 4f, arrowPaint);

        // Target channel badge
        int targetId = slot.CopyTargetChannelId;
        if (targetId > 0)
        {
            string targetLabel = $"CH{targetId}";
            float labelWidth = _labelPaint.MeasureText(targetLabel);
            float labelX = x + (width - labelWidth) / 2f;
            canvas.DrawText(targetLabel, labelX, y + height - 6f, _labelPaint);
        }

        // Remove button (X) at top right if not bypassed
        SKRect removeRect = SKRect.Empty;
        if (!slot.IsBypassed)
        {
            float removeSize = 6f;
            float removeX = x + width - removeSize - 3f;
            float removeY = y + 3f;
            removeRect = new SKRect(removeX - 2f, removeY - 2f, removeX + removeSize + 2f, removeY + removeSize + 2f);
            var removePaint = CreateStrokePaint(_theme.TextMuted, 1f);
            canvas.DrawLine(removeX, removeY, removeX + removeSize, removeY + removeSize, removePaint);
            canvas.DrawLine(removeX + removeSize, removeY, removeX, removeY + removeSize, removePaint);
        }

        _routingSlots.Add(new RoutingSlotRect(channelIndex, slot.InstanceId, slotIndex,
            slot.PluginId, rect, rect, removeRect, SKRect.Empty));
    }

    private void DrawMergeSlot(SKCanvas canvas, SKRect rect, PluginViewModel slot,
        int channelIndex, int slotIndex)
    {
        float x = rect.Left;
        float y = rect.Top;
        float width = rect.Width;
        float height = rect.Height;

        // Background
        var roundRect = new SKRoundRect(rect, 4f);
        canvas.DrawRoundRect(roundRect, _mergeBgPaint);
        canvas.DrawRoundRect(roundRect, slot.IsBypassed ? _borderPaint : _accentBorderPaint);

        // Bypass button
        float bypassW = 18f;
        float bypassX = x + 2f;
        float bypassY = y + 2f;
        var bypassRect = new SKRect(bypassX, bypassY, bypassX + bypassW, bypassY + 10f);
        var bypassColor = slot.IsBypassed ? _theme.Bypass : _theme.Surface;
        canvas.DrawRoundRect(new SKRoundRect(bypassRect, 2f), CreateFillPaint(bypassColor));
        var bypassTextPaint = slot.IsBypassed
            ? CreateCenteredTextPaint(new SKColor(0x12, 0x12, 0x14), 6f, SKFontStyle.Bold)
            : CreateCenteredTextPaint(_theme.TextMuted, 6f);
        canvas.DrawText("BYP", bypassRect.MidX, bypassRect.MidY + 2f, bypassTextPaint);

        // Merge icon (junction symbol) - lines coming in from left, merging to center
        float iconX = x + 8f;
        float iconY = y + 18f;
        float lineSpacing = 10f;
        var linePaint = CreateStrokePaint(slot.IsBypassed ? _theme.TextMuted : _theme.Accent, 1.5f);

        // Draw 3 merge lines pointing to center
        float centerX = x + width / 2f;
        float centerY = y + height / 2f + 4f;
        canvas.DrawLine(iconX, iconY, centerX - 4f, centerY, linePaint);
        canvas.DrawLine(iconX, iconY + lineSpacing, centerX - 4f, centerY, linePaint);
        canvas.DrawLine(iconX, iconY + lineSpacing * 2, centerX - 4f, centerY, linePaint);

        // Sum mode icon at center
        string sumIcon = slot.Param0Value switch
        {
            1f => "\u03BC", // mu for average
            2f => "=",      // equal power
            _ => "\u03A3"   // sigma for sum
        };
        var sumPaint = CreateCenteredTextPaint(slot.IsBypassed ? _theme.TextMuted : _theme.Accent, 12f, SKFontStyle.Bold);
        canvas.DrawText(sumIcon, centerX + 6f, centerY + 4f, sumPaint);

        // Source count label at bottom
        // Param3 = source count in MergePlugin
        int sourceCount = (int)slot.Param0Value; // This should ideally come from a dedicated property
        // For now, we'll show a generic label
        string countLabel = "sources";
        canvas.DrawText(countLabel, x + 4f, y + height - 4f, _labelPaint);

        // Remove button
        SKRect removeRect = SKRect.Empty;
        float removeSize = 6f;
        float removeX = x + width - removeSize - 3f;
        float removeY = y + 3f;
        removeRect = new SKRect(removeX - 2f, removeY - 2f, removeX + removeSize + 2f, removeY + removeSize + 2f);
        var removePaint = CreateStrokePaint(_theme.TextMuted, 1f);
        canvas.DrawLine(removeX, removeY, removeX + removeSize, removeY + removeSize, removePaint);
        canvas.DrawLine(removeX + removeSize, removeY, removeX, removeY + removeSize, removePaint);

        _routingSlots.Add(new RoutingSlotRect(channelIndex, slot.InstanceId, slotIndex,
            slot.PluginId, rect, rect, removeRect, bypassRect));
    }

    private void DrawMiniKnob(SKCanvas canvas, float cx, float cy, float radius, float normalizedValue, bool dimmed)
    {
        var bgColor = dimmed ? _theme.Surface.WithAlpha(100) : _theme.Surface;
        canvas.DrawCircle(cx, cy, radius, CreateFillPaint(bgColor));
        canvas.DrawCircle(cx, cy, radius, _borderPaint);

        float startAngle = 135f;
        float sweepAngle = 270f * normalizedValue;
        using var arc = new SKPath();
        arc.AddArc(new SKRect(cx - radius + 1.5f, cy - radius + 1.5f, cx + radius - 1.5f, cy + radius - 1.5f), startAngle, sweepAngle);
        var arcColor = dimmed ? _theme.Accent.WithAlpha(100) : _theme.Accent;
        canvas.DrawPath(arc, CreateStrokePaint(arcColor, 2f));

        // Pointer
        float angle = (startAngle + sweepAngle) * MathF.PI / 180f;
        float innerR = radius * 0.3f;
        float outerR = radius * 0.7f;
        var pointerColor = dimmed ? _theme.TextPrimary.WithAlpha(100) : _theme.TextPrimary;
        canvas.DrawLine(
            cx + MathF.Cos(angle) * innerR, cy + MathF.Sin(angle) * innerR,
            cx + MathF.Cos(angle) * outerR, cy + MathF.Sin(angle) * outerR,
            CreateStrokePaint(pointerColor, 1f));
    }

    private void DrawMiniMeter(SKCanvas canvas, float x, float y, float width, float height, float level, bool voxScale)
    {
        // Background
        canvas.DrawRect(new SKRect(x, y, x + width, y + height), CreateFillPaint(_theme.MeterBackground));

        if (level <= 0f)
        {
            return;
        }

        float db = LinearToDb(level);
        float normalized;
        if (voxScale)
        {
            // VOX scale: -60 to 0 maps to 0-1, with -18 dB as the "target"
            normalized = Math.Clamp((db + 60f) / 60f, 0f, 1f);
        }
        else
        {
            normalized = Math.Clamp((db + 60f) / 60f, 0f, 1f);
        }

        float fillHeight = height * normalized;
        float fillY = y + height - fillHeight;

        SKColor meterColor;
        if (db > -6f) meterColor = _theme.MeterHigh;
        else if (db > -18f) meterColor = _theme.MeterMid;
        else meterColor = _theme.MeterLow;

        canvas.DrawRect(new SKRect(x, fillY, x + width, y + height), CreateFillPaint(meterColor));
    }

    public RoutingKnobHit? HitTestKnob(float x, float y)
    {
        foreach (var knob in _routingKnobs)
        {
            if (knob.Rect.Contains(x, y))
            {
                return new RoutingKnobHit(knob.ChannelIndex, knob.PluginInstanceId, knob.KnobType, knob.MinValue, knob.MaxValue);
            }
        }
        return null;
    }

    public RoutingSlotHit? HitTestSlot(float x, float y, out RoutingSlotRegion region)
    {
        foreach (var slot in _routingSlots)
        {
            if (!slot.Rect.Contains(x, y))
            {
                continue;
            }

            if (!slot.BypassRect.IsEmpty && slot.BypassRect.Contains(x, y))
            {
                region = RoutingSlotRegion.Bypass;
                return new RoutingSlotHit(slot.ChannelIndex, slot.PluginInstanceId, slot.SlotIndex, slot.PluginId);
            }

            if (!slot.RemoveRect.IsEmpty && slot.RemoveRect.Contains(x, y))
            {
                region = RoutingSlotRegion.Remove;
                return new RoutingSlotHit(slot.ChannelIndex, slot.PluginInstanceId, slot.SlotIndex, slot.PluginId);
            }

            if (slot.ActionRect.Contains(x, y))
            {
                region = RoutingSlotRegion.Action;
                return new RoutingSlotHit(slot.ChannelIndex, slot.PluginInstanceId, slot.SlotIndex, slot.PluginId);
            }

            region = RoutingSlotRegion.None;
            return new RoutingSlotHit(slot.ChannelIndex, slot.PluginInstanceId, slot.SlotIndex, slot.PluginId);
        }

        region = RoutingSlotRegion.None;
        return null;
    }

    public RoutingBadgeHit? HitTestBadge(float x, float y)
    {
        foreach (var badge in _routingBadges)
        {
            if (badge.Rect.Contains(x, y))
            {
                return new RoutingBadgeHit(badge.ChannelIndex, badge.PluginInstanceId, badge.BadgeType);
            }
        }
        return null;
    }

    private static string TruncateText(string text, float maxWidth, SkiaTextPaint paint)
    {
        if (paint.MeasureText(text) <= maxWidth)
        {
            return text;
        }

        int len = text.Length;
        while (len > 0 && paint.MeasureText(text[..len] + "..") > maxWidth)
        {
            len--;
        }
        return len > 0 ? text[..len] + ".." : "..";
    }

    private static float LinearToDb(float linear)
    {
        if (linear <= 0f) return -60f;
        return 20f * MathF.Log10(linear + 1e-6f);
    }

    private static SKPaint CreateFillPaint(SKColor color) => new() { Color = color, IsAntialias = true, Style = SKPaintStyle.Fill };

    private static SKPaint CreateStrokePaint(SKColor color, float width) => new() { Color = color, IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = width };

    private static SkiaTextPaint CreateTextPaint(SKColor color, float size, SKFontStyle? style = null) =>
        new(color, size, style, SKTextAlign.Left);

    private static SkiaTextPaint CreateCenteredTextPaint(SKColor color, float size, SKFontStyle? style = null) =>
        new(color, size, style, SKTextAlign.Center);

    private sealed record RoutingSlotRect(int ChannelIndex, int PluginInstanceId, int SlotIndex, string PluginId, SKRect Rect, SKRect ActionRect, SKRect RemoveRect, SKRect BypassRect);

    private sealed record RoutingKnobRect(int ChannelIndex, int PluginInstanceId, RoutingKnobType KnobType, SKRect Rect, float MinValue, float MaxValue);

    private sealed record RoutingBadgeRect(int ChannelIndex, int PluginInstanceId, RoutingBadgeType BadgeType, SKRect Rect);
}

public enum RoutingKnobType
{
    InputGain,
    OutputGain
}

public enum RoutingSlotRegion
{
    None,
    Action,
    Remove,
    Bypass
}

public readonly record struct RoutingKnobHit(int ChannelIndex, int PluginInstanceId, RoutingKnobType KnobType, float MinValue, float MaxValue);

public readonly record struct RoutingSlotHit(int ChannelIndex, int PluginInstanceId, int SlotIndex, string PluginId);

public readonly record struct RoutingBadgeHit(int ChannelIndex, int PluginInstanceId, RoutingBadgeType BadgeType);

public enum RoutingBadgeType
{
    InputChannelMode,
    OutputSendMode
}
