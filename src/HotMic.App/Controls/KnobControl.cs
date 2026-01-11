using System.Globalization;
using System.Windows;
using SkiaSharp;

namespace HotMic.App.Controls;

public class KnobControl : SkiaControl
{
    public static readonly DependencyProperty ValueProperty =
        DependencyProperty.Register(nameof(Value), typeof(float), typeof(KnobControl),
            new FrameworkPropertyMetadata(0f, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty MinValueProperty =
        DependencyProperty.Register(nameof(MinValue), typeof(float), typeof(KnobControl),
            new FrameworkPropertyMetadata(-60f, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty MaxValueProperty =
        DependencyProperty.Register(nameof(MaxValue), typeof(float), typeof(KnobControl),
            new FrameworkPropertyMetadata(12f, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty LabelProperty =
        DependencyProperty.Register(nameof(Label), typeof(string), typeof(KnobControl),
            new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.AffectsRender));

    public float Value
    {
        get => (float)GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public float MinValue
    {
        get => (float)GetValue(MinValueProperty);
        set => SetValue(MinValueProperty, value);
    }

    public float MaxValue
    {
        get => (float)GetValue(MaxValueProperty);
        set => SetValue(MaxValueProperty, value);
    }

    public string Label
    {
        get => (string)GetValue(LabelProperty);
        set => SetValue(LabelProperty, value);
    }

    private bool _isDragging;
    private float _startValue;
    private System.Windows.Point _startPoint;

    private readonly SKPaint _basePaint = new() { Color = new SKColor(0x33, 0x33, 0x33), IsAntialias = true };
    private readonly SKPaint _ringPaint = new() { Color = new SKColor(0xFF, 0x6B, 0x00), StrokeWidth = 4f, IsAntialias = true, Style = SKPaintStyle.Stroke };
    private readonly SKPaint _tickPaint = new() { Color = SKColors.White, StrokeWidth = 2f, IsAntialias = true };
    private readonly SKPaint _labelPaint = new() { Color = new SKColor(0xFF, 0xFF, 0xFF), TextAlign = SKTextAlign.Center, TextSize = 12f, IsAntialias = true };

    protected override void Render(SKCanvas canvas, int width, int height)
    {
        var center = new SKPoint(width / 2f, height / 2f - 6f);
        float radius = MathF.Min(width, height) * 0.35f;

        canvas.DrawCircle(center, radius, _basePaint);

        float normalized = (Value - MinValue) / Math.Max(0.0001f, MaxValue - MinValue);
        normalized = Math.Clamp(normalized, 0f, 1f);
        float startAngle = 225f;
        float sweepAngle = 270f * normalized;
        using var arcPath = new SKPath();
        arcPath.AddArc(new SKRect(center.X - radius, center.Y - radius, center.X + radius, center.Y + radius), startAngle, sweepAngle);
        canvas.DrawPath(arcPath, _ringPaint);

        float indicatorAngle = (startAngle + sweepAngle) * (MathF.PI / 180f);
        var indicator = new SKPoint(center.X + MathF.Cos(indicatorAngle) * radius * 0.8f,
            center.Y + MathF.Sin(indicatorAngle) * radius * 0.8f);
        canvas.DrawLine(center, indicator, _tickPaint);

        string valueLabel = Value.ToString("0.0", CultureInfo.InvariantCulture);
        canvas.DrawText(valueLabel, center.X, center.Y + radius + 18f, _labelPaint);
        if (!string.IsNullOrWhiteSpace(Label))
        {
            canvas.DrawText(Label, center.X, center.Y - radius - 6f, _labelPaint);
        }
    }

    protected override void OnMouseDown(System.Windows.Input.MouseButtonEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.ChangedButton != System.Windows.Input.MouseButton.Left)
        {
            return;
        }

        _isDragging = true;
        _startValue = Value;
        _startPoint = e.GetPosition(this);
        CaptureMouse();
    }

    protected override void OnMouseMove(System.Windows.Input.MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (!_isDragging || e.LeftButton != System.Windows.Input.MouseButtonState.Pressed)
        {
            return;
        }

        var current = e.GetPosition(this);
        float delta = (float)(_startPoint.Y - current.Y);
        float range = MaxValue - MinValue;
        float sensitivity = range / 150f;
        float nextValue = _startValue + delta * sensitivity;
        Value = Math.Clamp(nextValue, MinValue, MaxValue);
    }

    protected override void OnMouseUp(System.Windows.Input.MouseButtonEventArgs e)
    {
        base.OnMouseUp(e);
        if (e.ChangedButton != System.Windows.Input.MouseButton.Left)
        {
            return;
        }

        _isDragging = false;
        ReleaseMouseCapture();
    }
}
