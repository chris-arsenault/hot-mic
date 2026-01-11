using System;
using System.Collections.Generic;
using System.Globalization;
using HotMic.App.ViewModels;
using HotMic.Common.Configuration;
using HotMic.Common.Models;
using SkiaSharp;

namespace HotMic.App.UI;

public sealed class MainRenderer
{
    // Layout constants
    private const float CornerRadius = 10f;
    private const float TitleBarHeight = 40f;
    private const float Padding = 12f;
    private const float ChannelStripHeight = 120f;
    private const float ChannelSpacing = 8f;
    private const float MasterWidth = 80f;

    // Section dimensions
    private const float InputSectionWidth = 100f;
    private const float PluginSlotWidth = 110f;
    private const float PluginSlotHeight = 28f;
    private const float PluginSlotSpacing = 4f;
    private const float OutputSectionWidth = 100f;
    private const float MeterWidth = 24f;
    private const float MeterHeight = 80f;
    private const float StereoMeterWidth = 44f;
    private const float KnobSize = 48f;
    private const float ToggleSize = 22f;

    // Meter segments
    private const int MeterSegments = 24;
    private const float SegmentGap = 1f;

    private readonly HotMicTheme _theme = HotMicTheme.Default;

    // Pre-allocated paints
    private readonly SKPaint _backgroundPaint;
    private readonly SKPaint _titleBarPaint;
    private readonly SKPaint _borderPaint;
    private readonly SKPaint _borderLightPaint;
    private readonly SKPaint _sectionPaint;
    private readonly SKPaint _pluginSlotEmptyPaint;
    private readonly SKPaint _pluginSlotFilledPaint;
    private readonly SKPaint _pluginSlotBypassedPaint;
    private readonly SKPaint _accentPaint;
    private readonly SKPaint _textPaint;
    private readonly SKPaint _textSecondaryPaint;
    private readonly SKPaint _textMutedPaint;
    private readonly SKPaint _titlePaint;
    private readonly SKPaint _smallTextPaint;
    private readonly SKPaint _meterBackgroundPaint;
    private readonly SKPaint _meterSegmentOffPaint;
    private readonly SKPaint _peakPaint;
    private readonly SKPaint _iconPaint;
    private readonly SKPaint _mutePaint;
    private readonly SKPaint _soloPaint;
    private readonly SKPaint _buttonPaint;
    private readonly SKPaint _debugPanelPaint;
    private readonly SKPaint _debugTextPaint;

    // Hit target storage
    private readonly Dictionary<MainButton, SKRect> _topButtonRects = new();
    private readonly List<KnobRect> _knobRects = new();
    private readonly List<PluginSlotRect> _pluginSlots = new();
    private readonly List<ToggleRect> _toggleRects = new();
    private SKRect _titleBarRect;

    public MainRenderer()
    {
        _backgroundPaint = CreateFillPaint(_theme.BackgroundPrimary);
        _titleBarPaint = CreateFillPaint(_theme.BackgroundSecondary);
        _borderPaint = CreateStrokePaint(_theme.Border, 1f);
        _borderLightPaint = CreateStrokePaint(_theme.BorderLight, 1f);
        _sectionPaint = CreateFillPaint(_theme.BackgroundTertiary);
        _pluginSlotEmptyPaint = CreateFillPaint(_theme.PluginSlotEmpty);
        _pluginSlotFilledPaint = CreateFillPaint(_theme.PluginSlotFilled);
        _pluginSlotBypassedPaint = CreateFillPaint(_theme.PluginSlotBypassed);
        _accentPaint = CreateFillPaint(_theme.Accent);
        _textPaint = CreateTextPaint(_theme.TextPrimary, 12f);
        _textSecondaryPaint = CreateTextPaint(_theme.TextSecondary, 11f);
        _textMutedPaint = CreateTextPaint(_theme.TextMuted, 10f);
        _titlePaint = CreateTextPaint(_theme.TextPrimary, 14f, SKFontStyle.Bold);
        _smallTextPaint = CreateTextPaint(_theme.TextSecondary, 9f);
        _meterBackgroundPaint = CreateFillPaint(_theme.MeterBackground);
        _meterSegmentOffPaint = CreateFillPaint(_theme.MeterSegmentOff);
        _peakPaint = CreateStrokePaint(_theme.TextPrimary, 1.5f);
        _iconPaint = CreateStrokePaint(_theme.TextSecondary, 1.5f);
        _mutePaint = CreateFillPaint(_theme.Mute);
        _soloPaint = CreateFillPaint(_theme.Solo);
        _buttonPaint = CreateFillPaint(_theme.Surface);
        _debugPanelPaint = CreateFillPaint(new SKColor(0x12, 0x12, 0x14, 0xE0));
        _debugTextPaint = CreateTextPaint(_theme.TextSecondary, 10f);
    }

