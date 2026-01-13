using System.Threading;
using HotMic.Core.Dsp;
using HotMic.Core.Threading;

namespace HotMic.Core.Plugins.BuiltIn;

/// <summary>
/// Vocal-focused spectrograph analysis with pitch, formant, voicing, and harmonic overlays.
/// Pass-through plugin (no audio modification).
/// </summary>
public sealed class VocalSpectrographPlugin : IPlugin
{
    public const int FftSizeIndex = 0;
    public const int WindowFunctionIndex = 1;
    public const int OverlapIndex = 2;
    public const int ScaleIndex = 3;
    public const int MinFrequencyIndex = 4;
    public const int MaxFrequencyIndex = 5;
    public const int MinDbIndex = 6;
    public const int MaxDbIndex = 7;
    public const int TimeWindowIndex = 8;
    public const int ColorMapIndex = 9;
    public const int ShowPitchIndex = 10;
    public const int ShowFormantsIndex = 11;
    public const int ShowHarmonicsIndex = 12;
    public const int ShowVoicingIndex = 13;
    public const int PreEmphasisIndex = 14;
    public const int HighPassEnabledIndex = 15;
    public const int HighPassCutoffIndex = 16;
    public const int LpcOrderIndex = 17;

    private const float DefaultMinFrequency = 80f;
    private const float DefaultMaxFrequency = 8000f;
    private const float DefaultMinDb = -80f;
    private const float DefaultMaxDb = 0f;
    private const float DefaultTimeWindow = 5f;
    private const float DcCutoffHz = 10f;
    private const float DefaultHighPassHz = 60f;
    private const float DefaultPreEmphasis = 0.97f;
    private const int CaptureBufferSize = 262144;
    private const int MaxFormants = 5;
    private const int MaxHarmonics = 20;

    private static readonly int[] FftSizes = { 1024, 2048, 4096, 8192 };
    private static readonly float[] OverlapOptions = { 0.5f, 0.75f, 0.875f };

    private readonly LockFreeRingBuffer _captureBuffer = new(CaptureBufferSize);
    private readonly SpectrumMapper _mapper = new();
    private readonly VoicingDetector _voicingDetector = new();
    private readonly BiquadFilter _rumbleHighPass = new();
    private OnePoleHighPass _dcHighPass = new();
    private PreEmphasisFilter _preEmphasisFilter = new();

    private Thread? _analysisThread;
    private CancellationTokenSource? _analysisCts;

    private int _sampleRate;
    private int _activeFftSize;
    private int _activeHopSize;
    private int _activeDisplayBins;
    private int _activeOverlapIndex;
    private WindowFunction _activeWindow;
    private FrequencyScale _activeScale;
    private float _activeMinFrequency;
    private float _activeMaxFrequency;
    private float _activeTimeWindow;
    private float _activeHighPassCutoff;
    private bool _activeHighPassEnabled;
    private bool _activePreEmphasisEnabled;
    private int _activeFrameCapacity;
    private int _frameWriteIndex;
    private int _dataVersion;
    private FastFft? _fft;
    private float[] _analysisBufferRaw = Array.Empty<float>();
    private float[] _analysisBufferProcessed = Array.Empty<float>();
    private float[] _hopBuffer = Array.Empty<float>();
    private float[] _fftReal = Array.Empty<float>();
    private float[] _fftImag = Array.Empty<float>();
    private float[] _fftWindow = Array.Empty<float>();
    private float[] _fftMagnitudes = Array.Empty<float>();
    private float[] _spectrumScratch = Array.Empty<float>();
    private float _fftNormalization = 1f;

    private float[] _spectrogramBuffer = Array.Empty<float>();
    private float[] _pitchTrack = Array.Empty<float>();
    private float[] _pitchConfidence = Array.Empty<float>();
    private float[] _formantFrequencies = Array.Empty<float>();
    private float[] _formantBandwidths = Array.Empty<float>();
    private byte[] _voicingStates = Array.Empty<byte>();
    private float[] _harmonicFrequencies = Array.Empty<float>();

