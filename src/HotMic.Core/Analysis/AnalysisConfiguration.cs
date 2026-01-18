using HotMic.Core.Dsp;
using HotMic.Core.Dsp.Spectrogram;
using HotMic.Core.Dsp.Voice;

namespace HotMic.Core.Analysis;

/// <summary>
/// Shared analysis configuration. Changes trigger buffer reallocation.
/// </summary>
public sealed class AnalysisConfiguration
{
    public const int DefaultFftSize = 2048;
    public const int DefaultOverlapIndex = 2; // 87.5%
    public const float DefaultTimeWindow = 5f;
    public const float DefaultMinFrequency = 80f;
    public const float DefaultMaxFrequency = 8000f;
    public const int MaxFormants = 5;
    public const int MaxHarmonics = 24;
    public const int FixedDisplayBins = 512;

    public static readonly float[] OverlapOptions = { 0.5f, 0.75f, 0.875f, 0.9375f, 0.96875f };
    public static readonly int[] FftSizes = { 1024, 2048, 4096, 8192 };
    public static readonly int[] CqtBinsPerOctaveOptions = { 12, 24, 48, 96 };

    private int _fftSize = DefaultFftSize;
    private int _overlapIndex = DefaultOverlapIndex;
    private float _timeWindow = DefaultTimeWindow;
    private float _minFrequency = DefaultMinFrequency;
    private float _maxFrequency = DefaultMaxFrequency;
    private WindowFunction _windowFunction = WindowFunction.Hann;
    private FrequencyScale _frequencyScale = FrequencyScale.Mel;
    private SpectrogramTransformType _transformType = SpectrogramTransformType.Fft;
    private int _cqtBinsPerOctave = 48;
    private PitchDetectorType _pitchAlgorithm = PitchDetectorType.Yin;
    private FormantProfile _formantProfile = FormantProfile.Tenor;
    private ClarityProcessingMode _clarityMode = ClarityProcessingMode.None;
    private float _clarityNoise = 1f;
    private float _clarityHarmonic = 1f;
    private float _claritySmoothing = 0.3f;
    private SpectrogramSmoothingMode _smoothingMode = SpectrogramSmoothingMode.Ema;
    private bool _preEmphasis = true;
    private bool _highPassEnabled = true;
    private float _highPassCutoff = 60f;
    private SpectrogramReassignMode _reassignMode = SpectrogramReassignMode.Off;
    private float _reassignThreshold = -60f;
    private float _reassignSpread = 1f;
    private SpectrogramNormalizationMode _normalizationMode = SpectrogramNormalizationMode.None;

    public int FftSize
    {
        get => _fftSize;
        set => _fftSize = SelectDiscrete(value, FftSizes);
    }

    public int OverlapIndex
    {
        get => _overlapIndex;
        set => _overlapIndex = Math.Clamp(value, 0, OverlapOptions.Length - 1);
    }

    public float Overlap => OverlapOptions[_overlapIndex];

    public float TimeWindow
    {
        get => _timeWindow;
        set => _timeWindow = Math.Clamp(value, 1f, 60f);
    }

    public float MinFrequency
    {
        get => _minFrequency;
        set => _minFrequency = Math.Clamp(value, 20f, 1000f);
    }

    public float MaxFrequency
    {
        get => _maxFrequency;
        set => _maxFrequency = Math.Clamp(value, 500f, 24000f);
    }

    public WindowFunction WindowFunction
    {
        get => _windowFunction;
        set => _windowFunction = value;
    }

    public FrequencyScale FrequencyScale
    {
        get => _frequencyScale;
        set => _frequencyScale = value;
    }

    public SpectrogramTransformType TransformType
    {
        get => _transformType;
        set => _transformType = value;
    }

    public int CqtBinsPerOctave
    {
        get => _cqtBinsPerOctave;
        set => _cqtBinsPerOctave = SelectDiscrete(value, CqtBinsPerOctaveOptions);
    }

