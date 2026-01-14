using System.Threading;
using HotMic.Core.Dsp;
using HotMic.Core.Threading;

namespace HotMic.Core.Plugins.BuiltIn;

/// <summary>
/// Real-time frequency analyzer with configurable FFT size and display bins.
/// Pass-through plugin (no audio modification).
/// </summary>
public sealed class FrequencyAnalyzerPlugin : IPlugin
{
    public const int FftSizeIndex = 0;
    public const int DisplayBinsIndex = 1;
    public const int ScaleIndex = 2;
    public const int MinFrequencyIndex = 3;
    public const int MaxFrequencyIndex = 4;
    public const int MinDbIndex = 5;
    public const int MaxDbIndex = 6;

    private static readonly int[] FftSizes = { 1024, 2048, 4096, 8192 };
    private static readonly int[] DisplayBinsOptions = { 32, 64, 128, 256 };

    private const float DefaultMinFrequency = 80f;
    private const float DefaultMaxFrequency = 8000f;
    private const float DefaultMinDb = -80f;
    private const float DefaultMaxDb = 0f;
    private const int CaptureBufferSize = 65536;

    private readonly LockFreeRingBuffer _captureBuffer = new(CaptureBufferSize);
    private readonly SpectrumMapper _mapper = new();
    private readonly float[][] _spectrumBuffers = { Array.Empty<float>(), Array.Empty<float>() };

    private Thread? _analysisThread;
    private CancellationTokenSource? _analysisCts;

    private int _sampleRate;
    private int _displayIndex;
    private int _activeFftSize;
    private int _activeDisplayBins;
    private int _activeHopSize;
    private FrequencyScale _activeScale;
    private float _activeMinFrequency;
    private float _activeMaxFrequency;

    private FastFft? _fft;
    private float[] _fftReal = Array.Empty<float>();
    private float[] _fftImag = Array.Empty<float>();
    private float[] _fftWindow = Array.Empty<float>();
    private float[] _fftMagnitudes = Array.Empty<float>();
    private float _fftNormalization = 1f;
    private float[] _analysisBuffer = Array.Empty<float>();
    private float[] _hopBuffer = Array.Empty<float>();
    private float[] _displayScratch = Array.Empty<float>();

    private int _analysisActive;
    private int _requestedFftSize = 2048;
    private int _requestedDisplayBins = 128;
    private int _requestedScale = (int)FrequencyScale.Mel;
    private float _requestedMinFrequency = DefaultMinFrequency;
    private float _requestedMaxFrequency = DefaultMaxFrequency;
    private float _requestedMinDb = DefaultMinDb;
    private float _requestedMaxDb = DefaultMaxDb;

    public FrequencyAnalyzerPlugin()
    {
        Parameters =
        [
            new PluginParameter
            {
                Index = FftSizeIndex,
                Name = "FFT Size",
                MinValue = 1024f,
                MaxValue = 8192f,
                DefaultValue = 2048f,
                Unit = "samples",
                FormatValue = value => FormatDiscrete(value, FftSizes, "")
            },
            new PluginParameter
            {
                Index = DisplayBinsIndex,
                Name = "Display Bins",
                MinValue = 32f,
                MaxValue = 256f,
                DefaultValue = 128f,
                Unit = "bins",
                FormatValue = value => FormatDiscrete(value, DisplayBinsOptions, "")
            },
            new PluginParameter
            {
                Index = ScaleIndex,
                Name = "Scale",
                MinValue = 0f,
                MaxValue = 4f,
                DefaultValue = (float)FrequencyScale.Mel,
                Unit = "",
                FormatValue = value => ((FrequencyScale)Math.Clamp((int)MathF.Round(value), 0, 4)).ToString()
            },
            new PluginParameter
            {
                Index = MinFrequencyIndex,
                Name = "Min Freq",
                MinValue = 20f,
                MaxValue = 2000f,
                DefaultValue = DefaultMinFrequency,
                Unit = "Hz"
            },
            new PluginParameter
            {
                Index = MaxFrequencyIndex,
                Name = "Max Freq",
                MinValue = 2000f,
                MaxValue = 12000f,
                DefaultValue = DefaultMaxFrequency,
                Unit = "Hz"
            },
            new PluginParameter
            {
                Index = MinDbIndex,
                Name = "Min dB",
                MinValue = -120f,
                MaxValue = -20f,
                DefaultValue = DefaultMinDb,
                Unit = "dB"
            },
            new PluginParameter
            {
                Index = MaxDbIndex,
                Name = "Max dB",
                MinValue = -40f,
                MaxValue = 0f,
                DefaultValue = DefaultMaxDb,
                Unit = "dB"
            }
        ];
    }

