using HotMic.App.UI;
using SkiaSharp;

namespace HotMic.App.UI.PluginComponents;

/// <summary>
/// Manages hover tooltips with configurable delay for SkiaSharp-rendered UI.
/// </summary>
public sealed class TooltipManager : IDisposable
{
    private const float DelayMs = 300f;
    private const float MaxWidth = 280f;
    private const float Padding = 10f;
    private const float CornerRadius = 6f;
    private const float TitleFontSize = 12f;
    private const float DescFontSize = 11f;
    private const float LineSpacing = 4f;

    private readonly SKPaint _backgroundPaint;
    private readonly SKPaint _borderPaint;
    private readonly SkiaTextPaint _titlePaint;
    private readonly SkiaTextPaint _descPaint;

    private string? _controlId;
    private string? _title;
    private string? _description;
    private SKPoint _anchor;
    private DateTime _hoverStart;

    public TooltipManager(PluginComponentTheme? theme = null)
    {
        var t = theme ?? PluginComponentTheme.Default;

        _backgroundPaint = new SKPaint
        {
            Style = SKPaintStyle.Fill,
            Color = new SKColor(20, 22, 28, 245),
            IsAntialias = true
        };

        _borderPaint = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            Color = new SKColor(60, 65, 75, 200),
            StrokeWidth = 1f,
            IsAntialias = true
        };

        _titlePaint = new SkiaTextPaint(
            SKColors.White,
            TitleFontSize,
            SKFontStyle.Bold);

        _descPaint = new SkiaTextPaint(
            new SKColor(180, 185, 195),
            DescFontSize);
    }

    /// <summary>
    /// Current control ID being hovered.
    /// </summary>
    public string? ControlId => _controlId;

    /// <summary>
    /// Whether the tooltip should be visible (delay has elapsed).
    /// </summary>
    public bool IsVisible => _controlId != null &&
        (DateTime.UtcNow - _hoverStart).TotalMilliseconds >= DelayMs;

    /// <summary>
    /// Begin hover tracking for a control.
    /// </summary>
    public void StartHover(string controlId, string title, string description, SKPoint anchor)
    {
        if (_controlId == controlId)
        {
            // Same control - update position only
            _anchor = anchor;
            return;
        }

        _controlId = controlId;
        _title = title;
        _description = description;
        _anchor = anchor;
        _hoverStart = DateTime.UtcNow;
    }

    /// <summary>
    /// End hover tracking.
    /// </summary>
    public void EndHover()
    {
        _controlId = null;
        _title = null;
        _description = null;
    }

    /// <summary>
    /// Render the tooltip if visible.
    /// </summary>
    public void Render(SKCanvas canvas, SKSize bounds)
    {
        if (!IsVisible || _title == null) return;

        // Measure text
        float titleWidth = _titlePaint.MeasureText(_title);
        float descWidth = _description != null ? MeasureWrappedWidth(_description) : 0;
        float contentWidth = Math.Max(titleWidth, Math.Min(descWidth, MaxWidth - Padding * 2));
        float boxWidth = contentWidth + Padding * 2;

        // Wrap description text
        var descLines = _description != null ? WrapText(_description, contentWidth) : Array.Empty<string>();
        float descHeight = descLines.Length * (DescFontSize + LineSpacing);

        float boxHeight = Padding + TitleFontSize + LineSpacing + descHeight + Padding;

        // Position tooltip above anchor, avoiding edges
        float x = _anchor.X - boxWidth / 2;
        float y = _anchor.Y - boxHeight - 12f;

        // Clamp to screen bounds
        if (x < Padding) x = Padding;
        if (x + boxWidth > bounds.Width - Padding) x = bounds.Width - Padding - boxWidth;
        if (y < Padding)
        {
            // Show below anchor instead
            y = _anchor.Y + 20f;
        }
        if (y + boxHeight > bounds.Height - Padding) y = bounds.Height - Padding - boxHeight;

        var rect = new SKRect(x, y, x + boxWidth, y + boxHeight);
        var rrect = new SKRoundRect(rect, CornerRadius);

        // Draw background and border
        canvas.DrawRoundRect(rrect, _backgroundPaint);
        canvas.DrawRoundRect(rrect, _borderPaint);

        // Draw title
        float textX = x + Padding;
        float textY = y + Padding + TitleFontSize - 2f;
        _titlePaint.DrawText(canvas, _title, textX, textY);

        // Draw description lines
        textY += LineSpacing + 2f;
        foreach (var line in descLines)
        {
            textY += DescFontSize + LineSpacing;
            _descPaint.DrawText(canvas, line, textX, textY - LineSpacing);
        }
    }

    private float MeasureWrappedWidth(string text)
    {
        // Estimate width needed
        return _descPaint.MeasureText(text);
    }

    private string[] WrapText(string text, float maxWidth)
    {
        if (string.IsNullOrEmpty(text)) return Array.Empty<string>();

        var words = text.Split(' ');
        var lines = new List<string>();
        var currentLine = "";

        foreach (var word in words)
        {
            var testLine = string.IsNullOrEmpty(currentLine) ? word : currentLine + " " + word;
            if (_descPaint.MeasureText(testLine) <= maxWidth)
            {
                currentLine = testLine;
            }
            else
            {
                if (!string.IsNullOrEmpty(currentLine))
                    lines.Add(currentLine);
                currentLine = word;
            }
        }

        if (!string.IsNullOrEmpty(currentLine))
            lines.Add(currentLine);

        return lines.ToArray();
    }

    public void Dispose()
    {
        _backgroundPaint.Dispose();
        _borderPaint.Dispose();
        _titlePaint.Dispose();
        _descPaint.Dispose();
    }
}
