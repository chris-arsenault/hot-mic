using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HotMic.App.Models;

namespace HotMic.App.ViewModels;

public partial class PluginViewModel : ObservableObject
{
    private readonly Action? _action;
    private readonly Action? _remove;
    private readonly Action<HotMic.Core.Engine.ParameterChange>? _parameterSink;
    private readonly Action<int, bool>? _bypassConfigSink;
    private readonly int _channelId;

    private PluginViewModel(int channelId, int slotIndex, Action? action, Action? remove, Action<HotMic.Core.Engine.ParameterChange>? parameterSink, Action<int, bool>? bypassConfigSink)
    {
        _channelId = channelId;
        SlotIndex = slotIndex;
        _action = action;
        _remove = remove;
        _parameterSink = parameterSink;
        _bypassConfigSink = bypassConfigSink;
        ActionCommand = new RelayCommand(() => _action?.Invoke());
        RemoveCommand = new RelayCommand(() => _remove?.Invoke());
    }

    public int SlotIndex { get; }

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

    public static PluginViewModel CreateEmpty(int channelId, int slotIndex, Action? action = null, Action? remove = null, Action<HotMic.Core.Engine.ParameterChange>? parameterSink = null, Action<int, bool>? bypassConfigSink = null)
    {
        var viewModel = new PluginViewModel(channelId, slotIndex, action, remove, parameterSink, bypassConfigSink)
        {
            IsEmpty = true,
            DisplayName = $"Slot {slotIndex}",
            LatencyMs = 0f
        };

        return viewModel;
    }

    public static PluginViewModel CreateFilled(int channelId, int slotIndex, string pluginId, string name, float latencyMs, float[] elevatedValues, Action? action = null, Action? remove = null, Action<HotMic.Core.Engine.ParameterChange>? parameterSink = null, Action<int, bool>? bypassConfigSink = null)
    {
        var viewModel = new PluginViewModel(channelId, slotIndex, action, remove, parameterSink, bypassConfigSink)
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

    partial void OnIsEmptyChanged(bool value)
    {
        OnPropertyChanged(nameof(ActionLabel));
    }

    partial void OnIsBypassedChanged(bool value)
    {
        _parameterSink?.Invoke(new HotMic.Core.Engine.ParameterChange
        {
            ChannelId = _channelId,
            Type = HotMic.Core.Engine.ParameterType.PluginBypass,
            PluginIndex = SlotIndex - 1,
            Value = value ? 1f : 0f
        });
        _bypassConfigSink?.Invoke(SlotIndex - 1, value);
    }

    partial void OnParam0ValueChanged(float value)
    {
        if (ElevatedParams is null || ElevatedParams.Length < 1)
        {
            return;
        }

        _parameterSink?.Invoke(new HotMic.Core.Engine.ParameterChange
        {
            ChannelId = _channelId,
            Type = HotMic.Core.Engine.ParameterType.PluginParameter,
            PluginIndex = SlotIndex - 1,
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

        _parameterSink?.Invoke(new HotMic.Core.Engine.ParameterChange
        {
            ChannelId = _channelId,
            Type = HotMic.Core.Engine.ParameterType.PluginParameter,
            PluginIndex = SlotIndex - 1,
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
}
