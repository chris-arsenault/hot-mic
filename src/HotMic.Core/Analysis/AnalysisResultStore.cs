using System.Threading;
using HotMic.Core.Dsp.Spectrogram;
using HotMic.Core.Plugins;

namespace HotMic.Core.Analysis;

/// <summary>
/// Thread-safe storage for all analysis results.
/// Uses version-based locking for torn-read prevention.
/// </summary>
public sealed class AnalysisResultStore : IAnalysisResultStore
{
    private const int MaxDiscontinuityEvents = 32;

    private readonly object _configLock = new();
    private readonly object _discontinuityLock = new();
    private readonly Queue<DiscontinuityEvent> _discontinuityEvents = new();
    private AnalysisConfiguration _config = new();
    private int _sampleRate;

    // Frame tracking
    private long _frameCounter;
    private long _latestFrameId = -1;
    private int _availableFrames;
    private int _dataVersion;

    // Buffer dimensions
    private int _frameCapacity;
    private int _displayBins;
    private int _analysisBins;
    private float _binResolutionHz;
    private SpectrogramTransformType _transformType;
    private SpectrogramAnalysisDescriptor? _analysisDescriptor;

    // Spectrogram buffers
    private float[] _spectrogramBuffer = Array.Empty<float>();
    private float[] _linearMagnitudeBuffer = Array.Empty<float>();

    // Pitch/voicing
    private float[] _pitchTrack = Array.Empty<float>();
    private float[] _pitchConfidence = Array.Empty<float>();
    private byte[] _voicingStates = Array.Empty<byte>();

    // Harmonics
    private float[] _harmonicFrequencies = Array.Empty<float>();
    private float[] _harmonicMagnitudes = Array.Empty<float>();

    // Waveform
    private float[] _waveformMin = Array.Empty<float>();
    private float[] _waveformMax = Array.Empty<float>();

    // Spectral features
    private float[] _spectralCentroid = Array.Empty<float>();
    private float[] _spectralSlope = Array.Empty<float>();
    private float[] _spectralFlux = Array.Empty<float>();
    private float[] _hnrTrack = Array.Empty<float>();
    private float[] _cppTrack = Array.Empty<float>();

    // Speech metrics
    private float[] _syllableRateTrack = Array.Empty<float>();
    private float[] _articulationRateTrack = Array.Empty<float>();
    private float[] _pauseRatioTrack = Array.Empty<float>();
    private float[] _monotoneScoreTrack = Array.Empty<float>();
    private float[] _clarityScoreTrack = Array.Empty<float>();
    private float[] _intelligibilityTrack = Array.Empty<float>();
    private byte[] _speakingStateTrack = Array.Empty<byte>();
    private byte[] _syllableMarkers = Array.Empty<byte>();

    // Analysis signal tracks
    private float[][] _analysisSignalTracks = Array.Empty<float[]>();

    public AnalysisConfiguration Config
    {
        get { lock (_configLock) return _config.Clone(); }
    }

    public int SampleRate => Volatile.Read(ref _sampleRate);
    public long LatestFrameId => Volatile.Read(ref _latestFrameId);
    public int AvailableFrames => Volatile.Read(ref _availableFrames);
    public int FrameCapacity => Volatile.Read(ref _frameCapacity);
    public int DisplayBins => Volatile.Read(ref _displayBins);
    public int AnalysisBins => Volatile.Read(ref _analysisBins);
    public float BinResolutionHz => Volatile.Read(ref _binResolutionHz);
    public SpectrogramTransformType TransformType => _transformType;
    public int DataVersion => Volatile.Read(ref _dataVersion);

