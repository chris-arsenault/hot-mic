using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using HotMic.Core.Plugins;
using HotMic.Core.Threading;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace HotMic.Core.Plugins.BuiltIn;

public sealed class SpeechDenoiserPlugin : IPlugin, IPluginStatusProvider
{
    private const int RequiredSampleRate = 48000;
    private const int HopSize = 480;
    private const int FftSize = 960;
    private const int StateSize = 45304;
    private const int StartupSkipSamples = FftSize - HopSize;
    private const string ModelFileName = "denoiser_model.onnx";
    private const int StatusUpdateIntervalMs = 500;

    private readonly object _workerLock = new();
    private InferenceSession? _session;
    private LockFreeRingBuffer? _inputBuffer;
    private LockFreeRingBuffer? _outputBuffer;
    private float[] _inputFrame = Array.Empty<float>();
    private float[] _outputFrame = Array.Empty<float>();
    private float[] _processedScratch = Array.Empty<float>();
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
    private long _inputDropSamples;
    private long _outputUnderrunSamples;
    private long _startupSkipRemaining;
    private long _hopCounter;
    private float _lastLsnrDb;
    private string _statusMessage = string.Empty;
    private int _lastStatusTick;

    public SpeechDenoiserPlugin()
    {
        Parameters = Array.Empty<PluginParameter>();
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

        int written = _inputBuffer.Write(buffer);
        if (written < buffer.Length)
        {
            Interlocked.Add(ref _inputDropSamples, buffer.Length - written);
        }
        _frameSignal?.Set();

        DrainStartupSkip();
        if (_startupSkipRemaining > 0)
        {
            buffer.Clear();
            return;
        }

        int read = _outputBuffer.Read(_processedScratch.AsSpan(0, buffer.Length));
        if (read < buffer.Length)
        {
            Interlocked.Add(ref _outputUnderrunSamples, buffer.Length - read);
            _processedScratch.AsSpan(read, buffer.Length - read).Clear();
        }

        _processedScratch.AsSpan(0, buffer.Length).CopyTo(buffer);
    }

    public void SetParameter(int index, float value)
    {
        _ = index;
        _ = value;
    }

    public byte[] GetState()
    {
        return Array.Empty<byte>();
    }

    public void SetState(byte[] state)
    {
        _ = state;
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
        _lastLsnrDb = 0f;
        _hopCounter = 0;
        _inputDropSamples = 0;
        _outputUnderrunSamples = 0;
        _lastStatusTick = unchecked(Environment.TickCount - StatusUpdateIntervalMs);

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
                _hopCounter++;
            }

            UpdateStatusMessage();
        }
    }

    private void RunInference()
    {
        if (_session is null || _inputTensor is null || _stateTensor is null || _attenTensor is null)
        {
            return;
        }

        _atten[0] = 0f;

        var inputValue = NamedOnnxValue.CreateFromTensor("input_frame", _inputTensor);
        var stateValue = NamedOnnxValue.CreateFromTensor("states", _stateTensor);
        var attenValue = NamedOnnxValue.CreateFromTensor("atten_lim_db", _attenTensor);

        using var results = _session.Run(new[] { inputValue, stateValue, attenValue });
        var enhanced = GetTensor(results, "enhanced_audio_frame");
        var newStates = GetTensor(results, "new_states");
        var lsnr = GetTensor(results, "lsnr");

        CopyTensorToArray(enhanced, _outputFrame);
        CopyTensorToArray(newStates, _state);
        _lastLsnrDb = GetLastValue(lsnr);
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

    private void UpdateStatusMessage()
    {
        int now = Environment.TickCount;
        if (unchecked(now - _lastStatusTick) < StatusUpdateIntervalMs)
        {
            return;
        }
        _lastStatusTick = now;

        long dropped = Interlocked.Read(ref _inputDropSamples);
        long underrun = Interlocked.Read(ref _outputUnderrunSamples);
        long hops = Interlocked.Read(ref _hopCounter);

        string status = $"SpeechDenoiser | dropped {dropped} / short {underrun} | hops {hops} | lsnr {_lastLsnrDb:0.0}dB";
        Volatile.Write(ref _statusMessage, status);
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

    private static float GetLastValue(Tensor<float> tensor)
    {
        if (tensor is DenseTensor<float> dense)
        {
            var span = dense.Buffer.Span;
            return span.Length > 0 ? span[^1] : 0f;
        }

        float last = 0f;
        foreach (var value in tensor)
        {
            last = value;
        }
        return last;
    }
}
