using SkiaSharp;

namespace HotMic.App.UI;

/// <summary>
/// Drag state for plugin slots, shared between main window and container windows.
/// </summary>
public readonly record struct PluginStripDragState(
    int ChannelIndex,
    int PluginInstanceId,
    int SlotIndex,
    float StartX,
    float StartY,
    float CurrentX,
    float CurrentY,
    bool IsDragging,
    SKRect SourceRect,
    string DisplayName);

/// <summary>
/// UI state for plugin strip drag operations, used by renderers.
/// </summary>
public sealed class PluginStripUiState
{
    public PluginStripDragState? PluginDrag { get; set; }
    public DropTarget? CurrentDropTarget { get; set; }
}