    /// <summary>
    /// Configure the store with new dimensions. Called by orchestrator on config change.
    /// Only reallocates buffers and clears data when display dimensions change.
    /// </summary>
    public void Configure(
        int sampleRate,
        int frameCapacity,
        int displayBins,
        int analysisBins,
        float binResolutionHz,
        SpectrogramTransformType transformType,
        AnalysisConfiguration config)
    {
        Interlocked.Increment(ref _dataVersion);

        // Check if display dimensions changed (requires buffer reallocation)
        int oldFrameCapacity = _frameCapacity;
        int oldDisplayBins = _displayBins;
        bool displayDimensionsChanged = frameCapacity != oldFrameCapacity || displayBins != oldDisplayBins;

        Volatile.Write(ref _sampleRate, sampleRate);
        Volatile.Write(ref _frameCapacity, frameCapacity);
        Volatile.Write(ref _displayBins, displayBins);
        Volatile.Write(ref _analysisBins, analysisBins);
        Volatile.Write(ref _binResolutionHz, binResolutionHz);
        _transformType = transformType;

        lock (_configLock)
        {
            _config = config.Clone();
        }

        if (displayDimensionsChanged)
        {
            // Reallocate buffers - display dimensions changed
            int specLength = frameCapacity * displayBins;
            int linearLength = frameCapacity * analysisBins;
            int harmonicLength = frameCapacity * AnalysisConfiguration.MaxHarmonics;

            _spectrogramBuffer = new float[specLength];
            _linearMagnitudeBuffer = new float[linearLength];
            _pitchTrack = new float[frameCapacity];
            _pitchConfidence = new float[frameCapacity];
            _voicingStates = new byte[frameCapacity];
            _harmonicFrequencies = new float[harmonicLength];
            _harmonicMagnitudes = new float[harmonicLength];
            _waveformMin = new float[frameCapacity];
            _waveformMax = new float[frameCapacity];
            _spectralCentroid = new float[frameCapacity];
            _spectralSlope = new float[frameCapacity];
            _spectralFlux = new float[frameCapacity];
            _hnrTrack = new float[frameCapacity];
            _cppTrack = new float[frameCapacity];
            _syllableRateTrack = new float[frameCapacity];
            _articulationRateTrack = new float[frameCapacity];
            _pauseRatioTrack = new float[frameCapacity];
            _monotoneScoreTrack = new float[frameCapacity];
            _clarityScoreTrack = new float[frameCapacity];
            _intelligibilityTrack = new float[frameCapacity];
            _speakingStateTrack = new byte[frameCapacity];
            _syllableMarkers = new byte[frameCapacity];
            _analysisSignalTracks = new float[(int)AnalysisSignalId.Count][];
            for (int i = 0; i < _analysisSignalTracks.Length; i++)
            {
                _analysisSignalTracks[i] = new float[frameCapacity];
            }

            Volatile.Write(ref _frameCounter, 0);
            Volatile.Write(ref _latestFrameId, -1);
            Volatile.Write(ref _availableFrames, 0);

            // Clear discontinuity events when buffers are reallocated
            lock (_discontinuityLock)
            {
                _discontinuityEvents.Clear();
            }
        }
        else
        {
            // Only analysis parameters changed - resize linear magnitude buffer if needed
            int linearLength = frameCapacity * analysisBins;
            if (_linearMagnitudeBuffer.Length != linearLength)
            {
                _linearMagnitudeBuffer = new float[linearLength];
            }
        }

        Interlocked.Increment(ref _dataVersion);
    }

    /// <summary>
    /// Records a discontinuity event at the specified frame position.
    /// Used to mark where analysis parameters changed without clearing display.
    /// </summary>
    public void RecordDiscontinuity(DiscontinuityType type, string? description = null)
    {
        if (type == DiscontinuityType.None)
            return;

        long frameId = Volatile.Read(ref _latestFrameId);
        if (frameId < 0)
            frameId = 0;

        var evt = new DiscontinuityEvent(frameId, type, description ?? BuildDiscontinuityDescription(type));

        lock (_discontinuityLock)
        {
            if (_discontinuityEvents.Count >= MaxDiscontinuityEvents)
            {
                _discontinuityEvents.Dequeue();
            }
            _discontinuityEvents.Enqueue(evt);
        }
    }

    /// <summary>
    /// Gets discontinuity events that occurred at or after the specified frame ID.
    /// </summary>
    public IReadOnlyList<DiscontinuityEvent> GetDiscontinuities(long oldestFrameId)
    {
        lock (_discontinuityLock)
        {
            var result = new List<DiscontinuityEvent>();
            foreach (var evt in _discontinuityEvents)
            {
                if (evt.FrameId >= oldestFrameId)
                {
                    result.Add(evt);
                }
            }
            return result;
        }
    }

    private static string BuildDiscontinuityDescription(DiscontinuityType type)
    {
        var parts = new List<string>(4);
        if (type.HasFlag(DiscontinuityType.TransformChange)) parts.Add("Transform");
        if (type.HasFlag(DiscontinuityType.ResolutionChange)) parts.Add("Resolution");
        if (type.HasFlag(DiscontinuityType.FrequencyRangeChange)) parts.Add("Freq Range");
        if (type.HasFlag(DiscontinuityType.WindowChange)) parts.Add("Window");
        if (type.HasFlag(DiscontinuityType.FilterChange)) parts.Add("Filter");
        if (type.HasFlag(DiscontinuityType.TimeWindowChange)) parts.Add("Time");
        if (type.HasFlag(DiscontinuityType.OverlapChange)) parts.Add("Overlap");
        if (type.HasFlag(DiscontinuityType.BufferDrop)) parts.Add("Dropout");
        return parts.Count > 0 ? string.Join(", ", parts) : "Change";
    }

