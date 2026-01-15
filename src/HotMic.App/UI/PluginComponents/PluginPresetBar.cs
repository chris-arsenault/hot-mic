using SkiaSharp;

namespace HotMic.App.UI.PluginComponents;

/// <summary>
/// Reusable preset selector bar for plugin windows.
/// Renders a dropdown for preset selection and a save button.
/// </summary>
public sealed class PluginPresetBar : IDisposable
{
    private const float DropdownWidth = 100f;
    private const float SaveButtonWidth = 24f;
    private const float Height = 22f;
    private const float Spacing = 6f;
    private const float CornerRadius = 4f;

    private readonly PluginComponentTheme _theme;
    private readonly SKPaint _backgroundPaint;
    private readonly SKPaint _borderPaint;
    private readonly SkiaTextPaint _textPaint;
    private readonly SKPaint _arrowPaint;
    private readonly SKPaint _iconPaint;

    private SKRect _dropdownRect;
    private SKRect _saveButtonRect;

    public PluginPresetBar(PluginComponentTheme? theme = null)
    {
        _theme = theme ?? PluginComponentTheme.Default;

        _backgroundPaint = new SKPaint
        {
            Color = _theme.PanelBackgroundLight,
            IsAntialias = true,
            Style = SKPaintStyle.Fill
        };

        _borderPaint = new SKPaint
        {
            Color = _theme.PanelBorder,
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 1f
        };

        _textPaint = new SkiaTextPaint(_theme.TextSecondary, 10f, SKFontStyle.Normal);

        _arrowPaint = new SKPaint
        {
            Color = _theme.TextMuted,
            IsAntialias = true,
            Style = SKPaintStyle.Fill
        };

        _iconPaint = new SKPaint
        {
            Color = _theme.TextSecondary,
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 1f
        };
    }

    public static float TotalWidth => DropdownWidth + Spacing + SaveButtonWidth;
    public static float TotalHeight => Height;

    /// <summary>
    /// Renders the preset bar at the specified position.
    /// </summary>
    /// <param name="canvas">The canvas to render to.</param>
    /// <param name="x">Left edge X position.</param>
    /// <param name="y">Top edge Y position.</param>
    /// <param name="currentPresetName">The name of the currently selected preset.</param>
    public void Render(SKCanvas canvas, float x, float y, string currentPresetName)
    {
        // Dropdown
        _dropdownRect = new SKRect(x, y, x + DropdownWidth, y + Height);
        var dropdownRound = new SKRoundRect(_dropdownRect, CornerRadius);
        canvas.DrawRoundRect(dropdownRound, _backgroundPaint);
        canvas.DrawRoundRect(dropdownRound, _borderPaint);

        // Truncate preset name if needed
        string displayName = TruncateText(currentPresetName, DropdownWidth - 20f);
        canvas.DrawText(displayName, x + 6f, y + Height / 2f + 3.5f, _textPaint);

        // Dropdown arrow
        float arrowX = x + DropdownWidth - 12f;
        float arrowY = y + Height / 2f - 2f;
        using var arrowPath = new SKPath();
        arrowPath.MoveTo(arrowX - 3f, arrowY);
        arrowPath.LineTo(arrowX + 3f, arrowY);
        arrowPath.LineTo(arrowX, arrowY + 4f);
        arrowPath.Close();
        canvas.DrawPath(arrowPath, _arrowPaint);

        // Save button
        float saveX = x + DropdownWidth + Spacing;
        _saveButtonRect = new SKRect(saveX, y, saveX + SaveButtonWidth, y + Height);
        var saveRound = new SKRoundRect(_saveButtonRect, CornerRadius);
        canvas.DrawRoundRect(saveRound, _backgroundPaint);
        canvas.DrawRoundRect(saveRound, _borderPaint);

        // Save icon (floppy disk)
        float iconCx = _saveButtonRect.MidX;
        float iconCy = _saveButtonRect.MidY;
        canvas.DrawRect(new SKRect(iconCx - 5f, iconCy - 5f, iconCx + 5f, iconCy + 5f), _iconPaint);

        using var fillPaint = new SKPaint
        {
            Color = _theme.TextSecondary,
            IsAntialias = true,
            Style = SKPaintStyle.Fill
        };
        canvas.DrawRect(new SKRect(iconCx - 2.5f, iconCy - 5f, iconCx + 2.5f, iconCy - 1f), fillPaint);
    }

    private string TruncateText(string text, float maxWidth)
    {
        if (_textPaint.MeasureText(text) <= maxWidth)
        {
            return text;
        }

        const string ellipsis = "..";
        float available = maxWidth - _textPaint.MeasureText(ellipsis);
        int len = text.Length;
        while (len > 0 && _textPaint.MeasureText(text.AsSpan(0, len).ToString()) > available)
        {
            len--;
        }
        return len > 0 ? $"{text[..len]}{ellipsis}" : ellipsis;
    }

    /// <summary>
    /// Hit tests the preset bar controls.
    /// </summary>
    /// <returns>The hit area, or None if not hit.</returns>
    public PresetBarHitArea HitTest(float x, float y)
    {
        if (_dropdownRect.Contains(x, y))
            return PresetBarHitArea.Dropdown;

        if (_saveButtonRect.Contains(x, y))
            return PresetBarHitArea.SaveButton;

        return PresetBarHitArea.None;
    }

    public SKRect GetDropdownRect() => _dropdownRect;

    public void Dispose()
    {
        _backgroundPaint.Dispose();
        _borderPaint.Dispose();
        _textPaint.Dispose();
        _arrowPaint.Dispose();
        _iconPaint.Dispose();
    }
}

public enum PresetBarHitArea
{
    None,
    Dropdown,
    SaveButton
}
