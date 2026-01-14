using HotMic.Core.Plugins.BuiltIn;
using SkiaSharp;

namespace HotMic.App.UI.PluginComponents;

/// <summary>
/// Renderer for the Signal Generator plugin UI with 3 slots, master section, and sample controls.
/// </summary>
public sealed class SignalGeneratorRenderer : IDisposable
{
    private const float TitleBarHeight = 40f;
    private const float Padding = 8f;
    private const float SlotHeight = 120f;
    private const float SlotWidth = 130f;
    private const float MasterWidth = 80f;
    private const float CornerRadius = 10f;
    private const float KnobRadius = 22f;
    private const float SmallKnobRadius = 16f;

    private readonly PluginComponentTheme _theme;
    private readonly PluginPresetBar _presetBar;

    private readonly SKPaint _backgroundPaint;
    private readonly SKPaint _titleBarPaint;
    private readonly SKPaint _borderPaint;
    private readonly SKPaint _titlePaint;
    private readonly SKPaint _closeButtonPaint;
    private readonly SKPaint _slotBackgroundPaint;
    private readonly SKPaint _labelPaint;
    private readonly SKPaint _valuePaint;
    private readonly SKPaint _mutePaint;
    private readonly SKPaint _mutedPaint;
    private readonly SKPaint _soloPaint;
    private readonly SKPaint _soloedPaint;
    private readonly SKPaint _bypassPaint;
    private readonly SKPaint _bypassActivePaint;
    private readonly SKPaint _knobBackgroundPaint;
    private readonly SKPaint _knobTrackPaint;
    private readonly SKPaint _knobArcPaint;
    private readonly SKPaint _knobPointerPaint;
    private readonly SKPaint _typeSelectorPaint;
    private readonly SKPaint _recordPaint;
    private readonly SKPaint _recordActivePaint;

    private readonly LevelMeter[] _slotMeters;
    private readonly LevelMeter _masterMeter;

    // Hit test regions
    private SKRect _titleBarRect;
    private SKRect _closeButtonRect;
    private SKRect _bypassButtonRect;
    private readonly SKRect[] _slotRects = new SKRect[3];
    private readonly SKRect[] _slotTypeSelectorRects = new SKRect[3];
    private readonly SKPoint[] _slotGainKnobCenters = new SKPoint[3];
    private readonly SKPoint[] _slotFreqKnobCenters = new SKPoint[3];
    private readonly SKRect[] _slotMuteRects = new SKRect[3];
    private readonly SKRect[] _slotSoloRects = new SKRect[3];
    private SKPoint _masterGainKnobCenter;
    private SKRect _recordButtonRect;

