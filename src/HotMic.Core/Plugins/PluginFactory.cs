using HotMic.Core.Plugins.BuiltIn;

namespace HotMic.Core.Plugins;

public static class PluginFactory
{
    public static IPlugin? Create(string type)
    {
        switch (type)
        {
            case "builtin:compressor": return new CompressorPlugin();
            case "builtin:noisegate": return new NoiseGatePlugin();
            case "builtin:eq3": return new FiveBandEqPlugin();
            case "builtin:hpf": return new HighPassFilterPlugin();
            case "builtin:deesser": return new DeEsserPlugin();
            case "builtin:saturation": return new SaturationPlugin();
            case "builtin:limiter": return new LimiterPlugin();
            case "builtin:fft-noise": return new FFTNoiseRemovalPlugin();
            case "builtin:deverb": return new DereverbPlugin();
            case "builtin:gain": return new GainPlugin();
            case "builtin:rnnoise": return new RNNoisePlugin();
            case "builtin:speechdenoiser": return new SpeechDenoiserPlugin();
            case "builtin:voice-gate": return new SileroVoiceGatePlugin();
            case "builtin:reverb": return new ConvolutionReverbPlugin();
            case "builtin:signal-generator": return new SignalGeneratorPlugin();
            case "builtin:analysis-tap": return new AnalysisTapPlugin();
            case "builtin:upward-expander": return new UpwardExpanderPlugin();
            case "builtin:spectral-contrast": return new SpectralContrastPlugin();
            case "builtin:dynamic-eq": return new DynamicEqPlugin();
            case "builtin:room-tone": return new RoomTonePlugin();
            case "builtin:air-exciter": return new AirExciterPlugin();
            case "builtin:bass-enhancer": return new BassEnhancerPlugin();
            case "builtin:vitalizer-mk2t": return new VitalizerMk2TPlugin();
            case "builtin:consonant-transient": return new ConsonantTransientPlugin();
            case "builtin:input": return new InputPlugin();
            case "builtin:copy": return new CopyToChannelPlugin();
            case "builtin:bus-input": return new BusInputPlugin();
            case "builtin:merge": return new MergePlugin();
            case "builtin:output-send": return new OutputSendPlugin();
            default: return null;
        }
    }
}
