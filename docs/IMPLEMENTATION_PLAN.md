# HotMic Implementation Plan

## Goals
- Implement the HotMic audio routing pipeline (2x WASAPI inputs -> plugin chains -> VB-Cable output).
- Provide WPF + SkiaSharp UI with full edit and minimal view modes.
- Support built-in DSP plugins and VST2/VST3 discovery/hosting.
- Persist configuration across sessions via JSON.

## Phases (aligned to REQUIREMENTS.md + AGENTS.md)
1. Foundation
   - Create solution/projects and initial MVVM wiring.
   - Add common configuration models and shared types.

2. Core Audio Engine
   - Implement device management, dual-input capture, and output routing.
   - Add lock-free data paths and metering processors.
   - Provide parameter queue for UI -> audio thread updates.

3. Channel Strip & Plugins
   - Implement channel strip processing (gain, mute/solo, meters).
   - Build plugin chain with fixed slot array and processing order.
   - Implement built-in plugins: Compressor, Noise Gate, FFT Noise Removal, 3-Band EQ.

4. UI & View Modes
   - SkiaSharp controls for meters/knobs.
   - Full edit view with channel strips and plugin slots (drag/drop).
   - Minimal view with compact meters and channel labels.
   - Always-on-top toggle and view switching.

5. VST Support
   - Scan VST2/VST3 directories and cache results.
   - Host VST plugins through wrapper implementing IPlugin.

6. Persistence & Polish
   - Persist devices, plugins, parameters, and UI state to JSON.
   - Graceful error handling for device disconnects and missing VB-Cable.

## Requirement Tracking
- FR-1/FR-2: Audio engine + DeviceManager
- FR-3/FR-7: ChannelStrip + MeterProcessor + UI meters
- FR-4/FR-5: PluginChain + built-in plugins + plugin UI
- FR-6: VST scanning/hosting
- FR-8/FR-9: WPF views and SkiaSharp controls
- FR-10: ConfigManager + AppConfig persistence

## bd Epics
- Foundation & Solution Scaffolding
- Audio I/O & Device Management
- Channel Strip & Metering
- Plugin Chain & Built-in DSP
- UI & View Modes
- VST Support
- Configuration & Persistence
