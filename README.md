<p align="center">
  <strong>HotMic</strong>
</p>

# HotMic

A low-latency Windows audio router for two microphones with per-channel DSP, AI noise reduction, and a custom WPF + Skia UI.

**Windows only. Requires VB-Cable** for the virtual output device.

## Why HotMic?

If you have two mics, a handful of plugins you trust, and a meeting or stream to run, you should not need a full mixer app. HotMic focuses on a clean routing path, fast channel strip edits, and repeatable presets.

## How It Works

HotMic captures up to two hardware microphone inputs with WASAPI, runs each input through its own channel strip, then routes the result to a single virtual output device.

Signal flow (routing selectable):

```
Mic 1 -> Input Gain -> Plugins -> Output Gain -> Left or Sum -> VB-Cable
Mic 2 -> Input Gain -> Plugins -> Output Gain -> Right or Sum -> VB-Cable
(Optional) Monitor output mirrors the master mix
```

Routing options:
- Input channel mode per mic: Sum / Left / Right
- Output routing: Split (L/R) or Sum (mono)

## Channel Strips

Each channel includes:
- Input and output gain staging
- Mute / solo with smooth ramps
- Pre and post meters (RMS + peak)
- Per-slot plugin meters and bypass

Master meters include LUFS (momentary + short-term) on the output.

## Built-in Plugins

Dynamics:
- Gain (with phase invert)
- Noise Gate
- Compressor
- De-Esser
- Limiter

EQ:
- High-Pass Filter
- 5-Band EQ (HPF + shelves + dual mid bands)

Noise Reduction:
- FFT Noise Removal (learned profile)

AI / ML:
- RNNoise (neural noise suppression)
- Speech Denoiser (DFN3 streaming model)
- Voice Gate (Silero VAD)

Effects:
- Saturation
- Convolution Reverb

## Presets

HotMic ships with built-in chain presets (Broadcast Radio, Clean/Natural, Podcast/Voiceover, SM7B Optimized, NT1 Optimized) plus per-plugin presets. Custom presets can be saved and recalled.

## VST Plugin Support

HotMic scans common VST2 and VST3 locations (plus optional custom paths), caches results, and lets you host external plugins in the chain. Native editor windows are supported when available.

## MIDI Control

Enable MIDI in Settings and use MIDI Learn on gain knobs or plugin parameters to bind CC controls.

## Views

- Full Edit View for building and tuning chains
- Minimal View for monitoring levels at a glance

Always-on-top mode keeps controls within reach during calls or streams.

## Quick Start

1. Install VB-Cable and reboot if required.
2. Launch HotMic and select your two mic inputs.
3. Set output to VB-Cable (optional: pick a monitor output).
4. Choose routing (Split or Sum) and input channel modes as needed.
5. Add plugins or pick a preset chain.
6. Set your app's input device to "VB-Cable Output".

## Notes

- RNNoise, Speech Denoiser, and Voice Gate require 48 kHz and will auto-bypass at other sample rates.
- Configuration is stored in `%AppData%\HotMic\config.json`.

## Documentation

- `docs/README.md` - documentation index (DSP/algorithm references, architecture, feature docs).
- `docs/technical/README.md` - DSP/algorithm references.

## Building From Source

Requires the .NET 8 SDK on Windows.

```bash
git clone https://github.com/chris-arsenault/hot-mic
cd hot-mic
dotnet build
dotnet run --project src/HotMic.App
```

## Tech Stack

- .NET 8 / WPF
- SkiaSharp for UI rendering
- NAudio for WASAPI capture
- CommunityToolkit.Mvvm
- VST.NET (Jacobi.Vst) for VST hosting
- Microsoft.ML.OnnxRuntime for AI models
- System.Text.Json for configuration

## License

MIT License - see [LICENSE](LICENSE).

---

HotMic is designed to stay out of your way while making your mic sound better. Clean routing, minimal latency, no fluff.
