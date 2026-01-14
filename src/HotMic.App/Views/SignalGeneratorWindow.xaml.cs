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

public partial class SignalGeneratorWindow : Window
{
    private readonly SignalGeneratorRenderer _renderer;
    private readonly SignalGeneratorPlugin _plugin;
    private readonly Action<int, float> _parameterCallback;
    private readonly Action<bool> _bypassCallback;
    private readonly DispatcherTimer _renderTimer;
    private readonly PluginPresetHelper _presetHelper;

    private int _activeKnobSlot = -1;
    private SignalGeneratorHitArea _activeKnobType;
    private float _dragStartY;
    private float _dragStartValue;
    private SignalGeneratorHitArea _hoveredArea = SignalGeneratorHitArea.None;
    private int _hoveredSlot = -1;

    private float _smoothedOutputLevel;
    private readonly float[] _smoothedSlotLevels = new float[3];

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

        var preferredSize = SignalGeneratorRenderer.GetPreferredSize();
        Width = preferredSize.Width;
        Height = preferredSize.Height;

        _renderTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        _renderTimer.Tick += OnRenderTick;
        Loaded += (_, _) => _renderTimer.Start();
        Closed += (_, _) =>
        {
            _renderTimer.Stop();
            _renderer.Dispose();
        };
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

        var state = new SignalGeneratorState
        {
            IsBypassed = _plugin.IsBypassed,
            PresetName = _presetHelper.CurrentPresetName,
            OutputLevel = _smoothedOutputLevel,
            MasterGainDb = _plugin.MasterGainDb,
            HoveredArea = _hoveredArea,
            HoveredSlot = _hoveredSlot
        };

        // Build slot states from plugin
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
                Level = _smoothedSlotLevels[i]
            };
        }

        _renderer.Render(canvas, size, dpiScale, state);
    }

    private void SkiaCanvas_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left)
            return;

        var pos = e.GetPosition(SkiaCanvas);
        float x = (float)pos.X;
        float y = (float)pos.Y;

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

            case SignalGeneratorHitArea.SlotGainKnob:
            case SignalGeneratorHitArea.SlotFreqKnob:
            case SignalGeneratorHitArea.MasterGainKnob:
                _activeKnobSlot = hit.SlotIndex;
                _activeKnobType = hit.Area;
                _dragStartY = y;
                _dragStartValue = GetKnobNormalizedValue(hit.Area, hit.SlotIndex);
                SkiaCanvas.CaptureMouse();
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

            case SignalGeneratorHitArea.RecordButton:
                ToggleRecording();
                e.Handled = true;
                break;

            case SignalGeneratorHitArea.LoadSampleButton:
                LoadSampleFile(hit.SlotIndex);
                e.Handled = true;
                break;
        }
    }

    private void SkiaCanvas_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        var pos = e.GetPosition(SkiaCanvas);
        float x = (float)pos.X;
        float y = (float)pos.Y;

        if (_activeKnobSlot >= -1 && _activeKnobType != SignalGeneratorHitArea.None && e.LeftButton == MouseButtonState.Pressed)
        {
            float deltaY = _dragStartY - y;
            float newNormalized = RotaryKnob.CalculateValueFromDrag(_dragStartValue, -deltaY, 0.004f);
            ApplyKnobValue(_activeKnobType, _activeKnobSlot, newNormalized);
            e.Handled = true;
        }
        else
        {
            var hit = _renderer.HitTest(x, y);
            _hoveredArea = hit.Area;
            _hoveredSlot = hit.SlotIndex;
        }
    }

    private void SkiaCanvas_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left)
            return;

        _activeKnobSlot = -1;
        _activeKnobType = SignalGeneratorHitArea.None;
        SkiaCanvas.ReleaseMouseCapture();
    }

    private void SkiaCanvas_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        var pos = e.GetPosition(SkiaCanvas);
        var hit = _renderer.HitTest((float)pos.X, (float)pos.Y);

        if (hit.Area is SignalGeneratorHitArea.SlotGainKnob or SignalGeneratorHitArea.SlotFreqKnob or SignalGeneratorHitArea.MasterGainKnob)
        {
            float current = GetKnobNormalizedValue(hit.Area, hit.SlotIndex);
            float delta = e.Delta > 0 ? 0.02f : -0.02f;
            float newValue = Math.Clamp(current + delta, 0f, 1f);
            ApplyKnobValue(hit.Area, hit.SlotIndex, newValue);
            e.Handled = true;
        }
    }

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
        return area switch
        {
            SignalGeneratorHitArea.SlotGainKnob => (GetSlotGainDb(slotIndex) + 60f) / 72f,
            SignalGeneratorHitArea.SlotFreqKnob => NormalizeFrequency(GetSlotFrequency(slotIndex)),
            SignalGeneratorHitArea.MasterGainKnob => (_plugin.MasterGainDb + 60f) / 72f,
            _ => 0f
        };
    }

    private void ApplyKnobValue(SignalGeneratorHitArea area, int slotIndex, float normalized)
    {
        switch (area)
        {
            case SignalGeneratorHitArea.SlotGainKnob:
                float gainDb = -60f + normalized * 72f;
                int gainIndex = slotIndex * 20 + SignalGeneratorPlugin.GainIndex;
                _parameterCallback(gainIndex, gainDb);
                _presetHelper.MarkAsCustom();
                break;

            case SignalGeneratorHitArea.SlotFreqKnob:
                float freq = DenormalizeFrequency(normalized);
                int freqIndex = slotIndex * 20 + SignalGeneratorPlugin.FrequencyIndex;
                _parameterCallback(freqIndex, freq);
                _presetHelper.MarkAsCustom();
                break;

            case SignalGeneratorHitArea.MasterGainKnob:
                float masterDb = -60f + normalized * 72f;
                _parameterCallback(SignalGeneratorPlugin.MasterGainIndex, masterDb);
                _presetHelper.MarkAsCustom();
                break;
        }
    }

    private float GetSlotGainDb(int slot)
    {
        var slotState = _plugin.GetSlotState(slot);
        return slotState.GainDb;
    }

    private float GetSlotFrequency(int slot)
    {
        var slotState = _plugin.GetSlotState(slot);
        return slotState.Frequency;
    }

    private static float NormalizeFrequency(float hz)
    {
        // Log scale: 20Hz to 20kHz
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

    private void ToggleRecording()
    {
        // Toggle recording state
        // This would need additional plugin API to track recording state
        _plugin.SetRecordingEnabled(true);
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
            // Find parameter index by name
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
            result[param.Name] = param.DefaultValue; // Would need current values
        }
        return result;
    }

    private float GetDpiScale()
    {
        var source = PresentationSource.FromVisual(this);
        return (float)(source?.CompositionTarget?.TransformToDevice.M11 ?? 1.0);
    }
}
