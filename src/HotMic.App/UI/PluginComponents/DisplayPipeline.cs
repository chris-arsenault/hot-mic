using HotMic.Core.Dsp;
using HotMic.Core.Dsp.Analysis;
using HotMic.Core.Dsp.Mapping;
using HotMic.Core.Dsp.Spectrogram;

namespace HotMic.App.UI.PluginComponents;

/// <summary>
/// Transforms analysis-resolution linear magnitudes to display-ready normalized values.
/// Owns the frequency-to-display mapping and dB normalization.
/// This moves display concerns out of the analysis layer.
/// </summary>
public sealed class DisplayPipeline
{
    private readonly SpectrumMapper _mapper = new();
    private readonly CqtSpectrumMapper _cqtMapper = new();
    private readonly SpectrogramDynamicRangeTracker _dynamicRangeTracker = new();
    private float[] _mappedBuffer = Array.Empty<float>();
    private int _displayBins;
    private int _analysisBins;
    private float _minDb;
    private float _maxDb;
    private SpectrogramTransformType _transformType;
    private SpectrogramDynamicRangeMode _dynamicRangeMode;
    private bool _configured;

    /// <summary>
    /// Number of display bins.
    /// </summary>
    public int DisplayBins => _displayBins;

    /// <summary>
    /// Center frequencies for each display bin.
    /// </summary>
    public ReadOnlySpan<float> CenterFrequencies => _transformType == SpectrogramTransformType.Cqt
        ? _cqtMapper.CenterFrequencies
        : _mapper.CenterFrequencies;

    /// <summary>
    /// Configure the pipeline for FFT-based transforms.
    /// </summary>
    public void ConfigureForFft(int analysisBins, int displayBins, int sampleRate,
        float minHz, float maxHz, FrequencyScale scale,
        float minDb, float maxDb, SpectrogramDynamicRangeMode dynamicRangeMode)
    {
        _analysisBins = analysisBins;
        _displayBins = displayBins;
        _transformType = SpectrogramTransformType.Fft;

        if (_mappedBuffer.Length < displayBins)
        {
            _mappedBuffer = new float[displayBins];
        }

        // SpectrumMapper expects fftSize, so we pass analysisBins * 2
        _mapper.Configure(analysisBins * 2, sampleRate, displayBins, minHz, maxHz, scale);
        UpdateProcessing(minDb, maxDb, dynamicRangeMode);
        _configured = true;
    }

    /// <summary>
    /// Configure the pipeline for CQT transforms.
    /// </summary>
    public void ConfigureForCqt(ReadOnlySpan<float> cqtFrequencies, int displayBins,
        float minHz, float maxHz, FrequencyScale scale,
        float minDb, float maxDb, SpectrogramDynamicRangeMode dynamicRangeMode)
    {
        _analysisBins = cqtFrequencies.Length;
        _displayBins = displayBins;
        _transformType = SpectrogramTransformType.Cqt;

        if (_mappedBuffer.Length < displayBins)
        {
            _mappedBuffer = new float[displayBins];
        }

        _cqtMapper.Configure(displayBins, cqtFrequencies, minHz, maxHz, scale);
        UpdateProcessing(minDb, maxDb, dynamicRangeMode);
        _configured = true;
    }

    /// <summary>
    /// Configure the pipeline for ZoomFFT transforms.
    /// </summary>
    public void ConfigureForZoomFft(int zoomBins, int displayBins, int sampleRate,
        float minHz, float maxHz, FrequencyScale scale,
        float minDb, float maxDb, SpectrogramDynamicRangeMode dynamicRangeMode, float binResolutionHz)
    {
        _analysisBins = zoomBins;
        _displayBins = displayBins;
        _transformType = SpectrogramTransformType.ZoomFft;

        if (_mappedBuffer.Length < displayBins)
        {
            _mappedBuffer = new float[displayBins];
        }

        // For ZoomFFT, use SpectrumMapper configured for the zoom range
        int effectiveFftSize = (int)(sampleRate / binResolutionHz);
        _mapper.Configure(effectiveFftSize, sampleRate, displayBins, minHz, maxHz, scale);
        UpdateProcessing(minDb, maxDb, dynamicRangeMode);
        _configured = true;
    }

    /// <summary>
    /// Update display-side processing parameters without rebuilding mapping.
    /// </summary>
    public void UpdateProcessing(float minDb, float maxDb, SpectrogramDynamicRangeMode dynamicRangeMode)
    {
        bool rangeChanged = MathF.Abs(minDb - _minDb) > 1e-3f || MathF.Abs(maxDb - _maxDb) > 1e-3f;
        bool modeChanged = _dynamicRangeMode != dynamicRangeMode;

        _minDb = minDb;
        _maxDb = maxDb;
        _dynamicRangeMode = dynamicRangeMode;

        if (rangeChanged || modeChanged)
        {
            _dynamicRangeTracker.Reset(minDb);
        }
    }

