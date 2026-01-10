namespace HotMic.App.UI;

public enum MainButton
{
    ToggleView,
    Settings,
    Pin,
    Minimize,
    Close
}

public enum DevicePickerTarget
{
    None,
    Input1,
    Input2,
    Output,
    Monitor,
    SampleRate,
    BufferSize
}

public enum KnobType
{
    InputGain,
    OutputGain
}

public enum ToggleType
{
    Mute,
    Solo
}

public sealed class MainUiState
{
    public DevicePickerTarget ActiveDevicePicker { get; set; } = DevicePickerTarget.None;
    public float DevicePickerScroll { get; set; }
    public PluginDragState? PluginDrag { get; set; }
    public KnobDragState? KnobDrag { get; set; }
}

public readonly record struct PluginDragState(int ChannelIndex, int SlotIndex, float StartX, float StartY, float CurrentX, float CurrentY, bool IsDragging);

public readonly record struct KnobDragState(int ChannelIndex, KnobType KnobType, float StartValue, float StartY);
