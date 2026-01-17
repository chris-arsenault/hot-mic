using System;
using System.Collections.Generic;
using HotMic.App.Views;
using SkiaSharp;

namespace HotMic.App.UI.PluginComponents;

/// <summary>
/// Base class for visualizer renderers providing common UI components.
/// Follows the VocalSpectrographRenderer patterns for consistency.
/// </summary>
public abstract class BaseVisualizerRenderer : IBaseVisualizerRenderer
{
    // Layout constants
    protected const float TitleBarHeight = 32f;
    protected const float PanelPadding = 10f;
    protected const float ButtonHeight = 24f;
    protected const float ButtonSpacing = 6f;
    protected const float KnobSize = 40f;
    protected const float SmallKnobSize = 32f;
    protected const float ToggleWidth = 50f;
    protected const float ToggleHeight = 22f;

    // Bloom settings
    protected const float BloomSigma = 12f;
    protected const float BloomIntensity = 0.35f;

    protected readonly PluginComponentTheme Theme;
    protected readonly List<KnobWidget> Knobs = new();

    // Common paints
    protected readonly SKPaint BackgroundPaint;
    protected readonly SKPaint TitleBarPaint;
    protected readonly SKPaint PanelPaint;
    protected readonly SKPaint BorderPaint;
    protected readonly SKPaint ButtonPaint;
    protected readonly SKPaint ButtonActivePaint;
    protected readonly SKPaint BloomPaint;
    protected readonly SkiaTextPaint TitleTextPaint;
    protected readonly SkiaTextPaint LabelTextPaint;
    protected readonly SkiaTextPaint ValueTextPaint;
    protected readonly SkiaTextPaint SmallTextPaint;

    // Hit test rects
    protected SKRect CloseButtonRect;
    protected SKRect TitleBarRect;

    private bool _disposed;

    protected BaseVisualizerRenderer(PluginComponentTheme? theme = null)
    {
        Theme = theme ?? PluginComponentTheme.BlueOnBlack;

        BackgroundPaint = new SKPaint
        {
            Color = Theme.PanelBackground,
            IsAntialias = true,
            Style = SKPaintStyle.Fill
        };

        TitleBarPaint = new SKPaint
        {
            Color = Theme.PanelBackgroundLight,
            IsAntialias = true,
            Style = SKPaintStyle.Fill
        };

        PanelPaint = new SKPaint
        {
            Color = Theme.PanelBackgroundLight,
            IsAntialias = true,
            Style = SKPaintStyle.Fill
        };

        BorderPaint = new SKPaint
        {
            Color = Theme.Border,
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 1f
        };

        ButtonPaint = new SKPaint
        {
            Color = Theme.PanelBackgroundLight,
            IsAntialias = true,
            Style = SKPaintStyle.Fill
        };

        ButtonActivePaint = new SKPaint
        {
            Color = Theme.KnobArc.WithAlpha(120),
            IsAntialias = true,
            Style = SKPaintStyle.Fill
        };

        // Bloom paint for glow effects
        BloomPaint = new SKPaint
        {
            IsAntialias = true,
            BlendMode = SKBlendMode.Plus,
            ImageFilter = SKImageFilter.CreateBlur(BloomSigma, BloomSigma),
            ColorFilter = SKColorFilter.CreateBlendMode(
                new SKColor(255, 255, 255, (byte)(255 * BloomIntensity)),
                SKBlendMode.Modulate)
        };

        TitleTextPaint = new SkiaTextPaint(Theme.TextPrimary, 14f, SKFontStyle.Bold);
        LabelTextPaint = new SkiaTextPaint(Theme.TextSecondary, 11f);
        ValueTextPaint = new SkiaTextPaint(Theme.TextPrimary, 12f);
        SmallTextPaint = new SkiaTextPaint(Theme.TextMuted, 10f);
    }

    public IReadOnlyList<KnobWidget> AllKnobs => Knobs;

