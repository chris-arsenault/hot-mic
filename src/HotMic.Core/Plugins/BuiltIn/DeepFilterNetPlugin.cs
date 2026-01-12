using System.Diagnostics;
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
    private bool _debugEnabled;
    private long _debugNextTicks;
    private int _debugProcessCalls;
    private int _debugWorkerHops;
    private int _lastLsnrBits;
    private int _lastGainMinBits;
    private int _lastGainMaxBits;
    private int _lastHopPeakBits;
    private int _lastHopRmsBits;
    private int _lastSpecPeakBits;
    private int _lastOutPeakBits;
    private int _lastDebugFlags;

    private float _reductionPct = 100f;
    private float _attenLimitDb = 40f;
    private float _postFilterEnabled = 1f;
    private bool _forcedBypass;
    private bool _wasBypassed = true;
    private string _statusMessage = string.Empty;

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

    public void Initialize(int sampleRate, int blockSize)
    {
        _sampleRate = sampleRate;
        _statusMessage = string.Empty;
        _forcedBypass = false;
        _debugEnabled = string.Equals(Environment.GetEnvironmentVariable("HOTMIC_DFN_DEBUG"), "1", StringComparison.OrdinalIgnoreCase);
        _debugNextTicks = 0;

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
        _mixSmoother.Configure(sampleRate, MixSmoothingMs, _reductionPct / 100f);
        StartWorker();
        _wasBypassed = false;
    }

    public void Process(Span<float> buffer)
    {
        if (IsBypassed || _forcedBypass || _processor is null || _inputBuffer is null || _outputBuffer is null)
        {
            _wasBypassed = true;
            return;
        }

        if (_wasBypassed)
        {
            ResetBuffers();
            _wasBypassed = false;
        }

        _inputBuffer.Write(buffer);
        _frameSignal?.Set();
        if (_debugEnabled)
        {
            Interlocked.Increment(ref _debugProcessCalls);
        }

        int read = _outputBuffer.Read(_processedScratch.AsSpan(0, buffer.Length));

        float mix = _mixSmoother.Current;
        bool smoothing = _mixSmoother.IsSmoothing;
        bool debug = _debugEnabled;
        float peakIn = 0f;
        float peakDry = 0f;
        float peakWet = 0f;
        float peakOut = 0f;

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

            _dryRing[_dryIndex] = input;
            _dryIndex++;
            if (_dryIndex >= _dryRing.Length)
            {
                _dryIndex = 0;
            }

            if (debug)
            {
                float absIn = MathF.Abs(input);
                if (absIn > peakIn)
                {
                    peakIn = absIn;
                }

                float absDry = MathF.Abs(dry);
                if (absDry > peakDry)
                {
                    peakDry = absDry;
                }

                float absWet = MathF.Abs(wet);
                if (absWet > peakWet)
                {
                    peakWet = absWet;
                }

                float absOut = MathF.Abs(output);
                if (absOut > peakOut)
                {
                    peakOut = absOut;
                }
            }
        }

        if (debug)
        {
            MaybeLogDebug(peakIn, peakDry, peakWet, peakOut, read, mix);
        }
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
        _processor?.Reset();
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
                _processor.ProcessHop(_hopBuffer, _hopOutput, postFilter, attenDb,
                    out float lsnr, out int flags, out float gainMin, out float gainMax, out float hopPeak, out float hopRms,
                    out float specPeak, out float outPeak);
                Interlocked.Exchange(ref _lastLsnrBits, BitConverter.SingleToInt32Bits(lsnr));
                Interlocked.Exchange(ref _lastGainMinBits, BitConverter.SingleToInt32Bits(gainMin));
                Interlocked.Exchange(ref _lastGainMaxBits, BitConverter.SingleToInt32Bits(gainMax));
                Interlocked.Exchange(ref _lastHopPeakBits, BitConverter.SingleToInt32Bits(hopPeak));
                Interlocked.Exchange(ref _lastHopRmsBits, BitConverter.SingleToInt32Bits(hopRms));
                Interlocked.Exchange(ref _lastSpecPeakBits, BitConverter.SingleToInt32Bits(specPeak));
                Interlocked.Exchange(ref _lastOutPeakBits, BitConverter.SingleToInt32Bits(outPeak));
                Interlocked.Exchange(ref _lastDebugFlags, flags);
                if (_debugEnabled)
                {
                    Interlocked.Increment(ref _debugWorkerHops);
                }
                _outputBuffer.Write(_hopOutput);
            }
        }
    }

    private void MaybeLogDebug(float peakIn, float peakDry, float peakWet, float peakOut, int read, float mix)
    {
        long now = Stopwatch.GetTimestamp();
        if (now < _debugNextTicks)
        {
            return;
        }

        int processCalls = Interlocked.Exchange(ref _debugProcessCalls, 0);
        int workerHops = Interlocked.Exchange(ref _debugWorkerHops, 0);
        _debugNextTicks = now + (Stopwatch.Frequency * 2);

        float lsnr = BitConverter.Int32BitsToSingle(Interlocked.CompareExchange(ref _lastLsnrBits, 0, 0));
        float gainMin = BitConverter.Int32BitsToSingle(Interlocked.CompareExchange(ref _lastGainMinBits, 0, 0));
        float gainMax = BitConverter.Int32BitsToSingle(Interlocked.CompareExchange(ref _lastGainMaxBits, 0, 0));
        float hopPeak = BitConverter.Int32BitsToSingle(Interlocked.CompareExchange(ref _lastHopPeakBits, 0, 0));
        float hopRms = BitConverter.Int32BitsToSingle(Interlocked.CompareExchange(ref _lastHopRmsBits, 0, 0));
        float specPeak = BitConverter.Int32BitsToSingle(Interlocked.CompareExchange(ref _lastSpecPeakBits, 0, 0));
        float outPeak = BitConverter.Int32BitsToSingle(Interlocked.CompareExchange(ref _lastOutPeakBits, 0, 0));
        int flags = Interlocked.CompareExchange(ref _lastDebugFlags, 0, 0);
        int inAvail = _inputBuffer?.AvailableRead ?? 0;
        int outAvail = _outputBuffer?.AvailableRead ?? 0;
        float attenDb = Volatile.Read(ref _attenLimitDb);
        bool postFilter = Volatile.Read(ref _postFilterEnabled) >= 0.5f;

        Console.WriteLine(
            $"[DeepFilterNet] in={peakIn:0.000000} dry={peakDry:0.000000} wet={peakWet:0.000000} out={peakOut:0.000000} " +
            $"mix={mix:0.000} read={read} inBuf={inAvail} outBuf={outAvail} " +
            $"lsnr={lsnr:0.00} gainMin={gainMin:0.000} gainMax={gainMax:0.000} hopPeak={hopPeak:0.000000} hopRms={hopRms:0.000000} specPeak={specPeak:0.000000} outPeak={outPeak:0.000000} " +
            $"flags=0x{flags:X} atten={attenDb:0.0} post={(postFilter ? 1 : 0)} procCalls={processCalls} hops={workerHops}");
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
