using System.Threading;
using HotMic.Common.Configuration;
using HotMic.Core.Dsp;
using NAudio.Wave;

namespace HotMic.Core.Plugins.BuiltIn;

/// <summary>
/// Convolution reverb plugin using FFT-based overlap-add convolution.
/// Supports loading custom impulse response files or using built-in presets.
/// </summary>
public sealed class ConvolutionReverbPlugin : IPlugin, IQualityConfigurablePlugin, IPluginStatusProvider
{
    public const int DryWetIndex = 0;
    public const int DecayIndex = 1;
    public const int PreDelayIndex = 2;
    public const int IrPresetIndex = 3;

    // FFT parameters
    private int _fftSize = 2048;
    private int _fftHalfSize = 1024;
    private int _overlapSize = 1024;

    // Pre-allocated buffers (no allocations in audio thread)
    private float[] _inputBuffer = Array.Empty<float>();
    private float[] _outputBuffer = Array.Empty<float>();
    private float[] _overlapBuffer = Array.Empty<float>();
    private FastFft? _fft;
    private float[] _fftReal = Array.Empty<float>();
    private float[] _fftImag = Array.Empty<float>();
    private float[] _irFftReal = Array.Empty<float>();
    private float[] _irFftImag = Array.Empty<float>();
    private float[] _convReal = Array.Empty<float>();
    private float[] _convImag = Array.Empty<float>();
    private float[] _irSamples = Array.Empty<float>();

    // Pre-delay buffer
    private float[] _preDelayBuffer = Array.Empty<float>();
    private int _preDelayWritePos;
    private int _preDelayReadPos;
    private int _preDelaySamples;

    // Buffer positions
    private int _inputPos;
    private int _outputPos;
    private int _outputAvailable;

    // Parameters
    private float _dryWet = 0.3f;
    private float _decay = 1.0f;
    private float _preDelayMs = 0f;
    private int _irPreset;

    private int _sampleRate;
    private int _blockSize;
    private bool _irLoaded;
    private string _statusMessage = "No IR loaded";
    private string? _loadedIrPath;

    // Level metering
    private int _inputLevelBits;
    private int _outputLevelBits;

    // Built-in IR presets
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

    // UI properties
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

        // Choose FFT size based on sample rate for reasonable latency
        _fftSize = sampleRate >= 88200 ? 4096 : 2048;
        _fftHalfSize = _fftSize / 2;
        _overlapSize = _fftHalfSize;

        // Pre-allocate all buffers
        _inputBuffer = new float[_fftSize];
        _outputBuffer = new float[_fftSize];
        _overlapBuffer = new float[_overlapSize];
        _fft = new FastFft(_fftSize);
        _fftReal = new float[_fftSize];
        _fftImag = new float[_fftSize];
        _convReal = new float[_fftSize];
        _convImag = new float[_fftSize];
        _irFftReal = new float[_fftSize];
        _irFftImag = new float[_fftSize];

        // Pre-delay buffer (max 100ms)
        int maxPreDelaySamples = (int)(0.1f * sampleRate);
        _preDelayBuffer = new float[maxPreDelaySamples];
        UpdatePreDelay();

        // Reset positions
        _inputPos = 0;
        _outputPos = 0;
        _outputAvailable = 0;
        _preDelayWritePos = 0;

