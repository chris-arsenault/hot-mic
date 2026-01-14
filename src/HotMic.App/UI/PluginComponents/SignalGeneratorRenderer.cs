using HotMic.Core.Plugins.BuiltIn;
using SkiaSharp;

namespace HotMic.App.UI.PluginComponents;

/// <summary>
/// Renderer for the Signal Generator plugin UI with 3 vertical slot rows.
/// </summary>
public sealed class SignalGeneratorRenderer : IDisposable
{
    private const float TitleBarHeight = 40f;
    private const float SlotRowHeight = 56f;
    private const float MasterRowHeight = 44f;
    private const float Padding = 6f;
    private const float CornerRadius = 8f;
    private const float KnobRadius = 16f;
    private const float SmallKnobRadius = 14f;
    private const float TinyKnobRadius = 12f;
    private const float ButtonHeight = 18f;

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
    private readonly SKPaint _dropdownPaint;
    private readonly SKPaint _toggleOffPaint;
    private readonly SKPaint _toggleOnPaint;

    private readonly LevelMeter _masterMeter;

    // Hit test regions - per slot
    private SKRect _titleBarRect;
    private SKRect _closeButtonRect;
    private SKRect _bypassButtonRect;
    private readonly SKRect[] _slotRects = new SKRect[3];
    private readonly SKRect[] _slotTypeSelectorRects = new SKRect[3];
    private readonly SKPoint[] _slotGainKnobCenters = new SKPoint[3];
    private readonly SKPoint[] _slotFreqKnobCenters = new SKPoint[3];
    private readonly SKRect[] _slotMuteRects = new SKRect[3];
    private readonly SKRect[] _slotSoloRects = new SKRect[3];
    private readonly SKRect[] _slotSweepToggleRects = new SKRect[3];
    private readonly SKPoint[] _slotSweepStartKnobCenters = new SKPoint[3];
    private readonly SKPoint[] _slotSweepEndKnobCenters = new SKPoint[3];
    private readonly SKPoint[] _slotSweepDurKnobCenters = new SKPoint[3];
    private readonly SKPoint[] _slotPulseWidthKnobCenters = new SKPoint[3];
    private readonly SKPoint[] _slotIntervalKnobCenters = new SKPoint[3];
    private readonly SKPoint[] _slotChirpDurKnobCenters = new SKPoint[3];
    private readonly SKRect[] _slotLoopModeRects = new SKRect[3];
    private readonly SKPoint[] _slotSpeedKnobCenters = new SKPoint[3];
    private readonly SKPoint[] _slotTrimStartKnobCenters = new SKPoint[3];
    private readonly SKPoint[] _slotTrimEndKnobCenters = new SKPoint[3];
    private SKPoint _masterGainKnobCenter;
    private SKRect _masterHeadroomRect;
    private SKRect _recordButtonRect;

