# Plugin Bugs Found During Exploration

1) Plugins are removed/replaced without Dispose(), and engine shutdown does not
   dispose plugin instances, which can leak worker threads and native resources.
   - Replace slot: `src/HotMic.App/ViewModels/MainViewModel.cs`
   - Remove slot: `src/HotMic.App/ViewModels/MainViewModel.cs`
   - Engine shutdown: `src/HotMic.Core/Engine/AudioEngine.cs`

2) Parameter changes are indexed only by slot index. If plugins are reordered
   or replaced while queued updates exist, parameters can apply to the wrong
   plugin (no plugin identity/version guard).
   - `src/HotMic.Core/Engine/ParameterChange.cs`
   - `src/HotMic.Core/Engine/AudioEngine.cs`

3) PluginChain.SetSlot/Swap mutates shared arrays read by the audio thread,
   which can cause transient UI meter/delta inconsistencies.
   - `src/HotMic.Core/Plugins/PluginChain.cs`