    public void Render(SKCanvas canvas, SKSize size, MainViewModel viewModel, MainUiState uiState, float dpiScale)
    {
        ClearHitTargets();
        canvas.Clear(SKColors.Transparent);

        canvas.Save();
        canvas.Scale(dpiScale);
        size = new SKSize(size.Width / dpiScale, size.Height / dpiScale);

        DrawBackground(canvas, size);
        DrawTitleBar(canvas, size, viewModel);

        if (viewModel.IsMinimalView)
        {
            DrawMinimal(canvas, size, viewModel);
        }
        else
        {
            DrawFull(canvas, size, viewModel, uiState);
        }

        DrawDebugOverlay(canvas, size, viewModel);

        canvas.Restore();
    }

    private void DrawBackground(SKCanvas canvas, SKSize size)
    {
        var rect = new SKRoundRect(new SKRect(0, 0, size.Width, size.Height), CornerRadius);
        canvas.DrawRoundRect(rect, _backgroundPaint);
        canvas.DrawRoundRect(rect, _borderPaint);
    }

    private void DrawTitleBar(SKCanvas canvas, SKSize size, MainViewModel viewModel)
    {
        _titleBarRect = new SKRect(0, 0, size.Width, TitleBarHeight);

        // Draw title bar background clipped to top corners
        using var clip = new SKPath();
        clip.AddRoundRect(new SKRoundRect(new SKRect(0, 0, size.Width, TitleBarHeight + CornerRadius), CornerRadius));
        clip.AddRect(new SKRect(0, TitleBarHeight, size.Width, TitleBarHeight + CornerRadius));
        canvas.Save();
        canvas.ClipPath(clip);
        canvas.DrawRect(_titleBarRect, _titleBarPaint);
        canvas.Restore();

        canvas.DrawLine(0, TitleBarHeight, size.Width, TitleBarHeight, _borderPaint);

        // Logo/title
        canvas.DrawText("HotMic", Padding, TitleBarHeight / 2f + 5f, _titlePaint);

        // Status message
        if (!string.IsNullOrWhiteSpace(viewModel.StatusMessage))
        {
            var statusPaint = CreateTextPaint(_theme.Accent, 11f);
            canvas.DrawText(viewModel.StatusMessage, 80f, TitleBarHeight / 2f + 4f, statusPaint);
        }

        DrawTopButtons(canvas, size, viewModel);
    }

    private void DrawTopButtons(SKCanvas canvas, SKSize size, MainViewModel viewModel)
    {
        _topButtonRects.Clear();
        float right = size.Width - Padding;
        float centerY = TitleBarHeight / 2f;
        float buttonSize = 24f;
        float spacing = 6f;

        // Close
        var closeRect = new SKRect(right - buttonSize, centerY - buttonSize / 2f, right, centerY + buttonSize / 2f);
        DrawIconButton(canvas, closeRect, MainButton.Close, false, IconType.Close);
        right -= buttonSize + spacing;

        // Minimize
        var minRect = new SKRect(right - buttonSize, centerY - buttonSize / 2f, right, centerY + buttonSize / 2f);
        DrawIconButton(canvas, minRect, MainButton.Minimize, false, IconType.Minimize);
        right -= buttonSize + spacing;

        // Pin
        var pinRect = new SKRect(right - buttonSize, centerY - buttonSize / 2f, right, centerY + buttonSize / 2f);
        DrawIconButton(canvas, pinRect, MainButton.Pin, viewModel.AlwaysOnTop, IconType.Pin);
        right -= buttonSize + spacing;

        // Settings
        var settingsRect = new SKRect(right - buttonSize, centerY - buttonSize / 2f, right, centerY + buttonSize / 2f);
        DrawIconButton(canvas, settingsRect, MainButton.Settings, false, IconType.Settings);
        right -= buttonSize + spacing + 8f;

        // View toggle
        string viewLabel = viewModel.IsMinimalView ? "Expand" : "Compact";
        float toggleWidth = 60f;
        var toggleRect = new SKRect(right - toggleWidth, centerY - buttonSize / 2f, right, centerY + buttonSize / 2f);
        DrawTextButton(canvas, toggleRect, viewLabel, MainButton.ToggleView);
    }

