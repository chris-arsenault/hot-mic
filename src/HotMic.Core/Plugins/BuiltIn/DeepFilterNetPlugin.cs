using System.Threading;
using HotMic.Common.Configuration;
using HotMic.Core.Dsp;
using HotMic.Core.Plugins;
using HotMic.Core.Threading;

namespace HotMic.Core.Plugins.BuiltIn;

public sealed class DeepFilterNetPlugin : IPlugin, IQualityConfigurablePlugin, IPluginStatusProvider
{
    public const int ReductionIndex = 0;
    public const int AttenuationIndex = 1;
    public const int PostFilterIndex = 2;

    private const float MixSmoothingMs = 8f;

    private LinearSmoother _mixSmoother = new();
    private readonly object _workerLock = new();
    private DeepFilterNetProcessor? _processor;
    private LockFreeRingBuffer? _inputBuffer;
    private LockFreeRingBuffer? _outputBuffer;
    private float[] _hopBuffer = Array.Empty<float>();
    private float[] _hopOutput = Array.Empty<float>();
    private float[] _processedScratch = Array.Empty<float>();
    private float[] _dryRing = Array.Empty<float>();
    private int _dryIndex;
    private int _hopSize;
    private int _latencySamples;
    private int _sampleRate;
    private Thread? _workerThread;
    private AutoResetEvent? _frameSignal;
    private int _running;
    private float _reductionPct = 100f;
    private float _attenLimitDb = 40f;
    private float _postFilterEnabled = 1f;
    private bool _forcedBypass;
    private bool _wasBypassed = true;
    private bool _roundTripPrimed;
    private int _roundTripMinOutput;
    private string _statusMessage = string.Empty;
    private int _gainReductionDbBits;
    private float _smoothedGrDb;
    private const float GrSmoothingAttack = 0.3f;
    private const float GrSmoothingRelease = 0.85f;
    private const float SilenceThreshold = 1e-8f;

    public DeepFilterNetPlugin()
    {
        Parameters =
        [
            new PluginParameter
            {
                Index = ReductionIndex,
                Name = "Reduction",
                MinValue = 0f,
                MaxValue = 100f,
                DefaultValue = 100f,
                Unit = "%"
            },
            new PluginParameter
            {
                Index = AttenuationIndex,
                Name = "Attenuation Limit",
                MinValue = 6f,
                MaxValue = 60f,
                DefaultValue = 40f,
                Unit = "dB"
            },
            new PluginParameter
            {
                Index = PostFilterIndex,
                Name = "Post-Filter",
                MinValue = 0f,
                MaxValue = 1f,
                DefaultValue = 1f,
                Unit = ""
            }
        ];
    }

    public string Id => "builtin:deepfilternet";

    public string Name => "DeepFilterNet";

    public bool IsBypassed { get; set; }

    public int LatencySamples => _forcedBypass ? 0 : _latencySamples;

    public IReadOnlyList<PluginParameter> Parameters { get; }

    public string StatusMessage => _statusMessage;

    /// <summary>
    /// Gets the current gain reduction in dB (positive value = reduction).
    /// This represents how much DeepFilterNet is attenuating the noise.
    /// </summary>
    public float GainReductionDb
    {
        get => BitConverter.Int32BitsToSingle(Interlocked.CompareExchange(ref _gainReductionDbBits, 0, 0));
    }

    public void Initialize(int sampleRate, int blockSize)
    {
        _sampleRate = sampleRate;
        _statusMessage = string.Empty;
        _forcedBypass = false;

        StopWorker();

        if (sampleRate != 48000)
        {
            _forcedBypass = true;
            _statusMessage = "DeepFilterNet requires 48kHz; auto-bypassed.";
            _latencySamples = 0;
            return;
        }

        try
        {
            string modelDir = ResolveModelDirectory();
            if (string.IsNullOrEmpty(modelDir))
            {
                throw new DirectoryNotFoundException("DeepFilterNet model directory not found.");
            }
            _processor = new DeepFilterNetProcessor(modelDir);
            _hopSize = _processor.HopSize;
            _latencySamples = _processor.LatencySamples;
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException or InvalidOperationException)
        {
            _forcedBypass = true;
            _statusMessage = $"DeepFilterNet models missing ({ex.Message}); auto-bypassed.";
            _latencySamples = 0;
            return;
        }
        catch (Exception ex) when (ex is DllNotFoundException or BadImageFormatException)
        {
            _forcedBypass = true;
            _statusMessage = $"DeepFilterNet runtime error ({ex.Message}); auto-bypassed.";
            _latencySamples = 0;
            return;
        }

        InitializeBuffers(blockSize);
        if (DeepFilterNetProcessor.RoundTripOnly)
        {
            _statusMessage = "DeepFilterNet STFT round-trip mode.";
            int hop = Math.Max(1, _hopSize);
            int minOutput = ((blockSize / hop) + 2) * hop;
            _roundTripMinOutput = Math.Min(_outputBuffer!.Capacity, Math.Max(hop * 2, minOutput));
            _roundTripPrimed = false;
        }
        _mixSmoother.Configure(sampleRate, MixSmoothingMs, _reductionPct / 100f);
        if (!DeepFilterNetProcessor.RoundTripOnly)
        {
            StartWorker();
        }
        _wasBypassed = false;
    }

