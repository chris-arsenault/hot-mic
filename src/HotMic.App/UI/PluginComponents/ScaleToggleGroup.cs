using SkiaSharp;

namespace HotMic.App.UI.PluginComponents;

public sealed class ScaleToggleGroup : IDisposable
{
    private const float ToggleWidth = 58f;
    private const float ToggleHeight = 18f;
    private const float CornerRadius = 9f;

    private static readonly string[] Labels = ["x1", "x2", "x5", "x10"];

    private readonly SKPaint _fillPaint;
    private readonly SKPaint _borderPaint;
    private readonly SkiaTextPaint _textPaint;
    private readonly SKColor[] _fillColors;
    private readonly SKColor[] _textColors;

    private SKRect _toggleRect;
    private int _lastSelectedIndex;

    public ScaleToggleGroup(PluginComponentTheme theme)
    {
        _fillPaint = new SKPaint
        {
            Color = theme.LabelBackground,
            IsAntialias = true,
            Style = SKPaintStyle.Fill
        };

        _borderPaint = new SKPaint
        {
            Color = theme.LabelBorder,
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 1f
        };

        _textPaint = new SkiaTextPaint(theme.TextPrimary, 9f, SKFontStyle.Normal, SKTextAlign.Center);

        _fillColors =
        [
            theme.PanelBackgroundLight.WithAlpha(0),
            new SKColor(0x4A, 0x8D, 0xFF),
            new SKColor(0xE0, 0xB0, 0x2E),
            new SKColor(0xFF, 0x5A, 0x5A)
        ];

        _textColors =
        [
            theme.TextMuted,
            theme.TextPrimary,
            theme.TextPrimary,
            theme.TextPrimary
        ];
    }

    public float Width => ToggleWidth;

    public float Height => ToggleHeight;

    public void Render(SKCanvas canvas, float x, float y, int selectedIndex)
    {
        int index = Math.Clamp(selectedIndex, 0, Labels.Length - 1);
        _lastSelectedIndex = index;
        _toggleRect = new SKRect(x, y, x + Width, y + Height);

        _fillPaint.Color = _fillColors[index];
        canvas.DrawRoundRect(new SKRoundRect(_toggleRect, CornerRadius), _fillPaint);
        canvas.DrawRoundRect(new SKRoundRect(_toggleRect, CornerRadius), _borderPaint);

        _textPaint.Color = _textColors[index];
        _textPaint.DrawText(canvas, Labels[index], _toggleRect.MidX, _toggleRect.MidY + 3f);
    }

    public int HitTest(float x, float y)
    {
        if (!_toggleRect.Contains(x, y))
        {
            return -1;
        }

        return (_lastSelectedIndex + 1) % Labels.Length;
    }

    public void Dispose()
    {
        _fillPaint.Dispose();
        _borderPaint.Dispose();
        _textPaint.Dispose();
    }
}