    public PitchDetectorType PitchAlgorithm
    {
        get => _pitchAlgorithm;
        set => _pitchAlgorithm = value;
    }

    public FormantProfile FormantProfile
    {
        get => _formantProfile;
        set => _formantProfile = value;
    }

    public ClarityProcessingMode ClarityMode
    {
        get => _clarityMode;
        set => _clarityMode = value;
    }

    public float ClarityNoise
    {
        get => _clarityNoise;
        set => _clarityNoise = Math.Clamp(value, 0f, 1f);
    }

    public float ClarityHarmonic
    {
        get => _clarityHarmonic;
        set => _clarityHarmonic = Math.Clamp(value, 0f, 1f);
    }

    public float ClaritySmoothing
    {
        get => _claritySmoothing;
        set => _claritySmoothing = Math.Clamp(value, 0f, 1f);
    }

    public SpectrogramSmoothingMode SmoothingMode
    {
        get => _smoothingMode;
        set => _smoothingMode = value;
    }

    public bool PreEmphasis
    {
        get => _preEmphasis;
        set => _preEmphasis = value;
    }

    public bool HighPassEnabled
    {
        get => _highPassEnabled;
        set => _highPassEnabled = value;
    }

    public float HighPassCutoff
    {
        get => _highPassCutoff;
        set => _highPassCutoff = Math.Clamp(value, 20f, 120f);
    }

    public SpectrogramReassignMode ReassignMode
    {
        get => _reassignMode;
        set => _reassignMode = value;
    }

    public float ReassignThreshold
    {
        get => _reassignThreshold;
        set => _reassignThreshold = Math.Clamp(value, -100f, 0f);
    }

    public float ReassignSpread
    {
        get => _reassignSpread;
        set => _reassignSpread = Math.Clamp(value, 0f, 1f);
    }

    public SpectrogramNormalizationMode NormalizationMode
    {
        get => _normalizationMode;
        set => _normalizationMode = value;
    }

    public int ComputeHopSize() => Math.Max(1, (int)(_fftSize * (1f - Overlap)));

    public int ComputeFrameCapacity(int sampleRate)
    {
        int hopSize = ComputeHopSize();
        return Math.Max(1, (int)MathF.Ceiling(_timeWindow * sampleRate / hopSize));
    }

    public AnalysisConfiguration Clone()
    {
        return new AnalysisConfiguration
        {
            _fftSize = _fftSize,
            _overlapIndex = _overlapIndex,
            _timeWindow = _timeWindow,
            _minFrequency = _minFrequency,
            _maxFrequency = _maxFrequency,
            _windowFunction = _windowFunction,
            _frequencyScale = _frequencyScale,
            _transformType = _transformType,
            _cqtBinsPerOctave = _cqtBinsPerOctave,
            _pitchAlgorithm = _pitchAlgorithm,
            _formantProfile = _formantProfile,
            _clarityMode = _clarityMode,
            _clarityNoise = _clarityNoise,
            _clarityHarmonic = _clarityHarmonic,
            _claritySmoothing = _claritySmoothing,
            _smoothingMode = _smoothingMode,
            _preEmphasis = _preEmphasis,
            _highPassEnabled = _highPassEnabled,
            _highPassCutoff = _highPassCutoff,
            _reassignMode = _reassignMode,
            _reassignThreshold = _reassignThreshold,
            _reassignSpread = _reassignSpread,
            _normalizationMode = _normalizationMode,
        };
    }

    private static int SelectDiscrete(int value, IReadOnlyList<int> options)
    {
        int best = options[0];
        int bestDelta = Math.Abs(options[0] - value);
        for (int i = 1; i < options.Count; i++)
        {
            int delta = Math.Abs(options[i] - value);
            if (delta < bestDelta)
            {
                bestDelta = delta;
                best = options[i];
            }
        }
        return best;
    }
}
