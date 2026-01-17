using System.Threading;

namespace HotMic.Core.Plugins.BuiltIn;

public sealed class FormantEnhancerPlugin : IContextualPlugin, ISidechainConsumer, IPluginStatusProvider
{
    public const int AmountIndex = 0;
    public const int BoostIndex = 1;
    public const int SmoothingIndex = 2;

    private static readonly float[] F1Candidates = { 350f, 500f, 700f, 900f };
    private static readonly float[] F2Candidates = { 900f, 1400f, 1900f, 2400f };
    private static readonly float[] F3Candidates = { 2500f, 3000f, 3400f };

    private const int F1Offset = 0;
    private const int F2Offset = F1Offset + 4;
    private const int F3Offset = F2Offset + 4;
    private const int CandidateCount = F3Offset + 3;

    private float _amount = 0.4f;
    private float _boostDb = 2f;
    private float _smoothingMs = 120f;

    private float _currentF1 = 500f;
    private float _currentF2 = 1400f;
    private float _currentF3 = 3000f;
    private float _smoothingCoeff;

    private int _sampleRate;
    private int _blockSize;
    private string _statusMessage = string.Empty;

    private const string MissingSidechainMessage = "Missing sidechain data.";

    private readonly BiquadFilter _f1Enhance = new();
    private readonly BiquadFilter _f2Enhance = new();
    private readonly BiquadFilter _f3Enhance = new();

    private readonly BiquadFilter[] _candidateFilters = new BiquadFilter[CandidateCount];
    private readonly EnvelopeFollower[] _candidateEnvs = new EnvelopeFollower[CandidateCount];
    private readonly float[] _candidateSums = new float[CandidateCount];

    // Metering
    private float _meterF1Hz;
    private float _meterF2Hz;
    private float _meterF3Hz;
    private float _meterSpeechPresence;

    public FormantEnhancerPlugin()
    {
        Parameters =
        [
            new PluginParameter { Index = AmountIndex, Name = "Amount", MinValue = 0f, MaxValue = 1f, DefaultValue = 0.4f, Unit = string.Empty },
            new PluginParameter { Index = BoostIndex, Name = "Boost", MinValue = 0f, MaxValue = 4f, DefaultValue = 2f, Unit = "dB" },
            new PluginParameter { Index = SmoothingIndex, Name = "Smoothing", MinValue = 40f, MaxValue = 200f, DefaultValue = 120f, Unit = "ms" }
        ];

        for (int i = 0; i < CandidateCount; i++)
        {
            _candidateFilters[i] = new BiquadFilter();
            _candidateEnvs[i] = new EnvelopeFollower();
        }
    }

    public string Id => "builtin:formant-enhance";

    public string Name => "Formant Enhancer";

    public bool IsBypassed { get; set; }

    public int LatencySamples => 0;

    public IReadOnlyList<PluginParameter> Parameters { get; }

    public SidechainSignalMask RequiredSignals => SidechainSignalMask.SpeechPresence;

    public string StatusMessage => Volatile.Read(ref _statusMessage);

    public float Amount => _amount;
    public float BoostDb => _boostDb;
    public float SmoothingMs => _smoothingMs;
    public int SampleRate => _sampleRate;

    public void SetSidechainAvailable(bool available)
    {
        Volatile.Write(ref _statusMessage, available ? string.Empty : MissingSidechainMessage);
    }

    public void Initialize(int sampleRate, int blockSize)
    {
        _sampleRate = sampleRate;
        _blockSize = blockSize;
        ConfigureCandidateFilters();
        UpdateSmoothing();
        UpdateEnhancers(_boostDb);
    }

    public void Process(Span<float> buffer, in PluginProcessContext context)
    {
        if (IsBypassed || buffer.IsEmpty)
        {
            return;
        }

        Array.Clear(_candidateSums);

        if (!context.TryGetSidechainSource(SidechainSignalId.SpeechPresence, out var speechSource))
        {
            return;
        }

        long baseTime = context.SampleTime;
        float speechSum = 0f;

        for (int i = 0; i < buffer.Length; i++)
        {
            float input = buffer[i];

            float enhanced = _f1Enhance.Process(input);
            enhanced = _f2Enhance.Process(enhanced);
            enhanced = _f3Enhance.Process(enhanced);

            buffer[i] = input * (1f - _amount) + enhanced * _amount;

            for (int c = 0; c < CandidateCount; c++)
            {
                float filtered = _candidateFilters[c].Process(input);
                float env = _candidateEnvs[c].Process(filtered);
                _candidateSums[c] += env;
            }

            speechSum += speechSource.ReadSample(baseTime + i);
        }

        float speechAvg = Math.Clamp(speechSum / buffer.Length, 0f, 1f);
        UpdateTargets(speechAvg);

        // Update metering
        _meterF1Hz = _currentF1;
        _meterF2Hz = _currentF2;
        _meterF3Hz = _currentF3;
        _meterSpeechPresence = speechAvg;
    }

    /// <summary>Gets the current F1 formant frequency in Hz.</summary>
    public float GetF1Hz() => Volatile.Read(ref _meterF1Hz);

