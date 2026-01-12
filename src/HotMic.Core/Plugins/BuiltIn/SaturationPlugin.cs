using HotMic.Common.Configuration;
using HotMic.Core.Dsp;
using HotMic.Core.Plugins;

namespace HotMic.Core.Plugins.BuiltIn;

public sealed class SaturationPlugin : IPlugin, IQualityConfigurablePlugin
{
    public const int WarmthIndex = 0;
    public const int BlendIndex = 1;

    private const float SmoothingMs = 8f;
    private const float AttackMs = 2f;
    private const float ReleaseMs = 60f;
    private const float PreEmphasisMaxDb = 2.5f;
    private const float PreEmphasisHz = 6000f;
    private const float HfSplitHz = 3500f;
    private const float FilterQ = 0.707f;
    private const float MaxDriveK = 0.6f;
    private const float MaxBias = 0.04f;
    private const float MinShaperA = 0.18f;
    private const float MaxShaperA = 0.25f;
    private const float MinShaperB = 0.015f;
    private const float MaxShaperB = 0.03f;
    private const float OutputTrimMax = 0.95f;

    private float _warmthPct = 30f;
    private float _blendPct = 100f;
    private int _sampleRate;
    private int _blockSize;
    private int _oversampleFactor = 4;
    private int _oversampledSampleRate;
    private int _latencySamples;

    // Thread-safe metering
    private int _inputLevelBits;
    private int _outputLevelBits;

    private LinearSmoother _warmthSmoother = new();
    private LinearSmoother _blendSmoother = new();

    private readonly EnvelopeFollower _envelope = new();
    private readonly BiquadFilter _preEmphasis = new();
    private readonly BiquadFilter _postEmphasis = new();
    private readonly BiquadFilter _hfLowpass = new();

    private HalfbandResampler[] _upsamplers = [];
    private HalfbandResampler[] _downsamplers = [];
    private float[][] _stageBuffers = [];
    private float[] _dryBuffer = Array.Empty<float>();

    public SaturationPlugin()
    {
        Parameters =
        [
            new PluginParameter { Index = WarmthIndex, Name = "Warmth", MinValue = 0f, MaxValue = 100f, DefaultValue = 30f, Unit = "%" },
            new PluginParameter { Index = BlendIndex, Name = "Blend", MinValue = 0f, MaxValue = 100f, DefaultValue = 100f, Unit = "%" }
        ];
    }

    public string Id => "builtin:saturation";

    public string Name => "Saturation";

    public bool IsBypassed { get; set; }

    public int LatencySamples => _latencySamples;

    public IReadOnlyList<PluginParameter> Parameters { get; }

    public float WarmthPct => _warmthPct;
    public float BlendPct => _blendPct;
    public int SampleRate => _sampleRate;

    public void Initialize(int sampleRate, int blockSize)
    {
        _sampleRate = sampleRate;
        _blockSize = blockSize;
        ConfigureOversampling();
    }

    public void Process(Span<float> buffer)
    {
        if (IsBypassed || buffer.IsEmpty)
        {
            return;
        }

        var drySpan = _dryBuffer.AsSpan(0, buffer.Length);
        float inputPeak = 0f;
        for (int i = 0; i < buffer.Length; i++)
        {
            float input = buffer[i];
            drySpan[i] = input;
            inputPeak = MathF.Max(inputPeak, MathF.Abs(input));
        }

        ReadOnlySpan<float> stageInput = buffer;
        int stageLength = buffer.Length;
        for (int stage = 0; stage < _upsamplers.Length; stage++)
        {
            int stageOutputLength = stageLength * 2;
            var stageBuffer = _stageBuffers[stage].AsSpan(0, stageOutputLength);
            _upsamplers[stage].ProcessUpsample(stageInput, stageBuffer);
            stageInput = stageBuffer;
            stageLength = stageOutputLength;
        }

        Span<float> oversampled = _stageBuffers[^1].AsSpan(0, stageLength);
        float warmth = _warmthSmoother.Current;
        bool warmthSmooth = _warmthSmoother.IsSmoothing;
        for (int i = 0; i < oversampled.Length; i++)
        {
            if (warmthSmooth)
            {
                warmth = _warmthSmoother.Next();
                warmthSmooth = _warmthSmoother.IsSmoothing;
            }

            float bias = MaxBias * warmth;
            float driveK = MaxDriveK * warmth;
            float shaperA = (MinShaperA + (MaxShaperA - MinShaperA) * warmth) * warmth;
            float shaperB = (MinShaperB + (MaxShaperB - MinShaperB) * warmth) * warmth;
            float hfCompression = warmth;
            float outputTrim = 1f - (1f - OutputTrimMax) * warmth;

            float x1 = _preEmphasis.Process(oversampled[i]);
            float env = _envelope.Process(x1);
            float drive = 1f + driveK * env;
            float x2 = (x1 + bias) * drive;
            float biasSignal = bias * drive;

            // Polynomial shaper (spec: y = x - a*x^3 + b*x^5) for even-harmonic warmth.
            float x2Sq = x2 * x2;
            float x2Cube = x2Sq * x2;
            float x2Pow5 = x2Cube * x2Sq;
            float shaped = x2 - shaperA * x2Cube + shaperB * x2Pow5;

            // Remove DC offset introduced by the bias so silence stays silent.
            float biasSq = biasSignal * biasSignal;
            float biasCube = biasSq * biasSignal;
            float biasPow5 = biasCube * biasSq;
            float biasOutput = biasSignal - shaperA * biasCube + shaperB * biasPow5;
            shaped -= biasOutput;

            float low = _hfLowpass.Process(shaped);
            float high = shaped - low;
            float hfGain = 1f / (1f + hfCompression * env);
            float y = low + high * hfGain;

            y = _postEmphasis.Process(y);
            oversampled[i] = y * outputTrim;
        }

        ReadOnlySpan<float> downInput = oversampled;
        int downLength = oversampled.Length;
        for (int stage = _downsamplers.Length - 1; stage >= 0; stage--)
        {
            int outputLength = downLength / 2;
            Span<float> downOutput = stage == 0
                ? buffer
                : _stageBuffers[stage - 1].AsSpan(0, outputLength);
            _downsamplers[stage].ProcessDownsample(downInput, downOutput);
            downInput = downOutput;
            downLength = outputLength;
        }

        float blend = _blendSmoother.Current;
        bool blendSmooth = _blendSmoother.IsSmoothing;
        float outputPeak = 0f;
        for (int i = 0; i < buffer.Length; i++)
        {
            if (blendSmooth)
            {
                blend = _blendSmoother.Next();
                blendSmooth = _blendSmoother.IsSmoothing;
            }

            float dry = drySpan[i];
            float wet = buffer[i];
            float output = dry + (wet - dry) * blend;
            buffer[i] = output;
            outputPeak = MathF.Max(outputPeak, MathF.Abs(output));
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
            case WarmthIndex:
                _warmthPct = Math.Clamp(value, 0f, 100f);
                if (_sampleRate > 0)
                {
                    _warmthSmoother.SetTarget(_warmthPct / 100f);
                    UpdateToneFilters(_warmthPct / 100f);
                }
                break;
            case BlendIndex:
                _blendPct = Math.Clamp(value, 0f, 100f);
                if (_sampleRate > 0)
                {
                    _blendSmoother.SetTarget(_blendPct / 100f);
                }
                break;
        }
    }

