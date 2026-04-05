namespace HotMic.App.UI;

internal enum MainButton
{
    ToggleView,
    Settings,
    Pin,
    Minimize,
    Close,
    SavePreset
}

internal enum DevicePickerTarget
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

internal enum KnobType
{
    InputGain,
    OutputGain,
    PluginParam0,
    PluginParam1
}

internal enum ToggleType
{
    Mute,
    Solo,
    InputChannelMode,
    MasterMute
}

internal sealed class MainUiState
{
    public DevicePickerTarget ActiveDevicePicker { get; set; } = DevicePickerTarget.None;
    public float DevicePickerScroll { get; set; }
    public PluginDragState? PluginDrag { get; set; }
    public ContainerDragState? ContainerDrag { get; set; }
    public KnobDragState? KnobDrag { get; set; }
    public DropTarget? CurrentDropTarget { get; set; }
}

internal readonly record struct PluginDragState(
    int ChannelIndex, int PluginInstanceId, int SlotIndex,
    float StartX, float StartY, float CurrentX, float CurrentY,
    bool IsDragging,
    SkiaSharp.SKRect SourceRect,
    string DisplayName);

internal readonly record struct ContainerDragState(
    int ChannelIndex, int ContainerId, int SlotIndex,
    float StartX, float StartY, float CurrentX, float CurrentY,
    bool IsDragging,
    SkiaSharp.SKRect SourceRect,
    string DisplayName);

/// <summary>
/// Represents a valid drop target during drag operations, used for rendering visual feedback.
/// </summary>
internal readonly record struct DropTarget(
    bool IsValid,
    SkiaSharp.SKRect TargetRect,
    float InsertLineX,
    float InsertLineTop,
    float InsertLineBottom);

internal readonly record struct KnobDragState(int ChannelIndex, KnobType KnobType, float StartValue, float StartY, int PluginInstanceId = 0, float MinValue = -60f, float MaxValue = 12f);
