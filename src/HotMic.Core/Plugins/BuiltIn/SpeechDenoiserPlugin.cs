using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
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
    private const int StateSize = 45304;
    // Measured DFN3 model latency via Learn Latency (48kHz). Update if model changes.
    private const int StartupSkipSamples = 1387;
    private const string ModelFileName = "denoiser_model.onnx";
    private const float MixSmoothingMs = 8f;
    private const float LatencyProbeDurationSeconds = 0.5f;
    private const float LatencyProbeStartHz = 100f;
    private const float LatencyProbeEndHz = 8000f;
    private const int LatencyProbeLeadInSamples = RequiredSampleRate / 10;
    private const int LatencyProbeTailSamples = RequiredSampleRate / 10;
    private const int LatencyProbeReferenceOffsetSamples = RequiredSampleRate / 10;
    private const int LatencyProbeCorrelationSamples = RequiredSampleRate / 5;
    private const int LatencyProbeMaxLagSamples = 8192;
    private const float LatencyProbeMinConfidence = 0.1f;
    private const float LatencyProbePeakRatio = 1.05f;

    private readonly object _workerLock = new();
    private InferenceSession? _session;
    private LockFreeRingBuffer? _inputBuffer;
    private LockFreeRingBuffer? _outputBuffer;
    private float[] _inputFrame = Array.Empty<float>();
    private float[] _outputFrame = Array.Empty<float>();
    private float[] _processedScratch = Array.Empty<float>();
    private float[] _dryRing = Array.Empty<float>();
    private int _dryRingMask;
    private long _inputSampleIndex;
    private long _nextFrameSampleIndex;
    private long _alignedInputIndex;
    private bool _hasAlignedIndex;
    private long _currentFrameStart;
    private int _currentFrameOffset;
    private bool _hasCurrentFrame;
    private FrameIndexQueue? _frameQueue;
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
    private int _modelLatencySamples = StartupSkipSamples;
    private int _pendingReset;
    private int _latencyLearningActive;
    private LinearSmoother _mixSmoother = new();
    private string _statusMessage = string.Empty;
    private string _latencyReport = string.Empty;

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

    public int LatencySamples => _forcedBypass ? 0 : Volatile.Read(ref _modelLatencySamples);

    public IReadOnlyList<PluginParameter> Parameters { get; }

    public string StatusMessage => Volatile.Read(ref _statusMessage);

    public string LatencyReport => Volatile.Read(ref _latencyReport);

    public bool IsLatencyLearning => Volatile.Read(ref _latencyLearningActive) == 1;

    public void Initialize(int sampleRate, int blockSize)
    {
        _sampleRate = sampleRate;
        _statusMessage = string.Empty;
        _latencyReport = string.Empty;
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

    public void Process(Span<float> buffer, in PluginProcessContext context)
    {
        Process(buffer);
    }

    public void Process(Span<float> buffer)
    {
        if (IsBypassed || _forcedBypass || _session is null || _inputBuffer is null || _outputBuffer is null)
        {
            _wasBypassed = true;
            return;
        }

        if (Interlocked.Exchange(ref _pendingReset, 0) == 1)
        {
            ResetState(clearBuffers: true);
            _wasBypassed = false;
        }

        if (_wasBypassed)
        {
            ResetState(clearBuffers: true);
            _wasBypassed = false;
        }

        _inputBuffer.Write(buffer);
        _frameSignal?.Set();

        int read = _outputBuffer.Read(_processedScratch.AsSpan(0, buffer.Length));
        if (read < buffer.Length)
        {
            _processedScratch.AsSpan(read, buffer.Length - read).Clear();
        }

        float mix = _mixSmoother.Current;
        bool smoothing = _mixSmoother.IsSmoothing;
        int wetIndex = 0;

        for (int i = 0; i < buffer.Length; i++)
        {
            if (smoothing)
            {
                mix = _mixSmoother.Next();
                smoothing = _mixSmoother.IsSmoothing;
            }

            float input = buffer[i];
            long inputIndex = _inputSampleIndex;
            _dryRing[(int)(inputIndex & _dryRingMask)] = input;
            _inputSampleIndex = inputIndex + 1;

            float wet = 0f;
            if (wetIndex < read)
            {
                wet = _processedScratch[wetIndex++];
                // Align dry to the input timeline associated with each wet frame.
                if (!_hasCurrentFrame || _currentFrameOffset >= HopSize)
                {
                    if (_frameQueue is not null && _frameQueue.TryDequeue(out _currentFrameStart))
                    {
                        _currentFrameOffset = 0;
                        _hasCurrentFrame = true;
                    }
                    else
                    {
                        _hasCurrentFrame = false;
                    }
                }

                if (_hasCurrentFrame)
                {
                    long alignedIndex = _currentFrameStart + _currentFrameOffset;
                    _currentFrameOffset++;
                    if (alignedIndex >= 0)
                    {
                        _alignedInputIndex = alignedIndex;
                        _hasAlignedIndex = true;
                    }
                    else
                    {
                        _hasAlignedIndex = false;
                        wet = 0f;
                    }
                }
                else
                {
                    wet = 0f;
                }
            }
            else if (_hasAlignedIndex)
            {
                _alignedInputIndex++;
            }

            float dry = 0f;
            if (_hasAlignedIndex)
            {
                long dryIndex = _alignedInputIndex;
                long oldest = _inputSampleIndex - _dryRing.Length;
                if (dryIndex >= oldest && dryIndex >= 0 && dryIndex < _inputSampleIndex)
                {
                    dry = _dryRing[(int)(dryIndex & _dryRingMask)];
                }
            }

            buffer[i] = dry * (1f - mix) + wet * mix;
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

    /// <summary>
    /// Measures the model's intrinsic latency and applies it for the current session.
    /// </summary>
    public void LearnLatency()
    {
        if (_forcedBypass)
        {
            Volatile.Write(ref _latencyReport, "Latency learn unavailable (requires 48kHz).");
            return;
        }

        if (Interlocked.Exchange(ref _latencyLearningActive, 1) == 1)
        {
            return;
        }

        Volatile.Write(ref _latencyReport, "Measuring latency...");
        Task.Run(MeasureLatencyWorker);
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
        int dryRingSize = NextPowerOfTwo(Math.Max(StartupSkipSamples + ringCapacity + blockSize, HopSize * 16));
        _dryRing = new float[dryRingSize];
        _dryRingMask = dryRingSize - 1;
        _inputBuffer = new LockFreeRingBuffer(ringCapacity);
        _outputBuffer = new LockFreeRingBuffer(ringCapacity);
        int frameQueueCapacity = Math.Max(32, ringCapacity / HopSize + 8);
        _frameQueue = new FrameIndexQueue(frameQueueCapacity);
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
        _inputSampleIndex = 0;
        _nextFrameSampleIndex = 0;
        _alignedInputIndex = 0;
        _hasAlignedIndex = false;
        _currentFrameStart = 0;
        _currentFrameOffset = 0;
        _hasCurrentFrame = false;
        if (_dryRing.Length > 0)
        {
            Array.Clear(_dryRing, 0, _dryRing.Length);
        }
        _frameQueue?.Clear();

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
                int modelLatency = Volatile.Read(ref _modelLatencySamples);
                long frameStart = _nextFrameSampleIndex - modelLatency;
                _nextFrameSampleIndex += read;
                if (_frameQueue is null || !_frameQueue.TryEnqueue(frameStart))
                {
                    continue;
                }
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

    private void MeasureLatencyWorker()
    {
        try
        {
            string modelPath = ResolveModelPath();
            if (string.IsNullOrEmpty(modelPath))
            {
                Volatile.Write(ref _latencyReport, "Latency learn failed: model not found.");
                return;
            }

            using var session = new InferenceSession(modelPath, BuildSessionOptions());

            float[] probe = GenerateChirp(RequiredSampleRate, LatencyProbeStartHz, LatencyProbeEndHz, LatencyProbeDurationSeconds);
            float[] input = new float[LatencyProbeLeadInSamples + probe.Length + LatencyProbeTailSamples];
            Array.Copy(probe, 0, input, LatencyProbeLeadInSamples, probe.Length);

            float[] output = ProcessSignalThroughModel(session, input);
            if (output.Length <= LatencyProbeLeadInSamples + LatencyProbeTailSamples)
            {
                Volatile.Write(ref _latencyReport, "Latency undetectable (output too short).");
                return;
            }

            int referenceOffset = Math.Min(LatencyProbeReferenceOffsetSamples, Math.Max(0, probe.Length - 1));
            int referenceLength = Math.Min(LatencyProbeCorrelationSamples, Math.Max(0, probe.Length - referenceOffset));
            int referenceStart = LatencyProbeLeadInSamples + referenceOffset;
            if (referenceLength <= 0 || referenceStart + referenceLength >= input.Length)
            {
                Volatile.Write(ref _latencyReport, "Latency undetectable (invalid probe window).");
                return;
            }

            int maxLag = Math.Min(LatencyProbeMaxLagSamples, output.Length - referenceStart - referenceLength);
            if (maxLag <= 0)
            {
                Volatile.Write(ref _latencyReport, "Latency undetectable (output too short).");
                return;
            }

            var reference = input.AsSpan(referenceStart, referenceLength);
            var search = output.AsSpan(referenceStart);
            if (TryFindCorrelationPeak(reference, search, maxLag, out int latencySamples, out float confidence))
            {
                Volatile.Write(ref _modelLatencySamples, latencySamples);
                Volatile.Write(ref _latencyReport, $"Measured latency: {latencySamples} samples ({latencySamples * 1000f / RequiredSampleRate:0.0} ms)");
                Interlocked.Exchange(ref _pendingReset, 1);
            }
            else
            {
                Volatile.Write(ref _latencyReport, "Latency undetectable (low confidence).");
            }
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException or InvalidOperationException)
        {
            Volatile.Write(ref _latencyReport, "Latency learn failed: model error.");
        }
        catch (Exception ex) when (ex is DllNotFoundException or BadImageFormatException)
        {
            Volatile.Write(ref _latencyReport, "Latency learn failed: runtime error.");
        }
        catch (Exception ex)
        {
            Volatile.Write(ref _latencyReport, $"Latency learn failed ({ex.Message}).");
        }
        finally
        {
            Interlocked.Exchange(ref _latencyLearningActive, 0);
        }
    }

    private static float[] ProcessSignalThroughModel(InferenceSession session, float[] input)
    {
        float[] state = new float[StateSize];
        float[] atten = new float[1];
        float[] inputFrame = new float[HopSize];
        float[] outputFrame = new float[HopSize];
        var inputTensor = new DenseTensor<float>(inputFrame, new[] { HopSize });
        var stateTensor = new DenseTensor<float>(state, new[] { StateSize });
        var attenTensor = new DenseTensor<float>(atten, new[] { 1 });

        int frameCount = (input.Length + HopSize - 1) / HopSize;
        float[] output = new float[frameCount * HopSize];
        int outputOffset = 0;

        for (int frame = 0; frame < frameCount; frame++)
        {
            int offset = frame * HopSize;
            int copyCount = Math.Min(HopSize, input.Length - offset);
            Array.Clear(inputFrame, 0, inputFrame.Length);
            Array.Copy(input, offset, inputFrame, 0, copyCount);

            var inputValue = NamedOnnxValue.CreateFromTensor("input_frame", inputTensor);
            var stateValue = NamedOnnxValue.CreateFromTensor("states", stateTensor);
            var attenValue = NamedOnnxValue.CreateFromTensor("atten_lim_db", attenTensor);

            using var results = session.Run(new[] { inputValue, stateValue, attenValue });
            var enhanced = GetTensor(results, "enhanced_audio_frame");
            var newStates = GetTensor(results, "new_states");

            CopyTensorToArray(enhanced, outputFrame);
            CopyTensorToArray(newStates, state);

            Array.Copy(outputFrame, 0, output, outputOffset, HopSize);
            outputOffset += HopSize;
        }

        return output;
    }

    private static float[] GenerateChirp(int sampleRate, float startHz, float endHz, float durationSeconds)
    {
        int length = Math.Max(1, (int)(sampleRate * durationSeconds));
        float[] signal = new float[length];
        float amplitude = 0.35f;
        float duration = Math.Max(1e-6f, durationSeconds);
        float k = (endHz - startHz) / duration;
        float twoPi = 2f * MathF.PI;

        for (int i = 0; i < length; i++)
        {
            float t = i / (float)sampleRate;
            float phase = twoPi * (startHz * t + 0.5f * k * t * t);
            signal[i] = amplitude * MathF.Sin(phase);
        }

        int fadeSamples = Math.Min(length / 4, sampleRate / 200); // 5 ms max
        if (fadeSamples > 0)
        {
            for (int i = 0; i < fadeSamples; i++)
            {
                float fade = i / (float)fadeSamples;
                signal[i] *= fade;
                signal[length - 1 - i] *= fade;
            }
        }

        return signal;
    }

    private static bool TryFindCorrelationPeak(ReadOnlySpan<float> reference, ReadOnlySpan<float> target, int maxLag, out int delay, out float confidence)
    {
        delay = 0;
        confidence = 0f;

        if (reference.Length == 0 || target.Length <= reference.Length)
        {
            return false;
        }

        maxLag = Math.Min(maxLag, target.Length - reference.Length);
        if (maxLag <= 0)
        {
            return false;
        }

        float refEnergy = 0f;
        for (int i = 0; i < reference.Length; i++)
        {
            float sample = reference[i];
            refEnergy += sample * sample;
        }
        if (refEnergy <= 1e-10f)
        {
            return false;
        }

        float best = float.NegativeInfinity;
        float secondBest = float.NegativeInfinity;
        int bestLag = 0;

        for (int lag = 0; lag <= maxLag; lag++)
        {
            float sum = 0f;
            for (int i = 0; i < reference.Length; i++)
            {
                sum += reference[i] * target[lag + i];
            }
            float magnitude = MathF.Abs(sum);
            if (magnitude > best)
            {
                secondBest = best;
                best = magnitude;
                bestLag = lag;
            }
            else if (magnitude > secondBest)
            {
                secondBest = magnitude;
            }
        }

        float targetEnergy = 0f;
        for (int i = 0; i < reference.Length; i++)
        {
            float sample = target[bestLag + i];
            targetEnergy += sample * sample;
        }

        if (targetEnergy <= 1e-10f)
        {
            return false;
        }

        confidence = best / MathF.Sqrt(refEnergy * targetEnergy);
        if (confidence < LatencyProbeMinConfidence)
        {
            return false;
        }

        if (secondBest > 0f && best / secondBest < LatencyProbePeakRatio)
        {
            return false;
        }

        delay = bestLag;
        return true;
    }

    private static int NextPowerOfTwo(int value)
    {
        int power = 1;
        while (power < value)
        {
            power <<= 1;
        }
        return power;
    }

    private sealed class FrameIndexQueue
    {
        private readonly long[] _buffer;
        private readonly int _mask;
        private long _writeIndex;
        private long _readIndex;

        public FrameIndexQueue(int capacity)
        {
            int size = NextPowerOfTwo(Math.Max(1, capacity));
            _buffer = new long[size];
            _mask = size - 1;
        }

        public bool TryEnqueue(long value)
        {
            long write = Volatile.Read(ref _writeIndex);
            long read = Volatile.Read(ref _readIndex);
            if (write - read >= _buffer.Length)
            {
                return false;
            }

            _buffer[(int)(write & _mask)] = value;
            Volatile.Write(ref _writeIndex, write + 1);
            return true;
        }

        public bool TryDequeue(out long value)
        {
            long write = Volatile.Read(ref _writeIndex);
            long read = Volatile.Read(ref _readIndex);
            if (write - read <= 0)
            {
                value = 0;
                return false;
            }

            value = _buffer[(int)(read & _mask)];
            Volatile.Write(ref _readIndex, read + 1);
            return true;
        }

        public void Clear()
        {
            Volatile.Write(ref _writeIndex, 0);
            Volatile.Write(ref _readIndex, 0);
        }
    }
}
