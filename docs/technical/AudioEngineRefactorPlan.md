AudioEngine Refactor Plan
=========================

Goals
-----
- Split `AudioEngine` into focused managers: input capture, routing scheduler, output pipeline, diagnostics, recovery.
- Preserve audio-thread constraints: no allocations, locks, or UI calls in the callback.
- Ensure preset load pauses input, clears buffers, and resumes on a fresh block.

Plan
----
1) Create new engine components
   - InputCaptureManager (input lifecycle + buffers + input diagnostics)
   - RoutingScheduler + RoutingSnapshot (processing order + snapshot construction)
   - OutputPipeline (audio callback + parameter application + meters + analysis)
   - MonitorWaveProvider (monitor output reader)
   - PluginDisposalQueue (deferred disposal tracking)
   - DeviceRecoveryManager (device invalidation + recovery loop)
   - AudioEngineDiagnosticsCollector (snapshot builder)
   - DeviceErrorHelper (AUDCLNT error detection)

2) Wire AudioEngine to components
   - Replace nested classes with new files.
   - Publish routing snapshots atomically.
   - Keep output stats in AudioEngine and feed via callbacks.

3) Preset load pause/resume
   - Add engine update scope (BeginPresetLoad/EndPresetLoad).
   - Stop input captures, clear buffers, reset output sample clock.
   - Resume input and restore processing state after preset load completes.

4) Remove plugin-type coupling from output pipeline
   - Add IPluginCommandHandler in plugins layer.
   - Implement in FFTNoiseRemovalPlugin.
   - Route PluginCommand via PluginChain.

5) Clean up
   - Delete dead methods and old nested types in AudioEngine.
   - Update using statements and diagnostics collection sites.
