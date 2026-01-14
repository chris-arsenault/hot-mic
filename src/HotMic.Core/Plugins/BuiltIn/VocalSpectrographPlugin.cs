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
    public const int ReassignThresholdIndex = 19;
    public const int ReassignSpreadIndex = 20;
    public const int ClarityModeIndex = 21;
    public const int ClarityNoiseIndex = 22;
    public const int ClarityHarmonicIndex = 23;
    public const int ClaritySmoothingIndex = 24;
    public const int PitchAlgorithmIndex = 25;
    public const int AxisModeIndex = 26;
    public const int VoiceRangeIndex = 27;
    public const int ShowRangeIndex = 28;
    public const int ShowGuidesIndex = 29;
    public const int ShowWaveformIndex = 30;
    public const int ShowSpectrumIndex = 31;
    public const int ShowPitchMeterIndex = 32;
    public const int ShowVowelSpaceIndex = 33;
    public const int SmoothingModeIndex = 34;
    public const int BrightnessIndex = 35;
    public const int GammaIndex = 36;
    public const int ContrastIndex = 37;
    public const int ColorLevelsIndex = 38;

    private const float DefaultMinFrequency = 80f;
    private const float DefaultMaxFrequency = 8000f;
    private const float DefaultMinDb = -70f;
    private const float DefaultMaxDb = 0f;
    private const float DefaultTimeWindow = 5f;
    private const float DcCutoffHz = 10f;
    private const float DefaultHighPassHz = 60f;
    private const float DefaultPreEmphasis = 0.97f;
    private const int CaptureBufferSize = 262144;
    private const int MaxFormants = 5;
    private const int MaxHarmonics = 24;
    private const int NoiseHistoryLength = 64;
    private const float NoisePercentile = 0.1f;
    private const float NoiseGateMultiplier = 2.0f;
    private const float NoiseAdaptFast = 0.2f;
    private const float NoiseAdaptSlow = 0.02f;
    private const float NoiseOverSubtractionMin = 1.2f;
    private const float NoiseOverSubtractionMax = 2.2f;
    private const float NoiseFloorMin = 0.01f;
    private const float NoiseFloorMax = 0.02f;
    private const int HpssTimeKernel = 17;
    private const int HpssFreqKernel = 17;
    private const float HpssMaskPower = 2.0f;
    private const float TemporalSmoothingFactor = 0.3f;
    private const float HarmonicBoost = 1.35f;
    private const float HarmonicAttenuation = 0.25f;
    private const float HarmonicConfidenceThreshold = 0.35f;
    private const float HarmonicToleranceCents = 50f;
    private const float ReassignMinDb = -60f;
    private const float MaxReassignBinShift = 0.5f;
    private const float MaxReassignFrameShift = 0.5f;
    private const float DefaultBrightness = 1.0f;
    private const float DefaultGamma = 0.8f;
    private const float DefaultContrast = 1.2f;
    private const int BilateralTimeRadius = 2;
    private const int BilateralFreqRadius = 2;
    private const float BilateralSigmaSpatial = 1.5f;
    private const float BilateralSigmaIntensityDb = 8f;
    private static readonly int[] ColorLevelOptions = { 16, 24, 32, 48, 64 };

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
    private float[] _noiseHistory = Array.Empty<float>();
    private int _noiseHistoryIndex;
    private int _noiseHistoryCount;
    private float[] _noiseScratch = Array.Empty<float>();
    private float[] _hpssHistory = Array.Empty<float>();
    private int _hpssHistoryIndex;
    private int _hpssHistoryCount;
    private float[] _hpssTimeScratch = Array.Empty<float>();
    private float[] _hpssFreqScratch = Array.Empty<float>();
    private float[] _smoothingHistory = Array.Empty<float>();
    private int _smoothingHistoryIndex;
    private int _smoothingHistoryCount;
    private float[] _bilateralTimeWeights = Array.Empty<float>();
    private float[] _bilateralFreqWeights = Array.Empty<float>();
    private float[] _harmonicMask = Array.Empty<float>();
    private bool _harmonicMaskActive;
    private int[] _fftBinToDisplay = Array.Empty<int>();
    private float[] _fftBinDisplayPos = Array.Empty<float>();
    private float[] _displayBinFrequencies = Array.Empty<float>();
    private float _displayFreqSum;
    private float _displayFreqSumSq;
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
    private float[] _waveformMin = Array.Empty<float>();
    private float[] _waveformMax = Array.Empty<float>();
    private float[] _hnrTrack = Array.Empty<float>();
    private float[] _cppTrack = Array.Empty<float>();
    private float[] _spectralCentroid = Array.Empty<float>();
    private float[] _spectralSlope = Array.Empty<float>();
    private float[] _spectralFlux = Array.Empty<float>();
    private float[] _fluxPrevious = Array.Empty<float>();

    private YinPitchDetector? _yinPitchDetector;
    private AutocorrelationPitchDetector? _autocorrPitchDetector;
    private CepstralPitchDetector? _cepstralPitchDetector;
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
    private float _requestedReassignThreshold = ReassignMinDb;
    private float _requestedReassignSpread = 1f;
    private int _requestedClarityMode = (int)ClarityProcessingMode.Full;
    private float _requestedClarityNoise = 1f;
    private float _requestedClarityHarmonic = 1f;
    private float _requestedClaritySmoothing = TemporalSmoothingFactor;
    private int _requestedPitchAlgorithm = (int)PitchDetectorType.Yin;
    private int _requestedAxisMode = (int)SpectrogramAxisMode.Hz;
    private int _requestedVoiceRange = (int)VocalRangeType.Tenor;
    private int _requestedShowRange = 1;
    private int _requestedShowGuides = 1;
    private int _requestedShowWaveform = 1;
    private int _requestedShowSpectrum = 1;
    private int _requestedShowPitchMeter = 1;
    private int _requestedShowVowelSpace = 1;
    private int _requestedSmoothingMode = (int)SpectrogramSmoothingMode.Ema;
    private float _requestedBrightness = DefaultBrightness;
    private float _requestedGamma = DefaultGamma;
    private float _requestedContrast = DefaultContrast;
    private int _requestedColorLevels = 32;
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
                MaxValue = 60f,
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
            },
            new PluginParameter
            {
                Index = ReassignThresholdIndex,
                Name = "Reassign Threshold",
                MinValue = -120f,
                MaxValue = -20f,
                DefaultValue = ReassignMinDb,
                Unit = "dB"
            },
            new PluginParameter
            {
                Index = ReassignSpreadIndex,
                Name = "Reassign Spread",
                MinValue = 0f,
                MaxValue = 1f,
                DefaultValue = 1f,
                Unit = "%",
                FormatValue = value => $"{Math.Clamp(value, 0f, 1f) * 100f:0}%"
            },
            new PluginParameter
            {
                Index = ClarityModeIndex,
                Name = "Clarity Mode",
                MinValue = 0f,
                MaxValue = 3f,
                DefaultValue = (float)ClarityProcessingMode.Full,
                Unit = "",
                FormatValue = value => ((ClarityProcessingMode)Math.Clamp((int)MathF.Round(value), 0, 3)).ToString()
            },
            new PluginParameter
            {
                Index = ClarityNoiseIndex,
                Name = "Clarity Noise",
                MinValue = 0f,
                MaxValue = 1f,
                DefaultValue = 1f,
                Unit = "%",
                FormatValue = value => $"{Math.Clamp(value, 0f, 1f) * 100f:0}%"
            },
            new PluginParameter
            {
                Index = ClarityHarmonicIndex,
                Name = "Clarity Harmonic",
                MinValue = 0f,
                MaxValue = 1f,
                DefaultValue = 1f,
                Unit = "%",
                FormatValue = value => $"{Math.Clamp(value, 0f, 1f) * 100f:0}%"
            },
            new PluginParameter
            {
                Index = ClaritySmoothingIndex,
                Name = "Clarity Smoothing",
                MinValue = 0f,
                MaxValue = 1f,
                DefaultValue = TemporalSmoothingFactor,
                Unit = "%",
                FormatValue = value => $"{Math.Clamp(value, 0f, 1f) * 100f:0}%"
            },
            new PluginParameter
            {
                Index = PitchAlgorithmIndex,
                Name = "Pitch Algorithm",
                MinValue = 0f,
                MaxValue = 2f,
                DefaultValue = (float)PitchDetectorType.Yin,
                Unit = "",
                FormatValue = value => ((PitchDetectorType)Math.Clamp((int)MathF.Round(value), 0, 2)).ToString()
            },
            new PluginParameter
            {
                Index = AxisModeIndex,
                Name = "Axis Mode",
                MinValue = 0f,
                MaxValue = 2f,
                DefaultValue = (float)SpectrogramAxisMode.Hz,
                Unit = "",
                FormatValue = value => ((SpectrogramAxisMode)Math.Clamp((int)MathF.Round(value), 0, 2)).ToString()
            },
            new PluginParameter
            {
                Index = VoiceRangeIndex,
                Name = "Voice Range",
                MinValue = 0f,
                MaxValue = 5f,
                DefaultValue = (float)VocalRangeType.Tenor,
                Unit = "",
                FormatValue = value => ((VocalRangeType)Math.Clamp((int)MathF.Round(value), 0, 5)).ToString()
            },
            new PluginParameter
            {
                Index = ShowRangeIndex,
                Name = "Range Overlay",
                MinValue = 0f,
                MaxValue = 1f,
                DefaultValue = 1f,
                Unit = ""
            },
            new PluginParameter
            {
                Index = ShowGuidesIndex,
                Name = "Guides",
                MinValue = 0f,
                MaxValue = 1f,
                DefaultValue = 1f,
                Unit = ""
            },
            new PluginParameter
            {
                Index = ShowWaveformIndex,
                Name = "Waveform View",
                MinValue = 0f,
                MaxValue = 1f,
                DefaultValue = 1f,
                Unit = ""
            },
            new PluginParameter
            {
                Index = ShowSpectrumIndex,
                Name = "Spectrum View",
                MinValue = 0f,
                MaxValue = 1f,
                DefaultValue = 1f,
                Unit = ""
            },
            new PluginParameter
            {
                Index = ShowPitchMeterIndex,
                Name = "Pitch Meter",
                MinValue = 0f,
                MaxValue = 1f,
                DefaultValue = 1f,
                Unit = ""
            },
            new PluginParameter
            {
                Index = ShowVowelSpaceIndex,
                Name = "Vowel View",
                MinValue = 0f,
                MaxValue = 1f,
                DefaultValue = 1f,
                Unit = ""
            },
            new PluginParameter
            {
                Index = SmoothingModeIndex,
                Name = "Smoothing Mode",
                MinValue = 0f,
                MaxValue = 2f,
                DefaultValue = (float)SpectrogramSmoothingMode.Ema,
                Unit = "",
                FormatValue = value => ((SpectrogramSmoothingMode)Math.Clamp((int)MathF.Round(value), 0, 2)).ToString()
            },
            new PluginParameter
            {
                Index = BrightnessIndex,
                Name = "Brightness",
                MinValue = 0.5f,
                MaxValue = 2f,
                DefaultValue = DefaultBrightness,
                Unit = "x",
                FormatValue = value => $"{Math.Clamp(value, 0.5f, 2f):0.00}"
            },
            new PluginParameter
            {
                Index = GammaIndex,
                Name = "Gamma",
                MinValue = 0.6f,
                MaxValue = 1.2f,
                DefaultValue = DefaultGamma,
                Unit = "",
                FormatValue = value => $"{Math.Clamp(value, 0.6f, 1.2f):0.00}"
            },
            new PluginParameter
            {
                Index = ContrastIndex,
                Name = "Contrast",
                MinValue = 0.8f,
                MaxValue = 1.5f,
                DefaultValue = DefaultContrast,
                Unit = "x",
                FormatValue = value => $"{Math.Clamp(value, 0.8f, 1.5f):0.00}"
            },
            new PluginParameter
            {
                Index = ColorLevelsIndex,
                Name = "Color Levels",
                MinValue = ColorLevelOptions[0],
                MaxValue = ColorLevelOptions[^1],
                DefaultValue = 32f,
                Unit = "",
                FormatValue = value => FormatDiscrete(value, ColorLevelOptions, "")
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

    public float ReassignThresholdDb => Volatile.Read(ref _requestedReassignThreshold);

    public float ReassignSpread => Volatile.Read(ref _requestedReassignSpread);

    public ClarityProcessingMode ClarityMode =>
        (ClarityProcessingMode)Math.Clamp(Volatile.Read(ref _requestedClarityMode), 0, 3);

    public float ClarityNoise => Volatile.Read(ref _requestedClarityNoise);

    public float ClarityHarmonic => Volatile.Read(ref _requestedClarityHarmonic);

    public float ClaritySmoothing => Volatile.Read(ref _requestedClaritySmoothing);

    public PitchDetectorType PitchAlgorithm =>
        (PitchDetectorType)Math.Clamp(Volatile.Read(ref _requestedPitchAlgorithm), 0, 2);

    public SpectrogramAxisMode AxisMode =>
        (SpectrogramAxisMode)Math.Clamp(Volatile.Read(ref _requestedAxisMode), 0, 2);

    public VocalRangeType VoiceRange =>
        (VocalRangeType)Math.Clamp(Volatile.Read(ref _requestedVoiceRange), 0, 5);

    public bool ShowRange => Volatile.Read(ref _requestedShowRange) != 0;

    public bool ShowGuides => Volatile.Read(ref _requestedShowGuides) != 0;

    public bool ShowWaveform => Volatile.Read(ref _requestedShowWaveform) != 0;

    public bool ShowSpectrum => Volatile.Read(ref _requestedShowSpectrum) != 0;

    public bool ShowPitchMeter => Volatile.Read(ref _requestedShowPitchMeter) != 0;

    public bool ShowVowelSpace => Volatile.Read(ref _requestedShowVowelSpace) != 0;

    public SpectrogramSmoothingMode SmoothingMode =>
        (SpectrogramSmoothingMode)Math.Clamp(Volatile.Read(ref _requestedSmoothingMode), 0, 2);

    public float Brightness => Volatile.Read(ref _requestedBrightness);

    public float Gamma => Volatile.Read(ref _requestedGamma);

    public float Contrast => Volatile.Read(ref _requestedContrast);

    public int ColorLevels => SelectDiscrete(Volatile.Read(ref _requestedColorLevels), ColorLevelOptions);

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
            case ReassignThresholdIndex:
                Interlocked.Exchange(ref _requestedReassignThreshold, Math.Clamp(value, -120f, -20f));
                break;
            case ReassignSpreadIndex:
                Interlocked.Exchange(ref _requestedReassignSpread, Math.Clamp(value, 0f, 1f));
                break;
            case ClarityModeIndex:
                Interlocked.Exchange(ref _requestedClarityMode, Math.Clamp((int)MathF.Round(value), 0, 3));
                break;
            case ClarityNoiseIndex:
                Interlocked.Exchange(ref _requestedClarityNoise, Math.Clamp(value, 0f, 1f));
                break;
            case ClarityHarmonicIndex:
                Interlocked.Exchange(ref _requestedClarityHarmonic, Math.Clamp(value, 0f, 1f));
                break;
            case ClaritySmoothingIndex:
                Interlocked.Exchange(ref _requestedClaritySmoothing, Math.Clamp(value, 0f, 1f));
                break;
            case PitchAlgorithmIndex:
                Interlocked.Exchange(ref _requestedPitchAlgorithm, Math.Clamp((int)MathF.Round(value), 0, 2));
                break;
            case AxisModeIndex:
                Interlocked.Exchange(ref _requestedAxisMode, Math.Clamp((int)MathF.Round(value), 0, 2));
                break;
            case VoiceRangeIndex:
                Interlocked.Exchange(ref _requestedVoiceRange, Math.Clamp((int)MathF.Round(value), 0, 5));
                break;
            case ShowRangeIndex:
                Interlocked.Exchange(ref _requestedShowRange, value >= 0.5f ? 1 : 0);
                break;
            case ShowGuidesIndex:
                Interlocked.Exchange(ref _requestedShowGuides, value >= 0.5f ? 1 : 0);
                break;
            case ShowWaveformIndex:
                Interlocked.Exchange(ref _requestedShowWaveform, value >= 0.5f ? 1 : 0);
                break;
            case ShowSpectrumIndex:
                Interlocked.Exchange(ref _requestedShowSpectrum, value >= 0.5f ? 1 : 0);
                break;
            case ShowPitchMeterIndex:
                Interlocked.Exchange(ref _requestedShowPitchMeter, value >= 0.5f ? 1 : 0);
                break;
            case ShowVowelSpaceIndex:
                Interlocked.Exchange(ref _requestedShowVowelSpace, value >= 0.5f ? 1 : 0);
                break;
            case SmoothingModeIndex:
                Interlocked.Exchange(ref _requestedSmoothingMode, Math.Clamp((int)MathF.Round(value), 0, 2));
                break;
            case BrightnessIndex:
                Interlocked.Exchange(ref _requestedBrightness, Math.Clamp(value, 0.5f, 2f));
                break;
            case GammaIndex:
                Interlocked.Exchange(ref _requestedGamma, Math.Clamp(value, 0.6f, 1.2f));
                break;
            case ContrastIndex:
                Interlocked.Exchange(ref _requestedContrast, Math.Clamp(value, 0.8f, 1.5f));
                break;
            case ColorLevelsIndex:
                Interlocked.Exchange(ref _requestedColorLevels, SelectDiscrete(value, ColorLevelOptions));
                break;
        }
    }

    public byte[] GetState()
    {
        var bytes = new byte[sizeof(float) * 39];
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
        Buffer.BlockCopy(BitConverter.GetBytes(_requestedReassignThreshold), 0, bytes, 76, 4);
        Buffer.BlockCopy(BitConverter.GetBytes(_requestedReassignSpread), 0, bytes, 80, 4);
        Buffer.BlockCopy(BitConverter.GetBytes((float)_requestedClarityMode), 0, bytes, 84, 4);
        Buffer.BlockCopy(BitConverter.GetBytes(_requestedClarityNoise), 0, bytes, 88, 4);
        Buffer.BlockCopy(BitConverter.GetBytes(_requestedClarityHarmonic), 0, bytes, 92, 4);
        Buffer.BlockCopy(BitConverter.GetBytes(_requestedClaritySmoothing), 0, bytes, 96, 4);
        Buffer.BlockCopy(BitConverter.GetBytes((float)_requestedPitchAlgorithm), 0, bytes, 100, 4);
        Buffer.BlockCopy(BitConverter.GetBytes((float)_requestedAxisMode), 0, bytes, 104, 4);
        Buffer.BlockCopy(BitConverter.GetBytes((float)_requestedVoiceRange), 0, bytes, 108, 4);
        Buffer.BlockCopy(BitConverter.GetBytes(_requestedShowRange), 0, bytes, 112, 4);
        Buffer.BlockCopy(BitConverter.GetBytes(_requestedShowGuides), 0, bytes, 116, 4);
        Buffer.BlockCopy(BitConverter.GetBytes(_requestedShowWaveform), 0, bytes, 120, 4);
        Buffer.BlockCopy(BitConverter.GetBytes(_requestedShowSpectrum), 0, bytes, 124, 4);
        Buffer.BlockCopy(BitConverter.GetBytes(_requestedShowPitchMeter), 0, bytes, 128, 4);
        Buffer.BlockCopy(BitConverter.GetBytes(_requestedShowVowelSpace), 0, bytes, 132, 4);
        Buffer.BlockCopy(BitConverter.GetBytes((float)_requestedSmoothingMode), 0, bytes, 136, 4);
        Buffer.BlockCopy(BitConverter.GetBytes(_requestedBrightness), 0, bytes, 140, 4);
        Buffer.BlockCopy(BitConverter.GetBytes(_requestedGamma), 0, bytes, 144, 4);
        Buffer.BlockCopy(BitConverter.GetBytes(_requestedContrast), 0, bytes, 148, 4);
        Buffer.BlockCopy(BitConverter.GetBytes((float)_requestedColorLevels), 0, bytes, 152, 4);
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

        if (state.Length >= sizeof(float) * 25)
        {
            SetParameter(ReassignThresholdIndex, BitConverter.ToSingle(state, 76));
            SetParameter(ReassignSpreadIndex, BitConverter.ToSingle(state, 80));
            SetParameter(ClarityModeIndex, BitConverter.ToSingle(state, 84));
            SetParameter(ClarityNoiseIndex, BitConverter.ToSingle(state, 88));
            SetParameter(ClarityHarmonicIndex, BitConverter.ToSingle(state, 92));
            SetParameter(ClaritySmoothingIndex, BitConverter.ToSingle(state, 96));
        }

        if (state.Length >= sizeof(float) * 39)
        {
            SetParameter(PitchAlgorithmIndex, BitConverter.ToSingle(state, 100));
            SetParameter(AxisModeIndex, BitConverter.ToSingle(state, 104));
            SetParameter(VoiceRangeIndex, BitConverter.ToSingle(state, 108));
            SetParameter(ShowRangeIndex, BitConverter.ToSingle(state, 112));
            SetParameter(ShowGuidesIndex, BitConverter.ToSingle(state, 116));
            SetParameter(ShowWaveformIndex, BitConverter.ToSingle(state, 120));
            SetParameter(ShowSpectrumIndex, BitConverter.ToSingle(state, 124));
            SetParameter(ShowPitchMeterIndex, BitConverter.ToSingle(state, 128));
            SetParameter(ShowVowelSpaceIndex, BitConverter.ToSingle(state, 132));
            SetParameter(SmoothingModeIndex, BitConverter.ToSingle(state, 136));
            SetParameter(BrightnessIndex, BitConverter.ToSingle(state, 140));
            SetParameter(GammaIndex, BitConverter.ToSingle(state, 144));
            SetParameter(ContrastIndex, BitConverter.ToSingle(state, 148));
            SetParameter(ColorLevelsIndex, BitConverter.ToSingle(state, 152));
        }
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
        float[] harmonicFrequencies,
        float[] waveformMin,
        float[] waveformMax,
        float[] hnrTrack,
        float[] cppTrack,
        float[] spectralCentroid,
        float[] spectralSlope,
        float[] spectralFlux)
    {
        var spectrogramBuffer = _spectrogramBuffer;
        var pitchTrackBuffer = _pitchTrack;
        var pitchConfidenceBuffer = _pitchConfidence;
        var formantFrequencyBuffer = _formantFrequencies;
        var formantBandwidthBuffer = _formantBandwidths;
        var voicingBuffer = _voicingStates;
        var harmonicBuffer = _harmonicFrequencies;
        var waveformMinBuffer = _waveformMin;
        var waveformMaxBuffer = _waveformMax;
        var hnrBuffer = _hnrTrack;
        var cppBuffer = _cppTrack;
        var centroidBuffer = _spectralCentroid;
        var slopeBuffer = _spectralSlope;
        var fluxBuffer = _spectralFlux;

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
            || harmonicBuffer.Length != harmonicLength
            || waveformMinBuffer.Length != frames
            || waveformMaxBuffer.Length != frames
            || hnrBuffer.Length != frames
            || cppBuffer.Length != frames
            || centroidBuffer.Length != frames
            || slopeBuffer.Length != frames
            || fluxBuffer.Length != frames)
        {
            return false;
        }

        if (magnitudes.Length < specLength
            || pitchTrack.Length < frames
            || pitchConfidence.Length < frames
            || formantFrequencies.Length < formantLength
            || formantBandwidths.Length < formantLength
            || voicingStates.Length < frames
            || harmonicFrequencies.Length < harmonicLength
            || waveformMin.Length < frames
            || waveformMax.Length < frames
            || hnrTrack.Length < frames
            || cppTrack.Length < frames
            || spectralCentroid.Length < frames
            || spectralSlope.Length < frames
            || spectralFlux.Length < frames)
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
                Array.Clear(waveformMin, 0, frames);
                Array.Clear(waveformMax, 0, frames);
                Array.Clear(hnrTrack, 0, frames);
                Array.Clear(cppTrack, 0, frames);
                Array.Clear(spectralCentroid, 0, frames);
                Array.Clear(spectralSlope, 0, frames);
                Array.Clear(spectralFlux, 0, frames);
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
                CopyRing(waveformMinBuffer, waveformMin, availableFrames, 1, startIndex, padFrames);
                CopyRing(waveformMaxBuffer, waveformMax, availableFrames, 1, startIndex, padFrames);
                CopyRing(hnrBuffer, hnrTrack, availableFrames, 1, startIndex, padFrames);
                CopyRing(cppBuffer, cppTrack, availableFrames, 1, startIndex, padFrames);
                CopyRing(centroidBuffer, spectralCentroid, availableFrames, 1, startIndex, padFrames);
                CopyRing(slopeBuffer, spectralSlope, availableFrames, 1, startIndex, padFrames);
                CopyRing(fluxBuffer, spectralFlux, availableFrames, 1, startIndex, padFrames);
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
        int cppFrameCounter = 0;
        float lastPitch = 0f;
        float lastConfidence = 0f;
        float lastCpp = 0f;
        float lastHnr = 0f;

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

            float waveformMin = float.MaxValue;
            float waveformMax = float.MinValue;
            for (int i = 0; i < shift; i++)
            {
                float sample = _hopBuffer[i];
                float dcRemoved = _dcHighPass.Process(sample);
                float filtered = hpfEnabled ? _rumbleHighPass.Process(dcRemoved) : dcRemoved;
                float emphasized = preEmphasis ? _preEmphasisFilter.Process(filtered) : filtered;

                _analysisBufferRaw[tail + i] = filtered;
                _analysisBufferProcessed[tail + i] = emphasized;
                if (filtered < waveformMin)
                {
                    waveformMin = filtered;
                }
                if (filtered > waveformMax)
                {
                    waveformMax = filtered;
                }
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

            // Pitch + CPP detection (raw buffer) at a reduced rate for performance.
            var pitchAlgorithm = (PitchDetectorType)Math.Clamp(Volatile.Read(ref _requestedPitchAlgorithm), 0, 2);
            pitchFrameCounter++;
            if (pitchFrameCounter >= 2)
            {
                pitchFrameCounter = 0;
                if (pitchAlgorithm == PitchDetectorType.Yin && _yinPitchDetector is not null)
                {
                    var pitch = _yinPitchDetector.Detect(_analysisBufferRaw);
                    lastPitch = pitch.FrequencyHz ?? 0f;
                    lastConfidence = pitch.Confidence;
                }
                else if (pitchAlgorithm == PitchDetectorType.Autocorrelation && _autocorrPitchDetector is not null)
                {
                    var pitch = _autocorrPitchDetector.Detect(_analysisBufferRaw);
                    lastPitch = pitch.FrequencyHz ?? 0f;
                    lastConfidence = pitch.Confidence;
                }
            }

            cppFrameCounter++;
            if (cppFrameCounter >= 2)
            {
                cppFrameCounter = 0;
                if (_cepstralPitchDetector is not null)
                {
                    var pitch = _cepstralPitchDetector.Detect(_analysisBufferRaw);
                    lastCpp = _cepstralPitchDetector.LastCpp;
                    if (pitchAlgorithm == PitchDetectorType.Cepstral)
                    {
                        lastPitch = pitch.FrequencyHz ?? 0f;
                        lastConfidence = pitch.Confidence;
                    }
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

            var clarityMode = (ClarityProcessingMode)Math.Clamp(Volatile.Read(ref _requestedClarityMode), 0, 3);
            float clarityNoise = Volatile.Read(ref _requestedClarityNoise);
            float clarityHarmonic = Volatile.Read(ref _requestedClarityHarmonic);
            float claritySmoothing = Volatile.Read(ref _requestedClaritySmoothing);
            var smoothingMode = (SpectrogramSmoothingMode)Math.Clamp(Volatile.Read(ref _requestedSmoothingMode), 0, 2);
            bool clarityEnabled = clarityMode != ClarityProcessingMode.None;
            bool useNoise = clarityMode is ClarityProcessingMode.Noise or ClarityProcessingMode.Full;
            bool useHarmonic = clarityMode is ClarityProcessingMode.Harmonic or ClarityProcessingMode.Full;
            lastHnr = 0f;

            if (!clarityEnabled)
            {
                Array.Copy(_spectrumScratch, _displaySmoothed, _activeDisplayBins);
                Array.Copy(_spectrumScratch, _displayProcessed, _activeDisplayBins);
            }
            else
            {
                Array.Copy(_spectrumScratch, _displayWork, _activeDisplayBins);
                if (useNoise && clarityNoise > 0f)
                {
                    ApplyNoiseReduction(_displayWork, clarityNoise, voicing);
                }

                if (useHarmonic && clarityHarmonic > 0f)
                {
                    ApplyHpss(_displayWork, _displayProcessed, clarityHarmonic);
                    UpdateHarmonicMask(lastPitch, lastConfidence, voicing);
                    lastHnr = ApplyHarmonicComb(_displayProcessed, clarityHarmonic);
                }
                else
                {
                    Array.Copy(_displayWork, _displayProcessed, _activeDisplayBins);
                    if (_harmonicMaskActive)
                    {
                        Array.Clear(_harmonicMask, 0, _harmonicMask.Length);
                        _harmonicMaskActive = false;
                    }
                }

                if (claritySmoothing > 0f && smoothingMode != SpectrogramSmoothingMode.Off)
                {
                    if (smoothingMode == SpectrogramSmoothingMode.Bilateral)
                    {
                        ApplyBilateralSmoothing(_displayProcessed, _displaySmoothed, claritySmoothing);
                    }
                    else
                    {
                        ApplyTemporalSmoothing(_displayProcessed, _displaySmoothed, claritySmoothing);
                    }
                }
                else
                {
                    Array.Copy(_displayProcessed, _displaySmoothed, _activeDisplayBins);
                }
            }

            float minDb = Volatile.Read(ref _requestedMinDb);
            float maxDb = Volatile.Read(ref _requestedMaxDb);
            float floorDb = Math.Min(minDb, maxDb - 1f);
            float range = MathF.Max(1f, maxDb - floorDb);

            long frameId = _frameCounter;
            int frameIndex = (int)(frameId % _activeFrameCapacity);
            Interlocked.Increment(ref _dataVersion);

            if (reassignEnabled)
            {
                int specOffset = frameIndex * _activeDisplayBins;
                Array.Clear(_spectrogramBuffer, specOffset, _activeDisplayBins);
                BuildDisplayGain();

                float reassignThresholdDb = MathF.Max(Volatile.Read(ref _requestedReassignThreshold), floorDb);
                float reassignThresholdLinear = DspUtils.DbToLinear(reassignThresholdDb);
                float reassignSpread = Math.Clamp(Volatile.Read(ref _requestedReassignSpread), 0f, 1f);
                float maxTimeShift = MaxReassignFrameShift * reassignSpread;
                float maxBinShift = MaxReassignBinShift * reassignSpread;
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
                        timeShiftFrames = Math.Clamp(timeShiftSamples * invHop, -maxTimeShift, maxTimeShift);
                    }

                    float freqShiftBins = 0f;
                    if (_activeReassignMode.HasFlag(SpectrogramReassignMode.Frequency))
                    {
                        float reDeriv = _fftDerivReal[bin];
                        float imDeriv = _fftDerivImag[bin];
                        // STFT reassignment frequency shift from the window derivative FFT.
                        float imag = (imDeriv * re - reDeriv * im) / denom;
                        freqShiftBins = Math.Clamp(imag * freqBinScale, -maxBinShift, maxBinShift);
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

            ComputeSpectralFeatures(_displaySmoothed, out float centroid, out float slope, out float flux);
            _waveformMin[frameIndex] = waveformMin == float.MaxValue ? 0f : waveformMin;
            _waveformMax[frameIndex] = waveformMax == float.MinValue ? 0f : waveformMax;
            _hnrTrack[frameIndex] = lastHnr;
            _cppTrack[frameIndex] = lastCpp;
            _spectralCentroid[frameIndex] = centroid;
            _spectralSlope[frameIndex] = slope;
            _spectralFlux[frameIndex] = flux;

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
            _noiseHistory = new float[_activeDisplayBins * NoiseHistoryLength];
            _noiseHistoryIndex = 0;
            _noiseHistoryCount = 0;
            _noiseScratch = new float[NoiseHistoryLength];
            _hpssHistory = new float[_activeDisplayBins * HpssTimeKernel];
            _hpssHistoryIndex = 0;
            _hpssHistoryCount = 0;
            _hpssTimeScratch = new float[HpssTimeKernel];
            _hpssFreqScratch = new float[HpssFreqKernel];
            _smoothingHistory = new float[_activeDisplayBins * (BilateralTimeRadius + 1)];
            _smoothingHistoryIndex = 0;
            _smoothingHistoryCount = 0;
            _bilateralTimeWeights = new float[BilateralTimeRadius + 1];
            _bilateralFreqWeights = new float[BilateralFreqRadius * 2 + 1];
            _harmonicMask = new float[_activeDisplayBins];
            _fftBinToDisplay = new int[_activeFftSize / 2];
            _fftBinDisplayPos = new float[_activeFftSize / 2];
            _displayBinFrequencies = new float[_activeDisplayBins];
            _fftNormalization = 2f / MathF.Max(1f, _activeFftSize);
            _binResolution = _sampleRate / (float)_activeFftSize;

            _spectrogramBuffer = new float[_activeFrameCapacity * _activeDisplayBins];
            _pitchTrack = new float[_activeFrameCapacity];
            _pitchConfidence = new float[_activeFrameCapacity];
            _formantFrequencies = new float[_activeFrameCapacity * MaxFormants];
            _formantBandwidths = new float[_activeFrameCapacity * MaxFormants];
            _voicingStates = new byte[_activeFrameCapacity];
            _harmonicFrequencies = new float[_activeFrameCapacity * MaxHarmonics];
            _waveformMin = new float[_activeFrameCapacity];
            _waveformMax = new float[_activeFrameCapacity];
            _hnrTrack = new float[_activeFrameCapacity];
            _cppTrack = new float[_activeFrameCapacity];
            _spectralCentroid = new float[_activeFrameCapacity];
            _spectralSlope = new float[_activeFrameCapacity];
            _spectralFlux = new float[_activeFrameCapacity];
            _fluxPrevious = new float[_activeDisplayBins];
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
                ? (int)MathF.Ceiling(MaxReassignFrameShift)
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
            _yinPitchDetector ??= new YinPitchDetector(_sampleRate, _activeFftSize, 60f, 1200f, 0.15f);
            _yinPitchDetector.Configure(_sampleRate, _activeFftSize, 60f, 1200f, 0.15f);

            _autocorrPitchDetector ??= new AutocorrelationPitchDetector(_sampleRate, _activeFftSize, 60f, 1200f, 0.3f);
            _autocorrPitchDetector.Configure(_sampleRate, _activeFftSize, 60f, 1200f, 0.3f);

            _cepstralPitchDetector ??= new CepstralPitchDetector(_sampleRate, _activeFftSize, 60f, 1200f, 2f);
            _cepstralPitchDetector.Configure(_sampleRate, _activeFftSize, 60f, 1200f, 2f);

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

        if (sizeChanged)
        {
            UpdateBilateralWeights();
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
        Array.Clear(_waveformMin, 0, _waveformMin.Length);
        Array.Clear(_waveformMax, 0, _waveformMax.Length);
        Array.Clear(_hnrTrack, 0, _hnrTrack.Length);
        Array.Clear(_cppTrack, 0, _cppTrack.Length);
        Array.Clear(_spectralCentroid, 0, _spectralCentroid.Length);
        Array.Clear(_spectralSlope, 0, _spectralSlope.Length);
        Array.Clear(_spectralFlux, 0, _spectralFlux.Length);
        Array.Clear(_noiseEstimate, 0, _noiseEstimate.Length);
        Array.Clear(_noiseHistory, 0, _noiseHistory.Length);
        Array.Clear(_displayWork, 0, _displayWork.Length);
        Array.Clear(_displayProcessed, 0, _displayProcessed.Length);
        Array.Clear(_displaySmoothed, 0, _displaySmoothed.Length);
        Array.Clear(_displayGain, 0, _displayGain.Length);
        Array.Clear(_hpssHistory, 0, _hpssHistory.Length);
        Array.Clear(_smoothingHistory, 0, _smoothingHistory.Length);
        Array.Clear(_harmonicMask, 0, _harmonicMask.Length);
        Array.Clear(_fluxPrevious, 0, _fluxPrevious.Length);
        _harmonicMaskActive = false;
        _hpssHistoryIndex = 0;
        _hpssHistoryCount = 0;
        _noiseHistoryIndex = 0;
        _noiseHistoryCount = 0;
        _smoothingHistoryIndex = 0;
        _smoothingHistoryCount = 0;

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

        var centers = _mapper.CenterFrequencies;
        if (_displayBinFrequencies.Length != centers.Length)
        {
            _displayBinFrequencies = new float[centers.Length];
        }

        float sum = 0f;
        float sumSq = 0f;
        for (int i = 0; i < centers.Length; i++)
        {
            float freq = centers[i];
            _displayBinFrequencies[i] = freq;
            sum += freq;
            sumSq += freq * freq;
        }

        _displayFreqSum = sum;
        _displayFreqSumSq = sumSq;
    }

    private void ApplyNoiseReduction(float[] magnitudes, float amount, VoicingState voicing)
    {
        int bins = magnitudes.Length;
        if (bins == 0)
        {
            return;
        }

        int historyOffset = _noiseHistoryIndex * bins;
        for (int i = 0; i < bins; i++)
        {
            float value = magnitudes[i];
            _noiseHistory[historyOffset + i] = value * value;
        }

        _noiseHistoryIndex = (_noiseHistoryIndex + 1) % NoiseHistoryLength;
        if (_noiseHistoryCount < NoiseHistoryLength)
        {
            _noiseHistoryCount++;
        }

        UpdateNoiseEstimate(voicing, bins);

        float alpha = NoiseOverSubtractionMin + (NoiseOverSubtractionMax - NoiseOverSubtractionMin) * amount;
        float beta = NoiseFloorMax + (NoiseFloorMin - NoiseFloorMax) * amount;

        for (int i = 0; i < bins; i++)
        {
            float value = magnitudes[i];
            float power = value * value;
            float noisePower = _noiseEstimate[i];
            float threshold = noisePower * NoiseGateMultiplier;

            float cleanPower = power - alpha * noisePower;
            float floor = beta * power;
            if (cleanPower < floor)
            {
                cleanPower = floor;
            }

            if (power < threshold && threshold > 1e-12f)
            {
                cleanPower *= power / threshold;
            }

            float processed = MathF.Sqrt(MathF.Max(cleanPower, 0f));
            magnitudes[i] = value + (processed - value) * amount;
        }
    }

    private void UpdateNoiseEstimate(VoicingState voicing, int bins)
    {
        int count = _noiseHistoryCount;
        if (count <= 0 || bins <= 0)
        {
            return;
        }

        int percentileIndex = Math.Clamp((int)MathF.Floor((count - 1) * NoisePercentile), 0, count - 1);
        float rate = voicing == VoicingState.Silence ? NoiseAdaptFast : NoiseAdaptSlow;

        for (int bin = 0; bin < bins; bin++)
        {
            for (int i = 0; i < count; i++)
            {
                _noiseScratch[i] = _noiseHistory[i * bins + bin];
            }

            Array.Sort(_noiseScratch, 0, count);
            float target = _noiseScratch[percentileIndex];
            float estimate = _noiseEstimate[bin];
            estimate += (target - estimate) * rate;
            _noiseEstimate[bin] = estimate;
        }
    }

    private void ApplyHpss(float[] input, float[] output, float amount)
    {
        int bins = input.Length;
        if (bins == 0)
        {
            return;
        }

        if (amount <= 0f)
        {
            Array.Copy(input, output, bins);
            return;
        }

        int historyOffset = _hpssHistoryIndex * bins;
        Array.Copy(input, 0, _hpssHistory, historyOffset, bins);
        _hpssHistoryIndex = (_hpssHistoryIndex + 1) % HpssTimeKernel;
        if (_hpssHistoryCount < HpssTimeKernel)
        {
            _hpssHistoryCount++;
        }

        int timeCount = _hpssHistoryCount;
        int halfFreq = HpssFreqKernel / 2;
        bool maskPowIsTwo = MathF.Abs(HpssMaskPower - 2f) < 1e-3f;

        for (int bin = 0; bin < bins; bin++)
        {
            for (int k = 0; k < timeCount; k++)
            {
                int index = (_hpssHistoryIndex - 1 - k + HpssTimeKernel) % HpssTimeKernel;
                _hpssTimeScratch[k] = _hpssHistory[index * bins + bin];
            }
            float harmonic = Median(_hpssTimeScratch, timeCount);

            int start = Math.Max(0, bin - halfFreq);
            int end = Math.Min(bins - 1, bin + halfFreq);
            int count = end - start + 1;
            for (int i = 0; i < count; i++)
            {
                _hpssFreqScratch[i] = input[start + i];
            }
            float percussive = Median(_hpssFreqScratch, count);

            float hPow = maskPowIsTwo ? harmonic * harmonic : MathF.Pow(harmonic, HpssMaskPower);
            float pPow = maskPowIsTwo ? percussive * percussive : MathF.Pow(percussive, HpssMaskPower);
            float mask = hPow / (hPow + pPow + 1e-12f);
            float gain = (1f - amount) + mask * amount;
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

        float ratio = MathF.Pow(2f, HarmonicToleranceCents / 1200f);
        for (int harmonic = 1; harmonic <= MaxHarmonics; harmonic++)
        {
            float frequency = pitchHz * harmonic;
            if (frequency > maxFrequency)
            {
                break;
            }

            float startFreq = MathF.Max(minFrequency, frequency / ratio);
            float endFreq = MathF.Min(maxFrequency, frequency * ratio);
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

    private float ApplyHarmonicComb(float[] magnitudes, float amount)
    {
        if (!_harmonicMaskActive || magnitudes.Length == 0)
        {
            return 0f;
        }

        float harmonicEnergy = 0f;
        float noiseEnergy = 0f;

        for (int i = 0; i < magnitudes.Length; i++)
        {
            float mask = _harmonicMask[i];
            float value = magnitudes[i];
            float power = value * value;
            harmonicEnergy += power * mask;
            noiseEnergy += power * (1f - mask);

            float targetGain = HarmonicAttenuation + (HarmonicBoost - HarmonicAttenuation) * mask;
            float gain = 1f + (targetGain - 1f) * amount;
            magnitudes[i] = value * gain;
        }

        return 10f * MathF.Log10((harmonicEnergy + 1e-9f) / (noiseEnergy + 1e-9f));
    }

    private void ApplyTemporalSmoothing(float[] input, float[] output, float amount)
    {
        float blend = 1f - amount;
        for (int i = 0; i < input.Length; i++)
        {
            float current = output[i];
            output[i] = current + (input[i] - current) * blend;
        }
    }

    private void ApplyBilateralSmoothing(float[] input, float[] output, float amount)
    {
        int bins = input.Length;
        if (bins == 0)
        {
            return;
        }

        int historyLength = BilateralTimeRadius + 1;
        int writeIndex = _smoothingHistoryIndex;
        Array.Copy(input, 0, _smoothingHistory, writeIndex * bins, bins);
        _smoothingHistoryIndex = (writeIndex + 1) % historyLength;
        if (_smoothingHistoryCount < historyLength)
        {
            _smoothingHistoryCount++;
        }

        int currentIndex = writeIndex;
        int available = _smoothingHistoryCount;
        float intensityCoeff = -0.5f / (BilateralSigmaIntensityDb * BilateralSigmaIntensityDb);

        for (int bin = 0; bin < bins; bin++)
        {
            float center = input[bin];
            float centerDb = DspUtils.LinearToDb(center);
            float sum = 0f;
            float sumW = 0f;

            int timeCount = Math.Min(available, BilateralTimeRadius + 1);
            for (int dt = 0; dt < timeCount; dt++)
            {
                int frameIndex = (currentIndex - dt + historyLength) % historyLength;
                float timeWeight = _bilateralTimeWeights[dt];
                int baseOffset = frameIndex * bins;

                for (int df = -BilateralFreqRadius; df <= BilateralFreqRadius; df++)
                {
                    int freqIndex = bin + df;
                    if (freqIndex < 0 || freqIndex >= bins)
                    {
                        continue;
                    }

                    float neighbor = _smoothingHistory[baseOffset + freqIndex];
                    float neighborDb = DspUtils.LinearToDb(neighbor);
                    float deltaDb = neighborDb - centerDb;
                    float intensityWeight = MathF.Exp(deltaDb * deltaDb * intensityCoeff);
                    float weight = timeWeight * _bilateralFreqWeights[df + BilateralFreqRadius] * intensityWeight;
                    sum += neighbor * weight;
                    sumW += weight;
                }
            }

            float filtered = sumW > 1e-6f ? sum / sumW : center;
            output[bin] = center + (filtered - center) * amount;
        }
    }

    private void UpdateBilateralWeights()
    {
        if (_bilateralTimeWeights.Length != BilateralTimeRadius + 1)
        {
            _bilateralTimeWeights = new float[BilateralTimeRadius + 1];
        }

        if (_bilateralFreqWeights.Length != BilateralFreqRadius * 2 + 1)
        {
            _bilateralFreqWeights = new float[BilateralFreqRadius * 2 + 1];
        }

        float spatialCoeff = -0.5f / (BilateralSigmaSpatial * BilateralSigmaSpatial);
        for (int dt = 0; dt <= BilateralTimeRadius; dt++)
        {
            float dist = dt;
            _bilateralTimeWeights[dt] = MathF.Exp(dist * dist * spatialCoeff);
        }

        for (int df = -BilateralFreqRadius; df <= BilateralFreqRadius; df++)
        {
            float dist = df;
            _bilateralFreqWeights[df + BilateralFreqRadius] = MathF.Exp(dist * dist * spatialCoeff);
        }
    }

    private void ComputeSpectralFeatures(float[] magnitudes, out float centroid, out float slope, out float flux)
    {
        int bins = Math.Min(magnitudes.Length, _displayBinFrequencies.Length);
        if (bins == 0)
        {
            centroid = 0f;
            slope = 0f;
            flux = 0f;
            return;
        }

        float sumMag = 0f;
        float sumWeighted = 0f;
        float sumDb = 0f;
        float sumFreqDb = 0f;
        float fluxSum = 0f;

        for (int i = 0; i < bins; i++)
        {
            float mag = magnitudes[i];
            float freq = _displayBinFrequencies[i];
            sumMag += mag;
            sumWeighted += mag * freq;

            float db = DspUtils.LinearToDb(mag);
            sumDb += db;
            sumFreqDb += db * freq;

            float diff = mag - _fluxPrevious[i];
            fluxSum += diff * diff;
            _fluxPrevious[i] = mag;
        }

        centroid = sumMag > 1e-6f ? sumWeighted / sumMag : 0f;
        float denom = bins * _displayFreqSumSq - _displayFreqSum * _displayFreqSum;
        slope = MathF.Abs(denom) > 1e-6f
            ? (bins * sumFreqDb - _displayFreqSum * sumDb) / denom * 1000f
            : 0f;
        flux = fluxSum / bins;
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
