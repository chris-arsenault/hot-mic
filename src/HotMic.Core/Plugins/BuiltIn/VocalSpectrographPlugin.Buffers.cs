using System.Runtime.CompilerServices;
using System.Threading;
using HotMic.Core.Dsp.Analysis.Formants;
using HotMic.Core.Dsp.Fft;
using HotMic.Core.Dsp.Spectrogram;

namespace HotMic.Core.Plugins.BuiltIn;

public sealed partial class VocalSpectrographPlugin
{
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
            Volatile.Write(ref _analysisFilled, 0);
            Volatile.Write(ref _analysisActive, 1);
        }
        else
        {
            Volatile.Write(ref _analysisActive, 0);
        }
    }

    /// <summary>
    /// Copy the current display-bin magnitudes (linear) and overlay data into the provided arrays.
    /// Magnitudes are updated when reassignment is active; UI display mapping handles the standard path.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public bool CopySpectrogramData(
        float[] magnitudes,
        float[] pitchTrack,
        float[] pitchConfidence,
        float[] formantFrequencies,
        float[] formantBandwidths,
        byte[] voicingStates,
        float[] harmonicFrequencies,
        float[] harmonicMagnitudes,
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
        var harmonicMagBuffer = _harmonicMagnitudes;
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
            || harmonicMagBuffer.Length != harmonicLength
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
            || harmonicMagnitudes.Length < harmonicLength
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
                Array.Clear(harmonicMagnitudes, 0, harmonicLength);
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

                if (padFrames > 0)
                {
                    Array.Clear(magnitudes, 0, padFrames * bins);
                    Array.Clear(pitchTrack, 0, padFrames);
                    Array.Clear(pitchConfidence, 0, padFrames);
                    Array.Clear(formantFrequencies, 0, padFrames * MaxFormants);
                    Array.Clear(formantBandwidths, 0, padFrames * MaxFormants);
                    Array.Clear(voicingStates, 0, padFrames);
                    Array.Clear(harmonicFrequencies, 0, padFrames * MaxHarmonics);
                    Array.Clear(harmonicMagnitudes, 0, padFrames * MaxHarmonics);
                    Array.Clear(waveformMin, 0, padFrames);
                    Array.Clear(waveformMax, 0, padFrames);
                    Array.Clear(hnrTrack, 0, padFrames);
                    Array.Clear(cppTrack, 0, padFrames);
                    Array.Clear(spectralCentroid, 0, padFrames);
                    Array.Clear(spectralSlope, 0, padFrames);
                    Array.Clear(spectralFlux, 0, padFrames);
                }

                CopyRing(spectrogramBuffer, magnitudes, availableFrames, bins, startIndex, padFrames);
                CopyRing(pitchTrackBuffer, pitchTrack, availableFrames, 1, startIndex, padFrames);
                CopyRing(pitchConfidenceBuffer, pitchConfidence, availableFrames, 1, startIndex, padFrames);
                CopyRing(formantFrequencyBuffer, formantFrequencies, availableFrames, MaxFormants, startIndex, padFrames);
                CopyRing(formantBandwidthBuffer, formantBandwidths, availableFrames, MaxFormants, startIndex, padFrames);
                CopyRing(voicingBuffer, voicingStates, availableFrames, startIndex, padFrames);
                CopyRing(harmonicBuffer, harmonicFrequencies, availableFrames, MaxHarmonics, startIndex, padFrames);
                CopyRing(harmonicMagBuffer, harmonicMagnitudes, availableFrames, MaxHarmonics, startIndex, padFrames);
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

        return false;
    }

    /// <summary>
    /// Copy post-clarity linear magnitude data at analysis resolution.
    /// Returns false if copy failed due to buffer size mismatch or data version change.
    /// </summary>
    /// <param name="magnitudes">Output array for linear magnitudes (must be at least FrameCount * AnalysisBins).</param>
    /// <param name="analysisBins">Output: number of analysis bins per frame.</param>
    /// <param name="binResolutionHz">Output: frequency resolution per bin in Hz.</param>
    /// <param name="transformType">Output: the active transform type.</param>
    /// <returns>True if copy succeeded, false if buffers don't match or data changed during copy.</returns>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public bool CopyLinearMagnitudes(
        float[] magnitudes,
        out int analysisBins,
        out float binResolutionHz,
        out SpectrogramTransformType transformType)
    {
        analysisBins = Volatile.Read(ref _activeAnalysisBins);
        transformType = _activeTransformType;
        binResolutionHz = 0f;
        var descriptor = Volatile.Read(ref _analysisDescriptor);
        if (descriptor is not null && descriptor.IsLinear)
        {
            binResolutionHz = descriptor.BinResolutionHz;
        }
        else if (transformType == SpectrogramTransformType.ZoomFft)
        {
            binResolutionHz = _zoomFft?.BinResolutionHz ?? _binResolution;
        }
        else if (transformType == SpectrogramTransformType.Fft)
        {
            binResolutionHz = _binResolution;
        }

        int frames = Volatile.Read(ref _activeFrameCapacity);
        int requiredLength = frames * analysisBins;

        if (analysisBins <= 0 || frames <= 0)
        {
            return false;
        }

        if (magnitudes.Length < requiredLength || _linearMagnitudeBuffer.Length < requiredLength)
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
                Array.Clear(magnitudes, 0, requiredLength);
            }
            else
            {
                long oldestFrameId = latestFrameId - availableFrames + 1;
                int startIndex = (int)(oldestFrameId % frames);
                int padFrames = Math.Max(0, frames - availableFrames);

                if (padFrames > 0)
                {
                    Array.Clear(magnitudes, 0, padFrames * analysisBins);
                }

                CopyRing(_linearMagnitudeBuffer, magnitudes, availableFrames, analysisBins, startIndex, padFrames);
            }

            int versionEnd = Volatile.Read(ref _dataVersion);
            if (versionStart == versionEnd && (versionEnd & 1) == 0)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Incrementally copy display-bin magnitudes and overlay data since the provided frame ID.
    /// Arrays are treated as ring buffers keyed by frameId % FrameCount.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public bool CopySpectrogramUpdates(
        long sinceFrameId,
        float[] magnitudes,
        float[] pitchTrack,
        float[] pitchConfidence,
        float[] formantFrequencies,
        float[] formantBandwidths,
        byte[] voicingStates,
        float[] harmonicFrequencies,
        float[] harmonicMagnitudes,
        float[] waveformMin,
        float[] waveformMax,
        float[] hnrTrack,
        float[] cppTrack,
        float[] spectralCentroid,
        float[] spectralSlope,
        float[] spectralFlux,
        out long latestFrameId,
        out int availableFrames,
        out bool fullCopy)
    {
        latestFrameId = -1;
        availableFrames = 0;
        fullCopy = false;

        var spectrogramBuffer = _spectrogramBuffer;
        var pitchTrackBuffer = _pitchTrack;
        var pitchConfidenceBuffer = _pitchConfidence;
        var formantFrequencyBuffer = _formantFrequencies;
        var formantBandwidthBuffer = _formantBandwidths;
        var voicingBuffer = _voicingStates;
        var harmonicBuffer = _harmonicFrequencies;
        var harmonicMagBuffer = _harmonicMagnitudes;
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
            || harmonicMagBuffer.Length != harmonicLength
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
            || harmonicMagnitudes.Length < harmonicLength
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

            fullCopy = false;
            latestFrameId = Volatile.Read(ref _latestFrameId);
            availableFrames = Volatile.Read(ref _availableFrames);
            if (availableFrames <= 0 || latestFrameId < 0)
            {
                fullCopy = true;
                Array.Clear(magnitudes, 0, specLength);
                Array.Clear(pitchTrack, 0, frames);
                Array.Clear(pitchConfidence, 0, frames);
                Array.Clear(formantFrequencies, 0, formantLength);
                Array.Clear(formantBandwidths, 0, formantLength);
                Array.Clear(voicingStates, 0, frames);
                Array.Clear(harmonicFrequencies, 0, harmonicLength);
                Array.Clear(harmonicMagnitudes, 0, harmonicLength);
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
                bool needsFullCopy = sinceFrameId < 0
                    || sinceFrameId > latestFrameId
                    || sinceFrameId < oldestFrameId - 1;

                if (needsFullCopy)
                {
                    fullCopy = true;
                    Array.Copy(spectrogramBuffer, magnitudes, specLength);
                    Array.Copy(pitchTrackBuffer, pitchTrack, frames);
                    Array.Copy(pitchConfidenceBuffer, pitchConfidence, frames);
                    Array.Copy(formantFrequencyBuffer, formantFrequencies, formantLength);
                    Array.Copy(formantBandwidthBuffer, formantBandwidths, formantLength);
                    Array.Copy(voicingBuffer, voicingStates, frames);
                    Array.Copy(harmonicBuffer, harmonicFrequencies, harmonicLength);
                    Array.Copy(harmonicMagBuffer, harmonicMagnitudes, harmonicLength);
                    Array.Copy(waveformMinBuffer, waveformMin, frames);
                    Array.Copy(waveformMaxBuffer, waveformMax, frames);
                    Array.Copy(hnrBuffer, hnrTrack, frames);
                    Array.Copy(cppBuffer, cppTrack, frames);
                    Array.Copy(centroidBuffer, spectralCentroid, frames);
                    Array.Copy(slopeBuffer, spectralSlope, frames);
                    Array.Copy(fluxBuffer, spectralFlux, frames);
                }
                else if (sinceFrameId < latestFrameId)
                {
                    long firstFrameId = sinceFrameId + 1;
                    int framesToCopy = (int)Math.Min(latestFrameId - firstFrameId + 1, frames);
                    int startIndex = (int)(firstFrameId % frames);
                    if (startIndex < 0)
                    {
                        startIndex += frames;
                    }

                    CopyRingFrames(spectrogramBuffer, magnitudes, framesToCopy, bins, startIndex);
                    CopyRingFrames(pitchTrackBuffer, pitchTrack, framesToCopy, 1, startIndex);
                    CopyRingFrames(pitchConfidenceBuffer, pitchConfidence, framesToCopy, 1, startIndex);
                    CopyRingFrames(formantFrequencyBuffer, formantFrequencies, framesToCopy, MaxFormants, startIndex);
                    CopyRingFrames(formantBandwidthBuffer, formantBandwidths, framesToCopy, MaxFormants, startIndex);
                    CopyRingFrames(voicingBuffer, voicingStates, framesToCopy, startIndex);
                    CopyRingFrames(harmonicBuffer, harmonicFrequencies, framesToCopy, MaxHarmonics, startIndex);
                    CopyRingFrames(harmonicMagBuffer, harmonicMagnitudes, framesToCopy, MaxHarmonics, startIndex);
                    CopyRingFrames(waveformMinBuffer, waveformMin, framesToCopy, 1, startIndex);
                    CopyRingFrames(waveformMaxBuffer, waveformMax, framesToCopy, 1, startIndex);
                    CopyRingFrames(hnrBuffer, hnrTrack, framesToCopy, 1, startIndex);
                    CopyRingFrames(cppBuffer, cppTrack, framesToCopy, 1, startIndex);
                    CopyRingFrames(centroidBuffer, spectralCentroid, framesToCopy, 1, startIndex);
                    CopyRingFrames(slopeBuffer, spectralSlope, framesToCopy, 1, startIndex);
                    CopyRingFrames(fluxBuffer, spectralFlux, framesToCopy, 1, startIndex);
                }
            }

            int versionEnd = Volatile.Read(ref _dataVersion);
            if (versionStart == versionEnd && (versionEnd & 1) == 0)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Incrementally copy analysis-resolution linear magnitudes since the provided frame ID.
    /// Array is treated as a ring buffer keyed by frameId % FrameCount.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public bool CopyLinearMagnitudesUpdates(
        long sinceFrameId,
        float[] magnitudes,
        out int analysisBins,
        out float binResolutionHz,
        out SpectrogramTransformType transformType,
        out long latestFrameId,
        out int availableFrames,
        out bool fullCopy)
    {
        analysisBins = Volatile.Read(ref _activeAnalysisBins);
        transformType = _activeTransformType;
        binResolutionHz = 0f;
        latestFrameId = -1;
        availableFrames = 0;
        fullCopy = false;

        var descriptor = Volatile.Read(ref _analysisDescriptor);
        if (descriptor is not null && descriptor.IsLinear)
        {
            binResolutionHz = descriptor.BinResolutionHz;
        }
        else if (transformType == SpectrogramTransformType.ZoomFft)
        {
            binResolutionHz = _zoomFft?.BinResolutionHz ?? _binResolution;
        }
        else if (transformType == SpectrogramTransformType.Fft)
        {
            binResolutionHz = _binResolution;
        }

        int frames = Volatile.Read(ref _activeFrameCapacity);
        int requiredLength = frames * analysisBins;

        if (analysisBins <= 0 || frames <= 0)
        {
            return false;
        }

        if (magnitudes.Length < requiredLength || _linearMagnitudeBuffer.Length < requiredLength)
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

            fullCopy = false;
            latestFrameId = Volatile.Read(ref _latestFrameId);
            availableFrames = Volatile.Read(ref _availableFrames);
            if (availableFrames <= 0 || latestFrameId < 0)
            {
                fullCopy = true;
                Array.Clear(magnitudes, 0, requiredLength);
            }
            else
            {
                long oldestFrameId = latestFrameId - availableFrames + 1;
                bool needsFullCopy = sinceFrameId < 0
                    || sinceFrameId > latestFrameId
                    || sinceFrameId < oldestFrameId - 1;

                if (needsFullCopy)
                {
                    fullCopy = true;
                    Array.Copy(_linearMagnitudeBuffer, magnitudes, requiredLength);
                }
                else if (sinceFrameId < latestFrameId)
                {
                    long firstFrameId = sinceFrameId + 1;
                    int framesToCopy = (int)Math.Min(latestFrameId - firstFrameId + 1, frames);
                    int startIndex = (int)(firstFrameId % frames);
                    if (startIndex < 0)
                    {
                        startIndex += frames;
                    }

                    CopyRingFrames(_linearMagnitudeBuffer, magnitudes, framesToCopy, analysisBins, startIndex);
                }
            }

            int versionEnd = Volatile.Read(ref _dataVersion);
            if (versionStart == versionEnd && (versionEnd & 1) == 0)
            {
                return true;
            }
        }

        return false;
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
        // LPC order minimum is 12 for decimated analysis at 12kHz (even if user sets lower)
        int lpcOrder = Math.Clamp(Volatile.Read(ref _requestedLpcOrder), 12, 24);
        float hpfCutoff = Volatile.Read(ref _requestedHighPassCutoff);
        bool hpfEnabled = Volatile.Read(ref _requestedHighPassEnabled) != 0;
        bool preEmphasisEnabled = Volatile.Read(ref _requestedPreEmphasis) != 0;
        var transformType = (SpectrogramTransformType)Math.Clamp(Volatile.Read(ref _requestedTransformType), 0, 2);
        int cqtBinsPerOctave = SelectDiscrete(Volatile.Read(ref _requestedCqtBinsPerOctave), CqtBinsPerOctaveOptions);

        // Detect what changed (excluding force - that's handled separately)
        bool fftSizeChanged = !force && fftSize != _activeFftSize;
        bool overlapChanged = !force && overlapIndex != _activeOverlapIndex;
        bool timeWindowChanged = !force && MathF.Abs(timeWindow - _activeTimeWindow) > 1e-3f;
        bool sizeChanged = force || fftSizeChanged || overlapChanged || timeWindowChanged;

        bool scaleChanged = !force && scale != _activeScale;
        bool frequencyRangeChanged = !force && (MathF.Abs(minHz - _activeMinFrequency) > 1e-3f
            || MathF.Abs(maxHz - _activeMaxFrequency) > 1e-3f);
        bool mappingChanged = force || scaleChanged || frequencyRangeChanged;

        bool windowChanged = !force && window != _activeWindow;
        bool filterChanged = !force && (MathF.Abs(hpfCutoff - _activeHighPassCutoff) > 1e-3f
            || hpfEnabled != _activeHighPassEnabled
            || preEmphasisEnabled != _activePreEmphasisEnabled);
        bool lpcChanged = force || _lpcAnalyzer is null || lpcOrder != _lpcAnalyzer.Order;
        bool reassignChanged = !force && reassignMode != _activeReassignMode;
        bool transformChanged = !force && transformType != _activeTransformType;
        bool cqtChanged = !force && cqtBinsPerOctave != _activeCqtBinsPerOctave;

        // Track discontinuity type and whether buffers need reallocation
        var discontinuity = DiscontinuityType.None;
        bool reallocateBuffers = force; // Force always requires full reallocation

        if (sizeChanged)
        {
            int oldFrameCapacity = _activeFrameCapacity;

            _activeFftSize = fftSize;
            _activeOverlapIndex = overlapIndex;
            _activeTimeWindow = timeWindow;
            _activeHopSize = Math.Max(1, (int)(fftSize * (1f - OverlapOptions[overlapIndex])));
            _activeFrameCapacity = Math.Max(1, (int)MathF.Ceiling(timeWindow * _sampleRate / _activeHopSize));
            _activeAnalysisSize = fftSize;
            _analysisFilled = 0;

            // FFT working buffers - no need to preserve, these are per-frame scratch
            _fft = new FastFft(_activeFftSize);
            _analysisBufferRaw = new float[_activeFftSize];
            _analysisBufferProcessed = new float[_activeFftSize];
            _lpcInputBuffer = new float[_activeFftSize]; // LPC always uses pre-emphasis
            _lpcDecimateBuffer1 = new float[LpcWindowSamples / 2]; // 48kHz→24kHz intermediate
            _lpcDecimatedBuffer = new float[LpcWindowSamples / 4]; // Final 12kHz buffer
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
            _fftDisplayMagnitudes = new float[_activeFftSize / 2];
            _aWeighting = new float[_activeFftSize / 2];
            _fftNormalization = 2f / MathF.Max(1f, _activeFftSize);
            _binResolution = _sampleRate / (float)_activeFftSize;
            UpdateAWeighting();

            // Overlay buffers - preserve existing data when resizing
            int framesToCopy = Math.Min(oldFrameCapacity, _activeFrameCapacity);

            // Simple per-frame buffers
            _pitchTrack = ResizePreserve(_pitchTrack, _activeFrameCapacity, framesToCopy);
            _pitchConfidence = ResizePreserve(_pitchConfidence, _activeFrameCapacity, framesToCopy);
            _waveformMin = ResizePreserve(_waveformMin, _activeFrameCapacity, framesToCopy);
            _waveformMax = ResizePreserve(_waveformMax, _activeFrameCapacity, framesToCopy);
            _hnrTrack = ResizePreserve(_hnrTrack, _activeFrameCapacity, framesToCopy);
            _cppTrack = ResizePreserve(_cppTrack, _activeFrameCapacity, framesToCopy);
            _spectralCentroid = ResizePreserve(_spectralCentroid, _activeFrameCapacity, framesToCopy);
            _spectralSlope = ResizePreserve(_spectralSlope, _activeFrameCapacity, framesToCopy);
            _spectralFlux = ResizePreserve(_spectralFlux, _activeFrameCapacity, framesToCopy);
            _voicingStates = ResizePreserve(_voicingStates, _activeFrameCapacity, framesToCopy);

            // Multi-value per-frame buffers
            _formantFrequencies = ResizePreserveStrided(_formantFrequencies, _activeFrameCapacity, MaxFormants, framesToCopy);
            _formantBandwidths = ResizePreserveStrided(_formantBandwidths, _activeFrameCapacity, MaxFormants, framesToCopy);
            _harmonicFrequencies = ResizePreserveStrided(_harmonicFrequencies, _activeFrameCapacity, MaxHarmonics, framesToCopy);
            _harmonicMagnitudes = ResizePreserveStrided(_harmonicMagnitudes, _activeFrameCapacity, MaxHarmonics, framesToCopy);

            // Don't set reallocateBuffers - let spectrogram buffer logic preserve data
            if (fftSizeChanged) discontinuity |= DiscontinuityType.ResolutionChange;
            if (overlapChanged) discontinuity |= DiscontinuityType.OverlapChange;
            if (timeWindowChanged) discontinuity |= DiscontinuityType.TimeWindowChange;
        }

        // Refill the window buffer when size changes to avoid zeroed FFT input.
        if (windowChanged || sizeChanged)
        {
            _activeWindow = window;
            WindowFunctions.Fill(_fftWindow, window);
            UpdateWindowNormalization();
            UpdateReassignWindows();
            if (windowChanged) discontinuity |= DiscontinuityType.WindowChange;
        }

        if (mappingChanged || sizeChanged)
        {
            _activeScale = scale;
            _activeMinFrequency = minHz;
            _activeMaxFrequency = maxHz;
            _swipePitchDetector ??= new SwipePitchDetector(_sampleRate, _activeFftSize, minHz, maxHz);
            _swipePitchDetector.Configure(_sampleRate, _activeFftSize, minHz, maxHz);
            if (frequencyRangeChanged) discontinuity |= DiscontinuityType.FrequencyRangeChange;
            // Note: scaleChanged is display-only, no discontinuity needed
        }

        if (reassignChanged || sizeChanged)
        {
            _activeReassignMode = reassignMode;
            _reassignLatencyFrames = _activeReassignMode.HasFlag(SpectrogramReassignMode.Time)
                ? (int)MathF.Ceiling(MaxReassignFrameShift)
                : 0;
            // Reassign mode change doesn't need a discontinuity marker
        }

        // Configure Zoom FFT when selected
        if (transformType == SpectrogramTransformType.ZoomFft && (transformChanged || mappingChanged || sizeChanged))
        {
            _zoomFft ??= new ZoomFft();
            _zoomFft.Configure(_sampleRate, _activeFftSize, minHz, maxHz, ZoomFftZoomFactor, window);

            // ZoomFFT requires larger analysis buffer for true zoom resolution
            int requiredSize = _zoomFft.RequiredInputSize;
            if (requiredSize > _analysisBufferRaw.Length)
            {
                _analysisBufferRaw = new float[requiredSize];
                _analysisBufferProcessed = new float[requiredSize];
                _lpcInputBuffer = new float[requiredSize];
                _analysisFilled = 0;
            }
            _activeAnalysisSize = requiredSize;

            // Allocate ZoomFFT reassignment buffers (at output bins = fftSize/2)
            int zoomBins = _zoomFft.OutputBins;
            if (_zoomReal.Length < zoomBins)
            {
                _zoomReal = new float[zoomBins];
                _zoomImag = new float[zoomBins];
                _zoomTimeReal = new float[zoomBins];
                _zoomTimeImag = new float[zoomBins];
                _zoomDerivReal = new float[zoomBins];
                _zoomDerivImag = new float[zoomBins];
            }
        }

        // Configure CQT when selected
        if (transformType == SpectrogramTransformType.Cqt && (transformChanged || mappingChanged || cqtChanged))
        {
            _cqt ??= new ConstantQTransform();
            _cqt.Configure(_sampleRate, minHz, maxHz, cqtBinsPerOctave);

            // CQT requires larger analysis buffers for low frequencies
            int requiredSize = _cqt.MaxWindowLength;
            if (requiredSize > _analysisBufferRaw.Length)
            {
                _analysisBufferRaw = new float[requiredSize];
                _analysisBufferProcessed = new float[requiredSize];
                _lpcInputBuffer = new float[requiredSize];
                _analysisFilled = 0;
            }
            _activeAnalysisSize = requiredSize;

            // Allocate CQT buffers if needed (including reassignment buffers)
            if (_cqtMagnitudes.Length < _cqt.BinCount)
            {
                _cqtMagnitudes = new float[_cqt.BinCount];
                _cqtReal = new float[_cqt.BinCount];
                _cqtImag = new float[_cqt.BinCount];
                _cqtTimeReal = new float[_cqt.BinCount];
                _cqtTimeImag = new float[_cqt.BinCount];
                _cqtPhaseDiff = new float[_cqt.BinCount];
            }

            _activeCqtBinsPerOctave = cqtBinsPerOctave;
            if (cqtChanged) discontinuity |= DiscontinuityType.ResolutionChange;
        }

        // Reset analysis size when switching to standard FFT (away from CQT or ZoomFFT)
        if (transformType == SpectrogramTransformType.Fft && _activeAnalysisSize != _activeFftSize)
        {
            _activeAnalysisSize = _activeFftSize;
            _analysisFilled = 0;
        }

        int desiredAnalysisBins = transformType switch
        {
            SpectrogramTransformType.Cqt => _cqt?.BinCount ?? _activeAnalysisBins,
            SpectrogramTransformType.ZoomFft => _zoomFft?.OutputBins ?? _activeAnalysisBins,
            _ => _activeFftSize / 2
        };
        desiredAnalysisBins = Math.Max(1, desiredAnalysisBins);

        // Display bins are FIXED to preserve visual continuity across transform changes.
        // The DisplayPipeline handles mapping from any analysis resolution to display resolution.
        // Using 512 as a reasonable default that works for most window sizes.
        const int FixedDisplayBins = 512;
        bool analysisBinsChanged = desiredAnalysisBins != _activeAnalysisBins;
        bool displayBinsNeedsInit = _activeDisplayBins == 0;

        if (analysisBinsChanged)
        {
            _activeAnalysisBins = desiredAnalysisBins;

            // Clarity processing buffers at analysis resolution (no display cap).
            _spectrumScratch = new float[_activeAnalysisBins];
            _displayWork = new float[_activeAnalysisBins];
            _displayProcessed = new float[_activeAnalysisBins];
            _displaySmoothed = new float[_activeAnalysisBins];
            _displayGain = new float[_activeAnalysisBins];

            // DSP components at analysis resolution for full-resolution clarity processing.
            _noiseReducer.EnsureCapacity(_activeAnalysisBins);
            _hpssProcessor.EnsureCapacity(_activeAnalysisBins);
            _smoother.EnsureCapacity(_activeAnalysisBins);
            _harmonicComb.EnsureCapacity(_activeAnalysisBins);
            _featureExtractor.EnsureCapacity(_activeAnalysisBins);

            // Analysis bins change triggers discontinuity but NOT display buffer clear
            if (transformChanged) discontinuity |= DiscontinuityType.TransformChange;
        }

        // Reallocate linear magnitude buffer when analysis bins OR frame capacity changes
        if (analysisBinsChanged || sizeChanged)
        {
            _linearMagnitudeBuffer = new float[_activeFrameCapacity * _activeAnalysisBins];
        }

        // Initialize display bins on first run only - never changes after that
        if (displayBinsNeedsInit)
        {
            _activeDisplayBins = FixedDisplayBins;
        }

        // Display buffer resize: preserve existing data when only frame capacity changes
        int requiredSpectrogramSize = _activeFrameCapacity * _activeDisplayBins;
        if (_spectrogramBuffer.Length != requiredSpectrogramSize)
        {
            var oldBuffer = _spectrogramBuffer;
            int oldCapacity = oldBuffer.Length / Math.Max(1, _activeDisplayBins);
            _spectrogramBuffer = new float[requiredSpectrogramSize];

            // Copy existing frames if we have compatible data
            if (oldBuffer.Length > 0 && !displayBinsNeedsInit)
            {
                int framesToCopy = Math.Min(oldCapacity, _activeFrameCapacity);
                int elementsToCopy = framesToCopy * _activeDisplayBins;
                if (elementsToCopy > 0 && elementsToCopy <= oldBuffer.Length)
                {
                    Array.Copy(oldBuffer, 0, _spectrogramBuffer, 0, elementsToCopy);
                    // Record discontinuity but don't clear
                    if (sizeChanged) discontinuity |= DiscontinuityType.ResolutionChange;
                }
            }
            else
            {
                // First init - need full clear
                reallocateBuffers = true;
            }
        }

        if (analysisBinsChanged || transformChanged || sizeChanged || mappingChanged || cqtChanged)
        {
            UpdateAnalysisDescriptor(transformType);
        }

        if (transformChanged)
        {
            _activeTransformType = transformType;
            // Discontinuity already recorded in analysisBinsChanged block if applicable
            if (!analysisBinsChanged) discontinuity |= DiscontinuityType.TransformChange;
        }

        if (sizeChanged || filterChanged)
        {
            _dcHighPass.Configure(DcCutoffHz, _sampleRate);
            _dcHighPass.Reset();
            _rumbleHighPass.SetHighPass(_sampleRate, hpfCutoff, 0.707f);
            _rumbleHighPass.Reset();
            _activeHighPassCutoff = hpfCutoff;
            _activeHighPassEnabled = hpfEnabled;

            _preEmphasisFilter.Configure(DefaultPreEmphasis);
            _preEmphasisFilter.Reset();
            _activePreEmphasisEnabled = preEmphasisEnabled;
            if (filterChanged) discontinuity |= DiscontinuityType.FilterChange;
        }

        if (sizeChanged || lpcChanged || frequencyRangeChanged)
        {
            // Pitch range should cover display range; Nyquist/2 is practical limit for time-domain methods
            float maxPitch = MathF.Min(_sampleRate * 0.25f, maxHz);
            _yinPitchDetector ??= new YinPitchDetector(_sampleRate, _activeFftSize, 50f, maxPitch, 0.15f);
            _yinPitchDetector.Configure(_sampleRate, _activeFftSize, 50f, maxPitch, 0.15f);

            _pyinPitchDetector ??= new PyinPitchDetector(_sampleRate, _activeFftSize, 50f, maxPitch, 0.15f);
            _pyinPitchDetector.Configure(_sampleRate, _activeFftSize, 50f, maxPitch, 0.15f);

            _autocorrPitchDetector ??= new AutocorrelationPitchDetector(_sampleRate, _activeFftSize, 50f, maxPitch, 0.3f);
            _autocorrPitchDetector.Configure(_sampleRate, _activeFftSize, 50f, maxPitch, 0.3f);

            _cepstralPitchDetector ??= new CepstralPitchDetector(_sampleRate, _activeFftSize, 50f, maxPitch, 2f);
            _cepstralPitchDetector.Configure(_sampleRate, _activeFftSize, 50f, maxPitch, 2f);

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

            // Initialize beam-search tracker (V2)
            if (_beamFormantTracker is null)
            {
                _beamFormantTracker = new BeamSearchFormantTracker(lpcOrder, MaxFormants, beamWidth: 8);
            }
            else
            {
                _beamFormantTracker.Configure(lpcOrder, MaxFormants, beamWidth: 8);
            }

            _lpcCoefficients = new float[lpcOrder + 1];
        }

        // Apply changes based on what's needed
        if (reallocateBuffers)
        {
            // Buffer structure changed - must clear and restart
            ClearVisualizationBuffers();
        }
        else if (discontinuity != DiscontinuityType.None)
        {
            // Analysis parameters changed but buffer structure is same - preserve display, reset state
            RecordDiscontinuity(discontinuity);
            ResetAnalysisState();
        }
    }

    /// <summary>
    /// Records a discontinuity event at the current frame position.
    /// </summary>
    private void RecordDiscontinuity(DiscontinuityType type)
    {
        if (type == DiscontinuityType.None)
        {
            return;
        }

        long frameId = Volatile.Read(ref _frameCounter);
        string description = BuildDiscontinuityDescription(type);
        var evt = new DiscontinuityEvent(frameId, type, description);

        lock (_discontinuityLock)
        {
            if (_discontinuityEvents.Count >= MaxDiscontinuityEvents)
            {
                _discontinuityEvents.Dequeue();
            }
            _discontinuityEvents.Enqueue(evt);
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
    /// Resets analysis DSP state without clearing the display buffers.
    /// This preserves visual continuity while ensuring clean analysis restart.
    /// </summary>
    private void ResetAnalysisState()
    {
        _analysisFilled = 0;
        Interlocked.Increment(ref _dataVersion);

        // Reset DSP processors
        _noiseReducer.Reset();
        _hpssProcessor.Reset();
        _smoother.Reset();
        _harmonicComb.Reset();
        _featureExtractor.Reset();
        _cqt?.Reset();
        _zoomFft?.Reset();

        // Clear working buffers (not display buffers)
        Array.Clear(_displayWork, 0, _displayWork.Length);
        Array.Clear(_displayProcessed, 0, _displayProcessed.Length);
        Array.Clear(_displaySmoothed, 0, _displaySmoothed.Length);
        Array.Clear(_displayGain, 0, _displayGain.Length);

        Interlocked.Increment(ref _dataVersion);
    }

    private void ClearVisualizationBuffers()
    {
        Interlocked.Increment(ref _dataVersion);

        Array.Clear(_spectrogramBuffer, 0, _spectrogramBuffer.Length);
        Array.Clear(_linearMagnitudeBuffer, 0, _linearMagnitudeBuffer.Length);
        Array.Clear(_pitchTrack, 0, _pitchTrack.Length);
        Array.Clear(_pitchConfidence, 0, _pitchConfidence.Length);
        Array.Clear(_formantFrequencies, 0, _formantFrequencies.Length);
        Array.Clear(_formantBandwidths, 0, _formantBandwidths.Length);
        Array.Clear(_voicingStates, 0, _voicingStates.Length);
        Array.Clear(_harmonicFrequencies, 0, _harmonicFrequencies.Length);
        Array.Clear(_harmonicMagnitudes, 0, _harmonicMagnitudes.Length);
        Array.Clear(_waveformMin, 0, _waveformMin.Length);
        Array.Clear(_waveformMax, 0, _waveformMax.Length);
        Array.Clear(_hnrTrack, 0, _hnrTrack.Length);
        Array.Clear(_cppTrack, 0, _cppTrack.Length);
        Array.Clear(_spectralCentroid, 0, _spectralCentroid.Length);
        Array.Clear(_spectralSlope, 0, _spectralSlope.Length);
        Array.Clear(_spectralFlux, 0, _spectralFlux.Length);
        Array.Clear(_displayWork, 0, _displayWork.Length);
        Array.Clear(_displayProcessed, 0, _displayProcessed.Length);
        Array.Clear(_displaySmoothed, 0, _displaySmoothed.Length);
        Array.Clear(_displayGain, 0, _displayGain.Length);

        _noiseReducer.Reset();
        _hpssProcessor.Reset();
        _smoother.Reset();
        _harmonicComb.Reset();
        _featureExtractor.Reset();
        _cqt?.Reset();
        _zoomFft?.Reset();

        Volatile.Write(ref _frameCounter, 0);
        Volatile.Write(ref _latestFrameId, -1);
        Volatile.Write(ref _availableFrames, 0);

        // Clear discontinuity events since frame counter is reset
        lock (_discontinuityLock)
        {
            _discontinuityEvents.Clear();
        }

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

    private void UpdateAnalysisDescriptor(SpectrogramTransformType transformType)
    {
        SpectrogramAnalysisDescriptor? descriptor = null;
        switch (transformType)
        {
            case SpectrogramTransformType.Cqt:
                if (_cqt is not null)
                {
                    descriptor = SpectrogramAnalysisDescriptor.CreateFromCenters(
                        SpectrogramTransformType.Cqt,
                        _cqt.CenterFrequencies);
                }
                break;
            case SpectrogramTransformType.ZoomFft:
                if (_zoomFft is not null)
                {
                    descriptor = SpectrogramAnalysisDescriptor.CreateLinear(
                        SpectrogramTransformType.ZoomFft,
                        _zoomFft.OutputBins,
                        _zoomFft.MinFrequency,
                        _zoomFft.BinResolutionHz);
                }
                break;
            default:
                int half = _activeFftSize / 2;
                descriptor = SpectrogramAnalysisDescriptor.CreateLinear(
                    SpectrogramTransformType.Fft,
                    half,
                    0f,
                    _binResolution);
                break;
        }

        if (descriptor is not null)
        {
            _featureExtractor.UpdateFrequencies(descriptor.BinCentersHz.Span);
            Volatile.Write(ref _analysisDescriptor, descriptor);
        }
        else
        {
            Volatile.Write(ref _analysisDescriptor, null);
        }
    }

    private void UpdateAWeighting()
    {
        int half = _activeFftSize / 2;
        if (half <= 0)
        {
            return;
        }

        if (_aWeighting.Length != half)
        {
            _aWeighting = new float[half];
        }

        for (int bin = 0; bin < half; bin++)
        {
            float freq = bin * _binResolution;
            _aWeighting[bin] = AWeighting.GetLinearWeight(freq);
        }
    }

    private void UpdateWindowNormalization()
    {
        double sum = 0.0;
        for (int i = 0; i < _fftWindow.Length; i++)
        {
            sum += _fftWindow[i];
        }

        float denom = sum > 1e-6 ? (float)sum : 1f;
        _fftNormalization = 2f / denom;
    }

    private void BuildDisplayGain()
    {
        int bins = Math.Min(_activeAnalysisBins, _displayGain.Length);
        for (int i = 0; i < bins; i++)
        {
            float raw = _spectrumScratch[i];
            float processed = _displaySmoothed[i];
            float gain = raw > 1e-8f ? processed / raw : 0f;
            _displayGain[i] = Math.Clamp(gain, 0f, 4f);
        }

        if (bins < _displayGain.Length)
        {
            Array.Clear(_displayGain, bins, _displayGain.Length - bins);
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

    private static void CopyRingFrames(float[] source, float[] destination, int framesToCopy, int stride, int startIndex)
    {
        if (framesToCopy <= 0 || stride <= 0)
        {
            return;
        }

        int capacity = source.Length / stride;
        int clampedFrames = Math.Min(framesToCopy, capacity);
        int firstFrames = Math.Min(clampedFrames, capacity - startIndex);
        if (firstFrames > 0)
        {
            Array.Copy(source, startIndex * stride, destination, startIndex * stride, firstFrames * stride);
        }

        int remainingFrames = clampedFrames - firstFrames;
        if (remainingFrames > 0)
        {
            Array.Copy(source, 0, destination, 0, remainingFrames * stride);
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

    private static void CopyRingFrames(byte[] source, byte[] destination, int framesToCopy, int startIndex)
    {
        if (framesToCopy <= 0)
        {
            return;
        }

        int capacity = source.Length;
        int clampedFrames = Math.Min(framesToCopy, capacity);
        int firstFrames = Math.Min(clampedFrames, capacity - startIndex);
        if (firstFrames > 0)
        {
            Array.Copy(source, startIndex, destination, startIndex, firstFrames);
        }

        int remainingFrames = clampedFrames - firstFrames;
        if (remainingFrames > 0)
        {
            Array.Copy(source, 0, destination, 0, remainingFrames);
        }
    }

    /// <summary>
    /// Resizes an array while preserving existing data (for simple per-frame buffers).
    /// </summary>
    private static T[] ResizePreserve<T>(T[] source, int newSize, int elementsToCopy)
    {
        var result = new T[newSize];
        if (source is not null && elementsToCopy > 0)
        {
            int copyCount = Math.Min(elementsToCopy, Math.Min(source.Length, newSize));
            if (copyCount > 0)
            {
                Array.Copy(source, 0, result, 0, copyCount);
            }
        }
        return result;
    }

    /// <summary>
    /// Resizes a strided array while preserving existing data (for multi-value per-frame buffers).
    /// </summary>
    private static float[] ResizePreserveStrided(float[] source, int newFrameCount, int stride, int framesToCopy)
    {
        int newSize = newFrameCount * stride;
        var result = new float[newSize];
        if (source is not null && framesToCopy > 0)
        {
            int sourceFrames = source.Length / stride;
            int copyFrames = Math.Min(framesToCopy, Math.Min(sourceFrames, newFrameCount));
            int copyElements = copyFrames * stride;
            if (copyElements > 0)
            {
                Array.Copy(source, 0, result, 0, copyElements);
            }
        }
        return result;
    }
}