    public SignalGeneratorRenderer(PluginComponentTheme? theme = null)
    {
        _theme = theme ?? PluginComponentTheme.Default;
        _presetBar = new PluginPresetBar(_theme);

        _backgroundPaint = new SKPaint { Color = _theme.PanelBackground, IsAntialias = true, Style = SKPaintStyle.Fill };
        _titleBarPaint = new SKPaint { Color = _theme.PanelBackgroundLight, IsAntialias = true, Style = SKPaintStyle.Fill };
        _borderPaint = new SKPaint { Color = _theme.PanelBorder, IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1f };
        _titlePaint = new SKPaint { Color = _theme.TextPrimary, IsAntialias = true, TextSize = 14f, Typeface = SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Bold) };
        _closeButtonPaint = new SKPaint { Color = _theme.TextSecondary, IsAntialias = true, TextSize = 18f, TextAlign = SKTextAlign.Center };
        _slotBackgroundPaint = new SKPaint { Color = _theme.MeterBackground, IsAntialias = true, Style = SKPaintStyle.Fill };
        _labelPaint = new SKPaint { Color = _theme.TextSecondary, IsAntialias = true, TextSize = 10f, TextAlign = SKTextAlign.Center, Typeface = SKTypeface.FromFamilyName("Segoe UI") };
        _valuePaint = new SKPaint { Color = _theme.TextPrimary, IsAntialias = true, TextSize = 11f, TextAlign = SKTextAlign.Center, Typeface = SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Bold) };
        _mutePaint = new SKPaint { Color = _theme.PanelBackgroundLight, IsAntialias = true, Style = SKPaintStyle.Fill };
        _mutedPaint = new SKPaint { Color = new SKColor(0xFF, 0x50, 0x50), IsAntialias = true, Style = SKPaintStyle.Fill };
        _soloPaint = new SKPaint { Color = _theme.PanelBackgroundLight, IsAntialias = true, Style = SKPaintStyle.Fill };
        _soloedPaint = new SKPaint { Color = new SKColor(0xFF, 0xD7, 0x00), IsAntialias = true, Style = SKPaintStyle.Fill };
        _bypassPaint = new SKPaint { Color = _theme.PanelBackgroundLight, IsAntialias = true, Style = SKPaintStyle.Fill };
        _bypassActivePaint = new SKPaint { Color = new SKColor(0xFF, 0x50, 0x50), IsAntialias = true, Style = SKPaintStyle.Fill };
        _knobBackgroundPaint = new SKPaint { Color = _theme.KnobBackground, IsAntialias = true, Style = SKPaintStyle.Fill };
        _knobTrackPaint = new SKPaint { Color = _theme.KnobTrack, IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 4f, StrokeCap = SKStrokeCap.Round };
        _knobArcPaint = new SKPaint { Color = _theme.KnobArc, IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 4f, StrokeCap = SKStrokeCap.Round };
        _knobPointerPaint = new SKPaint { Color = _theme.KnobPointer, IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 2f, StrokeCap = SKStrokeCap.Round };
        _typeSelectorPaint = new SKPaint { Color = _theme.PanelBackgroundLight, IsAntialias = true, Style = SKPaintStyle.Fill };
        _recordPaint = new SKPaint { Color = _theme.PanelBackgroundLight, IsAntialias = true, Style = SKPaintStyle.Fill };
        _recordActivePaint = new SKPaint { Color = new SKColor(0xFF, 0x30, 0x30), IsAntialias = true, Style = SKPaintStyle.Fill };

        _slotMeters = new LevelMeter[3];
        for (int i = 0; i < 3; i++)
        {
            _slotMeters[i] = new LevelMeter();
        }
        _masterMeter = new LevelMeter();
    }

    public void Render(SKCanvas canvas, SKSize size, float dpiScale, SignalGeneratorState state)
    {
        canvas.Clear(SKColors.Transparent);
        canvas.Save();
        canvas.Scale(dpiScale);
        size = new SKSize(size.Width / dpiScale, size.Height / dpiScale);

        // Main background
        var backgroundRect = new SKRect(0, 0, size.Width, size.Height);
        var roundRect = new SKRoundRect(backgroundRect, CornerRadius);
        canvas.DrawRoundRect(roundRect, _backgroundPaint);

        // Title bar
        RenderTitleBar(canvas, size, state);

        // Content area
        float contentTop = TitleBarHeight + Padding;
        float contentHeight = size.Height - TitleBarHeight - Padding * 2;

        // Slots section (3 slots in a row)
        float slotsWidth = SlotWidth * 3 + Padding * 2;
        float slotStartX = Padding;

        for (int i = 0; i < 3; i++)
        {
            float slotX = slotStartX + i * (SlotWidth + Padding);
            float slotY = contentTop;
            _slotRects[i] = new SKRect(slotX, slotY, slotX + SlotWidth, slotY + SlotHeight);
            RenderSlot(canvas, i, _slotRects[i], state.Slots[i], state.HoveredSlot == i, state.HoveredArea);
        }

        // Master section (right side)
        float masterX = size.Width - MasterWidth - Padding;
        float masterY = contentTop;
        RenderMasterSection(canvas, masterX, masterY, MasterWidth, SlotHeight, state);

        // Record and sample load section (below slots)
        float controlsY = contentTop + SlotHeight + Padding;
        RenderControlsSection(canvas, slotStartX, controlsY, slotsWidth, 36f, state);

        // Outer border
        canvas.DrawRoundRect(roundRect, _borderPaint);
        canvas.Restore();
    }

    private void RenderTitleBar(SKCanvas canvas, SKSize size, SignalGeneratorState state)
    {
        _titleBarRect = new SKRect(0, 0, size.Width, TitleBarHeight);
        using (var titleClip = new SKPath())
        {
            titleClip.AddRoundRect(new SKRoundRect(_titleBarRect, CornerRadius, CornerRadius));
            titleClip.AddRect(new SKRect(0, CornerRadius, size.Width, TitleBarHeight));
            canvas.Save();
            canvas.ClipPath(titleClip);
            canvas.DrawRect(_titleBarRect, _titleBarPaint);
            canvas.Restore();
        }
        canvas.DrawLine(0, TitleBarHeight, size.Width, TitleBarHeight, _borderPaint);

        // Title
        canvas.DrawText("Signal Generator", Padding, TitleBarHeight / 2f + 5, _titlePaint);

        // Preset bar
        float presetBarX = 130f;
        float presetBarY = (TitleBarHeight - PluginPresetBar.TotalHeight) / 2f;
        _presetBar.Render(canvas, presetBarX, presetBarY, state.PresetName);

        // Bypass button
        float bypassWidth = 60f;
        _bypassButtonRect = new SKRect(
            size.Width - Padding - 30 - bypassWidth - 8,
            (TitleBarHeight - 24) / 2,
            size.Width - Padding - 30 - 8,
            (TitleBarHeight + 24) / 2);
        var bypassRound = new SKRoundRect(_bypassButtonRect, 4f);
        canvas.DrawRoundRect(bypassRound, state.IsBypassed ? _bypassActivePaint : _bypassPaint);
        canvas.DrawRoundRect(bypassRound, _borderPaint);

        using var bypassTextPaint = new SKPaint
        {
            Color = state.IsBypassed ? _theme.TextPrimary : _theme.TextSecondary,
            IsAntialias = true,
            TextSize = 10f,
            TextAlign = SKTextAlign.Center,
            Typeface = SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Bold)
        };
        canvas.DrawText("BYPASS", _bypassButtonRect.MidX, _bypassButtonRect.MidY + 4, bypassTextPaint);

        // Close button
        _closeButtonRect = new SKRect(size.Width - Padding - 24, (TitleBarHeight - 24) / 2,
            size.Width - Padding, (TitleBarHeight + 24) / 2);
        canvas.DrawText("\u00D7", _closeButtonRect.MidX, _closeButtonRect.MidY + 6, _closeButtonPaint);
    }

    private void RenderSlot(SKCanvas canvas, int slotIndex, SKRect rect, SlotRenderState slot, bool isHovered, SignalGeneratorHitArea hoveredArea)
    {
        // Slot background
        var slotRound = new SKRoundRect(rect, 6f);
        canvas.DrawRoundRect(slotRound, _slotBackgroundPaint);

        // Slot label at top
        canvas.DrawText($"SLOT {slotIndex + 1}", rect.MidX, rect.Top + 12, _labelPaint);

        // Type selector below label
        float headerY = rect.Top + 18;
        _slotTypeSelectorRects[slotIndex] = new SKRect(rect.Left + 6, headerY, rect.Right - 6, headerY + 20);
        var typeRound = new SKRoundRect(_slotTypeSelectorRects[slotIndex], 4f);
        canvas.DrawRoundRect(typeRound, _typeSelectorPaint);
        canvas.DrawRoundRect(typeRound, _borderPaint);

        string typeLabel = slot.Type.ToString();
        if (typeLabel.Length > 10) typeLabel = typeLabel[..10];
        canvas.DrawText(typeLabel, _slotTypeSelectorRects[slotIndex].MidX, _slotTypeSelectorRects[slotIndex].MidY + 4, _valuePaint);

        // Draw dropdown arrow
        float arrowX = _slotTypeSelectorRects[slotIndex].Right - 10;
        float arrowY = _slotTypeSelectorRects[slotIndex].MidY;
        using var arrowPath = new SKPath();
        arrowPath.MoveTo(arrowX - 3, arrowY - 2);
        arrowPath.LineTo(arrowX, arrowY + 2);
        arrowPath.LineTo(arrowX + 3, arrowY - 2);
        using var arrowPaint = new SKPaint { Color = _theme.TextSecondary, IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1.5f };
        canvas.DrawPath(arrowPath, arrowPaint);

        // Knobs - position depends on whether we show frequency
        float knobY = rect.Top + 62;
        float gainNormalized = (slot.GainDb + 60f) / 72f;
        bool gainHovered = isHovered && hoveredArea == SignalGeneratorHitArea.SlotGainKnob;

        // Only show frequency knob for oscillator types (Sine, Square, Saw, Triangle)
        bool showFreq = (int)slot.Type <= 3;

        if (showFreq)
        {
            // Two knobs: gain left, freq right
            float gainKnobX = rect.Left + 32;
            float freqKnobX = rect.Right - 32;

            _slotGainKnobCenters[slotIndex] = new SKPoint(gainKnobX, knobY);
            DrawSmallKnob(canvas, _slotGainKnobCenters[slotIndex], gainNormalized, gainHovered);
            canvas.DrawText("GAIN", gainKnobX, knobY + SmallKnobRadius + 10, _labelPaint);

            _slotFreqKnobCenters[slotIndex] = new SKPoint(freqKnobX, knobY);
            float freqNormalized = NormalizeFrequency(slot.Frequency);
            bool freqHovered = isHovered && hoveredArea == SignalGeneratorHitArea.SlotFreqKnob;
            DrawSmallKnob(canvas, _slotFreqKnobCenters[slotIndex], freqNormalized, freqHovered);
            canvas.DrawText("FREQ", freqKnobX, knobY + SmallKnobRadius + 10, _labelPaint);
        }
        else
        {
            // Single centered gain knob
            _slotGainKnobCenters[slotIndex] = new SKPoint(rect.MidX, knobY);
            _slotFreqKnobCenters[slotIndex] = new SKPoint(-100, -100); // Off-screen
            DrawSmallKnob(canvas, _slotGainKnobCenters[slotIndex], gainNormalized, gainHovered);
            canvas.DrawText("GAIN", rect.MidX, knobY + SmallKnobRadius + 10, _labelPaint);
        }

        // Mute/Solo buttons at bottom
        float buttonY = rect.Bottom - 24;
        float buttonWidth = 24f;
        float buttonHeight = 16f;
        float muteX = rect.Left + 16;
        float soloX = rect.Right - 16 - buttonWidth;

        _slotMuteRects[slotIndex] = new SKRect(muteX, buttonY, muteX + buttonWidth, buttonY + buttonHeight);
        var muteRound = new SKRoundRect(_slotMuteRects[slotIndex], 3f);
        canvas.DrawRoundRect(muteRound, slot.IsMuted ? _mutedPaint : _mutePaint);
        canvas.DrawRoundRect(muteRound, _borderPaint);
        using var muteTextPaint = new SKPaint { Color = slot.IsMuted ? _theme.TextPrimary : _theme.TextSecondary, IsAntialias = true, TextSize = 9f, TextAlign = SKTextAlign.Center };
        canvas.DrawText("M", _slotMuteRects[slotIndex].MidX, _slotMuteRects[slotIndex].MidY + 3, muteTextPaint);

        _slotSoloRects[slotIndex] = new SKRect(soloX, buttonY, soloX + buttonWidth, buttonY + buttonHeight);
        var soloRound = new SKRoundRect(_slotSoloRects[slotIndex], 3f);
        canvas.DrawRoundRect(soloRound, slot.IsSolo ? _soloedPaint : _soloPaint);
        canvas.DrawRoundRect(soloRound, _borderPaint);
        using var soloTextPaint = new SKPaint { Color = slot.IsSolo ? _theme.PanelBackground : _theme.TextSecondary, IsAntialias = true, TextSize = 9f, TextAlign = SKTextAlign.Center };
        canvas.DrawText("S", _slotSoloRects[slotIndex].MidX, _slotSoloRects[slotIndex].MidY + 3, soloTextPaint);
    }

    private void RenderMasterSection(SKCanvas canvas, float x, float y, float width, float height, SignalGeneratorState state)
    {
        var rect = new SKRect(x, y, x + width, y + height);
        var masterRound = new SKRoundRect(rect, 6f);
        canvas.DrawRoundRect(masterRound, _slotBackgroundPaint);

        // Master label
        canvas.DrawText("MASTER", rect.MidX - 6, y + 14, _labelPaint);

        // Master gain knob (slightly left to make room for meter)
        _masterGainKnobCenter = new SKPoint(rect.Left + 32, y + 55);
        float gainNormalized = (state.MasterGainDb + 60f) / 72f;
        bool masterHovered = state.HoveredArea == SignalGeneratorHitArea.MasterGainKnob;
        DrawSmallKnob(canvas, _masterGainKnobCenter, gainNormalized, masterHovered);

        // Value display
        string sign = state.MasterGainDb > 0.05f ? "+" : "";
        canvas.DrawText($"{sign}{state.MasterGainDb:0.0}", rect.Left + 32, y + 55 + SmallKnobRadius + 10, _valuePaint);

        // Output meter
        var meterRect = new SKRect(rect.Right - 16, y + 20, rect.Right - 6, y + height - 10);
        _masterMeter.Update(state.OutputLevel);
        _masterMeter.Render(canvas, meterRect, MeterOrientation.Vertical);
    }

    private void RenderControlsSection(SKCanvas canvas, float x, float y, float width, float height, SignalGeneratorState state)
    {
        // Record button
        float buttonWidth = 70f;
        float buttonHeight = 24f;
        _recordButtonRect = new SKRect(x, y + (height - buttonHeight) / 2, x + buttonWidth, y + (height + buttonHeight) / 2);

        var recordRound = new SKRoundRect(_recordButtonRect, 4f);
        canvas.DrawRoundRect(recordRound, _recordPaint);
        canvas.DrawRoundRect(recordRound, _borderPaint);

        // Record circle icon
        float circleX = _recordButtonRect.Left + 14;
        float circleY = _recordButtonRect.MidY;
        using var circlePaint = new SKPaint { Color = new SKColor(0xFF, 0x30, 0x30), IsAntialias = true, Style = SKPaintStyle.Fill };
        canvas.DrawCircle(circleX, circleY, 4f, circlePaint);

        using var recordTextPaint = new SKPaint { Color = _theme.TextSecondary, IsAntialias = true, TextSize = 10f, TextAlign = SKTextAlign.Left };
        canvas.DrawText("REC", circleX + 10, _recordButtonRect.MidY + 3, recordTextPaint);

        // Hint text
        using var hintPaint = new SKPaint { Color = _theme.TextMuted, IsAntialias = true, TextSize = 9f, TextAlign = SKTextAlign.Left };
        canvas.DrawText("Drop WAV onto slot to load sample", x + buttonWidth + 12, y + height / 2 + 3, hintPaint);
    }

    private void DrawKnob(SKCanvas canvas, SKPoint center, float normalized, bool isHovered)
    {
        const float startAngle = 135f;
        const float sweepAngle = 270f;
        float arcRadius = KnobRadius * 0.8f;

        // Background
        canvas.DrawCircle(center, KnobRadius, _knobBackgroundPaint);

        // Track
        using var trackPath = new SKPath();
        trackPath.AddArc(new SKRect(center.X - arcRadius, center.Y - arcRadius, center.X + arcRadius, center.Y + arcRadius), startAngle, sweepAngle);
        canvas.DrawPath(trackPath, _knobTrackPaint);

        // Value arc
        float valueSweep = sweepAngle * normalized;
        if (valueSweep > 0.5f)
        {
            using var arcPath = new SKPath();
            arcPath.AddArc(new SKRect(center.X - arcRadius, center.Y - arcRadius, center.X + arcRadius, center.Y + arcRadius), startAngle, valueSweep);
            canvas.DrawPath(arcPath, _knobArcPaint);
        }

        // Pointer
        float pointerAngle = startAngle + sweepAngle * normalized;
        float pointerRad = pointerAngle * MathF.PI / 180f;
        float pointerEnd = arcRadius - 4f;
        canvas.DrawLine(center.X, center.Y, center.X + pointerEnd * MathF.Cos(pointerRad), center.Y + pointerEnd * MathF.Sin(pointerRad), _knobPointerPaint);

        // Hover highlight
        if (isHovered)
        {
            using var hoverPaint = new SKPaint { Color = _theme.KnobArc.WithAlpha(40), IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 2f };
            canvas.DrawCircle(center, KnobRadius + 3, hoverPaint);
        }
    }

    private void DrawSmallKnob(SKCanvas canvas, SKPoint center, float normalized, bool isHovered)
    {
        const float startAngle = 135f;
        const float sweepAngle = 270f;
        float arcRadius = SmallKnobRadius * 0.75f;

        // Background
        canvas.DrawCircle(center, SmallKnobRadius, _knobBackgroundPaint);

        // Track
        _knobTrackPaint.StrokeWidth = 3f;
        using var trackPath = new SKPath();
        trackPath.AddArc(new SKRect(center.X - arcRadius, center.Y - arcRadius, center.X + arcRadius, center.Y + arcRadius), startAngle, sweepAngle);
        canvas.DrawPath(trackPath, _knobTrackPaint);
        _knobTrackPaint.StrokeWidth = 4f;

        // Value arc
        float valueSweep = sweepAngle * normalized;
        if (valueSweep > 0.5f)
        {
            _knobArcPaint.StrokeWidth = 3f;
            using var arcPath = new SKPath();
            arcPath.AddArc(new SKRect(center.X - arcRadius, center.Y - arcRadius, center.X + arcRadius, center.Y + arcRadius), startAngle, valueSweep);
            canvas.DrawPath(arcPath, _knobArcPaint);
            _knobArcPaint.StrokeWidth = 4f;
        }

        // Pointer
        float pointerAngle = startAngle + sweepAngle * normalized;
        float pointerRad = pointerAngle * MathF.PI / 180f;
        float pointerEnd = arcRadius - 2f;
        _knobPointerPaint.StrokeWidth = 1.5f;
        canvas.DrawLine(center.X, center.Y, center.X + pointerEnd * MathF.Cos(pointerRad), center.Y + pointerEnd * MathF.Sin(pointerRad), _knobPointerPaint);
        _knobPointerPaint.StrokeWidth = 2f;

        // Hover highlight
        if (isHovered)
        {
            using var hoverPaint = new SKPaint { Color = _theme.KnobArc.WithAlpha(40), IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1.5f };
            canvas.DrawCircle(center, SmallKnobRadius + 2, hoverPaint);
        }
    }

    private static float NormalizeFrequency(float hz)
    {
        float logMin = MathF.Log(20f);
        float logMax = MathF.Log(20000f);
        float logHz = MathF.Log(Math.Clamp(hz, 20f, 20000f));
        return (logHz - logMin) / (logMax - logMin);
    }

    public SignalGeneratorHitTest HitTest(float x, float y)
    {
        if (_closeButtonRect.Contains(x, y))
            return new SignalGeneratorHitTest(SignalGeneratorHitArea.CloseButton, -1);

        if (_bypassButtonRect.Contains(x, y))
            return new SignalGeneratorHitTest(SignalGeneratorHitArea.BypassButton, -1);

        var presetHit = _presetBar.HitTest(x, y);
        if (presetHit == PresetBarHitArea.Dropdown)
            return new SignalGeneratorHitTest(SignalGeneratorHitArea.PresetDropdown, -1);
        if (presetHit == PresetBarHitArea.SaveButton)
            return new SignalGeneratorHitTest(SignalGeneratorHitArea.PresetSave, -1);

        if (_recordButtonRect.Contains(x, y))
            return new SignalGeneratorHitTest(SignalGeneratorHitArea.RecordButton, -1);

        // Check master gain knob
        float mdx = x - _masterGainKnobCenter.X;
        float mdy = y - _masterGainKnobCenter.Y;
        if (mdx * mdx + mdy * mdy <= KnobRadius * KnobRadius * 1.5f)
            return new SignalGeneratorHitTest(SignalGeneratorHitArea.MasterGainKnob, -1);

        // Check slots
        for (int i = 0; i < 3; i++)
        {
            if (_slotTypeSelectorRects[i].Contains(x, y))
                return new SignalGeneratorHitTest(SignalGeneratorHitArea.SlotTypeSelector, i);

            if (_slotMuteRects[i].Contains(x, y))
                return new SignalGeneratorHitTest(SignalGeneratorHitArea.SlotMuteButton, i);

            if (_slotSoloRects[i].Contains(x, y))
                return new SignalGeneratorHitTest(SignalGeneratorHitArea.SlotSoloButton, i);

            float gdx = x - _slotGainKnobCenters[i].X;
            float gdy = y - _slotGainKnobCenters[i].Y;
            if (gdx * gdx + gdy * gdy <= SmallKnobRadius * SmallKnobRadius * 1.5f)
                return new SignalGeneratorHitTest(SignalGeneratorHitArea.SlotGainKnob, i);

            float fdx = x - _slotFreqKnobCenters[i].X;
            float fdy = y - _slotFreqKnobCenters[i].Y;
            if (fdx * fdx + fdy * fdy <= SmallKnobRadius * SmallKnobRadius * 1.5f)
                return new SignalGeneratorHitTest(SignalGeneratorHitArea.SlotFreqKnob, i);

            if (_slotRects[i].Contains(x, y))
                return new SignalGeneratorHitTest(SignalGeneratorHitArea.SlotArea, i);
        }

        if (_titleBarRect.Contains(x, y))
            return new SignalGeneratorHitTest(SignalGeneratorHitArea.TitleBar, -1);

        return new SignalGeneratorHitTest(SignalGeneratorHitArea.None, -1);
    }

    public SKRect GetPresetDropdownRect() => _presetBar.GetDropdownRect();

    public static SKSize GetPreferredSize() => new(510, 230);

    public void Dispose()
    {
        _presetBar.Dispose();
        _masterMeter.Dispose();
        foreach (var meter in _slotMeters) meter.Dispose();
        _backgroundPaint.Dispose();
        _titleBarPaint.Dispose();
        _borderPaint.Dispose();
        _titlePaint.Dispose();
        _closeButtonPaint.Dispose();
        _slotBackgroundPaint.Dispose();
        _labelPaint.Dispose();
        _valuePaint.Dispose();
        _mutePaint.Dispose();
        _mutedPaint.Dispose();
        _soloPaint.Dispose();
        _soloedPaint.Dispose();
        _bypassPaint.Dispose();
        _bypassActivePaint.Dispose();
        _knobBackgroundPaint.Dispose();
        _knobTrackPaint.Dispose();
        _knobArcPaint.Dispose();
        _knobPointerPaint.Dispose();
        _typeSelectorPaint.Dispose();
        _recordPaint.Dispose();
        _recordActivePaint.Dispose();
    }
}

/// <summary>
/// State data for rendering the Signal Generator UI.
/// </summary>
public class SignalGeneratorState
{
    public bool IsBypassed { get; set; }
    public string PresetName { get; set; } = "Custom";
    public float OutputLevel { get; set; }
    public float MasterGainDb { get; set; }
    public SignalGeneratorHitArea HoveredArea { get; set; }
    public int HoveredSlot { get; set; } = -1;
    public SlotRenderState[] Slots { get; } = new SlotRenderState[3];
}

public struct SlotRenderState
{
    public GeneratorType Type;
    public float Frequency;
    public float GainDb;
    public bool IsMuted;
    public bool IsSolo;
    public float Level;
}

public enum SignalGeneratorHitArea
{
    None,
    TitleBar,
    CloseButton,
    BypassButton,
    PresetDropdown,
    PresetSave,
    SlotArea,
    SlotTypeSelector,
    SlotGainKnob,
    SlotFreqKnob,
    SlotMuteButton,
    SlotSoloButton,
    MasterGainKnob,
    RecordButton,
    LoadSampleButton
}

public record struct SignalGeneratorHitTest(SignalGeneratorHitArea Area, int SlotIndex);
