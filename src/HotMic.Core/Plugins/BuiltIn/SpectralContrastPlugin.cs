using System.Threading;
using HotMic.Core.Dsp;
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
    private float[] _outputWeightRing = Array.Empty<float>();
    private float[] _window = Array.Empty<float>();
    private float[] _fftReal = Array.Empty<float>();
    private float[] _fftImag = Array.Empty<float>();
    private float[] _magnitudes = Array.Empty<float>();
    private float[] _logMagnitudes = Array.Empty<float>();
    private float[] _gainDbSmoothed = Array.Empty<float>();
    private float[] _dryDelay = Array.Empty<float>();
    private int _dryIndex;
    private int[][] _contrastIndices = Array.Empty<int[]>();
    private float[][] _contrastWeights = Array.Empty<float[]>();
    private float[] _erbRates = Array.Empty<float>();

    private FastFft? _fft;
    private int _inputIndex;
    private int _hopCounter;
    private int _sampleRate;
    private string _statusMessage = string.Empty;

    // Metering - store spectrum for UI
    private float[] _meterMagnitudes = Array.Empty<float>();
    private float _meterSpeechGate;
    private float _meterGateApplied;
    private float _meterContrastStrength;
    private float _meterContrastMeanAbs;
    private float _meterContrastPeakAbs;
    private float _meterGainMean;
    private float _meterGainPeak;

    private const string MissingSidechainMessage = "Missing analysis data.";
    private const float MaxContrastGainDb = 6f;
    private const float ContrastAttackMs = 12f;
    private const float ContrastReleaseMs = 80f;
    private float _contrastAttackCoeff;
    private float _contrastReleaseCoeff;

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

    public int LatencySamples => FftSize;

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
        _outputWeightRing = new float[FftSize];
        _window = new float[FftSize];
        _fftReal = new float[FftSize];
        _fftImag = new float[FftSize];
        _magnitudes = new float[FftSize / 2];
        _logMagnitudes = new float[FftSize / 2];
        _meterMagnitudes = new float[FftSize / 2];
        _gainDbSmoothed = new float[FftSize / 2];
        int latency = LatencySamples;
        _dryDelay = latency > 0 ? new float[latency] : Array.Empty<float>();
        _dryIndex = 0;

        for (int i = 0; i < FftSize; i++)
        {
            float w = 0.5f - 0.5f * MathF.Cos(2f * MathF.PI * i / (FftSize - 1));
            _window[i] = w;
        }

        BuildContrastKernel();
        UpdateContrastSmoothing();

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
            float weight = _outputWeightRing[_inputIndex];
            float wet = weight > 1e-6f ? _outputRing[_inputIndex] / weight : 0f;
            _outputRing[_inputIndex] = 0f;
            _outputWeightRing[_inputIndex] = 0f;

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
            float weight = _outputWeightRing[_inputIndex];
            float wet = weight > 1e-6f ? _outputRing[_inputIndex] / weight : 0f;
            _outputRing[_inputIndex] = 0f;
            _outputWeightRing[_inputIndex] = 0f;
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
            _logMagnitudes[i] = DspUtils.LinearToDb(_magnitudes[i]);
        }

        float gate = 1f;
        if (useSidechain)
        {
            float speech = Math.Clamp(speechSource.ReadSample(frameTime), 0f, 1f);
            gate = speech * _gateStrength;
            _meterSpeechGate = speech;
        }

        float strength = _strength * gate;
        strength *= EnhanceAmountScale.FromIndex(_strengthScaleIndex);
        _meterContrastStrength = strength;
        _meterGateApplied = gate;

        // Copy magnitudes for UI display
        Array.Copy(_magnitudes, _meterMagnitudes, _magnitudes.Length);

        if (strength <= 1e-6f)
        {
            Array.Clear(_gainDbSmoothed);
            _meterContrastMeanAbs = 0f;
            _meterContrastPeakAbs = 0f;
            _meterGainMean = 1f;
            _meterGainPeak = 1f;
        }
        else
        {
            float contrastAbsSum = 0f;
            float contrastPeak = 0f;
            float gainSum = 0f;
            float gainPeak = 0f;
            int contrastCount = 0;
            float attackCoeff = _contrastAttackCoeff;
            float releaseCoeff = _contrastReleaseCoeff;

            for (int i = 1; i < bins - 1; i++)
            {
                float dog = 0f;
                float[] weights = _contrastWeights[i];
                int[] indices = _contrastIndices[i];
                for (int k = 0; k < weights.Length; k++)
                {
                    dog += weights[k] * _logMagnitudes[indices[k]];
                }

                float contrastDb = dog;
                float targetDb = Math.Clamp(strength * contrastDb, -MaxContrastGainDb, MaxContrastGainDb);
                float currentDb = _gainDbSmoothed[i];
                float coeff = targetDb > currentDb ? attackCoeff : releaseCoeff;
                currentDb += coeff * (targetDb - currentDb);
                _gainDbSmoothed[i] = currentDb;
                float gain = DspUtils.DbToLinear(currentDb);
                _fftReal[i] *= gain;
                _fftImag[i] *= gain;

                float absContrast = MathF.Abs(contrastDb);
                contrastAbsSum += absContrast;
                if (absContrast > contrastPeak)
                {
                    contrastPeak = absContrast;
                }

                gainSum += gain;
                if (gain > gainPeak)
                {
                    gainPeak = gain;
                }
                contrastCount++;
            }

            if (contrastCount > 0)
            {
                _meterContrastMeanAbs = contrastAbsSum / contrastCount;
                _meterGainMean = gainSum / contrastCount;
            }
            else
            {
                _meterContrastMeanAbs = 0f;
                _meterGainMean = 1f;
            }
            _meterContrastPeakAbs = contrastPeak;
            _meterGainPeak = gainPeak;
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
            float w = _window[i];
            _outputRing[index] += _fftReal[i] * w;
            _outputWeightRing[index] += w * w;
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

    /// <summary>Gets the applied gate (speech * gateStrength).</summary>
    public float GetAppliedGate() => Volatile.Read(ref _meterGateApplied);

    /// <summary>Gets the current contrast strength being applied.</summary>
    public float GetContrastStrength() => Volatile.Read(ref _meterContrastStrength);

    /// <summary>Gets the mean absolute contrast value from the last frame.</summary>
    public float GetContrastMeanAbs() => Volatile.Read(ref _meterContrastMeanAbs);

    /// <summary>Gets the peak absolute contrast value from the last frame.</summary>
    public float GetContrastPeakAbs() => Volatile.Read(ref _meterContrastPeakAbs);

    /// <summary>Gets the mean gain applied across bins.</summary>
    public float GetGainMean() => Volatile.Read(ref _meterGainMean);

    /// <summary>Gets the peak gain applied across bins.</summary>
    public float GetGainPeak() => Volatile.Read(ref _meterGainPeak);

    /// <summary>Gets a copy of the current magnitude spectrum for display.</summary>
    public void GetMagnitudeSpectrum(Span<float> dest)
    {
        var source = _meterMagnitudes.AsSpan();
        int count = Math.Min(source.Length, dest.Length);
        source.Slice(0, count).CopyTo(dest);
    }

    /// <summary>Gets the number of magnitude bins available.</summary>
    public int MagnitudeBinCount => _meterMagnitudes.Length;

    private void BuildContrastKernel()
    {
        int bins = _magnitudes.Length;
        _erbRates = new float[bins];
        float binHz = _sampleRate > 0 ? _sampleRate / (float)FftSize : 1f;
        for (int i = 0; i < bins; i++)
        {
            float freq = i * binHz;
            _erbRates[i] = 21.4f * MathF.Log10(1f + 0.00437f * freq);
        }

        const float centerSigma = 0.5f;
        const float surroundSigma = 1.5f;
        float radius = surroundSigma * 3f;

        _contrastIndices = new int[bins][];
        _contrastWeights = new float[bins][];

        for (int i = 0; i < bins; i++)
        {
            float centerErb = _erbRates[i];
            int start = i;
            while (start > 0 && centerErb - _erbRates[start - 1] <= radius)
            {
                start--;
            }
            int end = i;
            while (end < bins - 1 && _erbRates[end + 1] - centerErb <= radius)
            {
                end++;
            }

            int count = end - start + 1;
            var indices = new int[count];
            var weights = new float[count];

            float centerSum = 0f;
            float surroundSum = 0f;
            for (int j = start; j <= end; j++)
            {
                float d = _erbRates[j] - centerErb;
                float center = MathF.Exp(-0.5f * (d / centerSigma) * (d / centerSigma));
                float surround = MathF.Exp(-0.5f * (d / surroundSigma) * (d / surroundSigma));
                centerSum += center;
                surroundSum += surround;
            }

            float k = surroundSum > 1e-6f ? centerSum / surroundSum : 1f;

            int idx = 0;
            for (int j = start; j <= end; j++)
            {
                float d = _erbRates[j] - centerErb;
                float center = MathF.Exp(-0.5f * (d / centerSigma) * (d / centerSigma));
                float surround = MathF.Exp(-0.5f * (d / surroundSigma) * (d / surroundSigma));
                weights[idx] = center - k * surround;
                indices[idx] = j;
                idx++;
            }

            float absSum = 0f;
            for (int j = 0; j < weights.Length; j++)
            {
                absSum += MathF.Abs(weights[j]);
            }
            if (absSum > 1e-6f)
            {
                float inv = 1f / absSum;
                for (int j = 0; j < weights.Length; j++)
                {
                    weights[j] *= inv;
                }
            }

            _contrastIndices[i] = indices;
            _contrastWeights[i] = weights;
        }
    }

    private void UpdateContrastSmoothing()
    {
        if (_sampleRate <= 0)
        {
            _contrastAttackCoeff = 1f;
            _contrastReleaseCoeff = 1f;
            return;
        }

        float framesPerSecond = _sampleRate / (float)HopSize;
        _contrastAttackCoeff = TimeToFrameCoefficient(ContrastAttackMs, framesPerSecond);
        _contrastReleaseCoeff = TimeToFrameCoefficient(ContrastReleaseMs, framesPerSecond);
    }

    private static float TimeToFrameCoefficient(float timeMs, float framesPerSecond)
    {
        float timeSeconds = MathF.Max(0.0001f, timeMs * 0.001f);
        return 1f - MathF.Exp(-1f / (timeSeconds * framesPerSecond));
    }
}