    public string Id => "builtin:freq-analyzer";

    public string Name => "Frequency Analyzer";

    public bool IsBypassed { get; set; }

    public int LatencySamples => 0;

    public IReadOnlyList<PluginParameter> Parameters { get; }

    public int SampleRate => _sampleRate;

    public int FftSize => Volatile.Read(ref _activeFftSize);

    public int DisplayBins => Volatile.Read(ref _activeDisplayBins);

    public FrequencyScale Scale => (FrequencyScale)Math.Clamp(Volatile.Read(ref _requestedScale), 0, 4);

    public float MinFrequency => Volatile.Read(ref _requestedMinFrequency);

    public float MaxFrequency => Volatile.Read(ref _requestedMaxFrequency);

    public float MinDb => Volatile.Read(ref _requestedMinDb);

    public float MaxDb => Volatile.Read(ref _requestedMaxDb);

    public void Initialize(int sampleRate, int blockSize)
    {
        _sampleRate = sampleRate;
        ConfigureAnalysis(force: true);
        EnsureAnalysisThread();
    }

    public void Process(Span<float> buffer)
    {
        if (buffer.IsEmpty)
        {
            return;
        }

        _captureBuffer.Write(buffer);
    }

    public void SetParameter(int index, float value)
    {
        switch (index)
        {
            case FftSizeIndex:
                Interlocked.Exchange(ref _requestedFftSize, SelectDiscrete(value, FftSizes));
                break;
            case DisplayBinsIndex:
                Interlocked.Exchange(ref _requestedDisplayBins, SelectDiscrete(value, DisplayBinsOptions));
                break;
            case ScaleIndex:
                Interlocked.Exchange(ref _requestedScale, Math.Clamp((int)MathF.Round(value), 0, 4));
                break;
            case MinFrequencyIndex:
            {
                float max = Volatile.Read(ref _requestedMaxFrequency);
                float next = Math.Clamp(value, 20f, MathF.Max(100f, max - 10f));
                Interlocked.Exchange(ref _requestedMinFrequency, next);
                break;
            }
            case MaxFrequencyIndex:
            {
                float min = Volatile.Read(ref _requestedMinFrequency);
                float next = Math.Clamp(value, MathF.Min(20000f, min + 10f), 20000f);
                Interlocked.Exchange(ref _requestedMaxFrequency, next);
                break;
            }
            case MinDbIndex:
            {
                float max = Volatile.Read(ref _requestedMaxDb);
                float next = Math.Clamp(value, -120f, MathF.Min(-1f, max - 1f));
                Interlocked.Exchange(ref _requestedMinDb, next);
                break;
            }
            case MaxDbIndex:
            {
                float min = Volatile.Read(ref _requestedMinDb);
                float next = Math.Clamp(value, MathF.Max(-120f, min + 1f), 0f);
                Interlocked.Exchange(ref _requestedMaxDb, next);
                break;
            }
        }
    }

