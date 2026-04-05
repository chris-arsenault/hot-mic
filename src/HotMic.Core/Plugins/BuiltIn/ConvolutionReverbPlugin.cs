using System.Threading;
using HotMic.Common.Configuration;
using HotMic.Core.Dsp;
using NAudio.Wave;

namespace HotMic.Core.Plugins.BuiltIn;

/// <summary>
/// Convolution reverb plugin using uniform partitioned convolution.
/// Supports loading custom impulse response files or using built-in presets.
/// </summary>
public sealed class ConvolutionReverbPlugin : IPlugin, IQualityConfigurablePlugin, IPluginStatusProvider
{
    public const int DryWetIndex = 0;
    public const int DecayIndex = 1;
    public const int PreDelayIndex = 2;
    public const int IrPresetIndex = 3;

    private int _fftSize = 2048;
    private int _fftHalfSize = 1024;
    private int _overlapSize = 1024;

    private float[] _inputBuffer = Array.Empty<float>();
    private float[] _outputBuffer = Array.Empty<float>();
    private float[] _overlapBuffer = Array.Empty<float>();
    private FastFft? _fft;
    private float[] _fftReal = Array.Empty<float>();
    private float[] _fftImag = Array.Empty<float>();
    private float[] _convReal = Array.Empty<float>();
    private float[] _convImag = Array.Empty<float>();
    private float[] _irSamples = Array.Empty<float>();

    // Partitioned convolution state. Each IR partition stores an FFT of fftHalfSize samples.
    private float[][] _irPartitionReal = Array.Empty<float[]>();
    private float[][] _irPartitionImag = Array.Empty<float[]>();
    private float[][] _inputHistoryReal = Array.Empty<float[]>();
    private float[][] _inputHistoryImag = Array.Empty<float[]>();
    private int _historyIndex;

    private float[] _preDelayBuffer = Array.Empty<float>();
    private int _preDelayWritePos;
    private int _preDelayReadPos;
    private int _preDelaySamples;

    private DelayLine? _dryDelayLine;
    private float[] _dryAlignedBuffer = Array.Empty<float>();

    private int _inputPos;
    private int _outputPos;
    private int _outputAvailable;

    private float _dryWet = 0.3f;
    private float _decay = 1.0f;
    private float _preDelayMs;
    private int _irPreset;

    private int _sampleRate;
    private int _blockSize;
    private bool _irLoaded;
    private string _statusMessage = "No IR loaded";
    private string? _loadedIrPath;

    private int _inputLevelBits;
    private int _outputLevelBits;

    private static readonly string[] IrPresetNames =
    [
        "None",
        "Small Room",
        "Medium Hall",
        "Large Hall",
        "Plate",
        "Custom..."
    ];

    public ConvolutionReverbPlugin()
    {
        Parameters =
        [
            new PluginParameter { Index = DryWetIndex, Name = "Dry/Wet", MinValue = 0f, MaxValue = 1f, DefaultValue = 0.3f, Unit = "%" },
            new PluginParameter { Index = DecayIndex, Name = "Decay", MinValue = 0.1f, MaxValue = 2f, DefaultValue = 1f, Unit = "" },
            new PluginParameter { Index = PreDelayIndex, Name = "Pre-Delay", MinValue = 0f, MaxValue = 100f, DefaultValue = 0f, Unit = "ms" },
            new PluginParameter { Index = IrPresetIndex, Name = "IR Preset", MinValue = 0f, MaxValue = 5f, DefaultValue = 0f, Unit = "" }
        ];
    }

    public string Id => "builtin:reverb";
    public string Name => "Reverb";
    public bool IsBypassed { get; set; }
    public int LatencySamples => _fftHalfSize + _preDelaySamples;
    public IReadOnlyList<PluginParameter> Parameters { get; }
    public string StatusMessage => _statusMessage;

    public float DryWet => _dryWet;
    public float Decay => _decay;
    public float PreDelayMs => _preDelayMs;
    public int IrPreset => _irPreset;
    public bool IsIrLoaded => _irLoaded;
    public string? LoadedIrPath => _loadedIrPath;
    public static IReadOnlyList<string> PresetNames => IrPresetNames;

