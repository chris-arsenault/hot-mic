using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using HotMic.Core.Dsp;
using HotMic.Core.Plugins;
using HotMic.Core.Threading;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace HotMic.Core.Plugins.BuiltIn;

public sealed class SpeechDenoiserPlugin : IPlugin, IPluginStatusProvider
{
    public const int DryWetIndex = 0;
    public const int AttenLimitIndex = 1;
    public const int AttenEnableIndex = 2;

    private const int RequiredSampleRate = 48000;
    private const int HopSize = 480;
    private const int FftSize = 960;
    private const int StateSize = 45304;
    private const int StartupSkipSamples = FftSize - HopSize;
    private const string ModelFileName = "denoiser_model.onnx";
    private const float MixSmoothingMs = 8f;

    private readonly object _workerLock = new();
    private InferenceSession? _session;
    private LockFreeRingBuffer? _inputBuffer;
    private LockFreeRingBuffer? _outputBuffer;
    private float[] _inputFrame = Array.Empty<float>();
    private float[] _outputFrame = Array.Empty<float>();
    private float[] _processedScratch = Array.Empty<float>();
    private float[] _dryRing = Array.Empty<float>();
    private int _dryIndex;
    private float[] _state = Array.Empty<float>();
    private float[] _atten = Array.Empty<float>();
    private DenseTensor<float>? _inputTensor;
    private DenseTensor<float>? _stateTensor;
    private DenseTensor<float>? _attenTensor;
    private Thread? _workerThread;
    private AutoResetEvent? _frameSignal;
    private int _running;
    private int _sampleRate;
    private bool _forcedBypass;
    private bool _wasBypassed = true;
    private float _dryWetPercent = 100f;
    private float _attenLimitDb = 100f;
    private bool _attenEnabled;
    private LinearSmoother _mixSmoother = new();
    private long _startupSkipRemaining;
    private string _statusMessage = string.Empty;

    public SpeechDenoiserPlugin()
    {
        Parameters =
        [
            new PluginParameter
            {
                Index = DryWetIndex,
                Name = "Dry/Wet",
                MinValue = 0f,
                MaxValue = 100f,
                DefaultValue = 100f,
                Unit = "%"
            },
            new PluginParameter
            {
                Index = AttenLimitIndex,
                Name = "Attenuation Limit",
                MinValue = 0f,
                MaxValue = 100f,
                DefaultValue = 100f,
                Unit = "dB"
            },
            new PluginParameter
            {
                Index = AttenEnableIndex,
                Name = "Attenuation Enable",
                MinValue = 0f,
                MaxValue = 1f,
                DefaultValue = 0f,
                Unit = ""
            }
        ];
    }

    public string Id => "builtin:speechdenoiser";

    public string Name => "Speech Denoiser";

    public bool IsBypassed { get; set; }

    public int LatencySamples => _forcedBypass ? 0 : StartupSkipSamples;

    public IReadOnlyList<PluginParameter> Parameters { get; }

    public string StatusMessage => Volatile.Read(ref _statusMessage);

    public void Initialize(int sampleRate, int blockSize)
    {
        _sampleRate = sampleRate;
        _statusMessage = string.Empty;
        _forcedBypass = false;

        StopWorker();
        ReleaseSession();

        if (sampleRate != RequiredSampleRate)
        {
            _forcedBypass = true;
            _statusMessage = "Speech Denoiser requires 48kHz; auto-bypassed.";
            return;
        }

        string modelPath = ResolveModelPath();
        if (string.IsNullOrEmpty(modelPath))
        {
            _forcedBypass = true;
            _statusMessage = "Speech Denoiser model not found; auto-bypassed.";
            return;
        }

        try
        {
            _session = new InferenceSession(modelPath, BuildSessionOptions());
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException or InvalidOperationException)
        {
            _forcedBypass = true;
            _statusMessage = $"Speech Denoiser model error ({ex.Message}); auto-bypassed.";
            return;
        }
        catch (Exception ex) when (ex is DllNotFoundException or BadImageFormatException)
        {
            _forcedBypass = true;
            _statusMessage = $"Speech Denoiser runtime error ({ex.Message}); auto-bypassed.";
            return;
        }

        InitializeBuffers(blockSize);
        ResetState(clearBuffers: true);
        _mixSmoother.Configure(sampleRate, MixSmoothingMs, _dryWetPercent / 100f);
        StartWorker();
        _wasBypassed = false;
    }

