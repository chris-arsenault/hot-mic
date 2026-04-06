namespace HotMic.App.UI;

internal readonly record struct KnobHit(int ChannelIndex, KnobType KnobType);
internal readonly record struct PluginKnobHit(int ChannelIndex, int PluginInstanceId, int ParamIndex, float MinValue, float MaxValue);
internal readonly record struct PluginSlotHit(int ChannelIndex, int PluginInstanceId, int SlotIndex);
internal enum PluginSlotRegion { None, Action, Bypass, Remove, Knob, DeltaStrip }
internal readonly record struct MainContainerSlotHit(int ChannelIndex, int ContainerId, int SlotIndex);
internal enum MainContainerSlotRegion { None, Action, Bypass, Remove }
internal readonly record struct ToggleHit(int ChannelIndex, ToggleType ToggleType);