    /// <summary>
    /// Begin writing a new frame. Returns the frame index.
    /// </summary>
    public int BeginWriteFrame(long frameId)
    {
        Interlocked.Increment(ref _dataVersion);
        return (int)(frameId % _frameCapacity);
    }

    /// <summary>
    /// Write spectrogram data for a frame.
    /// </summary>
    public void WriteSpectrogramFrame(int frameIndex, ReadOnlySpan<float> displayMagnitudes)
    {
        int offset = frameIndex * _displayBins;
        int count = Math.Min(displayMagnitudes.Length, _displayBins);
        displayMagnitudes.Slice(0, count).CopyTo(_spectrogramBuffer.AsSpan(offset, count));
    }

    internal Span<float> GetSpectrogramFrameSpan(int frameIndex)
    {
        int frames = _frameCapacity;
        if (frames <= 0 || _displayBins <= 0)
        {
            return Span<float>.Empty;
        }

        int index = frameIndex;
        if (index < 0)
        {
            index = (index % frames + frames) % frames;
        }
        else if (index >= frames)
        {
            index %= frames;
        }

        int offset = index * _displayBins;
        return _spectrogramBuffer.AsSpan(offset, _displayBins);
    }

    internal void ClearSpectrogramFrame(int frameIndex)
    {
        var span = GetSpectrogramFrameSpan(frameIndex);
        span.Clear();
    }

    /// <summary>
    /// Write linear magnitude data for a frame.
    /// </summary>
    public void WriteLinearMagnitudes(int frameIndex, ReadOnlySpan<float> magnitudes)
    {
        int offset = frameIndex * _analysisBins;
        int count = Math.Min(magnitudes.Length, _analysisBins);
        magnitudes.Slice(0, count).CopyTo(_linearMagnitudeBuffer.AsSpan(offset, count));
    }

    /// <summary>
    /// Write pitch/voicing data for a frame.
    /// </summary>
    public void WritePitchFrame(int frameIndex, float pitch, float confidence, VoicingState voicing)
    {
        _pitchTrack[frameIndex] = pitch;
        _pitchConfidence[frameIndex] = confidence;
        _voicingStates[frameIndex] = (byte)voicing;
    }

    /// <summary>
    /// Write harmonic data for a frame.
    /// </summary>
    public void WriteHarmonicFrame(int frameIndex, ReadOnlySpan<float> frequencies, ReadOnlySpan<float> magnitudes, int count)
    {
        int offset = frameIndex * AnalysisConfiguration.MaxHarmonics;
        for (int i = 0; i < AnalysisConfiguration.MaxHarmonics; i++)
        {
            _harmonicFrequencies[offset + i] = i < count ? frequencies[i] : 0f;
            _harmonicMagnitudes[offset + i] = i < count ? magnitudes[i] : float.MinValue;
        }
    }

    /// <summary>
    /// Write waveform data for a frame.
    /// </summary>
    public void WriteWaveformFrame(int frameIndex, float min, float max)
    {
        _waveformMin[frameIndex] = min;
        _waveformMax[frameIndex] = max;
    }

    /// <summary>
    /// Write spectral feature data for a frame.
    /// </summary>
    public void WriteSpectralFeatures(int frameIndex, float centroid, float slope, float flux, float hnr, float cpp)
    {
        _spectralCentroid[frameIndex] = centroid;
        _spectralSlope[frameIndex] = slope;
        _spectralFlux[frameIndex] = flux;
        _hnrTrack[frameIndex] = hnr;
        _cppTrack[frameIndex] = cpp;
    }

    /// <summary>
    /// Write analysis signal values for a frame.
    /// </summary>
    public void WriteAnalysisSignalFrame(int frameIndex, ReadOnlySpan<float> values)
    {
        int count = Math.Min(values.Length, _analysisSignalTracks.Length);
        for (int i = 0; i < count; i++)
        {
            _analysisSignalTracks[i][frameIndex] = values[i];
        }
    }

