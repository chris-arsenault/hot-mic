using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HotMic.App.Models;
using HotMic.Core.Dsp;

namespace HotMic.App.ViewModels;

public partial class PluginViewModel : ObservableObject
{
    private Action? _action;
    private Action? _remove;
    private readonly Action<HotMic.Core.Engine.ParameterChange>? _parameterSink;
    private readonly Action<int, int, int, float>? _pluginParameterSink;
    private readonly Action<int, bool>? _bypassConfigSink;
    private readonly int _channelId;

    private PluginViewModel(int channelId, int slotIndex, int instanceId, Action? action, Action? remove, Action<HotMic.Core.Engine.ParameterChange>? parameterSink, Action<int, int, int, float>? pluginParameterSink, Action<int, bool>? bypassConfigSink)
    {
        _channelId = channelId;
        SlotIndex = slotIndex;
        InstanceId = instanceId;
        _action = action;
        _remove = remove;
        _parameterSink = parameterSink;
        _pluginParameterSink = pluginParameterSink;
        _bypassConfigSink = bypassConfigSink;
        ActionCommand = new RelayCommand(() => _action?.Invoke());
        RemoveCommand = new RelayCommand(() => _remove?.Invoke());
    }

    public int SlotIndex { get; private set; }

    public int InstanceId { get; }

    public int ChannelId => _channelId;

    [ObservableProperty]
    private string pluginId = string.Empty;

    [ObservableProperty]
    private string displayName = string.Empty;

    [ObservableProperty]
    private bool isBypassed;

    [ObservableProperty]
    private bool isEmpty;

    [ObservableProperty]
    private float latencyMs;

    [ObservableProperty]
    private float outputPeakLevel;

    [ObservableProperty]
    private float outputRmsLevel;

    [ObservableProperty]
    private bool isClipping;

    [ObservableProperty]
    private int copyTargetChannelId;

    // Spectral delta data for the delta strip visualization (32 bands)
    [ObservableProperty]
    private float[]? spectralDelta;

    // Display mode for the delta strip (Full Spectrum vs Vocal Range)
    [ObservableProperty]
    private DeltaDisplayMode deltaDisplayMode = DeltaDisplayMode.VocalRange;

    // Elevated parameter definitions (from ElevatedParameterDefinitions)
    public ElevatedParameterDefinitions.ParamDef[]? ElevatedParams { get; private set; }

    // Current values for the elevated parameters
    [ObservableProperty]
    private float param0Value;

    [ObservableProperty]
    private float param1Value;

    public string ActionLabel => IsEmpty ? "Add Plugin" : "Edit Parameters";

    public IRelayCommand ActionCommand { get; }

    public IRelayCommand RemoveCommand { get; }

    /// <summary>
    /// Toggle between Full Spectrum and Vocal Range display modes for the delta strip.
    /// </summary>
    public void ToggleDeltaDisplayMode()
    {
        DeltaDisplayMode = DeltaDisplayMode == DeltaDisplayMode.VocalRange
            ? DeltaDisplayMode.FullSpectrum
            : DeltaDisplayMode.VocalRange;
    }

    public static PluginViewModel CreateEmpty(int channelId, int slotIndex, int instanceId, Action? action = null, Action? remove = null, Action<HotMic.Core.Engine.ParameterChange>? parameterSink = null, Action<int, int, int, float>? pluginParameterSink = null, Action<int, bool>? bypassConfigSink = null)
    {
        var viewModel = new PluginViewModel(channelId, slotIndex, instanceId, action, remove, parameterSink, pluginParameterSink, bypassConfigSink)
        {
            IsEmpty = true,
            DisplayName = $"Slot {slotIndex}",
            LatencyMs = 0f
        };

        return viewModel;
    }

    public static PluginViewModel CreateFilled(int channelId, int slotIndex, int instanceId, string pluginId, string name, float latencyMs, float[] elevatedValues, Action? action = null, Action? remove = null, Action<HotMic.Core.Engine.ParameterChange>? parameterSink = null, Action<int, int, int, float>? pluginParameterSink = null, Action<int, bool>? bypassConfigSink = null)
    {
        var viewModel = new PluginViewModel(channelId, slotIndex, instanceId, action, remove, parameterSink, pluginParameterSink, bypassConfigSink)
        {
            IsEmpty = false,
            PluginId = pluginId,
            DisplayName = name,
            LatencyMs = latencyMs
        };

        // Load elevated parameter definitions for this plugin type
        viewModel.ElevatedParams = ElevatedParameterDefinitions.GetElevations(pluginId);

        // Set initial values without triggering change notifications
        // Intentionally accessing backing fields to avoid OnParamXValueChanged callbacks
#pragma warning disable MVVMTK0034
        if (elevatedValues.Length > 0)
        {
            viewModel.param0Value = elevatedValues[0];
        }
        if (elevatedValues.Length > 1)
        {
            viewModel.param1Value = elevatedValues[1];
        }
#pragma warning restore MVVMTK0034

        return viewModel;
    }