    public byte[] GetState()
    {
        var bytes = new byte[sizeof(float) * 2];
        Buffer.BlockCopy(BitConverter.GetBytes(_warmthPct), 0, bytes, 0, 4);
        Buffer.BlockCopy(BitConverter.GetBytes(_blendPct), 0, bytes, 4, 4);
        return bytes;
    }

    public void SetState(byte[] state)
    {
        if (state.Length < sizeof(float))
        {
            return;
        }

        _warmthPct = Math.Clamp(BitConverter.ToSingle(state, 0), 0f, 100f);
        if (state.Length >= sizeof(float) * 2)
        {
            _blendPct = Math.Clamp(BitConverter.ToSingle(state, 4), 0f, 100f);
        }

        if (_sampleRate > 0)
        {
            _warmthSmoother.SetTarget(_warmthPct / 100f);
            _blendSmoother.SetTarget(_blendPct / 100f);
            UpdateToneFilters(_warmthPct / 100f);
        }
    }

    public float GetAndResetInputLevel()
    {
        return BitConverter.Int32BitsToSingle(Interlocked.Exchange(ref _inputLevelBits, 0));
    }

    public float GetAndResetOutputLevel()
    {
        return BitConverter.Int32BitsToSingle(Interlocked.Exchange(ref _outputLevelBits, 0));
    }

    public void Dispose()
    {
    }

    public void ApplyQuality(AudioQualityProfile profile)
    {
        int targetFactor = profile.Mode == AudioQualityMode.QualityPriority ? 8 : 4;
        if (_oversampleFactor == targetFactor)
        {
            return;
        }

        _oversampleFactor = targetFactor;
        if (_sampleRate > 0)
        {
            ConfigureOversampling();
        }
    }

    private void ConfigureOversampling()
    {
        int stages = _oversampleFactor == 8 ? 3 : 2;
        _upsamplers = new HalfbandResampler[stages];
        _downsamplers = new HalfbandResampler[stages];
        _stageBuffers = new float[stages][];

        int length = _blockSize * 2;
        for (int i = 0; i < stages; i++)
        {
            _upsamplers[i] = new HalfbandResampler();
            _downsamplers[i] = new HalfbandResampler();
            _stageBuffers[i] = new float[length];
            length *= 2;
        }

        _dryBuffer = new float[_blockSize];
        _oversampledSampleRate = _sampleRate * _oversampleFactor;

        _warmthSmoother.Configure(_oversampledSampleRate, SmoothingMs, _warmthPct / 100f);
        _blendSmoother.Configure(_sampleRate, SmoothingMs, _blendPct / 100f);
        _envelope.Configure(AttackMs, ReleaseMs, _oversampledSampleRate);
        _envelope.Reset();

        _preEmphasis.Reset();
        _postEmphasis.Reset();
        _hfLowpass.Reset();
        UpdateToneFilters(_warmthPct / 100f);
        UpdateLatency();
    }

    private void UpdateToneFilters(float warmth)
    {
        if (_oversampledSampleRate <= 0)
        {
            return;
        }

        float emphasisDb = PreEmphasisMaxDb * Math.Clamp(warmth, 0f, 1f);
        _preEmphasis.SetHighShelf(_oversampledSampleRate, PreEmphasisHz, emphasisDb, FilterQ);
        _postEmphasis.SetHighShelf(_oversampledSampleRate, PreEmphasisHz, -emphasisDb, FilterQ);
        _hfLowpass.SetLowPass(_oversampledSampleRate, HfSplitHz, FilterQ);
    }

    private void UpdateLatency()
    {
        if (_upsamplers.Length == 0)
        {
            _latencySamples = 0;
            return;
        }

        int filterDelay = _upsamplers[0].FilterDelaySamples;
        float latency = 0f;
        float divisor = 2f;
        for (int i = 0; i < _upsamplers.Length; i++)
        {
            latency += filterDelay / divisor;
            divisor *= 2f;
        }

        latency *= 2f; // up + down
        _latencySamples = Math.Max(0, (int)MathF.Round(latency));
    }
}
