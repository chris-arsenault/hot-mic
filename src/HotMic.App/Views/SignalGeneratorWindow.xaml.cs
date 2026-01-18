using System;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using HotMic.App.UI.PluginComponents;
using HotMic.Core.Plugins.BuiltIn;
using HotMic.Core.Presets;
using NAudio.Wave;
using SkiaSharp;
using SkiaSharp.Views.WPF;

namespace HotMic.App.Views;

public partial class SignalGeneratorWindow : Window, IDisposable
{
    private readonly SignalGeneratorRenderer _renderer;
    private readonly SignalGeneratorPlugin _plugin;
    private readonly Action<int, float> _parameterCallback;
    private readonly Action<bool> _bypassCallback;
    private readonly DispatcherTimer _renderTimer;
    private readonly PluginPresetHelper _presetHelper;

    private float _smoothedOutputLevel;
    private bool _disposed;
    private readonly float[] _smoothedSlotLevels = new float[3];

    // Sample persistence
    private readonly string _sampleStoragePath;
    private int _lastSavedSlot = -1;
    private int _lastSavedSampleRate;

    public SignalGeneratorWindow(SignalGeneratorPlugin plugin, Action<int, float> parameterCallback, Action<bool> bypassCallback)
    {
        InitializeComponent();
        _plugin = plugin;
        _parameterCallback = parameterCallback;
        _bypassCallback = bypassCallback;
        _renderer = new SignalGeneratorRenderer();

        _presetHelper = new PluginPresetHelper(
            plugin.Id,
            PluginPresetManager.Default,
            ApplyPreset,
            GetCurrentParameters);

        // Initialize sample storage path
        var basePath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        _sampleStoragePath = Path.Combine(basePath, "HotMic", "signal-generator-sample.raw");

        // Subscribe to sample loaded events for auto-save
        _plugin.SampleLoaded += OnSampleLoaded;

        // Try to load persisted sample on startup
        TryLoadPersistedSample();

        var preferredSize = SignalGeneratorRenderer.GetPreferredSize();
        Width = preferredSize.Width;
        Height = preferredSize.Height;

        // Wire up KnobWidget ValueChanged events
        WireUpKnobEvents();

        _renderTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        _renderTimer.Tick += OnRenderTick;
        Loaded += (_, _) => _renderTimer.Start();
        Closed += (_, _) => Dispose();
    }

    private void WireUpKnobEvents()
    {
        // Master gain knob
        _renderer.MasterGainKnob.ValueChanged += value =>
        {
            _parameterCallback(SignalGeneratorPlugin.MasterGainIndex, value);
            _presetHelper.MarkAsCustom();
        };

        // Per-slot knobs
        for (int i = 0; i < 3; i++)
        {
            int slotIndex = i; // Capture for closure
            int baseIndex = slotIndex * 20;

            _renderer.SlotGainKnobs[i].ValueChanged += value =>
            {
                _parameterCallback(baseIndex + SignalGeneratorPlugin.GainIndex, value);
                _presetHelper.MarkAsCustom();
            };

            _renderer.SlotFreqKnobs[i].ValueChanged += value =>
            {
                _parameterCallback(baseIndex + SignalGeneratorPlugin.FrequencyIndex, value);
                _presetHelper.MarkAsCustom();
            };

            _renderer.SlotSweepStartKnobs[i].ValueChanged += value =>
            {
                _parameterCallback(baseIndex + SignalGeneratorPlugin.SweepStartHzIndex, value);
                _presetHelper.MarkAsCustom();
            };

            _renderer.SlotSweepEndKnobs[i].ValueChanged += value =>
            {
                _parameterCallback(baseIndex + SignalGeneratorPlugin.SweepEndHzIndex, value);
                _presetHelper.MarkAsCustom();
            };

            _renderer.SlotSweepDurKnobs[i].ValueChanged += value =>
            {
                _parameterCallback(baseIndex + SignalGeneratorPlugin.SweepDurationMsIndex, value);
                _presetHelper.MarkAsCustom();
            };

            _renderer.SlotPulseWidthKnobs[i].ValueChanged += value =>
            {
                _parameterCallback(baseIndex + SignalGeneratorPlugin.PulseWidthIndex, value);
                _presetHelper.MarkAsCustom();
            };

            _renderer.SlotIntervalKnobs[i].ValueChanged += value =>
            {
                _parameterCallback(baseIndex + SignalGeneratorPlugin.ImpulseIntervalMsIndex, value);
                _presetHelper.MarkAsCustom();
            };

            _renderer.SlotChirpDurKnobs[i].ValueChanged += value =>
            {
                _parameterCallback(baseIndex + SignalGeneratorPlugin.ChirpDurationMsIndex, value);
                _presetHelper.MarkAsCustom();
            };

            _renderer.SlotSpeedKnobs[i].ValueChanged += value =>
            {
                _parameterCallback(baseIndex + SignalGeneratorPlugin.SampleSpeedIndex, value);
                _presetHelper.MarkAsCustom();
            };

            _renderer.SlotTrimStartKnobs[i].ValueChanged += value =>
            {
                _parameterCallback(baseIndex + SignalGeneratorPlugin.SampleTrimStartIndex, value);
                _presetHelper.MarkAsCustom();
            };

            _renderer.SlotTrimEndKnobs[i].ValueChanged += value =>
            {
                _parameterCallback(baseIndex + SignalGeneratorPlugin.SampleTrimEndIndex, value);
                _presetHelper.MarkAsCustom();
            };
        }
    }