    public void Process(Span<float> buffer)
    {
        if (IsBypassed || _forcedBypass || _session is null || _inputBuffer is null || _outputBuffer is null)
        {
            _wasBypassed = true;
            return;
        }

        if (_wasBypassed)
        {
            ResetState(clearBuffers: true);
            _wasBypassed = false;
        }

        _inputBuffer.Write(buffer);
        _frameSignal?.Set();

        DrainStartupSkip();

        int read = 0;
        if (_startupSkipRemaining <= 0)
        {
            read = _outputBuffer.Read(_processedScratch.AsSpan(0, buffer.Length));
            if (read < buffer.Length)
            {
                _processedScratch.AsSpan(read, buffer.Length - read).Clear();
            }
        }
        else
        {
            _processedScratch.AsSpan(0, buffer.Length).Clear();
        }

        float mix = _mixSmoother.Current;
        bool smoothing = _mixSmoother.IsSmoothing;

        for (int i = 0; i < buffer.Length; i++)
        {
            if (smoothing)
            {
                mix = _mixSmoother.Next();
                smoothing = _mixSmoother.IsSmoothing;
            }

            float input = buffer[i];
            float dry = _dryRing[_dryIndex];
            float wet = i < read ? _processedScratch[i] : 0f;
            buffer[i] = dry * (1f - mix) + wet * mix;

            _dryRing[_dryIndex] = input;
            _dryIndex++;
            if (_dryIndex >= _dryRing.Length)
            {
                _dryIndex = 0;
            }
        }
    }

    public void SetParameter(int index, float value)
    {
        if (index == DryWetIndex)
        {
            _dryWetPercent = Math.Clamp(value, 0f, 100f);
            if (_sampleRate > 0)
            {
                _mixSmoother.SetTarget(_dryWetPercent / 100f);
            }
            return;
        }

        if (index == AttenLimitIndex)
        {
            Volatile.Write(ref _attenLimitDb, Math.Clamp(value, 0f, 100f));
            return;
        }

        if (index == AttenEnableIndex)
        {
            Volatile.Write(ref _attenEnabled, value >= 0.5f);
        }
    }

    public byte[] GetState()
    {
        var bytes = new byte[sizeof(float) * 3];
        Buffer.BlockCopy(BitConverter.GetBytes(_dryWetPercent), 0, bytes, 0, 4);
        Buffer.BlockCopy(BitConverter.GetBytes(Volatile.Read(ref _attenLimitDb)), 0, bytes, 4, 4);
        Buffer.BlockCopy(BitConverter.GetBytes(Volatile.Read(ref _attenEnabled) ? 1f : 0f), 0, bytes, 8, 4);
        return bytes;
    }

    public void SetState(byte[] state)
    {
        if (state.Length < sizeof(float))
        {
            return;
        }

        _dryWetPercent = Math.Clamp(BitConverter.ToSingle(state, 0), 0f, 100f);
        if (state.Length >= sizeof(float) * 2)
        {
            Volatile.Write(ref _attenLimitDb, Math.Clamp(BitConverter.ToSingle(state, 4), 0f, 100f));
        }
        if (state.Length >= sizeof(float) * 3)
        {
            Volatile.Write(ref _attenEnabled, BitConverter.ToSingle(state, 8) >= 0.5f);
        }
        if (_sampleRate > 0)
        {
            _mixSmoother.SetTarget(_dryWetPercent / 100f);
        }
    }

    public void Dispose()
    {
        StopWorker();
        ReleaseSession();
    }

    private void InitializeBuffers(int blockSize)
    {
        _inputFrame = new float[HopSize];
        _outputFrame = new float[HopSize];
        _processedScratch = new float[blockSize];
        _dryRing = new float[Math.Max(1, StartupSkipSamples)];
        _dryIndex = 0;
        _state = new float[StateSize];
        _atten = new float[1];
        _inputTensor = new DenseTensor<float>(_inputFrame, new[] { HopSize });
        _stateTensor = new DenseTensor<float>(_state, new[] { StateSize });
        _attenTensor = new DenseTensor<float>(_atten, new[] { 1 });

        int ringCapacity = Math.Max(HopSize * 8, blockSize * 4);
        _inputBuffer = new LockFreeRingBuffer(ringCapacity);
        _outputBuffer = new LockFreeRingBuffer(ringCapacity);
    }

    private void ResetState(bool clearBuffers)
    {
        if (_state.Length > 0)
        {
            Array.Clear(_state, 0, _state.Length);
        }
        if (_atten.Length > 0)
        {
            _atten[0] = 0f;
        }
        _startupSkipRemaining = StartupSkipSamples;
        _dryIndex = 0;
        if (_dryRing.Length > 0)
        {
            Array.Clear(_dryRing, 0, _dryRing.Length);
        }

        if (clearBuffers)
        {
            _inputBuffer?.Clear();
            _outputBuffer?.Clear();
            if (_processedScratch.Length > 0)
            {
                Array.Clear(_processedScratch, 0, _processedScratch.Length);
            }
        }
    }

