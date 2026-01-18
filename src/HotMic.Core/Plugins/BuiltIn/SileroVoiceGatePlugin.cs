using System;
using System.IO;
using System.Threading;
using HotMic.Core.Dsp;
using HotMic.Core.Plugins;
using HotMic.Core.Threading;

namespace HotMic.Core.Plugins.BuiltIn;

public sealed class SileroVoiceGatePlugin : IPlugin, IPluginStatusProvider, IAnalysisSignalProducer
{
    public const int ThresholdIndex = 0;
    public const int AttackIndex = 1;
    public const int ReleaseIndex = 2;
    public const int HoldIndex = 3;

    private const int RequiredSampleRate = 48000;
    private const int Decimation = 3;
    private const float LowPassCutoffHz = 7000f;
    private const float SilencePowerThreshold = 1e-8f; // ~ -80 dBFS RMS
    private const int SilenceFramesForReset = 8;

    private readonly object _workerLock = new();
    private readonly OnePoleLowPass _decimatorFilter = new();

    private LockFreeRingBuffer? _inputBuffer;
    private float[] _frame48 = Array.Empty<float>();
    private float[] _frame16 = Array.Empty<float>();
    private int _frameSamples16k;
    private int _frameSamples48k;
    private Thread? _workerThread;
    private AutoResetEvent? _frameSignal;
    private int _running;
    private SileroVadInference? _inference;

    private float _threshold = 0.5f;
    private float _attackMs = 6f;
    private float _releaseMs = 120f;
    private float _holdMs = 80f;
    private float _attackCoeff;
    private float _releaseCoeff;
    private int _holdSamples;
    private int _holdSamplesLeft;
    private float _gate;
    private int _sampleRate;
    private int _silenceFrames;

    private int _vadBits;
    private int _inputLevelBits;
    private int _outputLevelBits;
    private bool _forcedBypass;
    private bool _wasBypassed = true;
    private string _statusMessage = string.Empty;
    private float[] _speechBuffer = Array.Empty<float>();

    public SileroVoiceGatePlugin()
    {
        Parameters =
        [
            new PluginParameter { Index = ThresholdIndex, Name = "Threshold", MinValue = 0.05f, MaxValue = 0.95f, DefaultValue = 0.5f, Unit = "" },
            new PluginParameter { Index = AttackIndex, Name = "Attack", MinValue = 1f, MaxValue = 50f, DefaultValue = 6f, Unit = "ms" },
            new PluginParameter { Index = ReleaseIndex, Name = "Release", MinValue = 20f, MaxValue = 500f, DefaultValue = 120f, Unit = "ms" },
            new PluginParameter { Index = HoldIndex, Name = "Hold", MinValue = 0f, MaxValue = 300f, DefaultValue = 80f, Unit = "ms" }
        ];
    }

    public string Id => "builtin:voice-gate";

    public string Name => "Voice Gate (AI)";

    public bool IsBypassed { get; set; }

    public int LatencySamples => 0;

    public IReadOnlyList<PluginParameter> Parameters { get; }

    public string StatusMessage => _statusMessage;

    public AnalysisSignalMask ProducedSignals => _forcedBypass || _inputBuffer is null
        ? AnalysisSignalMask.None
        : AnalysisSignalMask.SpeechPresence;

    public float VadProbability => BitConverter.Int32BitsToSingle(Interlocked.CompareExchange(ref _vadBits, 0, 0));

    public float GetAndResetInputLevel()
    {
        return BitConverter.Int32BitsToSingle(Interlocked.Exchange(ref _inputLevelBits, 0));
    }

    public float GetAndResetOutputLevel()
    {
        return BitConverter.Int32BitsToSingle(Interlocked.Exchange(ref _outputLevelBits, 0));
    }

    public void Initialize(int sampleRate, int blockSize)
    {
        _sampleRate = sampleRate;
        _statusMessage = string.Empty;
        _forcedBypass = false;

        StopWorker();

        if (sampleRate != RequiredSampleRate)
        {
            _forcedBypass = true;
            _statusMessage = "Voice Gate requires 48kHz input; auto-bypassed.";
            return;
        }

        try
        {
            string modelPath = ResolveModelPath();
            if (string.IsNullOrEmpty(modelPath))
            {
                throw new FileNotFoundException("Silero VAD model not found.");
            }
            _inference = new SileroVadInference(modelPath);
            _frameSamples16k = _inference.FrameSize;
            _frameSamples48k = _frameSamples16k * Decimation;
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException or InvalidOperationException)
        {
            _forcedBypass = true;
            _statusMessage = $"Voice Gate model missing ({ex.Message}); auto-bypassed.";
            return;
        }
        catch (Exception ex) when (ex is DllNotFoundException or BadImageFormatException)
        {
            _forcedBypass = true;
            _statusMessage = $"Voice Gate runtime error ({ex.Message}); auto-bypassed.";
            return;
        }

        InitializeBuffers(blockSize);
        EnsureSidechainBuffer(blockSize);
        UpdateCoefficients();
        StartWorker();
        _wasBypassed = false;
    }