    private void DrawFull(SKCanvas canvas, SKSize size, MainViewModel viewModel, MainUiState uiState)
    {
        float contentTop = TitleBarHeight + Padding;
        float contentLeft = Padding;
        float contentRight = size.Width - Padding;

        // Calculate widths
        float masterSectionWidth = MasterWidth + Padding;
        float channelAreaWidth = contentRight - contentLeft - masterSectionWidth - Padding;

        // Draw channel strips
        float channelY = contentTop;
        DrawChannelStrip(canvas, contentLeft, channelY, channelAreaWidth, ChannelStripHeight, viewModel.Channel1, 0, uiState);
        channelY += ChannelStripHeight + ChannelSpacing;
        DrawChannelStrip(canvas, contentLeft, channelY, channelAreaWidth, ChannelStripHeight, viewModel.Channel2, 1, uiState);

        // Draw master section
        float masterX = contentRight - masterSectionWidth;
        float masterHeight = ChannelStripHeight * 2 + ChannelSpacing;
        DrawMasterSection(canvas, masterX, contentTop, masterSectionWidth, masterHeight, viewModel);
    }

    private void DrawChannelStrip(SKCanvas canvas, float x, float y, float width, float height, ChannelStripViewModel channel, int channelIndex, MainUiState uiState)
    {
        // Channel strip background
        var stripRect = new SKRect(x, y, x + width, y + height);
        var stripRound = new SKRoundRect(stripRect, 8f);
        canvas.DrawRoundRect(stripRound, _sectionPaint);
        canvas.DrawRoundRect(stripRound, _borderPaint);

        float sectionX = x + 8f;
        float sectionY = y + 8f;
        float sectionHeight = height - 16f;

        // Input section
        DrawInputSection(canvas, sectionX, sectionY, InputSectionWidth, sectionHeight, channel, channelIndex);
        sectionX += InputSectionWidth + 8f;

        // Plugin chain section
        float pluginAreaWidth = width - InputSectionWidth - OutputSectionWidth - 40f;
        DrawPluginChain(canvas, sectionX, sectionY, pluginAreaWidth, sectionHeight, channel, channelIndex, uiState);
        sectionX += pluginAreaWidth + 8f;

        // Output section
        DrawOutputSection(canvas, sectionX, sectionY, OutputSectionWidth, sectionHeight, channel, channelIndex);
    }

    private void DrawInputSection(SKCanvas canvas, float x, float y, float width, float height, ChannelStripViewModel channel, int channelIndex)
    {
        // Section background
        var rect = new SKRect(x, y, x + width, y + height);
        var roundRect = new SKRoundRect(rect, 6f);
        canvas.DrawRoundRect(roundRect, CreateFillPaint(_theme.ChannelInput));
        canvas.DrawRoundRect(roundRect, _borderPaint);

        // Channel name
        float nameY = y + 14f;
        canvas.DrawText(channel.Name, x + 8f, nameY, _textPaint);

        // Input knob
        float knobX = x + 8f;
        float knobY = y + 22f;
        DrawMiniKnob(canvas, knobX, knobY, channel.InputGainDb, "IN");
        _knobRects.Add(new KnobRect(channelIndex, KnobType.InputGain, new SKRect(knobX, knobY, knobX + KnobSize, knobY + KnobSize)));

        // Input meter
        float meterX = x + width - MeterWidth - 8f;
        float meterY = y + 20f;
        DrawVerticalMeter(canvas, meterX, meterY, MeterWidth, MeterHeight - 10f, channel.InputPeakLevel, channel.InputRmsLevel);

        // Mute/Solo buttons
        float toggleY = y + height - ToggleSize - 6f;
        var muteRect = new SKRect(x + 8f, toggleY, x + 8f + ToggleSize, toggleY + ToggleSize);
        DrawToggleButton(canvas, muteRect, "M", channel.IsMuted, _mutePaint);
        _toggleRects.Add(new ToggleRect(channelIndex, ToggleType.Mute, muteRect));

        var soloRect = new SKRect(x + 8f + ToggleSize + 4f, toggleY, x + 8f + ToggleSize * 2 + 4f, toggleY + ToggleSize);
        DrawToggleButton(canvas, soloRect, "S", channel.IsSoloed, _soloPaint);
        _toggleRects.Add(new ToggleRect(channelIndex, ToggleType.Solo, soloRect));
    }