    /// <summary>
    /// Write speech metrics for a frame.
    /// </summary>
    public void WriteSpeechMetrics(int frameIndex, in SpeechMetricsFrame metrics)
    {
        _syllableRateTrack[frameIndex] = metrics.SyllableRate;
        _articulationRateTrack[frameIndex] = metrics.ArticulationRate;
        _pauseRatioTrack[frameIndex] = metrics.PauseRatio;
        _monotoneScoreTrack[frameIndex] = metrics.MonotoneScore;
        _clarityScoreTrack[frameIndex] = metrics.ClarityScore;
        _intelligibilityTrack[frameIndex] = metrics.IntelligibilityScore;
        _speakingStateTrack[frameIndex] = metrics.SpeakingState;
        _syllableMarkers[frameIndex] = metrics.SyllableDetected ? (byte)1 : (byte)0;
    }

    /// <summary>
    /// End writing a frame. Updates frame counter and available frames.
    /// </summary>
    public void EndWriteFrame(long frameId)
    {
        EndWriteFrame(frameId, 0);
    }

    /// <summary>
    /// End writing a frame with an optional display latency offset (e.g., time reassignment).
    /// </summary>
    public void EndWriteFrame(long frameId, int displayLatencyFrames)
    {
        int frameCapacity = _frameCapacity;
        long latestFrameId = frameId - Math.Max(0, displayLatencyFrames);
        long oldestFrameId = Math.Max(0, frameId - frameCapacity + 1);
        int availableFrames = 0;

        if (latestFrameId >= 0 && latestFrameId >= oldestFrameId)
        {
            availableFrames = (int)Math.Min(frameCapacity, latestFrameId - oldestFrameId + 1);
            Volatile.Write(ref _latestFrameId, latestFrameId);
            Volatile.Write(ref _availableFrames, availableFrames);
        }
        else
        {
            Volatile.Write(ref _latestFrameId, -1);
            Volatile.Write(ref _availableFrames, 0);
        }

        Volatile.Write(ref _frameCounter, frameId + 1);

        Interlocked.Increment(ref _dataVersion);
    }

    /// <summary>
    /// Set the analysis descriptor for frequency mapping.
    /// </summary>
    public void SetAnalysisDescriptor(SpectrogramAnalysisDescriptor? descriptor)
    {
        Volatile.Write(ref _analysisDescriptor, descriptor);
    }

    public SpectrogramAnalysisDescriptor? GetAnalysisDescriptor()
    {
        return Volatile.Read(ref _analysisDescriptor);
    }

    /// <summary>
    /// Clear all buffers and reset frame counters.
    /// </summary>
    public void Clear()
    {
        Interlocked.Increment(ref _dataVersion);

        Array.Clear(_spectrogramBuffer);
        Array.Clear(_linearMagnitudeBuffer);
        Array.Clear(_pitchTrack);
        Array.Clear(_pitchConfidence);
        Array.Clear(_voicingStates);
        Array.Clear(_harmonicFrequencies);
        Array.Clear(_harmonicMagnitudes);
        Array.Clear(_waveformMin);
        Array.Clear(_waveformMax);
        Array.Clear(_spectralCentroid);
        Array.Clear(_spectralSlope);
        Array.Clear(_spectralFlux);
        Array.Clear(_hnrTrack);
        Array.Clear(_cppTrack);
        Array.Clear(_syllableRateTrack);
        Array.Clear(_articulationRateTrack);
        Array.Clear(_pauseRatioTrack);
        Array.Clear(_monotoneScoreTrack);
        Array.Clear(_clarityScoreTrack);
        Array.Clear(_intelligibilityTrack);
        Array.Clear(_speakingStateTrack);
        Array.Clear(_syllableMarkers);
        for (int i = 0; i < _analysisSignalTracks.Length; i++)
        {
            Array.Clear(_analysisSignalTracks[i]);
        }

        Volatile.Write(ref _frameCounter, 0);
        Volatile.Write(ref _latestFrameId, -1);
        Volatile.Write(ref _availableFrames, 0);

        Interlocked.Increment(ref _dataVersion);
    }

    // IAnalysisResultStore implementation

    public bool TryGetSpectrogramRange(
        long sinceFrameId,
        float[] magnitudes,
        out long latestFrameId,
        out int availableFrames,
        out bool fullCopy)
    {
        return TryGetRangeInternal(
            sinceFrameId,
            _spectrogramBuffer,
            magnitudes,
            _displayBins,
            out latestFrameId,
            out availableFrames,
            out fullCopy);
    }