    private void StartWorker()
    {
        if (_session is null || _inputBuffer is null || _outputBuffer is null)
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
                Name = "SpeechDenoiserWorker"
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
        }
    }

    private void WorkerLoop()
    {
        if (_session is null || _inputBuffer is null || _outputBuffer is null || _frameSignal is null)
        {
            return;
        }

        while (Volatile.Read(ref _running) == 1)
        {
            _frameSignal.WaitOne(10);

            while (_inputBuffer.AvailableRead >= HopSize)
            {
                int read = _inputBuffer.Read(_inputFrame);
                if (read < HopSize)
                {
                    break;
                }

                RunInference();
                _outputBuffer.Write(_outputFrame);
            }
        }
    }

    private void RunInference()
    {
        if (_session is null || _inputTensor is null || _stateTensor is null || _attenTensor is null)
        {
            return;
        }

        bool attenEnabled = Volatile.Read(ref _attenEnabled);
        float attenLimit = Volatile.Read(ref _attenLimitDb);
        _atten[0] = attenEnabled ? attenLimit : 0f;

        var inputValue = NamedOnnxValue.CreateFromTensor("input_frame", _inputTensor);
        var stateValue = NamedOnnxValue.CreateFromTensor("states", _stateTensor);
        var attenValue = NamedOnnxValue.CreateFromTensor("atten_lim_db", _attenTensor);

        using var results = _session.Run(new[] { inputValue, stateValue, attenValue });
        var enhanced = GetTensor(results, "enhanced_audio_frame");
        var newStates = GetTensor(results, "new_states");

        CopyTensorToArray(enhanced, _outputFrame);
        CopyTensorToArray(newStates, _state);
    }

    private void DrainStartupSkip()
    {
        if (_startupSkipRemaining <= 0 || _outputBuffer is null)
        {
            return;
        }

        int scratchLimit = _processedScratch.Length;
        if (scratchLimit == 0)
        {
            return;
        }

        while (_startupSkipRemaining > 0)
        {
            int available = _outputBuffer.AvailableRead;
            if (available <= 0)
            {
                break;
            }

            int toSkip = (int)Math.Min(_startupSkipRemaining, Math.Min(available, scratchLimit));
            int skipped = _outputBuffer.Read(_processedScratch.AsSpan(0, toSkip));
            if (skipped <= 0)
            {
                break;
            }

            _startupSkipRemaining -= skipped;
        }
    }

    private static SessionOptions BuildSessionOptions()
    {
        return new SessionOptions
        {
            GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_EXTENDED,
            ExecutionMode = ExecutionMode.ORT_SEQUENTIAL,
            InterOpNumThreads = 1,
            IntraOpNumThreads = 1
        };
    }

    private static string ResolveModelPath()
    {
        string baseDir = AppContext.BaseDirectory;
        string primary = Path.Combine(baseDir, "Models", "speechdenoiser", ModelFileName);
        if (File.Exists(primary))
        {
            return primary;
        }

        string assets = Path.Combine(baseDir, "Assets", "Models", "speechdenoiser", ModelFileName);
        if (File.Exists(assets))
        {
            return assets;
        }

        return string.Empty;
    }

    private void ReleaseSession()
    {
        _session?.Dispose();
        _session = null;
    }

    private static Tensor<float> GetTensor(IDisposableReadOnlyCollection<DisposableNamedOnnxValue> results, string name)
    {
        foreach (var item in results)
        {
            if (item.Name == name)
            {
                return item.AsTensor<float>();
            }
        }

        throw new InvalidOperationException($"ONNX output '{name}' not found.");
    }

    private static void CopyTensorToArray(Tensor<float> tensor, float[] destination)
    {
        if (tensor is DenseTensor<float> dense)
        {
            var span = dense.Buffer.Span;
            int copyCount = Math.Min(destination.Length, span.Length);
            span.Slice(0, copyCount).CopyTo(destination);
            if (copyCount < destination.Length)
            {
                Array.Clear(destination, copyCount, destination.Length - copyCount);
            }
            return;
        }

        int index = 0;
        foreach (var value in tensor)
        {
            if (index >= destination.Length)
            {
                break;
            }
            destination[index++] = value;
        }
        if (index < destination.Length)
        {
            Array.Clear(destination, index, destination.Length - index);
        }
    }

}
