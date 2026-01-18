using System;
using System.Collections.Generic;
using System.Globalization;
using HotMic.App.ViewModels;
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
    private const float AddChannelHeight = 26f;
    private const float AddChannelSpacing = 0f;
    private const float ChannelDeleteSize = 14f;

    // Section dimensions
    private const float ChannelHeaderWidth = 60f;
    private const float PluginSlotWidth = 130f; // 32 bands × 4px = 128px + 2px padding for delta strip
    private const float PluginSlotSpacing = 2f;
    private const float MeterWidth = 16f;
    private const float MiniMeterWidth = 6f;
    private const float StereoMeterWidth = 36f;
    private const float KnobSize = 36f;
    private const float ToggleSize = 18f;

    // Meter segments
    private const int MeterSegments = 16;
    private const float SegmentGap = 1f;
    private static readonly float[] MeterGradientStops = [0f, 0.35f, 0.65f, 0.85f, 1f];

    private readonly HotMicTheme _theme = HotMicTheme.Default;
    private readonly SKColor[] _meterGradientColors;

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
    private readonly SKPaint _bridgePaint;

    // Hit target storage
    private readonly Dictionary<MainButton, SKRect> _topButtonRects = new();
    private readonly List<KnobRect> _knobRects = new();
    private readonly List<ContainerSlotRect> _containerSlots = new();
    private readonly List<ToggleRect> _toggleRects = new();
    private readonly List<PluginAreaRect> _pluginAreaRects = new();
    private readonly Dictionary<int, int> _containerIndexByPluginId = new();
    private readonly HashSet<int> _drawnContainerIndices = new();
    private readonly PluginShellRenderer _pluginShellRenderer = new();
    private readonly RoutingSlotRenderer _routingSlotRenderer = new();
    private readonly Dictionary<int, SKRect> _channelHeaderRects = new();
    private readonly Dictionary<int, SKRect> _channelNameRects = new();
    private readonly List<CopyBridgeRect> _copyBridgeRects = new();
    private readonly List<MergeBridgeRect> _mergeBridgeRects = new();
    private readonly List<ChannelDeleteRect> _channelDeleteRects = new();
    private SKRect _titleBarRect;
    private SKRect _meterScaleToggleRect;
    private SKRect _qualityToggleRect;
    private SKRect _statsAreaRect;
    private SKRect _presetDropdownRect;
    private SKRect _masterMeterRect;
    private SKRect _visualizerButtonRect;
    private SKRect _addChannelRect;

    public MainRenderer()
    {
        _meterGradientColors =
        [
            _theme.MeterLow,
            _theme.MeterMid,
            _theme.MeterHigh,
            _theme.MeterWarn,
            _theme.MeterClip
        ];
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
        _bridgePaint = CreateStrokePaint(_theme.Accent.WithAlpha(180), 2f);
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
            DrawFull(canvas, size, viewModel);

            if (viewModel.ShowDebugOverlay)
            {
                DrawDebugOverlay(canvas, size, viewModel);
            }
        }

        canvas.Restore();
    }

    public void RenderPluginStrip(SKCanvas canvas, SKRect bounds, IReadOnlyList<PluginViewModel> slots, int channelIndex, bool voxScale)
    {
        ClearHitTargets();

        var roundRect = new SKRoundRect(bounds, 4f);
        canvas.DrawRoundRect(roundRect, CreateFillPaint(_theme.ChannelPlugins));
        canvas.DrawRoundRect(roundRect, _borderPaint);

        float slotX = bounds.Left + 4f;
        float slotY = bounds.Top + 4f;
        float slotHeight = bounds.Height - 8f;

        for (int i = 0; i < slots.Count; i++)
        {
            var slot = slots[i];
            float slotWidth = slot.IsEmpty
                ? (PluginSlotWidth - MiniMeterWidth - 2f) * 0.6f
                : PluginSlotWidth - MiniMeterWidth - 2f;
            _pluginShellRenderer.DrawSlot(canvas, new SKRect(slotX, slotY, slotX + slotWidth, slotY + slotHeight), slot, channelIndex, i);

            float miniMeterX = slotX + slotWidth;
            float meterLevel = slot.IsEmpty ? 0f : slot.OutputRmsLevel;
            DrawMiniMeter(canvas, miniMeterX, slotY + 2f, MiniMeterWidth, slotHeight - 4f, meterLevel, voxScale);

            slotX += slotWidth + MiniMeterWidth + PluginSlotSpacing;
        }
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

        // Preset dropdown (active channel)
        float presetX = _qualityToggleRect.Right + 12f;
        string presetLabel = $"CH {Math.Max(1, viewModel.ActiveChannelIndex + 1)}";
        DrawPresetSelector(canvas, presetX, toggleY, presetLabel, viewModel.ActiveChannelPresetName, MainButton.SavePreset, out _presetDropdownRect, out var saveRect);
        _topButtonRects[MainButton.SavePreset] = saveRect;

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

    private void DrawFull(SKCanvas canvas, SKSize size, MainViewModel viewModel)
    {
        float contentTop = TitleBarHeight + HotbarHeight + Padding;
        float contentLeft = Padding;
        float contentRight = size.Width - Padding;

        float masterSectionWidth = MasterWidth + Padding;
        float channelAreaWidth = contentRight - contentLeft - masterSectionWidth - Padding;

        float channelY = contentTop;
        bool canDelete = viewModel.Channels.Count > 1;
        for (int i = 0; i < viewModel.Channels.Count; i++)
        {
            DrawChannelStrip(canvas, contentLeft, channelY, channelAreaWidth, ChannelStripHeight, viewModel.Channels[i], i, canDelete, viewModel.MeterScaleVox);
            channelY += ChannelStripHeight + ChannelSpacing;
        }

        DrawAddChannelButton(canvas, contentLeft, channelY + AddChannelSpacing, channelAreaWidth, AddChannelHeight);

        DrawCopyBridges(canvas);
        DrawMergeBridges(canvas);

        float masterX = contentRight - masterSectionWidth;
        float masterHeight = ChannelStripHeight * Math.Max(1, viewModel.Channels.Count) +
                             ChannelSpacing * Math.Max(0, viewModel.Channels.Count - 1);
        DrawMasterSection(canvas, masterX, contentTop, masterSectionWidth, masterHeight, viewModel);
    }

    private void DrawChannelStrip(SKCanvas canvas, float x, float y, float width, float height, ChannelStripViewModel channel, int channelIndex, bool canDelete, bool voxScale)
    {
        var stripRect = new SKRect(x, y, x + width, y + height);
        var stripRound = new SKRoundRect(stripRect, 6f);
        canvas.DrawRoundRect(stripRound, _sectionPaint);
        canvas.DrawRoundRect(stripRound, _borderPaint);

        float sectionX = x + 6f;
        float sectionY = y + 6f;
        float sectionHeight = height - 12f;

        _channelHeaderRects[channelIndex] = new SKRect(sectionX, sectionY, sectionX + ChannelHeaderWidth, sectionY + sectionHeight);
        DrawChannelHeader(canvas, sectionX, sectionY, ChannelHeaderWidth, sectionHeight, channel, channelIndex, canDelete);
        sectionX += ChannelHeaderWidth + 4f;

        float pluginAreaWidth = width - ChannelHeaderWidth - 16f;
        DrawPluginChain(canvas, sectionX, sectionY, pluginAreaWidth, sectionHeight, channel, channelIndex, voxScale);
    }

    private void DrawChannelHeader(SKCanvas canvas, float x, float y, float width, float height, ChannelStripViewModel channel, int channelIndex, bool canDelete)
    {
        var rect = new SKRect(x, y, x + width, y + height);
        var roundRect = new SKRoundRect(rect, 4f);
        canvas.DrawRoundRect(roundRect, CreateFillPaint(_theme.ChannelInput));
        canvas.DrawRoundRect(roundRect, _borderPaint);

        // Channel name at top (right-click to rename)
        string displayName = string.IsNullOrWhiteSpace(channel.Name) ? $"CH{channelIndex + 1}" : channel.Name;
        float nameMaxWidth = width - 8f;
        var nameRect = new SKRect(x + 2f, y + 2f, x + width - 2f, y + 18f);
        DrawTruncatedText(canvas, displayName, x + width / 2f, y + 13f, nameMaxWidth, CreateCenteredTextPaint(_theme.TextSecondary, 8f));
        _channelNameRects[channelIndex] = nameRect;

        // Delete button at top right (small)
        float deleteSize = 10f;
        float deleteX = x + width - deleteSize - 2f;
        float deleteY = y + 2f;
        var deleteRect = new SKRect(deleteX, deleteY, deleteX + deleteSize, deleteY + deleteSize);
        if (canDelete)
        {
            var deletePaint = CreateStrokePaint(_theme.TextMuted, 1f);
            canvas.DrawLine(deleteX + 2f, deleteY + 2f, deleteX + deleteSize - 2f, deleteY + deleteSize - 2f, deletePaint);
            canvas.DrawLine(deleteX + deleteSize - 2f, deleteY + 2f, deleteX + 2f, deleteY + deleteSize - 2f, deletePaint);
        }
        _channelDeleteRects.Add(new ChannelDeleteRect(channelIndex, deleteRect, canDelete));

        // Mute/Solo buttons at bottom (smaller 14px)
        float smallToggle = 14f;
        float toggleY = y + height - smallToggle - 2f;
        float toggleSpacing = (width - 2f * smallToggle - 4f) / 3f;
        var muteRect = new SKRect(x + toggleSpacing, toggleY, x + toggleSpacing + smallToggle, toggleY + smallToggle);
        DrawToggleButton(canvas, muteRect, "M", channel.IsMuted, _mutePaint);
        _toggleRects.Add(new ToggleRect(channelIndex, ToggleType.Mute, muteRect));

        var soloRect = new SKRect(muteRect.Right + toggleSpacing, toggleY, muteRect.Right + toggleSpacing + smallToggle, toggleY + smallToggle);
        DrawToggleButton(canvas, soloRect, "S", channel.IsSoloed, _soloPaint);
        _toggleRects.Add(new ToggleRect(channelIndex, ToggleType.Solo, soloRect));
    }

    private void DrawPluginChain(SKCanvas canvas, float x, float y, float width, float height, ChannelStripViewModel channel, int channelIndex, bool voxScale)
    {
        var rect = new SKRect(x, y, x + width, y + height);
        var roundRect = new SKRoundRect(rect, 4f);
        canvas.DrawRoundRect(roundRect, CreateFillPaint(_theme.ChannelPlugins));
        canvas.DrawRoundRect(roundRect, _borderPaint);
        _pluginAreaRects.Add(new PluginAreaRect(channelIndex, rect));

        float slotX = x + 4f;
        float slotY = y + 4f;
        float slotHeight = height - 8f;

        int slotCount = channel.PluginSlots.Count;
        if (slotCount == 0)
        {
            return;
        }

        int addSlotIndex = slotCount - 1;
        int pluginCount = Math.Max(0, addSlotIndex);

        _containerIndexByPluginId.Clear();
        for (int i = 0; i < channel.Containers.Count; i++)
        {
            var container = channel.Containers[i];
            var pluginIds = container.PluginInstanceIds;
            for (int j = 0; j < pluginIds.Count; j++)
            {
                int instanceId = pluginIds[j];
                if (instanceId > 0 && !_containerIndexByPluginId.ContainsKey(instanceId))
                {
                    _containerIndexByPluginId[instanceId] = i;
                }
            }
        }

        _drawnContainerIndices.Clear();

        for (int i = 0; i < pluginCount; i++)
        {
            var slot = channel.PluginSlots[i];
            if (slot.InstanceId <= 0 || slot.IsEmpty)
            {
                continue;
            }

            if (_containerIndexByPluginId.TryGetValue(slot.InstanceId, out int containerIndex))
            {
                if (!_drawnContainerIndices.Add(containerIndex))
                {
                    continue;
                }

                var container = channel.Containers[containerIndex];
                float slotWidth = container.IsEmpty
                    ? (PluginSlotWidth - MiniMeterWidth - 2f) * 0.6f
                    : PluginSlotWidth - MiniMeterWidth - 2f;
                DrawContainerSlot(canvas, slotX, slotY, slotWidth, slotHeight, container, channelIndex, containerIndex);

                float miniMeterX = slotX + slotWidth;
                float meterLevel = container.IsEmpty ? 0f : container.OutputRmsLevel;
                DrawMiniMeter(canvas, miniMeterX, slotY + 2f, MiniMeterWidth, slotHeight - 4f, meterLevel, voxScale);

                slotX += slotWidth + MiniMeterWidth + PluginSlotSpacing;
                continue;
            }

            // Check if this is a routing plugin that needs compact rendering
            float routingWidth = RoutingSlotRenderer.GetRoutingSlotWidth(slot.PluginId);
            if (routingWidth > 0f)
            {
                // Routing plugins use compact width and no separate meter (meter is inline)
                var slotRect = new SKRect(slotX, slotY, slotX + routingWidth, slotY + slotHeight);
                _routingSlotRenderer.DrawRoutingSlot(canvas, slotRect, slot, channelIndex, i, channel, voxScale);

                // Copy plugin still needs bridge tracking
                if (slot.PluginId == "builtin:copy" && slot.CopyTargetChannelId > 0)
                {
                    _copyBridgeRects.Add(new CopyBridgeRect(channelIndex, slot.CopyTargetChannelId - 1, slotRect));
                }

                slotX += routingWidth + PluginSlotSpacing;
                continue;
            }

            // Standard plugin rendering
            float pluginSlotWidth = slot.IsEmpty
                ? (PluginSlotWidth - MiniMeterWidth - 2f) * 0.6f
                : PluginSlotWidth - MiniMeterWidth - 2f;
            var standardSlotRect = new SKRect(slotX, slotY, slotX + pluginSlotWidth, slotY + slotHeight);
            _pluginShellRenderer.DrawSlot(canvas, standardSlotRect, slot, channelIndex, i);

            float pluginMeterX = slotX + pluginSlotWidth;
            float pluginMeterLevel = slot.IsEmpty ? 0f : slot.OutputRmsLevel;
            DrawMiniMeter(canvas, pluginMeterX, slotY + 2f, MiniMeterWidth, slotHeight - 4f, pluginMeterLevel, voxScale);

            slotX += pluginSlotWidth + MiniMeterWidth + PluginSlotSpacing;
        }

        for (int i = 0; i < channel.Containers.Count; i++)
        {
            var container = channel.Containers[i];
            if (container.PluginInstanceIds.Count > 0)
            {
                continue;
            }

            float slotWidth = (PluginSlotWidth - MiniMeterWidth - 2f) * 0.6f;
            DrawContainerSlot(canvas, slotX, slotY, slotWidth, slotHeight, container, channelIndex, i);

            float miniMeterX = slotX + slotWidth;
            DrawMiniMeter(canvas, miniMeterX, slotY + 2f, MiniMeterWidth, slotHeight - 4f, 0f, voxScale);

            slotX += slotWidth + MiniMeterWidth + PluginSlotSpacing;
        }

        var addSlot = channel.PluginSlots[addSlotIndex];
        float addWidth = addSlot.IsEmpty
            ? (PluginSlotWidth - MiniMeterWidth - 2f) * 0.6f
            : PluginSlotWidth - MiniMeterWidth - 2f;
        _pluginShellRenderer.DrawSlot(canvas, new SKRect(slotX, slotY, slotX + addWidth, slotY + slotHeight), addSlot, channelIndex, addSlotIndex);

        float addMeterX = slotX + addWidth;
        DrawMiniMeter(canvas, addMeterX, slotY + 2f, MiniMeterWidth, slotHeight - 4f, 0f, voxScale);
    }

    private void DrawCopyBridges(SKCanvas canvas)
    {
        if (_copyBridgeRects.Count == 0)
        {
            return;
        }

        for (int i = 0; i < _copyBridgeRects.Count; i++)
        {
            var bridge = _copyBridgeRects[i];
            if (bridge.TargetChannelIndex == bridge.SourceChannelIndex)
            {
                continue;
            }

            if (!_channelHeaderRects.TryGetValue(bridge.TargetChannelIndex, out var targetRect))
            {
                continue;
            }

            float startX = bridge.SourceRect.Right + 2f;
            float startY = bridge.SourceRect.MidY;
            float endX = targetRect.Left - 2f;
            float endY = targetRect.MidY;
            float controlX = (startX + endX) * 0.5f;

            using var path = new SKPath();
            path.MoveTo(startX, startY);
            path.CubicTo(controlX, startY, controlX, endY, endX, endY);
            canvas.DrawPath(path, _bridgePaint);
        }
    }

    private void DrawMergeBridges(SKCanvas canvas)
    {
        if (_mergeBridgeRects.Count == 0)
        {
            return;
        }

        // Use a different color for merge bridges (slightly different shade)
        using var mergePaint = CreateStrokePaint(_theme.Accent.WithAlpha(140), 1.5f);

        for (int i = 0; i < _mergeBridgeRects.Count; i++)
        {
            var bridge = _mergeBridgeRects[i];
            if (bridge.SourceChannelIndex == bridge.TargetChannelIndex)
            {
                continue;
            }

            if (!_channelHeaderRects.TryGetValue(bridge.SourceChannelIndex, out var sourceRect))
            {
                continue;
            }

            // Draw from source channel header to merge slot
            float startX = sourceRect.Right + 2f;
            float startY = sourceRect.MidY;
            float endX = bridge.TargetRect.Left - 2f;
            float endY = bridge.TargetRect.MidY;
            float controlX = (startX + endX) * 0.5f;

            using var path = new SKPath();
            path.MoveTo(startX, startY);
            path.CubicTo(controlX, startY, controlX, endY, endX, endY);
            canvas.DrawPath(path, mergePaint);
        }
    }

    private void DrawContainerSlot(SKCanvas canvas, float x, float y, float width, float height, PluginContainerViewModel container, int channelIndex, int slotIndex)
    {
        var rect = new SKRect(x, y, x + width, y + height);
        var roundRect = new SKRoundRect(rect, 3f);

        SKPaint bgPaint = container.IsEmpty ? _pluginSlotEmptyPaint :
            container.IsBypassed ? _pluginSlotBypassedPaint : _pluginSlotFilledPaint;
        canvas.DrawRoundRect(roundRect, bgPaint);
        canvas.DrawRoundRect(roundRect, _borderPaint);

        var bypassRect = SKRect.Empty;
        var removeRect = SKRect.Empty;

        bool isPlaceholder = container.ContainerId <= 0;
        if (isPlaceholder)
        {
            canvas.DrawText($"{slotIndex + 1}", x + 3f, y + 10f, _tinyTextPaint);

            float centerX = x + width / 2f;
            float centerY = y + height / 2f;
            canvas.DrawLine(centerX - 6f, centerY, centerX + 6f, centerY, _iconPaint);
            canvas.DrawLine(centerX, centerY - 6f, centerX, centerY + 6f, _iconPaint);

            DrawTruncatedText(canvas, container.ActionLabel, centerX, y + height - 10f, width - 8f, CreateCenteredTextPaint(_theme.TextMuted, 8f));
        }
        else
        {
            float topRowY = y + 2f;
            float topRowH = 12f;
            float bypassW = 22f;
            float bypassX = x + 3f;
            bypassRect = new SKRect(bypassX, topRowY, bypassX + bypassW, topRowY + topRowH);
            var bypassColor = container.IsBypassed ? _theme.Bypass : _theme.Surface;
            canvas.DrawRoundRect(new SKRoundRect(bypassRect, 2f), CreateFillPaint(bypassColor));
            var bypassTextPaint = container.IsBypassed
                ? CreateCenteredTextPaint(new SKColor(0x12, 0x12, 0x14), 7f, SKFontStyle.Bold)
                : CreateCenteredTextPaint(_theme.TextMuted, 7f);
            canvas.DrawText("BYP", bypassRect.MidX, bypassRect.MidY + 2.5f, bypassTextPaint);

            float removeSize = 8f;
            float removeX = x + width - removeSize - 4f;
            float removeY = topRowY + (topRowH - removeSize) / 2f;
            removeRect = new SKRect(removeX - 2f, removeY - 2f, removeX + removeSize + 2f, removeY + removeSize + 2f);
            canvas.DrawLine(removeX, removeY, removeX + removeSize, removeY + removeSize, _iconPaint);
            canvas.DrawLine(removeX + removeSize, removeY, removeX, removeY + removeSize, _iconPaint);

            string displayName = string.IsNullOrWhiteSpace(container.Name) ? $"Container {slotIndex + 1}" : container.Name;
            float nameLeft = bypassX + bypassW + 4f;
            float nameRight = removeX - 4f;
            float nameY = topRowY + topRowH - 2f;
            float nameCenterX = nameLeft + (nameRight - nameLeft) / 2f;
            var namePaint = container.IsBypassed
                ? CreateCenteredTextPaint(_theme.TextMuted, 8f)
                : CreateCenteredTextPaint(_theme.TextSecondary, 8f);
            DrawTruncatedText(canvas, displayName, nameCenterX, nameY, nameRight - nameLeft, namePaint);

            int pluginCount = container.PluginInstanceIds.Count;
            string countText = pluginCount == 1 ? "1 plugin" : $"{pluginCount} plugins";
            DrawTruncatedText(canvas, countText, x + width / 2f, y + height / 2f + 6f, width - 8f, CreateCenteredTextPaint(_theme.TextMuted, 8f));

            DrawTruncatedText(canvas, container.ActionLabel, x + width / 2f, y + height - 10f, width - 8f, CreateCenteredTextPaint(_theme.TextMuted, 8f));
        }

        _containerSlots.Add(new ContainerSlotRect(channelIndex, container.ContainerId, slotIndex, rect, bypassRect, removeRect));
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
            leftPeak = viewModel.MasterPeakLeft;
            rightPeak = viewModel.MasterPeakRight;
            leftRms = viewModel.MasterRmsLeft;
            rightRms = viewModel.MasterRmsRight;

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

        // Toggle on the right side (mute)
        float toggleX = x + width - ToggleSize - 6f;
        float toggleSpacing = ToggleSize + 4f;

        // Mute toggle
        float muteY = y + 18f;
        var muteRect = new SKRect(toggleX, muteY, toggleX + ToggleSize, muteY + ToggleSize);
        DrawToggleButton(canvas, muteRect, "M", viewModel.MasterMuted, _mutePaint);
        _toggleRects.Add(new ToggleRect(-1, ToggleType.MasterMute, muteRect));

        // Visualizer button
        float vizY = muteY + toggleSpacing;
        _visualizerButtonRect = new SKRect(toggleX, vizY, toggleX + ToggleSize, vizY + ToggleSize);
        DrawVisualizerButton(canvas, _visualizerButtonRect);

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
        int lineCount = 1 + 2 + 1 + inputLines + 1 + 3;
        float overlayHeight = lineCount * lineHeight + 32f;

        // Semi-transparent overlay panel
        float overlayX = Padding;
        float overlayY = TitleBarHeight + HotbarHeight + Padding;
        float overlayWidth = size.Width - Padding * 2 - MasterWidth - Padding;

        var overlayRect = new SKRect(overlayX, overlayY, overlayX + overlayWidth, overlayY + overlayHeight);
        var overlayRound = new SKRoundRect(overlayRect, 6f);

        // Dark semi-transparent background
        using var overlayBg = new SKPaint { Color = new SKColor(0x00, 0x00, 0x00, 0xE8), IsAntialias = true };
        canvas.DrawRoundRect(overlayRound, overlayBg);
        canvas.DrawRoundRect(overlayRound, _borderPaint);

        float col1X = overlayX + 12f;
        float col2X = overlayX + overlayWidth / 2f;
        float textY = overlayY + 18f;

        // Title
        canvas.DrawText("AUDIO ENGINE DIAGNOSTICS", col1X, textY, _textPaint);
        textY += lineHeight + 4f;

        // Status indicators
        string outputStatus = diag.OutputActive ? "ACTIVE" : "INACTIVE";
        string monitorStatus = diag.MonitorActive ? "ACTIVE" : "INACTIVE";

        var activeColor = CreateTextPaint(new SKColor(0x00, 0xFF, 0x00), 9f);
        var inactiveColor = CreateTextPaint(new SKColor(0xFF, 0x66, 0x66), 9f);

        canvas.DrawText("Output:", col1X, textY, _smallTextPaint);
        canvas.DrawText(outputStatus, col1X + 50f, textY, diag.OutputActive ? activeColor : inactiveColor);
        canvas.DrawText("Monitor:", col2X, textY, _smallTextPaint);
        canvas.DrawText(monitorStatus, col2X + 55f, textY, diag.MonitorActive ? activeColor : inactiveColor);
        textY += lineHeight;

        canvas.DrawText($"Inputs: {activeInputs}/{inputCount} active", col1X, textY, _smallTextPaint);
        if (diag.IsRecovering)
        {
            canvas.DrawText("RECOVERING...", col2X, textY, CreateTextPaint(new SKColor(0xFF, 0xFF, 0x00), 9f, SKFontStyle.Bold));
        }
        textY += lineHeight + 6f;

        // Input stats
        canvas.DrawText("INPUTS", col1X, textY, _textPaint);
        textY += lineHeight;

        if (inputCount == 0)
        {
            canvas.DrawText("No inputs configured", col1X, textY, _smallTextPaint);
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
                canvas.DrawText(line, col1X, textY, _smallTextPaint);
                textY += lineHeight;
            }
        }

        textY += 4f;

        // Output stats
        canvas.DrawText("OUTPUT", col1X, textY, _textPaint);
        textY += lineHeight;

        var dropColor = CreateTextPaint(_theme.MeterClip, 9f);
        var okColor = _smallTextPaint;
        string dropLine = $"Drops 30s: in {viewModel.InputDrops30Sec} out {viewModel.OutputUnderflowDrops30Sec} total {viewModel.Drops30Sec}";
        canvas.DrawText(dropLine, col1X, textY, viewModel.Drops30Sec > 0 ? dropColor : okColor);
        textY += lineHeight;

        canvas.DrawText($"Output: {diag.OutputCallbackCount} ({diag.LastOutputFrames} frames) under {diag.OutputUnderflowSamples}", col1X, textY, _smallTextPaint);
        textY += lineHeight;

        canvas.DrawText($"Monitor: {diag.MonitorBufferedSamples}/{diag.MonitorBufferCapacity}", col1X, textY, _smallTextPaint);
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

    private void DrawAddChannelButton(SKCanvas canvas, float x, float y, float width, float height)
    {
        _addChannelRect = new SKRect(x, y, x + width, y + height);
        var roundRect = new SKRoundRect(_addChannelRect, 4f);
        canvas.DrawRoundRect(roundRect, _buttonPaint);
        canvas.DrawRoundRect(roundRect, _borderPaint);

        float iconX = _addChannelRect.Left + 12f;
        float iconY = _addChannelRect.MidY;
        canvas.DrawLine(iconX - 4f, iconY, iconX + 4f, iconY, _iconPaint);
        canvas.DrawLine(iconX, iconY - 4f, iconX, iconY + 4f, _iconPaint);

        canvas.DrawText("Add Channel", _addChannelRect.Left + 22f, _addChannelRect.MidY + 4f, _textPaint);
    }

    private void DrawDeleteButton(SKCanvas canvas, SKRect rect, bool isEnabled)
    {
        var roundRect = new SKRoundRect(rect, 3f);
        canvas.DrawRoundRect(roundRect, _buttonPaint);
        canvas.DrawRoundRect(roundRect, _borderPaint);

        var color = isEnabled ? _theme.TextSecondary : _theme.TextMuted;
        using var paint = CreateStrokePaint(color, 1.5f);
        float inset = 3f;
        canvas.DrawLine(rect.Left + inset, rect.Top + inset, rect.Right - inset, rect.Bottom - inset, paint);
        canvas.DrawLine(rect.Right - inset, rect.Top + inset, rect.Left + inset, rect.Bottom - inset, paint);
    }

    private void DrawVisualizerButton(SKCanvas canvas, SKRect rect)
    {
        var roundRect = new SKRoundRect(rect, 3f);
        canvas.DrawRoundRect(roundRect, _buttonPaint);
        canvas.DrawRoundRect(roundRect, _borderPaint);

        // Draw a small waveform icon
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
            Color = _theme.Accent,
            StrokeWidth = 1.5f,
            Style = SKPaintStyle.Stroke,
            IsAntialias = true
        };
        canvas.DrawPath(path, iconPaint);
    }

    private void DrawMinimal(SKCanvas canvas, SKSize size, MainViewModel viewModel)
    {
        float y = TitleBarHeight + Padding;
        float width = size.Width - Padding * 2f;
        float rowHeight = 40f;

        for (int i = 0; i < viewModel.Channels.Count; i++)
        {
            DrawMinimalChannelRow(canvas, Padding, y, width, rowHeight, viewModel.Channels[i]);
            y += rowHeight + 4f;
        }
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
                _meterGradientColors,
                MeterGradientStops,
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

    private void DrawTruncatedText(SKCanvas canvas, string text, float x, float y, float maxWidth, SkiaTextPaint paint)
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
        _pluginShellRenderer.ClearHitTargets();
        _routingSlotRenderer.ClearHitTargets();
        _containerSlots.Clear();
        _toggleRects.Clear();
        _pluginAreaRects.Clear();
        _containerIndexByPluginId.Clear();
        _drawnContainerIndices.Clear();
        _channelHeaderRects.Clear();
        _channelNameRects.Clear();
        _copyBridgeRects.Clear();
        _mergeBridgeRects.Clear();
        _channelDeleteRects.Clear();
        _masterMeterRect = SKRect.Empty;
        _addChannelRect = SKRect.Empty;
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
        return _pluginShellRenderer.HitTestKnob(x, y);
    }

    public PluginSlotHit? HitTestPluginSlot(float x, float y, out PluginSlotRegion region)
    {
        return _pluginShellRenderer.HitTestSlot(x, y, out region);
    }

    public RoutingSlotHit? HitTestRoutingSlot(float x, float y, out RoutingSlotRegion region)
    {
        return _routingSlotRenderer.HitTestSlot(x, y, out region);
    }

    public RoutingKnobHit? HitTestRoutingKnob(float x, float y)
    {
        return _routingSlotRenderer.HitTestKnob(x, y);
    }

    public RoutingBadgeHit? HitTestRoutingBadge(float x, float y)
    {
        return _routingSlotRenderer.HitTestBadge(x, y);
    }

    public MainContainerSlotHit? HitTestContainerSlot(float x, float y, out MainContainerSlotRegion region)
    {
        foreach (var slot in _containerSlots)
        {
            if (!slot.Rect.Contains(x, y)) continue;
            if (slot.BypassRect.Contains(x, y)) { region = MainContainerSlotRegion.Bypass; return new MainContainerSlotHit(slot.ChannelIndex, slot.ContainerId, slot.SlotIndex); }
            if (slot.RemoveRect.Contains(x, y)) { region = MainContainerSlotRegion.Remove; return new MainContainerSlotHit(slot.ChannelIndex, slot.ContainerId, slot.SlotIndex); }
            region = MainContainerSlotRegion.Action;
            return new MainContainerSlotHit(slot.ChannelIndex, slot.ContainerId, slot.SlotIndex);
        }

        region = MainContainerSlotRegion.None;
        return null;
    }

    public int HitTestPluginArea(float x, float y)
    {
        foreach (var area in _pluginAreaRects)
        {
            if (area.Rect.Contains(x, y))
            {
                return area.ChannelIndex;
            }
        }

        return -1;
    }

    public ToggleHit? HitTestToggle(float x, float y)
    {
        foreach (var toggle in _toggleRects)
            if (toggle.Rect.Contains(x, y)) return new ToggleHit(toggle.ChannelIndex, toggle.ToggleType);
        return null;
    }

    public int HitTestChannelDelete(float x, float y)
    {
        for (int i = 0; i < _channelDeleteRects.Count; i++)
        {
            var deleteRect = _channelDeleteRects[i];
            if (deleteRect.Rect.Contains(x, y) && deleteRect.IsEnabled)
            {
                return deleteRect.ChannelIndex;
            }
        }

        return -1;
    }

    public int HitTestChannelName(float x, float y)
    {
        foreach (var (channelIndex, rect) in _channelNameRects)
        {
            if (rect.Contains(x, y))
            {
                return channelIndex;
            }
        }
        return -1;
    }

    public bool HitTestAddChannel(float x, float y) => _addChannelRect.Contains(x, y);

    public bool HitTestMasterMeter(float x, float y) => _masterMeterRect.Contains(x, y);

    public bool HitTestVisualizerButton(float x, float y) => _visualizerButtonRect.Contains(x, y);

    public bool HitTestMeterScaleToggle(float x, float y) => _meterScaleToggleRect.Contains(x, y);

    public bool HitTestQualityToggle(float x, float y) => _qualityToggleRect.Contains(x, y);

    public bool HitTestStatsArea(float x, float y) => _statsAreaRect.Contains(x, y);

    public bool HitTestTitleBar(float x, float y) => _titleBarRect.Contains(x, y);

    public bool HitTestPresetDropdown(float x, float y) => _presetDropdownRect.Contains(x, y);

    public SKRect GetPresetDropdownRect() => _presetDropdownRect;

    // Paint factories
    private static SKPaint CreateFillPaint(SKColor color) => new() { Color = color, IsAntialias = true, Style = SKPaintStyle.Fill };
    private static SKPaint CreateStrokePaint(SKColor color, float width) => new() { Color = color, IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = width };
    private static SkiaTextPaint CreateTextPaint(SKColor color, float size, SKFontStyle? style = null) =>
        new(color, size, style, SKTextAlign.Left);

    private static SkiaTextPaint CreateCenteredTextPaint(SKColor color, float size, SKFontStyle? style = null) =>
        new(color, size, style, SKTextAlign.Center);

    // Internal records
    private sealed record KnobRect(int ChannelIndex, KnobType KnobType, SKRect Rect);
    private sealed record CopyBridgeRect(int SourceChannelIndex, int TargetChannelIndex, SKRect SourceRect);
    private sealed record MergeBridgeRect(int SourceChannelIndex, int TargetChannelIndex, SKRect TargetRect);
    private sealed record ContainerSlotRect(int ChannelIndex, int ContainerId, int SlotIndex, SKRect Rect, SKRect BypassRect, SKRect RemoveRect);
    private sealed record PluginAreaRect(int ChannelIndex, SKRect Rect);
    private sealed record ToggleRect(int ChannelIndex, ToggleType ToggleType, SKRect Rect);
    private sealed record ChannelDeleteRect(int ChannelIndex, SKRect Rect, bool IsEnabled);
    private enum IconType { Close, Minimize, Pin, Settings }
}

public readonly record struct KnobHit(int ChannelIndex, KnobType KnobType);
public readonly record struct PluginKnobHit(int ChannelIndex, int PluginInstanceId, int ParamIndex, float MinValue, float MaxValue);
public readonly record struct PluginSlotHit(int ChannelIndex, int PluginInstanceId, int SlotIndex);
public enum PluginSlotRegion { None, Action, Bypass, Remove, Knob, DeltaStrip }
public readonly record struct MainContainerSlotHit(int ChannelIndex, int ContainerId, int SlotIndex);
public enum MainContainerSlotRegion { None, Action, Bypass, Remove }
public readonly record struct ToggleHit(int ChannelIndex, ToggleType ToggleType);
