namespace HotMic.App.UI;

public enum MainButton
{
    ToggleView,
    Settings,
    Pin,
    Minimize,
    Close,
    SavePreset
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
    InputChannelMode,
    MasterMute
}

public sealed class MainUiState
{
    public DevicePickerTarget ActiveDevicePicker { get; set; } = DevicePickerTarget.None;
    public float DevicePickerScroll { get; set; }
    public PluginDragState? PluginDrag { get; set; }
    public ContainerDragState? ContainerDrag { get; set; }
    public KnobDragState? KnobDrag { get; set; }
    public DropTarget? CurrentDropTarget { get; set; }
}

public readonly record struct PluginDragState(
    int ChannelIndex, int PluginInstanceId, int SlotIndex,
    float StartX, float StartY, float CurrentX, float CurrentY,
    bool IsDragging,
    SkiaSharp.SKRect SourceRect,
    string DisplayName);

public readonly record struct ContainerDragState(
    int ChannelIndex, int ContainerId, int SlotIndex,
    float StartX, float StartY, float CurrentX, float CurrentY,
    bool IsDragging,
    SkiaSharp.SKRect SourceRect,
    string DisplayName);

/// <summary>
/// Represents a valid drop target during drag operations, used for rendering visual feedback.
/// </summary>
public readonly record struct DropTarget(
    bool IsValid,
    SkiaSharp.SKRect TargetRect,
    float InsertLineX,
    float InsertLineTop,
    float InsertLineBottom);

public readonly record struct KnobDragState(int ChannelIndex, KnobType KnobType, float StartValue, float StartY, int PluginInstanceId = 0, float MinValue = -60f, float MaxValue = 12f);