    public void Initialize(int sampleRate, int blockSize)
    {
        _sampleRate = sampleRate;
        _blockSize = blockSize;

        _fftSize = sampleRate >= 88200 ? 4096 : 2048;
        _fftHalfSize = _fftSize / 2;
        _overlapSize = _fftHalfSize;

        _inputBuffer = new float[_fftHalfSize];
        _outputBuffer = new float[_fftHalfSize];
        _overlapBuffer = new float[_overlapSize];
        _fft = new FastFft(_fftSize);
        _fftReal = new float[_fftSize];
        _fftImag = new float[_fftSize];
        _convReal = new float[_fftSize];
        _convImag = new float[_fftSize];

        int maxPreDelaySamples = Math.Max(1, (int)(0.1f * sampleRate));
        _preDelayBuffer = new float[maxPreDelaySamples];
        _dryAlignedBuffer = new float[Math.Max(blockSize, 1)];
        _dryDelayLine = new DelayLine(_fftHalfSize + maxPreDelaySamples + 1);

        ResetProcessingState();
        ApplyIrPreset(_irPreset);
    }

    public void Process(Span<float> buffer, in PluginProcessContext context)
    {
        Process(buffer);
    }

    public void Process(Span<float> buffer)
    {
        if (IsBypassed || !_irLoaded || buffer.IsEmpty)
        {
            return;
        }

        var dryAligned = _dryAlignedBuffer.AsSpan(0, buffer.Length);
        if (_dryDelayLine is not null && LatencySamples > 0)
        {
            _dryDelayLine.Process(buffer, dryAligned, LatencySamples);
        }
        else
        {
            buffer.CopyTo(dryAligned);
        }

        float peakIn = 0f;
        float peakOut = 0f;
        float dry = 1f - _dryWet;
        float wet = _dryWet;

        for (int i = 0; i < buffer.Length; i++)
        {
            float input = buffer[i];
            peakIn = MathF.Max(peakIn, MathF.Abs(input));

            float delayedInput = input;
            if (_preDelaySamples > 0)
            {
                delayedInput = _preDelayBuffer[_preDelayReadPos];
                _preDelayBuffer[_preDelayWritePos] = input;
                _preDelayWritePos = (_preDelayWritePos + 1) % _preDelayBuffer.Length;
                _preDelayReadPos = (_preDelayReadPos + 1) % _preDelayBuffer.Length;
            }

            _inputBuffer[_inputPos++] = delayedInput;
            if (_inputPos >= _fftHalfSize)
            {
                ProcessFftBlock();
                _inputPos = 0;
            }

            float wetSample = 0f;
            if (_outputAvailable > 0)
            {
                wetSample = _outputBuffer[_outputPos++];
                _outputAvailable--;
                if (_outputPos >= _outputBuffer.Length)
                {
                    _outputPos = 0;
                }
            }

            float output = dryAligned[i] * dry + wetSample * wet;
            buffer[i] = output;
            peakOut = MathF.Max(peakOut, MathF.Abs(output));
        }

        Interlocked.Exchange(ref _inputLevelBits, BitConverter.SingleToInt32Bits(peakIn));
        Interlocked.Exchange(ref _outputLevelBits, BitConverter.SingleToInt32Bits(peakOut));
    }

