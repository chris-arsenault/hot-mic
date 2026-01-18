using System.Threading;
using HotMic.Core.Dsp;
using HotMic.Core.Dsp.Analysis.Speech;
using HotMic.Core.Dsp.Fft;
using HotMic.Core.Dsp.Filters;
using HotMic.Core.Dsp.Spectrogram;
using HotMic.Core.Threading;

namespace HotMic.Core.Plugins.BuiltIn;

/// <summary>
/// Vocal-focused spectrograph analysis with pitch, formant, voicing, and harmonic overlays.
/// Pass-through plugin (no audio modification).
/// </summary>
public sealed partial class VocalSpectrographPlugin : IPlugin
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
    public const int NormalizationModeIndex = 39;
    public const int DynamicRangeModeIndex = 40;
    public const int TransformTypeIndex = 41;
    public const int CqtBinsPerOctaveIndex = 42;
    public const int HarmonicDisplayModeIndex = 43;
    public const int ShowFormantBandwidthsIndex = 44;

    // Speech Coach parameters
    public const int SpeechCoachEnabledIndex = 45;
    public const int SpeechRateWindowIndex = 46;
    public const int ShowSpeechMetricsIndex = 47;
    public const int ShowSyllableMarkersIndex = 48;
    public const int ShowPauseOverlayIndex = 49;
    public const int ShowFillerMarkersIndex = 50;
    public const int FormantProfileIndex = 51;

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
    private const int MaxHarmonics = 24;
    private const float LpcWindowSeconds = 0.025f; // Praat default
    private const float TemporalSmoothingFactor = 0.3f;
    private const float ReassignMinDb = -60f;
    private const float MaxReassignBinShift = 0.5f;
    private const float MaxReassignFrameShift = 0.5f;
    private const float DefaultBrightness = 1.0f;
    private const float DefaultGamma = 0.8f;
    private const float DefaultContrast = 1.2f;
    private const float VowelEnergyMinHz = 200f;
    private const float VowelEnergyMaxHz = 1000f;
    private const float VowelEnergyRatioThreshold = 0.15f;
    private const float LpcGaussianEdgeAmplitude = 0.04f; // Praat: ~4% outside the effective window
    private static readonly float LpcGaussianSigma =
        1f / MathF.Sqrt(-2f * MathF.Log(LpcGaussianEdgeAmplitude));
    private static readonly int[] ColorLevelOptions = { 16, 24, 32, 48, 64 };

    private static readonly int[] FftSizes = { 1024, 2048, 4096, 8192 };
    /// <summary>
    /// Overlap options: 50%, 75%, 87.5%, 93.75%, 96.875%.
    /// Higher overlap = smoother time axis but more CPU. 93.75%+ recommended for professional look.
    /// </summary>
    public static readonly float[] OverlapOptions = { 0.5f, 0.75f, 0.875f, 0.9375f, 0.96875f };
    private static readonly int[] CqtBinsPerOctaveOptions = { 12, 24, 48, 96 };

    /// <summary>
    /// ZoomFFT zoom factor. Higher = better resolution but more latency.
    /// 2x zoom at 4096 FFT: ~170ms latency, 5.86 Hz resolution.
    /// </summary>
    private const int ZoomFftZoomFactor = 2;

    private readonly LockFreeRingBuffer _captureBuffer = new(CaptureBufferSize);
    private readonly VoicingDetector _voicingDetector = new();
    private readonly BiquadFilter _rumbleHighPass = new();
    private readonly SpectrogramNoiseReducer _noiseReducer = new();
    private readonly SpectrogramHpssProcessor _hpssProcessor = new();
    private readonly SpectrogramSmoother _smoother = new();
    private readonly HarmonicCombEnhancer _harmonicComb = new(MaxHarmonics);
    private readonly SpectralFeatureExtractor _featureExtractor = new();
    private readonly SpectrographTimingCollector _timing = new();
    private OnePoleHighPass _dcHighPass = new();
    private PreEmphasisFilter _preEmphasisFilter = new();
    private PreEmphasisFilter _lpcPreEmphasisFilter = new(); // Always-on pre-emphasis for LPC
    private float[] _lpcInputBuffer = Array.Empty<float>();
    private float[] _lpcWindowedBuffer = Array.Empty<float>();
    private float[] _lpcWindow = Array.Empty<float>();
    private readonly HalfbandResampler _lpcDecimator1 = new(); // 48kHz → 24kHz
    private readonly HalfbandResampler _lpcDecimator2 = new(); // 24kHz → 12kHz
    private float[] _lpcDecimateBuffer1 = Array.Empty<float>(); // Intermediate decimation buffer
    private float[] _lpcDecimatedBuffer = Array.Empty<float>(); // Final decimated buffer for LPC
    private int _lpcWindowSamples;
    private int _lpcWindowLength;
    private int _activeLpcSampleRate;
    private int _activeLpcDecimationStages;
    private float _activeFormantCeilingHz;
    private FormantProfile _activeFormantProfile = FormantProfile.Tenor;
    private FormantTrackingPreset _activeFormantPreset;
    private long _lastDroppedHops;

    private Thread? _analysisThread;
    private CancellationTokenSource? _analysisCts;

    private int _sampleRate;
    private int _activeFftSize;
    private int _activeHopSize;
    private int _activeAnalysisBins;
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
    private int _activeAnalysisSize;
    private long _frameCounter;
    private long _latestFrameId = -1;
    private int _availableFrames;
    private int _formantDiagCounter;
    private int _reassignLatencyFrames;
    private int _dataVersion;
    private FastFft? _fft;
    private ZoomFft? _zoomFft;
    private ConstantQTransform? _cqt;
    private SpectrogramTransformType _activeTransformType;
    private int _activeCqtBinsPerOctave = 48;
    private float[] _cqtMagnitudes = Array.Empty<float>();
    private float[] _cqtReal = Array.Empty<float>();
    private float[] _cqtImag = Array.Empty<float>();
    private float[] _cqtTimeReal = Array.Empty<float>();
    private float[] _cqtTimeImag = Array.Empty<float>();
    private float[] _cqtPhaseDiff = Array.Empty<float>();
    private float[] _zoomReal = Array.Empty<float>();
    private float[] _zoomImag = Array.Empty<float>();
    private float[] _zoomTimeReal = Array.Empty<float>();
    private float[] _zoomTimeImag = Array.Empty<float>();
    private float[] _zoomDerivReal = Array.Empty<float>();
    private float[] _zoomDerivImag = Array.Empty<float>();
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
    private float[] _fftDisplayMagnitudes = Array.Empty<float>();
    private float[] _aWeighting = Array.Empty<float>();
    private float[] _spectrumScratch = Array.Empty<float>();
    private float[] _displayWork = Array.Empty<float>();
    private float[] _displayProcessed = Array.Empty<float>();
    private float[] _displaySmoothed = Array.Empty<float>();
    private float[] _displayGain = Array.Empty<float>();
    private float _fftNormalization = 1f;
    private float _binResolution;
    private SpectrogramAnalysisDescriptor? _analysisDescriptor;

    // Discontinuity tracking for display continuity
    private const int MaxDiscontinuityEvents = 32;
    private readonly Queue<DiscontinuityEvent> _discontinuityEvents = new();
    private readonly object _discontinuityLock = new();

    private float[] _spectrogramBuffer = Array.Empty<float>();
    private float[] _linearMagnitudeBuffer = Array.Empty<float>();
    private float[] _pitchTrack = Array.Empty<float>();
    private float[] _pitchConfidence = Array.Empty<float>();
    private float[] _formantFrequencies = Array.Empty<float>();
    private float[] _formantBandwidths = Array.Empty<float>();
    private byte[] _voicingStates = Array.Empty<byte>();
    private float[] _harmonicFrequencies = Array.Empty<float>();
    private float[] _harmonicMagnitudes = Array.Empty<float>();
    private float[] _waveformMin = Array.Empty<float>();
    private float[] _waveformMax = Array.Empty<float>();
    private float[] _hnrTrack = Array.Empty<float>();
    private float[] _cppTrack = Array.Empty<float>();
    private float[] _spectralCentroid = Array.Empty<float>();
    private float[] _spectralSlope = Array.Empty<float>();
    private float[] _spectralFlux = Array.Empty<float>();

    private YinPitchDetector? _yinPitchDetector;
    private AutocorrelationPitchDetector? _autocorrPitchDetector;
    private CepstralPitchDetector? _cepstralPitchDetector;
    private PyinPitchDetector? _pyinPitchDetector;
    private SwipePitchDetector? _swipePitchDetector;
    private LpcAnalyzer? _lpcAnalyzer;
    private FormantTracker? _formantTracker;
    private BeamSearchFormantTracker? _beamFormantTracker;
    private float[] _lpcCoefficients = Array.Empty<float>();
    private float[] _formantFreqScratch = new float[MaxFormants];
    private float[] _formantBwScratch = new float[MaxFormants];
    private float[] _harmonicScratch = new float[MaxHarmonics];
    private float[] _harmonicMagScratch = new float[MaxHarmonics];

    // Speech Coach components
    private readonly SpeechCoach _speechCoach = new();
    private float[] _syllableRateTrack = Array.Empty<float>();
    private float[] _articulationRateTrack = Array.Empty<float>();
    private float[] _pauseRatioTrack = Array.Empty<float>();
    private float[] _monotoneScoreTrack = Array.Empty<float>();
    private float[] _clarityScoreTrack = Array.Empty<float>();
    private float[] _intelligibilityTrack = Array.Empty<float>();
    private byte[] _speakingStateTrack = Array.Empty<byte>();
    private byte[] _syllableMarkers = Array.Empty<byte>();

    private int _analysisActive;
    private int _analysisFilled;
    private int _requestedFftSize = 2048;
    private int _requestedWindow = (int)WindowFunction.Hann;
    private int _requestedOverlapIndex = 2; // Default 87.5% for smoother display
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
    private int _requestedHarmonicDisplayMode = (int)HarmonicDisplayMode.Detected;
    private int _requestedShowFormantBandwidths = 0; // Default to dots-only (like Praat)
    private int _requestedShowVoicing = 1;
    private int _requestedPreEmphasis = 1;
    private int _requestedHighPassEnabled = 1;
    private float _requestedHighPassCutoff = DefaultHighPassHz;
    private int _requestedLpcOrder = 10;
    private int _requestedReassignMode;
    private float _requestedReassignThreshold = ReassignMinDb;
    private float _requestedReassignSpread = 1f;
    private int _requestedClarityMode = (int)ClarityProcessingMode.None;
    private float _requestedClarityNoise = 1f;
    private float _requestedClarityHarmonic = 1f;
    private float _requestedClaritySmoothing = TemporalSmoothingFactor;
    private int _requestedPitchAlgorithm = (int)PitchDetectorType.Yin;
    private int _requestedAxisMode = (int)SpectrogramAxisMode.Hz;
    private int _requestedVoiceRange = (int)VocalRangeType.Tenor;
    private int _requestedFormantProfile = (int)FormantProfile.Tenor;
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
    private int _requestedNormalizationMode = (int)SpectrogramNormalizationMode.None;
    private int _requestedDynamicRangeMode = (int)SpectrogramDynamicRangeMode.Custom;
    private int _requestedTransformType = (int)SpectrogramTransformType.Fft;
    private int _requestedCqtBinsPerOctave = 48;
    private SpectrogramReassignMode _activeReassignMode;

    // Speech Coach requested parameters
    private int _requestedSpeechCoachEnabled = 0;
    private int _requestedSpeechRateWindow = 10; // seconds
    private int _requestedShowSpeechMetrics = 1;
    private int _requestedShowSyllableMarkers = 1;
    private int _requestedShowPauseOverlay = 1;
    private int _requestedShowFillerMarkers = 1;
}
