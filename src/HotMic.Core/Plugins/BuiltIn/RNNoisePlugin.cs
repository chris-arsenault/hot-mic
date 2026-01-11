using System.Threading;
using HotMic.Common.Configuration;
using HotMic.Core.Dsp;
using HotMic.Core.Plugins;

namespace HotMic.Core.Plugins.BuiltIn;

public sealed class RNNoisePlugin : IPlugin, IQualityConfigurablePlugin, IPluginStatusProvider
{
    public const int ReductionIndex = 0;
    public const int VadThresholdIndex = 1;

    private const int FrameSize = 480;
    private const float MixSmoothingMs = 8f;

    private readonly float[] _inputRing = new float[FrameSize];
    private readonly float[] _outputRing = new float[FrameSize];
    private readonly float[] _dryRing = new float[FrameSize];
    private readonly float[] _frameIn = new float[FrameSize];
    private readonly float[] _frameOut = new float[FrameSize];
    private LinearSmoother _mixSmoother = new();

    private IntPtr _state = IntPtr.Zero;
    private int _ringIndex;
    private int _hopCounter;
    private int _sampleRate;
    private float _reductionPct = 100f;
    private float _vadThresholdPct;
    private bool _wasBypassed = true;
    private bool _forcedBypass;
    private string _statusMessage = string.Empty;

    private int _vadBits;

    public RNNoisePlugin()
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
                Index = VadThresholdIndex,
                Name = "VAD Threshold",
                MinValue = 0f,
                MaxValue = 100f,
                DefaultValue = 0f,
                Unit = "%"
            }
        ];
    }

    public string Id => "builtin:rnnoise";

    public string Name => "RNNoise";

    public bool IsBypassed { get; set; }

    public int LatencySamples => _forcedBypass ? 0 : FrameSize;

    public IReadOnlyList<PluginParameter> Parameters { get; }

    public string StatusMessage => _statusMessage;

    public void Initialize(int sampleRate, int blockSize)
    {
        _sampleRate = sampleRate;
        _statusMessage = string.Empty;
        _forcedBypass = false;
        ReleaseState();
        ResetState(clearBuffers: true);

        if (sampleRate != 48000)
        {
            _forcedBypass = true;
            _statusMessage = "RNNoise requires 48kHz; auto-bypassed.";
            return;
        }

        try
        {
            RNNoiseInterop.TryLoadFromBaseDirectory();
            _state = RNNoiseInterop.rnnoise_create(IntPtr.Zero);
            if (_state == IntPtr.Zero)
            {
                _forcedBypass = true;
                _statusMessage = "RNNoise failed to initialize; auto-bypassed.";
                return;
            }
        }
        catch (DllNotFoundException)
        {
            _forcedBypass = true;
            _statusMessage = "RNNoise DLL not found; auto-bypassed.";
            return;
        }
        catch (BadImageFormatException)
        {
            _forcedBypass = true;
            _statusMessage = "RNNoise DLL incompatible; auto-bypassed.";
            return;
        }

        _mixSmoother.Configure(sampleRate, MixSmoothingMs, _reductionPct / 100f);
        _wasBypassed = false;
    }

    public void Process(Span<float> buffer)
    {
        if (IsBypassed || _forcedBypass || _state == IntPtr.Zero)
        {
            _wasBypassed = true;
            return;
        }

        if (_wasBypassed)
        {
            ResetState(clearBuffers: true);
            _wasBypassed = false;
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
            float dry = _dryRing[_ringIndex];
            float wet = _outputRing[_ringIndex];
            _outputRing[_ringIndex] = 0f;

            buffer[i] = dry * (1f - mix) + wet * mix;

            _dryRing[_ringIndex] = input;
            _inputRing[_ringIndex] = input;

            _ringIndex++;
            if (_ringIndex >= FrameSize)
            {
                _ringIndex = 0;
            }

            _hopCounter++;
            if (_hopCounter >= FrameSize)
            {
                _hopCounter = 0;
                ProcessFrame();
            }
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
            case VadThresholdIndex:
                _vadThresholdPct = Math.Clamp(value, 0f, 100f);
                break;
        }
    }

    public float VadProbability
    {
        get => BitConverter.Int32BitsToSingle(Interlocked.CompareExchange(ref _vadBits, 0, 0));
    }

    public byte[] GetState()
    {
        var bytes = new byte[sizeof(float) * 2];
        Buffer.BlockCopy(BitConverter.GetBytes(_reductionPct), 0, bytes, 0, 4);
        Buffer.BlockCopy(BitConverter.GetBytes(_vadThresholdPct), 0, bytes, 4, 4);
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
            _vadThresholdPct = BitConverter.ToSingle(state, 4);
        }

        if (_sampleRate > 0)
        {
            _mixSmoother.SetTarget(_reductionPct / 100f);
        }
    }

    public void ApplyQuality(AudioQualityProfile profile)
    {
        // RNNoise frame size is fixed; quality mode has no effect.
    }

    public void Dispose()
    {
        ReleaseState();
    }

    private void ProcessFrame()
    {
        int start = _ringIndex;
        float maxAbs = 0f;
        for (int i = 0; i < FrameSize; i++)
        {
            int index = start + i;
            if (index >= FrameSize)
            {
                index -= FrameSize;
            }
            float sample = _inputRing[index];
            _frameIn[i] = sample;
            float abs = MathF.Abs(sample);
            if (abs > maxAbs)
            {
                maxAbs = abs;
            }
        }

        if (maxAbs <= 0f)
        {
            Interlocked.Exchange(ref _vadBits, 0);
            return;
        }

        float vad = RNNoiseInterop.rnnoise_process_frame(_state, _frameOut, _frameIn);
        Interlocked.Exchange(ref _vadBits, BitConverter.SingleToInt32Bits(vad));

        if (vad < _vadThresholdPct / 100f)
        {
            Array.Copy(_frameIn, _frameOut, FrameSize);
        }

        for (int i = 0; i < FrameSize; i++)
        {
            int index = start + i;
            if (index >= FrameSize)
            {
                index -= FrameSize;
            }
            _outputRing[index] = _frameOut[i];
        }
    }

    private void ResetState(bool clearBuffers)
    {
        _ringIndex = 0;
        _hopCounter = 0;
        Interlocked.Exchange(ref _vadBits, 0);
        if (clearBuffers)
        {
            Array.Clear(_inputRing, 0, _inputRing.Length);
            Array.Clear(_outputRing, 0, _outputRing.Length);
            Array.Clear(_dryRing, 0, _dryRing.Length);
            Array.Clear(_frameIn, 0, _frameIn.Length);
            Array.Clear(_frameOut, 0, _frameOut.Length);
        }
    }

    private void ReleaseState()
    {
        if (_state == IntPtr.Zero)
        {
            return;
        }

        RNNoiseInterop.rnnoise_destroy(_state);
        _state = IntPtr.Zero;
    }
}