    private void OnRenderTick(object? sender, EventArgs e)
    {
        float rawOutput = _plugin.GetOutputLevel();
        _smoothedOutputLevel = _smoothedOutputLevel * 0.7f + rawOutput * 0.3f;

        for (int i = 0; i < 3; i++)
        {
            float rawSlot = _plugin.GetSlotLevel(i);
            _smoothedSlotLevels[i] = _smoothedSlotLevels[i] * 0.7f + rawSlot * 0.3f;
        }

        SkiaCanvas.InvalidateVisual();
    }

    private void SkiaCanvas_PaintSurface(object sender, SKPaintSurfaceEventArgs e)
    {
        var canvas = e.Surface.Canvas;
        var size = new SKSize(e.Info.Width, e.Info.Height);
        float dpiScale = GetDpiScale();

        var masterState = _plugin.GetMasterState();
        var state = new SignalGeneratorState
        {
            IsBypassed = _plugin.IsBypassed,
            PresetName = _presetHelper.CurrentPresetName,
            OutputLevel = _smoothedOutputLevel,
            MasterGainDb = masterState.GainDb,
            HeadroomMode = masterState.Headroom
        };

        // Build slot states from plugin
        int recordingSlot = _plugin.RecordingTargetSlot;
        for (int i = 0; i < 3; i++)
        {
            var slotState = _plugin.GetSlotState(i);
            state.Slots[i] = new SlotRenderState
            {
                Type = slotState.Type,
                Frequency = slotState.Frequency,
                GainDb = slotState.GainDb,
                IsMuted = slotState.Muted,
                IsSolo = slotState.Solo,
                SweepEnabled = slotState.SweepEnabled,
                SweepStartHz = slotState.SweepStartHz,
                SweepEndHz = slotState.SweepEndHz,
                SweepDurationMs = slotState.SweepDurationMs,
                PulseWidth = slotState.PulseWidth,
                ImpulseIntervalMs = slotState.ImpulseIntervalMs,
                ChirpDurationMs = slotState.ChirpDurationMs,
                LoopMode = slotState.LoopMode,
                SampleSpeed = slotState.SampleSpeed,
                TrimStart = slotState.TrimStart,
                TrimEnd = slotState.TrimEnd,
                Level = _smoothedSlotLevels[i],
                IsRecording = recordingSlot == i,
                HasSample = _plugin.HasSample(i)
            };
        }

        _renderer.Render(canvas, size, dpiScale, state);
    }

