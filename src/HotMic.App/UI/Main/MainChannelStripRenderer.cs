using SkiaSharp;
using HotMic.App.ViewModels;

namespace HotMic.App.UI;

internal sealed class MainChannelStripRenderer
{
    private readonly MainPaintCache _paints;
    private readonly MainRenderPrimitives _primitives;
    private readonly MainHitTargetRegistry _hitTargets;
    private readonly MainPluginChainRenderer _pluginChainRenderer;

    public MainChannelStripRenderer(
        MainPaintCache paints,
        MainRenderPrimitives primitives,
        MainHitTargetRegistry hitTargets,
        MainPluginChainRenderer pluginChainRenderer)
    {
        _paints = paints;
        _primitives = primitives;
        _hitTargets = hitTargets;
        _pluginChainRenderer = pluginChainRenderer;
    }

    public void Render(
        SKCanvas canvas,
        float x,
        float y,
        float width,
        float height,
        ChannelStripViewModel channel,
        int channelIndex,
        bool canDelete,
        bool voxScale,
        IDictionary<int, SKRect> channelHeaderRects,
        IList<CopyBridgeRect> copyBridges,
        IList<MergeBridgeRect> mergeBridges)
    {
        var stripRect = new SKRect(x, y, x + width, y + height);
        var stripRound = new SKRoundRect(stripRect, 6f);
        canvas.DrawRoundRect(stripRound, _paints.SectionPaint);
        canvas.DrawRoundRect(stripRound, _paints.BorderPaint);

        float sectionX = x + MainLayoutMetrics.ChannelStripInnerPadding;
        float sectionY = y + MainLayoutMetrics.ChannelStripInnerPadding;
        float sectionHeight = height - MainLayoutMetrics.ChannelStripInnerPadding * 2f;

        var headerRect = new SKRect(sectionX, sectionY, sectionX + MainLayoutMetrics.ChannelHeaderWidth, sectionY + sectionHeight);
        channelHeaderRects[channelIndex] = headerRect;
        DrawChannelHeader(canvas, headerRect, channel, channelIndex, canDelete);

        sectionX += MainLayoutMetrics.ChannelHeaderWidth + MainLayoutMetrics.ChannelStripHeaderGap;
        float pluginAreaWidth = width - MainLayoutMetrics.ChannelHeaderWidth - MainLayoutMetrics.ChannelStripExtraWidth;
        var pluginRect = new SKRect(sectionX, sectionY, sectionX + pluginAreaWidth, sectionY + sectionHeight);
        _pluginChainRenderer.Render(canvas, pluginRect, channel, channelIndex, voxScale, copyBridges, mergeBridges);
    }

    private void DrawChannelHeader(SKCanvas canvas, SKRect rect, ChannelStripViewModel channel, int channelIndex, bool canDelete)
    {
        var roundRect = new SKRoundRect(rect, 4f);
        canvas.DrawRoundRect(roundRect, MainRenderPrimitives.CreateFillPaint(_paints.Theme.ChannelInput));
        canvas.DrawRoundRect(roundRect, _paints.BorderPaint);

        string displayName = string.IsNullOrWhiteSpace(channel.Name) ? $"CH{channelIndex + 1}" : channel.Name;
        float nameMaxWidth = rect.Width - 8f;
        var nameRect = new SKRect(rect.Left + 2f, rect.Top + 2f, rect.Right - 2f, rect.Top + 18f);
        _primitives.DrawEllipsizedText(canvas, displayName, rect.MidX, rect.Top + 13f, nameMaxWidth,
            MainRenderPrimitives.CreateCenteredTextPaint(_paints.Theme.TextSecondary, 8f));
        _hitTargets.ChannelNames[channelIndex] = nameRect;

        float deleteSize = 10f;
        float deleteX = rect.Right - deleteSize - 2f;
        float deleteY = rect.Top + 2f;
        var deleteRect = new SKRect(deleteX, deleteY, deleteX + deleteSize, deleteY + deleteSize);
        if (canDelete)
        {
            var deletePaint = MainRenderPrimitives.CreateStrokePaint(_paints.Theme.TextMuted, 1f);
            canvas.DrawLine(deleteX + 2f, deleteY + 2f, deleteX + deleteSize - 2f, deleteY + deleteSize - 2f, deletePaint);
            canvas.DrawLine(deleteX + deleteSize - 2f, deleteY + 2f, deleteX + 2f, deleteY + deleteSize - 2f, deletePaint);
        }
        _hitTargets.ChannelDeletes.Add(new ChannelDeleteRect(channelIndex, deleteRect, canDelete));

        float smallToggle = 14f;
        float toggleY = rect.Bottom - smallToggle - 2f;
        float toggleSpacing = (rect.Width - 2f * smallToggle - 4f) / 3f;
        var muteRect = new SKRect(rect.Left + toggleSpacing, toggleY, rect.Left + toggleSpacing + smallToggle, toggleY + smallToggle);
        _primitives.DrawToggleButton(canvas, muteRect, "M", channel.IsMuted, _paints.MutePaint);
        _hitTargets.Toggles.Add(new ToggleRect(channelIndex, ToggleType.Mute, muteRect));

        var soloRect = new SKRect(muteRect.Right + toggleSpacing, toggleY, muteRect.Right + toggleSpacing + smallToggle, toggleY + smallToggle);
        _primitives.DrawToggleButton(canvas, soloRect, "S", channel.IsSoloed, _paints.SoloPaint);
        _hitTargets.Toggles.Add(new ToggleRect(channelIndex, ToggleType.Solo, soloRect));
    }
}
