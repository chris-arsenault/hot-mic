using System.Threading;
using HotMic.Core.Dsp.Fft;

namespace HotMic.Core.Plugins.BuiltIn;

public sealed class SpectralContrastPlugin : IPlugin, IAnalysisSignalConsumer, IPluginStatusProvider
{
    public const int StrengthIndex = 0;
    public const int MixIndex = 1;
    public const int GateStrengthIndex = 2;
    public const int ScaleIndex = 3;

    private const int FftSize = 512;
    private const int HopSize = 128;

    private float _strength = 0.3f;
    private float _mix = 1f;
    private float _gateStrength = 1f;
    private int _strengthScaleIndex;

    private float[] _inputRing = Array.Empty<float>();
    private float[] _outputRing = Array.Empty<float>();
    private float[] _window = Array.Empty<float>();
    private float[] _synthesisWindow = Array.Empty<float>();
    private float[] _fftReal = Array.Empty<float>();
    private float[] _fftImag = Array.Empty<float>();
    private float[] _magnitudes = Array.Empty<float>();
    private float[] _dryDelay = Array.Empty<float>();
    private int _dryIndex;

    private FastFft? _fft;
    private int _inputIndex;
    private int _hopCounter;
    private int _sampleRate;
    private string _statusMessage = string.Empty;

    // Metering - store spectrum for UI
    private float[] _meterMagnitudes = Array.Empty<float>();
    private float _meterSpeechGate;
    private float _meterContrastStrength;

    private const string MissingSidechainMessage = "Missing analysis data.";

    public SpectralContrastPlugin()
    {
        Parameters =
        [
            new PluginParameter { Index = StrengthIndex, Name = "Strength", MinValue = 0f, MaxValue = 100f, DefaultValue = 30f, Unit = "%" },
            new PluginParameter
            {
                Index = ScaleIndex,
                Name = "Scale",
                MinValue = 0f,
                MaxValue = 3f,
                DefaultValue = 0f,
                Unit = string.Empty,
                FormatValue = EnhanceAmountScale.FormatLabel
            },
            new PluginParameter { Index = MixIndex, Name = "Mix", MinValue = 0f, MaxValue = 100f, DefaultValue = 100f, Unit = "%" },
            new PluginParameter { Index = GateStrengthIndex, Name = "Gate Strength", MinValue = 0f, MaxValue = 1f, DefaultValue = 1f, Unit = string.Empty }
        ];
    }

    public string Id => "builtin:spectral-contrast";

    public string Name => "Spectral Contrast";

    public bool IsBypassed { get; set; }

    public int LatencySamples => Math.Max(0, FftSize - HopSize);

    public IReadOnlyList<PluginParameter> Parameters { get; }

    public AnalysisSignalMask RequiredSignals => AnalysisSignalMask.SpeechPresence;

    public string StatusMessage => Volatile.Read(ref _statusMessage);

    public float StrengthPct => _strength * 100f;
    public float MixPct => _mix * 100f;
    public float GateStrength => _gateStrength;
    public int StrengthScaleIndex => _strengthScaleIndex;
    public int SampleRate => _sampleRate;

    public void SetAnalysisSignalsAvailable(bool available)
    {
        Volatile.Write(ref _statusMessage, available ? string.Empty : MissingSidechainMessage);
    }

    public void Initialize(int sampleRate, int blockSize)
    {
        _sampleRate = sampleRate;
        _fft = new FastFft(FftSize);
        _inputRing = new float[FftSize];
        _outputRing = new float[FftSize];
        _window = new float[FftSize];
        _synthesisWindow = new float[FftSize];
        _fftReal = new float[FftSize];
        _fftImag = new float[FftSize];
        _magnitudes = new float[FftSize / 2];
        _meterMagnitudes = new float[FftSize / 2];
        int latency = LatencySamples;
        _dryDelay = latency > 0 ? new float[latency] : Array.Empty<float>();
        _dryIndex = 0;

        for (int i = 0; i < FftSize; i++)
        {
            float w = 0.5f - 0.5f * MathF.Cos(2f * MathF.PI * i / (FftSize - 1));
            _window[i] = w;
            _synthesisWindow[i] = w;
        }

        _inputIndex = 0;
        _hopCounter = 0;
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
        float mix = _mix;

        for (int i = 0; i < buffer.Length; i++)
        {
            float input = buffer[i];
            float wet = _outputRing[_inputIndex];
            _outputRing[_inputIndex] = 0f;

            float dry = GetDrySample(input);
            buffer[i] = dry * (1f - mix) + wet * mix;

            _inputRing[_inputIndex] = input;
            _inputIndex++;
            if (_inputIndex >= FftSize)
            {
                _inputIndex = 0;
            }

            _hopCounter++;
            if (_hopCounter >= HopSize)
            {
                _hopCounter = 0;
                long frameTime = baseTime + i - (FftSize / 2);
                ProcessFrame(true, speechSource, frameTime);
            }
        }
    }

    public void Process(Span<float> buffer)
    {
        if (IsBypassed || buffer.IsEmpty)
        {
            return;
        }

        float mix = _mix;
        for (int i = 0; i < buffer.Length; i++)
        {
            float input = buffer[i];
            float wet = _outputRing[_inputIndex];
            _outputRing[_inputIndex] = 0f;
            float dry = GetDrySample(input);
            buffer[i] = dry * (1f - mix) + wet * mix;

            _inputRing[_inputIndex] = input;
            _inputIndex++;
            if (_inputIndex >= FftSize)
            {
                _inputIndex = 0;
            }

            _hopCounter++;
            if (_hopCounter >= HopSize)
            {
                _hopCounter = 0;
                ProcessFrame(false, default, 0);
            }
        }
    }

