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
            "builtin:eq3" => new ThreeBandEqPlugin(),
            "builtin:fft-noise" => new FFTNoiseRemovalPlugin(),
            _ => null
        };
    }
}