    private void ProcessFftBlock()
    {
        if (_fft is null || _irPartitionReal.Length == 0)
        {
            return;
        }

        Array.Clear(_fftReal, 0, _fftReal.Length);
        Array.Clear(_fftImag, 0, _fftImag.Length);
        _inputBuffer.AsSpan().CopyTo(_fftReal);
        _fft.Forward(_fftReal, _fftImag);

        int currentHistory = _historyIndex;
        _fftReal.CopyTo(_inputHistoryReal[currentHistory], 0);
        _fftImag.CopyTo(_inputHistoryImag[currentHistory], 0);

        Array.Clear(_convReal, 0, _convReal.Length);
        Array.Clear(_convImag, 0, _convImag.Length);

        int partitionCount = _irPartitionReal.Length;
        for (int partition = 0; partition < partitionCount; partition++)
        {
            int historySlot = currentHistory - partition;
            if (historySlot < 0)
            {
                historySlot += partitionCount;
            }

            var inputReal = _inputHistoryReal[historySlot];
            var inputImag = _inputHistoryImag[historySlot];
            var irReal = _irPartitionReal[partition];
            var irImag = _irPartitionImag[partition];

            for (int i = 0; i < _fftSize; i++)
            {
                float aRe = inputReal[i];
                float aIm = inputImag[i];
                float bRe = irReal[i];
                float bIm = irImag[i];
                _convReal[i] += aRe * bRe - aIm * bIm;
                _convImag[i] += aRe * bIm + aIm * bRe;
            }
        }

        _fft.Inverse(_convReal, _convImag);

        for (int i = 0; i < _fftHalfSize; i++)
        {
            _outputBuffer[i] = _convReal[i] * _decay + _overlapBuffer[i];
            _overlapBuffer[i] = _convReal[i + _fftHalfSize] * _decay;
        }

        _outputAvailable = _fftHalfSize;
        _outputPos = 0;

        _historyIndex++;
        if (_historyIndex >= partitionCount)
        {
            _historyIndex = 0;
        }
    }

    public void SetParameter(int index, float value)
    {
        switch (index)
        {
            case DryWetIndex:
                _dryWet = Math.Clamp(value, 0f, 1f);
                break;
            case DecayIndex:
                _decay = Math.Clamp(value, 0.1f, 2f);
                break;
            case PreDelayIndex:
                _preDelayMs = Math.Clamp(value, 0f, 100f);
                UpdatePreDelay();
                break;
            case IrPresetIndex:
                int preset = (int)Math.Clamp(value, 0f, IrPresetNames.Length - 1);
                if (preset != _irPreset)
                {
                    _irPreset = preset;
                    ApplyIrPreset(preset);
                }
                break;
        }
    }

    public bool LoadImpulseResponse(string path)
    {
        try
        {
            using var reader = new AudioFileReader(path);

            int irLength = (int)(reader.TotalTime.TotalSeconds * _sampleRate);
            if (irLength > _sampleRate * 10)
            {
                irLength = _sampleRate * 10;
            }

            var tempSamples = new List<float>();
            float[] readBuffer = new float[4096];
            int samplesRead;

            while ((samplesRead = reader.Read(readBuffer, 0, readBuffer.Length)) > 0)
            {
                if (reader.WaveFormat.Channels == 2)
                {
                    for (int i = 0; i < samplesRead; i += 2)
                    {
                        tempSamples.Add((readBuffer[i] + readBuffer[i + 1]) * 0.5f);
                    }
                }
                else
                {
                    for (int i = 0; i < samplesRead; i++)
                    {
                        tempSamples.Add(readBuffer[i]);
                    }
                }

                if (tempSamples.Count >= irLength)
                {
                    break;
                }
            }

            if (reader.WaveFormat.SampleRate != _sampleRate)
            {
                tempSamples = ResampleIr(tempSamples, reader.WaveFormat.SampleRate, _sampleRate);
            }

            SetIrSamples(tempSamples.ToArray());
            _loadedIrPath = path;
            _statusMessage = $"Loaded: {Path.GetFileName(path)}";
            _irPreset = 5;
            return true;
        }
        catch (IOException ex)
        {
            _statusMessage = $"Error: {ex.Message}";
            return false;
        }
    }

    private void SetIrSamples(float[] samples)
    {
        if (samples.Length == 0 || _fft is null)
        {
            _irSamples = Array.Empty<float>();
            _irPartitionReal = Array.Empty<float[]>();
            _irPartitionImag = Array.Empty<float[]>();
            _inputHistoryReal = Array.Empty<float[]>();
            _inputHistoryImag = Array.Empty<float[]>();
            _irLoaded = false;
            ResetProcessingState();
            return;
        }

        _irSamples = new float[samples.Length];
        Array.Copy(samples, _irSamples, samples.Length);

        int partitionCount = Math.Max(1, (_irSamples.Length + _fftHalfSize - 1) / _fftHalfSize);
        _irPartitionReal = new float[partitionCount][];
        _irPartitionImag = new float[partitionCount][];
        _inputHistoryReal = new float[partitionCount][];
        _inputHistoryImag = new float[partitionCount][];

        for (int partition = 0; partition < partitionCount; partition++)
        {
            var partReal = new float[_fftSize];
            var partImag = new float[_fftSize];

            int sourceOffset = partition * _fftHalfSize;
            int copyLength = Math.Min(_fftHalfSize, _irSamples.Length - sourceOffset);
            if (copyLength > 0)
            {
                Array.Copy(_irSamples, sourceOffset, partReal, 0, copyLength);
            }

            _fft.Forward(partReal, partImag);
            _irPartitionReal[partition] = partReal;
            _irPartitionImag[partition] = partImag;
            _inputHistoryReal[partition] = new float[_fftSize];
            _inputHistoryImag[partition] = new float[_fftSize];
        }

        _irLoaded = true;
        ResetProcessingState();
    }