    private void SkiaCanvas_MouseDown(object sender, MouseButtonEventArgs e)
    {
        var pos = e.GetPosition(SkiaCanvas);
        float x = (float)pos.X;
        float y = (float)pos.Y;

        // Check if any knob handles the mouse first
        if (TryHandleKnobMouseDown(x, y, e.ChangedButton))
        {
            if (GetActiveKnob()?.IsDragging == true)
            {
                SkiaCanvas.CaptureMouse();
            }
            e.Handled = true;
            return;
        }

        if (e.ChangedButton != MouseButton.Left)
            return;

        var hit = _renderer.HitTest(x, y);

        switch (hit.Area)
        {
            case SignalGeneratorHitArea.TitleBar:
                DragMove();
                e.Handled = true;
                break;

            case SignalGeneratorHitArea.CloseButton:
                Close();
                e.Handled = true;
                break;

            case SignalGeneratorHitArea.BypassButton:
                _bypassCallback(!_plugin.IsBypassed);
                e.Handled = true;
                break;

            case SignalGeneratorHitArea.PresetDropdown:
                _presetHelper.ShowPresetMenu(SkiaCanvas, _renderer.GetPresetDropdownRect());
                e.Handled = true;
                break;

            case SignalGeneratorHitArea.PresetSave:
                _presetHelper.ShowSaveMenu(SkiaCanvas, this);
                e.Handled = true;
                break;

            case SignalGeneratorHitArea.SlotTypeSelector:
                ShowTypeMenu(hit.SlotIndex);
                e.Handled = true;
                break;

            case SignalGeneratorHitArea.SlotMuteButton:
                ToggleSlotMute(hit.SlotIndex);
                e.Handled = true;
                break;

            case SignalGeneratorHitArea.SlotSoloButton:
                ToggleSlotSolo(hit.SlotIndex);
                e.Handled = true;
                break;

            case SignalGeneratorHitArea.SlotSweepToggle:
                ToggleSweep(hit.SlotIndex);
                e.Handled = true;
                break;

            case SignalGeneratorHitArea.SlotLoopModeDropdown:
                ShowLoopModeMenu(hit.SlotIndex);
                e.Handled = true;
                break;

            case SignalGeneratorHitArea.MasterHeadroomDropdown:
                ShowHeadroomMenu();
                e.Handled = true;
                break;

            case SignalGeneratorHitArea.RecordButton:
                ToggleRecordingToSlot(hit.SlotIndex);
                e.Handled = true;
                break;

            case SignalGeneratorHitArea.LoadSampleButton:
                LoadSampleFile(hit.SlotIndex);
                e.Handled = true;
                break;

            case SignalGeneratorHitArea.SaveSampleButton:
                SaveSampleToFile(hit.SlotIndex);
                e.Handled = true;
                break;

            case SignalGeneratorHitArea.ReloadSampleButton:
                ReloadPersistedSample(hit.SlotIndex);
                e.Handled = true;
                break;
        }
    }

    private bool TryHandleKnobMouseDown(float x, float y, MouseButton button)
    {
        // Check master gain knob
        if (_renderer.MasterGainKnob.HandleMouseDown(x, y, button, SkiaCanvas))
            return true;

        // Check all slot knobs
        for (int i = 0; i < 3; i++)
        {
            if (_renderer.SlotGainKnobs[i].HandleMouseDown(x, y, button, SkiaCanvas))
                return true;
            if (_renderer.SlotFreqKnobs[i].HandleMouseDown(x, y, button, SkiaCanvas))
                return true;
            if (_renderer.SlotSweepStartKnobs[i].HandleMouseDown(x, y, button, SkiaCanvas))
                return true;
            if (_renderer.SlotSweepEndKnobs[i].HandleMouseDown(x, y, button, SkiaCanvas))
                return true;
            if (_renderer.SlotSweepDurKnobs[i].HandleMouseDown(x, y, button, SkiaCanvas))
                return true;
            if (_renderer.SlotPulseWidthKnobs[i].HandleMouseDown(x, y, button, SkiaCanvas))
                return true;
            if (_renderer.SlotIntervalKnobs[i].HandleMouseDown(x, y, button, SkiaCanvas))
                return true;
            if (_renderer.SlotChirpDurKnobs[i].HandleMouseDown(x, y, button, SkiaCanvas))
                return true;
            if (_renderer.SlotSpeedKnobs[i].HandleMouseDown(x, y, button, SkiaCanvas))
                return true;
            if (_renderer.SlotTrimStartKnobs[i].HandleMouseDown(x, y, button, SkiaCanvas))
                return true;
            if (_renderer.SlotTrimEndKnobs[i].HandleMouseDown(x, y, button, SkiaCanvas))
                return true;
        }

        return false;
    }

    private KnobWidget? GetActiveKnob()
    {
        if (_renderer.MasterGainKnob.IsDragging)
            return _renderer.MasterGainKnob;

        for (int i = 0; i < 3; i++)
        {
            if (_renderer.SlotGainKnobs[i].IsDragging) return _renderer.SlotGainKnobs[i];
            if (_renderer.SlotFreqKnobs[i].IsDragging) return _renderer.SlotFreqKnobs[i];
            if (_renderer.SlotSweepStartKnobs[i].IsDragging) return _renderer.SlotSweepStartKnobs[i];
            if (_renderer.SlotSweepEndKnobs[i].IsDragging) return _renderer.SlotSweepEndKnobs[i];
            if (_renderer.SlotSweepDurKnobs[i].IsDragging) return _renderer.SlotSweepDurKnobs[i];
            if (_renderer.SlotPulseWidthKnobs[i].IsDragging) return _renderer.SlotPulseWidthKnobs[i];
            if (_renderer.SlotIntervalKnobs[i].IsDragging) return _renderer.SlotIntervalKnobs[i];
            if (_renderer.SlotChirpDurKnobs[i].IsDragging) return _renderer.SlotChirpDurKnobs[i];
            if (_renderer.SlotSpeedKnobs[i].IsDragging) return _renderer.SlotSpeedKnobs[i];
            if (_renderer.SlotTrimStartKnobs[i].IsDragging) return _renderer.SlotTrimStartKnobs[i];
            if (_renderer.SlotTrimEndKnobs[i].IsDragging) return _renderer.SlotTrimEndKnobs[i];
        }

        return null;
    }

