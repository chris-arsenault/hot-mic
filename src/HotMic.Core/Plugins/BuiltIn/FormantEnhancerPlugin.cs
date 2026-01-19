using System.Threading;
using HotMic.Core.Dsp;
using HotMic.Core.Dsp.Filters;

namespace HotMic.Core.Plugins.BuiltIn;

public sealed class FormantEnhancerPlugin : IPlugin, IAnalysisSignalConsumer, IPluginStatusProvider
{
    public const int AmountIndex = 0;
    public const int BoostIndex = 1;
    public const int SmoothingIndex = 2;

    private const float MinFormantConfidence = 0.2f;
    private const float MinF1Hz = 150f;
    private const float MaxF1Hz = 1100f;
    private const float MinF2Hz = 500f;
    private const float MaxF2Hz = 3500f;
    private const float MinF3Hz = 1500f;
    private const float MaxF3Hz = 5000f;

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

    private const string MissingSidechainMessage = "Missing analysis data.";

    private readonly BiquadFilter _f1Enhance = new();
    private readonly BiquadFilter _f2Enhance = new();
    private readonly BiquadFilter _f3Enhance = new();


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

    }

    public string Id => "builtin:formant-enhance";

    public string Name => "Formant Enhancer";

    public bool IsBypassed { get; set; }

    public int LatencySamples => 0;

    public IReadOnlyList<PluginParameter> Parameters { get; }

    public AnalysisSignalMask RequiredSignals =>
        AnalysisSignalMask.SpeechPresence |
        AnalysisSignalMask.FormantF1Hz |
        AnalysisSignalMask.FormantF2Hz |
        AnalysisSignalMask.FormantF3Hz |
        AnalysisSignalMask.FormantConfidence;

    public string StatusMessage => Volatile.Read(ref _statusMessage);

    public float Amount => _amount;
    public float BoostDb => _boostDb;
    public float SmoothingMs => _smoothingMs;
    public int SampleRate => _sampleRate;

    public void SetAnalysisSignalsAvailable(bool available)
    {
        Volatile.Write(ref _statusMessage, available ? string.Empty : MissingSidechainMessage);
    }

    public void Initialize(int sampleRate, int blockSize)
    {
        _sampleRate = sampleRate;
        _blockSize = blockSize;
        UpdateSmoothing();
        UpdateEnhancers(_boostDb);
    }

    public void Process(Span<float> buffer, in PluginProcessContext context)
    {
        if (IsBypassed || buffer.IsEmpty)
        {
            return;
        }

        if (!context.TryGetAnalysisSignalSource(AnalysisSignalId.SpeechPresence, out var speechSource) ||
            !context.TryGetAnalysisSignalSource(AnalysisSignalId.FormantF1Hz, out var f1Source) ||
            !context.TryGetAnalysisSignalSource(AnalysisSignalId.FormantF2Hz, out var f2Source) ||
            !context.TryGetAnalysisSignalSource(AnalysisSignalId.FormantF3Hz, out var f3Source) ||
            !context.TryGetAnalysisSignalSource(AnalysisSignalId.FormantConfidence, out var confidenceSource))
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

            speechSum += speechSource.ReadSample(baseTime + i);
        }

        float speechAvg = Math.Clamp(speechSum / buffer.Length, 0f, 1f);
        int lastIndex = buffer.Length - 1;
        long lastTime = baseTime + lastIndex;
        float confidence = confidenceSource.ReadSample(lastTime);
        float f1 = f1Source.ReadSample(lastTime);
        float f2 = f2Source.ReadSample(lastTime);
        float f3 = f3Source.ReadSample(lastTime);

        UpdateTargets(f1, f2, f3, confidence);
        float confidenceGain = Math.Clamp(confidence, 0f, 1f) * speechAvg;
        UpdateEnhancers(_boostDb * confidenceGain);

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

    private void UpdateTargets(float f1Hz, float f2Hz, float f3Hz, float confidence)
    {
        if (confidence < MinFormantConfidence)
        {
            return;
        }

        if (f1Hz > 0f)
        {
            float targetF1 = Math.Clamp(f1Hz, MinF1Hz, MaxF1Hz);
            _currentF1 += _smoothingCoeff * (targetF1 - _currentF1);
        }

        if (f2Hz > 0f)
        {
            float targetF2 = Math.Clamp(f2Hz, MinF2Hz, MaxF2Hz);
            _currentF2 += _smoothingCoeff * (targetF2 - _currentF2);
        }

        if (f3Hz > 0f)
        {
            float targetF3 = Math.Clamp(f3Hz, MinF3Hz, MaxF3Hz);
            _currentF3 += _smoothingCoeff * (targetF3 - _currentF3);
        }
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