    private void DrawPluginChain(SKCanvas canvas, float x, float y, float width, float height, ChannelStripViewModel channel, int channelIndex, MainUiState uiState)
    {
        // Section background
        var rect = new SKRect(x, y, x + width, y + height);
        var roundRect = new SKRoundRect(rect, 6f);
        canvas.DrawRoundRect(roundRect, CreateFillPaint(_theme.ChannelPlugins));
        canvas.DrawRoundRect(roundRect, _borderPaint);

        // Label
        canvas.DrawText("PLUGINS", x + 8f, y + 12f, _smallTextPaint);

        // Plugin slots in horizontal layout
        float slotX = x + 8f;
        float slotY = y + 20f;

        for (int i = 0; i < channel.PluginSlots.Count && i < 5; i++)
        {
            var slot = channel.PluginSlots[i];
            DrawPluginSlot(canvas, slotX, slotY, PluginSlotWidth, height - 28f, slot, channelIndex, i, uiState);
            slotX += PluginSlotWidth + PluginSlotSpacing;
        }
    }

    private void DrawPluginSlot(SKCanvas canvas, float x, float y, float width, float height, PluginViewModel slot, int channelIndex, int slotIndex, MainUiState uiState)
    {
        var rect = new SKRect(x, y, x + width, y + height);
        var roundRect = new SKRoundRect(rect, 4f);

        // Background based on state
        SKPaint bgPaint = slot.IsEmpty ? _pluginSlotEmptyPaint :
                          slot.IsBypassed ? _pluginSlotBypassedPaint : _pluginSlotFilledPaint;
        canvas.DrawRoundRect(roundRect, bgPaint);
        canvas.DrawRoundRect(roundRect, _borderPaint);

        // Slot number
        canvas.DrawText($"{slotIndex + 1}", x + 6f, y + 14f, _textMutedPaint);

        if (slot.IsEmpty)
        {
            // Plus icon for empty slot
            float centerX = x + width / 2f;
            float centerY = y + height / 2f;
            canvas.DrawLine(centerX - 8f, centerY, centerX + 8f, centerY, _iconPaint);
            canvas.DrawLine(centerX, centerY - 8f, centerX, centerY + 8f, _iconPaint);
        }
        else
        {
            // Plugin name (vertical)
            canvas.Save();
            canvas.RotateDegrees(-90f, x + 18f, y + height - 8f);
            var textPaint = slot.IsBypassed ? _textMutedPaint : _textSecondaryPaint;
            DrawEllipsizedText(canvas, slot.DisplayName, x + 18f, y + height - 8f, height - 24f, textPaint);
            canvas.Restore();

            // Bypass indicator
            if (slot.IsBypassed)
            {
                float bypassX = x + width - 18f;
                float bypassY = y + 6f;
                var bypassRect = new SKRect(bypassX, bypassY, bypassX + 12f, bypassY + 12f);
                canvas.DrawRoundRect(new SKRoundRect(bypassRect, 2f), CreateFillPaint(_theme.Bypass));
                canvas.DrawText("B", bypassX + 3f, bypassY + 10f, CreateTextPaint(SKColors.White, 8f));
            }

            // Remove button
            float removeX = x + width - 16f;
            float removeY = y + height - 16f;
            canvas.DrawLine(removeX, removeY, removeX + 8f, removeY + 8f, _iconPaint);
            canvas.DrawLine(removeX + 8f, removeY, removeX, removeY + 8f, _iconPaint);
        }

        var bypassHitRect = new SKRect(x + width - 20f, y + 4f, x + width - 4f, y + 20f);
        var removeHitRect = new SKRect(x + width - 20f, y + height - 20f, x + width - 4f, y + height - 4f);
        _pluginSlots.Add(new PluginSlotRect(channelIndex, slotIndex, rect, bypassHitRect, removeHitRect));
    }

