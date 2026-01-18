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
    private const float FilterQ = 0.707f;
    private const float WarmthPivotPct = 50f;
    private const float WarmthOverdriveMax = 2f;

    // Pre/post tilt (tape-like emphasis)
    private const float PreTiltDb = 2f;
    private const float PreTiltHz = 6000f;

    // HF self-compression (prevents sibilant edge)
    private const float HfSplitHz = 3800f;
    private const float HfCompC = 0.7f;

    // Asymmetric tanh warmth core coefficients
    // k = base curvature, asymmetry = split between positive/negative halves
    // kPos = k * (1 + asym), kNeg = k * (1 - asym) for different saturation on +/-
    private const float K0 = 1.0f;    // Base curvature
    private const float K1 = 1.0f;    // Envelope-scaled curvature addition
    private const float A0 = 0.15f;   // Base asymmetry (0 = symmetric, higher = more even harmonics)
    private const float A1 = 0.20f;   // Envelope-scaled asymmetry addition

    private float _warmthPct = 50f;
    private float _blendPct = 100f;
    private int _sampleRate;
    private int _blockSize;
    private int _oversampleFactor = 4;
    private int _oversampledSampleRate;
    private int _latencySamples;

    // Thread-safe metering
    private int _inputLevelBits;
    private int _outputLevelBits;

    // Diagnostic capture for visualization
    private readonly SaturationDiagnostics _diagnostics = new();
    private int _diagnosticDecimator;
    private const int DiagnosticDecimation = 8; // Capture every Nth sample to reduce overhead

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

    // Reference path for null-difference analysis
    // Must have identical filter chain (OS + linear filters) but NO saturation nonlinearity
    private HalfbandResampler[] _refUpsamplers = [];
    private HalfbandResampler[] _refDownsamplers = [];
    private float[][] _refStageBuffers = [];
    private float[] _dryFilteredBuffer = Array.Empty<float>();
    private readonly BiquadFilter _refPreEmphasis = new();
    private readonly BiquadFilter _refPostEmphasis = new();
    private readonly BiquadFilter _refHfLowpass = new();

    public SaturationPlugin()
    {
        Parameters =
        [
            new PluginParameter { Index = WarmthIndex, Name = "Warmth", MinValue = 0f, MaxValue = 100f, DefaultValue = 50f, Unit = "%" },
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
    public SaturationDiagnostics Diagnostics => _diagnostics;

    public void Initialize(int sampleRate, int blockSize)
    {
        _sampleRate = sampleRate;
        _blockSize = blockSize;
        ConfigureOversampling();
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

            // 1) Pre-tilt (push presence into the nonlinearity)
            float x1 = _preEmphasis.Process(oversampled[i]);

            // 2) Envelope follower (for dynamic behavior)
            float env = _envelope.Process(x1);

            // 3) Asymmetric tanh warmth core with split curvature
            // Different k for positive vs negative creates even harmonics at all signal levels
            float k = (K0 + K1 * env) * warmth;
            float asym = (A0 + A1 * env) * warmth;

            // Split curvature: positive saturates harder, negative saturates softer
            // This asymmetry persists regardless of signal amplitude
            float kPos = k * (1f + asym);
            float kNeg = k * (1f - asym);

            float shaped;
            if (x1 >= 0f)
            {
                shaped = MathF.Tanh(kPos * x1);
            }
            else
            {
                shaped = MathF.Tanh(kNeg * x1);
            }

            // Gain normalization: derivative at x=0 is k (average of kPos and kNeg at origin)
            // Since tanh'(0) = 1, derivative is just the k coefficient
            if (k > 0.001f)
            {
                shaped /= k;
            }

            // 4) HF self-compression (prevents sibilant edge)
            float low = _hfLowpass.Process(shaped);
            float high = shaped - low;
            float hfGain = 1f / (1f + HfCompC * warmth * env);
            float y = low + high * hfGain;

            // 5) De-tilt (inverse of pre-tilt)
            float finalOutput = _postEmphasis.Process(y);
            oversampled[i] = finalOutput;

            // Capture diagnostic samples (decimated to reduce overhead)
            if (++_diagnosticDecimator >= DiagnosticDecimation)
            {
                _diagnosticDecimator = 0;
                _diagnostics.RecordTransferSample(x1, finalOutput, env);
            }
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

        // Reference path: run dry through IDENTICAL filter chain (OS + linear filters) but NO saturation.
        // This ensures delta = 0 at warmth=0, and delta shows ONLY what the nonlinearity added.
        ReadOnlySpan<float> refInput = drySpan;
        int refLength = buffer.Length;
        for (int stage = 0; stage < _refUpsamplers.Length; stage++)
        {
            int refOutputLength = refLength * 2;
            var refBuffer = _refStageBuffers[stage].AsSpan(0, refOutputLength);
            _refUpsamplers[stage].ProcessUpsample(refInput, refBuffer);
            refInput = refBuffer;
            refLength = refOutputLength;
        }

        // Process reference through same linear filters (pre-emphasis, HF split, post-emphasis)
        // but skip the nonlinear saturation (no bias, no drive, no shaper)
        Span<float> refOversampled = _refStageBuffers[^1].AsSpan(0, refLength);
        for (int i = 0; i < refOversampled.Length; i++)
        {
            float x1 = _refPreEmphasis.Process(refOversampled[i]);
            // Skip envelope, drive, bias, shaper - just pass through linearly
            float low = _refHfLowpass.Process(x1);
            float high = x1 - low;
            float y = low + high; // No HF compression (hfGain = 1)
            y = _refPostEmphasis.Process(y);
            refOversampled[i] = y;
        }

        ReadOnlySpan<float> refDownInput = refOversampled;
        int refDownLength = refLength;
        var dryFilteredSpan = _dryFilteredBuffer.AsSpan(0, buffer.Length);
        for (int stage = _refDownsamplers.Length - 1; stage >= 0; stage--)
        {
            int outputLength = refDownLength / 2;
            Span<float> refDownOutput = stage == 0
                ? dryFilteredSpan
                : _refStageBuffers[stage - 1].AsSpan(0, outputLength);
            _refDownsamplers[stage].ProcessDownsample(refDownInput, refDownOutput);
            refDownInput = refDownOutput;
            refDownLength = outputLength;
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

            // Null-difference: delta = wet - reference (reference has same linear filters, no saturation)
            // At warmth=0, delta should be ~0. At warmth>0, delta shows ONLY what saturation added.
            float dryFiltered = dryFilteredSpan[i];
            float delta = wet - dryFiltered;
            _diagnostics.RecordScopeSample(delta);
            _diagnostics.RecordFftSample(delta);
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
                    float normalized = MapWarmthNormalized(_warmthPct);
                    _warmthSmoother.SetTarget(normalized);
                    UpdateToneFilters(normalized);
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
            _warmthSmoother.SetTarget(MapWarmthNormalized(_warmthPct));
            _blendSmoother.SetTarget(_blendPct / 100f);
            UpdateToneFilters(MapWarmthNormalized(_warmthPct));
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

        // Reference path (for null-difference analysis)
        _refUpsamplers = new HalfbandResampler[stages];
        _refDownsamplers = new HalfbandResampler[stages];
        _refStageBuffers = new float[stages][];

        int length = _blockSize * 2;
        for (int i = 0; i < stages; i++)
        {
            _upsamplers[i] = new HalfbandResampler();
            _downsamplers[i] = new HalfbandResampler();
            _stageBuffers[i] = new float[length];

            _refUpsamplers[i] = new HalfbandResampler();
            _refDownsamplers[i] = new HalfbandResampler();
            _refStageBuffers[i] = new float[length];

            length *= 2;
        }

        _dryBuffer = new float[_blockSize];
        _dryFilteredBuffer = new float[_blockSize];
        _oversampledSampleRate = _sampleRate * _oversampleFactor;

        _warmthSmoother.Configure(_oversampledSampleRate, SmoothingMs, MapWarmthNormalized(_warmthPct));
        _blendSmoother.Configure(_sampleRate, SmoothingMs, _blendPct / 100f);
        _envelope.Configure(AttackMs, ReleaseMs, _oversampledSampleRate);
        _envelope.Reset();

        _preEmphasis.Reset();
        _postEmphasis.Reset();
        _hfLowpass.Reset();
        _refPreEmphasis.Reset();
        _refPostEmphasis.Reset();
        _refHfLowpass.Reset();
        UpdateToneFilters(MapWarmthNormalized(_warmthPct));
        UpdateLatency();
    }

    private void UpdateToneFilters(float warmth)
    {
        if (_oversampledSampleRate <= 0)
        {
            return;
        }

        // Pre/de-tilt: fixed +/-2dB @ 6kHz (tape-like emphasis)
        // The tilt is always applied; warmth controls the nonlinearity, not the tilt
        _preEmphasis.SetHighShelf(_oversampledSampleRate, PreTiltHz, PreTiltDb, FilterQ);
        _postEmphasis.SetHighShelf(_oversampledSampleRate, PreTiltHz, -PreTiltDb, FilterQ);
        _hfLowpass.SetLowPass(_oversampledSampleRate, HfSplitHz, FilterQ);

        // Reference path filters (identical coefficients for proper null-difference)
        _refPreEmphasis.SetHighShelf(_oversampledSampleRate, PreTiltHz, PreTiltDb, FilterQ);
        _refPostEmphasis.SetHighShelf(_oversampledSampleRate, PreTiltHz, -PreTiltDb, FilterQ);
        _refHfLowpass.SetLowPass(_oversampledSampleRate, HfSplitHz, FilterQ);
    }

    private static float MapWarmthNormalized(float warmthPct)
    {
        float clamped = Math.Clamp(warmthPct, 0f, 100f);
        if (clamped <= WarmthPivotPct)
        {
            return clamped / WarmthPivotPct;
        }

        float t = (clamped - WarmthPivotPct) / (100f - WarmthPivotPct);
        return 1f + t * (WarmthOverdriveMax - 1f);
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
