using HotMic.Core.Plugins.BuiltIn;

namespace HotMic.Core.Plugins;

public static class PluginFactory
{
    public static IPlugin? Create(string type)
    {
        return type switch
        {
            "builtin:compressor" => new CompressorPlugin(),
            "builtin:noisegate" => new NoiseGatePlugin(),
            "builtin:eq3" => new FiveBandEqPlugin(),
            "builtin:hpf" => new HighPassFilterPlugin(),
            "builtin:deesser" => new DeEsserPlugin(),
            "builtin:saturation" => new SaturationPlugin(),
            "builtin:limiter" => new LimiterPlugin(),
            "builtin:fft-noise" => new FFTNoiseRemovalPlugin(),
            "builtin:gain" => new GainPlugin(),
            "builtin:freq-analyzer" => new FrequencyAnalyzerPlugin(),
            "builtin:vocal-spectrograph" => new VocalSpectrographPlugin(),
            "builtin:rnnoise" => new RNNoisePlugin(),
            "builtin:speechdenoiser" => new SpeechDenoiserPlugin(),
            "builtin:voice-gate" => new SileroVoiceGatePlugin(),
            "builtin:reverb" => new ConvolutionReverbPlugin(),
            "builtin:signal-generator" => new SignalGeneratorPlugin(),
            _ => null
        };
    }
}