    private void DrawOutputSection(SKCanvas canvas, float x, float y, float width, float height, ChannelStripViewModel channel, int channelIndex)
    {
        // Section background
        var rect = new SKRect(x, y, x + width, y + height);
        var roundRect = new SKRoundRect(rect, 6f);
        canvas.DrawRoundRect(roundRect, CreateFillPaint(_theme.ChannelOutput));
        canvas.DrawRoundRect(roundRect, _borderPaint);

        // Label
        canvas.DrawText("OUT", x + 8f, y + 12f, _smallTextPaint);

        // Output knob
        float knobX = x + 8f;
        float knobY = y + 18f;
        DrawMiniKnob(canvas, knobX, knobY, channel.OutputGainDb, "dB");
        _knobRects.Add(new KnobRect(channelIndex, KnobType.OutputGain, new SKRect(knobX, knobY, knobX + KnobSize, knobY + KnobSize)));

        // Output meter
        float meterX = x + width - MeterWidth - 8f;
        float meterY = y + 16f;
        DrawVerticalMeter(canvas, meterX, meterY, MeterWidth, MeterHeight - 6f, channel.OutputPeakLevel, channel.OutputRmsLevel);

        // dB value
        float dbY = y + height - 10f;
        string dbText = $"{ToDb(channel.OutputPeakLevel):0.0}";
        canvas.DrawText(dbText, x + 8f, dbY, _smallTextPaint);
    }

    private void DrawMasterSection(SKCanvas canvas, float x, float y, float width, float height, MainViewModel viewModel)
    {
        // Section background
        var rect = new SKRect(x, y, x + width, y + height);
        var roundRect = new SKRoundRect(rect, 8f);
        canvas.DrawRoundRect(roundRect, CreateFillPaint(_theme.MasterSection));
        canvas.DrawRoundRect(roundRect, _borderPaint);

        // Label
        canvas.DrawText("MASTER", x + (width - 50f) / 2f, y + 16f, _textSecondaryPaint);

        // Stereo meters
        float meterY = y + 28f;
        float meterHeight = height - 60f;
        float leftMeterX = x + (width - StereoMeterWidth) / 2f;
        float rightMeterX = leftMeterX + MeterWidth + 4f;

        // L/R labels
        canvas.DrawText("L", leftMeterX + 8f, meterY - 4f, _smallTextPaint);
        canvas.DrawText("R", rightMeterX + 8f, meterY - 4f, _smallTextPaint);

        // Get combined levels from both channels
        float leftPeak = MathF.Max(viewModel.Channel1.OutputPeakLevel, viewModel.Channel2.OutputPeakLevel * 0.3f);
        float rightPeak = MathF.Max(viewModel.Channel2.OutputPeakLevel, viewModel.Channel1.OutputPeakLevel * 0.3f);
        float leftRms = MathF.Max(viewModel.Channel1.OutputRmsLevel, viewModel.Channel2.OutputRmsLevel * 0.3f);
        float rightRms = MathF.Max(viewModel.Channel2.OutputRmsLevel, viewModel.Channel1.OutputRmsLevel * 0.3f);

        DrawVerticalMeter(canvas, leftMeterX, meterY, MeterWidth, meterHeight, leftPeak, leftRms);
        DrawVerticalMeter(canvas, rightMeterX, meterY, MeterWidth, meterHeight, rightPeak, rightRms);

        // Peak dB values
        float dbY = y + height - 12f;
        string leftDb = $"{ToDb(leftPeak):0.0}";
        string rightDb = $"{ToDb(rightPeak):0.0}";
        canvas.DrawText(leftDb, leftMeterX, dbY, _smallTextPaint);
        canvas.DrawText(rightDb, rightMeterX, dbY, _smallTextPaint);
    }

    private void DrawVerticalMeter(SKCanvas canvas, float x, float y, float width, float height, float peakLevel, float rmsLevel)
    {
        // Background
        var rect = new SKRect(x, y, x + width, y + height);
        canvas.DrawRect(rect, _meterBackgroundPaint);

        // Draw segments
        float segmentHeight = (height - (MeterSegments - 1) * SegmentGap) / MeterSegments;
        float rms = Math.Clamp(rmsLevel, 0f, 1f);
        float peak = Math.Clamp(peakLevel, 0f, 1f);

        for (int i = 0; i < MeterSegments; i++)
        {
            float segY = y + height - (i + 1) * (segmentHeight + SegmentGap);
            float threshold = (i + 0.5f) / MeterSegments;
            bool lit = rms >= threshold;

            var segRect = new SKRect(x + 2f, segY, x + width - 2f, segY + segmentHeight);

            if (lit)
            {
                // Color gradient based on position
                SKColor color = GetMeterSegmentColor(threshold);
                canvas.DrawRect(segRect, CreateFillPaint(color));
            }
            else
            {
                canvas.DrawRect(segRect, _meterSegmentOffPaint);
            }
        }

        // Peak line
        float peakY = y + height - height * peak;
        if (peak > 0.01f)
        {
            var peakColor = peak >= 0.95f ? _theme.MeterClip : _theme.TextPrimary;
            canvas.DrawLine(x, peakY, x + width, peakY, CreateStrokePaint(peakColor, 2f));
        }
    }

