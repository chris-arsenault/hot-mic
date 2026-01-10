using MathNet.Numerics;
using MathNet.Numerics.IntegralTransforms;

namespace HotMic.Core.Plugins.BuiltIn;

public sealed class FFTNoiseRemovalPlugin : IPlugin
{
    public const int ReductionIndex = 0;
    public const int SensitivityIndex = 1;

    private const int FftSize = 2048;
    private const int HopSize = FftSize / 2;
    private const int NoiseFramesToLearn = 30;

    private readonly float[] _inputRing = new float[FftSize];
    private readonly float[] _outputRing = new float[FftSize];
    private readonly float[] _window = new float[FftSize];
    private readonly Complex32[] _fftBuffer = new Complex32[FftSize];
    private readonly float[] _noiseProfile = new float[FftSize / 2 + 1];
    private readonly float[] _frameBuffer = new float[FftSize];

    private int _inputIndex;
    private int _hopCounter;
    private bool _learning;
    private int _noiseFrames;
    private float _reduction = 0.5f;
    private float _sensitivityDb = -60f;
    private float _sensitivityLinear;

    public FFTNoiseRemovalPlugin()
    {
        for (int i = 0; i < FftSize; i++)
        {
            _window[i] = 0.5f - 0.5f * MathF.Cos(2f * MathF.PI * i / (FftSize - 1));
        }

        Parameters =
        [
            new PluginParameter { Index = ReductionIndex, Name = "Reduction", MinValue = 0f, MaxValue = 1f, DefaultValue = 0.5f, Unit = "%" },
            new PluginParameter { Index = SensitivityIndex, Name = "Sensitivity", MinValue = -80f, MaxValue = 0f, DefaultValue = -60f, Unit = "dB" }
        ];

        UpdateSensitivity();
    }

    public string Id => "builtin:fft-noise";

    public string Name => "FFT Noise Removal";

    public bool IsBypassed { get; set; }

    public IReadOnlyList<PluginParameter> Parameters { get; }

    public void Initialize(int sampleRate, int blockSize)
    {
    }

    public void LearnNoiseProfile()
    {
        _learning = true;
        _noiseFrames = 0;
        Array.Clear(_noiseProfile);
    }

    public void Process(Span<float> buffer)
    {
        if (IsBypassed)
        {
            return;
        }

        for (int i = 0; i < buffer.Length; i++)
        {
            _inputRing[_inputIndex] = buffer[i];
            buffer[i] = _outputRing[_inputIndex];
            _outputRing[_inputIndex] = 0f;

            _inputIndex = (_inputIndex + 1) % FftSize;
            _hopCounter++;

            if (_hopCounter >= HopSize)
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
                _reduction = Math.Clamp(value, 0f, 1f);
                break;
            case SensitivityIndex:
                _sensitivityDb = value;
                UpdateSensitivity();
                break;
        }
    }

    public byte[] GetState()
    {
        var bytes = new byte[sizeof(float) * 2];
        Buffer.BlockCopy(BitConverter.GetBytes(_reduction), 0, bytes, 0, 4);
        Buffer.BlockCopy(BitConverter.GetBytes(_sensitivityDb), 0, bytes, 4, 4);
        return bytes;
    }

    public void SetState(byte[] state)
    {
        if (state.Length < sizeof(float) * 2)
        {
            return;
        }

        _reduction = BitConverter.ToSingle(state, 0);
        _sensitivityDb = BitConverter.ToSingle(state, 4);
        UpdateSensitivity();
    }

    public void Dispose()
    {
    }

    private void ProcessFrame()
    {
        int start = _inputIndex;
        for (int i = 0; i < FftSize; i++)
        {
            int index = (start + i) % FftSize;
            float sample = _inputRing[index] * _window[i];
            _frameBuffer[i] = sample;
            _fftBuffer[i] = new Complex32(sample, 0f);
        }

        Fourier.Forward(_fftBuffer, FourierOptions.Matlab);

        if (_learning)
        {
            AccumulateNoiseProfile();
        }

        for (int i = 0; i < _noiseProfile.Length; i++)
        {
            float real = _fftBuffer[i].Real;
            float imag = _fftBuffer[i].Imaginary;
            float magnitude = MathF.Sqrt(real * real + imag * imag);
            if (magnitude < _sensitivityLinear)
            {
                continue;
            }

            float reduced = MathF.Max(0f, magnitude - _noiseProfile[i] * _reduction);
            if (magnitude > 0f)
            {
                float scale = reduced / magnitude;
                _fftBuffer[i] = new Complex32(real * scale, imag * scale);
            }
        }

        for (int i = 1; i < _noiseProfile.Length - 1; i++)
        {
            int mirror = FftSize - i;
            _fftBuffer[mirror] = new Complex32(_fftBuffer[i].Real, -_fftBuffer[i].Imaginary);
        }

        Fourier.Inverse(_fftBuffer, FourierOptions.Matlab);

        for (int i = 0; i < FftSize; i++)
        {
            float sample = _fftBuffer[i].Real / FftSize;
            int index = (start + i) % FftSize;
            _outputRing[index] += sample * _window[i];
        }
    }

    private void AccumulateNoiseProfile()
    {
        for (int i = 0; i < _noiseProfile.Length; i++)
        {
            float real = _fftBuffer[i].Real;
            float imag = _fftBuffer[i].Imaginary;
            float magnitude = MathF.Sqrt(real * real + imag * imag);
            _noiseProfile[i] += magnitude;
        }

        _noiseFrames++;
        if (_noiseFrames >= NoiseFramesToLearn)
        {
            for (int i = 0; i < _noiseProfile.Length; i++)
            {
                _noiseProfile[i] /= _noiseFrames;
            }

            _learning = false;
        }
    }

    private void UpdateSensitivity()
    {
        _sensitivityLinear = MathF.Pow(10f, _sensitivityDb / 20f);
    }
}