    private void ApplyIrPreset(int preset)
    {
        _loadedIrPath = null;

        switch (preset)
        {
            case 0:
                _irLoaded = false;
                _statusMessage = "No IR loaded";
                ResetProcessingState();
                break;
            case 1:
                GenerateRoomIr(0.3f, 0.4f);
                _statusMessage = "Small Room";
                break;
            case 2:
                GenerateRoomIr(1.0f, 0.6f);
                _statusMessage = "Medium Hall";
                break;
            case 3:
                GenerateRoomIr(2.0f, 0.7f);
                _statusMessage = "Large Hall";
                break;
            case 4:
                GeneratePlateIr(1.5f, 0.5f);
                _statusMessage = "Plate";
                break;
            case 5:
                _irLoaded = false;
                _statusMessage = "Select IR file...";
                ResetProcessingState();
                break;
        }
    }

    /// <summary>Simple xorshift32 PRNG for deterministic IR generation (not security-sensitive).</summary>
    private static float NextFloat(ref uint state)
    {
        state ^= state << 13;
        state ^= state >> 17;
        state ^= state << 5;
        return (state & 0x7FFFFFu) / (float)0x800000u;
    }

    private void GenerateRoomIr(float durationSec, float density)
    {
        int irLength = Math.Max(1, (int)(durationSec * _sampleRate));
        var ir = new float[irLength];
        uint rngState = 42u;

        int earlyCount = Math.Min((int)(0.05f * _sampleRate), irLength);
        for (int i = 0; i < earlyCount; i++)
        {
            if (NextFloat(ref rngState) < density * 0.1f)
            {
                float amp = 0.5f * MathF.Exp(-i / (0.02f * _sampleRate));
                ir[i] = (NextFloat(ref rngState) * 2f - 1f) * amp;
            }
        }

        float decayRate = 3f / (_sampleRate * durationSec);
        for (int i = earlyCount; i < irLength; i++)
        {
            float amp = MathF.Exp(-i * decayRate) * density;
            ir[i] = (NextFloat(ref rngState) * 2f - 1f) * amp;
        }

        NormalizeImpulse(ir, targetPeak: 0.5f);
        SetIrSamples(ir);
    }

    private void GeneratePlateIr(float durationSec, float density)
    {
        int irLength = Math.Max(1, (int)(durationSec * _sampleRate));
        var ir = new float[irLength];
        uint rngState = 123u;

        float decayRate = 4f / (_sampleRate * durationSec);
        for (int i = 0; i < irLength; i++)
        {
            float t = (float)i / _sampleRate;
            float amp = MathF.Exp(-i * decayRate);
            float hfRolloff = MathF.Exp(-t * 8f);
            float noise = NextFloat(ref rngState) * 2f - 1f;
            ir[i] = noise * amp * density * (0.3f + 0.7f * hfRolloff);
        }

        NormalizeImpulse(ir, targetPeak: 0.4f);
        SetIrSamples(ir);
    }

    private void UpdatePreDelay()
    {
        if (_sampleRate <= 0 || _preDelayBuffer.Length == 0)
        {
            return;
        }

        _preDelaySamples = Math.Clamp((int)(_preDelayMs * 0.001f * _sampleRate), 0, _preDelayBuffer.Length - 1);
        _preDelayReadPos = (_preDelayWritePos - _preDelaySamples + _preDelayBuffer.Length) % _preDelayBuffer.Length;
    }

