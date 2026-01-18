using System.Windows;
using System.Windows.Input;
using HotMic.App.UI;
using SkiaSharp;
using SkiaSharp.Views.WPF;

namespace HotMic.App.Views;

public enum SkiaMessageType
{
    Information,
    Warning,
    Error
}

public partial class SkiaMessageDialog : Window
{
    private const float CornerRadius = 8f;
    private const float TitleBarHeight = 32f;
    private const float Padding = 16f;
    private const float ButtonWidth = 75f;
    private const float ButtonHeight = 28f;
    private const float IconSize = 24f;

    private readonly HotMicTheme _theme = HotMicTheme.Default;
    private readonly SKPaint _backgroundPaint;
    private readonly SKPaint _titleBarPaint;
    private readonly SKPaint _borderPaint;
    private readonly SKPaint _buttonPaint;
    private readonly SKPaint _buttonHoverPaint;
    private readonly SKPaint _iconPaint;
    private readonly SkiaTextPaint _titlePaint;
    private readonly SkiaTextPaint _messagePaint;
    private readonly SkiaTextPaint _buttonTextPaint;

    private SKRect _okButtonRect;
    private SKRect _titleBarRect;
    private bool _okHovered;

    private readonly string _title;
    private readonly string _message;
    private readonly SkiaMessageType _messageType;

    public SkiaMessageDialog(string title, string message, SkiaMessageType messageType = SkiaMessageType.Information)
    {
        InitializeComponent();

        _title = title;
        _message = message;
        _messageType = messageType;
        Title = title;

        _backgroundPaint = new SKPaint { Color = _theme.BackgroundPrimary, IsAntialias = true, Style = SKPaintStyle.Fill };
        _titleBarPaint = new SKPaint { Color = _theme.BackgroundSecondary, IsAntialias = true, Style = SKPaintStyle.Fill };
        _borderPaint = new SKPaint { Color = _theme.Border, IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1f };
        _buttonPaint = new SKPaint { Color = _theme.Surface, IsAntialias = true, Style = SKPaintStyle.Fill };
        _buttonHoverPaint = new SKPaint { Color = _theme.SurfaceHover, IsAntialias = true, Style = SKPaintStyle.Fill };
        _titlePaint = new SkiaTextPaint(_theme.TextPrimary, 12f, SKFontStyle.Bold, SKTextAlign.Left);
        _messagePaint = new SkiaTextPaint(_theme.TextSecondary, 11f, (SKFontStyle?)null, SKTextAlign.Left);
        _buttonTextPaint = new SkiaTextPaint(_theme.TextPrimary, 11f, (SKFontStyle?)null, SKTextAlign.Center);

        // Icon color based on message type
        SKColor iconColor = messageType switch
        {
            SkiaMessageType.Error => _theme.Mute,
            SkiaMessageType.Warning => _theme.Solo,
            _ => _theme.Accent
        };
        _iconPaint = new SKPaint { Color = iconColor, IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 2f };
    }

    public static void Show(Window owner, string message, string title, SkiaMessageType messageType = SkiaMessageType.Information)
    {
        var dialog = new SkiaMessageDialog(title, message, messageType)
        {
            Owner = owner
        };
        dialog.ShowDialog();
    }

    public static void ShowError(Window owner, string message, string title = "Error")
    {
        Show(owner, message, title, SkiaMessageType.Error);
    }

    public static void ShowWarning(Window owner, string message, string title = "Warning")
    {
        Show(owner, message, title, SkiaMessageType.Warning);
    }

    public static void ShowInfo(Window owner, string message, string title = "Information")
    {
        Show(owner, message, title, SkiaMessageType.Information);
    }