    public void Process(Span<float> buffer, in PluginProcessContext context)
    {
        if (!ProcessCore(buffer))
        {
            return;
        }

        if (_speechBuffer.Length < buffer.Length || !context.AnalysisSignalWriter.IsEnabled)
        {
            return;
        }

        float vad = VadProbability;
        if (!float.IsFinite(vad))
        {
            vad = 0f;
        }
        vad = Math.Clamp(vad, 0f, 1f);
        var span = _speechBuffer.AsSpan(0, buffer.Length);
        span.Fill(vad);
        long writeTime = context.SampleTime - LatencySamples;
        context.AnalysisSignalWriter.WriteBlock(AnalysisSignalId.SpeechPresence, writeTime, span);
    }

    public void Process(Span<float> buffer)
    {
        ProcessCore(buffer);
    }

    public void SetParameter(int index, float value)
    {
        switch (index)
        {
            case ThresholdIndex:
                _threshold = Math.Clamp(value, 0.05f, 0.95f);
                break;
            case AttackIndex:
                _attackMs = Math.Clamp(value, 1f, 50f);
                UpdateCoefficients();
                break;
            case ReleaseIndex:
                _releaseMs = Math.Clamp(value, 20f, 500f);
                UpdateCoefficients();
                break;
            case HoldIndex:
                _holdMs = Math.Clamp(value, 0f, 300f);
                UpdateCoefficients();
                break;
        }
    }

    public byte[] GetState()
    {
        var bytes = new byte[sizeof(float) * 4];
        Buffer.BlockCopy(BitConverter.GetBytes(_threshold), 0, bytes, 0, 4);
        Buffer.BlockCopy(BitConverter.GetBytes(_attackMs), 0, bytes, 4, 4);
        Buffer.BlockCopy(BitConverter.GetBytes(_releaseMs), 0, bytes, 8, 4);
        Buffer.BlockCopy(BitConverter.GetBytes(_holdMs), 0, bytes, 12, 4);
        return bytes;
    }

    public void SetState(byte[] state)
    {
        if (state.Length < sizeof(float))
        {
            return;
        }

        _threshold = BitConverter.ToSingle(state, 0);
        if (state.Length >= sizeof(float) * 2)
        {
            _attackMs = BitConverter.ToSingle(state, 4);
        }
        if (state.Length >= sizeof(float) * 3)
        {
            _releaseMs = BitConverter.ToSingle(state, 8);
        }
        if (state.Length >= sizeof(float) * 4)
        {
            _holdMs = BitConverter.ToSingle(state, 12);
        }

        UpdateCoefficients();
    }

    public void Dispose()
    {
        StopWorker();
    }

    private void InitializeBuffers(int blockSize)
    {
        _frame48 = new float[_frameSamples48k];
        _frame16 = new float[_frameSamples16k];
        int ringCapacity = Math.Max(_frameSamples48k * 6, blockSize * 8);
        _inputBuffer = new LockFreeRingBuffer(ringCapacity);
        _decimatorFilter.Configure(LowPassCutoffHz, RequiredSampleRate);
        ResetState();
    }

    private bool ProcessCore(Span<float> buffer)
    {
        if (IsBypassed || _forcedBypass || _inputBuffer is null)
        {
            _wasBypassed = true;
            return false;
        }

        if (_wasBypassed)
        {
            ResetState();
            _wasBypassed = false;
        }

        _inputBuffer.Write(buffer);
        _frameSignal?.Set();

        float vad = VadProbability;
        if (!float.IsFinite(vad))
        {
            vad = 0f;
        }
        vad = Math.Clamp(vad, 0f, 1f);
        bool detected = vad >= _threshold;
        int holdLeft = detected ? _holdSamples : _holdSamplesLeft;
        float gate = _gate;
        float peakIn = 0f;
        float peakOut = 0f;

        for (int i = 0; i < buffer.Length; i++)
        {
            float input = buffer[i];
            float absIn = MathF.Abs(input);
            if (absIn > peakIn)
            {
                peakIn = absIn;
            }

            if (!detected && holdLeft > 0)
            {
                holdLeft--;
            }

            float target = (detected || holdLeft > 0) ? 1f : 0f;
            float coeff = target > gate ? _attackCoeff : _releaseCoeff;
            gate += coeff * (target - gate);
            float output = input * gate;
            buffer[i] = output;

            float absOut = MathF.Abs(output);
            if (absOut > peakOut)
            {
                peakOut = absOut;
            }
        }

        _gate = gate;
        _holdSamplesLeft = holdLeft;

        Interlocked.Exchange(ref _inputLevelBits, BitConverter.SingleToInt32Bits(peakIn));
        Interlocked.Exchange(ref _outputLevelBits, BitConverter.SingleToInt32Bits(peakOut));
        return true;
    }