    public bool TryGetLinearMagnitudes(
        long sinceFrameId,
        float[] magnitudes,
        out int analysisBins,
        out float binResolutionHz,
        out SpectrogramTransformType transformType,
        out long latestFrameId,
        out int availableFrames,
        out bool fullCopy)
    {
        analysisBins = _analysisBins;
        binResolutionHz = _binResolutionHz;
        transformType = _transformType;

        return TryGetRangeInternal(
            sinceFrameId,
            _linearMagnitudeBuffer,
            magnitudes,
            analysisBins,
            out latestFrameId,
            out availableFrames,
            out fullCopy);
    }

    public bool TryGetPitchRange(
        long sinceFrameId,
        float[] pitches,
        float[] confidences,
        byte[] voicing,
        out long latestFrameId,
        out int availableFrames,
        out bool fullCopy)
    {
        latestFrameId = -1;
        availableFrames = 0;
        fullCopy = false;

        int frames = _frameCapacity;
        if (frames <= 0 || pitches.Length < frames || confidences.Length < frames || voicing.Length < frames)
            return false;

        for (int attempt = 0; attempt < 2; attempt++)
        {
            int versionStart = Volatile.Read(ref _dataVersion);
            if ((versionStart & 1) != 0)
            {
                Thread.Yield();
                continue;
            }

            latestFrameId = Volatile.Read(ref _latestFrameId);
            availableFrames = Volatile.Read(ref _availableFrames);

            if (availableFrames <= 0 || latestFrameId < 0)
            {
                fullCopy = true;
                Array.Clear(pitches, 0, frames);
                Array.Clear(confidences, 0, frames);
                Array.Clear(voicing, 0, frames);
            }
            else
            {
                long oldestFrameId = latestFrameId - availableFrames + 1;
                bool needsFullCopy = sinceFrameId < 0 || sinceFrameId > latestFrameId || sinceFrameId < oldestFrameId - 1;

                if (needsFullCopy)
                {
                    fullCopy = true;
                    Array.Copy(_pitchTrack, pitches, frames);
                    Array.Copy(_pitchConfidence, confidences, frames);
                    Array.Copy(_voicingStates, voicing, frames);
                }
                else if (sinceFrameId < latestFrameId)
                {
                    CopyRingFrames(_pitchTrack, pitches, frames, sinceFrameId, latestFrameId);
                    CopyRingFrames(_pitchConfidence, confidences, frames, sinceFrameId, latestFrameId);
                    CopyRingFrames(_voicingStates, voicing, frames, sinceFrameId, latestFrameId);
                }
            }

            int versionEnd = Volatile.Read(ref _dataVersion);
            if (versionStart == versionEnd && (versionEnd & 1) == 0)
                return true;
        }

        return false;
    }

    public bool TryGetHarmonicRange(
        long sinceFrameId,
        float[] frequencies,
        float[] magnitudes,
        out long latestFrameId,
        out int availableFrames,
        out bool fullCopy)
    {
        latestFrameId = -1;
        availableFrames = 0;
        fullCopy = false;

        int frames = _frameCapacity;
        int stride = AnalysisConfiguration.MaxHarmonics;
        int length = frames * stride;

        if (frames <= 0 || frequencies.Length < length || magnitudes.Length < length)
            return false;

        return TryGetStridedRange(
            sinceFrameId,
            _harmonicFrequencies,
            _harmonicMagnitudes,
            frequencies,
            magnitudes,
            frames,
            stride,
            out latestFrameId,
            out availableFrames,
            out fullCopy);
    }

    public bool TryGetWaveformRange(
        long sinceFrameId,
        float[] min,
        float[] max,
        out long latestFrameId,
        out int availableFrames,
        out bool fullCopy)
    {
        latestFrameId = -1;
        availableFrames = 0;
        fullCopy = false;

        int frames = _frameCapacity;
        if (frames <= 0 || min.Length < frames || max.Length < frames)
            return false;

        for (int attempt = 0; attempt < 2; attempt++)
        {
            int versionStart = Volatile.Read(ref _dataVersion);
            if ((versionStart & 1) != 0)
            {
                Thread.Yield();
                continue;
            }

            latestFrameId = Volatile.Read(ref _latestFrameId);
            availableFrames = Volatile.Read(ref _availableFrames);

            if (availableFrames <= 0 || latestFrameId < 0)
            {
                fullCopy = true;
                Array.Clear(min, 0, frames);
                Array.Clear(max, 0, frames);
            }
            else
            {
                long oldestFrameId = latestFrameId - availableFrames + 1;
                bool needsFullCopy = sinceFrameId < 0 || sinceFrameId > latestFrameId || sinceFrameId < oldestFrameId - 1;

                if (needsFullCopy)
                {
                    fullCopy = true;
                    Array.Copy(_waveformMin, min, frames);
                    Array.Copy(_waveformMax, max, frames);
                }
                else if (sinceFrameId < latestFrameId)
                {
                    CopyRingFrames(_waveformMin, min, frames, sinceFrameId, latestFrameId);
                    CopyRingFrames(_waveformMax, max, frames, sinceFrameId, latestFrameId);
                }
            }

            int versionEnd = Volatile.Read(ref _dataVersion);
            if (versionStart == versionEnd && (versionEnd & 1) == 0)
                return true;
        }

        return false;
    }