    private void SkiaCanvas_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        var pos = e.GetPosition(SkiaCanvas);
        float x = (float)pos.X;
        float y = (float)pos.Y;
        bool isLeftDown = e.LeftButton == MouseButtonState.Pressed;

        // Forward to all knobs (handles both dragging and hover updates)
        ForwardMouseMoveToKnobs(x, y, isLeftDown);
    }

    private void ForwardMouseMoveToKnobs(float x, float y, bool isLeftButtonDown)
    {
        _renderer.MasterGainKnob.HandleMouseMove(x, y, isLeftButtonDown);

        for (int i = 0; i < 3; i++)
        {
            _renderer.SlotGainKnobs[i].HandleMouseMove(x, y, isLeftButtonDown);
            _renderer.SlotFreqKnobs[i].HandleMouseMove(x, y, isLeftButtonDown);
            _renderer.SlotSweepStartKnobs[i].HandleMouseMove(x, y, isLeftButtonDown);
            _renderer.SlotSweepEndKnobs[i].HandleMouseMove(x, y, isLeftButtonDown);
            _renderer.SlotSweepDurKnobs[i].HandleMouseMove(x, y, isLeftButtonDown);
            _renderer.SlotPulseWidthKnobs[i].HandleMouseMove(x, y, isLeftButtonDown);
            _renderer.SlotIntervalKnobs[i].HandleMouseMove(x, y, isLeftButtonDown);
            _renderer.SlotChirpDurKnobs[i].HandleMouseMove(x, y, isLeftButtonDown);
            _renderer.SlotSpeedKnobs[i].HandleMouseMove(x, y, isLeftButtonDown);
            _renderer.SlotTrimStartKnobs[i].HandleMouseMove(x, y, isLeftButtonDown);
            _renderer.SlotTrimEndKnobs[i].HandleMouseMove(x, y, isLeftButtonDown);
        }
    }

    private void SkiaCanvas_MouseUp(object sender, MouseButtonEventArgs e)
    {
        // Forward to all knobs
        ForwardMouseUpToKnobs(e.ChangedButton);
        SkiaCanvas.ReleaseMouseCapture();
    }

    private void ForwardMouseUpToKnobs(MouseButton button)
    {
        _renderer.MasterGainKnob.HandleMouseUp(button);

        for (int i = 0; i < 3; i++)
        {
            _renderer.SlotGainKnobs[i].HandleMouseUp(button);
            _renderer.SlotFreqKnobs[i].HandleMouseUp(button);
            _renderer.SlotSweepStartKnobs[i].HandleMouseUp(button);
            _renderer.SlotSweepEndKnobs[i].HandleMouseUp(button);
            _renderer.SlotSweepDurKnobs[i].HandleMouseUp(button);
            _renderer.SlotPulseWidthKnobs[i].HandleMouseUp(button);
            _renderer.SlotIntervalKnobs[i].HandleMouseUp(button);
            _renderer.SlotChirpDurKnobs[i].HandleMouseUp(button);
            _renderer.SlotSpeedKnobs[i].HandleMouseUp(button);
            _renderer.SlotTrimStartKnobs[i].HandleMouseUp(button);
            _renderer.SlotTrimEndKnobs[i].HandleMouseUp(button);
        }
    }

    private void SkiaCanvas_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        var pos = e.GetPosition(SkiaCanvas);
        var hit = _renderer.HitTest((float)pos.X, (float)pos.Y);

        if (IsKnobArea(hit.Area))
        {
            float current = GetKnobNormalizedValue(hit.Area, hit.SlotIndex);
            float delta = e.Delta > 0 ? 0.02f : -0.02f;
            float newValue = Math.Clamp(current + delta, 0f, 1f);
            ApplyKnobValue(hit.Area, hit.SlotIndex, newValue);
            e.Handled = true;
        }
    }

    private static bool IsKnobArea(SignalGeneratorHitArea area) => area is
        SignalGeneratorHitArea.SlotGainKnob or
        SignalGeneratorHitArea.SlotFreqKnob or
        SignalGeneratorHitArea.SlotSweepStartKnob or
        SignalGeneratorHitArea.SlotSweepEndKnob or
        SignalGeneratorHitArea.SlotSweepDurKnob or
        SignalGeneratorHitArea.SlotPulseWidthKnob or
        SignalGeneratorHitArea.SlotIntervalKnob or
        SignalGeneratorHitArea.SlotChirpDurKnob or
        SignalGeneratorHitArea.SlotSpeedKnob or
        SignalGeneratorHitArea.SlotTrimStartKnob or
        SignalGeneratorHitArea.SlotTrimEndKnob or
        SignalGeneratorHitArea.MasterGainKnob;

    private void OnDragOver(object sender, System.Windows.DragEventArgs e)
    {
        if (e.Data.GetDataPresent(System.Windows.DataFormats.FileDrop))
        {
            var files = (string[])e.Data.GetData(System.Windows.DataFormats.FileDrop);
            if (files?.Length > 0 && files[0].EndsWith(".wav", StringComparison.OrdinalIgnoreCase))
            {
                e.Effects = System.Windows.DragDropEffects.Copy;
                e.Handled = true;
                return;
            }
        }
        e.Effects = System.Windows.DragDropEffects.None;
        e.Handled = true;
    }

    private void OnDrop(object sender, System.Windows.DragEventArgs e)
    {
        if (e.Data.GetDataPresent(System.Windows.DataFormats.FileDrop))
        {
            var files = (string[])e.Data.GetData(System.Windows.DataFormats.FileDrop);
            if (files?.Length > 0 && files[0].EndsWith(".wav", StringComparison.OrdinalIgnoreCase))
            {
                // Determine which slot based on drop position
                var pos = e.GetPosition(SkiaCanvas);
                var hit = _renderer.HitTest((float)pos.X, (float)pos.Y);
                int targetSlot = hit.SlotIndex >= 0 ? hit.SlotIndex : 0;

                LoadWavFile(files[0], targetSlot);
                e.Handled = true;
            }
        }
    }

    private float GetKnobNormalizedValue(SignalGeneratorHitArea area, int slotIndex)
    {
        var slotState = slotIndex >= 0 ? _plugin.GetSlotState(slotIndex) : default;

        return area switch
        {
            SignalGeneratorHitArea.SlotGainKnob => (slotState.GainDb + 60f) / 72f,
            SignalGeneratorHitArea.SlotFreqKnob => NormalizeFrequency(slotState.Frequency),
            SignalGeneratorHitArea.SlotSweepStartKnob => NormalizeFrequency(slotState.SweepStartHz),
            SignalGeneratorHitArea.SlotSweepEndKnob => NormalizeFrequency(slotState.SweepEndHz),
            SignalGeneratorHitArea.SlotSweepDurKnob => (slotState.SweepDurationMs - 100f) / (30000f - 100f),
            SignalGeneratorHitArea.SlotPulseWidthKnob => (slotState.PulseWidth - 0.1f) / 0.8f,
            SignalGeneratorHitArea.SlotIntervalKnob => (slotState.ImpulseIntervalMs - 10f) / (5000f - 10f),
            SignalGeneratorHitArea.SlotChirpDurKnob => (slotState.ChirpDurationMs - 50f) / (500f - 50f),
            SignalGeneratorHitArea.SlotSpeedKnob => (slotState.SampleSpeed - 0.5f) / 1.5f,
            SignalGeneratorHitArea.SlotTrimStartKnob => slotState.TrimStart,
            SignalGeneratorHitArea.SlotTrimEndKnob => slotState.TrimEnd,
            SignalGeneratorHitArea.MasterGainKnob => (_plugin.MasterGainDb + 60f) / 72f,
            _ => 0f
        };
    }

    private void ApplyKnobValue(SignalGeneratorHitArea area, int slotIndex, float normalized)
    {
        int baseIndex = slotIndex * 20;
        normalized = Math.Clamp(normalized, 0f, 1f);

        switch (area)
        {
            case SignalGeneratorHitArea.SlotGainKnob:
                float gainDb = -60f + normalized * 72f;
                _parameterCallback(baseIndex + SignalGeneratorPlugin.GainIndex, gainDb);
                break;

            case SignalGeneratorHitArea.SlotFreqKnob:
                float freq = DenormalizeFrequency(normalized);
                _parameterCallback(baseIndex + SignalGeneratorPlugin.FrequencyIndex, freq);
                break;

            case SignalGeneratorHitArea.SlotSweepStartKnob:
                float startHz = DenormalizeFrequency(normalized);
                _parameterCallback(baseIndex + SignalGeneratorPlugin.SweepStartHzIndex, startHz);
                break;

            case SignalGeneratorHitArea.SlotSweepEndKnob:
                float endHz = DenormalizeFrequency(normalized);
                _parameterCallback(baseIndex + SignalGeneratorPlugin.SweepEndHzIndex, endHz);
                break;

            case SignalGeneratorHitArea.SlotSweepDurKnob:
                float durMs = 100f + normalized * (30000f - 100f);
                _parameterCallback(baseIndex + SignalGeneratorPlugin.SweepDurationMsIndex, durMs);
                break;

            case SignalGeneratorHitArea.SlotPulseWidthKnob:
                float pw = 0.1f + normalized * 0.8f;
                _parameterCallback(baseIndex + SignalGeneratorPlugin.PulseWidthIndex, pw);
                break;

            case SignalGeneratorHitArea.SlotIntervalKnob:
                float interval = 10f + normalized * (5000f - 10f);
                _parameterCallback(baseIndex + SignalGeneratorPlugin.ImpulseIntervalMsIndex, interval);
                break;

            case SignalGeneratorHitArea.SlotChirpDurKnob:
                float chirpDur = 50f + normalized * (500f - 50f);
                _parameterCallback(baseIndex + SignalGeneratorPlugin.ChirpDurationMsIndex, chirpDur);
                break;

            case SignalGeneratorHitArea.SlotSpeedKnob:
                float speed = 0.5f + normalized * 1.5f;
                _parameterCallback(baseIndex + SignalGeneratorPlugin.SampleSpeedIndex, speed);
                break;

            case SignalGeneratorHitArea.SlotTrimStartKnob:
                _parameterCallback(baseIndex + SignalGeneratorPlugin.SampleTrimStartIndex, normalized);
                break;

            case SignalGeneratorHitArea.SlotTrimEndKnob:
                _parameterCallback(baseIndex + SignalGeneratorPlugin.SampleTrimEndIndex, normalized);
                break;

            case SignalGeneratorHitArea.MasterGainKnob:
                float masterDb = -60f + normalized * 72f;
                _parameterCallback(SignalGeneratorPlugin.MasterGainIndex, masterDb);
                break;

            default:
                return;
        }

        _presetHelper.MarkAsCustom();
    }

    private static float NormalizeFrequency(float hz)
    {
        float logMin = MathF.Log(20f);
        float logMax = MathF.Log(20000f);
        float logHz = MathF.Log(Math.Clamp(hz, 20f, 20000f));
        return (logHz - logMin) / (logMax - logMin);
    }

    private static float DenormalizeFrequency(float normalized)
    {
        float logMin = MathF.Log(20f);
        float logMax = MathF.Log(20000f);
        return MathF.Exp(logMin + normalized * (logMax - logMin));
    }

    private void ShowTypeMenu(int slotIndex)
    {
        var menu = new System.Windows.Controls.ContextMenu();
        foreach (GeneratorType type in Enum.GetValues<GeneratorType>())
        {
            var item = new System.Windows.Controls.MenuItem { Header = type.ToString() };
            item.Click += (_, _) =>
            {
                int typeIndex = slotIndex * 20 + SignalGeneratorPlugin.TypeIndex;
                _parameterCallback(typeIndex, (float)type);
                _presetHelper.MarkAsCustom();
            };
            menu.Items.Add(item);
        }
        menu.IsOpen = true;
    }

    private void ShowLoopModeMenu(int slotIndex)
    {
        var menu = new System.Windows.Controls.ContextMenu();
        foreach (SampleLoopMode mode in Enum.GetValues<SampleLoopMode>())
        {
            var item = new System.Windows.Controls.MenuItem { Header = mode.ToString() };
            item.Click += (_, _) =>
            {
                int loopIndex = slotIndex * 20 + SignalGeneratorPlugin.SampleLoopModeIndex;
                _parameterCallback(loopIndex, (float)mode);
                _presetHelper.MarkAsCustom();
            };
            menu.Items.Add(item);
        }
        menu.IsOpen = true;
    }

    private void ShowHeadroomMenu()
    {
        var menu = new System.Windows.Controls.ContextMenu();
        foreach (HeadroomMode mode in Enum.GetValues<HeadroomMode>())
        {
            string label = mode switch
            {
                HeadroomMode.None => "None (raw sum)",
                HeadroomMode.AutoCompensate => "Auto (-3dB/doubling)",
                HeadroomMode.Normalize => "Normalize (clip)",
                _ => mode.ToString()
            };
            var item = new System.Windows.Controls.MenuItem { Header = label };
            item.Click += (_, _) =>
            {
                _parameterCallback(SignalGeneratorPlugin.HeadroomModeIndex, (float)mode);
                _presetHelper.MarkAsCustom();
            };
            menu.Items.Add(item);
        }
        menu.IsOpen = true;
    }

    private void ToggleSlotMute(int slotIndex)
    {
        var slotState = _plugin.GetSlotState(slotIndex);
        int muteIndex = slotIndex * 20 + SignalGeneratorPlugin.MuteIndex;
        _parameterCallback(muteIndex, slotState.Muted ? 0f : 1f);
        _presetHelper.MarkAsCustom();
    }

    private void ToggleSlotSolo(int slotIndex)
    {
        var slotState = _plugin.GetSlotState(slotIndex);
        int soloIndex = slotIndex * 20 + SignalGeneratorPlugin.SoloIndex;
        _parameterCallback(soloIndex, slotState.Solo ? 0f : 1f);
        _presetHelper.MarkAsCustom();
    }

    private void ToggleSweep(int slotIndex)
    {
        var slotState = _plugin.GetSlotState(slotIndex);
        int sweepIndex = slotIndex * 20 + SignalGeneratorPlugin.SweepEnabledIndex;
        _parameterCallback(sweepIndex, slotState.SweepEnabled ? 0f : 1f);
        _presetHelper.MarkAsCustom();
    }

    private void ToggleRecordingToSlot(int slotIndex)
    {
        if (slotIndex < 0 || slotIndex >= 3)
            return;

        // If already recording to this slot, stop and capture
        if (_plugin.IsRecording && _plugin.RecordingTargetSlot == slotIndex)
        {
            _plugin.StopRecordingToSlot();

            // Switch slot type to Sample so it will play back
            int typeIndex = slotIndex * 20 + SignalGeneratorPlugin.TypeIndex;
            _parameterCallback(typeIndex, (float)GeneratorType.Sample);
            _presetHelper.MarkAsCustom();
        }
        else
        {
            // Stop any existing recording first
            if (_plugin.IsRecording)
            {
                _plugin.StopRecordingToSlot();
            }

            // Start recording to this slot
            _plugin.StartRecordingToSlot(slotIndex);
        }
    }

    private void LoadSampleFile(int slotIndex)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "WAV files (*.wav)|*.wav",
            Title = $"Load Sample for Slot {slotIndex + 1}"
        };

        if (dialog.ShowDialog() == true)
        {
            LoadWavFile(dialog.FileName, slotIndex);
        }
    }

    private void LoadWavFile(string path, int slotIndex)
    {
        try
        {
            using var reader = new AudioFileReader(path);

            // Read up to 10 seconds
            int maxSamples = (int)(reader.WaveFormat.SampleRate * 10);
            var samples = new float[Math.Min(maxSamples, (int)reader.Length / sizeof(float))];
            int read = reader.Read(samples, 0, samples.Length);

            // Convert to mono if stereo
            float[] monoSamples;
            if (reader.WaveFormat.Channels == 2)
            {
                monoSamples = new float[read / 2];
                for (int i = 0; i < monoSamples.Length; i++)
                {
                    monoSamples[i] = (samples[i * 2] + samples[i * 2 + 1]) * 0.5f;
                }
            }
            else
            {
                monoSamples = samples[..read];
            }

            _plugin.LoadSampleAsync(slotIndex, monoSamples, reader.WaveFormat.SampleRate);

            // Switch slot to sample type
            int typeIndex = slotIndex * 20 + SignalGeneratorPlugin.TypeIndex;
            _parameterCallback(typeIndex, (float)GeneratorType.Sample);
            _presetHelper.MarkAsCustom();
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show($"Failed to load sample: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ApplyPreset(string presetName, IReadOnlyDictionary<string, float> parameters)
    {
        foreach (var (name, value) in parameters)
        {
            var param = _plugin.Parameters.FirstOrDefault(p =>
                p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (param != null)
            {
                _parameterCallback(param.Index, value);
            }
        }
    }

    private Dictionary<string, float> GetCurrentParameters()
    {
        var result = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
        foreach (var param in _plugin.Parameters)
        {
            result[param.Name] = param.DefaultValue;
        }
        return result;
    }

    private float GetDpiScale()
    {
        var source = PresentationSource.FromVisual(this);
        return (float)(source?.CompositionTarget?.TransformToDevice.M11 ?? 1.0);
    }

    #region Sample Persistence

    /// <summary>
    /// Called when a sample is loaded or captured - auto-save to config location.
    /// </summary>
    private void OnSampleLoaded(int slotIndex)
    {
        // Dispatch to UI thread for safety
        Dispatcher.BeginInvoke(() => AutoSaveSample(slotIndex));
    }

    private void AutoSaveSample(int slotIndex)
    {
        var data = _plugin.GetSampleData(slotIndex);
        if (data == null) return;

        try
        {
            var dir = Path.GetDirectoryName(_sampleStoragePath);
            if (!string.IsNullOrWhiteSpace(dir))
                Directory.CreateDirectory(dir);

            using var fs = new FileStream(_sampleStoragePath, FileMode.Create, FileAccess.Write);
            using var bw = new BinaryWriter(fs);

            // Header: slot index, sample rate, sample count
            bw.Write(slotIndex);
            bw.Write(data.Value.SampleRate);
            bw.Write(data.Value.Samples.Length);

            // Sample data
            foreach (var sample in data.Value.Samples)
                bw.Write(sample);

            _lastSavedSlot = slotIndex;
            _lastSavedSampleRate = data.Value.SampleRate;
        }
        catch
        {
            // Silently ignore save errors
        }
    }

    private void TryLoadPersistedSample()
    {
        if (!File.Exists(_sampleStoragePath)) return;

        try
        {
            using var fs = new FileStream(_sampleStoragePath, FileMode.Open, FileAccess.Read);
            using var br = new BinaryReader(fs);

            int slotIndex = br.ReadInt32();
            int sampleRate = br.ReadInt32();
            int sampleCount = br.ReadInt32();

            if (slotIndex < 0 || slotIndex >= 3 || sampleCount <= 0 || sampleCount > 480000)
                return;

            var samples = new float[sampleCount];
            for (int i = 0; i < sampleCount; i++)
                samples[i] = br.ReadSingle();

            _plugin.LoadSampleAsync(slotIndex, samples, sampleRate);
            _lastSavedSlot = slotIndex;
            _lastSavedSampleRate = sampleRate;
        }
        catch
        {
            // Silently ignore load errors
        }
    }

    private void SaveSampleToFile(int slotIndex)
    {
        var data = _plugin.GetSampleData(slotIndex);
        if (data == null)
        {
            System.Windows.MessageBox.Show($"No sample loaded in Slot {slotIndex + 1}.", "Save Sample", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Filter = "WAV files (*.wav)|*.wav",
            Title = $"Save Sample from Slot {slotIndex + 1}",
            FileName = $"sample_slot{slotIndex + 1}.wav"
        };

        if (dialog.ShowDialog() != true)
            return;

        try
        {
            // Create 32-bit float WAV file (IEEE float format)
            var format = WaveFormat.CreateIeeeFloatWaveFormat(data.Value.SampleRate, 1);
            using var writer = new WaveFileWriter(dialog.FileName, format);
            writer.WriteSamples(data.Value.Samples, 0, data.Value.Samples.Length);
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show($"Failed to save sample: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ReloadPersistedSample(int slotIndex)
    {
        if (!File.Exists(_sampleStoragePath))
        {
            System.Windows.MessageBox.Show("No persisted sample found.", "Reload Sample", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        try
        {
            using var fs = new FileStream(_sampleStoragePath, FileMode.Open, FileAccess.Read);
            using var br = new BinaryReader(fs);

            int savedSlot = br.ReadInt32();
            int sampleRate = br.ReadInt32();
            int sampleCount = br.ReadInt32();

            if (sampleCount <= 0 || sampleCount > 480000)
            {
                System.Windows.MessageBox.Show("Invalid persisted sample data.", "Reload Sample", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var samples = new float[sampleCount];
            for (int i = 0; i < sampleCount; i++)
                samples[i] = br.ReadSingle();

            // Load into the requested slot (not necessarily the original slot)
            _plugin.LoadSampleAsync(slotIndex, samples, sampleRate);

            // Switch slot to sample type
            int typeIndex = slotIndex * 20 + SignalGeneratorPlugin.TypeIndex;
            _parameterCallback(typeIndex, (float)GeneratorType.Sample);
            _presetHelper.MarkAsCustom();
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show($"Failed to reload sample: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _renderTimer.Stop();
        _renderer.Dispose();
        _plugin.SampleLoaded -= OnSampleLoaded;
        GC.SuppressFinalize(this);
    }

    #endregion
}