    public byte[] GetState()
    {
        var bytes = new byte[sizeof(float) * 7];
        Buffer.BlockCopy(BitConverter.GetBytes((float)_requestedFftSize), 0, bytes, 0, 4);
        Buffer.BlockCopy(BitConverter.GetBytes((float)_requestedDisplayBins), 0, bytes, 4, 4);
        Buffer.BlockCopy(BitConverter.GetBytes((float)_requestedScale), 0, bytes, 8, 4);
        Buffer.BlockCopy(BitConverter.GetBytes(_requestedMinFrequency), 0, bytes, 12, 4);
        Buffer.BlockCopy(BitConverter.GetBytes(_requestedMaxFrequency), 0, bytes, 16, 4);
        Buffer.BlockCopy(BitConverter.GetBytes(_requestedMinDb), 0, bytes, 20, 4);
        Buffer.BlockCopy(BitConverter.GetBytes(_requestedMaxDb), 0, bytes, 24, 4);
        return bytes;
    }

    public void SetState(byte[] state)
    {
        if (state.Length < sizeof(float) * 7)
        {
            return;
        }

        SetParameter(FftSizeIndex, BitConverter.ToSingle(state, 0));
        SetParameter(DisplayBinsIndex, BitConverter.ToSingle(state, 4));
        SetParameter(ScaleIndex, BitConverter.ToSingle(state, 8));
        SetParameter(MinFrequencyIndex, BitConverter.ToSingle(state, 12));
        SetParameter(MaxFrequencyIndex, BitConverter.ToSingle(state, 16));
        SetParameter(MinDbIndex, BitConverter.ToSingle(state, 20));
        SetParameter(MaxDbIndex, BitConverter.ToSingle(state, 24));
    }

    public void Dispose()
    {
        if (_analysisThread is not null)
        {
            _analysisCts?.Cancel();
            _analysisThread.Join(500);
        }
        _analysisThread = null;
        _analysisCts?.Dispose();
        _analysisCts = null;
    }

    /// <summary>
    /// Enable or disable analysis updates (used by the visualization window).
    /// </summary>
    public void SetVisualizationActive(bool active)
    {
        Volatile.Write(ref _analysisActive, active ? 1 : 0);
        if (active)
        {
            _captureBuffer.Clear();
        }
    }

    /// <summary>
    /// Copy the current spectrum (normalized 0..1) into the provided array.
    /// </summary>
    public void GetSpectrum(float[] spectrum)
    {
        int index = Volatile.Read(ref _displayIndex);
        var buffer = _spectrumBuffers[index];
        if (buffer.Length == 0 || spectrum.Length < buffer.Length)
        {
            return;
        }

        Array.Copy(buffer, spectrum, buffer.Length);
    }

    /// <summary>
    /// Copy the display-bin center frequencies into the provided array.
    /// </summary>
    public void GetBinFrequencies(float[] frequencies)
    {
        var centers = _mapper.CenterFrequencies;
        if (frequencies.Length < centers.Length)
        {
            return;
        }

        for (int i = 0; i < centers.Length; i++)
        {
            frequencies[i] = centers[i];
        }
    }

    private void EnsureAnalysisThread()
    {
        if (_analysisThread is not null)
        {
            return;
        }

        _analysisCts = new CancellationTokenSource();
        _analysisThread = new Thread(() => AnalysisLoop(_analysisCts.Token))
        {
            IsBackground = true,
            Name = "HotMic-FrequencyAnalyzer"
        };
        _analysisThread.Start();
    }

