using SkiaSharp;
using HotMic.App.ViewModels;

namespace HotMic.App.UI;

internal sealed class MainMinimalViewRenderer
{
    private readonly MainPaintCache _paints;
    private readonly MainMeterRenderer _meterRenderer;

    public MainMinimalViewRenderer(MainPaintCache paints, MainMeterRenderer meterRenderer)
    {
        _paints = paints;
        _meterRenderer = meterRenderer;
    }

    public void Render(SKCanvas canvas, MainLayoutFrame layout, MainViewModel viewModel)
    {
        float y = layout.ContentRect.Top;
        float width = layout.ContentRect.Width;
        float rowHeight = MainLayoutMetrics.MinimalRowHeight;

        for (int i = 0; i < viewModel.Channels.Count; i++)
        {
            DrawMinimalChannelRow(canvas, layout.ContentRect.Left, y, width, rowHeight, viewModel.Channels[i]);
            y += rowHeight + MainLayoutMetrics.MinimalRowSpacing;
        }
    }

    private void DrawMinimalChannelRow(SKCanvas canvas, float x, float y, float width, float height, ChannelStripViewModel channel)
    {
        var rect = new SKRect(x, y, x + width, y + height);
        var roundRect = new SKRoundRect(rect, 4f);
        canvas.DrawRoundRect(roundRect, _paints.SectionPaint);
        canvas.DrawRoundRect(roundRect, _paints.BorderPaint);

        canvas.DrawText(channel.Name, x + 8f, y + height / 2f + 3f, _paints.TextPaint);

        float meterX = x + 80f;
        float meterWidth = width - 140f;
        float meterHeight = 12f;
        float meterY = y + (height - meterHeight) / 2f;
        _meterRenderer.DrawHorizontalMeter(canvas, meterX, meterY, meterWidth, meterHeight, channel.OutputPeakLevel, channel.OutputRmsLevel);

        string dbText = $"{MainMeterRenderer.LinearToDb(channel.OutputPeakLevel):0.0} dB";
        canvas.DrawText(dbText, x + width - 50f, y + height / 2f + 3f, _paints.TextSecondaryPaint);
    }
}