    public bool HitTestCloseButton(float x, float y) => CloseButtonRect.Contains(x, y);
    public bool HitTestTitleBar(float x, float y) => TitleBarRect.Contains(x, y) && !CloseButtonRect.Contains(x, y);

    public virtual string? GetTooltipText(float x, float y) => null;

    /// <summary>
    /// Main render method. Call from OnRender in the window.
    /// </summary>
    public void Render(SKCanvas canvas, SKSize size)
    {
        canvas.Clear(Theme.PanelBackground);
        DrawTitleBar(canvas, size);
        OnRender(canvas, size);
    }

    protected abstract void OnRender(SKCanvas canvas, SKSize size);

    #region Common Drawing Methods

    protected void DrawTitleBar(SKCanvas canvas, SKSize size)
    {
        TitleBarRect = new SKRect(0, 0, size.Width, TitleBarHeight);
        canvas.DrawRect(TitleBarRect, TitleBarPaint);

        // Title text
        TitleTextPaint.DrawText(canvas, GetTitle(), PanelPadding, TitleBarHeight / 2 + 5f);

        // Close button
        float closeSize = 20f;
        float closeX = size.Width - closeSize - 8f;
        float closeY = (TitleBarHeight - closeSize) / 2f;
        CloseButtonRect = new SKRect(closeX, closeY, closeX + closeSize, closeY + closeSize);

        DrawCloseButton(canvas, CloseButtonRect);

        // Bottom border
        canvas.DrawLine(0, TitleBarHeight, size.Width, TitleBarHeight, BorderPaint);
    }

    protected virtual string GetTitle() => "Visualizer";

    protected void DrawCloseButton(SKCanvas canvas, SKRect rect)
    {
        var roundRect = new SKRoundRect(rect, 4f);
        canvas.DrawRoundRect(roundRect, ButtonPaint);
        canvas.DrawRoundRect(roundRect, BorderPaint);

        // Draw X
        using var xPaint = new SKPaint
        {
            Color = Theme.TextSecondary,
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 2f,
            StrokeCap = SKStrokeCap.Round
        };

        float padding = 6f;
        canvas.DrawLine(rect.Left + padding, rect.Top + padding, rect.Right - padding, rect.Bottom - padding, xPaint);
        canvas.DrawLine(rect.Right - padding, rect.Top + padding, rect.Left + padding, rect.Bottom - padding, xPaint);
    }

    protected void DrawPanel(SKCanvas canvas, SKRect rect, string? header = null)
    {
        var roundRect = new SKRoundRect(rect, 6f);
        canvas.DrawRoundRect(roundRect, PanelPaint);
        canvas.DrawRoundRect(roundRect, BorderPaint);

        if (!string.IsNullOrEmpty(header))
        {
            LabelTextPaint.DrawText(canvas, header, rect.Left + PanelPadding, rect.Top + 16f);
        }
    }

    protected void DrawPillButton(SKCanvas canvas, SKRect rect, string label, bool active)
    {
        float radius = rect.Height / 2f;
        var roundRect = new SKRoundRect(rect, radius);
        canvas.DrawRoundRect(roundRect, active ? ButtonActivePaint : ButtonPaint);
        canvas.DrawRoundRect(roundRect, BorderPaint);

        using var textPaint = new SkiaTextPaint(
            active ? Theme.TextPrimary : Theme.TextSecondary,
            10f,
            active ? SKFontStyle.Bold : SKFontStyle.Normal,
            SKTextAlign.Center);
        textPaint.DrawText(canvas, label, rect.MidX, rect.MidY + 4f);
    }

    protected void DrawToggle(SKCanvas canvas, SKRect rect, string label, bool active)
    {
        var roundRect = new SKRoundRect(rect, 4f);
        canvas.DrawRoundRect(roundRect, active ? ButtonActivePaint : ButtonPaint);
        canvas.DrawRoundRect(roundRect, BorderPaint);

        using var textPaint = new SkiaTextPaint(
            active ? Theme.TextPrimary : Theme.TextSecondary,
            10f,
            SKFontStyle.Normal,
            SKTextAlign.Center);
        textPaint.DrawText(canvas, label, rect.MidX, rect.MidY + 4f);
    }

