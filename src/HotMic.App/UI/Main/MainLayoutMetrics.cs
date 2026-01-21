using System;

namespace HotMic.App.UI;

public static class MainLayoutMetrics
{
    public const float CornerRadius = 8f;
    public const float TitleBarHeight = 36f;
    public const float HotbarHeight = 24f;
    public const float Padding = 10f;
    public const float ChannelStripHeight = 102f;
    public const float ChannelSpacing = 6f;
    public const float MasterWidth = 90f;
    public const float AddChannelHeight = 26f;
    public const float AddChannelSpacing = 0f;

    public const float ChannelHeaderWidth = 60f;
    public const float PluginSlotWidth = 130f;
    public const float PluginSlotSpacing = 2f;
    public const float MiniMeterWidth = 6f;
    public const float PluginSlotInnerGap = 2f;
    public const float PluginAreaPadding = 4f;
    public const float ChannelStripInnerPadding = 6f;
    public const float ChannelStripHeaderGap = 4f;

    public const float MeterWidth = 16f;
    public const float StereoMeterWidth = 36f;
    public const float KnobSize = 36f;
    public const float ToggleSize = 18f;

    public const int MeterSegments = 16;
    public const float SegmentGap = 1f;

    public const float MinimalViewWidth = 400f;
    public const float MinimalViewPadding = 10f;
    public const float MinimalRowHeight = 40f;
    public const float MinimalRowSpacing = 4f;

    public const float MinFullViewWidth = 500f;
    public const float MaxFullViewWidth = 1600f;

    public static float ChannelStripExtraWidth => ChannelStripInnerPadding * 2f + ChannelStripHeaderGap;

    public static float PluginSlotPitch => PluginSlotWidth + PluginSlotSpacing - PluginSlotInnerGap;

    public static float FullViewBaseWidth => MasterWidth + (Padding * 4f) + ChannelHeaderWidth + ChannelStripExtraWidth;

    public static float GetPluginAreaWidth(int visibleSlots)
    {
        if (visibleSlots <= 0)
        {
            return 0f;
        }

        return (PluginAreaPadding * 2f) + (visibleSlots * PluginSlotPitch) - PluginSlotSpacing;
    }

    public static double GetFullViewWidth(int visibleSlots)
    {
        float width = FullViewBaseWidth + GetPluginAreaWidth(visibleSlots);
        return Math.Clamp(width, MinFullViewWidth, MaxFullViewWidth);
    }

    public static double GetFullViewHeight(int channelCount)
    {
        int count = Math.Max(1, channelCount);
        float channelAreaHeight = ChannelStripHeight * count + ChannelSpacing * Math.Max(0, count - 1);
        float addChannelAreaHeight = ChannelSpacing + AddChannelHeight;
        return TitleBarHeight + HotbarHeight + Padding + channelAreaHeight + addChannelAreaHeight + Padding;
    }

    public static double GetMinimalViewHeight(int channelCount)
    {
        int count = Math.Max(1, channelCount);
        float rowsHeight = MinimalRowHeight * count + MinimalRowSpacing * Math.Max(0, count - 1);
        return TitleBarHeight + MinimalViewPadding + rowsHeight + MinimalViewPadding;
    }
}
