using System.Windows;
using System.Windows.Input;
using HotMic.App.UI;
using SkiaSharp;
using SkiaSharp.Views.WPF;

namespace HotMic.App.Views;

public partial class SkiaInputDialog : Window
{
    private const float CornerRadius = 8f;
    private const float TitleBarHeight = 32f;
    private const float Padding = 16f;
    private const float ButtonWidth = 75f;
    private const float ButtonHeight = 28f;
    private const float ButtonSpacing = 8f;

    private readonly HotMicTheme _theme = HotMicTheme.Default;
    private readonly SKPaint _backgroundPaint;
    private readonly SKPaint _titleBarPaint;
    private readonly SKPaint _borderPaint;
    private readonly SKPaint _buttonPaint;
    private readonly SKPaint _buttonHoverPaint;
    private readonly SKPaint _buttonAccentPaint;
    private readonly SKPaint _buttonAccentHoverPaint;
    private readonly SkiaTextPaint _titlePaint;
    private readonly SkiaTextPaint _promptPaint;
    private readonly SkiaTextPaint _buttonTextPaint;
    private readonly SkiaTextPaint _buttonTextMutedPaint;

    private SKRect _okButtonRect;
    private SKRect _cancelButtonRect;
    private SKRect _titleBarRect;
    private bool _okHovered;
    private bool _cancelHovered;

    private readonly string _title;
    private readonly string _prompt;

    public string InputValue => InputTextBox.Text;

    public SkiaInputDialog(string title, string prompt, string defaultValue = "")
    {
        InitializeComponent();

        _title = title;
        _prompt = prompt;
        Title = title;
        InputTextBox.Text = defaultValue;

        _backgroundPaint = new SKPaint { Color = _theme.BackgroundPrimary, IsAntialias = true, Style = SKPaintStyle.Fill };
        _titleBarPaint = new SKPaint { Color = _theme.BackgroundSecondary, IsAntialias = true, Style = SKPaintStyle.Fill };
        _borderPaint = new SKPaint { Color = _theme.Border, IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1f };
        _buttonPaint = new SKPaint { Color = _theme.Surface, IsAntialias = true, Style = SKPaintStyle.Fill };
        _buttonHoverPaint = new SKPaint { Color = _theme.SurfaceHover, IsAntialias = true, Style = SKPaintStyle.Fill };
        _buttonAccentPaint = new SKPaint { Color = _theme.Accent, IsAntialias = true, Style = SKPaintStyle.Fill };
        _buttonAccentHoverPaint = new SKPaint { Color = _theme.AccentHover, IsAntialias = true, Style = SKPaintStyle.Fill };
        _titlePaint = new SkiaTextPaint(_theme.TextPrimary, 12f, SKFontStyle.Bold, SKTextAlign.Left);
        _promptPaint = new SkiaTextPaint(_theme.TextSecondary, 11f, (SKFontStyle?)null, SKTextAlign.Left);
        _buttonTextPaint = new SkiaTextPaint(_theme.TextPrimary, 11f, (SKFontStyle?)null, SKTextAlign.Center);
        _buttonTextMutedPaint = new SkiaTextPaint(_theme.TextSecondary, 11f, (SKFontStyle?)null, SKTextAlign.Center);

        Loaded += (_, _) =>
        {
            InputTextBox.SelectAll();
            InputTextBox.Focus();
        };
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

        // Prompt text
        float promptY = TitleBarHeight + 14f;
        canvas.DrawText(_prompt, Padding, promptY, _promptPaint);

        // Buttons at bottom
        float buttonY = height - Padding - ButtonHeight;
        float cancelX = width - Padding - ButtonWidth;
        float okX = cancelX - ButtonSpacing - ButtonWidth;

        // OK button (accent)
        _okButtonRect = new SKRect(okX, buttonY, okX + ButtonWidth, buttonY + ButtonHeight);
        var okPaint = _okHovered ? _buttonAccentHoverPaint : _buttonAccentPaint;
        canvas.DrawRoundRect(new SKRoundRect(_okButtonRect, 4f), okPaint);
        canvas.DrawText("OK", _okButtonRect.MidX, _okButtonRect.MidY + 4f, _buttonTextPaint);

        // Cancel button
        _cancelButtonRect = new SKRect(cancelX, buttonY, cancelX + ButtonWidth, buttonY + ButtonHeight);
        var cancelPaint = _cancelHovered ? _buttonHoverPaint : _buttonPaint;
        canvas.DrawRoundRect(new SKRoundRect(_cancelButtonRect, 4f), cancelPaint);
        canvas.DrawText("Cancel", _cancelButtonRect.MidX, _cancelButtonRect.MidY + 4f, _buttonTextMutedPaint);
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
            DialogResult = true;
            Close();
            e.Handled = true;
            return;
        }

        if (_cancelButtonRect.Contains(x, y))
        {
            DialogResult = false;
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
        bool cancelHovered = _cancelButtonRect.Contains(x, y);

        if (okHovered != _okHovered || cancelHovered != _cancelHovered)
        {
            _okHovered = okHovered;
            _cancelHovered = cancelHovered;
            SkiaCanvas.InvalidateVisual();
        }
    }

    private void InputTextBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            DialogResult = true;
            Close();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            DialogResult = false;
            Close();
            e.Handled = true;
        }
    }
}