    protected void DrawVerticalMeter(SKCanvas canvas, SKRect rect, float normalizedValue, SKColor fillColor)
    {
        // Background
        using var bgPaint = new SKPaint
        {
            Color = new SKColor(30, 30, 35),
            IsAntialias = true,
            Style = SKPaintStyle.Fill
        };
        canvas.DrawRect(rect, bgPaint);

        // Fill
        float fillHeight = rect.Height * Math.Clamp(normalizedValue, 0f, 1f);
        var fillRect = new SKRect(rect.Left, rect.Bottom - fillHeight, rect.Right, rect.Bottom);
        using var fillPaint = new SKPaint
        {
            Color = fillColor,
            IsAntialias = true,
            Style = SKPaintStyle.Fill
        };
        canvas.DrawRect(fillRect, fillPaint);

        // Border
        canvas.DrawRect(rect, BorderPaint);
    }

    protected void DrawHorizontalMeter(SKCanvas canvas, SKRect rect, float normalizedValue, SKColor fillColor)
    {
        // Background
        using var bgPaint = new SKPaint
        {
            Color = new SKColor(30, 30, 35),
            IsAntialias = true,
            Style = SKPaintStyle.Fill
        };
        canvas.DrawRect(rect, bgPaint);

        // Fill
        float fillWidth = rect.Width * Math.Clamp(normalizedValue, 0f, 1f);
        var fillRect = new SKRect(rect.Left, rect.Top, rect.Left + fillWidth, rect.Bottom);
        using var fillPaint = new SKPaint
        {
            Color = fillColor,
            IsAntialias = true,
            Style = SKPaintStyle.Fill
        };
        canvas.DrawRect(fillRect, fillPaint);

        // Border
        canvas.DrawRect(rect, BorderPaint);
    }

    protected void DrawLabel(SKCanvas canvas, string text, float x, float y)
    {
        LabelTextPaint.DrawText(canvas, text, x, y);
    }

    protected void DrawValue(SKCanvas canvas, string text, float x, float y)
    {
        ValueTextPaint.DrawText(canvas, text, x, y);
    }

    protected KnobWidget CreateKnob(
        string label,
        float defaultValue,
        float minValue,
        float maxValue,
        string unit = "",
        bool isLogarithmic = false,
        string? format = null)
    {
        var knob = new KnobWidget(
            defaultValue,
            minValue,
            maxValue,
            SmallKnobSize / 2f,
            unit,
            isLogarithmic,
            format ?? "{0:0.0}",
            Theme);
        knob.Label = label;
        Knobs.Add(knob);
        return knob;
    }

    #endregion

    #region Ballistics Helpers

    protected static float ApplyBallistics(float current, float target, float attackCoeff, float releaseCoeff)
    {
        float coeff = target > current ? attackCoeff : releaseCoeff;
        return current + (target - current) * coeff;
    }

    protected static float LinearToDb(float linear)
    {
        if (linear <= 0f) return -100f;
        return 20f * MathF.Log10(linear);
    }

    protected static float DbToLinear(float db)
    {
        return MathF.Pow(10f, db / 20f);
    }

    #endregion

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (_disposed) return;

        if (disposing)
        {
            BackgroundPaint.Dispose();
            TitleBarPaint.Dispose();
            PanelPaint.Dispose();
            BorderPaint.Dispose();
            ButtonPaint.Dispose();
            ButtonActivePaint.Dispose();
            BloomPaint.Dispose();
            TitleTextPaint.Dispose();
            LabelTextPaint.Dispose();
            ValueTextPaint.Dispose();
            SmallTextPaint.Dispose();

            foreach (var knob in Knobs)
            {
                knob.Dispose();
            }
            Knobs.Clear();
        }

        _disposed = true;
    }
}