    private void ResetProcessingState()
    {
        Array.Clear(_inputBuffer, 0, _inputBuffer.Length);
        Array.Clear(_outputBuffer, 0, _outputBuffer.Length);
        Array.Clear(_overlapBuffer, 0, _overlapBuffer.Length);
        Array.Clear(_fftReal, 0, _fftReal.Length);
        Array.Clear(_fftImag, 0, _fftImag.Length);
        Array.Clear(_convReal, 0, _convReal.Length);
        Array.Clear(_convImag, 0, _convImag.Length);
        Array.Clear(_preDelayBuffer, 0, _preDelayBuffer.Length);
        Array.Clear(_dryAlignedBuffer, 0, _dryAlignedBuffer.Length);

        for (int i = 0; i < _inputHistoryReal.Length; i++)
        {
            Array.Clear(_inputHistoryReal[i], 0, _inputHistoryReal[i].Length);
            Array.Clear(_inputHistoryImag[i], 0, _inputHistoryImag[i].Length);
        }

        _inputPos = 0;
        _outputPos = 0;
        _outputAvailable = 0;
        _historyIndex = 0;
        _preDelayWritePos = 0;
        UpdatePreDelay();

        if (_fftHalfSize > 0 || _preDelayBuffer.Length > 0)
        {
            _dryDelayLine = new DelayLine(Math.Max(1, _fftHalfSize + _preDelayBuffer.Length + 1));
        }
    }

    private static void NormalizeImpulse(float[] samples, float targetPeak)
    {
        float maxAbs = 0f;
        for (int i = 0; i < samples.Length; i++)
        {
            maxAbs = MathF.Max(maxAbs, MathF.Abs(samples[i]));
        }

        if (maxAbs <= 0f)
        {
            return;
        }

        float scale = targetPeak / maxAbs;
        for (int i = 0; i < samples.Length; i++)
        {
            samples[i] *= scale;
        }
    }

    private static List<float> ResampleIr(List<float> samples, int fromRate, int toRate)
    {
        if (fromRate == toRate)
        {
            return samples;
        }

        double ratio = (double)toRate / fromRate;
        int newLength = (int)(samples.Count * ratio);
        var result = new List<float>(newLength);

        for (int i = 0; i < newLength; i++)
        {
            double srcPos = i / ratio;
            int srcIdx = (int)srcPos;
            float frac = (float)(srcPos - srcIdx);

            if (srcIdx >= samples.Count - 1)
            {
                result.Add(samples[^1]);
            }
            else
            {
                result.Add(samples[srcIdx] * (1f - frac) + samples[srcIdx + 1] * frac);
            }
        }

        return result;
    }

    public float GetAndResetInputLevel()
    {
        return BitConverter.Int32BitsToSingle(Interlocked.Exchange(ref _inputLevelBits, 0));
    }

    public float GetAndResetOutputLevel()
    {
        return BitConverter.Int32BitsToSingle(Interlocked.Exchange(ref _outputLevelBits, 0));
    }

    public byte[] GetState()
    {
        using var ms = new MemoryStream();
        using var writer = new BinaryWriter(ms);

        writer.Write(_dryWet);
        writer.Write(_decay);
        writer.Write(_preDelayMs);
        writer.Write(_irPreset);
        writer.Write(_loadedIrPath ?? string.Empty);

        return ms.ToArray();
    }

    public void SetState(byte[] state)
    {
        ArgumentNullException.ThrowIfNull(state);

        if (state.Length == 0)
        {
            return;
        }

        try
        {
            using var ms = new MemoryStream(state);
            using var reader = new BinaryReader(ms);

            _dryWet = reader.ReadSingle();
            _decay = reader.ReadSingle();
            _preDelayMs = reader.ReadSingle();
            _irPreset = reader.ReadInt32();
            string customPath = reader.ReadString();

            UpdatePreDelay();

            if (_irPreset == 5 && !string.IsNullOrEmpty(customPath) && File.Exists(customPath))
            {
                LoadImpulseResponse(customPath);
            }
            else
            {
                ApplyIrPreset(_irPreset);
            }
        }
        catch (InvalidOperationException)
        {
        }
    }

    public void ApplyQuality(AudioQualityProfile profile)
    {
    }

    public void Dispose()
    {
    }
}