    public bool TryGetSpectralFeatures(
        long sinceFrameId,
        float[] centroid,
        float[] slope,
        float[] flux,
        float[] hnr,
        float[] cpp,
        out long latestFrameId,
        out int availableFrames,
        out bool fullCopy)
    {
        latestFrameId = -1;
        availableFrames = 0;
        fullCopy = false;

        int frames = _frameCapacity;
        if (frames <= 0 || centroid.Length < frames || slope.Length < frames ||
            flux.Length < frames || hnr.Length < frames || cpp.Length < frames)
            return false;

        for (int attempt = 0; attempt < 2; attempt++)
        {
            int versionStart = Volatile.Read(ref _dataVersion);
            if ((versionStart & 1) != 0)
            {
                Thread.Yield();
                continue;
            }

            latestFrameId = Volatile.Read(ref _latestFrameId);
            availableFrames = Volatile.Read(ref _availableFrames);

            if (availableFrames <= 0 || latestFrameId < 0)
            {
                fullCopy = true;
                Array.Clear(centroid, 0, frames);
                Array.Clear(slope, 0, frames);
                Array.Clear(flux, 0, frames);
                Array.Clear(hnr, 0, frames);
                Array.Clear(cpp, 0, frames);
            }
            else
            {
                long oldestFrameId = latestFrameId - availableFrames + 1;
                bool needsFullCopy = sinceFrameId < 0 || sinceFrameId > latestFrameId || sinceFrameId < oldestFrameId - 1;

                if (needsFullCopy)
                {
                    fullCopy = true;
                    Array.Copy(_spectralCentroid, centroid, frames);
                    Array.Copy(_spectralSlope, slope, frames);
                    Array.Copy(_spectralFlux, flux, frames);
                    Array.Copy(_hnrTrack, hnr, frames);
                    Array.Copy(_cppTrack, cpp, frames);
                }
                else if (sinceFrameId < latestFrameId)
                {
                    CopyRingFrames(_spectralCentroid, centroid, frames, sinceFrameId, latestFrameId);
                    CopyRingFrames(_spectralSlope, slope, frames, sinceFrameId, latestFrameId);
                    CopyRingFrames(_spectralFlux, flux, frames, sinceFrameId, latestFrameId);
                    CopyRingFrames(_hnrTrack, hnr, frames, sinceFrameId, latestFrameId);
                    CopyRingFrames(_cppTrack, cpp, frames, sinceFrameId, latestFrameId);
                }
            }

            int versionEnd = Volatile.Read(ref _dataVersion);
            if (versionStart == versionEnd && (versionEnd & 1) == 0)
                return true;
        }

        return false;
    }

    public bool TryGetAnalysisSignalRange(
        AnalysisSignalId signal,
        long sinceFrameId,
        float[] values,
        out long latestFrameId,
        out int availableFrames,
        out bool fullCopy)
    {
        latestFrameId = -1;
        availableFrames = 0;
        fullCopy = false;

        int index = (int)signal;
        if ((uint)index >= (uint)_analysisSignalTracks.Length)
        {
            return false;
        }

        return TryGetRangeInternal(
            sinceFrameId,
            _analysisSignalTracks[index],
            values,
            1,
            out latestFrameId,
            out availableFrames,
            out fullCopy);
    }

