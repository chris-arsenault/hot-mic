using System.Threading;
using HotMic.Common.Configuration;
using HotMic.Core.Dsp;
using HotMic.Core.Plugins;

namespace HotMic.Core.Plugins.BuiltIn;

public sealed class FFTNoiseRemovalPlugin : IPlugin, IQualityConfigurablePlugin
{
    public const int ReductionIndex = 0;

    // For UI visualization - use 64 bins for display
    public const int DisplayBins = 64;

    private const float SpectrumDecay = 0.85f;
    private const float PeakDecay = 0.98f;

    // Algorithm parameters
    private const float DecisionDirectedAlpha = 0.98f;
    private const float FrequencySmoothing = 0.35f;
    private const float TimeSmoothing = 0.4f;
    private const float OverSubMax = 2.5f;

    private int _fftSize = 1024;
    private int _hopSize = 256;
    private int _noiseFramesToLearn = 50;

    private float[] _inputRing = Array.Empty<float>();
    private float[] _outputRing = Array.Empty<float>();
    private float[] _window = Array.Empty<float>();
    private float[] _synthesisWindow = Array.Empty<float>();
    private float[] _fftReal = Array.Empty<float>();
    private float[] _fftImag = Array.Empty<float>();
    private float[] _noisePsd = Array.Empty<float>();
    private float[] _prevSnr = Array.Empty<float>();
    private float[] _prevGain = Array.Empty<float>();
    private float[] _gainBuffer = Array.Empty<float>();
    private FastFft? _fft;

    private Thread? _displayThread;
    private CancellationTokenSource? _displayCts;

    private readonly float[][] _displayInputReal = { Array.Empty<float>(), Array.Empty<float>() };
    private readonly float[][] _displayInputImag = { Array.Empty<float>(), Array.Empty<float>() };
    private readonly float[][] _displayOutputReal = { Array.Empty<float>(), Array.Empty<float>() };
    private readonly float[][] _displayOutputImag = { Array.Empty<float>(), Array.Empty<float>() };
    private readonly float[][] _displayNoisePsd = { Array.Empty<float>(), Array.Empty<float>() };
    private int _displaySnapshotIndex;
    private int _displaySnapshotPending;
    private int _displaySnapshotTarget = -1;
    private int[] _displayBinStart = Array.Empty<int>();
    private int[] _displayBinEnd = Array.Empty<int>();
    private float _displayNormFactor;
    private int _noiseProfileDirty;

    // Spectrum data for UI visualization (double-buffered).
    private readonly float[][] _inputSpectrumBuffers = { new float[DisplayBins], new float[DisplayBins] };
    private readonly float[][] _outputSpectrumBuffers = { new float[DisplayBins], new float[DisplayBins] };
    private readonly float[][] _noiseSpectrumBuffers = { new float[DisplayBins], new float[DisplayBins] };
    private readonly float[][] _inputPeakBuffers = { new float[DisplayBins], new float[DisplayBins] };
    private readonly float[][] _outputPeakBuffers = { new float[DisplayBins], new float[DisplayBins] };
    private int _displayIndex;

    private float _windowSum;
    private int _inputIndex;
    private int _hopCounter;
    private bool _learning;
    private int _noiseFrames;
    private float _reduction = 0.5f;
    private float _smoothedReduction = 0.5f;
    private int _sampleRate;
    private int _hasProfileFlag;

    public FFTNoiseRemovalPlugin()
    {
        ConfigureQuality(_fftSize, _hopSize, _noiseFramesToLearn);
        Parameters =
        [
            new PluginParameter { Index = ReductionIndex, Name = "Reduction", MinValue = 0f, MaxValue = 1f, DefaultValue = 0.5f, Unit = "%" }
        ];
    }

    public string Id => "builtin:fft-noise";

    public string Name => "FFT Noise Removal";

    public bool IsBypassed { get; set; }

    public int LatencySamples => Math.Max(0, _fftSize - _hopSize);

    public IReadOnlyList<PluginParameter> Parameters { get; }

    // Property getters for UI binding
    public float Reduction => _reduction;
    public bool IsLearning => _learning;
    public int LearningProgress => _noiseFrames;
    public int LearningTotal => _noiseFramesToLearn;
    public bool HasNoiseProfile => Volatile.Read(ref _hasProfileFlag) == 1;
    public int SampleRate => _sampleRate;

