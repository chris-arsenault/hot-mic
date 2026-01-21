using System;
using SkiaSharp;
using HotMic.App.ViewModels;

namespace HotMic.App.UI;

internal sealed class MainFullViewRenderer
{
    private readonly MainPaintCache _paints;
    private readonly MainRenderPrimitives _primitives;
    private readonly MainHitTargetRegistry _hitTargets;
    private readonly MainChannelStripRenderer _channelStripRenderer;
    private readonly MainMasterSectionRenderer _masterRenderer;
    private readonly Dictionary<int, SKRect> _channelHeaderRects = new();
    private readonly List<CopyBridgeRect> _copyBridgeRects = new();
    private readonly List<MergeBridgeRect> _mergeBridgeRects = new();

    public MainFullViewRenderer(
        MainPaintCache paints,
        MainRenderPrimitives primitives,
        MainHitTargetRegistry hitTargets,
        MainChannelStripRenderer channelStripRenderer,
        MainMasterSectionRenderer masterRenderer)
    {
        _paints = paints;
        _primitives = primitives;
        _hitTargets = hitTargets;
        _channelStripRenderer = channelStripRenderer;
        _masterRenderer = masterRenderer;
    }

    public void Render(SKCanvas canvas, MainLayoutFrame layout, MainViewModel viewModel)
    {
        _channelHeaderRects.Clear();
        _copyBridgeRects.Clear();
        _mergeBridgeRects.Clear();

        float contentTop = layout.ContentRect.Top;
        float contentLeft = layout.ContentRect.Left;
        float contentRight = layout.ContentRect.Right;

        float masterSectionWidth = MainLayoutMetrics.MasterWidth + MainLayoutMetrics.Padding;
        float channelAreaWidth = contentRight - contentLeft - masterSectionWidth - MainLayoutMetrics.Padding;

        float channelY = contentTop;
        bool canDelete = viewModel.Channels.Count > 1;
        for (int i = 0; i < viewModel.Channels.Count; i++)
        {
            _channelStripRenderer.Render(canvas, contentLeft, channelY, channelAreaWidth, MainLayoutMetrics.ChannelStripHeight,
                viewModel.Channels[i], i, canDelete, viewModel.MeterScaleVox, _channelHeaderRects, _copyBridgeRects, _mergeBridgeRects);
            channelY += MainLayoutMetrics.ChannelStripHeight + MainLayoutMetrics.ChannelSpacing;
        }

        DrawAddChannelButton(canvas, contentLeft, channelY + MainLayoutMetrics.AddChannelSpacing, channelAreaWidth, MainLayoutMetrics.AddChannelHeight);

        DrawCopyBridges(canvas);
        DrawMergeBridges(canvas);

        float masterX = contentRight - masterSectionWidth;
        float masterHeight = MainLayoutMetrics.ChannelStripHeight * Math.Max(1, viewModel.Channels.Count) +
                             MainLayoutMetrics.ChannelSpacing * Math.Max(0, viewModel.Channels.Count - 1);
        var masterRect = new SKRect(masterX, contentTop, masterX + masterSectionWidth, contentTop + masterHeight);
        _masterRenderer.Render(canvas, masterRect, viewModel);
    }

    private void DrawAddChannelButton(SKCanvas canvas, float x, float y, float width, float height)
    {
        _hitTargets.AddChannelRect = new SKRect(x, y, x + width, y + height);
        var roundRect = new SKRoundRect(_hitTargets.AddChannelRect, 4f);
        canvas.DrawRoundRect(roundRect, _paints.ButtonPaint);
        canvas.DrawRoundRect(roundRect, _paints.BorderPaint);

        float iconX = _hitTargets.AddChannelRect.Left + 12f;
        float iconY = _hitTargets.AddChannelRect.MidY;
        canvas.DrawLine(iconX - 4f, iconY, iconX + 4f, iconY, _paints.IconPaint);
        canvas.DrawLine(iconX, iconY - 4f, iconX, iconY + 4f, _paints.IconPaint);

        canvas.DrawText("Add Channel", _hitTargets.AddChannelRect.Left + 22f, _hitTargets.AddChannelRect.MidY + 4f, _paints.TextPaint);
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
            canvas.DrawPath(path, _paints.BridgePaint);
        }
    }

    private void DrawMergeBridges(SKCanvas canvas)
    {
        if (_mergeBridgeRects.Count == 0)
        {
            return;
        }

        using var mergePaint = MainRenderPrimitives.CreateStrokePaint(_paints.Theme.Accent.WithAlpha(140), 1.5f);

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
}
