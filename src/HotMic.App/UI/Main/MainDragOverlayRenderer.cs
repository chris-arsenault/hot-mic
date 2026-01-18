using SkiaSharp;

namespace HotMic.App.UI;

/// <summary>
/// Renders drag and drop visual feedback overlay including:
/// - Source slot dimming with dashed outline
/// - Ghost element following cursor
/// - Drop target highlighting with insertion line
/// </summary>
internal sealed class MainDragOverlayRenderer
{
    private static readonly float[] DashPattern = [4f, 3f];

    private readonly HotMicTheme _theme = HotMicTheme.Default;

    // Reusable paints
    private readonly SKPaint _sourceDimPaint;
    private readonly SKPaint _sourceDashedBorderPaint;
    private readonly SKPaint _ghostFillPaint;
    private readonly SKPaint _ghostBorderValidPaint;
    private readonly SKPaint _ghostBorderInvalidPaint;
    private readonly SKPaint _targetGlowPaint;
    private readonly SKPaint _insertLinePaint;
    private readonly SKPaint _insertCaretPaint;
    private readonly SkiaTextPaint _ghostTextPaint;

    public MainDragOverlayRenderer()
    {
        // Source slot dimming overlay (70% opaque background)
        _sourceDimPaint = new SKPaint
        {
            Color = _theme.BackgroundPrimary.WithAlpha(180),
            IsAntialias = true,
            Style = SKPaintStyle.Fill
        };

        // Source slot dashed border
        _sourceDashedBorderPaint = new SKPaint
        {
            Color = _theme.TextMuted,
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 1.5f,
            PathEffect = SKPathEffect.CreateDash(DashPattern, 0)
        };

        // Ghost background (60% opaque)
        _ghostFillPaint = new SKPaint
        {
            Color = _theme.PluginSlotFilled.WithAlpha(153),
            IsAntialias = true,
            Style = SKPaintStyle.Fill
        };

        // Ghost border for valid drop (accent orange)
        _ghostBorderValidPaint = new SKPaint
        {
            Color = _theme.Accent,
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 2f
        };

        // Ghost border for invalid drop (mute red)
        _ghostBorderInvalidPaint = new SKPaint
        {
            Color = _theme.Mute,
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 2f
        };

        // Target slot glow (15% opaque accent)
        _targetGlowPaint = new SKPaint
        {
            Color = _theme.Accent.WithAlpha(38),
            IsAntialias = true,
            Style = SKPaintStyle.Fill
        };

        // Insertion line
        _insertLinePaint = new SKPaint
        {
            Color = _theme.Accent,
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 2f
        };

        // Insertion caret (triangle)
        _insertCaretPaint = new SKPaint
        {
            Color = _theme.Accent,
            IsAntialias = true,
            Style = SKPaintStyle.Fill
        };

        _ghostTextPaint = new SkiaTextPaint(_theme.TextPrimary, 9f, (SKFontStyle?)null, SKTextAlign.Center);
    }

    public void Render(SKCanvas canvas, MainUiState uiState)
    {
        // Determine drag type and extract common info
        SKRect sourceRect;
        string displayName;
        float currentX, currentY;
        bool isDragging;
        bool isValid;

        if (uiState.PluginDrag is { IsDragging: true } pluginDrag)
        {
            sourceRect = pluginDrag.SourceRect;
            displayName = pluginDrag.DisplayName;
            currentX = pluginDrag.CurrentX;
            currentY = pluginDrag.CurrentY;
            isDragging = true;
            isValid = uiState.CurrentDropTarget?.IsValid ?? false;
        }
        else if (uiState.ContainerDrag is { IsDragging: true } containerDrag)
        {
            sourceRect = containerDrag.SourceRect;
            displayName = containerDrag.DisplayName;
            currentX = containerDrag.CurrentX;
            currentY = containerDrag.CurrentY;
            isDragging = true;
            isValid = uiState.CurrentDropTarget?.IsValid ?? false;
        }
        else
        {
            return;
        }

        if (!isDragging || sourceRect.IsEmpty)
        {
            return;
        }

        // 1. Draw source slot marker (dimmed with dashed outline)
        DrawSourceMarker(canvas, sourceRect);

        // 2. Draw drop target feedback if present
        if (uiState.CurrentDropTarget is { } dropTarget)
        {
            DrawDropTarget(canvas, dropTarget);
        }

        // 3. Draw ghost at cursor position
        DrawGhost(canvas, sourceRect, displayName, currentX, currentY, isValid);
    }

    private void DrawSourceMarker(SKCanvas canvas, SKRect sourceRect)
    {
        // Dim overlay
        var roundRect = new SKRoundRect(sourceRect, 3f);
        canvas.DrawRoundRect(roundRect, _sourceDimPaint);

        // Dashed border
        canvas.DrawRoundRect(roundRect, _sourceDashedBorderPaint);
    }

    private void DrawDropTarget(SKCanvas canvas, DropTarget target)
    {
        if (!target.IsValid)
        {
            return;
        }

        // Draw glow on target slot
        if (!target.TargetRect.IsEmpty)
        {
            var targetRound = new SKRoundRect(target.TargetRect, 3f);
            canvas.DrawRoundRect(targetRound, _targetGlowPaint);
        }

        // Draw insertion line with carets
        if (target.InsertLineTop < target.InsertLineBottom)
        {
            float lineX = target.InsertLineX;
            float lineTop = target.InsertLineTop;
            float lineBottom = target.InsertLineBottom;

            // Vertical line
            canvas.DrawLine(lineX, lineTop, lineX, lineBottom, _insertLinePaint);

            // Top caret (triangle pointing down)
            float caretSize = 5f;
            using var topCaret = new SKPath();
            topCaret.MoveTo(lineX - caretSize, lineTop);
            topCaret.LineTo(lineX + caretSize, lineTop);
            topCaret.LineTo(lineX, lineTop + caretSize);
            topCaret.Close();
            canvas.DrawPath(topCaret, _insertCaretPaint);

            // Bottom caret (triangle pointing up)
            using var bottomCaret = new SKPath();
            bottomCaret.MoveTo(lineX - caretSize, lineBottom);
            bottomCaret.LineTo(lineX + caretSize, lineBottom);
            bottomCaret.LineTo(lineX, lineBottom - caretSize);
            bottomCaret.Close();
            canvas.DrawPath(bottomCaret, _insertCaretPaint);
        }
    }

    private void DrawGhost(SKCanvas canvas, SKRect sourceRect, string displayName, float cursorX, float cursorY, bool isValid)
    {
        // Position ghost centered on cursor, slightly offset
        float ghostWidth = sourceRect.Width;
        float ghostHeight = sourceRect.Height;
        float ghostX = cursorX - ghostWidth / 2f;
        float ghostY = cursorY - 10f; // Slight upward offset so cursor is visible

        var ghostRect = new SKRect(ghostX, ghostY, ghostX + ghostWidth, ghostY + ghostHeight);
        var ghostRound = new SKRoundRect(ghostRect, 3f);

        // Background
        canvas.DrawRoundRect(ghostRound, _ghostFillPaint);

        // Border based on validity
        var borderPaint = isValid ? _ghostBorderValidPaint : _ghostBorderInvalidPaint;
        canvas.DrawRoundRect(ghostRound, borderPaint);

        // Display name centered in ghost
        if (!string.IsNullOrEmpty(displayName))
        {
            float textY = ghostRect.MidY + 3f;
            canvas.DrawText(displayName, ghostRect.MidX, textY, _ghostTextPaint);
        }
    }
}
