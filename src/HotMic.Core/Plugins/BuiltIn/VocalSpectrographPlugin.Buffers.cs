using System.Runtime.CompilerServices;
using System.Threading;
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
        var transformType = (SpectrogramTransformType)Math.Clamp(Volatile.Read(ref _requestedTransformType), 0, 2);
        int cqtBinsPerOctave = SelectDiscrete(Volatile.Read(ref _requestedCqtBinsPerOctave), CqtBinsPerOctaveOptions);

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
        bool transformChanged = force || transformType != _activeTransformType;
        bool cqtChanged = force || cqtBinsPerOctave != _activeCqtBinsPerOctave;
        bool resetBuffers = false;

        if (sizeChanged)
        {
            _activeFftSize = fftSize;
            _activeOverlapIndex = overlapIndex;
            _activeTimeWindow = timeWindow;
            _activeHopSize = Math.Max(1, (int)(fftSize * (1f - OverlapOptions[overlapIndex])));
            _activeFrameCapacity = Math.Max(1, (int)MathF.Ceiling(timeWindow * _sampleRate / _activeHopSize));
            _activeAnalysisSize = fftSize;
            _analysisFilled = 0;

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
            _fftDisplayMagnitudes = new float[_activeFftSize / 2];
            _aWeighting = new float[_activeFftSize / 2];
            _fftNormalization = 2f / MathF.Max(1f, _activeFftSize);
            _binResolution = _sampleRate / (float)_activeFftSize;
            UpdateAWeighting();

            _pitchTrack = new float[_activeFrameCapacity];
            _pitchConfidence = new float[_activeFrameCapacity];
            _formantFrequencies = new float[_activeFrameCapacity * MaxFormants];
            _formantBandwidths = new float[_activeFrameCapacity * MaxFormants];
            _voicingStates = new byte[_activeFrameCapacity];
            _harmonicFrequencies = new float[_activeFrameCapacity * MaxHarmonics];
            _harmonicMagnitudes = new float[_activeFrameCapacity * MaxHarmonics];
            _waveformMin = new float[_activeFrameCapacity];
            _waveformMax = new float[_activeFrameCapacity];
            _hnrTrack = new float[_activeFrameCapacity];
            _cppTrack = new float[_activeFrameCapacity];
            _spectralCentroid = new float[_activeFrameCapacity];
            _spectralSlope = new float[_activeFrameCapacity];
            _spectralFlux = new float[_activeFrameCapacity];
            resetBuffers = true;
        }

        // Refill the window buffer when size changes to avoid zeroed FFT input.
        if (windowChanged || sizeChanged)
        {
            _activeWindow = window;
            WindowFunctions.Fill(_fftWindow, window);
            UpdateWindowNormalization();
            UpdateReassignWindows();
            resetBuffers = true;
        }

        if (mappingChanged || sizeChanged)
        {
            _activeScale = scale;
            _activeMinFrequency = minHz;
            _activeMaxFrequency = maxHz;
            _swipePitchDetector ??= new SwipePitchDetector(_sampleRate, _activeFftSize, minHz, maxHz);
            _swipePitchDetector.Configure(_sampleRate, _activeFftSize, minHz, maxHz);
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

            resetBuffers = true;
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
            resetBuffers = true;
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
        int desiredDisplayBins = Math.Min(1024, desiredAnalysisBins);
        bool binsChanged = desiredAnalysisBins != _activeAnalysisBins
            || desiredDisplayBins != _activeDisplayBins;

        if (binsChanged)
        {
            _activeAnalysisBins = desiredAnalysisBins;
            _activeDisplayBins = desiredDisplayBins;

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

            resetBuffers = true;
        }

        if (binsChanged || sizeChanged)
        {
            // Display buffer at display resolution.
            _spectrogramBuffer = new float[_activeFrameCapacity * _activeDisplayBins];
            // Linear magnitude buffer at analysis resolution.
            _linearMagnitudeBuffer = new float[_activeFrameCapacity * _activeAnalysisBins];
            resetBuffers = true;
        }

        if (binsChanged || transformChanged || sizeChanged || mappingChanged || cqtChanged)
        {
            UpdateAnalysisDescriptor(transformType);
        }

        if (transformChanged)
        {
            _activeTransformType = transformType;
            resetBuffers = true;
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
        }

        if (sizeChanged || lpcChanged)
        {
            _yinPitchDetector ??= new YinPitchDetector(_sampleRate, _activeFftSize, 60f, 1200f, 0.15f);
            _yinPitchDetector.Configure(_sampleRate, _activeFftSize, 60f, 1200f, 0.15f);

            _pyinPitchDetector ??= new PyinPitchDetector(_sampleRate, _activeFftSize, 60f, 1200f, 0.15f);
            _pyinPitchDetector.Configure(_sampleRate, _activeFftSize, 60f, 1200f, 0.15f);

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

        if (resetBuffers)
        {
            ClearVisualizationBuffers();
        }
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
