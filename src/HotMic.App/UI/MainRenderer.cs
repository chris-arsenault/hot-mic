using System;
using System.Collections.Generic;
using System.Globalization;
using HotMic.App.ViewModels;
using HotMic.Core.Dsp;
using SkiaSharp;

namespace HotMic.App.UI;

public sealed class MainRenderer
{
    // Layout constants
    private const float CornerRadius = 8f;
    private const float TitleBarHeight = 36f;
    private const float HotbarHeight = 24f;
    private const float Padding = 10f;
    private const float ChannelStripHeight = 102f;
    private const float ChannelSpacing = 6f;
    private const float MasterWidth = 90f;

    // Section dimensions
    private const float InputSectionWidth = 70f;
    private const float PluginSlotWidth = 130f; // 32 bands × 4px = 128px + 2px padding for delta strip
    private const float PluginSlotSpacing = 2f;
    private const float OutputSectionWidth = 70f;
    private const float DeltaStripHeight = 18f;
    private const float MeterWidth = 16f;
    private const float MiniMeterWidth = 6f;
    private const float StereoMeterWidth = 36f;
    private const float KnobSize = 36f;
    private const float PluginKnobSize = 24f; // Smaller knobs for plugin parameters
    private const float ToggleSize = 18f;

    // Meter segments
    private const int MeterSegments = 16;
    private const float SegmentGap = 1f;

    private readonly HotMicTheme _theme = HotMicTheme.Default;

    // Pre-allocated paints
    private readonly SKPaint _backgroundPaint;
    private readonly SKPaint _titleBarPaint;
    private readonly SKPaint _hotbarPaint;
    private readonly SKPaint _borderPaint;
    private readonly SKPaint _sectionPaint;
    private readonly SKPaint _pluginSlotEmptyPaint;
    private readonly SKPaint _pluginSlotFilledPaint;
    private readonly SKPaint _pluginSlotBypassedPaint;
    private readonly SKPaint _accentPaint;
    private readonly SkiaTextPaint _textPaint;
    private readonly SkiaTextPaint _textSecondaryPaint;
    private readonly SkiaTextPaint _textMutedPaint;
    private readonly SkiaTextPaint _titlePaint;
    private readonly SkiaTextPaint _smallTextPaint;
    private readonly SkiaTextPaint _tinyTextPaint;
    private readonly SKPaint _meterBackgroundPaint;
    private readonly SKPaint _meterSegmentOffPaint;
    private readonly SKPaint _iconPaint;
    private readonly SKPaint _mutePaint;
    private readonly SKPaint _soloPaint;
    private readonly SKPaint _buttonPaint;

    // Hit target storage
    private readonly Dictionary<MainButton, SKRect> _topButtonRects = new();
    private readonly List<KnobRect> _knobRects = new();
    private readonly List<PluginKnobRect> _pluginKnobRects = new();
    private readonly List<PluginSlotRect> _pluginSlots = new();
    private readonly List<ToggleRect> _toggleRects = new();
    private SKRect _titleBarRect;
    private SKRect _meterScaleToggleRect;
    private SKRect _qualityToggleRect;
    private SKRect _statsAreaRect;
    private SKRect _preset1DropdownRect;
    private SKRect _preset2DropdownRect;
    private SKRect _masterMeterRect;

    public MainRenderer()
    {
        _backgroundPaint = CreateFillPaint(_theme.BackgroundPrimary);
        _titleBarPaint = CreateFillPaint(_theme.BackgroundSecondary);
        _hotbarPaint = CreateFillPaint(new SKColor(0x16, 0x16, 0x1A));
        _borderPaint = CreateStrokePaint(_theme.Border, 1f);
        _sectionPaint = CreateFillPaint(_theme.BackgroundTertiary);
        _pluginSlotEmptyPaint = CreateFillPaint(_theme.PluginSlotEmpty);
        _pluginSlotFilledPaint = CreateFillPaint(_theme.PluginSlotFilled);
        _pluginSlotBypassedPaint = CreateFillPaint(_theme.PluginSlotBypassed);
        _accentPaint = CreateFillPaint(_theme.Accent);
        _textPaint = CreateTextPaint(_theme.TextPrimary, 11f);
        _textSecondaryPaint = CreateTextPaint(_theme.TextSecondary, 10f);
        _textMutedPaint = CreateTextPaint(_theme.TextMuted, 9f);
        _titlePaint = CreateTextPaint(_theme.TextPrimary, 13f, SKFontStyle.Bold);
        _smallTextPaint = CreateTextPaint(_theme.TextSecondary, 8f);
        _tinyTextPaint = CreateTextPaint(_theme.TextMuted, 7f);
        _meterBackgroundPaint = CreateFillPaint(_theme.MeterBackground);
        _meterSegmentOffPaint = CreateFillPaint(_theme.MeterSegmentOff);
        _iconPaint = CreateStrokePaint(_theme.TextSecondary, 1.5f);
        _mutePaint = CreateFillPaint(_theme.Mute);
        _soloPaint = CreateFillPaint(_theme.Solo);
        _buttonPaint = CreateFillPaint(_theme.Surface);
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
            DrawHotbar(canvas, size, viewModel);
            DrawFull(canvas, size, viewModel, uiState);

            if (viewModel.ShowDebugOverlay)
            {
                DrawDebugOverlay(canvas, size, viewModel);
            }
        }

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

        using var clip = new SKPath();
        clip.AddRoundRect(new SKRoundRect(new SKRect(0, 0, size.Width, TitleBarHeight + CornerRadius), CornerRadius));
        clip.AddRect(new SKRect(0, TitleBarHeight, size.Width, TitleBarHeight + CornerRadius));
        canvas.Save();
        canvas.ClipPath(clip);
        canvas.DrawRect(_titleBarRect, _titleBarPaint);
        canvas.Restore();

        canvas.DrawLine(0, TitleBarHeight, size.Width, TitleBarHeight, _borderPaint);
        canvas.DrawText("HotMic", Padding, TitleBarHeight / 2f + 4f, _titlePaint);

        if (!string.IsNullOrWhiteSpace(viewModel.StatusMessage))
        {
            var statusPaint = CreateTextPaint(_theme.Accent, 10f);
            canvas.DrawText(viewModel.StatusMessage, 70f, TitleBarHeight / 2f + 3f, statusPaint);
        }