    private YinPitchDetector? _pitchDetector;
    private LpcAnalyzer? _lpcAnalyzer;
    private FormantTracker? _formantTracker;
    private float[] _lpcCoefficients = Array.Empty<float>();
    private float[] _formantFreqScratch = new float[MaxFormants];
    private float[] _formantBwScratch = new float[MaxFormants];
    private float[] _harmonicScratch = new float[MaxHarmonics];

    private volatile int _analysisActive;
    private int _requestedFftSize = 2048;
    private int _requestedWindow = (int)WindowFunction.Hann;
    private int _requestedOverlapIndex = 1;
    private int _requestedScale = (int)FrequencyScale.Mel;
    private float _requestedMinFrequency = DefaultMinFrequency;
    private float _requestedMaxFrequency = DefaultMaxFrequency;
    private float _requestedMinDb = DefaultMinDb;
    private float _requestedMaxDb = DefaultMaxDb;
    private float _requestedTimeWindow = DefaultTimeWindow;
    private int _requestedColorMap = 6;
    private int _requestedShowPitch = 1;
    private int _requestedShowFormants = 1;
    private int _requestedShowHarmonics = 1;
    private int _requestedShowVoicing = 1;
    private int _requestedPreEmphasis = 1;
    private int _requestedHighPassEnabled = 1;
    private float _requestedHighPassCutoff = DefaultHighPassHz;
    private int _requestedLpcOrder = 12;

