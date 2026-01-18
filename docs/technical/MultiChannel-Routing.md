# Multi-Channel Routing (Proposed)

This document defines the multi-channel routing design for HotMic using the existing
canonical plugin graph and DRY plugin shell UI. It covers routing plugins, latency
compensation, processing order, and analysis capture.

## Goals
- Support 1-N mono channels.
- Inputs are plugins bound to a single hardware device per channel.
- Copy-to-channel produces a new channel (no shared hardware inputs).
- Merge 2-N channels at any point in the target chain.
- Maintain sync across differing pipeline latencies (merge alignment).
- Main output is driven by a single Output Send plugin (left/right/both).
- Analysis taps are plugin-controlled; fallback capture can use the post-output feed.

## Non-Goals
- Backward compatibility for existing configs (users will delete old configs).
- UI redesign unrelated to routing (retain current visual language).

## Architecture Summary
### Existing Graph Extension
Routing is implemented as first-class plugins inside the existing `PluginGraph`
and `PluginChain`. No parallel routing graph is introduced. Cross-channel access is
handled through an extended `PluginProcessContext` with a routing accessor.

### Routing Plugins
1) **InputPlugin** (`builtin:input`)
   - Always first in chain (pinned).
   - Reads mono audio from a single device assigned to this channel.
   - Provides channel mode selection (Sum/Left/Right) for stereo inputs.

2) **CopyToChannelPlugin** (`builtin:copy`)
   - Captures audio + analysis signals at its slot position.
   - Writes to a copy bus associated with the target channel.
   - Always targets a newly created channel (no overwrite of existing channels).
   - The new channel starts with a pinned `BusInputPlugin`.

3) **BusInputPlugin** (`builtin:bus-input`)
   - Reads from a copy bus and feeds the channel buffer.
   - Produces analysis signals captured by the copy plugin.
   - Used only for copy-created channels (not user insertable).

4) **MergePlugin** (`builtin:merge`)
   - Appears anywhere in the target chain.
   - Pulls audio from 2-N source channels (post-chain output).
   - Provides sum strategy, polarity options, and latency alignment controls.

5) **OutputSendPlugin** (`builtin:output-send`)
   - Marks the channel as the main output sender (left/right/both).
   - Exactly one active Output Send plugin allowed globally (others blocked or auto-bypassed).
   - Output send happens post-fader (after output gain/mute) so UI faders apply.
   - Output fallback capture happens after this send when no analysis tap is present.

## Latency Compensation
### Merge Alignment (Primary)
The merge plugin aligns sources to the slowest contributor at the merge slot:
- `alignLatency = max(targetLatency, sourceLatencies...)`
- `targetDelay = alignLatency - targetLatency`
- `sourceDelay = alignLatency - sourceLatency`
- Delay is applied using preallocated delay lines (no audio-thread allocations).

### Copy Bus Latency
Copy-to-channel preserves the source chain latency at the tap. The bus input
plugin uses that latency as its own baseline so downstream merge alignment
has consistent timing data.

## Processing Order
Each audio block is processed in dependency order:
- Copy: source channel must run before its copy-created channel.
- Merge: all source channels must run before the target channel.
- Cycles are invalid; UI prevents them and core validation rejects them.

## Analysis Signal Propagation
Copy plugin captures analysis signals visible at its slot and forwards them.
Bus input plugin re-emits those signals into the target chain’s analysis bus
as a producer at slot 0.

## Analysis Capture
Analysis Tap forwards audio + analysis signals to the analysis orchestrator at
its slot in the chain. When no tap is present (or it is bypassed), the output
pipeline captures the post Output Send feed as a fallback.

## Config Changes (New)
- Channels own their input settings.
- Routing plugins persist their state in plugin config/state.
- Output routing in `AudioSettingsConfig` is replaced by Output Send plugin state.

## UI Notes
- Input is represented as a pinned plugin at the start of each channel strip.
- Copy-to-channel is visualized as a bridge from the copy slot to the new channel.
- Merge shows a list of source channels (2-N) with sum strategy + alignment options.
- Output Send is a single allowed plugin (others disabled or removed).

## Merge Options (Detailed)
### Sum Strategy
- **Sum**: Adds signals with no scaling. Loudness increases with more sources.
- **Average**: Divides by the number of sources + target to keep overall level stable.
- **Equal Power**: Scales by `1 / sqrt(N)` to preserve perceived loudness.

### Polarity (Phase) Mode
- **None**: No polarity change.
- **Invert Sources**: Flips polarity on all merged source channels.
- **Invert Target**: Flips polarity on the target buffer before merging.

### Latency Mode
- **Align**: Uses the slowest path (highest latency) as reference and delays others.
- **Off**: Leaves timing untouched (use when alignment is undesired).

## Implementation Phases
1) Extend core routing (plugins + context + buses + latency helpers).
2) Refactor AudioEngine for N channels, per-channel inputs, dependency order,
   output send, and analysis tap update.
3) Update UI/config for dynamic channels, routing plugins, and bridge visuals.