    public bool TryGetSpeechMetrics(
        long sinceFrameId,
        float[] syllableRate,
        float[] articulationRate,
        float[] pauseRatio,
        float[] monotoneScore,
        float[] clarityScore,
        float[] intelligibility,
        byte[] speakingState,
        byte[] syllableMarkers,
        out long latestFrameId,
        out int availableFrames,
        out bool fullCopy)
    {
        latestFrameId = -1;
        availableFrames = 0;
        fullCopy = false;

        int frames = _frameCapacity;
        if (frames <= 0 ||
            syllableRate.Length < frames || articulationRate.Length < frames ||
            pauseRatio.Length < frames || monotoneScore.Length < frames ||
            clarityScore.Length < frames || intelligibility.Length < frames ||
            speakingState.Length < frames || syllableMarkers.Length < frames)
            return false;

        for (int attempt = 0; attempt < 2; attempt++)
        {
            int versionStart = Volatile.Read(ref _dataVersion);
            if ((versionStart & 1) != 0)
            {
                Thread.Yield();
                continue;
            }

            latestFrameId = Volatile.Read(ref _latestFrameId);
            availableFrames = Volatile.Read(ref _availableFrames);

            if (availableFrames <= 0 || latestFrameId < 0)
            {
                fullCopy = true;
                Array.Clear(syllableRate, 0, frames);
                Array.Clear(articulationRate, 0, frames);
                Array.Clear(pauseRatio, 0, frames);
                Array.Clear(monotoneScore, 0, frames);
                Array.Clear(clarityScore, 0, frames);
                Array.Clear(intelligibility, 0, frames);
                Array.Clear(speakingState, 0, frames);
                Array.Clear(syllableMarkers, 0, frames);
            }
            else
            {
                long oldestFrameId = latestFrameId - availableFrames + 1;
                bool needsFullCopy = sinceFrameId < 0 || sinceFrameId > latestFrameId || sinceFrameId < oldestFrameId - 1;

                if (needsFullCopy)
                {
                    fullCopy = true;
                    Array.Copy(_syllableRateTrack, syllableRate, frames);
                    Array.Copy(_articulationRateTrack, articulationRate, frames);
                    Array.Copy(_pauseRatioTrack, pauseRatio, frames);
                    Array.Copy(_monotoneScoreTrack, monotoneScore, frames);
                    Array.Copy(_clarityScoreTrack, clarityScore, frames);
                    Array.Copy(_intelligibilityTrack, intelligibility, frames);
                    Array.Copy(_speakingStateTrack, speakingState, frames);
                    Array.Copy(_syllableMarkers, syllableMarkers, frames);
                }
                else if (sinceFrameId < latestFrameId)
                {
                    CopyRingFrames(_syllableRateTrack, syllableRate, frames, sinceFrameId, latestFrameId);
                    CopyRingFrames(_articulationRateTrack, articulationRate, frames, sinceFrameId, latestFrameId);
                    CopyRingFrames(_pauseRatioTrack, pauseRatio, frames, sinceFrameId, latestFrameId);
                    CopyRingFrames(_monotoneScoreTrack, monotoneScore, frames, sinceFrameId, latestFrameId);
                    CopyRingFrames(_clarityScoreTrack, clarityScore, frames, sinceFrameId, latestFrameId);
                    CopyRingFrames(_intelligibilityTrack, intelligibility, frames, sinceFrameId, latestFrameId);
                    CopyRingFrames(_speakingStateTrack, speakingState, frames, sinceFrameId, latestFrameId);
                    CopyRingFrames(_syllableMarkers, syllableMarkers, frames, sinceFrameId, latestFrameId);
                }
            }

            int versionEnd = Volatile.Read(ref _dataVersion);
            if (versionStart == versionEnd && (versionEnd & 1) == 0)
                return true;
        }

        return false;
    }

    private bool TryGetRangeInternal(
        long sinceFrameId,
        float[] source,
        float[] destination,
        int stride,
        out long latestFrameId,
        out int availableFrames,
        out bool fullCopy)
    {
        latestFrameId = -1;
        availableFrames = 0;
        fullCopy = false;

        int frames = _frameCapacity;
        int length = frames * stride;

        if (frames <= 0 || stride <= 0 || source.Length < length || destination.Length < length)
            return false;

        for (int attempt = 0; attempt < 2; attempt++)
        {
            int versionStart = Volatile.Read(ref _dataVersion);
            if ((versionStart & 1) != 0)
            {
                Thread.Yield();
                continue;
            }

            latestFrameId = Volatile.Read(ref _latestFrameId);
            availableFrames = Volatile.Read(ref _availableFrames);

            if (availableFrames <= 0 || latestFrameId < 0)
            {
                fullCopy = true;
                Array.Clear(destination, 0, length);
            }
            else
            {
                long oldestFrameId = latestFrameId - availableFrames + 1;
                bool needsFullCopy = sinceFrameId < 0 || sinceFrameId > latestFrameId || sinceFrameId < oldestFrameId - 1;

                if (needsFullCopy)
                {
                    fullCopy = true;
                    Array.Copy(source, destination, length);
                }
                else if (sinceFrameId < latestFrameId)
                {
                    CopyRingFrames(source, destination, frames, stride, sinceFrameId, latestFrameId);
                }
            }

            int versionEnd = Volatile.Read(ref _dataVersion);
            if (versionStart == versionEnd && (versionEnd & 1) == 0)
                return true;
        }

        return false;
    }