    public void Process(Span<float> buffer)
    {
        if (IsBypassed || _forcedBypass || _processor is null || _inputBuffer is null || _outputBuffer is null)
        {
            _wasBypassed = true;
            Interlocked.Exchange(ref _gainReductionDbBits, 0);
            return;
        }

        if (_wasBypassed)
        {
            ResetBuffers();
            _wasBypassed = false;
        }

        int read = 0;
        if (DeepFilterNetProcessor.RoundTripOnly)
        {
            _inputBuffer.Write(buffer);
            while (_inputBuffer.AvailableRead >= _hopSize)
            {
                int hopRead = _inputBuffer.Read(_hopBuffer);
                if (hopRead < _hopSize)
                {
                    break;
                }

                _processor.ProcessHop(_hopBuffer, _hopOutput, postFilterEnabled: false, attenLimitDb: _attenLimitDb);
                _outputBuffer.Write(_hopOutput);
            }

            if (!_roundTripPrimed && _outputBuffer.AvailableRead >= _roundTripMinOutput)
            {
                _roundTripPrimed = true;
            }

            if (!_roundTripPrimed)
            {
                buffer.Clear();
                Interlocked.Exchange(ref _gainReductionDbBits, 0);
                return;
            }

            read = _outputBuffer.Read(buffer);
            if (read < buffer.Length)
            {
                buffer.Slice(read).Clear();
            }

            Interlocked.Exchange(ref _gainReductionDbBits, 0);
            return;
        }

        _inputBuffer.Write(buffer);
        _frameSignal?.Set();
        read = _outputBuffer.Read(_processedScratch.AsSpan(0, buffer.Length));

        float mix = _mixSmoother.Current;
        bool smoothing = _mixSmoother.IsSmoothing;

        float currentInputEnergy = 0f;
        float dryEnergy = 0f;
        float outputEnergy = 0f;

        for (int i = 0; i < buffer.Length; i++)
        {
            if (smoothing)
            {
                mix = _mixSmoother.Next();
                smoothing = _mixSmoother.IsSmoothing;
            }

            float input = buffer[i];
            float dry = _dryRing[_dryIndex];
            float wet = i < read ? _processedScratch[i] : dry;
            float output = dry * (1f - mix) + wet * mix;
            buffer[i] = output;

            // Track current input energy for silence detection
            currentInputEnergy += input * input;
            // Track delayed input and output for GR measurement
            dryEnergy += dry * dry;
            outputEnergy += output * output;

            _dryRing[_dryIndex] = input;
            _dryIndex++;
            if (_dryIndex >= _dryRing.Length)
            {
                _dryIndex = 0;
            }
        }

        // Calculate GR from actual plugin output vs input (true measurement)
        // Use current input energy for silence detection (responds immediately to VAD gate)
        float avgCurrentInputEnergy = currentInputEnergy / buffer.Length;

        if (avgCurrentInputEnergy < SilenceThreshold)
        {
            // No signal coming in - decay GR quickly to zero
            _smoothedGrDb *= 0.5f;
            if (_smoothedGrDb < 0.1f)
            {
                _smoothedGrDb = 0f;
            }
        }
        else
        {
            // Calculate GR from delayed input vs output (properly aligned samples)
            float grDb = 0f;
            if (dryEnergy > 1e-10f && outputEnergy < dryEnergy)
            {
                float energyRatio = outputEnergy / dryEnergy;
                grDb = -10f * MathF.Log10(energyRatio + 1e-10f);
            }

            // Smooth the GR display (fast attack, slow release)
            float smoothing2 = grDb > _smoothedGrDb ? GrSmoothingAttack : GrSmoothingRelease;
            _smoothedGrDb = _smoothedGrDb * smoothing2 + grDb * (1f - smoothing2);
        }

        Interlocked.Exchange(ref _gainReductionDbBits, BitConverter.SingleToInt32Bits(_smoothedGrDb));
    }

    public void SetParameter(int index, float value)
    {
        switch (index)
        {
            case ReductionIndex:
                _reductionPct = Math.Clamp(value, 0f, 100f);
                if (_sampleRate > 0)
                {
                    _mixSmoother.SetTarget(_reductionPct / 100f);
                }
                break;
            case AttenuationIndex:
                Volatile.Write(ref _attenLimitDb, Math.Clamp(value, 6f, 60f));
                break;
            case PostFilterIndex:
                Volatile.Write(ref _postFilterEnabled, value >= 0.5f ? 1f : 0f);
                break;
        }
    }

