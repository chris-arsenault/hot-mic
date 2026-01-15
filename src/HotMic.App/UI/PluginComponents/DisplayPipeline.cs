using System.Runtime.CompilerServices;
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
    private readonly SpectrogramDisplayMapper _mapper = new();
    private readonly SpectrogramDynamicRangeTracker _dynamicRangeTracker = new();
    private float[] _mappedBuffer = Array.Empty<float>();
    private int _displayBins;
    private int _analysisBins;
    private float _minDb;
    private float _maxDb;
    private SpectrogramDynamicRangeMode _dynamicRangeMode;
    private bool _configured;

    /// <summary>
    /// Number of display bins.
    /// </summary>
    public int DisplayBins => _displayBins;

    /// <summary>
    /// Center frequencies for each display bin.
    /// </summary>
    public ReadOnlySpan<float> CenterFrequencies => _mapper.CenterFrequencies;

    /// <summary>
    /// Convert a frequency to normalized Y position [0,1] for overlay rendering.
    /// This uses the SAME mapping as the spectrogram display, ensuring alignment.
    /// </summary>
    /// <param name="frequencyHz">Frequency in Hz.</param>
    /// <returns>Normalized position where 0 = bottom (min freq) and 1 = top (max freq).</returns>
    public float FrequencyToNormalizedY(float frequencyHz)
    {
        if (!_configured || _displayBins <= 1)
        {
            return 0f;
        }

        float displayPos = _mapper.GetDisplayPosition(frequencyHz);
        return displayPos / (_displayBins - 1);
    }

    /// <summary>
    /// Convert a normalized Y position [0,1] back to frequency in Hz.
    /// </summary>
    /// <param name="normalizedY">Normalized position where 0 = min freq and 1 = max freq.</param>
    /// <returns>Frequency in Hz.</returns>
    public float NormalizedYToFrequency(float normalizedY)
    {
        if (!_configured || _displayBins <= 1)
        {
            return 0f;
        }

        var centers = _mapper.CenterFrequencies;
        if (centers.Length == 0)
        {
            return 0f;
        }

        float displayPos = normalizedY * (_displayBins - 1);
        int index = Math.Clamp((int)MathF.Round(displayPos), 0, centers.Length - 1);
        return centers[index];
    }

    /// <summary>
    /// Configure the pipeline for the current analysis layout.
    /// </summary>
    public void Configure(SpectrogramAnalysisDescriptor analysis, int displayBins,
        float minHz, float maxHz, FrequencyScale scale,
        float minDb, float maxDb, SpectrogramDynamicRangeMode dynamicRangeMode)
    {
        _analysisBins = analysis.BinCount;
        _displayBins = displayBins;

        if (_mappedBuffer.Length < displayBins)
        {
            _mappedBuffer = new float[displayBins];
        }

        _mapper.Configure(analysis, displayBins, minHz, maxHz, scale);
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
    [MethodImpl(MethodImplOptions.NoInlining)]
    public void ProcessFrame(ReadOnlySpan<float> linearMagnitudes, Span<float> normalizedOutput, byte voicingState)
    {
        if (!_configured || normalizedOutput.Length < _displayBins)
        {
            return;
        }

        // Step 1: Map analysis bins to display bins
        _mapper.MapMax(linearMagnitudes, _mappedBuffer);

        // Step 2: Convert to dB, apply dynamic range, normalize to [0,1]
        NormalizeDisplayMagnitudes(_mappedBuffer.AsSpan(0, _displayBins), normalizedOutput, voicingState);
    }

    /// <summary>
    /// Process multiple frames in batch.
    /// </summary>
    /// <param name="linearMagnitudes">Input buffer with frames * analysisBins elements.</param>
    /// <param name="normalizedOutput">Output buffer with frames * displayBins elements.</param>
    /// <param name="frameCount">Number of frames to process.</param>
    [MethodImpl(MethodImplOptions.NoInlining)]
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
    [MethodImpl(MethodImplOptions.NoInlining)]
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

    [MethodImpl(MethodImplOptions.NoInlining)]
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
