namespace HotMic.App.UI;

public enum MainButton
{
    ToggleView,
    Settings,
    Pin,
    Minimize,
    Close,
    SavePreset1,
    SavePreset2
}

public enum DevicePickerTarget
{
    None,
    Input1,
    Input2,
    Output,
    Monitor,
    SampleRate,
    BufferSize,
    Input1Channel,
    Input2Channel,
    OutputRouting,
    Preset1,
    Preset2
}

public enum KnobType
{
    InputGain,
    OutputGain,
    PluginParam0,
    PluginParam1
}

public enum ToggleType
{
    Mute,
    Solo,
    InputStereo,
    MasterStereo,
    MasterMute
}

public sealed class MainUiState
{
    public DevicePickerTarget ActiveDevicePicker { get; set; } = DevicePickerTarget.None;
    public float DevicePickerScroll { get; set; }
    public PluginDragState? PluginDrag { get; set; }
    public KnobDragState? KnobDrag { get; set; }
}

public readonly record struct PluginDragState(int ChannelIndex, int SlotIndex, float StartX, float StartY, float CurrentX, float CurrentY, bool IsDragging);

public readonly record struct KnobDragState(int ChannelIndex, KnobType KnobType, float StartValue, float StartY, int PluginSlotIndex = -1, float MinValue = -60f, float MaxValue = 12f);
