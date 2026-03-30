using HotMic.Core.Dsp.Filters;

namespace HotMic.Core.Analysis;

/// <summary>
/// Maintains rolling analysis buffers and applies pre-processing filters per hop.
/// </summary>
public sealed class AnalysisBufferPipeline
{
    private int _sampleRate;
    private int _hopSize;
    private int _analysisSize;
    private int _filled;
    private bool _highPassEnabled;
    private bool _preEmphasisEnabled;
    private float _highPassCutoff;
    private float _preEmphasisFactor;
    private float _dcCutoffHz;

    private float[] _rawBuffer = Array.Empty<float>();
    private float[] _processedBuffer = Array.Empty<float>();
    private OnePoleHighPass _dcHighPass = new();
    private BiquadFilter _rumbleHighPass = new();
    private PreEmphasisFilter _preEmphasisFilter = new();

    public int HopSize => _hopSize;
    public int AnalysisSize => _analysisSize;
    public int Filled => _filled;
    public float[] RawBuffer => _rawBuffer;
    public float[] ProcessedBuffer => _processedBuffer;
    public float LastProcessedMax { get; private set; }

    public void Configure(
        int sampleRate,
        int hopSize,
        int analysisSize,
        bool highPassEnabled,
        float highPassCutoff,
        bool preEmphasisEnabled,
        float preEmphasisFactor,
        float dcCutoffHz)
    {
        sampleRate = Math.Max(1, sampleRate);
        hopSize = Math.Max(1, hopSize);
        analysisSize = Math.Max(hopSize, analysisSize);

        bool sizeChanged = analysisSize != _analysisSize || hopSize != _hopSize;
        bool filterChanged = sampleRate != _sampleRate ||
                             highPassEnabled != _highPassEnabled ||
                             MathF.Abs(highPassCutoff - _highPassCutoff) > 1e-3f ||
                             preEmphasisEnabled != _preEmphasisEnabled ||
                             MathF.Abs(preEmphasisFactor - _preEmphasisFactor) > 1e-6f ||
                             MathF.Abs(dcCutoffHz - _dcCutoffHz) > 1e-6f;

        _sampleRate = sampleRate;
        _hopSize = hopSize;
        _analysisSize = analysisSize;
        _highPassEnabled = highPassEnabled;
        _highPassCutoff = highPassCutoff;
        _preEmphasisEnabled = preEmphasisEnabled;
        _preEmphasisFactor = preEmphasisFactor;
        _dcCutoffHz = dcCutoffHz;

        if (sizeChanged)
        {
            _rawBuffer = new float[analysisSize];
            _processedBuffer = new float[analysisSize];
            _filled = 0;
        }

        if (filterChanged)
        {
            _dcHighPass.Configure(_dcCutoffHz, _sampleRate);
            _dcHighPass.Reset();
            _rumbleHighPass.SetHighPass(_sampleRate, _highPassCutoff, 0.707f);
            _rumbleHighPass.Reset();
            _preEmphasisFilter.Configure(_preEmphasisFactor);
            _preEmphasisFilter.Reset();
        }
    }

    public void Reset()
    {
        _filled = 0;
        Array.Clear(_rawBuffer, 0, _rawBuffer.Length);
        Array.Clear(_processedBuffer, 0, _processedBuffer.Length);
        _dcHighPass.Reset();
        _rumbleHighPass.Reset();
        _preEmphasisFilter.Reset();
        LastProcessedMax = 0f;
    }

    public bool ProcessHop(ReadOnlySpan<float> hop, out float waveformMin, out float waveformMax)
    {
        int shift = _hopSize;
        int analysisSize = _analysisSize;
        int tail = analysisSize - shift;

        if (shift <= 0 || analysisSize <= 0 || hop.Length < shift)
        {
            waveformMin = 0f;
            waveformMax = 0f;
            return false;
        }

        Array.Copy(_rawBuffer, shift, _rawBuffer, 0, tail);
        Array.Copy(_processedBuffer, shift, _processedBuffer, 0, tail);

        waveformMin = float.MaxValue;
        waveformMax = float.MinValue;

        float processedMax = 0f;
        for (int i = 0; i < shift; i++)
        {
            float sample = hop[i];
            float dcRemoved = _dcHighPass.Process(sample);
            float filtered = _highPassEnabled ? _rumbleHighPass.Process(dcRemoved) : dcRemoved;
            float emphasized = _preEmphasisEnabled ? _preEmphasisFilter.Process(filtered) : filtered;

            _rawBuffer[tail + i] = filtered;
            _processedBuffer[tail + i] = emphasized;
            processedMax = MathF.Max(processedMax, MathF.Abs(emphasized));

            if (filtered < waveformMin) waveformMin = filtered;
            if (filtered > waveformMax) waveformMax = filtered;
        }

        LastProcessedMax = processedMax;
        _filled = Math.Min(analysisSize, _filled + shift);
        return _filled >= analysisSize;
    }
}