    private void AnalysisLoop(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            if (Volatile.Read(ref _analysisActive) == 0)
            {
                Thread.Sleep(20);
                continue;
            }

            ConfigureAnalysis(force: false);

            if (_captureBuffer.AvailableRead < _activeHopSize)
            {
                Thread.Sleep(1);
                continue;
            }

            int read = _captureBuffer.Read(_hopBuffer);
            if (read < _activeHopSize)
            {
                Thread.Sleep(1);
                continue;
            }

            int shift = _activeHopSize;
            int tail = _activeFftSize - shift;
            Array.Copy(_analysisBuffer, shift, _analysisBuffer, 0, tail);
            for (int i = 0; i < shift; i++)
            {
                _analysisBuffer[tail + i] = _hopBuffer[i];
            }

            for (int i = 0; i < _activeFftSize; i++)
            {
                _fftReal[i] = _analysisBuffer[i] * _fftWindow[i];
                _fftImag[i] = 0f;
            }

            _fft?.Forward(_fftReal, _fftImag);

            int half = _activeFftSize / 2;
            float normalization = _fftNormalization;
            for (int i = 0; i < half; i++)
            {
                float re = _fftReal[i];
                float im = _fftImag[i];
                _fftMagnitudes[i] = MathF.Sqrt(re * re + im * im) * normalization;
            }

            _mapper.MapMax(_fftMagnitudes, _displayScratch);

            float minDb = Volatile.Read(ref _requestedMinDb);
            float maxDb = Volatile.Read(ref _requestedMaxDb);
            float range = MathF.Max(1f, maxDb - minDb);

            int targetIndex = 1 - Volatile.Read(ref _displayIndex);
            var target = _spectrumBuffers[targetIndex];

            for (int i = 0; i < target.Length; i++)
            {
                float db = DspUtils.LinearToDb(_displayScratch[i]);
                target[i] = Math.Clamp((db - minDb) / range, 0f, 1f);
            }

            Volatile.Write(ref _displayIndex, targetIndex);
        }
    }

    private void ConfigureAnalysis(bool force)
    {
        int fftSize = Volatile.Read(ref _requestedFftSize);
        int displayBins = Volatile.Read(ref _requestedDisplayBins);
        var scale = (FrequencyScale)Math.Clamp(Volatile.Read(ref _requestedScale), 0, 4);
        float minHz = Volatile.Read(ref _requestedMinFrequency);
        float maxHz = Volatile.Read(ref _requestedMaxFrequency);

        fftSize = SelectDiscrete(fftSize, FftSizes);
        displayBins = SelectDiscrete(displayBins, DisplayBinsOptions);

        bool needsResize = force || fftSize != _activeFftSize || displayBins != _activeDisplayBins;
        bool mappingChanged = force
                              || scale != _activeScale
                              || MathF.Abs(minHz - _activeMinFrequency) > 1e-3f
                              || MathF.Abs(maxHz - _activeMaxFrequency) > 1e-3f;
        if (needsResize)
        {
            _activeFftSize = fftSize;
            _activeDisplayBins = displayBins;
            _activeHopSize = fftSize / 4; // 75% overlap

            _fft = new FastFft(fftSize);
            _fftReal = new float[fftSize];
            _fftImag = new float[fftSize];
            _fftWindow = new float[fftSize];
            _analysisBuffer = new float[fftSize];
            _fftMagnitudes = new float[fftSize / 2];
            _hopBuffer = new float[_activeHopSize];
            _displayScratch = new float[displayBins];
            _spectrumBuffers[0] = new float[displayBins];
            _spectrumBuffers[1] = new float[displayBins];
            _fftNormalization = 2f / MathF.Max(1f, fftSize);

            WindowFunctions.Fill(_fftWindow, WindowFunction.Hann);
        }

        if (needsResize || mappingChanged)
        {
            _mapper.Configure(_activeFftSize, _sampleRate, _activeDisplayBins, minHz, maxHz, scale);
            _activeScale = scale;
            _activeMinFrequency = minHz;
            _activeMaxFrequency = maxHz;
            Array.Clear(_analysisBuffer);
            Array.Clear(_spectrumBuffers[0]);
            Array.Clear(_spectrumBuffers[1]);
            Volatile.Write(ref _displayIndex, 0);
        }
    }

    private static int SelectDiscrete(float value, IReadOnlyList<int> options)
    {
        int best = options[0];
        float bestDelta = MathF.Abs(options[0] - value);
        for (int i = 1; i < options.Count; i++)
        {
            float delta = MathF.Abs(options[i] - value);
            if (delta < bestDelta)
            {
                bestDelta = delta;
                best = options[i];
            }
        }
        return best;
    }

    private static string FormatDiscrete(float value, IReadOnlyList<int> options, string suffix)
    {
        int selected = SelectDiscrete(value, options);
        return string.IsNullOrWhiteSpace(suffix) ? selected.ToString() : $"{selected}{suffix}";
    }
}