    public byte[] GetState()
    {
        var bytes = new byte[sizeof(float) * 3];
        Buffer.BlockCopy(BitConverter.GetBytes(_reductionPct), 0, bytes, 0, 4);
        Buffer.BlockCopy(BitConverter.GetBytes(_attenLimitDb), 0, bytes, 4, 4);
        Buffer.BlockCopy(BitConverter.GetBytes(_postFilterEnabled), 0, bytes, 8, 4);
        return bytes;
    }

    public void SetState(byte[] state)
    {
        if (state.Length < sizeof(float))
        {
            return;
        }

        _reductionPct = BitConverter.ToSingle(state, 0);
        if (state.Length >= sizeof(float) * 2)
        {
            _attenLimitDb = BitConverter.ToSingle(state, 4);
        }
        if (state.Length >= sizeof(float) * 3)
        {
            _postFilterEnabled = BitConverter.ToSingle(state, 8) >= 0.5f ? 1f : 0f;
        }

        if (_sampleRate > 0)
        {
            _mixSmoother.SetTarget(_reductionPct / 100f);
        }
    }

    public void ApplyQuality(AudioQualityProfile profile)
    {
        // DeepFilterNet model sizes are fixed; quality mode has no effect.
    }

    public void Dispose()
    {
        StopWorker();
    }

    private void InitializeBuffers(int blockSize)
    {
        _processedScratch = new float[blockSize];
        _dryRing = new float[Math.Max(1, _latencySamples)];
        _dryIndex = 0;

        int ringCapacity = Math.Max(_hopSize * 8, blockSize * 4);
        _inputBuffer = new LockFreeRingBuffer(ringCapacity);
        _outputBuffer = new LockFreeRingBuffer(ringCapacity);
        _hopBuffer = new float[_hopSize];
        _hopOutput = new float[_hopSize];
        ResetBuffers();
    }

    private void ResetBuffers()
    {
        _inputBuffer?.Clear();
        _outputBuffer?.Clear();
        Array.Clear(_processedScratch, 0, _processedScratch.Length);
        Array.Clear(_dryRing, 0, _dryRing.Length);
        _dryIndex = 0;
        _roundTripPrimed = false;
        _processor?.Reset();
        _smoothedGrDb = 0f;
        Interlocked.Exchange(ref _gainReductionDbBits, 0);
    }

    private void StartWorker()
    {
        if (_processor is null || _inputBuffer is null || _outputBuffer is null)
        {
            return;
        }

        lock (_workerLock)
        {
            if (_workerThread is not null)
            {
                return;
            }

            _frameSignal = new AutoResetEvent(false);
            _running = 1;
            _workerThread = new Thread(WorkerLoop)
            {
                IsBackground = true,
                Name = "DeepFilterNetWorker"
            };
            _workerThread.Start();
        }
    }

    private void StopWorker()
    {
        lock (_workerLock)
        {
            if (_workerThread is null)
            {
                return;
            }

            Interlocked.Exchange(ref _running, 0);
            _frameSignal?.Set();
            _workerThread.Join(500);
            _workerThread = null;
            _frameSignal?.Dispose();
            _frameSignal = null;
            _processor?.Dispose();
            _processor = null;
            _latencySamples = 0;
        }
    }

    private void WorkerLoop()
    {
        if (_processor is null || _inputBuffer is null || _outputBuffer is null || _frameSignal is null)
        {
            return;
        }

        while (Volatile.Read(ref _running) == 1)
        {
            _frameSignal.WaitOne(10);

            while (_inputBuffer.AvailableRead >= _hopSize)
            {
                int read = _inputBuffer.Read(_hopBuffer);
                if (read < _hopSize)
                {
                    break;
                }

                bool postFilter = Volatile.Read(ref _postFilterEnabled) >= 0.5f;
                float attenDb = Volatile.Read(ref _attenLimitDb);
                _processor.ProcessHop(_hopBuffer, _hopOutput, postFilter, attenDb);
                _outputBuffer.Write(_hopOutput);
            }
        }
    }

    private static string ResolveModelDirectory()
    {
        string baseDir = AppContext.BaseDirectory;
        string primary = Path.Combine(baseDir, "Models", "deepfilternet3");
        if (File.Exists(Path.Combine(primary, "config.ini")))
        {
            return primary;
        }

        string assets = Path.Combine(baseDir, "Assets", "Models", "deepfilternet3");
        if (File.Exists(Path.Combine(assets, "config.ini")))
        {
            return assets;
        }

        return string.Empty;
    }

}