    private SKColor GetMeterSegmentColor(float level)
    {
        if (level >= 0.95f) return _theme.MeterClip;
        if (level >= 0.85f) return _theme.MeterWarn;
        if (level >= 0.65f) return _theme.MeterHigh;
        if (level >= 0.35f) return _theme.MeterMid;
        return _theme.MeterLow;
    }

    private void DrawMiniKnob(SKCanvas canvas, float x, float y, float value, string label)
    {
        float size = KnobSize;
        var center = new SKPoint(x + size / 2f, y + size / 2f - 4f);
        float radius = size / 2f - 4f;

        // Knob background
        canvas.DrawCircle(center, radius, CreateFillPaint(_theme.Surface));
        canvas.DrawCircle(center, radius, _borderPaint);

        // Value arc
        float normalized = (value + 60f) / 72f; // -60 to +12 range
        normalized = Math.Clamp(normalized, 0f, 1f);
        float startAngle = 135f;
        float sweepAngle = 270f * normalized;

        using var arc = new SKPath();
        arc.AddArc(new SKRect(center.X - radius, center.Y - radius, center.X + radius, center.Y + radius), startAngle, sweepAngle);
        canvas.DrawPath(arc, CreateStrokePaint(_theme.Accent, 3f));

        // Value indicator line
        float angle = (startAngle + sweepAngle) * MathF.PI / 180f;
        float innerR = radius * 0.5f;
        float outerR = radius * 0.85f;
        canvas.DrawLine(
            center.X + MathF.Cos(angle) * innerR, center.Y + MathF.Sin(angle) * innerR,
            center.X + MathF.Cos(angle) * outerR, center.Y + MathF.Sin(angle) * outerR,
            CreateStrokePaint(_theme.TextPrimary, 2f));

        // Value text
        string valueText = value.ToString("0.0", CultureInfo.InvariantCulture);
        canvas.DrawText(valueText, x + 2f, y + size - 2f, _smallTextPaint);
    }

    private void DrawToggleButton(SKCanvas canvas, SKRect rect, string label, bool isActive, SKPaint activePaint)
    {
        var roundRect = new SKRoundRect(rect, 4f);
        canvas.DrawRoundRect(roundRect, isActive ? activePaint : _buttonPaint);
        canvas.DrawRoundRect(roundRect, _borderPaint);

        var textPaint = isActive
            ? CreateCenteredTextPaint(new SKColor(0x12, 0x12, 0x14), 10f, SKFontStyle.Bold)
            : CreateCenteredTextPaint(_theme.TextSecondary, 10f);
        canvas.DrawText(label, rect.MidX, rect.MidY + 3f, textPaint);
    }

    private void DrawMinimal(SKCanvas canvas, SKSize size, MainViewModel viewModel)
    {
        float y = TitleBarHeight + Padding;
        float width = size.Width - Padding * 2f;
        float rowHeight = 48f;

        DrawMinimalChannelRow(canvas, Padding, y, width, rowHeight, viewModel.Channel1);
        y += rowHeight + 6f;
        DrawMinimalChannelRow(canvas, Padding, y, width, rowHeight, viewModel.Channel2);
    }

    private void DrawMinimalChannelRow(SKCanvas canvas, float x, float y, float width, float height, ChannelStripViewModel channel)
    {
        // Background
        var rect = new SKRect(x, y, x + width, y + height);
        var roundRect = new SKRoundRect(rect, 6f);
        canvas.DrawRoundRect(roundRect, _sectionPaint);
        canvas.DrawRoundRect(roundRect, _borderPaint);

        // Channel name
        canvas.DrawText(channel.Name, x + 12f, y + height / 2f + 4f, _textPaint);

        // Horizontal meter
        float meterX = x + 100f;
        float meterWidth = width - 180f;
        float meterHeight = 16f;
        float meterY = y + (height - meterHeight) / 2f;
        DrawHorizontalMeter(canvas, meterX, meterY, meterWidth, meterHeight, channel.OutputPeakLevel, channel.OutputRmsLevel);

        // dB value
        string dbText = $"{ToDb(channel.OutputPeakLevel):0.0} dB";
        canvas.DrawText(dbText, x + width - 60f, y + height / 2f + 4f, _textSecondaryPaint);
    }

