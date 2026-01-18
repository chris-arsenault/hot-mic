using HotMic.Core.Dsp;
using HotMic.Core.Plugins;

namespace HotMic.Core.Plugins.BuiltIn;

public sealed class HighPassFilterPlugin : IContextualPlugin
{
    public const int CutoffIndex = 0;
    public const int SlopeIndex = 1;

    private const float MinCutoffHz = 40f;
    private const float MaxCutoffHz = 200f;
    private const int FftSize = 256;
    private const int SpectrumBins = 32;
    private const float SpectrumDecay = 0.92f;

    private float _cutoffHz = 100f;
    private float _slopeDbOct = 18f;
    private int _sampleRate;
    private bool _useFirstOrder = true;

    // Thread-safe metering
    private int _inputLevelBits;
    private int _outputLevelBits;

    private SpectrumDisplayWorker? _spectrumWorker;

    private readonly BiquadFilter _highPass = new();
    private OnePoleHighPass _firstOrder = new();

    public HighPassFilterPlugin()
    {
        Parameters =
        [
            new PluginParameter
            {
                Index = CutoffIndex,
                Name = "Cutoff",
                MinValue = MinCutoffHz,
                MaxValue = MaxCutoffHz,
                DefaultValue = 100f,
                Unit = "Hz"
            },
            new PluginParameter
            {
                Index = SlopeIndex,
                Name = "Slope",
                MinValue = 12f,
                MaxValue = 18f,
                DefaultValue = 18f,
                Unit = "dB/oct"
            }
        ];
    }

    public string Id => "builtin:hpf";

    public string Name => "High-Pass Filter";

    public bool IsBypassed { get; set; }

    public int LatencySamples => 0;

    public IReadOnlyList<PluginParameter> Parameters { get; }

    public float CutoffHz => _cutoffHz;
    public float SlopeDbOct => _slopeDbOct;
    public int SampleRate => _sampleRate;

    public void Initialize(int sampleRate, int blockSize)
    {
        _sampleRate = sampleRate;
        _spectrumWorker?.Dispose();
        _spectrumWorker = new SpectrumDisplayWorker(sampleRate, FftSize, SpectrumBins, 20f, 500f, SpectrumDecay, FftSize / 2);
        UpdateFilters();
    }

    public void Process(Span<float> buffer, in PluginProcessContext context)
    {
        Process(buffer);
    }

    public void Process(Span<float> buffer)
    {
        if (IsBypassed || buffer.IsEmpty)
        {
            return;
        }

        _spectrumWorker?.Write(buffer);

        bool useFirstOrder = _useFirstOrder;
        float inputPeak = 0f;
        float outputPeak = 0f;

        for (int i = 0; i < buffer.Length; i++)
        {
            float input = buffer[i];
            inputPeak = MathF.Max(inputPeak, MathF.Abs(input));

            float sample = _highPass.Process(input);
            if (useFirstOrder)
            {
                sample = _firstOrder.Process(sample);
            }
            outputPeak = MathF.Max(outputPeak, MathF.Abs(sample));
            buffer[i] = sample;
        }

        // Update metering (thread-safe)
        UpdatePeakLevel(ref _inputLevelBits, inputPeak);
        UpdatePeakLevel(ref _outputLevelBits, outputPeak);
    }

    private static void UpdatePeakLevel(ref int levelBits, float newPeak)
    {
        int current = Interlocked.CompareExchange(ref levelBits, 0, 0);
        float currentPeak = BitConverter.Int32BitsToSingle(current);
        if (newPeak > currentPeak)
        {
            Interlocked.Exchange(ref levelBits, BitConverter.SingleToInt32Bits(newPeak));
        }
    }

    public void SetParameter(int index, float value)
    {
        switch (index)
        {
            case CutoffIndex:
                _cutoffHz = Math.Clamp(value, MinCutoffHz, MaxCutoffHz);
                break;
            case SlopeIndex:
                // Quantize to the supported slopes to avoid ambiguous values.
                _slopeDbOct = value >= 15f ? 18f : 12f;
                _useFirstOrder = _slopeDbOct >= 18f;
                break;
        }

        UpdateFilters();
    }

    public byte[] GetState()
    {
        var bytes = new byte[sizeof(float) * 2];
        Buffer.BlockCopy(BitConverter.GetBytes(_cutoffHz), 0, bytes, 0, 4);
        Buffer.BlockCopy(BitConverter.GetBytes(_slopeDbOct), 0, bytes, 4, 4);
        return bytes;
    }

    public void SetState(byte[] state)
    {
        if (state.Length < sizeof(float))
        {
            return;
        }

        _cutoffHz = BitConverter.ToSingle(state, 0);
        if (state.Length >= sizeof(float) * 2)
        {
            _slopeDbOct = BitConverter.ToSingle(state, 4);
        }

        _useFirstOrder = _slopeDbOct >= 18f;
        UpdateFilters();
    }

    public float GetAndResetInputLevel()
    {
        return BitConverter.Int32BitsToSingle(Interlocked.Exchange(ref _inputLevelBits, 0));
    }

    public float GetAndResetOutputLevel()
    {
        return BitConverter.Int32BitsToSingle(Interlocked.Exchange(ref _outputLevelBits, 0));
    }

    /// <summary>
    /// Gets spectrum data for UI visualization. Caller must provide array of size SpectrumBins (32).
    /// Values are linear magnitudes (0-1 range, approximately).
    /// </summary>
    public void GetSpectrum(float[] spectrum)
    {
        if (spectrum.Length < SpectrumBins)
        {
            return;
        }

        _spectrumWorker?.GetSpectrum(spectrum);
    }

    public void Dispose()
    {
        _spectrumWorker?.Dispose();
        _spectrumWorker = null;
    }

    private void UpdateFilters()
    {
        if (_sampleRate <= 0)
        {
            return;
        }

        _highPass.SetHighPass(_sampleRate, _cutoffHz, 0.707f);
        _firstOrder.Configure(_cutoffHz, _sampleRate);
    }

}