    /// <summary>
    /// Updates the slot index used for interactions.
    /// </summary>
    public void UpdateSlotIndex(int slotIndex)
    {
        SlotIndex = slotIndex;
    }

    /// <summary>
    /// Updates action delegates for the slot.
    /// </summary>
    public void UpdateActions(Action? action, Action? remove)
    {
        _action = action;
        _remove = remove;
    }

    /// <summary>
    /// Updates this view model with the latest slot info without triggering parameter changes.
    /// </summary>
    public void UpdateFromSlotInfo(PluginSlotInfo slotInfo)
    {
        PluginId = slotInfo.PluginId;
        DisplayName = slotInfo.Name;
        LatencyMs = slotInfo.LatencyMs;
        IsEmpty = false;
        SetBypassSilent(slotInfo.IsBypassed);
        CopyTargetChannelId = slotInfo.CopyTargetChannelId;

        ElevatedParams = ElevatedParameterDefinitions.GetElevations(slotInfo.PluginId);

        if (slotInfo.ElevatedParamValues.Length > 0)
        {
            SetParamValueSilent(0, slotInfo.ElevatedParamValues[0]);
        }
        if (slotInfo.ElevatedParamValues.Length > 1)
        {
            SetParamValueSilent(1, slotInfo.ElevatedParamValues[1]);
        }
    }

    partial void OnIsEmptyChanged(bool value)
    {
        OnPropertyChanged(nameof(ActionLabel));
    }

    partial void OnIsBypassedChanged(bool value)
    {
        if (InstanceId <= 0)
        {
            return;
        }

        _parameterSink?.Invoke(new HotMic.Core.Engine.ParameterChange
        {
            ChannelId = _channelId,
            Type = HotMic.Core.Engine.ParameterType.PluginBypass,
            PluginInstanceId = InstanceId,
            Value = value ? 1f : 0f
        });
        _bypassConfigSink?.Invoke(InstanceId, value);
    }

    partial void OnParam0ValueChanged(float value)
    {
        if (ElevatedParams is null || ElevatedParams.Length < 1)
        {
            return;
        }

        if (InstanceId <= 0)
        {
            return;
        }

        if (_pluginParameterSink is not null)
        {
            _pluginParameterSink(_channelId, InstanceId, ElevatedParams[0].Index, value);
            return;
        }

        _parameterSink?.Invoke(new HotMic.Core.Engine.ParameterChange
        {
            ChannelId = _channelId,
            Type = HotMic.Core.Engine.ParameterType.PluginParameter,
            PluginInstanceId = InstanceId,
            ParameterIndex = ElevatedParams[0].Index,
            Value = value
        });
    }

    partial void OnParam1ValueChanged(float value)
    {
        if (ElevatedParams is null || ElevatedParams.Length < 2)
        {
            return;
        }

        if (InstanceId <= 0)
        {
            return;
        }

        if (_pluginParameterSink is not null)
        {
            _pluginParameterSink(_channelId, InstanceId, ElevatedParams[1].Index, value);
            return;
        }

        _parameterSink?.Invoke(new HotMic.Core.Engine.ParameterChange
        {
            ChannelId = _channelId,
            Type = HotMic.Core.Engine.ParameterType.PluginParameter,
            PluginInstanceId = InstanceId,
            ParameterIndex = ElevatedParams[1].Index,
            Value = value
        });
    }

    /// <summary>
    /// Updates a parameter value without triggering the change notification to the audio engine.
    /// Used when syncing values from the engine back to the UI.
    /// </summary>
    public void SetParamValueSilent(int elevatedIndex, float value)
    {
        // Intentionally accessing backing fields to avoid OnParamXValueChanged callbacks
#pragma warning disable MVVMTK0034
        if (elevatedIndex == 0)
        {
            param0Value = value;
            OnPropertyChanged(nameof(Param0Value));
        }
        else if (elevatedIndex == 1)
        {
            param1Value = value;
            OnPropertyChanged(nameof(Param1Value));
        }
#pragma warning restore MVVMTK0034
    }

    /// <summary>
    /// Updates bypass state without firing parameter change events.
    /// </summary>
    public void SetBypassSilent(bool value)
    {
#pragma warning disable MVVMTK0034
        if (isBypassed == value)
        {
            return;
        }
        isBypassed = value;
#pragma warning restore MVVMTK0034
        OnPropertyChanged(nameof(IsBypassed));
    }
}