    public VocalSpectrographPlugin()
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
                Index = WindowFunctionIndex,
                Name = "Window",
                MinValue = 0f,
                MaxValue = 4f,
                DefaultValue = (float)WindowFunction.Hann,
                Unit = "",
                FormatValue = value => ((WindowFunction)Math.Clamp((int)MathF.Round(value), 0, 4)).ToString()
            },
            new PluginParameter
            {
                Index = OverlapIndex,
                Name = "Overlap",
                MinValue = 0.5f,
                MaxValue = 0.875f,
                DefaultValue = 0.75f,
                Unit = "%",
                FormatValue = value => $"{SelectOverlap(value) * 100f:0.#}%"
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
            },
            new PluginParameter
            {
                Index = TimeWindowIndex,
                Name = "Time Window",
                MinValue = 1f,
                MaxValue = 30f,
                DefaultValue = DefaultTimeWindow,
                Unit = "s"
            },
            new PluginParameter
            {
                Index = ColorMapIndex,
                Name = "Color Map",
                MinValue = 0f,
                MaxValue = 6f,
                DefaultValue = 6f,
                Unit = "",
                FormatValue = value => ((int)MathF.Round(value)).ToString()
            },
            new PluginParameter
            {
                Index = ShowPitchIndex,
                Name = "Pitch Overlay",
                MinValue = 0f,
                MaxValue = 1f,
                DefaultValue = 1f,
                Unit = ""
            },
            new PluginParameter
            {
                Index = ShowFormantsIndex,
                Name = "Formants",
                MinValue = 0f,
                MaxValue = 1f,
                DefaultValue = 1f,
                Unit = ""
            },
            new PluginParameter
            {
                Index = ShowHarmonicsIndex,
                Name = "Harmonics",
                MinValue = 0f,
                MaxValue = 1f,
                DefaultValue = 1f,
                Unit = ""
            },
            new PluginParameter
            {
                Index = ShowVoicingIndex,
                Name = "Voicing",
                MinValue = 0f,
                MaxValue = 1f,
                DefaultValue = 1f,
                Unit = ""
            },
            new PluginParameter
            {
                Index = PreEmphasisIndex,
                Name = "Pre-Emphasis",
                MinValue = 0f,
                MaxValue = 1f,
                DefaultValue = 1f,
                Unit = ""
            },
            new PluginParameter
            {
                Index = HighPassEnabledIndex,
                Name = "HPF Enabled",
                MinValue = 0f,
                MaxValue = 1f,
                DefaultValue = 1f,
                Unit = ""
            },
            new PluginParameter
            {
                Index = HighPassCutoffIndex,
                Name = "HPF Cutoff",
                MinValue = 20f,
                MaxValue = 120f,
                DefaultValue = DefaultHighPassHz,
                Unit = "Hz"
            },
            new PluginParameter
            {
                Index = LpcOrderIndex,
                Name = "LPC Order",
                MinValue = 8f,
                MaxValue = 24f,
                DefaultValue = 12f,
                Unit = ""
            }
        ];
    }

    public string Id => "builtin:vocal-spectrograph";

    public string Name => "Vocal Spectrograph";

    public bool IsBypassed { get; set; }

    public int LatencySamples => 0;

    public IReadOnlyList<PluginParameter> Parameters { get; }

    public int SampleRate => _sampleRate;

    public int FftSize => Volatile.Read(ref _activeFftSize);

    public int DisplayBins => Volatile.Read(ref _activeDisplayBins);

    public int FrameCount => Volatile.Read(ref _activeFrameCapacity);

    public int MaxFormantCount => MaxFormants;

    public int MaxHarmonicCount => MaxHarmonics;

    public int DataVersion => Volatile.Read(ref _dataVersion);

    public FrequencyScale Scale => (FrequencyScale)Math.Clamp(Volatile.Read(ref _requestedScale), 0, 4);

    public WindowFunction WindowFunction => (WindowFunction)Math.Clamp(Volatile.Read(ref _requestedWindow), 0, 4);

    public float Overlap => OverlapOptions[Math.Clamp(Volatile.Read(ref _requestedOverlapIndex), 0, OverlapOptions.Length - 1)];

    public float MinFrequency => Volatile.Read(ref _requestedMinFrequency);

    public float MaxFrequency => Volatile.Read(ref _requestedMaxFrequency);

    public float MinDb => Volatile.Read(ref _requestedMinDb);

    public float MaxDb => Volatile.Read(ref _requestedMaxDb);

    public float TimeWindowSeconds => Volatile.Read(ref _requestedTimeWindow);

    public int ColorMap => Volatile.Read(ref _requestedColorMap);

    public bool ShowPitch => Volatile.Read(ref _requestedShowPitch) != 0;

    public bool ShowFormants => Volatile.Read(ref _requestedShowFormants) != 0;

    public bool ShowHarmonics => Volatile.Read(ref _requestedShowHarmonics) != 0;

    public bool ShowVoicing => Volatile.Read(ref _requestedShowVoicing) != 0;

    public bool PreEmphasisEnabled => Volatile.Read(ref _requestedPreEmphasis) != 0;

    public bool HighPassEnabled => Volatile.Read(ref _requestedHighPassEnabled) != 0;

    public float HighPassCutoff => Volatile.Read(ref _requestedHighPassCutoff);

    public int LpcOrder => Volatile.Read(ref _requestedLpcOrder);

    public void Initialize(int sampleRate, int blockSize)
    {
        _sampleRate = sampleRate;
        _requestedLpcOrder = Math.Clamp(sampleRate / 1000 + 4, 8, 24);
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
            case WindowFunctionIndex:
                Interlocked.Exchange(ref _requestedWindow, Math.Clamp((int)MathF.Round(value), 0, 4));
                break;
            case OverlapIndex:
                Interlocked.Exchange(ref _requestedOverlapIndex, SelectOverlapIndex(value));
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
            case TimeWindowIndex:
                Interlocked.Exchange(ref _requestedTimeWindow, Math.Clamp(value, 1f, 60f));
                break;
            case ColorMapIndex:
                Interlocked.Exchange(ref _requestedColorMap, Math.Clamp((int)MathF.Round(value), 0, 6));
                break;
            case ShowPitchIndex:
                Interlocked.Exchange(ref _requestedShowPitch, value >= 0.5f ? 1 : 0);
                break;
            case ShowFormantsIndex:
                Interlocked.Exchange(ref _requestedShowFormants, value >= 0.5f ? 1 : 0);
                break;
            case ShowHarmonicsIndex:
                Interlocked.Exchange(ref _requestedShowHarmonics, value >= 0.5f ? 1 : 0);
                break;
            case ShowVoicingIndex:
                Interlocked.Exchange(ref _requestedShowVoicing, value >= 0.5f ? 1 : 0);
                break;
            case PreEmphasisIndex:
                Interlocked.Exchange(ref _requestedPreEmphasis, value >= 0.5f ? 1 : 0);
                break;
            case HighPassEnabledIndex:
                Interlocked.Exchange(ref _requestedHighPassEnabled, value >= 0.5f ? 1 : 0);
                break;
            case HighPassCutoffIndex:
                Interlocked.Exchange(ref _requestedHighPassCutoff, Math.Clamp(value, 20f, 120f));
                break;
            case LpcOrderIndex:
                Interlocked.Exchange(ref _requestedLpcOrder, Math.Clamp((int)MathF.Round(value), 8, 24));
                break;
        }
    }

    public byte[] GetState()
    {
        var bytes = new byte[sizeof(float) * 18];
        Buffer.BlockCopy(BitConverter.GetBytes((float)_requestedFftSize), 0, bytes, 0, 4);
        Buffer.BlockCopy(BitConverter.GetBytes((float)_requestedWindow), 0, bytes, 4, 4);
        Buffer.BlockCopy(BitConverter.GetBytes(OverlapOptions[_requestedOverlapIndex]), 0, bytes, 8, 4);
        Buffer.BlockCopy(BitConverter.GetBytes((float)_requestedScale), 0, bytes, 12, 4);
        Buffer.BlockCopy(BitConverter.GetBytes(_requestedMinFrequency), 0, bytes, 16, 4);
        Buffer.BlockCopy(BitConverter.GetBytes(_requestedMaxFrequency), 0, bytes, 20, 4);
        Buffer.BlockCopy(BitConverter.GetBytes(_requestedMinDb), 0, bytes, 24, 4);
        Buffer.BlockCopy(BitConverter.GetBytes(_requestedMaxDb), 0, bytes, 28, 4);
        Buffer.BlockCopy(BitConverter.GetBytes(_requestedTimeWindow), 0, bytes, 32, 4);
        Buffer.BlockCopy(BitConverter.GetBytes((float)_requestedColorMap), 0, bytes, 36, 4);
        Buffer.BlockCopy(BitConverter.GetBytes(_requestedShowPitch), 0, bytes, 40, 4);
        Buffer.BlockCopy(BitConverter.GetBytes(_requestedShowFormants), 0, bytes, 44, 4);
        Buffer.BlockCopy(BitConverter.GetBytes(_requestedShowHarmonics), 0, bytes, 48, 4);
        Buffer.BlockCopy(BitConverter.GetBytes(_requestedShowVoicing), 0, bytes, 52, 4);
        Buffer.BlockCopy(BitConverter.GetBytes(_requestedPreEmphasis), 0, bytes, 56, 4);
        Buffer.BlockCopy(BitConverter.GetBytes(_requestedHighPassEnabled), 0, bytes, 60, 4);
        Buffer.BlockCopy(BitConverter.GetBytes(_requestedHighPassCutoff), 0, bytes, 64, 4);
        Buffer.BlockCopy(BitConverter.GetBytes((float)_requestedLpcOrder), 0, bytes, 68, 4);
        return bytes;
    }

    public void SetState(byte[] state)
    {
        if (state.Length < sizeof(float) * 18)
        {
            return;
        }

        SetParameter(FftSizeIndex, BitConverter.ToSingle(state, 0));
        SetParameter(WindowFunctionIndex, BitConverter.ToSingle(state, 4));
        SetParameter(OverlapIndex, BitConverter.ToSingle(state, 8));
        SetParameter(ScaleIndex, BitConverter.ToSingle(state, 12));
        SetParameter(MinFrequencyIndex, BitConverter.ToSingle(state, 16));
        SetParameter(MaxFrequencyIndex, BitConverter.ToSingle(state, 20));
        SetParameter(MinDbIndex, BitConverter.ToSingle(state, 24));
        SetParameter(MaxDbIndex, BitConverter.ToSingle(state, 28));
        SetParameter(TimeWindowIndex, BitConverter.ToSingle(state, 32));
        SetParameter(ColorMapIndex, BitConverter.ToSingle(state, 36));
        SetParameter(ShowPitchIndex, BitConverter.ToSingle(state, 40));
        SetParameter(ShowFormantsIndex, BitConverter.ToSingle(state, 44));
        SetParameter(ShowHarmonicsIndex, BitConverter.ToSingle(state, 48));
        SetParameter(ShowVoicingIndex, BitConverter.ToSingle(state, 52));
        SetParameter(PreEmphasisIndex, BitConverter.ToSingle(state, 56));
        SetParameter(HighPassEnabledIndex, BitConverter.ToSingle(state, 60));
        SetParameter(HighPassCutoffIndex, BitConverter.ToSingle(state, 64));
        SetParameter(LpcOrderIndex, BitConverter.ToSingle(state, 68));
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
    /// Copy the current spectrogram and overlay data into the provided arrays.
    /// </summary>
    public bool CopySpectrogramData(
        float[] magnitudes,
        float[] pitchTrack,
        float[] pitchConfidence,
        float[] formantFrequencies,
        float[] formantBandwidths,
        byte[] voicingStates,
        float[] harmonicFrequencies)
    {
        var spectrogramBuffer = _spectrogramBuffer;
        var pitchTrackBuffer = _pitchTrack;
        var pitchConfidenceBuffer = _pitchConfidence;
        var formantFrequencyBuffer = _formantFrequencies;
        var formantBandwidthBuffer = _formantBandwidths;
        var voicingBuffer = _voicingStates;
        var harmonicBuffer = _harmonicFrequencies;

        int frames = Volatile.Read(ref _activeFrameCapacity);
        int bins = Volatile.Read(ref _activeDisplayBins);
        if (frames <= 0 || bins <= 0)
        {
            return false;
        }

        int specLength = frames * bins;
        int formantLength = frames * MaxFormants;
        int harmonicLength = frames * MaxHarmonics;

        if (spectrogramBuffer.Length != specLength
            || pitchTrackBuffer.Length != frames
            || pitchConfidenceBuffer.Length != frames
            || formantFrequencyBuffer.Length != formantLength
            || formantBandwidthBuffer.Length != formantLength
            || voicingBuffer.Length != frames
            || harmonicBuffer.Length != harmonicLength)
        {
            return false;
        }

        if (magnitudes.Length < specLength
            || pitchTrack.Length < frames
            || pitchConfidence.Length < frames
            || formantFrequencies.Length < formantLength
            || formantBandwidths.Length < formantLength
            || voicingStates.Length < frames
            || harmonicFrequencies.Length < harmonicLength)
        {
            return false;
        }

        for (int attempt = 0; attempt < 2; attempt++)
        {
            int versionStart = Volatile.Read(ref _dataVersion);
            if ((versionStart & 1) != 0)
            {
                Thread.Yield();
                continue;
            }

            int writeIndex = Volatile.Read(ref _frameWriteIndex);
            if ((uint)writeIndex >= (uint)frames)
            {
                return false;
            }

            CopyRing(spectrogramBuffer, magnitudes, frames, bins, writeIndex);
            CopyRing(pitchTrackBuffer, pitchTrack, frames, 1, writeIndex);
            CopyRing(pitchConfidenceBuffer, pitchConfidence, frames, 1, writeIndex);
            CopyRing(formantFrequencyBuffer, formantFrequencies, frames, MaxFormants, writeIndex);
            CopyRing(formantBandwidthBuffer, formantBandwidths, frames, MaxFormants, writeIndex);
            CopyRing(voicingBuffer, voicingStates, frames, writeIndex);
            CopyRing(harmonicBuffer, harmonicFrequencies, frames, MaxHarmonics, writeIndex);

            int versionEnd = Volatile.Read(ref _dataVersion);
            if (versionStart == versionEnd && (versionEnd & 1) == 0)
            {
                return true;
            }
        }

        return true;
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
            Name = "HotMic-VocalSpectrograph"
        };
        _analysisThread.Start();
    }

    private void AnalysisLoop(CancellationToken token)
    {
        int pitchFrameCounter = 0;
        float lastPitch = 0f;
        float lastConfidence = 0f;

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
            Array.Copy(_analysisBufferRaw, shift, _analysisBufferRaw, 0, tail);
            Array.Copy(_analysisBufferProcessed, shift, _analysisBufferProcessed, 0, tail);

            bool preEmphasis = Volatile.Read(ref _requestedPreEmphasis) != 0;
            bool hpfEnabled = Volatile.Read(ref _requestedHighPassEnabled) != 0;

            for (int i = 0; i < shift; i++)
            {
                float sample = _hopBuffer[i];
                float dcRemoved = _dcHighPass.Process(sample);
                float filtered = hpfEnabled ? _rumbleHighPass.Process(dcRemoved) : dcRemoved;
                float emphasized = preEmphasis ? _preEmphasisFilter.Process(filtered) : filtered;

                _analysisBufferRaw[tail + i] = filtered;
                _analysisBufferProcessed[tail + i] = emphasized;
            }

            // FFT (processed buffer)
            for (int i = 0; i < _activeFftSize; i++)
            {
                _fftReal[i] = _analysisBufferProcessed[i] * _fftWindow[i];
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

            _mapper.MapMax(_fftMagnitudes, _spectrumScratch);

            float minDb = Volatile.Read(ref _requestedMinDb);
            float maxDb = Volatile.Read(ref _requestedMaxDb);
            float range = MathF.Max(1f, maxDb - minDb);

            // Pitch detection (raw buffer) at a reduced rate for performance.
            pitchFrameCounter++;
            if (pitchFrameCounter >= 2)
            {
                pitchFrameCounter = 0;
                if (_pitchDetector is not null)
                {
                    var pitch = _pitchDetector.Detect(_analysisBufferRaw);
                    lastPitch = pitch.FrequencyHz ?? 0f;
                    lastConfidence = pitch.Confidence;
                }
            }

            var voicing = _voicingDetector.Detect(_analysisBufferRaw, _fftMagnitudes, lastConfidence);

            int formantCount = 0;
            if (Volatile.Read(ref _requestedShowFormants) != 0
                && _lpcAnalyzer is not null
                && _formantTracker is not null
                && voicing == VoicingState.Voiced)
            {
                if (_lpcAnalyzer.Compute(_analysisBufferProcessed, _lpcCoefficients))
                {
                    formantCount = _formantTracker.Track(_lpcCoefficients, _sampleRate,
                        _formantFreqScratch, _formantBwScratch,
                        _activeMinFrequency, _activeMaxFrequency, MaxFormants);
                }
            }

            int harmonicCount = 0;
            if (Volatile.Read(ref _requestedShowHarmonics) != 0 && lastPitch > 0f)
            {
                harmonicCount = HarmonicPeakDetector.Detect(_fftMagnitudes, _sampleRate, _activeFftSize, lastPitch, _harmonicScratch);
            }

            int frameIndex = _frameWriteIndex;
            Interlocked.Increment(ref _dataVersion);

            int specOffset = frameIndex * _activeDisplayBins;
            for (int i = 0; i < _activeDisplayBins; i++)
            {
                float db = DspUtils.LinearToDb(_spectrumScratch[i]);
                _spectrogramBuffer[specOffset + i] = Math.Clamp((db - minDb) / range, 0f, 1f);
            }

            _pitchTrack[frameIndex] = Volatile.Read(ref _requestedShowPitch) != 0 ? lastPitch : 0f;
            _pitchConfidence[frameIndex] = lastConfidence;
            _voicingStates[frameIndex] = Volatile.Read(ref _requestedShowVoicing) != 0 ? (byte)voicing : (byte)VoicingState.Silence;

            int formantOffset = frameIndex * MaxFormants;
            for (int i = 0; i < MaxFormants; i++)
            {
                _formantFrequencies[formantOffset + i] = i < formantCount ? _formantFreqScratch[i] : 0f;
                _formantBandwidths[formantOffset + i] = i < formantCount ? _formantBwScratch[i] : 0f;
            }

            int harmonicOffset = frameIndex * MaxHarmonics;
            for (int i = 0; i < MaxHarmonics; i++)
            {
                _harmonicFrequencies[harmonicOffset + i] = i < harmonicCount ? _harmonicScratch[i] : 0f;
            }

            Volatile.Write(ref _frameWriteIndex, (frameIndex + 1) % _activeFrameCapacity);
            Interlocked.Increment(ref _dataVersion);
        }
    }

    private void ConfigureAnalysis(bool force)
    {
        int fftSize = SelectDiscrete(Volatile.Read(ref _requestedFftSize), FftSizes);
        var window = (WindowFunction)Math.Clamp(Volatile.Read(ref _requestedWindow), 0, 4);
        int overlapIndex = Math.Clamp(Volatile.Read(ref _requestedOverlapIndex), 0, OverlapOptions.Length - 1);
        var scale = (FrequencyScale)Math.Clamp(Volatile.Read(ref _requestedScale), 0, 4);
        float minHz = Volatile.Read(ref _requestedMinFrequency);
        float maxHz = Volatile.Read(ref _requestedMaxFrequency);
        float timeWindow = Volatile.Read(ref _requestedTimeWindow);
        int lpcOrder = Math.Clamp(Volatile.Read(ref _requestedLpcOrder), 8, 24);
        float hpfCutoff = Volatile.Read(ref _requestedHighPassCutoff);
        bool hpfEnabled = Volatile.Read(ref _requestedHighPassEnabled) != 0;
        bool preEmphasisEnabled = Volatile.Read(ref _requestedPreEmphasis) != 0;

        bool sizeChanged = force
            || fftSize != _activeFftSize
            || overlapIndex != _activeOverlapIndex
            || timeWindow != _activeTimeWindow;

        bool mappingChanged = force
            || scale != _activeScale
            || MathF.Abs(minHz - _activeMinFrequency) > 1e-3f
            || MathF.Abs(maxHz - _activeMaxFrequency) > 1e-3f;

        bool windowChanged = force || window != _activeWindow;
        bool filterChanged = force
            || MathF.Abs(hpfCutoff - _activeHighPassCutoff) > 1e-3f
            || hpfEnabled != _activeHighPassEnabled
            || preEmphasisEnabled != _activePreEmphasisEnabled;
        bool lpcChanged = force || _lpcAnalyzer is null || lpcOrder != _lpcAnalyzer.Order;

        if (sizeChanged)
        {
            _activeFftSize = fftSize;
            _activeOverlapIndex = overlapIndex;
            _activeTimeWindow = timeWindow;
            _activeHopSize = Math.Max(1, (int)(fftSize * (1f - OverlapOptions[overlapIndex])));
            _activeDisplayBins = Math.Min(1024, fftSize / 2);
            _activeFrameCapacity = Math.Max(1, (int)MathF.Ceiling(timeWindow * _sampleRate / _activeHopSize));

            _fft = new FastFft(_activeFftSize);
            _analysisBufferRaw = new float[_activeFftSize];
            _analysisBufferProcessed = new float[_activeFftSize];
            _hopBuffer = new float[_activeHopSize];
            _fftReal = new float[_activeFftSize];
            _fftImag = new float[_activeFftSize];
            _fftWindow = new float[_activeFftSize];
            _fftMagnitudes = new float[_activeFftSize / 2];
            _spectrumScratch = new float[_activeDisplayBins];
            _fftNormalization = 2f / MathF.Max(1f, _activeFftSize);

            _spectrogramBuffer = new float[_activeFrameCapacity * _activeDisplayBins];
            _pitchTrack = new float[_activeFrameCapacity];
            _pitchConfidence = new float[_activeFrameCapacity];
            _formantFrequencies = new float[_activeFrameCapacity * MaxFormants];
            _formantBandwidths = new float[_activeFrameCapacity * MaxFormants];
            _voicingStates = new byte[_activeFrameCapacity];
            _harmonicFrequencies = new float[_activeFrameCapacity * MaxHarmonics];
            Volatile.Write(ref _frameWriteIndex, 0);
        }

        // Refill the window buffer when size changes to avoid zeroed FFT input.
        if (windowChanged || sizeChanged)
        {
            _activeWindow = window;
            WindowFunctions.Fill(_fftWindow, window);
        }

        if (mappingChanged || sizeChanged)
        {
            _activeScale = scale;
            _activeMinFrequency = minHz;
            _activeMaxFrequency = maxHz;
            _mapper.Configure(_activeFftSize, _sampleRate, _activeDisplayBins, minHz, maxHz, scale);
        }

        if (sizeChanged || filterChanged)
        {
            _dcHighPass.Configure(DcCutoffHz, _sampleRate);
            _rumbleHighPass.SetHighPass(_sampleRate, hpfCutoff, 0.707f);
            _rumbleHighPass.Reset();
            _activeHighPassCutoff = hpfCutoff;
            _activeHighPassEnabled = hpfEnabled;

            _preEmphasisFilter.Configure(DefaultPreEmphasis);
            _preEmphasisFilter.Reset();
            _activePreEmphasisEnabled = preEmphasisEnabled;
        }

        if (sizeChanged || lpcChanged)
        {
            _pitchDetector ??= new YinPitchDetector(_sampleRate, _activeFftSize, 60f, 1200f, 0.15f);
            _pitchDetector.Configure(_sampleRate, _activeFftSize, 60f, 1200f, 0.15f);

            if (_lpcAnalyzer is null)
            {
                _lpcAnalyzer = new LpcAnalyzer(lpcOrder);
            }
            else
            {
                _lpcAnalyzer.Configure(lpcOrder);
            }

            if (_formantTracker is null)
            {
                _formantTracker = new FormantTracker(lpcOrder);
            }
            else
            {
                _formantTracker.Configure(lpcOrder);
            }

            _lpcCoefficients = new float[lpcOrder + 1];
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

    private static float SelectOverlap(float value)
    {
        return OverlapOptions[SelectOverlapIndex(value)];
    }

    private static int SelectOverlapIndex(float value)
    {
        int best = 0;
        float bestDelta = MathF.Abs(OverlapOptions[0] - value);
        for (int i = 1; i < OverlapOptions.Length; i++)
        {
            float delta = MathF.Abs(OverlapOptions[i] - value);
            if (delta < bestDelta)
            {
                bestDelta = delta;
                best = i;
            }
        }
        return best;
    }

    private static string FormatDiscrete(float value, IReadOnlyList<int> options, string suffix)
    {
        int selected = SelectDiscrete(value, options);
        return string.IsNullOrWhiteSpace(suffix) ? selected.ToString() : $"{selected}{suffix}";
    }

    private static void CopyRing(float[] source, float[] destination, int frames, int stride, int writeIndex)
    {
        int total = frames * stride;
        if (total == 0)
        {
            return;
        }

        int firstLength = (frames - writeIndex) * stride;
        if (firstLength > 0)
        {
            Array.Copy(source, writeIndex * stride, destination, 0, firstLength);
        }

        int secondLength = writeIndex * stride;
        if (secondLength > 0)
        {
            Array.Copy(source, 0, destination, firstLength, secondLength);
        }
    }

    private static void CopyRing(byte[] source, byte[] destination, int frames, int writeIndex)
    {
        if (frames == 0)
        {
            return;
        }

        int firstLength = frames - writeIndex;
        if (firstLength > 0)
        {
            Array.Copy(source, writeIndex, destination, 0, firstLength);
        }

        int secondLength = writeIndex;
        if (secondLength > 0)
        {
            Array.Copy(source, 0, destination, firstLength, secondLength);
        }
    }

}
