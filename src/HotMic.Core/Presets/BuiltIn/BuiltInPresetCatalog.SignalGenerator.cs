namespace HotMic.Core.Presets;

internal static partial class BuiltInPresetCatalog
{
    private static PluginPresetBank BuildSignalGeneratorBank()
    {
        // Generator type values: Sine=0, Square=1, Saw=2, Triangle=3, White=4, Pink=5, Brown=6, Blue=7, Impulse=8, Chirp=9, Sample=10
        return new PluginPresetBank("builtin:signal-generator",
        [
            // Frequency Sweep: Slot 1 = sine sweep 80Hz-8kHz
            CreatePreset("Frequency Sweep",
                ("Slot 1 Type", 0f),           // Sine
                ("Slot 1 Frequency", 80f),
                ("Slot 1 Gain", -12f),
                ("Slot 1 Mute", 0f),
                ("Slot 1 Sweep", 1f),          // Enabled
                ("Slot 1 Sweep Start", 80f),
                ("Slot 1 Sweep End", 8000f),
                ("Slot 1 Sweep Duration", 5000f),
                ("Slot 1 Sweep Dir", 0f),      // Up
                ("Slot 1 Sweep Curve", 1f),    // Log
                ("Slot 2 Mute", 1f),
                ("Slot 3 Mute", 1f),
                ("Master Gain", -6f),
                ("Headroom", 1f)),

            // Sibilance Test: Slot 1 = 4kHz sine, Slot 2 = 8kHz sine, Slot 3 = blue noise
            CreatePreset("Sibilance Test",
                ("Slot 1 Type", 0f),           // Sine
                ("Slot 1 Frequency", 4000f),
                ("Slot 1 Gain", -18f),
                ("Slot 1 Mute", 0f),
                ("Slot 2 Type", 0f),           // Sine
                ("Slot 2 Frequency", 8000f),
                ("Slot 2 Gain", -18f),
                ("Slot 2 Mute", 0f),
                ("Slot 3 Type", 7f),           // Blue noise
                ("Slot 3 Gain", -24f),
                ("Slot 3 Mute", 0f),
                ("Master Gain", -6f),
                ("Headroom", 1f)),

            // Proximity Test: Slot 1 = brown noise, Slot 2 = 120Hz sine
            CreatePreset("Proximity Test",
                ("Slot 1 Type", 6f),           // Brown noise
                ("Slot 1 Gain", -18f),
                ("Slot 1 Mute", 0f),
                ("Slot 2 Type", 0f),           // Sine
                ("Slot 2 Frequency", 120f),
                ("Slot 2 Gain", -12f),
                ("Slot 2 Mute", 0f),
                ("Slot 3 Mute", 1f),
                ("Master Gain", -6f),
                ("Headroom", 1f)),

            // Pitch Test: Slot 1 = 220Hz sine (A3)
            CreatePreset("Pitch Test",
                ("Slot 1 Type", 0f),           // Sine
                ("Slot 1 Frequency", 220f),
                ("Slot 1 Gain", -12f),
                ("Slot 1 Mute", 0f),
                ("Slot 2 Mute", 1f),
                ("Slot 3 Mute", 1f),
                ("Master Gain", -12f),
                ("Headroom", 1f)),

            // Full Spectrum: Slot 1 = pink noise
            CreatePreset("Full Spectrum",
                ("Slot 1 Type", 5f),           // Pink noise
                ("Slot 1 Gain", -12f),
                ("Slot 1 Mute", 0f),
                ("Slot 2 Mute", 1f),
                ("Slot 3 Mute", 1f),
                ("Master Gain", -12f),
                ("Headroom", 1f)),

            // Transient Test: Slot 1 = impulse @ 100ms interval
            CreatePreset("Transient Test",
                ("Slot 1 Type", 8f),           // Impulse
                ("Slot 1 Impulse Interval", 100f),
                ("Slot 1 Gain", -6f),
                ("Slot 1 Mute", 0f),
                ("Slot 2 Mute", 1f),
                ("Slot 3 Mute", 1f),
                ("Master Gain", -6f),
                ("Headroom", 1f)),

            // White Noise Reference
            CreatePreset("White Noise",
                ("Slot 1 Type", 4f),           // White noise
                ("Slot 1 Gain", -18f),
                ("Slot 1 Mute", 0f),
                ("Slot 2 Mute", 1f),
                ("Slot 3 Mute", 1f),
                ("Master Gain", -12f),
                ("Headroom", 1f)),

            // Chirp Sweep: Slot 1 = chirp generator
            CreatePreset("Chirp Sweep",
                ("Slot 1 Type", 9f),           // Chirp
                ("Slot 1 Chirp Duration", 200f),
                ("Slot 1 Gain", -6f),
                ("Slot 1 Mute", 0f),
                ("Slot 2 Mute", 1f),
                ("Slot 3 Mute", 1f),
                ("Master Gain", -6f),
                ("Headroom", 1f)),

            // Square Wave Test
            CreatePreset("Square Wave",
                ("Slot 1 Type", 1f),           // Square
                ("Slot 1 Frequency", 440f),
                ("Slot 1 Pulse Width", 0.5f),
                ("Slot 1 Gain", -18f),
                ("Slot 1 Mute", 0f),
                ("Slot 2 Mute", 1f),
                ("Slot 3 Mute", 1f),
                ("Master Gain", -12f),
                ("Headroom", 1f)),

            // Harmonics Test: Slots with fundamental + 2nd + 3rd harmonic
            CreatePreset("Harmonics Test",
                ("Slot 1 Type", 0f),           // Sine - fundamental
                ("Slot 1 Frequency", 220f),
                ("Slot 1 Gain", -12f),
                ("Slot 1 Mute", 0f),
                ("Slot 2 Type", 0f),           // Sine - 2nd harmonic
                ("Slot 2 Frequency", 440f),
                ("Slot 2 Gain", -18f),
                ("Slot 2 Mute", 0f),
                ("Slot 3 Type", 0f),           // Sine - 3rd harmonic
                ("Slot 3 Frequency", 660f),
                ("Slot 3 Gain", -24f),
                ("Slot 3 Mute", 0f),
                ("Master Gain", -6f),
                ("Headroom", 1f))
        ]);
    }
}