    /// <summary>
    /// Gets spectrum data for UI visualization. Caller must provide arrays of size DisplayBins.
    /// </summary>
    public void GetSpectrumData(float[] inputSpectrum, float[] inputPeaks,
                                 float[] outputSpectrum, float[] outputPeaks,
                                 float[] noiseProfile)
    {
        int index = Volatile.Read(ref _displayIndex);
        Array.Copy(_inputSpectrumBuffers[index], inputSpectrum, DisplayBins);
        Array.Copy(_inputPeakBuffers[index], inputPeaks, DisplayBins);
        Array.Copy(_outputSpectrumBuffers[index], outputSpectrum, DisplayBins);
        Array.Copy(_outputPeakBuffers[index], outputPeaks, DisplayBins);
        Array.Copy(_noiseSpectrumBuffers[index], noiseProfile, DisplayBins);
    }

    public void Initialize(int sampleRate, int blockSize)
    {
        _sampleRate = sampleRate;
        UpdateDisplayMapping();
        ResetState(preserveReduction: true);
        EnsureDisplayThread();
    }

    public void ApplyQuality(AudioQualityProfile profile)
    {
        ConfigureQuality(profile.NoiseFftSize, profile.NoiseHopSize, profile.NoiseLearnFrames);
    }

    public void LearnNoiseProfile()
    {
        StartLearning();
    }

    public void ToggleLearning()
    {
        if (_learning)
        {
            FinalizeNoiseProfile();
        }
        else
        {
            StartLearning();
        }
    }

    public void Process(Span<float> buffer, in PluginProcessContext context)
    {
        Process(buffer);
    }

    public void Process(Span<float> buffer)
    {
        if (IsBypassed)
        {
            return;
        }

        int ringSize = _inputRing.Length;
        if (ringSize == 0)
        {
            return;
        }

        for (int i = 0; i < buffer.Length; i++)
        {
            _inputRing[_inputIndex] = buffer[i];
            buffer[i] = _outputRing[_inputIndex];
            _outputRing[_inputIndex] = 0f;

            _inputIndex++;
            if (_inputIndex >= ringSize)
            {
                _inputIndex = 0;
            }

            _hopCounter++;
            if (_hopCounter >= _hopSize)
            {
                _hopCounter = 0;
                ProcessFrame();
            }
        }
    }

    public void SetParameter(int index, float value)
    {
        if (index == ReductionIndex)
        {
            _reduction = Math.Clamp(value, 0f, 1f);
        }
    }

    public byte[] GetState()
    {
        return BitConverter.GetBytes(_reduction);
    }

    public void SetState(byte[] state)
    {
        if (state.Length >= sizeof(float))
        {
            _reduction = BitConverter.ToSingle(state, 0);
        }
    }

    public void Dispose()
    {
        if (_displayThread is not null)
        {
            _displayCts?.Cancel();
            _displayThread.Join(500);
        }

        _displayThread = null;
        _displayCts?.Dispose();
        _displayCts = null;
    }

    private void ProcessFrame()
    {
        if (_fft is null || _fftReal.Length == 0)
        {
            return;
        }

        int start = _inputIndex;
        int fftSize = _fftSize;
        for (int i = 0; i < fftSize; i++)
        {
            int index = start + i;
            if (index >= fftSize)
            {
                index -= fftSize;
            }
            float sample = _inputRing[index] * _window[i];
            _fftReal[i] = sample;
            _fftImag[i] = 0f;
        }

        _fft.Forward(_fftReal, _fftImag);
        BeginDisplaySnapshot();

        if (_learning)
        {
            AccumulateNoiseProfile();
        }

        int bins = _noisePsd.Length;
        _smoothedReduction = _smoothedReduction * 0.9f + _reduction * 0.1f;
        bool applyReduction = _smoothedReduction > 1e-3f && Volatile.Read(ref _hasProfileFlag) == 1;
        float noiseScale = 1f + OverSubMax * _smoothedReduction;
        float minGain = DspUtils.DbToLinear(-60f * _smoothedReduction);

        for (int i = 0; i < bins; i++)
        {
            if (!applyReduction)
            {
                _gainBuffer[i] = 1f;
                continue;
            }

            float real = _fftReal[i];
            float imag = _fftImag[i];
            float mag2 = real * real + imag * imag;
            float noise = _noisePsd[i] * noiseScale + 1e-12f;
            float snrPost = mag2 / noise;

            // Decision-directed SNR drives a Wiener-style gain with a gentle floor.
            float snrPrior = DecisionDirectedAlpha * _prevSnr[i] +
                             (1f - DecisionDirectedAlpha) * MathF.Max(snrPost - 1f, 0f);
            _prevSnr[i] = snrPrior;

            float gain = snrPrior / (1f + snrPrior);
            _gainBuffer[i] = MathF.Max(gain, minGain);
        }

        // Smooth gains across frequency to reduce musical noise.
        for (int pass = 0; pass < 2; pass++)
        {
            for (int i = 1; i < bins - 1; i++)
            {
                _gainBuffer[i] = _gainBuffer[i] * (1f - FrequencySmoothing) +
                                 (_gainBuffer[i - 1] + _gainBuffer[i + 1]) * 0.5f * FrequencySmoothing;
            }
        }

        for (int i = 0; i < bins; i++)
        {
            float gain = _prevGain[i] * TimeSmoothing + _gainBuffer[i] * (1f - TimeSmoothing);
            _prevGain[i] = gain;
            _fftReal[i] *= gain;
            _fftImag[i] *= gain;
        }

        EndDisplaySnapshot();

        for (int i = 1; i < bins - 1; i++)
        {
            int mirror = _fftSize - i;
            _fftReal[mirror] = _fftReal[i];
            _fftImag[mirror] = -_fftImag[i];
        }

        _fft.Inverse(_fftReal, _fftImag);

        for (int i = 0; i < fftSize; i++)
        {
            int index = start + i;
            if (index >= fftSize)
            {
                index -= fftSize;
            }
            _outputRing[index] += _fftReal[i] * _synthesisWindow[i] / _windowSum;
        }
    }