    /// <summary>Gets the current F2 formant frequency in Hz.</summary>
    public float GetF2Hz() => Volatile.Read(ref _meterF2Hz);

    /// <summary>Gets the current F3 formant frequency in Hz.</summary>
    public float GetF3Hz() => Volatile.Read(ref _meterF3Hz);

    /// <summary>Gets the current speech presence level (0-1).</summary>
    public float GetSpeechPresence() => Volatile.Read(ref _meterSpeechPresence);

    public void Process(Span<float> buffer)
    {
        if (IsBypassed || buffer.IsEmpty)
        {
            return;
        }

        for (int i = 0; i < buffer.Length; i++)
        {
            float input = buffer[i];
            float enhanced = _f1Enhance.Process(input);
            enhanced = _f2Enhance.Process(enhanced);
            enhanced = _f3Enhance.Process(enhanced);
            buffer[i] = input * (1f - _amount) + enhanced * _amount;
        }
    }

    public void SetParameter(int index, float value)
    {
        switch (index)
        {
            case AmountIndex:
                _amount = Math.Clamp(value, 0f, 1f);
                break;
            case BoostIndex:
                _boostDb = Math.Clamp(value, 0f, 4f);
                UpdateEnhancers(_boostDb);
                break;
            case SmoothingIndex:
                _smoothingMs = Math.Clamp(value, 40f, 200f);
                UpdateSmoothing();
                break;
        }
    }

    public byte[] GetState()
    {
        var bytes = new byte[sizeof(float) * 3];
        Buffer.BlockCopy(BitConverter.GetBytes(_amount), 0, bytes, 0, 4);
        Buffer.BlockCopy(BitConverter.GetBytes(_boostDb), 0, bytes, 4, 4);
        Buffer.BlockCopy(BitConverter.GetBytes(_smoothingMs), 0, bytes, 8, 4);
        return bytes;
    }

    public void SetState(byte[] state)
    {
        if (state.Length < sizeof(float) * 2)
        {
            return;
        }

        _amount = BitConverter.ToSingle(state, 0);
        _boostDb = BitConverter.ToSingle(state, 4);
        if (state.Length >= sizeof(float) * 3)
        {
            _smoothingMs = BitConverter.ToSingle(state, 8);
        }

        UpdateSmoothing();
        UpdateEnhancers(_boostDb);
    }

    public void Dispose()
    {
    }

    private void ConfigureCandidateFilters()
    {
        if (_sampleRate <= 0)
        {
            return;
        }

        for (int i = 0; i < F1Candidates.Length; i++)
        {
            _candidateFilters[F1Offset + i].SetBandPass(_sampleRate, F1Candidates[i], 3f);
            _candidateEnvs[F1Offset + i].Configure(6f, 80f, _sampleRate);
        }

        for (int i = 0; i < F2Candidates.Length; i++)
        {
            _candidateFilters[F2Offset + i].SetBandPass(_sampleRate, F2Candidates[i], 3f);
            _candidateEnvs[F2Offset + i].Configure(6f, 80f, _sampleRate);
        }

        for (int i = 0; i < F3Candidates.Length; i++)
        {
            _candidateFilters[F3Offset + i].SetBandPass(_sampleRate, F3Candidates[i], 3f);
            _candidateEnvs[F3Offset + i].Configure(6f, 80f, _sampleRate);
        }
    }

    private void UpdateEnhancers(float boostDb)
    {
        if (_sampleRate <= 0)
        {
            return;
        }

        _f1Enhance.SetPeaking(_sampleRate, _currentF1, boostDb, 2f);
        _f2Enhance.SetPeaking(_sampleRate, _currentF2, boostDb, 2f);
        _f3Enhance.SetPeaking(_sampleRate, _currentF3, boostDb, 2f);
    }

    private void UpdateTargets(float speechGate)
    {
        float targetF1 = SelectCandidate(F1Offset, F1Candidates.Length, F1Candidates);
        float targetF2 = SelectCandidate(F2Offset, F2Candidates.Length, F2Candidates);
        float targetF3 = SelectCandidate(F3Offset, F3Candidates.Length, F3Candidates);

        _currentF1 += _smoothingCoeff * (targetF1 - _currentF1);
        _currentF2 += _smoothingCoeff * (targetF2 - _currentF2);
        _currentF3 += _smoothingCoeff * (targetF3 - _currentF3);

        float gatedBoost = _boostDb * speechGate;
        UpdateEnhancers(gatedBoost);
    }

    private float SelectCandidate(int offset, int count, float[] candidates)
    {
        float best = candidates[0];
        float bestValue = _candidateSums[offset];
        for (int i = 1; i < count; i++)
        {
            float value = _candidateSums[offset + i];
            if (value > bestValue)
            {
                bestValue = value;
                best = candidates[i];
            }
        }

        return best;
    }

    private void UpdateSmoothing()
    {
        if (_sampleRate <= 0)
        {
            return;
        }

        float perSample = DspUtils.TimeToCoefficient(_smoothingMs, _sampleRate);
        float blockSamples = _blockSize > 0 ? _blockSize : 256f;
        _smoothingCoeff = 1f - MathF.Pow(1f - perSample, blockSamples);
    }
}
