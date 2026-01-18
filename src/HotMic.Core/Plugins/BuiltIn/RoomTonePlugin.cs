using System.Threading;

namespace HotMic.Core.Plugins.BuiltIn;

public sealed class RoomTonePlugin : IPlugin, IAnalysisSignalConsumer, IPluginStatusProvider
{
    public const int LevelIndex = 0;
    public const int DuckIndex = 1;
    public const int ToneIndex = 2;

    private float _levelDb = -45f;
    private float _duckStrength = 0.8f;
    private float _toneHz = 8000f;

    private float _levelLinear = 0.0056f;
    private float _duckCoeff;
    private float _currentLevel;
    private uint _noiseSeed = 0x12345678u;
    private int _sampleRate;
    private string _statusMessage = string.Empty;

    private const string MissingSidechainMessage = "Missing analysis data.";

    private readonly BiquadFilter _highPass = new();
    private readonly BiquadFilter _lowPass = new();

    // Metering
    private float _meterSpeechPresence;
    private float _meterDuckAmount;
    private float _meterNoiseLevel;

    public RoomTonePlugin()
    {
        Parameters =
        [
            new PluginParameter { Index = LevelIndex, Name = "Level", MinValue = -60f, MaxValue = -20f, DefaultValue = -45f, Unit = "dB" },
            new PluginParameter { Index = DuckIndex, Name = "Duck", MinValue = 0f, MaxValue = 1f, DefaultValue = 0.8f, Unit = string.Empty },
            new PluginParameter { Index = ToneIndex, Name = "Tone", MinValue = 3000f, MaxValue = 12000f, DefaultValue = 8000f, Unit = "Hz" }
        ];
    }

    public string Id => "builtin:room-tone";

    public string Name => "Room Tone";

    public bool IsBypassed { get; set; }

    public int LatencySamples => 0;

    public IReadOnlyList<PluginParameter> Parameters { get; }

    public AnalysisSignalMask RequiredSignals => AnalysisSignalMask.SpeechPresence;

    public string StatusMessage => Volatile.Read(ref _statusMessage);

    public float LevelDb => _levelDb;
    public float DuckStrength => _duckStrength;
    public float ToneHz => _toneHz;
    public int SampleRate => _sampleRate;

    public void SetAnalysisSignalsAvailable(bool available)
    {
        Volatile.Write(ref _statusMessage, available ? string.Empty : MissingSidechainMessage);
    }

    public void Initialize(int sampleRate, int blockSize)
    {
        _sampleRate = sampleRate;
        UpdateFilters();
        UpdateLevel();
        _duckCoeff = DspUtils.TimeToCoefficient(80f, sampleRate);
        _currentLevel = _levelLinear;
    }

    public void Process(Span<float> buffer, in PluginProcessContext context)
    {
        if (IsBypassed || buffer.IsEmpty)
        {
            return;
        }

        if (!context.TryGetAnalysisSignalSource(AnalysisSignalId.SpeechPresence, out var speechSource))
        {
            return;
        }

        long baseTime = context.SampleTime;

        for (int i = 0; i < buffer.Length; i++)
        {
            float speech = speechSource.ReadSample(baseTime + i);

            float target = _levelLinear * (1f - _duckStrength * speech);
            _currentLevel += _duckCoeff * (target - _currentLevel);

            float noise = NextNoise();
            float shaped = _highPass.Process(noise);
            shaped = _lowPass.Process(shaped);

            buffer[i] += shaped * _currentLevel;

            // Update metering
            _meterSpeechPresence = speech;
            _meterDuckAmount = 1f - (_currentLevel / _levelLinear);
            _meterNoiseLevel = _currentLevel;
        }
    }

    /// <summary>Gets the current speech presence level (0-1).</summary>
    public float GetSpeechPresence() => Volatile.Read(ref _meterSpeechPresence);

    /// <summary>Gets the current duck amount (0-1, higher = more ducked).</summary>
    public float GetDuckAmount() => Volatile.Read(ref _meterDuckAmount);

    /// <summary>Gets the current noise output level.</summary>
    public float GetNoiseLevel() => Volatile.Read(ref _meterNoiseLevel);

    public void Process(Span<float> buffer)
    {
        if (IsBypassed || buffer.IsEmpty)
        {
            return;
        }

        for (int i = 0; i < buffer.Length; i++)
        {
            float noise = NextNoise();
            float shaped = _highPass.Process(noise);
            shaped = _lowPass.Process(shaped);
            buffer[i] += shaped * _currentLevel;
        }
    }

    public void SetParameter(int index, float value)
    {
        switch (index)
        {
            case LevelIndex:
                _levelDb = Math.Clamp(value, -60f, -20f);
                UpdateLevel();
                break;
            case DuckIndex:
                _duckStrength = Math.Clamp(value, 0f, 1f);
                break;
            case ToneIndex:
                _toneHz = Math.Clamp(value, 3000f, 12000f);
                UpdateFilters();
                break;
        }
    }

    public byte[] GetState()
    {
        var bytes = new byte[sizeof(float) * 3];
        Buffer.BlockCopy(BitConverter.GetBytes(_levelDb), 0, bytes, 0, 4);
        Buffer.BlockCopy(BitConverter.GetBytes(_duckStrength), 0, bytes, 4, 4);
        Buffer.BlockCopy(BitConverter.GetBytes(_toneHz), 0, bytes, 8, 4);
        return bytes;
    }

    public void SetState(byte[] state)
    {
        if (state.Length < sizeof(float) * 2)
        {
            return;
        }

        _levelDb = BitConverter.ToSingle(state, 0);
        _duckStrength = BitConverter.ToSingle(state, 4);
        if (state.Length >= sizeof(float) * 3)
        {
            _toneHz = BitConverter.ToSingle(state, 8);
        }

        UpdateFilters();
        UpdateLevel();
    }

    public void Dispose()
    {
    }

    private void UpdateFilters()
    {
        if (_sampleRate <= 0)
        {
            return;
        }

        _highPass.SetHighPass(_sampleRate, 80f, 0.707f);
        _lowPass.SetLowPass(_sampleRate, _toneHz, 0.707f);
    }

    private void UpdateLevel()
    {
        _levelLinear = DspUtils.DbToLinear(_levelDb);
    }

    private float NextNoise()
    {
        _noiseSeed = _noiseSeed * 1664525u + 1013904223u;
        uint bits = _noiseSeed >> 9;
        return (bits / 8388608f) * 2f - 1f;
    }
}