    private void DrawHorizontalMeter(SKCanvas canvas, float x, float y, float width, float height, float peakLevel, float rmsLevel)
    {
        canvas.DrawRect(new SKRect(x, y, x + width, y + height), _meterBackgroundPaint);

        float rms = Math.Clamp(rmsLevel, 0f, 1f);
        float peak = Math.Clamp(peakLevel, 0f, 1f);

        // RMS bar with gradient
        if (rms > 0.01f)
        {
            float rmsWidth = width * rms;
            using var gradient = SKShader.CreateLinearGradient(
                new SKPoint(x, y),
                new SKPoint(x + width, y),
                new[] { _theme.MeterLow, _theme.MeterMid, _theme.MeterHigh, _theme.MeterWarn, _theme.MeterClip },
                new[] { 0f, 0.35f, 0.65f, 0.85f, 1f },
                SKShaderTileMode.Clamp);

            using var gradientPaint = new SKPaint { Shader = gradient, IsAntialias = true };
            canvas.DrawRect(new SKRect(x + 2f, y + 2f, x + 2f + rmsWidth - 4f, y + height - 2f), gradientPaint);
        }

        // Peak line
        float peakX = x + width * peak;
        if (peak > 0.01f)
        {
            var peakColor = peak >= 0.95f ? _theme.MeterClip : _theme.TextPrimary;
            canvas.DrawLine(peakX, y, peakX, y + height, CreateStrokePaint(peakColor, 2f));
        }
    }

    private void DrawIconButton(SKCanvas canvas, SKRect rect, MainButton button, bool isActive, IconType icon)
    {
        var roundRect = new SKRoundRect(rect, 4f);
        canvas.DrawRoundRect(roundRect, isActive ? _accentPaint : _buttonPaint);

        float cx = rect.MidX;
        float cy = rect.MidY;
        float s = 5f;

        switch (icon)
        {
            case IconType.Close:
                canvas.DrawLine(cx - s, cy - s, cx + s, cy + s, _iconPaint);
                canvas.DrawLine(cx + s, cy - s, cx - s, cy + s, _iconPaint);
                break;
            case IconType.Minimize:
                canvas.DrawLine(cx - s, cy + 3f, cx + s, cy + 3f, _iconPaint);
                break;
            case IconType.Pin:
                canvas.DrawCircle(cx, cy - 1f, 3f, _iconPaint);
                canvas.DrawLine(cx, cy + 2f, cx, cy + 5f, _iconPaint);
                break;
            case IconType.Settings:
                canvas.DrawCircle(cx, cy, 3f, _iconPaint);
                for (int i = 0; i < 6; i++)
                {
                    float angle = i * 60f * MathF.PI / 180f;
                    canvas.DrawLine(cx + MathF.Cos(angle) * 4f, cy + MathF.Sin(angle) * 4f,
                                   cx + MathF.Cos(angle) * 6f, cy + MathF.Sin(angle) * 6f, _iconPaint);
                }
                break;
        }

        _topButtonRects[button] = rect;
    }

    private void DrawTextButton(SKCanvas canvas, SKRect rect, string text, MainButton button)
    {
        var roundRect = new SKRoundRect(rect, 4f);
        canvas.DrawRoundRect(roundRect, _buttonPaint);
        canvas.DrawRoundRect(roundRect, _borderPaint);
        canvas.DrawText(text, rect.MidX, rect.MidY + 4f, CreateCenteredTextPaint(_theme.TextSecondary, 10f));
        _topButtonRects[button] = rect;
    }

    private void DrawDebugOverlay(SKCanvas canvas, SKSize size, MainViewModel viewModel)
    {
        var lines = viewModel.DebugLines;
        if (lines.Count == 0) return;

        float padding = 8f;
        float lineHeight = 12f;
        float maxWidth = 0f;
        foreach (var line in lines)
            maxWidth = MathF.Max(maxWidth, _debugTextPaint.MeasureText(line));

        float panelWidth = MathF.Min(size.Width - Padding * 2f, maxWidth + padding * 2f);
        float panelHeight = lines.Count * lineHeight + padding * 2f;
        float x = Padding;
        float y = size.Height - Padding - panelHeight;

        var rect = new SKRoundRect(new SKRect(x, y, x + panelWidth, y + panelHeight), 6f);
        canvas.DrawRoundRect(rect, _debugPanelPaint);
        canvas.DrawRoundRect(rect, _borderPaint);

        float textY = y + padding + lineHeight - 2f;
        foreach (var line in lines)
        {
            canvas.DrawText(line, x + padding, textY, _debugTextPaint);
            textY += lineHeight;
        }
    }

