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
    public const int ReassignModeIndex = 18;

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
    private const float NoiseEstimateFast = 0.2f;
    private const float NoiseEstimateSlow = 0.01f;
    private const float NoiseSubtractionAlpha = 0.85f;
    private const float NoiseSubtractionFloor = 0.08f;
    private const float DisplayFloorDb = -96f;
    private const int HpssKernelSize = 5;
    private const float TemporalSmoothingFactor = 0.6f;
    private const float HarmonicBoost = 1.35f;
    private const float HarmonicAttenuation = 0.7f;
    private const float HarmonicConfidenceThreshold = 0.35f;
    private const float HarmonicToleranceFactor = 0.06f;
    private const float ReassignMinDb = -85f;
    private const int MaxReassignBinShift = 6;
    private const int MaxReassignFrameShift = 4;

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
    private long _frameCounter;
    private long _latestFrameId = -1;
    private int _availableFrames;
    private int _reassignLatencyFrames;
    private int _dataVersion;
    private FastFft? _fft;
    private float[] _analysisBufferRaw = Array.Empty<float>();
    private float[] _analysisBufferProcessed = Array.Empty<float>();
    private float[] _hopBuffer = Array.Empty<float>();
    private float[] _fftReal = Array.Empty<float>();
    private float[] _fftImag = Array.Empty<float>();
    private float[] _fftWindow = Array.Empty<float>();
    private float[] _fftWindowTime = Array.Empty<float>();
    private float[] _fftWindowDerivative = Array.Empty<float>();
    private float[] _fftTimeReal = Array.Empty<float>();
    private float[] _fftTimeImag = Array.Empty<float>();
    private float[] _fftDerivReal = Array.Empty<float>();
    private float[] _fftDerivImag = Array.Empty<float>();
    private float[] _fftMagnitudes = Array.Empty<float>();
    private float[] _spectrumScratch = Array.Empty<float>();
    private float[] _displayWork = Array.Empty<float>();
    private float[] _displayProcessed = Array.Empty<float>();
    private float[] _displaySmoothed = Array.Empty<float>();
    private float[] _displayGain = Array.Empty<float>();
    private float[] _noiseEstimate = Array.Empty<float>();
    private float[] _hpssHistory = Array.Empty<float>();
    private int _hpssHistoryIndex;
    private float[] _medianScratch = Array.Empty<float>();
    private float[] _harmonicMask = Array.Empty<float>();
    private bool _harmonicMaskActive;
    private int[] _fftBinToDisplay = Array.Empty<int>();
    private float[] _fftBinDisplayPos = Array.Empty<float>();
    private float _fftNormalization = 1f;
    private float _binResolution;
    private float _scaledMin;
    private float _scaledMax;
    private float _scaledRange;

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
    private int _requestedReassignMode;
    private SpectrogramReassignMode _activeReassignMode;

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
            },
            new PluginParameter
            {
                Index = ReassignModeIndex,
                Name = "Reassign",
                MinValue = 0f,
                MaxValue = 3f,
                DefaultValue = 0f,
                Unit = "",
                FormatValue = value => ((SpectrogramReassignMode)Math.Clamp((int)MathF.Round(value), 0, 3)).ToString()
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

    public long LatestFrameId => Volatile.Read(ref _latestFrameId);

    public int AvailableFrames => Volatile.Read(ref _availableFrames);

    public FrequencyScale Scale => (FrequencyScale)Math.Clamp(Volatile.Read(ref _requestedScale), 0, 4);

    public WindowFunction WindowFunction => (WindowFunction)Math.Clamp(Volatile.Read(ref _requestedWindow), 0, 4);

    public float Overlap => OverlapOptions[Math.Clamp(Volatile.Read(ref _requestedOverlapIndex), 0, OverlapOptions.Length - 1)];

    public SpectrogramReassignMode ReassignMode =>
        (SpectrogramReassignMode)Math.Clamp(Volatile.Read(ref _requestedReassignMode), 0, 3);

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
            case ReassignModeIndex:
                Interlocked.Exchange(ref _requestedReassignMode, Math.Clamp((int)MathF.Round(value), 0, 3));
                break;
        }
    }

    public byte[] GetState()
    {
        var bytes = new byte[sizeof(float) * 19];
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
        Buffer.BlockCopy(BitConverter.GetBytes((float)_requestedReassignMode), 0, bytes, 72, 4);
        return bytes;
    }

    public void SetState(byte[] state)
    {
        if (state.Length < sizeof(float) * 19)
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
        SetParameter(ReassignModeIndex, BitConverter.ToSingle(state, 72));
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
        if (active)
        {
            Volatile.Write(ref _analysisActive, 0);
            _captureBuffer.Clear();
            ClearVisualizationBuffers();
            Volatile.Write(ref _analysisActive, 1);
        }
        else
        {
            Volatile.Write(ref _analysisActive, 0);
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

            long latestFrameId = Volatile.Read(ref _latestFrameId);
            int availableFrames = Volatile.Read(ref _availableFrames);
            if (availableFrames <= 0 || latestFrameId < 0)
            {
                Array.Clear(magnitudes, 0, specLength);
                Array.Clear(pitchTrack, 0, frames);
                Array.Clear(pitchConfidence, 0, frames);
                Array.Clear(formantFrequencies, 0, formantLength);
                Array.Clear(formantBandwidths, 0, formantLength);
                Array.Clear(voicingStates, 0, frames);
                Array.Clear(harmonicFrequencies, 0, harmonicLength);
            }
            else
            {
                long oldestFrameId = latestFrameId - availableFrames + 1;
                int startIndex = (int)(oldestFrameId % frames);
                int padFrames = Math.Max(0, frames - availableFrames);

                CopyRing(spectrogramBuffer, magnitudes, availableFrames, bins, startIndex, padFrames);
                CopyRing(pitchTrackBuffer, pitchTrack, availableFrames, 1, startIndex, padFrames);
                CopyRing(pitchConfidenceBuffer, pitchConfidence, availableFrames, 1, startIndex, padFrames);
                CopyRing(formantFrequencyBuffer, formantFrequencies, availableFrames, MaxFormants, startIndex, padFrames);
                CopyRing(formantBandwidthBuffer, formantBandwidths, availableFrames, MaxFormants, startIndex, padFrames);
                CopyRing(voicingBuffer, voicingStates, availableFrames, startIndex, padFrames);
                CopyRing(harmonicBuffer, harmonicFrequencies, availableFrames, MaxHarmonics, startIndex, padFrames);
            }

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

            bool reassignEnabled = _activeReassignMode != SpectrogramReassignMode.Off;
            if (reassignEnabled)
            {
                for (int i = 0; i < _activeFftSize; i++)
                {
                    float sample = _analysisBufferProcessed[i];
                    _fftReal[i] = sample * _fftWindow[i];
                    _fftImag[i] = 0f;
                    _fftTimeReal[i] = sample * _fftWindowTime[i];
                    _fftTimeImag[i] = 0f;
                    _fftDerivReal[i] = sample * _fftWindowDerivative[i];
                    _fftDerivImag[i] = 0f;
                }

                _fft?.Forward(_fftReal, _fftImag);
                _fft?.Forward(_fftTimeReal, _fftTimeImag);
                _fft?.Forward(_fftDerivReal, _fftDerivImag);
            }
            else
            {
                // FFT (processed buffer)
                for (int i = 0; i < _activeFftSize; i++)
                {
                    _fftReal[i] = _analysisBufferProcessed[i] * _fftWindow[i];
                    _fftImag[i] = 0f;
                }

                _fft?.Forward(_fftReal, _fftImag);
            }

            int half = _activeFftSize / 2;
            float normalization = _fftNormalization;
            for (int i = 0; i < half; i++)
            {
                float re = _fftReal[i];
                float im = _fftImag[i];
                _fftMagnitudes[i] = MathF.Sqrt(re * re + im * im) * normalization;
            }

            _mapper.MapMax(_fftMagnitudes, _spectrumScratch);

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

            Array.Copy(_spectrumScratch, _displayWork, _activeDisplayBins);
            ApplyNoiseReduction(_displayWork);
            ApplyHpss(_displayWork, _displayProcessed);
            UpdateHarmonicMask(lastPitch, lastConfidence, voicing);
            ApplyHarmonicComb(_displayProcessed);
            ApplyTemporalSmoothing(_displayProcessed, _displaySmoothed);

            float minDb = Volatile.Read(ref _requestedMinDb);
            float maxDb = Volatile.Read(ref _requestedMaxDb);
            float floorDb = MathF.Max(minDb, DisplayFloorDb);
            float range = MathF.Max(1f, maxDb - floorDb);

            long frameId = _frameCounter;
            int frameIndex = (int)(frameId % _activeFrameCapacity);
            Interlocked.Increment(ref _dataVersion);

            if (reassignEnabled)
            {
                int specOffset = frameIndex * _activeDisplayBins;
                Array.Clear(_spectrogramBuffer, specOffset, _activeDisplayBins);
                BuildDisplayGain();

                float reassignThresholdDb = MathF.Max(ReassignMinDb, floorDb);
                float reassignThresholdLinear = DspUtils.DbToLinear(reassignThresholdDb);
                float invHop = 1f / MathF.Max(1f, _activeHopSize);
                float freqBinScale = _activeFftSize / (MathF.PI * 2f);
                long oldestFrameId = Math.Max(0, frameId - _activeFrameCapacity + 1);

                for (int bin = 0; bin < half; bin++)
                {
                    float mag = _fftMagnitudes[bin];
                    if (mag <= 0f)
                    {
                        continue;
                    }

                    int displayBin = _fftBinToDisplay[bin];
                    float gain = _displayGain[displayBin];
                    if (gain <= 0f)
                    {
                        continue;
                    }

                    float adjustedMag = mag * gain;
                    if (adjustedMag < reassignThresholdLinear)
                    {
                        continue;
                    }

                    float re = _fftReal[bin];
                    float im = _fftImag[bin];
                    float denom = re * re + im * im + 1e-12f;

                    float timeShiftFrames = 0f;
                    if (_activeReassignMode.HasFlag(SpectrogramReassignMode.Time))
                    {
                        float reTime = _fftTimeReal[bin];
                        float imTime = _fftTimeImag[bin];
                        // STFT reassignment time shift from the time-weighted window.
                        float timeShiftSamples = (reTime * re + imTime * im) / denom;
                        timeShiftFrames = Math.Clamp(timeShiftSamples * invHop, -MaxReassignFrameShift, MaxReassignFrameShift);
                    }

                    float freqShiftBins = 0f;
                    if (_activeReassignMode.HasFlag(SpectrogramReassignMode.Frequency))
                    {
                        float reDeriv = _fftDerivReal[bin];
                        float imDeriv = _fftDerivImag[bin];
                        // STFT reassignment frequency shift from the window derivative FFT.
                        float imag = (imDeriv * re - reDeriv * im) / denom;
                        freqShiftBins = Math.Clamp(imag * freqBinScale, -MaxReassignBinShift, MaxReassignBinShift);
                    }

                    float targetFrame = frameId + timeShiftFrames - _reassignLatencyFrames;
                    long frameBase = (long)MathF.Floor(targetFrame);
                    float frameFrac = targetFrame - frameBase;
                    if (frameBase < oldestFrameId || frameBase > frameId)
                    {
                        continue;
                    }

                    float reassignedBin = bin + freqShiftBins;
                    if (reassignedBin < 0f || reassignedBin > half - 1)
                    {
                        continue;
                    }

                    float displayPos = GetDisplayPosition(reassignedBin);
                    int binBase = (int)MathF.Floor(displayPos);
                    float binFrac = displayPos - binBase;
                    if (binBase < 0 || binBase >= _activeDisplayBins)
                    {
                        continue;
                    }

                    float db = DspUtils.LinearToDb(adjustedMag);
                    if (db < reassignThresholdDb)
                    {
                        continue;
                    }

                    float normalized = Math.Clamp((db - floorDb) / range, 0f, 1f);
                    if (normalized <= 0f)
                    {
                        continue;
                    }

                    float wFrame0 = 1f - frameFrac;
                    float wFrame1 = frameFrac;
                    float wBin0 = 1f - binFrac;
                    float wBin1 = binFrac;

                    long frame0 = frameBase;
                    if (frame0 >= oldestFrameId)
                    {
                        int targetIndex = (int)(frame0 % _activeFrameCapacity);
                        int baseOffset = targetIndex * _activeDisplayBins;
                        float value = normalized * wFrame0 * wBin0;
                        if (value > _spectrogramBuffer[baseOffset + binBase])
                        {
                            _spectrogramBuffer[baseOffset + binBase] = value;
                        }

                        int bin1 = binBase + 1;
                        if (bin1 < _activeDisplayBins && wBin1 > 0f)
                        {
                            value = normalized * wFrame0 * wBin1;
                            if (value > _spectrogramBuffer[baseOffset + bin1])
                            {
                                _spectrogramBuffer[baseOffset + bin1] = value;
                            }
                        }
                    }

                    long frame1 = frameBase + 1;
                    if (wFrame1 > 0f && frame1 <= frameId && frame1 >= oldestFrameId)
                    {
                        int targetIndex = (int)(frame1 % _activeFrameCapacity);
                        int baseOffset = targetIndex * _activeDisplayBins;
                        float value = normalized * wFrame1 * wBin0;
                        if (value > _spectrogramBuffer[baseOffset + binBase])
                        {
                            _spectrogramBuffer[baseOffset + binBase] = value;
                        }

                        int bin1 = binBase + 1;
                        if (bin1 < _activeDisplayBins && wBin1 > 0f)
                        {
                            value = normalized * wFrame1 * wBin1;
                            if (value > _spectrogramBuffer[baseOffset + bin1])
                            {
                                _spectrogramBuffer[baseOffset + bin1] = value;
                            }
                        }
                    }
                }
            }
            else
            {
                int specOffset = frameIndex * _activeDisplayBins;
                for (int i = 0; i < _activeDisplayBins; i++)
                {
                    float db = DspUtils.LinearToDb(_displaySmoothed[i]);
                    _spectrogramBuffer[specOffset + i] = Math.Clamp((db - floorDb) / range, 0f, 1f);
                }
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

            long nextFrame = frameId + 1;
            Volatile.Write(ref _frameCounter, nextFrame);
            UpdateDisplayWindow(nextFrame);
            Interlocked.Increment(ref _dataVersion);
        }
    }

    private void ConfigureAnalysis(bool force)
    {
        int fftSize = SelectDiscrete(Volatile.Read(ref _requestedFftSize), FftSizes);
        var window = (WindowFunction)Math.Clamp(Volatile.Read(ref _requestedWindow), 0, 4);
        int overlapIndex = Math.Clamp(Volatile.Read(ref _requestedOverlapIndex), 0, OverlapOptions.Length - 1);
        var scale = (FrequencyScale)Math.Clamp(Volatile.Read(ref _requestedScale), 0, 4);
        var reassignMode = (SpectrogramReassignMode)Math.Clamp(Volatile.Read(ref _requestedReassignMode), 0, 3);
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
            || MathF.Abs(timeWindow - _activeTimeWindow) > 1e-3f;

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
        bool reassignChanged = force || reassignMode != _activeReassignMode;
        bool resetBuffers = false;

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
            _fftWindowTime = new float[_activeFftSize];
            _fftWindowDerivative = new float[_activeFftSize];
            _fftTimeReal = new float[_activeFftSize];
            _fftTimeImag = new float[_activeFftSize];
            _fftDerivReal = new float[_activeFftSize];
            _fftDerivImag = new float[_activeFftSize];
            _fftMagnitudes = new float[_activeFftSize / 2];
            _spectrumScratch = new float[_activeDisplayBins];
            _displayWork = new float[_activeDisplayBins];
            _displayProcessed = new float[_activeDisplayBins];
            _displaySmoothed = new float[_activeDisplayBins];
            _displayGain = new float[_activeDisplayBins];
            _noiseEstimate = new float[_activeDisplayBins];
            _hpssHistory = new float[_activeDisplayBins * HpssKernelSize];
            _hpssHistoryIndex = 0;
            _medianScratch = new float[HpssKernelSize];
            _harmonicMask = new float[_activeDisplayBins];
            _fftBinToDisplay = new int[_activeFftSize / 2];
            _fftBinDisplayPos = new float[_activeFftSize / 2];
            _fftNormalization = 2f / MathF.Max(1f, _activeFftSize);
            _binResolution = _sampleRate / (float)_activeFftSize;

            _spectrogramBuffer = new float[_activeFrameCapacity * _activeDisplayBins];
            _pitchTrack = new float[_activeFrameCapacity];
            _pitchConfidence = new float[_activeFrameCapacity];
            _formantFrequencies = new float[_activeFrameCapacity * MaxFormants];
            _formantBandwidths = new float[_activeFrameCapacity * MaxFormants];
            _voicingStates = new byte[_activeFrameCapacity];
            _harmonicFrequencies = new float[_activeFrameCapacity * MaxHarmonics];
            resetBuffers = true;
        }

        // Refill the window buffer when size changes to avoid zeroed FFT input.
        if (windowChanged || sizeChanged)
        {
            _activeWindow = window;
            WindowFunctions.Fill(_fftWindow, window);
            UpdateReassignWindows();
            resetBuffers = true;
        }

        if (mappingChanged || sizeChanged)
        {
            _activeScale = scale;
            _activeMinFrequency = minHz;
            _activeMaxFrequency = maxHz;
            _mapper.Configure(_activeFftSize, _sampleRate, _activeDisplayBins, minHz, maxHz, scale);
            UpdateFftBinMapping();
            resetBuffers = true;
        }

        if (reassignChanged || sizeChanged)
        {
            _activeReassignMode = reassignMode;
            _reassignLatencyFrames = _activeReassignMode.HasFlag(SpectrogramReassignMode.Time)
                ? MaxReassignFrameShift
                : 0;
            resetBuffers = true;
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

        if (resetBuffers)
        {
            ClearVisualizationBuffers();
        }
    }

    private void ClearVisualizationBuffers()
    {
        Interlocked.Increment(ref _dataVersion);

        Array.Clear(_spectrogramBuffer, 0, _spectrogramBuffer.Length);
        Array.Clear(_pitchTrack, 0, _pitchTrack.Length);
        Array.Clear(_pitchConfidence, 0, _pitchConfidence.Length);
        Array.Clear(_formantFrequencies, 0, _formantFrequencies.Length);
        Array.Clear(_formantBandwidths, 0, _formantBandwidths.Length);
        Array.Clear(_voicingStates, 0, _voicingStates.Length);
        Array.Clear(_harmonicFrequencies, 0, _harmonicFrequencies.Length);
        Array.Clear(_noiseEstimate, 0, _noiseEstimate.Length);
        Array.Clear(_displayWork, 0, _displayWork.Length);
        Array.Clear(_displayProcessed, 0, _displayProcessed.Length);
        Array.Clear(_displaySmoothed, 0, _displaySmoothed.Length);
        Array.Clear(_displayGain, 0, _displayGain.Length);
        Array.Clear(_hpssHistory, 0, _hpssHistory.Length);
        Array.Clear(_harmonicMask, 0, _harmonicMask.Length);
        _harmonicMaskActive = false;
        _hpssHistoryIndex = 0;

        Volatile.Write(ref _frameCounter, 0);
        Volatile.Write(ref _latestFrameId, -1);
        Volatile.Write(ref _availableFrames, 0);

        Interlocked.Increment(ref _dataVersion);
    }

    private void UpdateDisplayWindow(long frameCounter)
    {
        long latestFrameId = frameCounter - 1;
        if (_activeReassignMode.HasFlag(SpectrogramReassignMode.Time))
        {
            latestFrameId -= _reassignLatencyFrames;
        }

        if (latestFrameId < 0)
        {
            Volatile.Write(ref _latestFrameId, -1);
            Volatile.Write(ref _availableFrames, 0);
            return;
        }

        long oldestFrameId = Math.Max(0, frameCounter - _activeFrameCapacity);
        if (latestFrameId < oldestFrameId)
        {
            Volatile.Write(ref _latestFrameId, -1);
            Volatile.Write(ref _availableFrames, 0);
            return;
        }

        long availableFrames = Math.Min(_activeFrameCapacity, latestFrameId - oldestFrameId + 1);
        Volatile.Write(ref _latestFrameId, latestFrameId);
        Volatile.Write(ref _availableFrames, (int)availableFrames);
    }

    private void UpdateReassignWindows()
    {
        if (_fftWindowTime.Length != _activeFftSize || _fftWindowDerivative.Length != _activeFftSize)
        {
            return;
        }

        float center = 0.5f * (_activeFftSize - 1);
        for (int i = 0; i < _activeFftSize; i++)
        {
            float t = i - center;
            _fftWindowTime[i] = _fftWindow[i] * t;
        }

        for (int i = 0; i < _activeFftSize; i++)
        {
            float prev = i > 0 ? _fftWindow[i - 1] : _fftWindow[i];
            float next = i < _activeFftSize - 1 ? _fftWindow[i + 1] : _fftWindow[i];
            _fftWindowDerivative[i] = 0.5f * (next - prev);
        }
    }

    private void UpdateFftBinMapping()
    {
        int half = _activeFftSize / 2;
        if (_fftBinToDisplay.Length != half)
        {
            _fftBinToDisplay = new int[half];
        }

        if (_fftBinDisplayPos.Length != half)
        {
            _fftBinDisplayPos = new float[half];
        }

        float nyquist = _sampleRate * 0.5f;
        float minHz = Math.Clamp(_activeMinFrequency, 1f, nyquist - 1f);
        float maxHz = Math.Clamp(_activeMaxFrequency, minHz + 1f, nyquist);
        _scaledMin = FrequencyScaleUtils.ToScale(_activeScale, minHz);
        _scaledMax = FrequencyScaleUtils.ToScale(_activeScale, maxHz);
        _scaledRange = MathF.Max(1e-6f, _scaledMax - _scaledMin);
        float invRange = 1f / _scaledRange;
        float maxPos = Math.Max(1f, _activeDisplayBins - 1);

        for (int bin = 0; bin < half; bin++)
        {
            float freq = bin * _binResolution;
            float clamped = Math.Clamp(freq, minHz, maxHz);
            float scaled = FrequencyScaleUtils.ToScale(_activeScale, clamped);
            float norm = (scaled - _scaledMin) * invRange;
            float pos = Math.Clamp(norm * maxPos, 0f, maxPos);
            _fftBinDisplayPos[bin] = pos;
            _fftBinToDisplay[bin] = (int)MathF.Round(pos);
        }
    }

    private void ApplyNoiseReduction(float[] magnitudes)
    {
        for (int i = 0; i < magnitudes.Length; i++)
        {
            float value = magnitudes[i];
            float estimate = _noiseEstimate[i];
            float coeff = value > estimate ? NoiseEstimateFast : NoiseEstimateSlow;
            estimate += (value - estimate) * coeff;
            _noiseEstimate[i] = estimate;
            float reduced = value - estimate * NoiseSubtractionAlpha;
            float floor = value * NoiseSubtractionFloor;
            magnitudes[i] = MathF.Max(reduced, floor);
        }
    }

    private void ApplyHpss(float[] input, float[] output)
    {
        int bins = input.Length;
        if (bins == 0)
        {
            return;
        }

        int historyOffset = _hpssHistoryIndex * bins;
        Array.Copy(input, 0, _hpssHistory, historyOffset, bins);
        _hpssHistoryIndex = (_hpssHistoryIndex + 1) % HpssKernelSize;

        int halfKernel = HpssKernelSize / 2;
        for (int bin = 0; bin < bins; bin++)
        {
            for (int k = 0; k < HpssKernelSize; k++)
            {
                _medianScratch[k] = _hpssHistory[k * bins + bin];
            }
            float harmonic = Median(_medianScratch, HpssKernelSize);

            int start = Math.Max(0, bin - halfKernel);
            int end = Math.Min(bins - 1, bin + halfKernel);
            int count = end - start + 1;
            for (int i = 0; i < count; i++)
            {
                _medianScratch[i] = input[start + i];
            }
            float percussive = Median(_medianScratch, count);

            float harmonicWeight = harmonic / (harmonic + percussive + 1e-8f);
            float gain = HarmonicAttenuation + (HarmonicBoost - HarmonicAttenuation) * harmonicWeight;
            output[bin] = input[bin] * gain;
        }
    }

    private void UpdateHarmonicMask(float pitchHz, float confidence, VoicingState voicing)
    {
        if (pitchHz <= 0f || confidence < HarmonicConfidenceThreshold || voicing != VoicingState.Voiced)
        {
            if (_harmonicMaskActive)
            {
                Array.Clear(_harmonicMask, 0, _harmonicMask.Length);
                _harmonicMaskActive = false;
            }
            return;
        }

        Array.Clear(_harmonicMask, 0, _harmonicMask.Length);
        _harmonicMaskActive = true;

        int bins = _activeDisplayBins;
        float maxFrequency = _activeMaxFrequency;
        float minFrequency = _activeMinFrequency;

        for (int harmonic = 1; harmonic <= MaxHarmonics; harmonic++)
        {
            float frequency = pitchHz * harmonic;
            if (frequency > maxFrequency)
            {
                break;
            }

            float tolerance = MathF.Max(30f, frequency * HarmonicToleranceFactor);
            float startFreq = MathF.Max(minFrequency, frequency - tolerance);
            float endFreq = MathF.Min(maxFrequency, frequency + tolerance);
            float centerPos = GetDisplayPositionFromHz(frequency);
            float startPos = GetDisplayPositionFromHz(startFreq);
            float endPos = GetDisplayPositionFromHz(endFreq);

            int startBin = Math.Clamp((int)MathF.Floor(startPos), 0, bins - 1);
            int endBin = Math.Clamp((int)MathF.Ceiling(endPos), 0, bins - 1);
            float halfSpan = MathF.Max(1f, (endBin - startBin) * 0.5f);

            for (int bin = startBin; bin <= endBin; bin++)
            {
                float dist = MathF.Abs(bin - centerPos);
                float weight = 1f - dist / halfSpan;
                if (weight > _harmonicMask[bin])
                {
                    _harmonicMask[bin] = Math.Clamp(weight, 0f, 1f);
                }
            }
        }
    }

    private void ApplyHarmonicComb(float[] magnitudes)
    {
        if (!_harmonicMaskActive)
        {
            return;
        }

        for (int i = 0; i < magnitudes.Length; i++)
        {
            float mask = _harmonicMask[i];
            float gain = HarmonicAttenuation + (HarmonicBoost - HarmonicAttenuation) * mask;
            magnitudes[i] *= gain;
        }
    }

    private void ApplyTemporalSmoothing(float[] input, float[] output)
    {
        float blend = 1f - TemporalSmoothingFactor;
        for (int i = 0; i < input.Length; i++)
        {
            float current = output[i];
            output[i] = current + (input[i] - current) * blend;
        }
    }

    private void BuildDisplayGain()
    {
        for (int i = 0; i < _activeDisplayBins; i++)
        {
            float raw = _spectrumScratch[i];
            float processed = _displaySmoothed[i];
            float gain = raw > 1e-8f ? processed / raw : 0f;
            _displayGain[i] = Math.Clamp(gain, 0f, 4f);
        }
    }

    private float GetDisplayPosition(float fftBin)
    {
        int count = _fftBinDisplayPos.Length;
        if (count == 0)
        {
            return 0f;
        }

        if (fftBin <= 0f)
        {
            return _fftBinDisplayPos[0];
        }

        if (fftBin >= count - 1)
        {
            return _fftBinDisplayPos[count - 1];
        }

        int index = (int)fftBin;
        float frac = fftBin - index;
        float pos0 = _fftBinDisplayPos[index];
        float pos1 = _fftBinDisplayPos[index + 1];
        return pos0 + (pos1 - pos0) * frac;
    }

    private float GetDisplayPositionFromHz(float hz)
    {
        if (_scaledRange <= 0f)
        {
            return 0f;
        }

        float clamped = Math.Clamp(hz, _activeMinFrequency, _activeMaxFrequency);
        float scaled = FrequencyScaleUtils.ToScale(_activeScale, clamped);
        float norm = (scaled - _scaledMin) / _scaledRange;
        return Math.Clamp(norm * Math.Max(1f, _activeDisplayBins - 1), 0f, Math.Max(1f, _activeDisplayBins - 1));
    }

    private static float Median(float[] values, int count)
    {
        if (count <= 0)
        {
            return 0f;
        }

        for (int i = 1; i < count; i++)
        {
            float key = values[i];
            int j = i - 1;
            while (j >= 0 && values[j] > key)
            {
                values[j + 1] = values[j];
                j--;
            }
            values[j + 1] = key;
        }

        return values[count / 2];
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

    private static void CopyRing(float[] source, float[] destination, int framesToCopy, int stride, int startIndex, int destOffsetFrames)
    {
        if (framesToCopy <= 0 || stride <= 0)
        {
            return;
        }

        int capacity = source.Length / stride;
        int clampedFrames = Math.Min(framesToCopy, capacity);
        int destOffset = destOffsetFrames * stride;
        int firstFrames = Math.Min(clampedFrames, capacity - startIndex);
        if (firstFrames > 0)
        {
            Array.Copy(source, startIndex * stride, destination, destOffset, firstFrames * stride);
        }

        int remainingFrames = clampedFrames - firstFrames;
        if (remainingFrames > 0)
        {
            Array.Copy(source, 0, destination, destOffset + firstFrames * stride, remainingFrames * stride);
        }
    }

    private static void CopyRing(byte[] source, byte[] destination, int framesToCopy, int startIndex, int destOffsetFrames)
    {
        if (framesToCopy <= 0)
        {
            return;
        }

        int capacity = source.Length;
        int clampedFrames = Math.Min(framesToCopy, capacity);
        int destOffset = destOffsetFrames;
        int firstFrames = Math.Min(clampedFrames, capacity - startIndex);
        if (firstFrames > 0)
        {
            Array.Copy(source, startIndex, destination, destOffset, firstFrames);
        }

        int remainingFrames = clampedFrames - firstFrames;
        if (remainingFrames > 0)
        {
            Array.Copy(source, 0, destination, destOffset + firstFrames, remainingFrames);
        }
    }

}
