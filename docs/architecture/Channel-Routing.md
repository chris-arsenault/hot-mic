# Channel Routing

## Purpose
Describe the multi-channel routing graph used for copy, merge, and output send
operations within the existing plugin chain.

## Core Building Blocks

### InputPlugin (Device Input)
- Reads mono audio for a channel from the routing context.
- Supports input channel mode (Sum / Left / Right) for stereo devices.

### CopyToChannelPlugin
- Captures audio and analysis signals at its slot.
- Writes to a per-channel CopyBus for the target channel.
- Declares a routing dependency so the source channel is processed before the target.

### BusInputPlugin
- Input plugin for copy-created channels.
- Reads from the target channel's CopyBus and re-emits analysis signals.
- Preserves the source latency so downstream alignment stays correct.

### MergePlugin
- Pulls output from multiple source channels into the current channel.
- Optional latency alignment using per-source delay lines:
  - Align mode delays each path to the slowest source.
- Sum strategies: Sum / Average / Equal Power.
- Phase options: None / Invert Sources / Invert Target.

### OutputSendPlugin
- Marks a channel as the output sender (Left / Right / Both).
- OutputBus accepts the first sender each block (no mixing at this stage).

## Routing Context
`RoutingContext` stores per-block routing state:
- Input sources (device or bus).
- Copy buses (audio + analysis signals).
- Output bus (mono output mapped to stereo).

## Processing Order
`RoutingScheduler` builds a dependency graph from routing plugins:
- Copy and merge dependencies are respected.
- If a cycle is detected, processing falls back to channel index order.

## Latency Accounting
- Each channel reports cumulative latency through the plugin chain.
- Merge alignment uses the routing context to query source latency.
- OutputBus stores the latency of the selected output sender.

Implementation refs: (src/HotMic.Core/Engine/RoutingContext.cs,
 src/HotMic.Core/Engine/CopyBus.cs,
 src/HotMic.Core/Engine/OutputBus.cs,
 src/HotMic.Core/Engine/RoutingScheduler.cs,
 src/HotMic.Core/Plugins/BuiltIn/InputPlugin.cs,
 src/HotMic.Core/Plugins/BuiltIn/CopyToChannelPlugin.cs,
 src/HotMic.Core/Plugins/BuiltIn/BusInputPlugin.cs,
 src/HotMic.Core/Plugins/BuiltIn/MergePlugin.cs,
 src/HotMic.Core/Plugins/BuiltIn/OutputSendPlugin.cs)
