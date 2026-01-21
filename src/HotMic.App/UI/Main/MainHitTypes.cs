namespace HotMic.App.UI;

public readonly record struct KnobHit(int ChannelIndex, KnobType KnobType);
public readonly record struct PluginKnobHit(int ChannelIndex, int PluginInstanceId, int ParamIndex, float MinValue, float MaxValue);
public readonly record struct PluginSlotHit(int ChannelIndex, int PluginInstanceId, int SlotIndex);
public enum PluginSlotRegion { None, Action, Bypass, Remove, Knob, DeltaStrip }
public readonly record struct MainContainerSlotHit(int ChannelIndex, int ContainerId, int SlotIndex);
public enum MainContainerSlotRegion { None, Action, Bypass, Remove }
public readonly record struct ToggleHit(int ChannelIndex, ToggleType ToggleType);