    private void EnsureDisplayThread()
    {
        if (_displayThread is not null)
        {
            return;
        }

        _displayCts = new CancellationTokenSource();
        _displayThread = new Thread(() => DisplayLoop(_displayCts.Token))
        {
            IsBackground = true,
            Name = "HotMic-FFTNoiseDisplay"
        };
        _displayThread.Start();
    }

    private void DisplayLoop(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            if (Volatile.Read(ref _displaySnapshotPending) == 0)
            {
                Thread.Sleep(1);
                continue;
            }

            int snapshotIndex = Volatile.Read(ref _displaySnapshotIndex);
            Volatile.Write(ref _displaySnapshotPending, 0);

            if (_displayInputReal[snapshotIndex].Length == 0)
            {
                Thread.Sleep(1);
                continue;
            }

            UpdateDisplaySpectrum(_inputSpectrumBuffers, _inputPeakBuffers,
                _displayInputReal[snapshotIndex], _displayInputImag[snapshotIndex]);
            UpdateDisplaySpectrum(_outputSpectrumBuffers, _outputPeakBuffers,
                _displayOutputReal[snapshotIndex], _displayOutputImag[snapshotIndex]);

            if (Interlocked.Exchange(ref _noiseProfileDirty, 0) == 1)
            {
                UpdateNoiseProfileDisplay(_displayNoisePsd[snapshotIndex]);
            }

            PublishDisplayBuffers();
        }
    }

    private void BeginDisplaySnapshot()
    {
        _displaySnapshotTarget = -1;
        if (_displayInputReal[0].Length == 0)
        {
            return;
        }

        int current = Volatile.Read(ref _displaySnapshotIndex);
        int target = current == 0 ? 1 : 0;
        int bins = _displayInputReal[target].Length;
        if (bins == 0 || _fftReal.Length < bins)
        {
            return;
        }

        Array.Copy(_fftReal, _displayInputReal[target], bins);
        Array.Copy(_fftImag, _displayInputImag[target], bins);
        _displaySnapshotTarget = target;
    }

    private void EndDisplaySnapshot()
    {
        int target = _displaySnapshotTarget;
        if (target < 0)
        {
            return;
        }

        int bins = _displayOutputReal[target].Length;
        if (bins == 0 || _fftReal.Length < bins)
        {
            _displaySnapshotTarget = -1;
            return;
        }

        Array.Copy(_fftReal, _displayOutputReal[target], bins);
        Array.Copy(_fftImag, _displayOutputImag[target], bins);
        if (_noisePsd.Length >= bins)
        {
            Array.Copy(_noisePsd, _displayNoisePsd[target], bins);
        }

        Volatile.Write(ref _displaySnapshotIndex, target);
        Volatile.Write(ref _displaySnapshotPending, 1);
        _displaySnapshotTarget = -1;
    }

    private void UpdateDisplayMapping()
    {
        if (_displayBinStart.Length != DisplayBins || _fftSize <= 0)
        {
            return;
        }

        int fftBins = _fftSize / 2 + 1;
        float minFreq = 20f;
        float maxFreq = _sampleRate > 0 ? _sampleRate / 2f : 20000f;
        float binWidth = _sampleRate > 0 ? _sampleRate / (float)_fftSize : maxFreq / Math.Max(1, fftBins - 1);
        float ratio = maxFreq / minFreq;

        for (int displayBin = 0; displayBin < DisplayBins; displayBin++)
        {
            float t0 = displayBin / (float)DisplayBins;
            float t1 = (displayBin + 1) / (float)DisplayBins;
            float freq0 = minFreq * MathF.Pow(ratio, t0);
            float freq1 = minFreq * MathF.Pow(ratio, t1);

            int bin0 = Math.Clamp((int)(freq0 / binWidth), 0, fftBins - 1);
            int bin1 = Math.Clamp((int)(freq1 / binWidth), bin0 + 1, fftBins);
            _displayBinStart[displayBin] = bin0;
            _displayBinEnd[displayBin] = bin1;
        }
    }

    private void ConfigureQuality(int fftSize, int hopSize, int noiseFrames)
    {
        int size = NextPowerOfTwo(Math.Max(256, fftSize));
        int hop = Math.Clamp(hopSize, 1, size - 1);
        _fftSize = size;
        _hopSize = hop;
        _noiseFramesToLearn = Math.Max(1, noiseFrames);

        _inputRing = new float[_fftSize];
        _outputRing = new float[_fftSize];
        _window = new float[_fftSize];
        _synthesisWindow = new float[_fftSize];
        _fftReal = new float[_fftSize];
        _fftImag = new float[_fftSize];

        int bins = _fftSize / 2 + 1;
        _noisePsd = new float[bins];
        _prevSnr = new float[bins];
        _prevGain = new float[bins];
        _gainBuffer = new float[bins];
        _displayInputReal[0] = new float[bins];
        _displayInputReal[1] = new float[bins];
        _displayInputImag[0] = new float[bins];
        _displayInputImag[1] = new float[bins];
        _displayOutputReal[0] = new float[bins];
        _displayOutputReal[1] = new float[bins];
        _displayOutputImag[0] = new float[bins];
        _displayOutputImag[1] = new float[bins];
        _displayNoisePsd[0] = new float[bins];
        _displayNoisePsd[1] = new float[bins];
        _displayBinStart = new int[DisplayBins];
        _displayBinEnd = new int[DisplayBins];
        _displayNormFactor = 2f / _fftSize;

        for (int i = 0; i < _fftSize; i++)
        {
            _window[i] = 0.5f - 0.5f * MathF.Cos(2f * MathF.PI * i / (_fftSize - 1));
            _synthesisWindow[i] = _window[i];
        }

        _fft = new FastFft(_fftSize);
        _windowSum = CalculateWindowSum();

        UpdateDisplayMapping();
        ResetState(preserveReduction: true);
    }

    private void ResetState(bool preserveReduction)
    {
        _inputIndex = 0;
        _hopCounter = 0;
        _learning = false;
        _noiseFrames = 0;
        _displayIndex = 0;
        _displaySnapshotIndex = 0;
        _displaySnapshotPending = 0;
        _displaySnapshotTarget = -1;
        Interlocked.Exchange(ref _noiseProfileDirty, 1);
        Volatile.Write(ref _hasProfileFlag, 0);

        if (!preserveReduction)
        {
            _reduction = 0.5f;
        }
        _smoothedReduction = _reduction;

        Array.Clear(_inputRing, 0, _inputRing.Length);
        Array.Clear(_outputRing, 0, _outputRing.Length);
        Array.Clear(_fftReal, 0, _fftReal.Length);
        Array.Clear(_fftImag, 0, _fftImag.Length);
        Array.Clear(_noisePsd, 0, _noisePsd.Length);
        Array.Fill(_prevSnr, 0f);
        Array.Fill(_prevGain, 1f);
        Array.Fill(_gainBuffer, 1f);
        ClearDisplayBuffers();
    }

    private void StartLearning()
    {
        _learning = true;
        _noiseFrames = 0;
        Volatile.Write(ref _hasProfileFlag, 0);
        Interlocked.Exchange(ref _noiseProfileDirty, 1);
        Array.Clear(_noisePsd, 0, _noisePsd.Length);
        Array.Fill(_prevSnr, 0f);
        Array.Fill(_prevGain, 1f);
    }

    private void AccumulateNoiseProfile()
    {
        for (int i = 0; i < _noisePsd.Length; i++)
        {
            float real = _fftReal[i];
            float imag = _fftImag[i];
            _noisePsd[i] += real * real + imag * imag;
        }

        _noiseFrames++;
        if (_noiseFrames >= _noiseFramesToLearn)
        {
            FinalizeNoiseProfile();
        }
    }

    private void FinalizeNoiseProfile()
    {
        if (_noiseFrames <= 0)
        {
            _learning = false;
            return;
        }

        float inv = 1f / _noiseFrames;
        for (int i = 0; i < _noisePsd.Length; i++)
        {
            _noisePsd[i] *= inv;
        }

        Interlocked.Exchange(ref _noiseProfileDirty, 1);
        _learning = false;
        Volatile.Write(ref _hasProfileFlag, 1);
    }

    private float CalculateWindowSum()
    {
        var overlap = new float[_fftSize];
        for (int frame = 0; frame < _fftSize; frame += _hopSize)
        {
            for (int i = 0; i < _fftSize; i++)
            {
                int index = frame + i;
                if (index >= _fftSize)
                {
                    index -= _fftSize;
                }
                overlap[index] += _window[i] * _synthesisWindow[i];
            }
        }

        float sum = 0f;
        for (int i = 0; i < overlap.Length; i++)
        {
            sum += overlap[i];
        }

        return sum / overlap.Length;
    }

    private void PublishDisplayBuffers()
    {
        int current = Volatile.Read(ref _displayIndex);
        int next = current == 0 ? 1 : 0;
        Volatile.Write(ref _displayIndex, next);
    }

    private void UpdateDisplaySpectrum(float[][] spectrumBuffers, float[][] peakBuffers, float[] fftReal, float[] fftImag)
    {
        int current = Volatile.Read(ref _displayIndex);
        int target = current == 0 ? 1 : 0;
        float[] spectrum = spectrumBuffers[target];
        float[] peaks = peakBuffers[target];

        for (int displayBin = 0; displayBin < DisplayBins; displayBin++)
        {
            int bin0 = _displayBinStart[displayBin];
            int bin1 = _displayBinEnd[displayBin];

            double sumSq = 0.0;
            int count = 0;
            int maxBin = Math.Min(bin1, fftReal.Length);
            for (int i = bin0; i < maxBin; i++)
            {
                float real = fftReal[i];
                float imag = fftImag[i];
                double mag = Math.Sqrt(real * real + imag * imag);
                sumSq += mag * mag;
                count++;
            }

            float magnitude = count > 0 ? (float)(Math.Sqrt(sumSq / count) * _displayNormFactor) : 0f;
            spectrum[displayBin] = MathF.Max(spectrum[displayBin] * SpectrumDecay, magnitude);

            if (magnitude > peaks[displayBin])
            {
                peaks[displayBin] = magnitude;
            }
            else
            {
                peaks[displayBin] *= PeakDecay;
            }
        }
    }

    private void UpdateNoiseProfileDisplay(float[] noisePsd)
    {
        int current = Volatile.Read(ref _displayIndex);
        int target = current == 0 ? 1 : 0;
        float[] profile = _noiseSpectrumBuffers[target];

        for (int displayBin = 0; displayBin < DisplayBins; displayBin++)
        {
            int bin0 = _displayBinStart[displayBin];
            int bin1 = _displayBinEnd[displayBin];

            double sumSq = 0.0;
            int count = 0;
            int maxBin = Math.Min(bin1, noisePsd.Length);
            for (int i = bin0; i < maxBin; i++)
            {
                double mag = Math.Sqrt(noisePsd[i]);
                sumSq += mag * mag;
                count++;
            }

            profile[displayBin] = count > 0 ? (float)(Math.Sqrt(sumSq / count) * _displayNormFactor) : 0f;
        }
    }

    private void ClearDisplayBuffers()
    {
        for (int i = 0; i < _inputSpectrumBuffers.Length; i++)
        {
            Array.Clear(_inputSpectrumBuffers[i], 0, _inputSpectrumBuffers[i].Length);
            Array.Clear(_outputSpectrumBuffers[i], 0, _outputSpectrumBuffers[i].Length);
            Array.Clear(_noiseSpectrumBuffers[i], 0, _noiseSpectrumBuffers[i].Length);
            Array.Clear(_inputPeakBuffers[i], 0, _inputPeakBuffers[i].Length);
            Array.Clear(_outputPeakBuffers[i], 0, _outputPeakBuffers[i].Length);
        }
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
}
