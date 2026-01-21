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
            "builtin:analysis-tap" => new AnalysisTapPlugin(),
            "builtin:upward-expander" => new UpwardExpanderPlugin(),
            "builtin:spectral-contrast" => new SpectralContrastPlugin(),
            "builtin:dynamic-eq" => new DynamicEqPlugin(),
            "builtin:room-tone" => new RoomTonePlugin(),
            "builtin:air-exciter" => new AirExciterPlugin(),
            "builtin:bass-enhancer" => new BassEnhancerPlugin(),
            "builtin:vitalizer-mk2t" => new VitalizerMk2TPlugin(),
            "builtin:consonant-transient" => new ConsonantTransientPlugin(),
            "builtin:input" => new InputPlugin(),
            "builtin:copy" => new CopyToChannelPlugin(),
            "builtin:bus-input" => new BusInputPlugin(),
            "builtin:merge" => new MergePlugin(),
            "builtin:output-send" => new OutputSendPlugin(),
            _ => null
        };
    }
}