    public void SetParameter(int index, float value)
    {
        switch (index)
        {
            case StrengthIndex:
                _strength = Math.Clamp(value / 100f, 0f, 1f);
                break;
            case MixIndex:
                _mix = Math.Clamp(value / 100f, 0f, 1f);
                break;
            case GateStrengthIndex:
                _gateStrength = Math.Clamp(value, 0f, 1f);
                break;
            case ScaleIndex:
                _strengthScaleIndex = EnhanceAmountScale.ClampIndex(value);
                break;
        }
    }

    public byte[] GetState()
    {
        var bytes = new byte[sizeof(float) * 4];
        Buffer.BlockCopy(BitConverter.GetBytes(_strength), 0, bytes, 0, 4);
        Buffer.BlockCopy(BitConverter.GetBytes(_mix), 0, bytes, 4, 4);
        Buffer.BlockCopy(BitConverter.GetBytes(_gateStrength), 0, bytes, 8, 4);
        Buffer.BlockCopy(BitConverter.GetBytes((float)_strengthScaleIndex), 0, bytes, 12, 4);
        return bytes;
    }

    public void SetState(byte[] state)
    {
        if (state.Length < sizeof(float) * 2)
        {
            return;
        }

        _strength = BitConverter.ToSingle(state, 0);
        _mix = BitConverter.ToSingle(state, 4);
        if (state.Length >= sizeof(float) * 3)
        {
            _gateStrength = BitConverter.ToSingle(state, 8);
        }
        if (state.Length >= sizeof(float) * 4)
        {
            _strengthScaleIndex = EnhanceAmountScale.ClampIndex(BitConverter.ToSingle(state, 12));
        }
    }

    public void Dispose()
    {
    }

    private void ProcessFrame(bool useSidechain, in AnalysisSignalSource speechSource, long frameTime)
    {
        if (_fft is null)
        {
            return;
        }

        int start = _inputIndex;
        for (int i = 0; i < FftSize; i++)
        {
            int index = start + i;
            if (index >= FftSize)
            {
                index -= FftSize;
            }
            float sample = _inputRing[index] * _window[i];
            _fftReal[i] = sample;
            _fftImag[i] = 0f;
        }

        _fft.Forward(_fftReal, _fftImag);

        int bins = _magnitudes.Length;
        for (int i = 0; i < bins; i++)
        {
            float re = _fftReal[i];
            float im = _fftImag[i];
            _magnitudes[i] = MathF.Sqrt(re * re + im * im) + 1e-12f;
        }

        float gate = 1f;
        if (useSidechain)
        {
            float speech = speechSource.ReadSample(frameTime);
            gate = 1f - _gateStrength + _gateStrength * speech;
            _meterSpeechGate = speech;
        }

        float strength = _strength * EnhanceAmountScale.FromIndex(_strengthScaleIndex) * gate;
        _meterContrastStrength = strength;

        // Copy magnitudes for UI display
        Array.Copy(_magnitudes, _meterMagnitudes, _magnitudes.Length);

        for (int i = 1; i < bins - 1; i++)
        {
            float mag = _magnitudes[i];
            float neighbor = (_magnitudes[i - 1] + _magnitudes[i] + _magnitudes[i + 1]) * 0.333f;
            float contrast = (mag - neighbor) / MathF.Max(neighbor, 1e-6f);
            float gain = 1f + strength * contrast;
            gain = Math.Clamp(gain, 0.5f, 2f);
            _fftReal[i] *= gain;
            _fftImag[i] *= gain;
        }

        for (int i = 1; i < bins - 1; i++)
        {
            int mirror = FftSize - i;
            _fftReal[mirror] = _fftReal[i];
            _fftImag[mirror] = -_fftImag[i];
        }

        _fft.Inverse(_fftReal, _fftImag);

        for (int i = 0; i < FftSize; i++)
        {
            int index = start + i;
            if (index >= FftSize)
            {
                index -= FftSize;
            }
            _outputRing[index] += _fftReal[i] * _synthesisWindow[i] / FftSize;
        }
    }

    private float GetDrySample(float input)
    {
        if (_dryDelay.Length == 0)
        {
            return input;
        }

        float dry = _dryDelay[_dryIndex];
        _dryDelay[_dryIndex] = input;
        _dryIndex++;
        if (_dryIndex >= _dryDelay.Length)
        {
            _dryIndex = 0;
        }
        return dry;
    }

    /// <summary>Gets the current speech gate level (0-1).</summary>
    public float GetSpeechGate() => Volatile.Read(ref _meterSpeechGate);

    /// <summary>Gets the current contrast strength being applied.</summary>
    public float GetContrastStrength() => Volatile.Read(ref _meterContrastStrength);

    /// <summary>Gets a copy of the current magnitude spectrum for display.</summary>
    public void GetMagnitudeSpectrum(Span<float> dest)
    {
        var source = _meterMagnitudes.AsSpan();
        int count = Math.Min(source.Length, dest.Length);
        source.Slice(0, count).CopyTo(dest);
    }

    /// <summary>Gets the number of magnitude bins available.</summary>
    public int MagnitudeBinCount => _meterMagnitudes.Length;
}