        // Load IR based on preset
        ApplyIrPreset(_irPreset);
    }

    public void Process(Span<float> buffer)
    {
        if (IsBypassed || !_irLoaded)
        {
            return;
        }

        float peakIn = 0f;
        float peakOut = 0f;
        float dry = 1f - _dryWet;
        float wet = _dryWet;

        for (int i = 0; i < buffer.Length; i++)
        {
            float input = buffer[i];
            float absIn = MathF.Abs(input);
            if (absIn > peakIn) peakIn = absIn;

            // Apply pre-delay
            float delayedInput = input;
            if (_preDelaySamples > 0)
            {
                delayedInput = _preDelayBuffer[_preDelayReadPos];
                _preDelayBuffer[_preDelayWritePos] = input;
                _preDelayWritePos = (_preDelayWritePos + 1) % _preDelayBuffer.Length;
                _preDelayReadPos = (_preDelayReadPos + 1) % _preDelayBuffer.Length;
            }

            // Add input to FFT buffer
            _inputBuffer[_inputPos++] = delayedInput;

            // When we have enough samples, process FFT
            if (_inputPos >= _fftHalfSize)
            {
                ProcessFftBlock();
                _inputPos = 0;
            }

            // Get wet output
            float wetSample = 0f;
            if (_outputAvailable > 0)
            {
                wetSample = _outputBuffer[_outputPos++];
                _outputAvailable--;
                if (_outputPos >= _fftSize)
                {
                    _outputPos = 0;
                }
            }

            // Mix dry and wet
            float output = input * dry + wetSample * wet;
            buffer[i] = output;

            float absOut = MathF.Abs(output);
            if (absOut > peakOut) peakOut = absOut;
        }

        Interlocked.Exchange(ref _inputLevelBits, BitConverter.SingleToInt32Bits(peakIn));
        Interlocked.Exchange(ref _outputLevelBits, BitConverter.SingleToInt32Bits(peakOut));
    }

    private void ProcessFftBlock()
    {
        // Zero-pad second half
        Array.Clear(_inputBuffer, _fftHalfSize, _fftHalfSize);

        if (_fft is null)
        {
            return;
        }

        // Forward FFT
        Array.Copy(_inputBuffer, _fftReal, _fftSize);
        Array.Clear(_fftImag, 0, _fftImag.Length);
        _fft.Forward(_fftReal, _fftImag);

        // Multiply with IR in frequency domain
        for (int i = 0; i < _fftSize; i++)
        {
            float aRe = _fftReal[i];
            float aIm = _fftImag[i];
            float bRe = _irFftReal[i];
            float bIm = _irFftImag[i];
            _convReal[i] = aRe * bRe - aIm * bIm;
            _convImag[i] = aRe * bIm + aIm * bRe;
        }

        // Inverse FFT
        _fft.Inverse(_convReal, _convImag);

        // Overlap-add
        for (int i = 0; i < _fftSize; i++)
        {
            float sample = _convReal[i] * _decay;
            if (i < _overlapSize)
            {
                sample += _overlapBuffer[i];
            }
            _outputBuffer[i] = sample;
        }

        // Save overlap for next block
        Array.Copy(_outputBuffer, _fftHalfSize, _overlapBuffer, 0, _overlapSize);

        _outputAvailable = _fftHalfSize;
        _outputPos = 0;
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

    /// <summary>
    /// Load a custom impulse response from a WAV file.
    /// </summary>
    public bool LoadImpulseResponse(string path)
    {
        try
        {
            using var reader = new AudioFileReader(path);

            // Convert to mono at current sample rate
            int irLength = (int)(reader.TotalTime.TotalSeconds * _sampleRate);
            if (irLength > _sampleRate * 10) // Max 10 seconds
            {
                irLength = _sampleRate * 10;
            }

            var tempSamples = new List<float>();
            float[] readBuffer = new float[4096];
            int samplesRead;

            while ((samplesRead = reader.Read(readBuffer, 0, readBuffer.Length)) > 0)
            {
                // Convert to mono if stereo
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

            // Resample if needed
            if (reader.WaveFormat.SampleRate != _sampleRate)
            {
                tempSamples = ResampleIr(tempSamples, reader.WaveFormat.SampleRate, _sampleRate);
            }

            SetIrSamples(tempSamples.ToArray());
            _loadedIrPath = path;
            _statusMessage = $"Loaded: {Path.GetFileName(path)}";
            _irPreset = 5; // Custom
            return true;
        }
        catch (Exception ex)
        {
            _statusMessage = $"Error: {ex.Message}";
            return false;
        }
    }

    private void SetIrSamples(float[] samples)
    {
        if (samples.Length == 0)
        {
            _irLoaded = false;
            return;
        }

        // Ensure IR fits in FFT buffer (pad or truncate)
        _irSamples = new float[_fftSize];
        int copyLength = Math.Min(samples.Length, _fftSize);
        Array.Copy(samples, _irSamples, copyLength);

        if (_fft is null)
        {
            _irLoaded = false;
            return;
        }

        // Pre-compute IR FFT
        Array.Copy(_irSamples, _irFftReal, _fftSize);
        Array.Clear(_irFftImag, 0, _irFftImag.Length);
        _fft.Forward(_irFftReal, _irFftImag);

        _irLoaded = true;
    }

    private void ApplyIrPreset(int preset)
    {
        _loadedIrPath = null;

        switch (preset)
        {
            case 0: // None
                _irLoaded = false;
                _statusMessage = "No IR loaded";
                break;
            case 1: // Small Room
                GenerateRoomIr(0.3f, 0.4f);
                _statusMessage = "Small Room";
                break;
            case 2: // Medium Hall
                GenerateRoomIr(1.0f, 0.6f);
                _statusMessage = "Medium Hall";
                break;
            case 3: // Large Hall
                GenerateRoomIr(2.0f, 0.7f);
                _statusMessage = "Large Hall";
                break;
            case 4: // Plate
                GeneratePlateIr(1.5f, 0.5f);
                _statusMessage = "Plate";
                break;
            case 5: // Custom (do nothing, wait for file load)
                if (_loadedIrPath == null)
                {
                    _irLoaded = false;
                    _statusMessage = "Select IR file...";
                }
                break;
        }
    }

    private void GenerateRoomIr(float durationSec, float density)
    {
        int irLength = Math.Min((int)(durationSec * _sampleRate), _fftSize);
        var ir = new float[_fftSize];

        // Simple algorithmic reverb impulse response
        // Early reflections + exponential decay tail
        var random = new Random(42); // Fixed seed for determinism

        // Early reflections (first 50ms)
        int earlyCount = (int)(0.05f * _sampleRate);
        for (int i = 0; i < Math.Min(earlyCount, irLength); i++)
        {
            if (random.NextSingle() < density * 0.1f)
            {
                float amp = 0.5f * MathF.Exp(-i / (0.02f * _sampleRate));
                ir[i] = (random.NextSingle() * 2f - 1f) * amp;
            }
        }

        // Diffuse tail
        float decayRate = 3f / (_sampleRate * durationSec);
        for (int i = earlyCount; i < irLength; i++)
        {
            float amp = MathF.Exp(-i * decayRate) * density;
            ir[i] = (random.NextSingle() * 2f - 1f) * amp;
        }

        // Normalize
        float maxAbs = 0f;
        for (int i = 0; i < irLength; i++)
        {
            float abs = MathF.Abs(ir[i]);
            if (abs > maxAbs) maxAbs = abs;
        }
        if (maxAbs > 0)
        {
            float scale = 0.5f / maxAbs;
            for (int i = 0; i < irLength; i++)
            {
                ir[i] *= scale;
            }
        }

        SetIrSamples(ir);
    }

    private void GeneratePlateIr(float durationSec, float density)
    {
        int irLength = Math.Min((int)(durationSec * _sampleRate), _fftSize);
        var ir = new float[_fftSize];
        var random = new Random(123);

        // Plate reverb: dense, smooth decay with high diffusion
        float decayRate = 4f / (_sampleRate * durationSec);

        for (int i = 0; i < irLength; i++)
        {
            float t = (float)i / _sampleRate;
            float amp = MathF.Exp(-i * decayRate);

            // High-frequency rolloff simulation
            float hfRolloff = MathF.Exp(-t * 8f);

            // Dense noise with slight modulation
            float noise = random.NextSingle() * 2f - 1f;
            ir[i] = noise * amp * density * (0.3f + 0.7f * hfRolloff);
        }

        // Normalize
        float maxAbs = 0f;
        for (int i = 0; i < irLength; i++)
        {
            float abs = MathF.Abs(ir[i]);
            if (abs > maxAbs) maxAbs = abs;
        }
        if (maxAbs > 0)
        {
            float scale = 0.4f / maxAbs;
            for (int i = 0; i < irLength; i++)
            {
                ir[i] *= scale;
            }
        }

        SetIrSamples(ir);
    }

    private void UpdatePreDelay()
    {
        if (_sampleRate == 0) return;
        _preDelaySamples = (int)(_preDelayMs * 0.001f * _sampleRate);
        _preDelayReadPos = (_preDelayWritePos - _preDelaySamples + _preDelayBuffer.Length) % _preDelayBuffer.Length;
        if (_preDelayReadPos < 0) _preDelayReadPos += _preDelayBuffer.Length;
    }

    private static List<float> ResampleIr(List<float> samples, int fromRate, int toRate)
    {
        if (fromRate == toRate) return samples;

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
        if (state.Length == 0) return;

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
        catch
        {
            // Ignore corrupt state
        }
    }

    public void ApplyQuality(AudioQualityProfile profile)
    {
        // Could adjust FFT size based on quality mode if needed
    }

    public void Dispose()
    {
        // No unmanaged resources
    }
}
