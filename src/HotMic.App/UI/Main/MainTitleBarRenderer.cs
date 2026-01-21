using System;
using SkiaSharp;
using HotMic.App.ViewModels;

namespace HotMic.App.UI;

internal sealed class MainTitleBarRenderer
{
    private readonly MainPaintCache _paints;
    private readonly MainRenderPrimitives _primitives;
    private readonly MainHitTargetRegistry _hitTargets;

    public MainTitleBarRenderer(MainPaintCache paints, MainRenderPrimitives primitives, MainHitTargetRegistry hitTargets)
    {
        _paints = paints;
        _primitives = primitives;
        _hitTargets = hitTargets;
    }

    public void Render(SKCanvas canvas, MainLayoutFrame layout, MainViewModel viewModel)
    {
        _hitTargets.TitleBarRect = layout.TitleBarRect;

        using var clip = new SKPath();
        clip.AddRoundRect(new SKRoundRect(new SKRect(0, 0, layout.Size.Width, MainLayoutMetrics.TitleBarHeight + MainLayoutMetrics.CornerRadius), MainLayoutMetrics.CornerRadius));
        clip.AddRect(new SKRect(0, MainLayoutMetrics.TitleBarHeight, layout.Size.Width, MainLayoutMetrics.TitleBarHeight + MainLayoutMetrics.CornerRadius));
        canvas.Save();
        canvas.ClipPath(clip);
        canvas.DrawRect(layout.TitleBarRect, _paints.TitleBarPaint);
        canvas.Restore();

        canvas.DrawLine(0, MainLayoutMetrics.TitleBarHeight, layout.Size.Width, MainLayoutMetrics.TitleBarHeight, _paints.BorderPaint);
        canvas.DrawText("HotMic", MainLayoutMetrics.Padding, MainLayoutMetrics.TitleBarHeight / 2f + 4f, _paints.TitlePaint);

        if (!string.IsNullOrWhiteSpace(viewModel.StatusMessage))
        {
            var statusPaint = MainRenderPrimitives.CreateTextPaint(_paints.Theme.Accent, 10f);
            canvas.DrawText(viewModel.StatusMessage, 70f, MainLayoutMetrics.TitleBarHeight / 2f + 3f, statusPaint);
        }

        DrawTopButtons(canvas, layout, viewModel);
    }

    private void DrawTopButtons(SKCanvas canvas, MainLayoutFrame layout, MainViewModel viewModel)
    {
        float right = layout.Size.Width - MainLayoutMetrics.Padding;
        float centerY = MainLayoutMetrics.TitleBarHeight / 2f;
        float buttonSize = 20f;
        float spacing = 4f;

        var closeRect = new SKRect(right - buttonSize, centerY - buttonSize / 2f, right, centerY + buttonSize / 2f);
        DrawIconButton(canvas, closeRect, MainButton.Close, false, IconType.Close);
        right -= buttonSize + spacing;

        var minRect = new SKRect(right - buttonSize, centerY - buttonSize / 2f, right, centerY + buttonSize / 2f);
        DrawIconButton(canvas, minRect, MainButton.Minimize, false, IconType.Minimize);
        right -= buttonSize + spacing;

        var pinRect = new SKRect(right - buttonSize, centerY - buttonSize / 2f, right, centerY + buttonSize / 2f);
        DrawIconButton(canvas, pinRect, MainButton.Pin, viewModel.AlwaysOnTop, IconType.Pin);
        right -= buttonSize + spacing;

        var settingsRect = new SKRect(right - buttonSize, centerY - buttonSize / 2f, right, centerY + buttonSize / 2f);
        DrawIconButton(canvas, settingsRect, MainButton.Settings, false, IconType.Settings);
        right -= buttonSize + spacing + 6f;

        string viewLabel = viewModel.IsMinimalView ? "Expand" : "Compact";
        float toggleWidth = 50f;
        var toggleRect = new SKRect(right - toggleWidth, centerY - buttonSize / 2f, right, centerY + buttonSize / 2f);
        DrawTextButton(canvas, toggleRect, viewLabel, MainButton.ToggleView);
    }

    private void DrawIconButton(SKCanvas canvas, SKRect rect, MainButton button, bool isActive, IconType icon)
    {
        var roundRect = new SKRoundRect(rect, 3f);
        canvas.DrawRoundRect(roundRect, isActive ? _paints.AccentPaint : _paints.ButtonPaint);

        float cx = rect.MidX;
        float cy = rect.MidY;
        float s = 4f;

        switch (icon)
        {
            case IconType.Close:
                canvas.DrawLine(cx - s, cy - s, cx + s, cy + s, _paints.IconPaint);
                canvas.DrawLine(cx + s, cy - s, cx - s, cy + s, _paints.IconPaint);
                break;
            case IconType.Minimize:
                canvas.DrawLine(cx - s, cy + 2f, cx + s, cy + 2f, _paints.IconPaint);
                break;
            case IconType.Pin:
                canvas.DrawCircle(cx, cy - 1f, 2.5f, _paints.IconPaint);
                canvas.DrawLine(cx, cy + 1f, cx, cy + 4f, _paints.IconPaint);
                break;
            case IconType.Settings:
                canvas.DrawCircle(cx, cy, 2.5f, _paints.IconPaint);
                for (int i = 0; i < 6; i++)
                {
                    float angle = i * 60f * MathF.PI / 180f;
                    canvas.DrawLine(cx + MathF.Cos(angle) * 3f, cy + MathF.Sin(angle) * 3f,
                        cx + MathF.Cos(angle) * 5f, cy + MathF.Sin(angle) * 5f, _paints.IconPaint);
                }
                break;
        }

        _hitTargets.TopButtons[button] = rect;
    }

    private void DrawTextButton(SKCanvas canvas, SKRect rect, string text, MainButton button)
    {
        var roundRect = new SKRoundRect(rect, 3f);
        canvas.DrawRoundRect(roundRect, _paints.ButtonPaint);
        canvas.DrawRoundRect(roundRect, _paints.BorderPaint);
        canvas.DrawText(text, rect.MidX, rect.MidY + 3f, MainRenderPrimitives.CreateCenteredTextPaint(_paints.Theme.TextSecondary, 9f));
        _hitTargets.TopButtons[button] = rect;
    }

    private enum IconType { Close, Minimize, Pin, Settings }
}