    public SignalGeneratorRenderer(PluginComponentTheme? theme = null)
    {
        _theme = theme ?? PluginComponentTheme.Default;
        _presetBar = new PluginPresetBar(_theme);

        _backgroundPaint = new SKPaint { Color = _theme.PanelBackground, IsAntialias = true, Style = SKPaintStyle.Fill };
        _titleBarPaint = new SKPaint { Color = _theme.PanelBackgroundLight, IsAntialias = true, Style = SKPaintStyle.Fill };
        _borderPaint = new SKPaint { Color = _theme.PanelBorder, IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1f };
        _titlePaint = new SKPaint { Color = _theme.TextPrimary, IsAntialias = true, TextSize = 13f, Typeface = SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Bold) };
        _closeButtonPaint = new SKPaint { Color = _theme.TextSecondary, IsAntialias = true, TextSize = 16f, TextAlign = SKTextAlign.Center };
        _slotBackgroundPaint = new SKPaint { Color = _theme.MeterBackground, IsAntialias = true, Style = SKPaintStyle.Fill };
        _labelPaint = new SKPaint { Color = _theme.TextSecondary, IsAntialias = true, TextSize = 9f, TextAlign = SKTextAlign.Center, Typeface = SKTypeface.FromFamilyName("Segoe UI") };
        _valuePaint = new SKPaint { Color = _theme.TextPrimary, IsAntialias = true, TextSize = 9f, TextAlign = SKTextAlign.Center, Typeface = SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Bold) };
        _mutePaint = new SKPaint { Color = _theme.PanelBackgroundLight, IsAntialias = true, Style = SKPaintStyle.Fill };
        _mutedPaint = new SKPaint { Color = new SKColor(0xFF, 0x50, 0x50), IsAntialias = true, Style = SKPaintStyle.Fill };
        _soloPaint = new SKPaint { Color = _theme.PanelBackgroundLight, IsAntialias = true, Style = SKPaintStyle.Fill };
        _soloedPaint = new SKPaint { Color = new SKColor(0xFF, 0xD7, 0x00), IsAntialias = true, Style = SKPaintStyle.Fill };
        _bypassPaint = new SKPaint { Color = _theme.PanelBackgroundLight, IsAntialias = true, Style = SKPaintStyle.Fill };
        _bypassActivePaint = new SKPaint { Color = new SKColor(0xFF, 0x50, 0x50), IsAntialias = true, Style = SKPaintStyle.Fill };
        _knobBackgroundPaint = new SKPaint { Color = _theme.KnobBackground, IsAntialias = true, Style = SKPaintStyle.Fill };
        _knobTrackPaint = new SKPaint { Color = _theme.KnobTrack, IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 3f, StrokeCap = SKStrokeCap.Round };
        _knobArcPaint = new SKPaint { Color = _theme.KnobArc, IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 3f, StrokeCap = SKStrokeCap.Round };
        _knobPointerPaint = new SKPaint { Color = _theme.KnobPointer, IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1.5f, StrokeCap = SKStrokeCap.Round };
        _dropdownPaint = new SKPaint { Color = _theme.PanelBackgroundLight, IsAntialias = true, Style = SKPaintStyle.Fill };
        _toggleOffPaint = new SKPaint { Color = _theme.PanelBackgroundLight, IsAntialias = true, Style = SKPaintStyle.Fill };
        _toggleOnPaint = new SKPaint { Color = new SKColor(0x40, 0xA0, 0x40), IsAntialias = true, Style = SKPaintStyle.Fill };

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

        // Slot rows (stacked vertically)
        float contentTop = TitleBarHeight + Padding;
        for (int i = 0; i < 3; i++)
        {
            float rowY = contentTop + i * (SlotRowHeight + Padding);
            _slotRects[i] = new SKRect(Padding, rowY, size.Width - Padding, rowY + SlotRowHeight);
            RenderSlotRow(canvas, i, _slotRects[i], state.Slots[i], state.HoveredSlot == i, state.HoveredArea);
        }

        // Master row at bottom
        float masterY = contentTop + 3 * (SlotRowHeight + Padding);
        RenderMasterRow(canvas, Padding, masterY, size.Width - Padding * 2, MasterRowHeight, state);

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
        canvas.DrawText("Signal Generator", Padding + 2, TitleBarHeight / 2f + 4, _titlePaint);

        // Preset bar
        float presetBarX = 115f;
        float presetBarY = (TitleBarHeight - PluginPresetBar.TotalHeight) / 2f;
        _presetBar.Render(canvas, presetBarX, presetBarY, state.PresetName);

        // Bypass button
        float bypassWidth = 50f;
        _bypassButtonRect = new SKRect(
            size.Width - Padding - 24 - bypassWidth - 6,
            (TitleBarHeight - 20) / 2,
            size.Width - Padding - 24 - 6,
            (TitleBarHeight + 20) / 2);
        var bypassRound = new SKRoundRect(_bypassButtonRect, 4f);
        canvas.DrawRoundRect(bypassRound, state.IsBypassed ? _bypassActivePaint : _bypassPaint);
        canvas.DrawRoundRect(bypassRound, _borderPaint);

        using var bypassTextPaint = new SKPaint
        {
            Color = state.IsBypassed ? _theme.TextPrimary : _theme.TextSecondary,
            IsAntialias = true,
            TextSize = 9f,
            TextAlign = SKTextAlign.Center,
            Typeface = SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Bold)
        };
        canvas.DrawText("BYPASS", _bypassButtonRect.MidX, _bypassButtonRect.MidY + 3, bypassTextPaint);

        // Close button
        _closeButtonRect = new SKRect(size.Width - Padding - 20, (TitleBarHeight - 20) / 2,
            size.Width - Padding, (TitleBarHeight + 20) / 2);
        canvas.DrawText("\u00D7", _closeButtonRect.MidX, _closeButtonRect.MidY + 5, _closeButtonPaint);
    }

    private void RenderSlotRow(SKCanvas canvas, int slotIndex, SKRect rect, SlotRenderState slot, bool isHovered, SignalGeneratorHitArea hoveredArea)
    {
        // Row background
        var slotRound = new SKRoundRect(rect, 4f);
        canvas.DrawRoundRect(slotRound, _slotBackgroundPaint);

        float x = rect.Left + 6;
        float centerY = rect.MidY;

        // Slot label
        using var slotLabelPaint = new SKPaint { Color = _theme.TextSecondary, IsAntialias = true, TextSize = 10f, TextAlign = SKTextAlign.Left, Typeface = SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Bold) };
        canvas.DrawText($"S{slotIndex + 1}", x, centerY + 3, slotLabelPaint);
        x += 18;

        // Type dropdown
        float typeWidth = 70f;
        _slotTypeSelectorRects[slotIndex] = new SKRect(x, centerY - 10, x + typeWidth, centerY + 10);
        var typeRound = new SKRoundRect(_slotTypeSelectorRects[slotIndex], 3f);
        canvas.DrawRoundRect(typeRound, _dropdownPaint);
        canvas.DrawRoundRect(typeRound, _borderPaint);

        string typeLabel = slot.Type.ToString();
        if (typeLabel.Length > 9) typeLabel = typeLabel[..9];
        canvas.DrawText(typeLabel, _slotTypeSelectorRects[slotIndex].MidX - 4, centerY + 3, _valuePaint);

        // Dropdown arrow
        float arrowX = _slotTypeSelectorRects[slotIndex].Right - 10;
        using var arrowPath = new SKPath();
        arrowPath.MoveTo(arrowX - 3, centerY - 2);
        arrowPath.LineTo(arrowX, centerY + 2);
        arrowPath.LineTo(arrowX + 3, centerY - 2);
        using var arrowPaint = new SKPaint { Color = _theme.TextSecondary, IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1.5f };
        canvas.DrawPath(arrowPath, arrowPaint);

        x += typeWidth + 6;

        // Render type-specific controls
        x = RenderTypeSpecificControls(canvas, slotIndex, slot, x, rect, centerY, isHovered, hoveredArea);

        // Gain knob (always shown, positioned after type-specific controls)
        float gainX = rect.Right - 80;
        _slotGainKnobCenters[slotIndex] = new SKPoint(gainX, centerY);
        float gainNormalized = (slot.GainDb + 60f) / 72f;
        bool gainHovered = isHovered && hoveredArea == SignalGeneratorHitArea.SlotGainKnob;
        DrawTinyKnob(canvas, _slotGainKnobCenters[slotIndex], gainNormalized, gainHovered);
        canvas.DrawText("GAIN", gainX, centerY + TinyKnobRadius + 8, _labelPaint);

        // Mute/Solo buttons at right edge
        float buttonX = rect.Right - 42;
        float buttonWidth = 18f;

        _slotMuteRects[slotIndex] = new SKRect(buttonX, centerY - 8, buttonX + buttonWidth, centerY + 8);
        var muteRound = new SKRoundRect(_slotMuteRects[slotIndex], 3f);
        canvas.DrawRoundRect(muteRound, slot.IsMuted ? _mutedPaint : _mutePaint);
        canvas.DrawRoundRect(muteRound, _borderPaint);
        using var muteTextPaint = new SKPaint { Color = slot.IsMuted ? _theme.TextPrimary : _theme.TextSecondary, IsAntialias = true, TextSize = 9f, TextAlign = SKTextAlign.Center, Typeface = SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Bold) };
        canvas.DrawText("M", _slotMuteRects[slotIndex].MidX, _slotMuteRects[slotIndex].MidY + 3, muteTextPaint);

        buttonX += buttonWidth + 3;
        _slotSoloRects[slotIndex] = new SKRect(buttonX, centerY - 8, buttonX + buttonWidth, centerY + 8);
        var soloRound = new SKRoundRect(_slotSoloRects[slotIndex], 3f);
        canvas.DrawRoundRect(soloRound, slot.IsSolo ? _soloedPaint : _soloPaint);
        canvas.DrawRoundRect(soloRound, _borderPaint);
        using var soloTextPaint = new SKPaint { Color = slot.IsSolo ? _theme.PanelBackground : _theme.TextSecondary, IsAntialias = true, TextSize = 9f, TextAlign = SKTextAlign.Center, Typeface = SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Bold) };
        canvas.DrawText("S", _slotSoloRects[slotIndex].MidX, _slotSoloRects[slotIndex].MidY + 3, soloTextPaint);
    }

    private float RenderTypeSpecificControls(SKCanvas canvas, int slotIndex, SlotRenderState slot, float x, SKRect rect, float centerY, bool isHovered, SignalGeneratorHitArea hoveredArea)
    {
        // Reset off-screen positions for unused controls
        _slotFreqKnobCenters[slotIndex] = new SKPoint(-100, -100);
        _slotSweepToggleRects[slotIndex] = SKRect.Empty;
        _slotSweepStartKnobCenters[slotIndex] = new SKPoint(-100, -100);
        _slotSweepEndKnobCenters[slotIndex] = new SKPoint(-100, -100);
        _slotSweepDurKnobCenters[slotIndex] = new SKPoint(-100, -100);
        _slotPulseWidthKnobCenters[slotIndex] = new SKPoint(-100, -100);
        _slotIntervalKnobCenters[slotIndex] = new SKPoint(-100, -100);
        _slotChirpDurKnobCenters[slotIndex] = new SKPoint(-100, -100);
        _slotLoopModeRects[slotIndex] = SKRect.Empty;
        _slotSpeedKnobCenters[slotIndex] = new SKPoint(-100, -100);
        _slotTrimStartKnobCenters[slotIndex] = new SKPoint(-100, -100);
        _slotTrimEndKnobCenters[slotIndex] = new SKPoint(-100, -100);

        switch (slot.Type)
        {
            case GeneratorType.Sine:
            case GeneratorType.Square:
            case GeneratorType.Saw:
            case GeneratorType.Triangle:
                x = RenderOscillatorControls(canvas, slotIndex, slot, x, centerY, isHovered, hoveredArea);
                break;

            case GeneratorType.WhiteNoise:
            case GeneratorType.PinkNoise:
            case GeneratorType.BrownNoise:
            case GeneratorType.BlueNoise:
                // Noise has no extra controls
                break;

            case GeneratorType.Impulse:
                x = RenderImpulseControls(canvas, slotIndex, slot, x, centerY, isHovered, hoveredArea);
                break;

            case GeneratorType.Chirp:
                x = RenderChirpControls(canvas, slotIndex, slot, x, centerY, isHovered, hoveredArea);
                break;

            case GeneratorType.Sample:
                x = RenderSampleControls(canvas, slotIndex, slot, x, centerY, isHovered, hoveredArea);
                break;
        }

        return x;
    }

    private float RenderOscillatorControls(SKCanvas canvas, int slotIndex, SlotRenderState slot, float x, float centerY, bool isHovered, SignalGeneratorHitArea hoveredArea)
    {
        // Frequency knob
        _slotFreqKnobCenters[slotIndex] = new SKPoint(x + TinyKnobRadius, centerY);
        float freqNormalized = NormalizeFrequency(slot.Frequency);
        bool freqHovered = isHovered && hoveredArea == SignalGeneratorHitArea.SlotFreqKnob;
        DrawTinyKnob(canvas, _slotFreqKnobCenters[slotIndex], freqNormalized, freqHovered);
        canvas.DrawText("FREQ", x + TinyKnobRadius, centerY + TinyKnobRadius + 8, _labelPaint);
        x += TinyKnobRadius * 2 + 8;

        // Sweep toggle
        float toggleWidth = 36f;
        _slotSweepToggleRects[slotIndex] = new SKRect(x, centerY - 8, x + toggleWidth, centerY + 8);
        var toggleRound = new SKRoundRect(_slotSweepToggleRects[slotIndex], 3f);
        canvas.DrawRoundRect(toggleRound, slot.SweepEnabled ? _toggleOnPaint : _toggleOffPaint);
        canvas.DrawRoundRect(toggleRound, _borderPaint);
        using var sweepTextPaint = new SKPaint { Color = slot.SweepEnabled ? _theme.TextPrimary : _theme.TextSecondary, IsAntialias = true, TextSize = 8f, TextAlign = SKTextAlign.Center };
        canvas.DrawText("SWEEP", _slotSweepToggleRects[slotIndex].MidX, centerY + 3, sweepTextPaint);
        x += toggleWidth + 4;

        // Sweep controls (only if sweep enabled)
        if (slot.SweepEnabled)
        {
            // Start Hz
            _slotSweepStartKnobCenters[slotIndex] = new SKPoint(x + TinyKnobRadius, centerY);
            float startNorm = NormalizeFrequency(slot.SweepStartHz);
            DrawTinyKnob(canvas, _slotSweepStartKnobCenters[slotIndex], startNorm, isHovered && hoveredArea == SignalGeneratorHitArea.SlotSweepStartKnob);
            canvas.DrawText("START", x + TinyKnobRadius, centerY + TinyKnobRadius + 8, _labelPaint);
            x += TinyKnobRadius * 2 + 4;

            // End Hz
            _slotSweepEndKnobCenters[slotIndex] = new SKPoint(x + TinyKnobRadius, centerY);
            float endNorm = NormalizeFrequency(slot.SweepEndHz);
            DrawTinyKnob(canvas, _slotSweepEndKnobCenters[slotIndex], endNorm, isHovered && hoveredArea == SignalGeneratorHitArea.SlotSweepEndKnob);
            canvas.DrawText("END", x + TinyKnobRadius, centerY + TinyKnobRadius + 8, _labelPaint);
            x += TinyKnobRadius * 2 + 4;

            // Duration
            _slotSweepDurKnobCenters[slotIndex] = new SKPoint(x + TinyKnobRadius, centerY);
            float durNorm = (slot.SweepDurationMs - 100f) / (30000f - 100f);
            DrawTinyKnob(canvas, _slotSweepDurKnobCenters[slotIndex], durNorm, isHovered && hoveredArea == SignalGeneratorHitArea.SlotSweepDurKnob);
            canvas.DrawText("DUR", x + TinyKnobRadius, centerY + TinyKnobRadius + 8, _labelPaint);
            x += TinyKnobRadius * 2 + 4;
        }

        // Pulse width for square wave
        if (slot.Type == GeneratorType.Square)
        {
            _slotPulseWidthKnobCenters[slotIndex] = new SKPoint(x + TinyKnobRadius, centerY);
            float pwNorm = (slot.PulseWidth - 0.1f) / 0.8f;
            DrawTinyKnob(canvas, _slotPulseWidthKnobCenters[slotIndex], pwNorm, isHovered && hoveredArea == SignalGeneratorHitArea.SlotPulseWidthKnob);
            canvas.DrawText("PW", x + TinyKnobRadius, centerY + TinyKnobRadius + 8, _labelPaint);
            x += TinyKnobRadius * 2 + 4;
        }

        return x;
    }

    private float RenderImpulseControls(SKCanvas canvas, int slotIndex, SlotRenderState slot, float x, float centerY, bool isHovered, SignalGeneratorHitArea hoveredArea)
    {
        // Interval knob
        _slotIntervalKnobCenters[slotIndex] = new SKPoint(x + TinyKnobRadius, centerY);
        float intervalNorm = (slot.ImpulseIntervalMs - 10f) / (5000f - 10f);
        DrawTinyKnob(canvas, _slotIntervalKnobCenters[slotIndex], intervalNorm, isHovered && hoveredArea == SignalGeneratorHitArea.SlotIntervalKnob);
        canvas.DrawText("INT", x + TinyKnobRadius, centerY + TinyKnobRadius + 8, _labelPaint);
        x += TinyKnobRadius * 2 + 8;

        return x;
    }

    private float RenderChirpControls(SKCanvas canvas, int slotIndex, SlotRenderState slot, float x, float centerY, bool isHovered, SignalGeneratorHitArea hoveredArea)
    {
        // Duration knob
        _slotChirpDurKnobCenters[slotIndex] = new SKPoint(x + TinyKnobRadius, centerY);
        float durNorm = (slot.ChirpDurationMs - 50f) / (500f - 50f);
        DrawTinyKnob(canvas, _slotChirpDurKnobCenters[slotIndex], durNorm, isHovered && hoveredArea == SignalGeneratorHitArea.SlotChirpDurKnob);
        canvas.DrawText("DUR", x + TinyKnobRadius, centerY + TinyKnobRadius + 8, _labelPaint);
        x += TinyKnobRadius * 2 + 8;

        return x;
    }

    private float RenderSampleControls(SKCanvas canvas, int slotIndex, SlotRenderState slot, float x, float centerY, bool isHovered, SignalGeneratorHitArea hoveredArea)
    {
        // Loop mode dropdown
        float loopWidth = 50f;
        _slotLoopModeRects[slotIndex] = new SKRect(x, centerY - 8, x + loopWidth, centerY + 8);
        var loopRound = new SKRoundRect(_slotLoopModeRects[slotIndex], 3f);
        canvas.DrawRoundRect(loopRound, _dropdownPaint);
        canvas.DrawRoundRect(loopRound, _borderPaint);
        string loopLabel = slot.LoopMode switch
        {
            SampleLoopMode.Loop => "Loop",
            SampleLoopMode.OneShot => "1-Shot",
            SampleLoopMode.PingPong => "P-Pong",
            _ => "Loop"
        };
        canvas.DrawText(loopLabel, _slotLoopModeRects[slotIndex].MidX, centerY + 3, _valuePaint);
        x += loopWidth + 6;

        // Speed knob
        _slotSpeedKnobCenters[slotIndex] = new SKPoint(x + TinyKnobRadius, centerY);
        float speedNorm = (slot.SampleSpeed - 0.5f) / 1.5f;
        DrawTinyKnob(canvas, _slotSpeedKnobCenters[slotIndex], speedNorm, isHovered && hoveredArea == SignalGeneratorHitArea.SlotSpeedKnob);
        canvas.DrawText("SPD", x + TinyKnobRadius, centerY + TinyKnobRadius + 8, _labelPaint);
        x += TinyKnobRadius * 2 + 4;

        // Trim start
        _slotTrimStartKnobCenters[slotIndex] = new SKPoint(x + TinyKnobRadius, centerY);
        DrawTinyKnob(canvas, _slotTrimStartKnobCenters[slotIndex], slot.TrimStart, isHovered && hoveredArea == SignalGeneratorHitArea.SlotTrimStartKnob);
        canvas.DrawText("IN", x + TinyKnobRadius, centerY + TinyKnobRadius + 8, _labelPaint);
        x += TinyKnobRadius * 2 + 4;

        // Trim end
        _slotTrimEndKnobCenters[slotIndex] = new SKPoint(x + TinyKnobRadius, centerY);
        DrawTinyKnob(canvas, _slotTrimEndKnobCenters[slotIndex], slot.TrimEnd, isHovered && hoveredArea == SignalGeneratorHitArea.SlotTrimEndKnob);
        canvas.DrawText("OUT", x + TinyKnobRadius, centerY + TinyKnobRadius + 8, _labelPaint);
        x += TinyKnobRadius * 2 + 4;

        return x;
    }

    private void RenderMasterRow(SKCanvas canvas, float x, float y, float width, float height, SignalGeneratorState state)
    {
        var rect = new SKRect(x, y, x + width, y + height);
        var masterRound = new SKRoundRect(rect, 4f);
        canvas.DrawRoundRect(masterRound, _slotBackgroundPaint);

        float centerY = rect.MidY;
        float px = rect.Left + 8;

        // Master label
        using var masterLabelPaint = new SKPaint { Color = _theme.TextSecondary, IsAntialias = true, TextSize = 10f, TextAlign = SKTextAlign.Left, Typeface = SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Bold) };
        canvas.DrawText("MASTER", px, centerY + 3, masterLabelPaint);
        px += 52;

        // Master gain knob
        _masterGainKnobCenter = new SKPoint(px + SmallKnobRadius, centerY);
        float gainNormalized = (state.MasterGainDb + 60f) / 72f;
        bool masterHovered = state.HoveredArea == SignalGeneratorHitArea.MasterGainKnob;
        DrawSmallKnob(canvas, _masterGainKnobCenter, gainNormalized, masterHovered);
        string sign = state.MasterGainDb > 0.05f ? "+" : "";
        canvas.DrawText($"{sign}{state.MasterGainDb:0.0}dB", px + SmallKnobRadius, centerY + SmallKnobRadius + 9, _labelPaint);
        px += SmallKnobRadius * 2 + 12;

        // Output meter
        var meterRect = new SKRect(px, y + 6, px + 8, y + height - 6);
        _masterMeter.Update(state.OutputLevel);
        _masterMeter.Render(canvas, meterRect, MeterOrientation.Vertical);
        px += 16;

        // Headroom dropdown
        float hrWidth = 65f;
        _masterHeadroomRect = new SKRect(px, centerY - 9, px + hrWidth, centerY + 9);
        var hrRound = new SKRoundRect(_masterHeadroomRect, 3f);
        canvas.DrawRoundRect(hrRound, _dropdownPaint);
        canvas.DrawRoundRect(hrRound, _borderPaint);
        string hrLabel = state.HeadroomMode switch
        {
            HeadroomMode.None => "None",
            HeadroomMode.AutoCompensate => "Auto",
            HeadroomMode.Normalize => "Normalize",
            _ => "Auto"
        };
        canvas.DrawText(hrLabel, _masterHeadroomRect.MidX, centerY + 3, _valuePaint);
        px += hrWidth + 10;

        // Record button
        float recWidth = 48f;
        _recordButtonRect = new SKRect(px, centerY - 10, px + recWidth, centerY + 10);
        var recordRound = new SKRoundRect(_recordButtonRect, 4f);
        canvas.DrawRoundRect(recordRound, _dropdownPaint);
        canvas.DrawRoundRect(recordRound, _borderPaint);

        // Record circle
        using var circlePaint = new SKPaint { Color = new SKColor(0xFF, 0x30, 0x30), IsAntialias = true, Style = SKPaintStyle.Fill };
        canvas.DrawCircle(px + 12, centerY, 4f, circlePaint);

        using var recTextPaint = new SKPaint { Color = _theme.TextSecondary, IsAntialias = true, TextSize = 9f, TextAlign = SKTextAlign.Left };
        canvas.DrawText("REC", px + 20, centerY + 3, recTextPaint);
        px += recWidth + 10;

        // Hint text
        using var hintPaint = new SKPaint { Color = _theme.TextMuted, IsAntialias = true, TextSize = 9f, TextAlign = SKTextAlign.Left };
        canvas.DrawText("Drop WAV onto slot", px, centerY + 3, hintPaint);
    }

    private void DrawSmallKnob(SKCanvas canvas, SKPoint center, float normalized, bool isHovered)
    {
        const float startAngle = 135f;
        const float sweepAngle = 270f;
        float arcRadius = SmallKnobRadius * 0.75f;

        canvas.DrawCircle(center, SmallKnobRadius, _knobBackgroundPaint);

        using var trackPath = new SKPath();
        trackPath.AddArc(new SKRect(center.X - arcRadius, center.Y - arcRadius, center.X + arcRadius, center.Y + arcRadius), startAngle, sweepAngle);
        canvas.DrawPath(trackPath, _knobTrackPaint);

        float valueSweep = sweepAngle * Math.Clamp(normalized, 0f, 1f);
        if (valueSweep > 0.5f)
        {
            using var arcPath = new SKPath();
            arcPath.AddArc(new SKRect(center.X - arcRadius, center.Y - arcRadius, center.X + arcRadius, center.Y + arcRadius), startAngle, valueSweep);
            canvas.DrawPath(arcPath, _knobArcPaint);
        }

        float pointerAngle = startAngle + sweepAngle * Math.Clamp(normalized, 0f, 1f);
        float pointerRad = pointerAngle * MathF.PI / 180f;
        float pointerEnd = arcRadius - 2f;
        canvas.DrawLine(center.X, center.Y, center.X + pointerEnd * MathF.Cos(pointerRad), center.Y + pointerEnd * MathF.Sin(pointerRad), _knobPointerPaint);

        if (isHovered)
        {
            using var hoverPaint = new SKPaint { Color = _theme.KnobArc.WithAlpha(40), IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1.5f };
            canvas.DrawCircle(center, SmallKnobRadius + 2, hoverPaint);
        }
    }

    private void DrawTinyKnob(SKCanvas canvas, SKPoint center, float normalized, bool isHovered)
    {
        const float startAngle = 135f;
        const float sweepAngle = 270f;
        float arcRadius = TinyKnobRadius * 0.7f;

        canvas.DrawCircle(center, TinyKnobRadius, _knobBackgroundPaint);

        _knobTrackPaint.StrokeWidth = 2.5f;
        using var trackPath = new SKPath();
        trackPath.AddArc(new SKRect(center.X - arcRadius, center.Y - arcRadius, center.X + arcRadius, center.Y + arcRadius), startAngle, sweepAngle);
        canvas.DrawPath(trackPath, _knobTrackPaint);
        _knobTrackPaint.StrokeWidth = 3f;

        float valueSweep = sweepAngle * Math.Clamp(normalized, 0f, 1f);
        if (valueSweep > 0.5f)
        {
            _knobArcPaint.StrokeWidth = 2.5f;
            using var arcPath = new SKPath();
            arcPath.AddArc(new SKRect(center.X - arcRadius, center.Y - arcRadius, center.X + arcRadius, center.Y + arcRadius), startAngle, valueSweep);
            canvas.DrawPath(arcPath, _knobArcPaint);
            _knobArcPaint.StrokeWidth = 3f;
        }

        float pointerAngle = startAngle + sweepAngle * Math.Clamp(normalized, 0f, 1f);
        float pointerRad = pointerAngle * MathF.PI / 180f;
        float pointerEnd = arcRadius - 1f;
        _knobPointerPaint.StrokeWidth = 1.2f;
        canvas.DrawLine(center.X, center.Y, center.X + pointerEnd * MathF.Cos(pointerRad), center.Y + pointerEnd * MathF.Sin(pointerRad), _knobPointerPaint);
        _knobPointerPaint.StrokeWidth = 1.5f;

        if (isHovered)
        {
            using var hoverPaint = new SKPaint { Color = _theme.KnobArc.WithAlpha(40), IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1.2f };
            canvas.DrawCircle(center, TinyKnobRadius + 1.5f, hoverPaint);
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

        if (_masterHeadroomRect.Contains(x, y))
            return new SignalGeneratorHitTest(SignalGeneratorHitArea.MasterHeadroomDropdown, -1);

        // Check master gain knob
        if (IsOverKnob(x, y, _masterGainKnobCenter, SmallKnobRadius))
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

            if (IsOverKnob(x, y, _slotGainKnobCenters[i], TinyKnobRadius))
                return new SignalGeneratorHitTest(SignalGeneratorHitArea.SlotGainKnob, i);

            if (IsOverKnob(x, y, _slotFreqKnobCenters[i], TinyKnobRadius))
                return new SignalGeneratorHitTest(SignalGeneratorHitArea.SlotFreqKnob, i);

            if (_slotSweepToggleRects[i].Contains(x, y))
                return new SignalGeneratorHitTest(SignalGeneratorHitArea.SlotSweepToggle, i);

            if (IsOverKnob(x, y, _slotSweepStartKnobCenters[i], TinyKnobRadius))
                return new SignalGeneratorHitTest(SignalGeneratorHitArea.SlotSweepStartKnob, i);

            if (IsOverKnob(x, y, _slotSweepEndKnobCenters[i], TinyKnobRadius))
                return new SignalGeneratorHitTest(SignalGeneratorHitArea.SlotSweepEndKnob, i);

            if (IsOverKnob(x, y, _slotSweepDurKnobCenters[i], TinyKnobRadius))
                return new SignalGeneratorHitTest(SignalGeneratorHitArea.SlotSweepDurKnob, i);

            if (IsOverKnob(x, y, _slotPulseWidthKnobCenters[i], TinyKnobRadius))
                return new SignalGeneratorHitTest(SignalGeneratorHitArea.SlotPulseWidthKnob, i);

            if (IsOverKnob(x, y, _slotIntervalKnobCenters[i], TinyKnobRadius))
                return new SignalGeneratorHitTest(SignalGeneratorHitArea.SlotIntervalKnob, i);

            if (IsOverKnob(x, y, _slotChirpDurKnobCenters[i], TinyKnobRadius))
                return new SignalGeneratorHitTest(SignalGeneratorHitArea.SlotChirpDurKnob, i);

            if (_slotLoopModeRects[i].Contains(x, y))
                return new SignalGeneratorHitTest(SignalGeneratorHitArea.SlotLoopModeDropdown, i);

            if (IsOverKnob(x, y, _slotSpeedKnobCenters[i], TinyKnobRadius))
                return new SignalGeneratorHitTest(SignalGeneratorHitArea.SlotSpeedKnob, i);

            if (IsOverKnob(x, y, _slotTrimStartKnobCenters[i], TinyKnobRadius))
                return new SignalGeneratorHitTest(SignalGeneratorHitArea.SlotTrimStartKnob, i);

            if (IsOverKnob(x, y, _slotTrimEndKnobCenters[i], TinyKnobRadius))
                return new SignalGeneratorHitTest(SignalGeneratorHitArea.SlotTrimEndKnob, i);

            if (_slotRects[i].Contains(x, y))
                return new SignalGeneratorHitTest(SignalGeneratorHitArea.SlotArea, i);
        }

        if (_titleBarRect.Contains(x, y))
            return new SignalGeneratorHitTest(SignalGeneratorHitArea.TitleBar, -1);

        return new SignalGeneratorHitTest(SignalGeneratorHitArea.None, -1);
    }

    private static bool IsOverKnob(float x, float y, SKPoint center, float radius)
    {
        if (center.X < 0) return false;
        float dx = x - center.X;
        float dy = y - center.Y;
        return dx * dx + dy * dy <= radius * radius * 1.5f;
    }

    public SKRect GetPresetDropdownRect() => _presetBar.GetDropdownRect();

    public static SKSize GetPreferredSize() => new(580, 280);

    public void Dispose()
    {
        _presetBar.Dispose();
        _masterMeter.Dispose();
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
        _dropdownPaint.Dispose();
        _toggleOffPaint.Dispose();
        _toggleOnPaint.Dispose();
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
    public HeadroomMode HeadroomMode { get; set; }
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
    public bool SweepEnabled;
    public float SweepStartHz;
    public float SweepEndHz;
    public float SweepDurationMs;
    public float PulseWidth;
    public float ImpulseIntervalMs;
    public float ChirpDurationMs;
    public SampleLoopMode LoopMode;
    public float SampleSpeed;
    public float TrimStart;
    public float TrimEnd;
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
    SlotSweepToggle,
    SlotSweepStartKnob,
    SlotSweepEndKnob,
    SlotSweepDurKnob,
    SlotPulseWidthKnob,
    SlotIntervalKnob,
    SlotChirpDurKnob,
    SlotLoopModeDropdown,
    SlotSpeedKnob,
    SlotTrimStartKnob,
    SlotTrimEndKnob,
    MasterGainKnob,
    MasterHeadroomDropdown,
    RecordButton,
    LoadSampleButton
}

public record struct SignalGeneratorHitTest(SignalGeneratorHitArea Area, int SlotIndex);