    private bool TryGetStridedRange(
        long sinceFrameId,
        float[] source1,
        float[] source2,
        float[] dest1,
        float[] dest2,
        int frames,
        int stride,
        out long latestFrameId,
        out int availableFrames,
        out bool fullCopy)
    {
        latestFrameId = -1;
        availableFrames = 0;
        fullCopy = false;

        int length = frames * stride;

        for (int attempt = 0; attempt < 2; attempt++)
        {
            int versionStart = Volatile.Read(ref _dataVersion);
            if ((versionStart & 1) != 0)
            {
                Thread.Yield();
                continue;
            }

            latestFrameId = Volatile.Read(ref _latestFrameId);
            availableFrames = Volatile.Read(ref _availableFrames);

            if (availableFrames <= 0 || latestFrameId < 0)
            {
                fullCopy = true;
                Array.Clear(dest1, 0, length);
                Array.Clear(dest2, 0, length);
            }
            else
            {
                long oldestFrameId = latestFrameId - availableFrames + 1;
                bool needsFullCopy = sinceFrameId < 0 || sinceFrameId > latestFrameId || sinceFrameId < oldestFrameId - 1;

                if (needsFullCopy)
                {
                    fullCopy = true;
                    Array.Copy(source1, dest1, length);
                    Array.Copy(source2, dest2, length);
                }
                else if (sinceFrameId < latestFrameId)
                {
                    CopyRingFrames(source1, dest1, frames, stride, sinceFrameId, latestFrameId);
                    CopyRingFrames(source2, dest2, frames, stride, sinceFrameId, latestFrameId);
                }
            }

            int versionEnd = Volatile.Read(ref _dataVersion);
            if (versionStart == versionEnd && (versionEnd & 1) == 0)
                return true;
        }

        return false;
    }

    private static void CopyRingFrames(float[] source, float[] dest, int capacity, long sinceFrameId, long latestFrameId)
    {
        long firstFrameId = sinceFrameId + 1;
        int framesToCopy = (int)Math.Min(latestFrameId - firstFrameId + 1, capacity);
        int startIndex = (int)(firstFrameId % capacity);
        if (startIndex < 0) startIndex += capacity;

        int firstChunk = Math.Min(framesToCopy, capacity - startIndex);
        if (firstChunk > 0)
            Array.Copy(source, startIndex, dest, startIndex, firstChunk);

        int remaining = framesToCopy - firstChunk;
        if (remaining > 0)
            Array.Copy(source, 0, dest, 0, remaining);
    }

    private static void CopyRingFrames(byte[] source, byte[] dest, int capacity, long sinceFrameId, long latestFrameId)
    {
        long firstFrameId = sinceFrameId + 1;
        int framesToCopy = (int)Math.Min(latestFrameId - firstFrameId + 1, capacity);
        int startIndex = (int)(firstFrameId % capacity);
        if (startIndex < 0) startIndex += capacity;

        int firstChunk = Math.Min(framesToCopy, capacity - startIndex);
        if (firstChunk > 0)
            Array.Copy(source, startIndex, dest, startIndex, firstChunk);

        int remaining = framesToCopy - firstChunk;
        if (remaining > 0)
            Array.Copy(source, 0, dest, 0, remaining);
    }

    private static void CopyRingFrames(float[] source, float[] dest, int capacity, int stride, long sinceFrameId, long latestFrameId)
    {
        long firstFrameId = sinceFrameId + 1;
        int framesToCopy = (int)Math.Min(latestFrameId - firstFrameId + 1, capacity);
        int startIndex = (int)(firstFrameId % capacity);
        if (startIndex < 0) startIndex += capacity;

        int firstChunk = Math.Min(framesToCopy, capacity - startIndex);
        if (firstChunk > 0)
            Array.Copy(source, startIndex * stride, dest, startIndex * stride, firstChunk * stride);

        int remaining = framesToCopy - firstChunk;
        if (remaining > 0)
            Array.Copy(source, 0, dest, 0, remaining * stride);
    }
}