    /// <summary>
    /// Process a single frame from analysis-resolution linear magnitudes to normalized display values.
    /// </summary>
    /// <param name="linearMagnitudes">Input linear magnitudes at analysis resolution.</param>
    /// <param name="normalizedOutput">Output normalized values [0,1] at display resolution.</param>
    public void ProcessFrame(ReadOnlySpan<float> linearMagnitudes, Span<float> normalizedOutput, byte voicingState)
    {
        if (!_configured || normalizedOutput.Length < _displayBins)
        {
            return;
        }

        // Step 1: Map analysis bins to display bins
        if (_transformType == SpectrogramTransformType.Cqt)
        {
            _cqtMapper.MapMax(linearMagnitudes, _mappedBuffer);
        }
        else
        {
            _mapper.MapMax(linearMagnitudes, _mappedBuffer);
        }

        // Step 2: Convert to dB, apply dynamic range, normalize to [0,1]
        NormalizeDisplayMagnitudes(_mappedBuffer.AsSpan(0, _displayBins), normalizedOutput, voicingState);
    }

    /// <summary>
    /// Process multiple frames in batch.
    /// </summary>
    /// <param name="linearMagnitudes">Input buffer with frames * analysisBins elements.</param>
    /// <param name="normalizedOutput">Output buffer with frames * displayBins elements.</param>
    /// <param name="frameCount">Number of frames to process.</param>
    public void ProcessFrames(ReadOnlySpan<float> linearMagnitudes, Span<float> normalizedOutput, int frameCount,
        ReadOnlySpan<byte> voicingStates)
    {
        if (!_configured)
        {
            return;
        }

        for (int f = 0; f < frameCount; f++)
        {
            var src = linearMagnitudes.Slice(f * _analysisBins, _analysisBins);
            var dst = normalizedOutput.Slice(f * _displayBins, _displayBins);
            byte voicing = f < voicingStates.Length ? voicingStates[f] : (byte)VoicingState.Silence;
            ProcessFrame(src, dst, voicing);
        }
    }

    /// <summary>
    /// Process display-resolution linear magnitudes directly.
    /// </summary>
    public void ProcessDisplayFrames(ReadOnlySpan<float> displayMagnitudes, Span<float> normalizedOutput, int frameCount,
        ReadOnlySpan<byte> voicingStates)
    {
        if (!_configured)
        {
            return;
        }

        int frameStride = _displayBins;
        for (int f = 0; f < frameCount; f++)
        {
            var src = displayMagnitudes.Slice(f * frameStride, frameStride);
            var dst = normalizedOutput.Slice(f * frameStride, frameStride);
            byte voicing = f < voicingStates.Length ? voicingStates[f] : (byte)VoicingState.Silence;
            NormalizeDisplayMagnitudes(src, dst, voicing);
        }
    }

    private void NormalizeDisplayMagnitudes(ReadOnlySpan<float> magnitudes, Span<float> normalizedOutput, byte voicingState)
    {
        if (_displayBins <= 0)
        {
            return;
        }

        _dynamicRangeTracker.EnsureCapacity(_displayBins);

        float floorDb = _minDb;
        float ceilingDb = _maxDb;
        switch (_dynamicRangeMode)
        {
            case SpectrogramDynamicRangeMode.Full:
                floorDb = -120f;
                ceilingDb = 0f;
                break;
            case SpectrogramDynamicRangeMode.VoiceOptimized:
                floorDb = -80f;
                ceilingDb = 0f;
                break;
            case SpectrogramDynamicRangeMode.Compressed:
                floorDb = -60f;
                ceilingDb = 0f;
                break;
            case SpectrogramDynamicRangeMode.NoiseFloor:
            {
                float adaptRate = voicingState == (byte)VoicingState.Silence ? 0.2f : 0.05f;
                float adaptiveFloor = _dynamicRangeTracker.Update(magnitudes, adaptRate);
                floorDb = Math.Clamp(adaptiveFloor, _minDb, _maxDb - 1f);
                ceilingDb = _maxDb;
                break;
            }
        }

        floorDb = Math.Min(floorDb, ceilingDb - 1f);
        float range = MathF.Max(1f, ceilingDb - floorDb);

        for (int i = 0; i < _displayBins; i++)
        {
            float db = DspUtils.LinearToDb(magnitudes[i]);
            normalizedOutput[i] = Math.Clamp((db - floorDb) / range, 0f, 1f);
        }
    }
}