    private void EnsureSidechainBuffer(int blockSize)
    {
        if (blockSize > 0 && _speechBuffer.Length != blockSize)
        {
            _speechBuffer = new float[blockSize];
        }
    }

    private void ResetState()
    {
        _inputBuffer?.Clear();
        _silenceFrames = 0;
        _holdSamplesLeft = 0;
        _gate = 0f;
        _decimatorFilter.Reset();
        _inference?.ResetState();
        Interlocked.Exchange(ref _vadBits, 0);
        Interlocked.Exchange(ref _inputLevelBits, 0);
        Interlocked.Exchange(ref _outputLevelBits, 0);
    }

    private void UpdateCoefficients()
    {
        if (_sampleRate <= 0)
        {
            return;
        }

        _attackCoeff = DspUtils.TimeToCoefficient(_attackMs, _sampleRate);
        _releaseCoeff = DspUtils.TimeToCoefficient(_releaseMs, _sampleRate);
        _holdSamples = (int)(_holdMs * 0.001f * _sampleRate);
    }

    private void StartWorker()
    {
        if (_inference is null || _inputBuffer is null)
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
            Interlocked.Exchange(ref _running, 1);
            _workerThread = new Thread(WorkerLoop)
            {
                IsBackground = true,
                Name = "SileroVadWorker"
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
            _inference?.Dispose();
            _inference = null;
        }
    }

    private void WorkerLoop()
    {
        if (_inference is null || _inputBuffer is null || _frameSignal is null)
        {
            return;
        }

        while (Volatile.Read(ref _running) == 1)
        {
            _frameSignal.WaitOne(10);

            while (_inputBuffer.AvailableRead >= _frameSamples48k)
            {
                int read = _inputBuffer.Read(_frame48);
                if (read < _frameSamples48k)
                {
                    break;
                }

                float energy = 0f;
                float maxAbs = 0f;
                for (int i = 0; i < _frame48.Length; i++)
                {
                    float sample = _frame48[i];
                    float abs = MathF.Abs(sample);
                    if (abs > maxAbs)
                    {
                        maxAbs = abs;
                    }
                    energy += sample * sample;
                }

                float power = energy / _frame48.Length;
                if (maxAbs <= 0f || power < SilencePowerThreshold)
                {
                    _silenceFrames++;
                    if (_silenceFrames >= SilenceFramesForReset)
                    {
                        _inference.ResetState();
                    }
                    Interlocked.Exchange(ref _vadBits, 0);
                    continue;
                }

                _silenceFrames = 0;
                int outIndex = 0;
                for (int i = 0; i < _frame48.Length; i++)
                {
                    float filtered = _decimatorFilter.Process(_frame48[i]);
                    if (i % Decimation == 0)
                    {
                        _frame16[outIndex++] = filtered;
                    }
                }

                if (outIndex < _frame16.Length)
                {
                    Array.Clear(_frame16, outIndex, _frame16.Length - outIndex);
                }

                float prob = _inference.Process(_frame16, out _);
                Interlocked.Exchange(ref _vadBits, BitConverter.SingleToInt32Bits(prob));
            }
        }
    }

    private static string ResolveModelPath()
    {
        string baseDir = AppContext.BaseDirectory;
        string primary = Path.Combine(baseDir, "Models", "silero-vad", "silero_vad.onnx");
        if (File.Exists(primary))
        {
            return primary;
        }

        string assets = Path.Combine(baseDir, "Assets", "Models", "silero-vad", "silero_vad.onnx");
        if (File.Exists(assets))
        {
            return assets;
        }

        return string.Empty;
    }

    private sealed class OnePoleLowPass
    {
        private float _a;
        private float _z;

        public void Configure(float cutoffHz, int sampleRate)
        {
            float x = MathF.Exp(-2f * MathF.PI * cutoffHz / sampleRate);
            _a = 1f - x;
        }

        public float Process(float input)
        {
            _z += _a * (input - _z);
            return _z;
        }

        public void Reset()
        {
            _z = 0f;
        }
    }
}
