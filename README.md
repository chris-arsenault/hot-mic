<p align="center">
  <strong>HotMic</strong>
</p>

# HotMic

A low-latency Windows audio router for microphones that gives you a clean, repeatable voice chain without the bloat.

**Windows only. Requires VB-Cable** for the virtual output device.

## Why HotMic?

If you have two mics, a couple of plugins you trust, and a meeting or stream to run, you should not need a full mixer app to get great sound. HotMic focuses on the essentials: reliable routing, a modern channel strip UI, and a plugin chain you can tweak quickly.

## How It Works

HotMic captures up to two hardware microphone inputs with WASAPI, runs each input through its own channel strip, then mixes to a single virtual output device you can select in any app.

Signal flow:

```
Mic 1 -> Input Gain -> Plugins -> Output Gain -> VB-Cable
Mic 2 -> Input Gain -> Plugins -> Output Gain -> VB-Cable
```

## Channel Strips

Each channel includes:

- Input and output gain staging
- Pre and post meters (RMS + peak)
- Plugin chain with bypass per slot

Meters are built for fast reads at a glance and update smoothly without blocking audio.

## Built-in Plugins

HotMic ships with a focused set of DSP tools:

- Gain
- Noise Gate
- Compressor
- 3-Band EQ
- FFT-based noise reduction

## VST3 Support

Bring your favorite VST3 plugins into the chain. HotMic can scan common VST3 locations, load plugins, and host native editors when available.

## Views

Two modes keep the UI fast and focused:

- Full Edit View for building and tuning chains
- Minimal View for monitoring levels at a glance

Always-on-top mode keeps controls within reach during calls or streams.

## Quick Start

1. Install VB-Cable and reboot if required.
2. Launch HotMic and select your two mic inputs.
3. Add or reorder plugins in each channel strip.
4. Set your app's input device to "VB-Cable Output".
5. Dial in gain and dynamics, then save your configuration.

## Building From Source

Requires the .NET 8 SDK on Windows.

```bash
git clone https://github.com/chris-arsenault/hot-mic
cd hot-mic
dotnet build
dotnet run --project src/HotMic.App
```

Run tests:

```bash
dotnet test
```

## Tech Stack

- .NET 8 / WPF
- SkiaSharp for UI rendering
- NAudio for WASAPI capture
- VST.NET for VST3 hosting
- System.Text.Json for configuration

## License

MIT License - see [LICENSE](LICENSE).

---

HotMic is designed to stay out of your way while making your mic sound better. Clean routing, minimal latency, no fluff.