    private void DrawEllipsizedText(SKCanvas canvas, string text, float x, float y, float maxWidth, SKPaint paint)
    {
        if (paint.MeasureText(text) <= maxWidth)
        {
            canvas.DrawText(text, x, y, paint);
            return;
        }
        const string ellipsis = "...";
        float available = MathF.Max(0f, maxWidth - paint.MeasureText(ellipsis));
        int len = text.Length;
        while (len > 0 && paint.MeasureText(text.AsSpan(0, len).ToString()) > available)
            len--;
        canvas.DrawText(len > 0 ? $"{text[..len]}{ellipsis}" : ellipsis, x, y, paint);
    }

    private static float ToDb(float linear) => linear <= 0f ? -60f : 20f * MathF.Log10(linear + 1e-6f);

    private void ClearHitTargets()
    {
        _topButtonRects.Clear();
        _knobRects.Clear();
        _pluginSlots.Clear();
        _toggleRects.Clear();
    }

    // Hit testing
    public MainButton? HitTestTopButton(float x, float y)
    {
        foreach (var (button, rect) in _topButtonRects)
            if (rect.Contains(x, y)) return button;
        return null;
    }

    public KnobHit? HitTestKnob(float x, float y)
    {
        foreach (var knob in _knobRects)
            if (knob.Rect.Contains(x, y)) return new KnobHit(knob.ChannelIndex, knob.KnobType);
        return null;
    }

    public PluginSlotHit? HitTestPluginSlot(float x, float y, out PluginSlotRegion region)
    {
        foreach (var slot in _pluginSlots)
        {
            if (!slot.Rect.Contains(x, y)) continue;
            if (slot.BypassRect.Contains(x, y)) { region = PluginSlotRegion.Bypass; return new PluginSlotHit(slot.ChannelIndex, slot.SlotIndex); }
            if (slot.RemoveRect.Contains(x, y)) { region = PluginSlotRegion.Remove; return new PluginSlotHit(slot.ChannelIndex, slot.SlotIndex); }
            region = PluginSlotRegion.Action;
            return new PluginSlotHit(slot.ChannelIndex, slot.SlotIndex);
        }
        region = PluginSlotRegion.None;
        return null;
    }

    public ToggleHit? HitTestToggle(float x, float y)
    {
        foreach (var toggle in _toggleRects)
            if (toggle.Rect.Contains(x, y)) return new ToggleHit(toggle.ChannelIndex, toggle.ToggleType);
        return null;
    }

    public bool HitTestTitleBar(float x, float y) => _titleBarRect.Contains(x, y);

    // Paint factories
    private static SKPaint CreateFillPaint(SKColor color) => new() { Color = color, IsAntialias = true, Style = SKPaintStyle.Fill };
    private static SKPaint CreateStrokePaint(SKColor color, float width) => new() { Color = color, IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = width };
    private static SKPaint CreateTextPaint(SKColor color, float size, SKFontStyle? style = null) => new()
    {
        Color = color, IsAntialias = true, TextSize = size, TextAlign = SKTextAlign.Left,
        Typeface = SKTypeface.FromFamilyName("Segoe UI", style ?? SKFontStyle.Normal)
    };
    private static SKPaint CreateCenteredTextPaint(SKColor color, float size, SKFontStyle? style = null) => new()
    {
        Color = color, IsAntialias = true, TextSize = size, TextAlign = SKTextAlign.Center,
        Typeface = SKTypeface.FromFamilyName("Segoe UI", style ?? SKFontStyle.Normal)
    };

    // Internal records
    private sealed record KnobRect(int ChannelIndex, KnobType KnobType, SKRect Rect);
    private sealed record PluginSlotRect(int ChannelIndex, int SlotIndex, SKRect Rect, SKRect BypassRect, SKRect RemoveRect);
    private sealed record ToggleRect(int ChannelIndex, ToggleType ToggleType, SKRect Rect);
    private enum IconType { Close, Minimize, Pin, Settings }
}

public readonly record struct DeviceItemHit(DevicePickerTarget Target, int Index);
public readonly record struct KnobHit(int ChannelIndex, KnobType KnobType);
public readonly record struct PluginSlotHit(int ChannelIndex, int SlotIndex);
public enum PluginSlotRegion { None, Action, Bypass, Remove }
public readonly record struct ToggleHit(int ChannelIndex, ToggleType ToggleType);
