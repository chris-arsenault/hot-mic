# Audio Engine

## Purpose
Describe the real-time audio path from device capture to output, with emphasis on
block processing, latency accounting, and audio-thread constraints.

## Top-Level Signal Flow

```
Input Device(s)
   |
   v
InputCaptureManager -> RoutingScheduler -> OutputPipeline (audio callback)
   |                                  |
   |                                  +-- ChannelStrip[] (per channel)
   |                                  |   - Input gain + input meter
   |                                  |   - PluginChain (per-slot DSP + analysis)
   |                                  |   - Output gain + mute + output meter
   |                                  +-- OutputBus (mono) -> Stereo output
   v
Monitor buffer (optional) -> Monitor output device
```

## Block Processing (OutputPipeline)
Each audio callback pulls interleaved stereo output in blocks:

1) **Apply parameter changes** from the lock-free queue.
2) **BeginBlock**: initialize routing state with the current `sampleClock`.
3) **Clear channel buffers** for the active block.
4) **Process channels** in dependency order:
   - `ChannelStrip.Process()` applies gain/mute and runs the plugin chain.
   - Each channel publishes its output and latency to the routing context.
5) **Output send**:
   - The first channel with an active Output Send plugin writes to `OutputBus`.
   - The bus exposes mono output as stereo (Left/Right/Both).
6) **Master metering**:
   - LUFS (momentary + short-term) computed from output.
7) **Analysis capture**:
   - If a plugin tap captured this block, it wins.
   - Otherwise the OutputPipeline captures channel 0 output for analysis.
8) **Monitor output** (optional) mirrors the main output.

## Sample Clock and Latency
- `sampleClock` increments by the processed block size each callback.
- Each plugin reports `LatencySamples`; the chain accumulates a per-slot latency.
- `sampleTime = sampleClock - cumulativeLatency` is passed to plugins for
  time-aligned analysis signals and routing.

## Preset Load Behavior
`BeginPresetLoad()` / `EndPresetLoad()` is a hard pause/resume of live processing:
- Stops input capture, clears buffers, resets analysis capture, and resets the sample clock.
- Prevents stale analysis or routing state during preset swaps.

## Threading and Safety
- OutputPipeline runs on the audio callback thread. No allocations or locks.
- UI -> audio updates are via `LockFreeChannel<ParameterChange>`.
- Diagnostics and recovery are handled off the audio thread.

Implementation refs: (src/HotMic.Core/Engine/AudioEngine.cs,
 src/HotMic.Core/Engine/OutputPipeline.cs,
 src/HotMic.Core/Engine/ChannelStrip.cs,
 src/HotMic.Core/Engine/RoutingScheduler.cs)

See also: `docs/architecture/Analysis-Signal-Routing.md`, `docs/technical/Metering.md`.