    private void SkiaCanvas_PaintSurface(object sender, SKPaintSurfaceEventArgs e)
    {
        var canvas = e.Surface.Canvas;
        float width = e.Info.Width;
        float height = e.Info.Height;

        canvas.Clear(SKColors.Transparent);

        // Background with rounded corners
        var bgRect = new SKRoundRect(new SKRect(0, 0, width, height), CornerRadius);
        canvas.DrawRoundRect(bgRect, _backgroundPaint);
        canvas.DrawRoundRect(bgRect, _borderPaint);

        // Title bar
        _titleBarRect = new SKRect(0, 0, width, TitleBarHeight);
        using (var clipPath = new SKPath())
        {
            var titleClipRect = new SKRoundRect(new SKRect(0, 0, width, TitleBarHeight + CornerRadius), CornerRadius);
            clipPath.AddRoundRect(titleClipRect);
            canvas.Save();
            canvas.ClipPath(clipPath);
            canvas.DrawRect(_titleBarRect, _titleBarPaint);
            canvas.Restore();
        }
        canvas.DrawLine(0, TitleBarHeight, width, TitleBarHeight, _borderPaint);

        // Title text
        canvas.DrawText(_title, Padding, TitleBarHeight / 2f + 4f, _titlePaint);

        // Icon
        float iconX = Padding;
        float iconY = TitleBarHeight + Padding;
        DrawIcon(canvas, iconX, iconY);

        // Message text (with word wrap)
        float messageX = iconX + IconSize + 12f;
        float messageY = iconY + 4f;
        float maxWidth = width - messageX - Padding;
        DrawWrappedText(canvas, _message, messageX, messageY, maxWidth);

        // OK button at bottom center
        float buttonY = height - Padding - ButtonHeight;
        float buttonX = (width - ButtonWidth) / 2f;

        _okButtonRect = new SKRect(buttonX, buttonY, buttonX + ButtonWidth, buttonY + ButtonHeight);
        var okPaint = _okHovered ? _buttonHoverPaint : _buttonPaint;
        canvas.DrawRoundRect(new SKRoundRect(_okButtonRect, 4f), okPaint);
        canvas.DrawText("OK", _okButtonRect.MidX, _okButtonRect.MidY + 4f, _buttonTextPaint);
    }

    private void DrawIcon(SKCanvas canvas, float x, float y)
    {
        float cx = x + IconSize / 2f;
        float cy = y + IconSize / 2f;
        float r = IconSize / 2f - 2f;

        switch (_messageType)
        {
            case SkiaMessageType.Error:
                // X in circle
                canvas.DrawCircle(cx, cy, r, _iconPaint);
                float offset = r * 0.5f;
                canvas.DrawLine(cx - offset, cy - offset, cx + offset, cy + offset, _iconPaint);
                canvas.DrawLine(cx + offset, cy - offset, cx - offset, cy + offset, _iconPaint);
                break;

            case SkiaMessageType.Warning:
                // Triangle with !
                using (var path = new SKPath())
                {
                    path.MoveTo(cx, y + 2f);
                    path.LineTo(x + IconSize - 2f, y + IconSize - 2f);
                    path.LineTo(x + 2f, y + IconSize - 2f);
                    path.Close();
                    canvas.DrawPath(path, _iconPaint);
                }
                canvas.DrawLine(cx, cy - 2f, cx, cy + 2f, _iconPaint);
                canvas.DrawCircle(cx, cy + 6f, 1.5f, new SKPaint { Color = _iconPaint.Color, IsAntialias = true, Style = SKPaintStyle.Fill });
                break;

            default:
                // Circle with i
                canvas.DrawCircle(cx, cy, r, _iconPaint);
                canvas.DrawCircle(cx, cy - 4f, 1.5f, new SKPaint { Color = _iconPaint.Color, IsAntialias = true, Style = SKPaintStyle.Fill });
                canvas.DrawLine(cx, cy, cx, cy + 6f, _iconPaint);
                break;
        }
    }

    private void DrawWrappedText(SKCanvas canvas, string text, float x, float y, float maxWidth)
    {
        // Simple word wrap
        var words = text.Split(' ');
        float lineHeight = 16f;
        float currentX = x;
        float currentY = y;
        string currentLine = "";

        foreach (var word in words)
        {
            string testLine = string.IsNullOrEmpty(currentLine) ? word : currentLine + " " + word;
            float testWidth = _messagePaint.MeasureText(testLine);

            if (testWidth > maxWidth && !string.IsNullOrEmpty(currentLine))
            {
                canvas.DrawText(currentLine, currentX, currentY, _messagePaint);
                currentY += lineHeight;
                currentLine = word;
            }
            else
            {
                currentLine = testLine;
            }
        }

        if (!string.IsNullOrEmpty(currentLine))
        {
            canvas.DrawText(currentLine, currentX, currentY, _messagePaint);
        }
    }

    private void SkiaCanvas_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left)
        {
            return;
        }

        var pos = e.GetPosition(SkiaCanvas);
        float x = (float)pos.X;
        float y = (float)pos.Y;

        if (_okButtonRect.Contains(x, y))
        {
            Close();
            e.Handled = true;
            return;
        }

        // Title bar drag
        if (_titleBarRect.Contains(x, y))
        {
            DragMove();
            e.Handled = true;
        }
    }

    private void SkiaCanvas_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        var pos = e.GetPosition(SkiaCanvas);
        float x = (float)pos.X;
        float y = (float)pos.Y;

        bool okHovered = _okButtonRect.Contains(x, y);

        if (okHovered != _okHovered)
        {
            _okHovered = okHovered;
            SkiaCanvas.InvalidateVisual();
        }
    }

    private void Window_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Enter || e.Key == Key.Escape)
        {
            Close();
            e.Handled = true;
        }
    }
}