        DrawTopButtons(canvas, size, viewModel);
    }

    private void DrawTopButtons(SKCanvas canvas, SKSize size, MainViewModel viewModel)
    {
        _topButtonRects.Clear();
        float right = size.Width - Padding;
        float centerY = TitleBarHeight / 2f;
        float buttonSize = 20f;
        float spacing = 4f;

        var closeRect = new SKRect(right - buttonSize, centerY - buttonSize / 2f, right, centerY + buttonSize / 2f);
        DrawIconButton(canvas, closeRect, MainButton.Close, false, IconType.Close);
        right -= buttonSize + spacing;

        var minRect = new SKRect(right - buttonSize, centerY - buttonSize / 2f, right, centerY + buttonSize / 2f);
        DrawIconButton(canvas, minRect, MainButton.Minimize, false, IconType.Minimize);
        right -= buttonSize + spacing;

        var pinRect = new SKRect(right - buttonSize, centerY - buttonSize / 2f, right, centerY + buttonSize / 2f);
        DrawIconButton(canvas, pinRect, MainButton.Pin, viewModel.AlwaysOnTop, IconType.Pin);
        right -= buttonSize + spacing;

        var settingsRect = new SKRect(right - buttonSize, centerY - buttonSize / 2f, right, centerY + buttonSize / 2f);
        DrawIconButton(canvas, settingsRect, MainButton.Settings, false, IconType.Settings);
        right -= buttonSize + spacing + 6f;

        string viewLabel = viewModel.IsMinimalView ? "Expand" : "Compact";
        float toggleWidth = 50f;
        var toggleRect = new SKRect(right - toggleWidth, centerY - buttonSize / 2f, right, centerY + buttonSize / 2f);
        DrawTextButton(canvas, toggleRect, viewLabel, MainButton.ToggleView);
    }

    private void DrawHotbar(SKCanvas canvas, SKSize size, MainViewModel viewModel)
    {
        float y = TitleBarHeight;
        var hotbarRect = new SKRect(0, y, size.Width, y + HotbarHeight);
        canvas.DrawRect(hotbarRect, _hotbarPaint);
        canvas.DrawLine(0, y + HotbarHeight, size.Width, y + HotbarHeight, _borderPaint);

        // Meter scale toggle (left side)
        float toggleX = Padding;
        float toggleY = y + (HotbarHeight - 16f) / 2f;
        _meterScaleToggleRect = new SKRect(toggleX, toggleY, toggleX + 40f, toggleY + 16f);

        bool voxActive = viewModel.MeterScaleVox;
        var toggleBg = voxActive ? _accentPaint : _buttonPaint;
        canvas.DrawRoundRect(new SKRoundRect(_meterScaleToggleRect, 3f), toggleBg);
        canvas.DrawRoundRect(new SKRoundRect(_meterScaleToggleRect, 3f), _borderPaint);

        string scaleLabel = voxActive ? "VOX" : "dB";
        var scalePaint = voxActive ? CreateCenteredTextPaint(new SKColor(0x12, 0x12, 0x14), 9f, SKFontStyle.Bold)
                                   : CreateCenteredTextPaint(_theme.TextSecondary, 9f);
        canvas.DrawText(scaleLabel, _meterScaleToggleRect.MidX, _meterScaleToggleRect.MidY + 3f, scalePaint);

        // Quality mode toggle
        float qualityX = _meterScaleToggleRect.Right + 6f;
        _qualityToggleRect = new SKRect(qualityX, toggleY, qualityX + 56f, toggleY + 16f);
        bool qualityActive = viewModel.QualityMode == HotMic.Common.Configuration.AudioQualityMode.QualityPriority;
        var qualityBg = qualityActive ? _accentPaint : _buttonPaint;
        canvas.DrawRoundRect(new SKRoundRect(_qualityToggleRect, 3f), qualityBg);
        canvas.DrawRoundRect(new SKRoundRect(_qualityToggleRect, 3f), _borderPaint);

        string qualityLabel = qualityActive ? "QUAL" : "LAT";
        var qualityPaint = qualityActive ? CreateCenteredTextPaint(new SKColor(0x12, 0x12, 0x14), 9f, SKFontStyle.Bold)
                                         : CreateCenteredTextPaint(_theme.TextSecondary, 9f);
        canvas.DrawText(qualityLabel, _qualityToggleRect.MidX, _qualityToggleRect.MidY + 3f, qualityPaint);

        // Preset dropdowns (center area)
        float presetX = _qualityToggleRect.Right + 12f;
        DrawPresetSelector(canvas, presetX, toggleY, "C1", viewModel.Channel1PresetName, MainButton.SavePreset1, out _preset1DropdownRect, out var save1Rect);
        _topButtonRects[MainButton.SavePreset1] = save1Rect;

        float preset2X = _preset1DropdownRect.Right + 16f;
        DrawPresetSelector(canvas, preset2X, toggleY, "C2", viewModel.Channel2PresetName, MainButton.SavePreset2, out _preset2DropdownRect, out var save2Rect);
        _topButtonRects[MainButton.SavePreset2] = save2Rect;

        // Stats on right side (clickable to toggle debug overlay)
        float statsRightX = size.Width - Padding;
        float statsY = y + HotbarHeight / 2f + 3f;
        float statsX = statsRightX;

        string dropsText = $"Drops: {viewModel.Drops30Sec}";
        float dropsWidth = _smallTextPaint.MeasureText(dropsText);
        var dropsPaint = viewModel.Drops30Sec > 0 ? CreateTextPaint(_theme.MeterClip, 8f) : _smallTextPaint;
        canvas.DrawText(dropsText, statsX - dropsWidth, statsY, dropsPaint);
        statsX -= dropsWidth + 16f;

        string latencyText = $"{viewModel.LatencyMs:0.0}ms";
        float latencyWidth = _smallTextPaint.MeasureText(latencyText);
        canvas.DrawText(latencyText, statsX - latencyWidth, statsY, _smallTextPaint);
        statsX -= latencyWidth + 16f;

        string cpuText = $"CPU: {viewModel.CpuUsage:0}%";
        float cpuWidth = _smallTextPaint.MeasureText(cpuText);
        canvas.DrawText(cpuText, statsX - cpuWidth, statsY, _smallTextPaint);

        // Store stats area for hit testing
        _statsAreaRect = new SKRect(statsX - cpuWidth - 4f, y, statsRightX + 4f, y + HotbarHeight);
    }

    private void DrawPresetSelector(SKCanvas canvas, float x, float y, string label, string presetName, MainButton saveButton, out SKRect dropdownRect, out SKRect saveRect)
    {
        // Label
        canvas.DrawText(label, x, y + 11f, _smallTextPaint);
        float labelWidth = _smallTextPaint.MeasureText(label);

        // Dropdown
        float dropdownX = x + labelWidth + 4f;
        float dropdownWidth = 80f;
        dropdownRect = new SKRect(dropdownX, y, dropdownX + dropdownWidth, y + 16f);

        canvas.DrawRoundRect(new SKRoundRect(dropdownRect, 3f), _buttonPaint);
        canvas.DrawRoundRect(new SKRoundRect(dropdownRect, 3f), _borderPaint);

        // Truncate preset name if needed
        string displayName = presetName.Length > 10 ? presetName[..10] + ".." : presetName;
        canvas.DrawText(displayName, dropdownX + 4f, y + 11f, _smallTextPaint);

        // Dropdown arrow
        float arrowX = dropdownX + dropdownWidth - 10f;
        float arrowY = y + 6f;
        using var arrowPath = new SKPath();
        arrowPath.MoveTo(arrowX - 3f, arrowY);
        arrowPath.LineTo(arrowX + 3f, arrowY);
        arrowPath.LineTo(arrowX, arrowY + 4f);
        arrowPath.Close();
        canvas.DrawPath(arrowPath, CreateFillPaint(_theme.TextMuted));

        // Save button
        float saveX = dropdownRect.Right + 4f;
        saveRect = new SKRect(saveX, y, saveX + 18f, y + 16f);
        canvas.DrawRoundRect(new SKRoundRect(saveRect, 3f), _buttonPaint);
        canvas.DrawRoundRect(new SKRoundRect(saveRect, 3f), _borderPaint);

        // Save icon (floppy disk outline)
        float iconCx = saveRect.MidX;
        float iconCy = saveRect.MidY;
        canvas.DrawRect(new SKRect(iconCx - 4f, iconCy - 4f, iconCx + 4f, iconCy + 4f), CreateStrokePaint(_theme.TextSecondary, 1f));
        canvas.DrawRect(new SKRect(iconCx - 2f, iconCy - 4f, iconCx + 2f, iconCy - 1f), CreateFillPaint(_theme.TextSecondary));
    }

    private void DrawFull(SKCanvas canvas, SKSize size, MainViewModel viewModel, MainUiState uiState)
    {
        float contentTop = TitleBarHeight + HotbarHeight + Padding;
        float contentLeft = Padding;
        float contentRight = size.Width - Padding;

        float masterSectionWidth = MasterWidth + Padding;
        float channelAreaWidth = contentRight - contentLeft - masterSectionWidth - Padding;

        float channelY = contentTop;
        DrawChannelStrip(canvas, contentLeft, channelY, channelAreaWidth, ChannelStripHeight, viewModel.Channel1, 0, viewModel.Input1IsStereo, uiState, viewModel.MeterScaleVox);
        channelY += ChannelStripHeight + ChannelSpacing;
        DrawChannelStrip(canvas, contentLeft, channelY, channelAreaWidth, ChannelStripHeight, viewModel.Channel2, 1, viewModel.Input2IsStereo, uiState, viewModel.MeterScaleVox);

        float masterX = contentRight - masterSectionWidth;
        float masterHeight = ChannelStripHeight * 2 + ChannelSpacing;
        DrawMasterSection(canvas, masterX, contentTop, masterSectionWidth, masterHeight, viewModel);
    }

    private void DrawChannelStrip(SKCanvas canvas, float x, float y, float width, float height, ChannelStripViewModel channel, int channelIndex, bool isStereo, MainUiState uiState, bool voxScale)
    {
        var stripRect = new SKRect(x, y, x + width, y + height);
        var stripRound = new SKRoundRect(stripRect, 6f);
        canvas.DrawRoundRect(stripRound, _sectionPaint);
        canvas.DrawRoundRect(stripRound, _borderPaint);

        float sectionX = x + 6f;
        float sectionY = y + 6f;
        float sectionHeight = height - 12f;

        DrawInputSection(canvas, sectionX, sectionY, InputSectionWidth, sectionHeight, channel, channelIndex, isStereo, voxScale);
        sectionX += InputSectionWidth + 4f;

        float pluginAreaWidth = width - InputSectionWidth - OutputSectionWidth - 24f;
        DrawPluginChain(canvas, sectionX, sectionY, pluginAreaWidth, sectionHeight, channel, channelIndex, uiState, voxScale);
        sectionX += pluginAreaWidth + 4f;

        DrawOutputSection(canvas, sectionX, sectionY, OutputSectionWidth, sectionHeight, channel, channelIndex, voxScale);
    }

    private void DrawInputSection(SKCanvas canvas, float x, float y, float width, float height, ChannelStripViewModel channel, int channelIndex, bool isStereo, bool voxScale)
    {
        var rect = new SKRect(x, y, x + width, y + height);
        var roundRect = new SKRoundRect(rect, 4f);
        canvas.DrawRoundRect(roundRect, CreateFillPaint(_theme.ChannelInput));
        canvas.DrawRoundRect(roundRect, _borderPaint);

        canvas.DrawText(channel.Name, x + 4f, y + 10f, _smallTextPaint);

        // Stereo toggle (next to name)
        float stereoX = x + width - ToggleSize - 4f;
        float stereoY = y + 2f;
        var stereoRect = new SKRect(stereoX, stereoY, stereoX + ToggleSize, stereoY + ToggleSize);
        DrawToggleButton(canvas, stereoRect, channelIndex == 0 ? "L" : "R", isStereo, _accentPaint);
        _toggleRects.Add(new ToggleRect(channelIndex, ToggleType.InputStereo, stereoRect));

        // Input knob
        float knobX = x + 4f;
        float knobY = y + 14f;
        DrawMiniKnob(canvas, knobX, knobY, KnobSize, channel.InputGainDb);
        _knobRects.Add(new KnobRect(channelIndex, KnobType.InputGain, new SKRect(knobX, knobY, knobX + KnobSize, knobY + KnobSize)));

        // Input meter
        float meterX = x + width - MeterWidth - 4f;
        float meterY = y + 22f;
        DrawVerticalMeter(canvas, meterX, meterY, MeterWidth, height - 28f, channel.InputPeakLevel, channel.InputRmsLevel, voxScale);

        // Mute/Solo
        float toggleY = y + height - ToggleSize - 2f;
        var muteRect = new SKRect(x + 4f, toggleY, x + 4f + ToggleSize, toggleY + ToggleSize);
        DrawToggleButton(canvas, muteRect, "M", channel.IsMuted, _mutePaint);
        _toggleRects.Add(new ToggleRect(channelIndex, ToggleType.Mute, muteRect));

        var soloRect = new SKRect(x + 6f + ToggleSize, toggleY, x + 6f + ToggleSize * 2, toggleY + ToggleSize);
        DrawToggleButton(canvas, soloRect, "S", channel.IsSoloed, _soloPaint);
        _toggleRects.Add(new ToggleRect(channelIndex, ToggleType.Solo, soloRect));
    }

    private void DrawPluginChain(SKCanvas canvas, float x, float y, float width, float height, ChannelStripViewModel channel, int channelIndex, MainUiState uiState, bool voxScale)
    {
        var rect = new SKRect(x, y, x + width, y + height);
        var roundRect = new SKRoundRect(rect, 4f);
        canvas.DrawRoundRect(roundRect, CreateFillPaint(_theme.ChannelPlugins));
        canvas.DrawRoundRect(roundRect, _borderPaint);

        float slotX = x + 4f;
        float slotY = y + 4f;
        float slotHeight = height - 8f;

        for (int i = 0; i < channel.PluginSlots.Count; i++)
        {
            var slot = channel.PluginSlots[i];
            float slotWidth = slot.IsEmpty ? (PluginSlotWidth - MiniMeterWidth - 2f) * 0.6f : PluginSlotWidth - MiniMeterWidth - 2f;
            DrawPluginSlot(canvas, slotX, slotY, slotWidth, slotHeight, slot, channelIndex, i, uiState);

            // Mini meter after plugin
            float miniMeterX = slotX + slotWidth;
            float meterLevel = slot.IsEmpty ? 0f : slot.OutputRmsLevel;
            DrawMiniMeter(canvas, miniMeterX, slotY + 2f, MiniMeterWidth, slotHeight - 4f, meterLevel, voxScale);

            slotX += slotWidth + MiniMeterWidth + PluginSlotSpacing;
        }
    }

    private void DrawPluginSlot(SKCanvas canvas, float x, float y, float width, float height, PluginViewModel slot, int channelIndex, int slotIndex, MainUiState uiState)
    {
        var rect = new SKRect(x, y, x + width, y + height);
        var roundRect = new SKRoundRect(rect, 3f);

        SKPaint bgPaint = slot.IsEmpty ? _pluginSlotEmptyPaint :
                          slot.IsBypassed ? _pluginSlotBypassedPaint : _pluginSlotFilledPaint;
        canvas.DrawRoundRect(roundRect, bgPaint);
        canvas.DrawRoundRect(roundRect, _borderPaint);

        if (slot.IsEmpty)
        {
            // Slot number in corner
            canvas.DrawText($"{slotIndex + 1}", x + 3f, y + 10f, _tinyTextPaint);

            float centerX = x + width / 2f;
            float centerY = y + height / 2f;
            canvas.DrawLine(centerX - 6f, centerY, centerX + 6f, centerY, _iconPaint);
            canvas.DrawLine(centerX, centerY - 6f, centerX, centerY + 6f, _iconPaint);
        }
        else
        {
            // Top row: [BYP] ... Plugin Name ... [X]
            float topRowY = y + 2f;
            float topRowH = 12f;

            // Bypass button - left side
            float bypassW = 20f;
            float bypassX = x + 3f;
            var bypassRect = new SKRect(bypassX, topRowY, bypassX + bypassW, topRowY + topRowH);
            var bypassColor = slot.IsBypassed ? _theme.Bypass : _theme.Surface;
            canvas.DrawRoundRect(new SKRoundRect(bypassRect, 2f), CreateFillPaint(bypassColor));
            var bypassTextPaint = slot.IsBypassed
                ? CreateCenteredTextPaint(new SKColor(0x12, 0x12, 0x14), 7f, SKFontStyle.Bold)
                : CreateCenteredTextPaint(_theme.TextMuted, 7f);
            canvas.DrawText("BYP", bypassRect.MidX, bypassRect.MidY + 2.5f, bypassTextPaint);

            // Remove button (X) - right side
            float removeSize = 8f;
            float removeX = x + width - removeSize - 4f;
            float removeY = topRowY + (topRowH - removeSize) / 2f;
            var removeIconPaint = CreateStrokePaint(_theme.TextMuted, 1.2f);
            canvas.DrawLine(removeX, removeY, removeX + removeSize, removeY + removeSize, removeIconPaint);
            canvas.DrawLine(removeX + removeSize, removeY, removeX, removeY + removeSize, removeIconPaint);

            // Plugin name - centered between bypass and X
            string displayText = $"{slotIndex + 1}. {slot.DisplayName}";
            var namePaint = slot.IsBypassed
                ? CreateCenteredTextPaint(_theme.TextMuted, 8f)
                : CreateCenteredTextPaint(_theme.TextSecondary, 8f);
            float nameY = topRowY + topRowH - 2f;
            float nameLeftEdge = bypassX + bypassW + 4f;
            float nameRightEdge = removeX - 4f;
            float maxNameWidth = nameRightEdge - nameLeftEdge;
            float nameCenterX = nameLeftEdge + maxNameWidth / 2f;

            // Truncate if needed
            if (namePaint.MeasureText(displayText) > maxNameWidth)
            {
                int len = displayText.Length;
                while (len > 0 && namePaint.MeasureText(displayText[..len] + "..") > maxNameWidth)
                    len--;
                displayText = len > 0 ? displayText[..len] + ".." : "..";
            }
            canvas.DrawText(displayText, nameCenterX, nameY, namePaint);

            // Draw 2 parameter knobs side by side horizontally (larger, moved down)
            float largerKnobSize = PluginKnobSize + 4f; // 28px instead of 24px
            if (slot.ElevatedParams is { Length: > 0 } elevParams)
            {
                float knobRadius = largerKnobSize / 2f - 2f;
                float knobY = y + 20f; // Below title row
                float knobSpacing = 14f;
                float totalKnobWidth = (largerKnobSize * 2) + knobSpacing;
                float knobStartX = x + (width - totalKnobWidth) / 2f;

                // Knob 0 (left)
                if (elevParams.Length > 0)
                {
                    float knob0X = knobStartX + largerKnobSize / 2f;
                    var def0 = elevParams[0];
                    float norm0 = (slot.Param0Value - def0.Min) / (def0.Max - def0.Min);
                    norm0 = Math.Clamp(norm0, 0f, 1f);
                    DrawPluginKnob(canvas, knob0X, knobY + knobRadius, knobRadius, norm0, def0.Name, slot.IsBypassed);
                    var knobRect0 = new SKRect(knob0X - knobRadius - 2f, knobY, knob0X + knobRadius + 2f, knobY + largerKnobSize);
                    _pluginKnobRects.Add(new PluginKnobRect(channelIndex, slotIndex, 0, knobRect0, def0.Min, def0.Max));
                }

                // Knob 1 (right)
                if (elevParams.Length > 1)
                {
                    float knob1X = knobStartX + largerKnobSize + knobSpacing + largerKnobSize / 2f;
                    var def1 = elevParams[1];
                    float norm1 = (slot.Param1Value - def1.Min) / (def1.Max - def1.Min);
                    norm1 = Math.Clamp(norm1, 0f, 1f);
                    DrawPluginKnob(canvas, knob1X, knobY + knobRadius, knobRadius, norm1, def1.Name, slot.IsBypassed);
                    var knobRect1 = new SKRect(knob1X - knobRadius - 2f, knobY, knob1X + knobRadius + 2f, knobY + largerKnobSize);
                    _pluginKnobRects.Add(new PluginKnobRect(channelIndex, slotIndex, 1, knobRect1, def1.Min, def1.Max));
                }
            }

            // F/V mode toggle - small indicator above delta strip on the left
            float deltaY = y + height - DeltaStripHeight - 2f;
            string modeChar = slot.DeltaDisplayMode == DeltaDisplayMode.VocalRange ? "V" : "F";
            var modePaint = CreateCenteredTextPaint(_theme.TextMuted, 7f);
            float modeX = x + 8f;
            float modeY = deltaY - 3f;
            canvas.DrawText(modeChar, modeX, modeY, modePaint);

            // Delta strip at bottom
            float deltaWidth = width - 4f;
            DrawDeltaStrip(canvas, x + 2f, deltaY, deltaWidth, DeltaStripHeight, slot.SpectralDelta, slot.DeltaDisplayMode, slot.IsBypassed);
        }

        // Hit rects - bypass on left of title row, X on right of title row
        var bypassHitRect = slot.IsEmpty ? SKRect.Empty : new SKRect(x + 1f, y + 1f, x + 26f, y + 16f);
        var removeHitRect = slot.IsEmpty ? SKRect.Empty : new SKRect(x + width - 16f, y + 1f, x + width - 1f, y + 16f);
        var deltaHitRect = slot.IsEmpty ? SKRect.Empty : new SKRect(x + 2f, y + height - DeltaStripHeight - 2f, x + width - 2f, y + height - 2f);
        _pluginSlots.Add(new PluginSlotRect(channelIndex, slotIndex, rect, bypassHitRect, removeHitRect, deltaHitRect));
    }

    private void DrawPluginKnob(SKCanvas canvas, float cx, float cy, float radius, float normalizedValue, string label, bool dimmed)
    {
        // Background circle
        var bgColor = dimmed ? _theme.Surface.WithAlpha(100) : _theme.Surface;
        canvas.DrawCircle(cx, cy, radius, CreateFillPaint(bgColor));
        canvas.DrawCircle(cx, cy, radius, _borderPaint);

        // Arc showing value
        float startAngle = 135f;
        float sweepAngle = 270f * normalizedValue;
        using var arc = new SKPath();
        arc.AddArc(new SKRect(cx - radius + 2f, cy - radius + 2f, cx + radius - 2f, cy + radius - 2f), startAngle, sweepAngle);
        var arcColor = dimmed ? _theme.Accent.WithAlpha(100) : _theme.Accent;
        canvas.DrawPath(arc, CreateStrokePaint(arcColor, 2f));

        // Pointer line
        float angle = (startAngle + sweepAngle) * MathF.PI / 180f;
        float innerR = radius * 0.3f;
        float outerR = radius * 0.7f;
        var pointerColor = dimmed ? _theme.TextPrimary.WithAlpha(100) : _theme.TextPrimary;
        canvas.DrawLine(
            cx + MathF.Cos(angle) * innerR, cy + MathF.Sin(angle) * innerR,
            cx + MathF.Cos(angle) * outerR, cy + MathF.Sin(angle) * outerR,
            CreateStrokePaint(pointerColor, 1f));

        // Label below knob
        var labelColor = dimmed ? _theme.TextMuted.WithAlpha(100) : _theme.TextMuted;
        var labelPaint = CreateCenteredTextPaint(labelColor, 6f);
        canvas.DrawText(label, cx, cy + radius + 7f, labelPaint);
    }

    private void DrawDeltaStrip(SKCanvas canvas, float x, float y, float width, float height,
                                float[]? deltas, DeltaDisplayMode mode, bool bypassed)
    {
        // Background
        var bgColor = bypassed ? _theme.DeltaNeutral.WithAlpha(100) : _theme.DeltaNeutral;
        canvas.DrawRect(new SKRect(x, y, x + width, y + height), CreateFillPaint(bgColor));

        if (deltas is null || bypassed) return;

        const int numBands = 32;
        float bandWidth = width / numBands;
        float centerY = y + height / 2f;
        float maxBarHeight = (height - 2f) / 2f;

        // Center line
        canvas.DrawLine(x, centerY, x + width, centerY, CreateStrokePaint(_theme.DeltaCenterLine, 0.5f));

        for (int i = 0; i < numBands && i < deltas.Length; i++)
        {
            float delta = deltas[i];
            if (MathF.Abs(delta) < 0.5f) continue; // Skip insignificant changes

            // Scale: ±12dB maps to full bar height
            float barHeight = MathF.Min(MathF.Abs(delta) / 12f, 1f) * maxBarHeight;
            float barX = x + i * bandWidth + 0.5f;
            float barW = bandWidth - 1f;

            SKColor color;
            float barY;

            if (delta > 0) // Boost
            {
                color = _theme.DeltaBoost;
                barY = centerY - barHeight;
            }
            else // Cut
            {
                color = _theme.DeltaCut;
                barY = centerY;
            }

            canvas.DrawRect(new SKRect(barX, barY, barX + barW, barY + barHeight), CreateFillPaint(color));
        }
    }

    private void DrawOutputSection(SKCanvas canvas, float x, float y, float width, float height, ChannelStripViewModel channel, int channelIndex, bool voxScale)
    {
        var rect = new SKRect(x, y, x + width, y + height);
        var roundRect = new SKRoundRect(rect, 4f);
        canvas.DrawRoundRect(roundRect, CreateFillPaint(_theme.ChannelOutput));
        canvas.DrawRoundRect(roundRect, _borderPaint);

        canvas.DrawText("OUT", x + 4f, y + 10f, _smallTextPaint);

        float knobX = x + 4f;
        float knobY = y + 14f;
        DrawMiniKnob(canvas, knobX, knobY, KnobSize, channel.OutputGainDb);
        _knobRects.Add(new KnobRect(channelIndex, KnobType.OutputGain, new SKRect(knobX, knobY, knobX + KnobSize, knobY + KnobSize)));

        float meterX = x + width - MeterWidth - 4f;
        float meterY = y + 10f;
        DrawVerticalMeter(canvas, meterX, meterY, MeterWidth, height - 24f, channel.OutputPeakLevel, channel.OutputRmsLevel, voxScale);

        string dbText = $"{LinearToDb(channel.OutputPeakLevel):0.0}";
        canvas.DrawText(dbText, x + 4f, y + height - 4f, _tinyTextPaint);
    }

    private void DrawMasterSection(SKCanvas canvas, float x, float y, float width, float height, MainViewModel viewModel)
    {
        var rect = new SKRect(x, y, x + width, y + height);
        var roundRect = new SKRoundRect(rect, 6f);
        canvas.DrawRoundRect(roundRect, CreateFillPaint(_theme.MasterSection));
        canvas.DrawRoundRect(roundRect, _borderPaint);

        canvas.DrawText("MASTER", x + 6f, y + 12f, _smallTextPaint);
        bool lufsMode = viewModel.MasterMeterLufs;
        float? voxTargetDb = viewModel.MeterScaleVox
            ? (lufsMode ? -16f : -18f)
            : null;
        string modeLabel = lufsMode ? "LUFS" : "dB";
        if (voxTargetDb.HasValue)
        {
            modeLabel = $"{modeLabel} {voxTargetDb.Value:0}";
        }
        float modeWidth = _tinyTextPaint.MeasureText(modeLabel);
        canvas.DrawText(modeLabel, x + width - modeWidth - 6f, y + 12f, _tinyTextPaint);

        // Meters on the left side
        float meterY = y + 18f;
        float meterHeight = height - 44f;
        float leftMeterX = x + 6f;
        float rightMeterX = leftMeterX + MeterWidth + 4f;

        canvas.DrawText("L", leftMeterX + 4f, meterY - 2f, _tinyTextPaint);
        canvas.DrawText("R", rightMeterX + 4f, meterY - 2f, _tinyTextPaint);

        float leftPeak, rightPeak, leftRms, rightRms;
        float leftReadout, rightReadout;

        if (lufsMode)
        {
            // LUFS mode: momentary drives the bar, short-term drives the peak marker.
            const float minLufs = -70f;
            float leftMomentary = viewModel.MasterLufsMomentaryLeft;
            float rightMomentary = viewModel.MasterLufsMomentaryRight;
            float leftShortTerm = viewModel.MasterLufsShortTermLeft;
            float rightShortTerm = viewModel.MasterLufsShortTermRight;

            if (!viewModel.MasterIsStereo)
            {
                float momentary = (leftMomentary + rightMomentary) * 0.5f;
                float shortTerm = (leftShortTerm + rightShortTerm) * 0.5f;
                leftMomentary = rightMomentary = momentary;
                leftShortTerm = rightShortTerm = shortTerm;
            }

            if (viewModel.MasterMuted)
            {
                leftMomentary = rightMomentary = minLufs;
                leftShortTerm = rightShortTerm = minLufs;
            }

            float leftPeakLufs = MathF.Max(leftMomentary, leftShortTerm);
            float rightPeakLufs = MathF.Max(rightMomentary, rightShortTerm);

            leftRms = LufsToLinear(leftShortTerm);
            rightRms = LufsToLinear(rightShortTerm);
            leftPeak = LufsToLinear(leftPeakLufs);
            rightPeak = LufsToLinear(rightPeakLufs);
            leftReadout = leftShortTerm;
            rightReadout = rightShortTerm;
        }
        else
        {
            if (viewModel.MasterIsStereo)
            {
                leftPeak = viewModel.Channel1.OutputPeakLevel;
                rightPeak = viewModel.Channel2.OutputPeakLevel;
                leftRms = viewModel.Channel1.OutputRmsLevel;
                rightRms = viewModel.Channel2.OutputRmsLevel;
            }
            else
            {
                float sumPeak = MathF.Max(viewModel.Channel1.OutputPeakLevel, viewModel.Channel2.OutputPeakLevel);
                float sumRms = (viewModel.Channel1.OutputRmsLevel + viewModel.Channel2.OutputRmsLevel) / 2f;
                leftPeak = rightPeak = sumPeak;
                leftRms = rightRms = sumRms;
            }

            // If master muted, show zero levels on meters
            if (viewModel.MasterMuted)
            {
                leftPeak = rightPeak = leftRms = rightRms = 0f;
            }

            leftReadout = LinearToDb(leftPeak);
            rightReadout = LinearToDb(rightPeak);
        }

        _masterMeterRect = new SKRect(leftMeterX, meterY, rightMeterX + MeterWidth, meterY + meterHeight);

        DrawVerticalMeter(canvas, leftMeterX, meterY, MeterWidth, meterHeight, leftPeak, leftRms, viewModel.MeterScaleVox, voxTargetDb);
        DrawVerticalMeter(canvas, rightMeterX, meterY, MeterWidth, meterHeight, rightPeak, rightRms, viewModel.MeterScaleVox, voxTargetDb);

        // Toggles on the right side (stacked: ST, M)
        float toggleX = x + width - ToggleSize - 6f;
        float toggleSpacing = ToggleSize + 4f;

        // Stereo toggle
        float stereoY = y + 18f;
        var stereoRect = new SKRect(toggleX, stereoY, toggleX + ToggleSize, stereoY + ToggleSize);
        DrawToggleButton(canvas, stereoRect, "ST", viewModel.MasterIsStereo, _accentPaint);
        _toggleRects.Add(new ToggleRect(-1, ToggleType.MasterStereo, stereoRect));

        // Mute toggle
        float muteY = stereoY + toggleSpacing;
        var muteRect = new SKRect(toggleX, muteY, toggleX + ToggleSize, muteY + ToggleSize);
        DrawToggleButton(canvas, muteRect, "M", viewModel.MasterMuted, _mutePaint);
        _toggleRects.Add(new ToggleRect(-1, ToggleType.MasterMute, muteRect));

        // Readings at bottom
        float dbY = y + height - 8f;
        string leftLabel = $"{leftReadout:0.0}";
        string rightLabel = $"{rightReadout:0.0}";
        canvas.DrawText(leftLabel, leftMeterX, dbY, _tinyTextPaint);
        canvas.DrawText(rightLabel, rightMeterX, dbY, _tinyTextPaint);
    }

    private void DrawDebugOverlay(SKCanvas canvas, SKSize size, MainViewModel viewModel)
    {
        var diag = viewModel.Diagnostics;

        // Semi-transparent overlay panel
        float overlayX = Padding;
        float overlayY = TitleBarHeight + HotbarHeight + Padding;
        float overlayWidth = size.Width - Padding * 2 - MasterWidth - Padding;
        float overlayHeight = ChannelStripHeight * 2 + ChannelSpacing;

        var overlayRect = new SKRect(overlayX, overlayY, overlayX + overlayWidth, overlayY + overlayHeight);
        var overlayRound = new SKRoundRect(overlayRect, 6f);

        // Dark semi-transparent background
        using var overlayBg = new SKPaint { Color = new SKColor(0x00, 0x00, 0x00, 0xE8), IsAntialias = true };
        canvas.DrawRoundRect(overlayRound, overlayBg);
        canvas.DrawRoundRect(overlayRound, _borderPaint);

        float lineHeight = 14f;
        float col1X = overlayX + 12f;
        float col2X = overlayX + overlayWidth / 2f;
        float textY = overlayY + 18f;

        // Title
        canvas.DrawText("AUDIO ENGINE DIAGNOSTICS", col1X, textY, _textPaint);
        textY += lineHeight + 4f;

        // Status indicators
        string outputStatus = diag.OutputActive ? "ACTIVE" : "INACTIVE";
        string input1Status = diag.Input1Active ? "ACTIVE" : "INACTIVE";
        string input2Status = diag.Input2Active ? "ACTIVE" : "INACTIVE";
        string monitorStatus = diag.MonitorActive ? "ACTIVE" : "INACTIVE";

        var activeColor = CreateTextPaint(new SKColor(0x00, 0xFF, 0x00), 9f);
        var inactiveColor = CreateTextPaint(new SKColor(0xFF, 0x66, 0x66), 9f);

        canvas.DrawText("Output:", col1X, textY, _smallTextPaint);
        canvas.DrawText(outputStatus, col1X + 50f, textY, diag.OutputActive ? activeColor : inactiveColor);
        canvas.DrawText("Input 1:", col2X, textY, _smallTextPaint);
        canvas.DrawText(input1Status, col2X + 50f, textY, diag.Input1Active ? activeColor : inactiveColor);
        textY += lineHeight;

        canvas.DrawText("Monitor:", col1X, textY, _smallTextPaint);
        canvas.DrawText(monitorStatus, col1X + 50f, textY, diag.MonitorActive ? activeColor : inactiveColor);
        canvas.DrawText("Input 2:", col2X, textY, _smallTextPaint);
        canvas.DrawText(input2Status, col2X + 50f, textY, diag.Input2Active ? activeColor : inactiveColor);
        textY += lineHeight + 6f;

        // Buffer stats
        canvas.DrawText("BUFFERS", col1X, textY, _textPaint);
        textY += lineHeight;

        float buf1Pct = diag.Input1BufferCapacity > 0 ? 100f * diag.Input1BufferedSamples / diag.Input1BufferCapacity : 0;
        float buf2Pct = diag.Input2BufferCapacity > 0 ? 100f * diag.Input2BufferedSamples / diag.Input2BufferCapacity : 0;

        canvas.DrawText($"Input 1: {diag.Input1BufferedSamples}/{diag.Input1BufferCapacity} ({buf1Pct:0}%)", col1X, textY, _smallTextPaint);
        canvas.DrawText($"Input 2: {diag.Input2BufferedSamples}/{diag.Input2BufferCapacity} ({buf2Pct:0}%)", col2X, textY, _smallTextPaint);
        textY += lineHeight;

        canvas.DrawText($"Input 1 Ch: {diag.Input1Channels} @ {diag.Input1SampleRate}Hz", col1X, textY, _smallTextPaint);
        canvas.DrawText($"Input 2 Ch: {diag.Input2Channels} @ {diag.Input2SampleRate}Hz", col2X, textY, _smallTextPaint);
        textY += lineHeight + 6f;

        // Drop/underflow stats (30 seconds and all-time)
        canvas.DrawText("DROPS & UNDERFLOWS (30s / all-time)", col1X, textY, _textPaint);
        textY += lineHeight;

        var dropColor = CreateTextPaint(_theme.MeterClip, 9f);
        var okColor = _smallTextPaint;

        canvas.DrawText($"Input 1 Dropped: {viewModel.Input1Drops30Sec} ({diag.Input1DroppedSamples})", col1X, textY, viewModel.Input1Drops30Sec > 0 ? dropColor : okColor);
        canvas.DrawText($"Input 2 Dropped: {viewModel.Input2Drops30Sec} ({diag.Input2DroppedSamples})", col2X, textY, viewModel.Input2Drops30Sec > 0 ? dropColor : okColor);
        textY += lineHeight;

        canvas.DrawText($"Output Underflow 1: {viewModel.Underflow1Drops30Sec} ({diag.OutputUnderflowSamples1})", col1X, textY, viewModel.Underflow1Drops30Sec > 0 ? dropColor : okColor);
        canvas.DrawText($"Output Underflow 2: {viewModel.Underflow2Drops30Sec} ({diag.OutputUnderflowSamples2})", col2X, textY, viewModel.Underflow2Drops30Sec > 0 ? dropColor : okColor);
        textY += lineHeight + 6f;

        // Callback stats
        canvas.DrawText("CALLBACKS", col1X, textY, _textPaint);
        textY += lineHeight;

        canvas.DrawText($"Output: {diag.OutputCallbackCount} ({diag.LastOutputFrames} frames)", col1X, textY, _smallTextPaint);
        canvas.DrawText($"Input 1: {diag.Input1CallbackCount} ({diag.LastInput1Frames} frames)", col2X, textY, _smallTextPaint);
        textY += lineHeight;

        canvas.DrawText($"Input 2: {diag.Input2CallbackCount} ({diag.LastInput2Frames} frames)", col1X, textY, _smallTextPaint);

        if (diag.IsRecovering)
        {
            canvas.DrawText("RECOVERING...", col2X, textY, CreateTextPaint(new SKColor(0xFF, 0xFF, 0x00), 9f, SKFontStyle.Bold));
        }
    }

    private void DrawVerticalMeter(SKCanvas canvas, float x, float y, float width, float height, float peakLevel, float rmsLevel, bool voxScale, float? voxTargetDb = null)
    {
        canvas.DrawRect(new SKRect(x, y, x + width, y + height), _meterBackgroundPaint);

        float rmsDb = LinearToDb(rmsLevel);
        float peakDb = LinearToDb(peakLevel);

        float rmsPos, peakPos;
        if (voxScale)
        {
            rmsPos = DbToVoxMeterPosition(rmsDb);
            peakPos = DbToVoxMeterPosition(peakDb);
        }
        else
        {
            // Linear mode: -60dB to 0dB mapped to 0-1
            rmsPos = Math.Clamp((rmsDb + 60f) / 60f, 0f, 1f);
            peakPos = Math.Clamp((peakDb + 60f) / 60f, 0f, 1f);
        }

        float segmentHeight = (height - (MeterSegments - 1) * SegmentGap) / MeterSegments;

        for (int i = 0; i < MeterSegments; i++)
        {
            float segY = y + height - (i + 1) * (segmentHeight + SegmentGap);
            float threshold = (i + 0.5f) / MeterSegments;
            bool lit = rmsPos >= threshold;

            var segRect = new SKRect(x + 1f, segY, x + width - 1f, segY + segmentHeight);

            if (lit)
            {
                // For VOX mode, color based on dB; for linear, color based on position
                float segDb = voxScale ? VoxMeterPositionLinearToDb(threshold) : -60f + threshold * 60f;
                SKColor color = voxScale ? GetVoxMeterColor(segDb) : GetMeterSegmentColor(threshold);
                canvas.DrawRect(segRect, CreateFillPaint(color));
            }
            else
            {
                canvas.DrawRect(segRect, _meterSegmentOffPaint);
            }
        }

        // Peak indicator
        float peakY = y + height - height * peakPos;
        if (peakPos > 0.01f)
        {
            var peakColor = peakDb > -6f ? _theme.MeterClip : _theme.TextPrimary;
            canvas.DrawLine(x, peakY, x + width, peakY, CreateStrokePaint(peakColor, 1.5f));
        }

        // VOX mode: draw target reference line
        if (voxScale)
        {
            float targetDb = voxTargetDb ?? -18f;
            float targetPos = DbToVoxMeterPosition(targetDb);
            float targetY = y + height - height * targetPos;
            canvas.DrawLine(x, targetY, x + width, targetY, CreateStrokePaint(_theme.Accent, 1f));
        }
    }

    private void DrawMiniMeter(SKCanvas canvas, float x, float y, float width, float height, float level, bool voxScale)
    {
        canvas.DrawRect(new SKRect(x, y, x + width, y + height), _meterBackgroundPaint);

        float db = LinearToDb(level);
        float pos = voxScale ? DbToVoxMeterPosition(db) : Math.Clamp((db + 60f) / 60f, 0f, 1f);

        if (pos > 0.01f)
        {
            float fillHeight = height * pos;
            var fillRect = new SKRect(x + 1f, y + height - fillHeight, x + width - 1f, y + height);

            SKColor color = voxScale ? GetVoxMeterColor(db) : GetMeterSegmentColor(pos);
            canvas.DrawRect(fillRect, CreateFillPaint(color));
        }

        // VOX mode: draw -18dBFS target tick
        if (voxScale)
        {
            float targetPos = DbToVoxMeterPosition(-18f);
            float targetY = y + height - height * targetPos;
            canvas.DrawLine(x, targetY, x + width, targetY, CreateStrokePaint(_theme.Accent, 1f));
        }
    }

    /// <summary>
    /// Convert linear level (0-1) to dB.
    /// </summary>
    private static float LinearToDb(float linear) => linear <= 0f ? -60f : 20f * MathF.Log10(linear + 1e-10f);

    /// <summary>
    /// Convert LUFS (dB) value to linear magnitude for meter positioning.
    /// </summary>
    private static float LufsToLinear(float lufs)
    {
        if (!float.IsFinite(lufs))
        {
            return 0f;
        }

        return MathF.Pow(10f, lufs / 20f);
    }

    /// <summary>
    /// Voice-optimized dB to meter position conversion.
    /// Expands the -30 to -12 dBFS range (where speech lives) to use 60% of the meter.
    /// </summary>
    private static float DbToVoxMeterPosition(float db)
    {
        db = Math.Clamp(db, -40f, 0f);

        if (db < -30f)
        {
            // -40 to -30 dB → 0% to 15% (silence/noise floor, compressed)
            return (db + 40f) / 10f * 0.15f;
        }
        else if (db < -12f)
        {
            // -30 to -12 dB → 15% to 75% (speech range, expanded)
            return 0.15f + (db + 30f) / 18f * 0.60f;
        }
        else
        {
            // -12 to 0 dB → 75% to 100% (loud/clipping, compressed)
            return 0.75f + (db + 12f) / 12f * 0.25f;
        }
    }

    /// <summary>
    /// Inverse of DbToVoxMeterPosition - convert meter position back to dB.
    /// </summary>
    private static float VoxMeterPositionLinearToDb(float pos)
    {
        pos = Math.Clamp(pos, 0f, 1f);

        if (pos < 0.15f)
        {
            // 0% to 15% → -40 to -30 dB
            return -40f + pos / 0.15f * 10f;
        }
        else if (pos < 0.75f)
        {
            // 15% to 75% → -30 to -12 dB
            return -30f + (pos - 0.15f) / 0.60f * 18f;
        }
        else
        {
            // 75% to 100% → -12 to 0 dB
            return -12f + (pos - 0.75f) / 0.25f * 12f;
        }
    }

    /// <summary>
    /// VOX mode color based on dB level.
    /// </summary>
    private static SKColor GetVoxMeterColor(float db)
    {
        if (db > -6f)  return new SKColor(0xFF, 0x00, 0x00); // Red - clipping
        if (db > -12f) return new SKColor(0xFF, 0xFF, 0x00); // Yellow - loud
        if (db > -30f) return new SKColor(0x00, 0xFF, 0x00); // Green - good speech range
        return new SKColor(0x66, 0x66, 0x66);                // Gray - too quiet
    }

    /// <summary>
    /// Linear mode color based on meter position (0-1).
    /// </summary>
    private SKColor GetMeterSegmentColor(float level)
    {
        if (level >= 0.95f) return _theme.MeterClip;
        if (level >= 0.85f) return _theme.MeterWarn;
        if (level >= 0.65f) return _theme.MeterHigh;
        if (level >= 0.35f) return _theme.MeterMid;
        return _theme.MeterLow;
    }

    private void DrawMiniKnob(SKCanvas canvas, float x, float y, float size, float value)
    {
        var center = new SKPoint(x + size / 2f, y + size / 2f - 2f);
        float radius = size / 2f - 3f;

        canvas.DrawCircle(center, radius, CreateFillPaint(_theme.Surface));
        canvas.DrawCircle(center, radius, _borderPaint);

        float normalized = (value + 60f) / 72f;
        normalized = Math.Clamp(normalized, 0f, 1f);
        float startAngle = 135f;
        float sweepAngle = 270f * normalized;

        using var arc = new SKPath();
        arc.AddArc(new SKRect(center.X - radius, center.Y - radius, center.X + radius, center.Y + radius), startAngle, sweepAngle);
        canvas.DrawPath(arc, CreateStrokePaint(_theme.Accent, 2f));

        float angle = (startAngle + sweepAngle) * MathF.PI / 180f;
        float innerR = radius * 0.4f;
        float outerR = radius * 0.8f;
        canvas.DrawLine(
            center.X + MathF.Cos(angle) * innerR, center.Y + MathF.Sin(angle) * innerR,
            center.X + MathF.Cos(angle) * outerR, center.Y + MathF.Sin(angle) * outerR,
            CreateStrokePaint(_theme.TextPrimary, 1.5f));

        string valueText = value.ToString("0", CultureInfo.InvariantCulture);
        canvas.DrawText(valueText, x + 2f, y + size - 1f, _tinyTextPaint);
    }

    private void DrawToggleButton(SKCanvas canvas, SKRect rect, string label, bool isActive, SKPaint activePaint)
    {
        var roundRect = new SKRoundRect(rect, 3f);
        canvas.DrawRoundRect(roundRect, isActive ? activePaint : _buttonPaint);
        canvas.DrawRoundRect(roundRect, _borderPaint);

        var textPaint = isActive
            ? CreateCenteredTextPaint(new SKColor(0x12, 0x12, 0x14), 9f, SKFontStyle.Bold)
            : CreateCenteredTextPaint(_theme.TextSecondary, 9f);
        canvas.DrawText(label, rect.MidX, rect.MidY + 3f, textPaint);
    }

    private void DrawMinimal(SKCanvas canvas, SKSize size, MainViewModel viewModel)
    {
        float y = TitleBarHeight + Padding;
        float width = size.Width - Padding * 2f;
        float rowHeight = 40f;

        DrawMinimalChannelRow(canvas, Padding, y, width, rowHeight, viewModel.Channel1);
        y += rowHeight + 4f;
        DrawMinimalChannelRow(canvas, Padding, y, width, rowHeight, viewModel.Channel2);
    }

    private void DrawMinimalChannelRow(SKCanvas canvas, float x, float y, float width, float height, ChannelStripViewModel channel)
    {
        var rect = new SKRect(x, y, x + width, y + height);
        var roundRect = new SKRoundRect(rect, 4f);
        canvas.DrawRoundRect(roundRect, _sectionPaint);
        canvas.DrawRoundRect(roundRect, _borderPaint);

        canvas.DrawText(channel.Name, x + 8f, y + height / 2f + 3f, _textPaint);

        float meterX = x + 80f;
        float meterWidth = width - 140f;
        float meterHeight = 12f;
        float meterY = y + (height - meterHeight) / 2f;
        DrawHorizontalMeter(canvas, meterX, meterY, meterWidth, meterHeight, channel.OutputPeakLevel, channel.OutputRmsLevel);

        string dbText = $"{LinearToDb(channel.OutputPeakLevel):0.0} dB";
        canvas.DrawText(dbText, x + width - 50f, y + height / 2f + 3f, _textSecondaryPaint);
    }

    private void DrawHorizontalMeter(SKCanvas canvas, float x, float y, float width, float height, float peakLevel, float rmsLevel)
    {
        canvas.DrawRect(new SKRect(x, y, x + width, y + height), _meterBackgroundPaint);

        float rms = Math.Clamp(rmsLevel, 0f, 1f);
        float peak = Math.Clamp(peakLevel, 0f, 1f);

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
            canvas.DrawRect(new SKRect(x + 1f, y + 1f, x + 1f + rmsWidth - 2f, y + height - 1f), gradientPaint);
        }

        float peakX = x + width * peak;
        if (peak > 0.01f)
        {
            var peakColor = peak >= 0.95f ? _theme.MeterClip : _theme.TextPrimary;
            canvas.DrawLine(peakX, y, peakX, y + height, CreateStrokePaint(peakColor, 1.5f));
        }
    }

    private void DrawIconButton(SKCanvas canvas, SKRect rect, MainButton button, bool isActive, IconType icon)
    {
        var roundRect = new SKRoundRect(rect, 3f);
        canvas.DrawRoundRect(roundRect, isActive ? _accentPaint : _buttonPaint);

        float cx = rect.MidX;
        float cy = rect.MidY;
        float s = 4f;

        switch (icon)
        {
            case IconType.Close:
                canvas.DrawLine(cx - s, cy - s, cx + s, cy + s, _iconPaint);
                canvas.DrawLine(cx + s, cy - s, cx - s, cy + s, _iconPaint);
                break;
            case IconType.Minimize:
                canvas.DrawLine(cx - s, cy + 2f, cx + s, cy + 2f, _iconPaint);
                break;
            case IconType.Pin:
                canvas.DrawCircle(cx, cy - 1f, 2.5f, _iconPaint);
                canvas.DrawLine(cx, cy + 1f, cx, cy + 4f, _iconPaint);
                break;
            case IconType.Settings:
                canvas.DrawCircle(cx, cy, 2.5f, _iconPaint);
                for (int i = 0; i < 6; i++)
                {
                    float angle = i * 60f * MathF.PI / 180f;
                    canvas.DrawLine(cx + MathF.Cos(angle) * 3f, cy + MathF.Sin(angle) * 3f,
                                   cx + MathF.Cos(angle) * 5f, cy + MathF.Sin(angle) * 5f, _iconPaint);
                }
                break;
        }

        _topButtonRects[button] = rect;
    }

    private void DrawTextButton(SKCanvas canvas, SKRect rect, string text, MainButton button)
    {
        var roundRect = new SKRoundRect(rect, 3f);
        canvas.DrawRoundRect(roundRect, _buttonPaint);
        canvas.DrawRoundRect(roundRect, _borderPaint);
        canvas.DrawText(text, rect.MidX, rect.MidY + 3f, CreateCenteredTextPaint(_theme.TextSecondary, 9f));
        _topButtonRects[button] = rect;
    }

    private void DrawEllipsizedText(SKCanvas canvas, string text, float x, float y, float maxWidth, SkiaTextPaint paint)
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

    private void ClearHitTargets()
    {
        _topButtonRects.Clear();
        _knobRects.Clear();
        _pluginKnobRects.Clear();
        _pluginSlots.Clear();
        _toggleRects.Clear();
        _masterMeterRect = SKRect.Empty;
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

    public PluginKnobHit? HitTestPluginKnob(float x, float y)
    {
        foreach (var knob in _pluginKnobRects)
            if (knob.Rect.Contains(x, y)) return new PluginKnobHit(knob.ChannelIndex, knob.SlotIndex, knob.ParamIndex, knob.MinValue, knob.MaxValue);
        return null;
    }

    public PluginSlotHit? HitTestPluginSlot(float x, float y, out PluginSlotRegion region)
    {
        foreach (var slot in _pluginSlots)
        {
            if (!slot.Rect.Contains(x, y)) continue;
            if (slot.BypassRect.Contains(x, y)) { region = PluginSlotRegion.Bypass; return new PluginSlotHit(slot.ChannelIndex, slot.SlotIndex); }
            if (slot.RemoveRect.Contains(x, y)) { region = PluginSlotRegion.Remove; return new PluginSlotHit(slot.ChannelIndex, slot.SlotIndex); }
            if (slot.DeltaStripRect.Contains(x, y)) { region = PluginSlotRegion.DeltaStrip; return new PluginSlotHit(slot.ChannelIndex, slot.SlotIndex); }
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

    public bool HitTestMasterMeter(float x, float y) => _masterMeterRect.Contains(x, y);

    public bool HitTestMeterScaleToggle(float x, float y) => _meterScaleToggleRect.Contains(x, y);

    public bool HitTestQualityToggle(float x, float y) => _qualityToggleRect.Contains(x, y);

    public bool HitTestStatsArea(float x, float y) => _statsAreaRect.Contains(x, y);

    public bool HitTestTitleBar(float x, float y) => _titleBarRect.Contains(x, y);

    public int HitTestPresetDropdown(float x, float y)
    {
        if (_preset1DropdownRect.Contains(x, y)) return 0;
        if (_preset2DropdownRect.Contains(x, y)) return 1;
        return -1;
    }

    public SKRect GetPresetDropdownRect(int channelIndex) => channelIndex == 0 ? _preset1DropdownRect : _preset2DropdownRect;

    // Paint factories
    private static SKPaint CreateFillPaint(SKColor color) => new() { Color = color, IsAntialias = true, Style = SKPaintStyle.Fill };
    private static SKPaint CreateStrokePaint(SKColor color, float width) => new() { Color = color, IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = width };
    private static SkiaTextPaint CreateTextPaint(SKColor color, float size, SKFontStyle? style = null) =>
        new(color, size, style, SKTextAlign.Left);

    private static SkiaTextPaint CreateCenteredTextPaint(SKColor color, float size, SKFontStyle? style = null) =>
        new(color, size, style, SKTextAlign.Center);

    // Internal records
    private sealed record KnobRect(int ChannelIndex, KnobType KnobType, SKRect Rect);
    private sealed record PluginKnobRect(int ChannelIndex, int SlotIndex, int ParamIndex, SKRect Rect, float MinValue, float MaxValue);
    private sealed record PluginSlotRect(int ChannelIndex, int SlotIndex, SKRect Rect, SKRect BypassRect, SKRect RemoveRect, SKRect DeltaStripRect);
    private sealed record ToggleRect(int ChannelIndex, ToggleType ToggleType, SKRect Rect);
    private enum IconType { Close, Minimize, Pin, Settings }
}

public readonly record struct KnobHit(int ChannelIndex, KnobType KnobType);
public readonly record struct PluginKnobHit(int ChannelIndex, int SlotIndex, int ParamIndex, float MinValue, float MaxValue);
public readonly record struct PluginSlotHit(int ChannelIndex, int SlotIndex);
public enum PluginSlotRegion { None, Action, Bypass, Remove, Knob, DeltaStrip }
public readonly record struct ToggleHit(int ChannelIndex, ToggleType ToggleType);
