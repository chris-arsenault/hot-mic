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
    private const float SmallKnobRadius = 14f;
    private const float TinyKnobRadius = 12f;

    private readonly PluginComponentTheme _theme;
    private readonly PluginPresetBar _presetBar;

    private readonly SKPaint _backgroundPaint;
    private readonly SKPaint _titleBarPaint;
    private readonly SKPaint _borderPaint;
    private readonly SkiaTextPaint _titlePaint;
    private readonly SkiaTextPaint _closeButtonPaint;
    private readonly SKPaint _slotBackgroundPaint;
    private readonly SkiaTextPaint _labelPaint;
    private readonly SkiaTextPaint _valuePaint;
    private readonly SKPaint _mutePaint;
    private readonly SKPaint _mutedPaint;
    private readonly SKPaint _soloPaint;
    private readonly SKPaint _soloedPaint;
    private readonly SKPaint _bypassPaint;
    private readonly SKPaint _bypassActivePaint;
    private readonly SKPaint _dropdownPaint;
    private readonly SKPaint _toggleOffPaint;
    private readonly SKPaint _toggleOnPaint;

    private readonly LevelMeter _masterMeter;

    // Knob widgets - per slot
    public KnobWidget[] SlotGainKnobs { get; } = new KnobWidget[3];
    public KnobWidget[] SlotFreqKnobs { get; } = new KnobWidget[3];
    public KnobWidget[] SlotSweepStartKnobs { get; } = new KnobWidget[3];
    public KnobWidget[] SlotSweepEndKnobs { get; } = new KnobWidget[3];
    public KnobWidget[] SlotSweepDurKnobs { get; } = new KnobWidget[3];
    public KnobWidget[] SlotPulseWidthKnobs { get; } = new KnobWidget[3];
    public KnobWidget[] SlotIntervalKnobs { get; } = new KnobWidget[3];
    public KnobWidget[] SlotChirpDurKnobs { get; } = new KnobWidget[3];
    public KnobWidget[] SlotSpeedKnobs { get; } = new KnobWidget[3];
    public KnobWidget[] SlotTrimStartKnobs { get; } = new KnobWidget[3];
    public KnobWidget[] SlotTrimEndKnobs { get; } = new KnobWidget[3];
    public KnobWidget MasterGainKnob { get; }

    // Hit test regions - per slot
    private SKRect _titleBarRect;
    private SKRect _closeButtonRect;
    private SKRect _bypassButtonRect;
    private readonly SKRect[] _slotRects = new SKRect[3];
    private readonly SKRect[] _slotTypeSelectorRects = new SKRect[3];
    private readonly SKRect[] _slotMuteRects = new SKRect[3];
    private readonly SKRect[] _slotSoloRects = new SKRect[3];
    private readonly SKRect[] _slotSweepToggleRects = new SKRect[3];
    private readonly SKRect[] _slotLoopModeRects = new SKRect[3];
    private readonly SKRect[] _slotRecordRects = new SKRect[3];
    private readonly SKRect[] _slotSaveSampleRects = new SKRect[3];
    private readonly SKRect[] _slotLoadSampleRects = new SKRect[3];
    private readonly SKRect[] _slotReloadSampleRects = new SKRect[3];
    private SKRect _masterHeadroomRect;

    public SignalGeneratorRenderer(PluginComponentTheme? theme = null)
    {
        _theme = theme ?? PluginComponentTheme.Default;
        _presetBar = new PluginPresetBar(_theme);

        _backgroundPaint = new SKPaint { Color = _theme.PanelBackground, IsAntialias = true, Style = SKPaintStyle.Fill };
        _titleBarPaint = new SKPaint { Color = _theme.PanelBackgroundLight, IsAntialias = true, Style = SKPaintStyle.Fill };
        _borderPaint = new SKPaint { Color = _theme.PanelBorder, IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1f };
        _titlePaint = new SkiaTextPaint(_theme.TextPrimary, 13f, SKFontStyle.Bold);
        _closeButtonPaint = new SkiaTextPaint(_theme.TextSecondary, 16f, SKFontStyle.Normal, SKTextAlign.Center);
        _slotBackgroundPaint = new SKPaint { Color = _theme.MeterBackground, IsAntialias = true, Style = SKPaintStyle.Fill };
        _labelPaint = new SkiaTextPaint(_theme.TextSecondary, 9f, SKFontStyle.Normal, SKTextAlign.Center);
        _valuePaint = new SkiaTextPaint(_theme.TextPrimary, 9f, SKFontStyle.Bold, SKTextAlign.Center);
        _mutePaint = new SKPaint { Color = _theme.PanelBackgroundLight, IsAntialias = true, Style = SKPaintStyle.Fill };
        _mutedPaint = new SKPaint { Color = new SKColor(0xFF, 0x50, 0x50), IsAntialias = true, Style = SKPaintStyle.Fill };
        _soloPaint = new SKPaint { Color = _theme.PanelBackgroundLight, IsAntialias = true, Style = SKPaintStyle.Fill };
        _soloedPaint = new SKPaint { Color = new SKColor(0xFF, 0xD7, 0x00), IsAntialias = true, Style = SKPaintStyle.Fill };
        _bypassPaint = new SKPaint { Color = _theme.PanelBackgroundLight, IsAntialias = true, Style = SKPaintStyle.Fill };
        _bypassActivePaint = new SKPaint { Color = new SKColor(0xFF, 0x50, 0x50), IsAntialias = true, Style = SKPaintStyle.Fill };
        _dropdownPaint = new SKPaint { Color = _theme.PanelBackgroundLight, IsAntialias = true, Style = SKPaintStyle.Fill };
        _toggleOffPaint = new SKPaint { Color = _theme.PanelBackgroundLight, IsAntialias = true, Style = SKPaintStyle.Fill };
        _toggleOnPaint = new SKPaint { Color = new SKColor(0x40, 0xA0, 0x40), IsAntialias = true, Style = SKPaintStyle.Fill };

        _masterMeter = new LevelMeter();

        // Initialize knob widgets with compact style (no shadow, inner circle, or labels)
        var compactStyle = KnobStyle.Compact;

        for (int i = 0; i < 3; i++)
        {
            SlotGainKnobs[i] = new KnobWidget(TinyKnobRadius, -60f, 12f, "GAIN", "dB", compactStyle, _theme)
            {
                ValueFormat = "0.0",
                ShowPositiveSign = true
            };
            SlotFreqKnobs[i] = new KnobWidget(TinyKnobRadius, 20f, 20000f, "FREQ", "Hz", compactStyle, _theme)
            {
                IsLogarithmic = true,
                ValueFormat = "0"
            };
            SlotSweepStartKnobs[i] = new KnobWidget(TinyKnobRadius, 20f, 20000f, "START", "Hz", compactStyle, _theme)
            {
                IsLogarithmic = true,
                ValueFormat = "0"
            };
            SlotSweepEndKnobs[i] = new KnobWidget(TinyKnobRadius, 20f, 20000f, "END", "Hz", compactStyle, _theme)
            {
                IsLogarithmic = true,
                ValueFormat = "0"
            };
            SlotSweepDurKnobs[i] = new KnobWidget(TinyKnobRadius, 100f, 30000f, "DUR", "ms", compactStyle, _theme)
            {
                ValueFormat = "0"
            };
            SlotPulseWidthKnobs[i] = new KnobWidget(TinyKnobRadius, 0.1f, 0.9f, "PW", "%", compactStyle, _theme)
            {
                ValueFormat = "0.0"
            };
            SlotIntervalKnobs[i] = new KnobWidget(TinyKnobRadius, 10f, 5000f, "INT", "ms", compactStyle, _theme)
            {
                ValueFormat = "0"
            };
            SlotChirpDurKnobs[i] = new KnobWidget(TinyKnobRadius, 50f, 500f, "DUR", "ms", compactStyle, _theme)
            {
                ValueFormat = "0"
            };
            SlotSpeedKnobs[i] = new KnobWidget(TinyKnobRadius, 0.5f, 2.0f, "SPD", "x", compactStyle, _theme)
            {
                ValueFormat = "0.00"
            };
            SlotTrimStartKnobs[i] = new KnobWidget(TinyKnobRadius, 0f, 1f, "IN", "", compactStyle, _theme)
            {
                ValueFormat = "0.00"
            };
            SlotTrimEndKnobs[i] = new KnobWidget(TinyKnobRadius, 0f, 1f, "OUT", "", compactStyle, _theme)
            {
                ValueFormat = "0.00"
            };
        }

        MasterGainKnob = new KnobWidget(SmallKnobRadius, -60f, 12f, "MASTER", "dB", compactStyle, _theme)
        {
            ValueFormat = "0.0",
            ShowPositiveSign = true
        };
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
            RenderSlotRow(canvas, i, _slotRects[i], state.Slots[i]);
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

        using var bypassTextPaint = new SkiaTextPaint(state.IsBypassed ? _theme.TextPrimary : _theme.TextSecondary, 9f, SKFontStyle.Bold, SKTextAlign.Center);
        canvas.DrawText("BYPASS", _bypassButtonRect.MidX, _bypassButtonRect.MidY + 3, bypassTextPaint);

        // Close button
        _closeButtonRect = new SKRect(size.Width - Padding - 20, (TitleBarHeight - 20) / 2,
            size.Width - Padding, (TitleBarHeight + 20) / 2);
        canvas.DrawText("\u00D7", _closeButtonRect.MidX, _closeButtonRect.MidY + 5, _closeButtonPaint);
    }

    private void RenderSlotRow(SKCanvas canvas, int slotIndex, SKRect rect, SlotRenderState slot)
    {
        // Row background
        var slotRound = new SKRoundRect(rect, 4f);
        canvas.DrawRoundRect(slotRound, _slotBackgroundPaint);

        float x = rect.Left + 6;
        float centerY = rect.MidY;

        // Slot label
        using var slotLabelPaint = new SkiaTextPaint(_theme.TextSecondary, 10f, SKFontStyle.Bold, SKTextAlign.Left);
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
        x = RenderTypeSpecificControls(canvas, slotIndex, slot, x, rect, centerY);

        // Gain knob (always shown, positioned after type-specific controls)
        float gainX = rect.Right - 100;
        SlotGainKnobs[slotIndex].Center = new SKPoint(gainX, centerY);
        SlotGainKnobs[slotIndex].Value = slot.GainDb;
        SlotGainKnobs[slotIndex].Render(canvas);
        canvas.DrawText("GAIN", gainX, centerY + TinyKnobRadius + 8, _labelPaint);

        // REC button (captures input to this slot's sample buffer)
        float recX = rect.Right - 68;
        float recWidth = 24f;
        _slotRecordRects[slotIndex] = new SKRect(recX, centerY - 8, recX + recWidth, centerY + 8);
        var recRound = new SKRoundRect(_slotRecordRects[slotIndex], 3f);

        // Highlight background when recording
        if (slot.IsRecording)
        {
            using var recBgPaint = new SKPaint { Color = new SKColor(0x60, 0x20, 0x20), IsAntialias = true, Style = SKPaintStyle.Fill };
            canvas.DrawRoundRect(recRound, recBgPaint);
        }
        else
        {
            canvas.DrawRoundRect(recRound, _dropdownPaint);
        }
        canvas.DrawRoundRect(recRound, _borderPaint);

        // Red record circle - brighter when recording
        var circleColor = slot.IsRecording ? new SKColor(0xFF, 0x00, 0x00) : new SKColor(0xFF, 0x30, 0x30);
        using var recCirclePaint = new SKPaint { Color = circleColor, IsAntialias = true, Style = SKPaintStyle.Fill };
        float circleRadius = slot.IsRecording ? 5f : 4f;
        canvas.DrawCircle(recX + recWidth / 2, centerY, circleRadius, recCirclePaint);

        // Mute/Solo buttons at right edge
        float buttonX = rect.Right - 42;
        float buttonWidth = 18f;

        _slotMuteRects[slotIndex] = new SKRect(buttonX, centerY - 8, buttonX + buttonWidth, centerY + 8);
        var muteRound = new SKRoundRect(_slotMuteRects[slotIndex], 3f);
        canvas.DrawRoundRect(muteRound, slot.IsMuted ? _mutedPaint : _mutePaint);
        canvas.DrawRoundRect(muteRound, _borderPaint);
        using var muteTextPaint = new SkiaTextPaint(slot.IsMuted ? _theme.TextPrimary : _theme.TextSecondary, 9f, SKFontStyle.Bold, SKTextAlign.Center);
        canvas.DrawText("M", _slotMuteRects[slotIndex].MidX, _slotMuteRects[slotIndex].MidY + 3, muteTextPaint);

        buttonX += buttonWidth + 3;
        _slotSoloRects[slotIndex] = new SKRect(buttonX, centerY - 8, buttonX + buttonWidth, centerY + 8);
        var soloRound = new SKRoundRect(_slotSoloRects[slotIndex], 3f);
        canvas.DrawRoundRect(soloRound, slot.IsSolo ? _soloedPaint : _soloPaint);
        canvas.DrawRoundRect(soloRound, _borderPaint);
        using var soloTextPaint = new SkiaTextPaint(slot.IsSolo ? _theme.PanelBackground : _theme.TextSecondary, 9f, SKFontStyle.Bold, SKTextAlign.Center);
        canvas.DrawText("S", _slotSoloRects[slotIndex].MidX, _slotSoloRects[slotIndex].MidY + 3, soloTextPaint);
    }

    private float RenderTypeSpecificControls(SKCanvas canvas, int slotIndex, SlotRenderState slot, float x, SKRect rect, float centerY)
    {
        // Reset off-screen positions for unused knobs
        var offScreen = new SKPoint(-100, -100);
        SlotFreqKnobs[slotIndex].Center = offScreen;
        SlotSweepStartKnobs[slotIndex].Center = offScreen;
        SlotSweepEndKnobs[slotIndex].Center = offScreen;
        SlotSweepDurKnobs[slotIndex].Center = offScreen;
        SlotPulseWidthKnobs[slotIndex].Center = offScreen;
        SlotIntervalKnobs[slotIndex].Center = offScreen;
        SlotChirpDurKnobs[slotIndex].Center = offScreen;
        SlotSpeedKnobs[slotIndex].Center = offScreen;
        SlotTrimStartKnobs[slotIndex].Center = offScreen;
        SlotTrimEndKnobs[slotIndex].Center = offScreen;
        _slotSweepToggleRects[slotIndex] = SKRect.Empty;
        _slotLoopModeRects[slotIndex] = SKRect.Empty;
        _slotSaveSampleRects[slotIndex] = SKRect.Empty;
        _slotLoadSampleRects[slotIndex] = SKRect.Empty;
        _slotReloadSampleRects[slotIndex] = SKRect.Empty;

        switch (slot.Type)
        {
            case GeneratorType.Sine:
            case GeneratorType.Square:
            case GeneratorType.Saw:
            case GeneratorType.Triangle:
                x = RenderOscillatorControls(canvas, slotIndex, slot, x, centerY);
                break;

            case GeneratorType.WhiteNoise:
            case GeneratorType.PinkNoise:
            case GeneratorType.BrownNoise:
            case GeneratorType.BlueNoise:
            case GeneratorType.DcTest:
                // Noise and DC test have no extra controls
                break;

            case GeneratorType.Impulse:
                x = RenderImpulseControls(canvas, slotIndex, slot, x, centerY);
                break;

            case GeneratorType.Chirp:
                x = RenderChirpControls(canvas, slotIndex, slot, x, centerY);
                break;

            case GeneratorType.Sample:
                x = RenderSampleControls(canvas, slotIndex, slot, x, centerY);
                break;
        }

        return x;
    }

    private float RenderOscillatorControls(SKCanvas canvas, int slotIndex, SlotRenderState slot, float x, float centerY)
    {
        // Frequency knob
        SlotFreqKnobs[slotIndex].Center = new SKPoint(x + TinyKnobRadius, centerY);
        SlotFreqKnobs[slotIndex].Value = slot.Frequency;
        SlotFreqKnobs[slotIndex].Render(canvas);
        canvas.DrawText("FREQ", x + TinyKnobRadius, centerY + TinyKnobRadius + 8, _labelPaint);
        x += TinyKnobRadius * 2 + 8;

        // Sweep toggle
        float toggleWidth = 36f;
        _slotSweepToggleRects[slotIndex] = new SKRect(x, centerY - 8, x + toggleWidth, centerY + 8);
        var toggleRound = new SKRoundRect(_slotSweepToggleRects[slotIndex], 3f);
        canvas.DrawRoundRect(toggleRound, slot.SweepEnabled ? _toggleOnPaint : _toggleOffPaint);
        canvas.DrawRoundRect(toggleRound, _borderPaint);
        using var sweepTextPaint = new SkiaTextPaint(slot.SweepEnabled ? _theme.TextPrimary : _theme.TextSecondary, 8f, SKFontStyle.Normal, SKTextAlign.Center);
        canvas.DrawText("SWEEP", _slotSweepToggleRects[slotIndex].MidX, centerY + 3, sweepTextPaint);
        x += toggleWidth + 4;

        // Sweep controls (only if sweep enabled)
        if (slot.SweepEnabled)
        {
            // Start Hz
            SlotSweepStartKnobs[slotIndex].Center = new SKPoint(x + TinyKnobRadius, centerY);
            SlotSweepStartKnobs[slotIndex].Value = slot.SweepStartHz;
            SlotSweepStartKnobs[slotIndex].Render(canvas);
            canvas.DrawText("START", x + TinyKnobRadius, centerY + TinyKnobRadius + 8, _labelPaint);
            x += TinyKnobRadius * 2 + 4;

            // End Hz
            SlotSweepEndKnobs[slotIndex].Center = new SKPoint(x + TinyKnobRadius, centerY);
            SlotSweepEndKnobs[slotIndex].Value = slot.SweepEndHz;
            SlotSweepEndKnobs[slotIndex].Render(canvas);
            canvas.DrawText("END", x + TinyKnobRadius, centerY + TinyKnobRadius + 8, _labelPaint);
            x += TinyKnobRadius * 2 + 4;

            // Duration
            SlotSweepDurKnobs[slotIndex].Center = new SKPoint(x + TinyKnobRadius, centerY);
            SlotSweepDurKnobs[slotIndex].Value = slot.SweepDurationMs;
            SlotSweepDurKnobs[slotIndex].Render(canvas);
            canvas.DrawText("DUR", x + TinyKnobRadius, centerY + TinyKnobRadius + 8, _labelPaint);
            x += TinyKnobRadius * 2 + 4;
        }

        // Pulse width for square wave
        if (slot.Type == GeneratorType.Square)
        {
            SlotPulseWidthKnobs[slotIndex].Center = new SKPoint(x + TinyKnobRadius, centerY);
            SlotPulseWidthKnobs[slotIndex].Value = slot.PulseWidth;
            SlotPulseWidthKnobs[slotIndex].Render(canvas);
            canvas.DrawText("PW", x + TinyKnobRadius, centerY + TinyKnobRadius + 8, _labelPaint);
            x += TinyKnobRadius * 2 + 4;
        }

        return x;
    }

    private float RenderImpulseControls(SKCanvas canvas, int slotIndex, SlotRenderState slot, float x, float centerY)
    {
        // Interval knob
        SlotIntervalKnobs[slotIndex].Center = new SKPoint(x + TinyKnobRadius, centerY);
        SlotIntervalKnobs[slotIndex].Value = slot.ImpulseIntervalMs;
        SlotIntervalKnobs[slotIndex].Render(canvas);
        canvas.DrawText("INT", x + TinyKnobRadius, centerY + TinyKnobRadius + 8, _labelPaint);
        x += TinyKnobRadius * 2 + 8;

        return x;
    }

    private float RenderChirpControls(SKCanvas canvas, int slotIndex, SlotRenderState slot, float x, float centerY)
    {
        // Duration knob
        SlotChirpDurKnobs[slotIndex].Center = new SKPoint(x + TinyKnobRadius, centerY);
        SlotChirpDurKnobs[slotIndex].Value = slot.ChirpDurationMs;
        SlotChirpDurKnobs[slotIndex].Render(canvas);
        canvas.DrawText("DUR", x + TinyKnobRadius, centerY + TinyKnobRadius + 8, _labelPaint);
        x += TinyKnobRadius * 2 + 8;

        return x;
    }

    private float RenderSampleControls(SKCanvas canvas, int slotIndex, SlotRenderState slot, float x, float centerY)
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
        x += loopWidth + 4;

        // Sample action buttons (Save/Load/Reload) - compact row
        float btnWidth = 18f;
        float btnHeight = 14f;
        float btnY = centerY - btnHeight / 2;

        // Save button
        _slotSaveSampleRects[slotIndex] = new SKRect(x, btnY, x + btnWidth, btnY + btnHeight);
        var saveRound = new SKRoundRect(_slotSaveSampleRects[slotIndex], 2f);
        canvas.DrawRoundRect(saveRound, slot.HasSample ? _toggleOnPaint : _toggleOffPaint);
        canvas.DrawRoundRect(saveRound, _borderPaint);
        using (var savePaint = new SkiaTextPaint(slot.HasSample ? _theme.TextPrimary : _theme.TextMuted, 7f, SKFontStyle.Bold, SKTextAlign.Center))
            canvas.DrawText("S", _slotSaveSampleRects[slotIndex].MidX, _slotSaveSampleRects[slotIndex].MidY + 2.5f, savePaint);
        x += btnWidth + 2;

        // Load button
        _slotLoadSampleRects[slotIndex] = new SKRect(x, btnY, x + btnWidth, btnY + btnHeight);
        var loadRound = new SKRoundRect(_slotLoadSampleRects[slotIndex], 2f);
        canvas.DrawRoundRect(loadRound, _dropdownPaint);
        canvas.DrawRoundRect(loadRound, _borderPaint);
        using (var loadPaint = new SkiaTextPaint(_theme.TextSecondary, 7f, SKFontStyle.Bold, SKTextAlign.Center))
            canvas.DrawText("L", _slotLoadSampleRects[slotIndex].MidX, _slotLoadSampleRects[slotIndex].MidY + 2.5f, loadPaint);
        x += btnWidth + 2;

        // Reload button
        _slotReloadSampleRects[slotIndex] = new SKRect(x, btnY, x + btnWidth, btnY + btnHeight);
        var reloadRound = new SKRoundRect(_slotReloadSampleRects[slotIndex], 2f);
        canvas.DrawRoundRect(reloadRound, _dropdownPaint);
        canvas.DrawRoundRect(reloadRound, _borderPaint);
        using (var reloadPaint = new SkiaTextPaint(_theme.TextSecondary, 7f, SKFontStyle.Bold, SKTextAlign.Center))
            canvas.DrawText("R", _slotReloadSampleRects[slotIndex].MidX, _slotReloadSampleRects[slotIndex].MidY + 2.5f, reloadPaint);
        x += btnWidth + 4;

        // Speed knob
        SlotSpeedKnobs[slotIndex].Center = new SKPoint(x + TinyKnobRadius, centerY);
        SlotSpeedKnobs[slotIndex].Value = slot.SampleSpeed;
        SlotSpeedKnobs[slotIndex].Render(canvas);
        canvas.DrawText("SPD", x + TinyKnobRadius, centerY + TinyKnobRadius + 8, _labelPaint);
        x += TinyKnobRadius * 2 + 4;

        // Trim start
        SlotTrimStartKnobs[slotIndex].Center = new SKPoint(x + TinyKnobRadius, centerY);
        SlotTrimStartKnobs[slotIndex].Value = slot.TrimStart;
        SlotTrimStartKnobs[slotIndex].Render(canvas);
        canvas.DrawText("IN", x + TinyKnobRadius, centerY + TinyKnobRadius + 8, _labelPaint);
        x += TinyKnobRadius * 2 + 4;

        // Trim end
        SlotTrimEndKnobs[slotIndex].Center = new SKPoint(x + TinyKnobRadius, centerY);
        SlotTrimEndKnobs[slotIndex].Value = slot.TrimEnd;
        SlotTrimEndKnobs[slotIndex].Render(canvas);
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
        using var masterLabelPaint = new SkiaTextPaint(_theme.TextSecondary, 10f, SKFontStyle.Bold, SKTextAlign.Left);
        canvas.DrawText("MASTER", px, centerY + 3, masterLabelPaint);
        px += 52;

        // Master gain knob
        MasterGainKnob.Center = new SKPoint(px + SmallKnobRadius, centerY);
        MasterGainKnob.Value = state.MasterGainDb;
        MasterGainKnob.Render(canvas);
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
        px += hrWidth + 16;

        // Hint text
        using var hintPaint = new SkiaTextPaint(_theme.TextMuted, 9f, SKFontStyle.Normal, SKTextAlign.Left);
        canvas.DrawText("Drop WAV or click ● to capture input", px, centerY + 3, hintPaint);
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

        if (_masterHeadroomRect.Contains(x, y))
            return new SignalGeneratorHitTest(SignalGeneratorHitArea.MasterHeadroomDropdown, -1);

        // Check master gain knob
        if (MasterGainKnob.HitTest(x, y))
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

            if (_slotRecordRects[i].Contains(x, y))
                return new SignalGeneratorHitTest(SignalGeneratorHitArea.RecordButton, i);

            if (SlotGainKnobs[i].HitTest(x, y))
                return new SignalGeneratorHitTest(SignalGeneratorHitArea.SlotGainKnob, i);

            if (SlotFreqKnobs[i].HitTest(x, y))
                return new SignalGeneratorHitTest(SignalGeneratorHitArea.SlotFreqKnob, i);

            if (_slotSweepToggleRects[i].Contains(x, y))
                return new SignalGeneratorHitTest(SignalGeneratorHitArea.SlotSweepToggle, i);

            if (SlotSweepStartKnobs[i].HitTest(x, y))
                return new SignalGeneratorHitTest(SignalGeneratorHitArea.SlotSweepStartKnob, i);

            if (SlotSweepEndKnobs[i].HitTest(x, y))
                return new SignalGeneratorHitTest(SignalGeneratorHitArea.SlotSweepEndKnob, i);

            if (SlotSweepDurKnobs[i].HitTest(x, y))
                return new SignalGeneratorHitTest(SignalGeneratorHitArea.SlotSweepDurKnob, i);

            if (SlotPulseWidthKnobs[i].HitTest(x, y))
                return new SignalGeneratorHitTest(SignalGeneratorHitArea.SlotPulseWidthKnob, i);

            if (SlotIntervalKnobs[i].HitTest(x, y))
                return new SignalGeneratorHitTest(SignalGeneratorHitArea.SlotIntervalKnob, i);

            if (SlotChirpDurKnobs[i].HitTest(x, y))
                return new SignalGeneratorHitTest(SignalGeneratorHitArea.SlotChirpDurKnob, i);

            if (_slotLoopModeRects[i].Contains(x, y))
                return new SignalGeneratorHitTest(SignalGeneratorHitArea.SlotLoopModeDropdown, i);

            if (_slotSaveSampleRects[i].Contains(x, y))
                return new SignalGeneratorHitTest(SignalGeneratorHitArea.SaveSampleButton, i);

            if (_slotLoadSampleRects[i].Contains(x, y))
                return new SignalGeneratorHitTest(SignalGeneratorHitArea.LoadSampleButton, i);

            if (_slotReloadSampleRects[i].Contains(x, y))
                return new SignalGeneratorHitTest(SignalGeneratorHitArea.ReloadSampleButton, i);

            if (SlotSpeedKnobs[i].HitTest(x, y))
                return new SignalGeneratorHitTest(SignalGeneratorHitArea.SlotSpeedKnob, i);

            if (SlotTrimStartKnobs[i].HitTest(x, y))
                return new SignalGeneratorHitTest(SignalGeneratorHitArea.SlotTrimStartKnob, i);

            if (SlotTrimEndKnobs[i].HitTest(x, y))
                return new SignalGeneratorHitTest(SignalGeneratorHitArea.SlotTrimEndKnob, i);

            if (_slotRects[i].Contains(x, y))
                return new SignalGeneratorHitTest(SignalGeneratorHitArea.SlotArea, i);
        }

        if (_titleBarRect.Contains(x, y))
            return new SignalGeneratorHitTest(SignalGeneratorHitArea.TitleBar, -1);

        return new SignalGeneratorHitTest(SignalGeneratorHitArea.None, -1);
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
        _dropdownPaint.Dispose();
        _toggleOffPaint.Dispose();
        _toggleOnPaint.Dispose();

        // Dispose knob widgets
        for (int i = 0; i < 3; i++)
        {
            SlotGainKnobs[i].Dispose();
            SlotFreqKnobs[i].Dispose();
            SlotSweepStartKnobs[i].Dispose();
            SlotSweepEndKnobs[i].Dispose();
            SlotSweepDurKnobs[i].Dispose();
            SlotPulseWidthKnobs[i].Dispose();
            SlotIntervalKnobs[i].Dispose();
            SlotChirpDurKnobs[i].Dispose();
            SlotSpeedKnobs[i].Dispose();
            SlotTrimStartKnobs[i].Dispose();
            SlotTrimEndKnobs[i].Dispose();
        }
        MasterGainKnob.Dispose();
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
    public bool IsRecording;
    public bool HasSample;
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
    LoadSampleButton,
    SaveSampleButton,
    ReloadSampleButton
}

public record struct SignalGeneratorHitTest(SignalGeneratorHitArea Area, int SlotIndex);
