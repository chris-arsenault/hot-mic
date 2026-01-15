using System.Threading;
using HotMic.Core.Dsp.Mapping;
using HotMic.Core.Dsp.Spectrogram;

namespace HotMic.Core.Plugins.BuiltIn;

public sealed partial class VocalSpectrographPlugin
{
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
            int analysisSize = _activeAnalysisSize;
            int tail = analysisSize - shift;
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

            int filled = Volatile.Read(ref _analysisFilled);
            filled = Math.Min(analysisSize, filled + shift);
            Volatile.Write(ref _analysisFilled, filled);
            if (filled < analysisSize)
            {
                continue;
            }

            var transformType = _activeTransformType;
            // Reassignment now supported for all transform types
            bool reassignEnabled = _activeReassignMode != SpectrogramReassignMode.Off;

            int half = _activeFftSize / 2;
            ReadOnlySpan<float> displayMagnitudes = _fftMagnitudes;

            // Track active bins for clarity processing (varies by transform type)
            int clarityBins = _activeAnalysisBins;

            // Compute transform based on type
            if (transformType == SpectrogramTransformType.Cqt && _cqt is not null)
            {
                // CQT: variable-window transform with logarithmic frequency spacing
                // CQT is a true filter bank (direct convolution per bin), not FFT-based
                clarityBins = Math.Min(_cqt.BinCount, _activeAnalysisBins);

                if (reassignEnabled)
                {
                    // CQT with reassignment data:
                    // - Time reassignment uses time-weighted kernels
                    // - Frequency reassignment uses phase derivative (dphi/dt)
                    _cqt.ForwardWithReassignment(
                        _analysisBufferProcessed,
                        _cqtMagnitudes,
                        _cqtReal,
                        _cqtImag,
                        _cqtTimeReal,
                        _cqtTimeImag,
                        _cqtPhaseDiff);
                }
                else
                {
                    _cqt.Forward(_analysisBufferProcessed, _cqtMagnitudes);
                }

                Array.Copy(_cqtMagnitudes, _spectrumScratch, clarityBins);
                // Zero-fill remainder for consistent buffer handling
                if (clarityBins < _activeAnalysisBins)
                {
                    Array.Clear(_spectrumScratch, clarityBins, _activeAnalysisBins - clarityBins);
                }
            }
            else if (transformType == SpectrogramTransformType.ZoomFft && _zoomFft is not null)
            {
                // Zoom FFT: high-resolution analysis of visible frequency range
                // ZoomFFT produces bins only for the zoomed range
                int zoomBins = _zoomFft.OutputBins;
                clarityBins = Math.Min(zoomBins, _activeAnalysisBins);
                Span<float> zoomMagnitudes = _fftDisplayMagnitudes.AsSpan(0, zoomBins);

                // Only compute full reassignment data when both time and freq are needed
                bool needsTimeData = reassignEnabled && _activeReassignMode.HasFlag(SpectrogramReassignMode.Time);
                bool needsFreqData = reassignEnabled && _activeReassignMode.HasFlag(SpectrogramReassignMode.Frequency);

                if (needsTimeData || needsFreqData)
                {
                    // ZoomFFT with reassignment data (time-weighted + derivative)
                    _zoomFft.ForwardWithReassignment(
                        _analysisBufferProcessed,
                        zoomMagnitudes,
                        _zoomReal,
                        _zoomImag,
                        _zoomTimeReal,
                        _zoomTimeImag,
                        _zoomDerivReal,
                        _zoomDerivImag);
                }
                else
                {
                    _zoomFft.Forward(_analysisBufferProcessed, zoomMagnitudes);
                }

                zoomMagnitudes.Slice(0, clarityBins).CopyTo(_spectrumScratch);

                // Zero-fill remainder
                if (clarityBins < _activeAnalysisBins)
                {
                    Array.Clear(_spectrumScratch, clarityBins, _activeAnalysisBins - clarityBins);
                }
            }
            else
            {
                // Standard FFT path - full analysis resolution, no mapping
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

                float normalization = _fftNormalization;
                for (int i = 0; i < half; i++)
                {
                    float re = _fftReal[i];
                    float im = _fftImag[i];
                    _fftMagnitudes[i] = MathF.Sqrt(re * re + im * im) * normalization;
                }

                var normalizationMode = (SpectrogramNormalizationMode)Math.Clamp(
                    Volatile.Read(ref _requestedNormalizationMode), 0, 3);
                displayMagnitudes = _fftMagnitudes;

                if (normalizationMode == SpectrogramNormalizationMode.AWeighted)
                {
                    for (int i = 0; i < half; i++)
                    {
                        _fftDisplayMagnitudes[i] = _fftMagnitudes[i] * _aWeighting[i];
                    }
                    displayMagnitudes = _fftDisplayMagnitudes;
                }
                else if (normalizationMode == SpectrogramNormalizationMode.Peak)
                {
                    float peak = 0f;
                    for (int i = 0; i < half; i++)
                    {
                        float mag = _fftMagnitudes[i];
                        if (mag > peak)
                        {
                            peak = mag;
                        }
                    }

                    float inv = peak > 1e-12f ? 1f / peak : 0f;
                    for (int i = 0; i < half; i++)
                    {
                        _fftDisplayMagnitudes[i] = _fftMagnitudes[i] * inv;
                    }
                    displayMagnitudes = _fftDisplayMagnitudes;
                }
                else if (normalizationMode == SpectrogramNormalizationMode.Rms)
                {
                    double sum = 0.0;
                    for (int i = 0; i < half; i++)
                    {
                        float mag = _fftMagnitudes[i];
                        sum += mag * mag;
                    }
                    float rms = sum > 0.0 ? MathF.Sqrt((float)(sum / half)) : 0f;
                    float inv = rms > 1e-12f ? 1f / rms : 0f;
                    for (int i = 0; i < half; i++)
                    {
                        _fftDisplayMagnitudes[i] = _fftMagnitudes[i] * inv;
                    }
                    displayMagnitudes = _fftDisplayMagnitudes;
                }

                // Copy to scratch buffer at analysis resolution (no mapping)
                displayMagnitudes.Slice(0, half).CopyTo(_spectrumScratch);
                clarityBins = half;
            }

            // Pitch + CPP detection (raw buffer) at a reduced rate for performance.
            var pitchAlgorithm = (PitchDetectorType)Math.Clamp(Volatile.Read(ref _requestedPitchAlgorithm), 0, 4);
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
                else if (pitchAlgorithm == PitchDetectorType.Pyin && _pyinPitchDetector is not null)
                {
                    var pitch = _pyinPitchDetector.Detect(_analysisBufferRaw);
                    lastPitch = pitch.FrequencyHz ?? 0f;
                    lastConfidence = pitch.Confidence;
                }
                else if (pitchAlgorithm == PitchDetectorType.Autocorrelation && _autocorrPitchDetector is not null)
                {
                    var pitch = _autocorrPitchDetector.Detect(_analysisBufferRaw);
                    lastPitch = pitch.FrequencyHz ?? 0f;
                    lastConfidence = pitch.Confidence;
                }
                else if (pitchAlgorithm == PitchDetectorType.Swipe && _swipePitchDetector is not null)
                {
                    var pitch = _swipePitchDetector.Detect(_fftMagnitudes);
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
            bool hnrComputed = false;

            // Clarity processing at analysis resolution (clarityBins)
            if (!clarityEnabled)
            {
                Array.Copy(_spectrumScratch, _displaySmoothed, clarityBins);
                Array.Copy(_spectrumScratch, _displayProcessed, clarityBins);
            }
            else
            {
                Array.Copy(_spectrumScratch, _displayWork, clarityBins);
                if (useNoise && clarityNoise > 0f)
                {
                    _noiseReducer.Apply(_displayWork, clarityNoise, voicing, clarityBins);
                }

                if (useHarmonic && clarityHarmonic > 0f)
                {
                    _hpssProcessor.Apply(_displayWork, _displayProcessed, clarityHarmonic, clarityBins);
                }
                else
                {
                    Array.Copy(_displayWork, _displayProcessed, clarityBins);
                }

                if (clarityMode == ClarityProcessingMode.Full && clarityHarmonic > 0f)
                {
                    // Harmonic comb at analysis resolution using linear Hz-to-bin mapping
                    _harmonicComb.UpdateMaskLinear(
                        lastPitch,
                        lastConfidence,
                        voicing,
                        _binResolution,
                        clarityBins);
                    lastHnr = _harmonicComb.Apply(_displayProcessed, clarityHarmonic, clarityBins);
                    hnrComputed = true;
                }

                if (claritySmoothing > 0f && smoothingMode != SpectrogramSmoothingMode.Off)
                {
                    if (smoothingMode == SpectrogramSmoothingMode.Bilateral)
                    {
                        _smoother.ApplyBilateral(_displayProcessed, _displaySmoothed, claritySmoothing, clarityBins);
                    }
                    else
                    {
                        _smoother.ApplyEma(_displayProcessed, _displaySmoothed, claritySmoothing, clarityBins);
                    }
                }
                else
                {
                    Array.Copy(_displayProcessed, _displaySmoothed, clarityBins);
                }
            }
            if (!hnrComputed
                && Volatile.Read(ref _requestedShowPitchMeter) != 0
                && lastPitch > 0f
                && voicing == VoicingState.Voiced)
            {
                Array.Copy(_displaySmoothed, _displayWork, clarityBins);
                _harmonicComb.UpdateMaskLinear(
                    lastPitch,
                    lastConfidence,
                    voicing,
                    _binResolution,
                    clarityBins);
                lastHnr = _harmonicComb.Apply(_displayWork, 1f, clarityBins);
            }

            long frameId = _frameCounter;
            int frameIndex = (int)(frameId % _activeFrameCapacity);
            Interlocked.Increment(ref _dataVersion);

            // Store post-clarity linear magnitudes at analysis resolution
            // _displaySmoothed contains clarity-processed data, mapping/dB done in display layer
            int linearOffset = frameIndex * _activeAnalysisBins;
            Array.Copy(_displaySmoothed, 0, _linearMagnitudeBuffer, linearOffset, clarityBins);
            // Zero-fill remainder if transform produced fewer bins than analysis resolution
            if (clarityBins < _activeAnalysisBins)
            {
                Array.Clear(_linearMagnitudeBuffer, linearOffset + clarityBins, _activeAnalysisBins - clarityBins);
            }

            if (reassignEnabled)
            {
                int specOffset = frameIndex * _activeDisplayBins;
                Array.Clear(_spectrogramBuffer, specOffset, _activeDisplayBins);
                BuildDisplayGain();

                // Store reassigned energy in linear display bins; UI applies dynamic range mapping.

                float reassignThresholdDb = Volatile.Read(ref _requestedReassignThreshold);
                float reassignThresholdLinear = DspUtils.DbToLinear(reassignThresholdDb);
                float reassignSpread = Math.Clamp(Volatile.Read(ref _requestedReassignSpread), 0f, 1f);
                float maxTimeShift = MaxReassignFrameShift * reassignSpread;
                float maxBinShift = MaxReassignBinShift * reassignSpread;
                float invHop = 1f / MathF.Max(1f, _activeHopSize);
                long oldestFrameId = Math.Max(0, frameId - _activeFrameCapacity + 1);

                // Transform-specific parameters
                int numBins;
                float freqBinScale;
                float binResHz;
                float minFreqHz = _activeMinFrequency;
                float maxFreqHz = _activeMaxFrequency;
                ReadOnlySpan<float> centerFreqs = ReadOnlySpan<float>.Empty;

                if (transformType == SpectrogramTransformType.Cqt && _cqt is not null)
                {
                    numBins = _cqt.BinCount;
                    freqBinScale = 0f; // CQT uses phase difference for freq reassignment
                    binResHz = 0f; // Not applicable for CQT
                    centerFreqs = _cqt.CenterFrequencies;
                    // Use CQT's actual frequency range (may differ from user request due to clamping)
                    minFreqHz = _cqt.MinFrequency;
                    maxFreqHz = _cqt.MaxFrequency;
                }
                else if (transformType == SpectrogramTransformType.ZoomFft && _zoomFft is not null)
                {
                    numBins = _zoomFft.OutputBins;
                    binResHz = _zoomFft.BinResolutionHz;
                    // ZoomFFT uses decimated FFT size for frequency scale
                    // outputSize = inputSize / decimationFactor, so effective FFT size = outputSize * 2
                    freqBinScale = numBins * 2 / (MathF.PI * 2f);
                }
                else
                {
                    numBins = half;
                    binResHz = _binResolution;
                    freqBinScale = _activeFftSize / (MathF.PI * 2f);
                }

                for (int bin = 0; bin < numBins; bin++)
                {
                    float mag;
                    double re, im, reTime, imTime, reDeriv, imDeriv;
                    float binFreqHz;
                    float phaseDiff = 0f;

                    // Get transform-specific values
                    if (transformType == SpectrogramTransformType.Cqt)
                    {
                        mag = _cqtMagnitudes[bin];
                        re = _cqtReal[bin];
                        im = _cqtImag[bin];
                        reTime = _cqtTimeReal[bin];
                        imTime = _cqtTimeImag[bin];
                        reDeriv = 0; // CQT uses phase diff instead
                        imDeriv = 0;
                        binFreqHz = centerFreqs[bin];
                        phaseDiff = _cqtPhaseDiff[bin];
                    }
                    else if (transformType == SpectrogramTransformType.ZoomFft)
                    {
                        mag = _fftDisplayMagnitudes[bin];
                        re = _zoomReal[bin];
                        im = _zoomImag[bin];
                        reTime = _zoomTimeReal[bin];
                        imTime = _zoomTimeImag[bin];
                        reDeriv = _zoomDerivReal[bin];
                        imDeriv = _zoomDerivImag[bin];
                        binFreqHz = _zoomFft!.GetBinFrequency(bin);
                    }
                    else
                    {
                        mag = displayMagnitudes[bin];
                        re = _fftReal[bin];
                        im = _fftImag[bin];
                        reTime = _fftTimeReal[bin];
                        imTime = _fftTimeImag[bin];
                        reDeriv = _fftDerivReal[bin];
                        imDeriv = _fftDerivImag[bin];
                        binFreqHz = bin * _binResolution;
                    }

                    if (mag <= 0f)
                    {
                        continue;
                    }

                    // Apply clarity gain (only for FFT which uses _fftBinToDisplay)
                    float adjustedMag = mag;
                    if (transformType == SpectrogramTransformType.Fft)
                    {
                        int displayBin = _fftBinToDisplay[bin];
                        float gain = _displayGain[displayBin];
                        if (gain <= 0f)
                        {
                            continue;
                        }
                        adjustedMag = mag * gain;
                    }

                    if (adjustedMag < reassignThresholdLinear)
                    {
                        continue;
                    }

                    double denom = re * re + im * im + 1e-12;

                    float timeShiftFrames = 0f;
                    if (_activeReassignMode.HasFlag(SpectrogramReassignMode.Time))
                    {
                        // Time reassignment from time-weighted transform
                        double timeShiftSamples = (reTime * re + imTime * im) / denom;
                        double timeShiftScaled = timeShiftSamples * invHop;
                        timeShiftFrames = (float)Math.Clamp(timeShiftScaled, -maxTimeShift, maxTimeShift);
                    }

                    float reassignedFreqHz = binFreqHz;
                    if (_activeReassignMode.HasFlag(SpectrogramReassignMode.Frequency))
                    {
                        if (transformType == SpectrogramTransformType.Cqt)
                        {
                            // CQT frequency reassignment in log-frequency space
                            // phaseDiff = phase[t] - phase[t-1], wrapped to [-π, π]
                            // We need the DEVIATION from expected phase advance
                            float hopTime = _activeHopSize / (float)_sampleRate;
                            float twoPi = MathF.PI * 2f;

                            // Expected phase advance for this bin's center frequency
                            float expectedPhaseAdvance = twoPi * binFreqHz * hopTime;

                            // Wrap expected to [-π, π] for comparison with phaseDiff
                            float expectedMod = expectedPhaseAdvance;
                            while (expectedMod > MathF.PI) expectedMod -= twoPi;
                            while (expectedMod < -MathF.PI) expectedMod += twoPi;

                            // Deviation from expected (this is what "unwrap" should give us)
                            float deviation = phaseDiff - expectedMod;
                            // Wrap deviation to [-π, π]
                            while (deviation > MathF.PI) deviation -= twoPi;
                            while (deviation < -MathF.PI) deviation += twoPi;

                            // dphi_dt is the deviation rate, normalized by bin frequency for log-space
                            // f_reassigned = f_k * exp(dphi_dt / (2π))
                            // where dphi_dt / (2π) = deviation / (2π * hopTime) / f_k
                            float logDeviation = deviation / (twoPi * hopTime * binFreqHz);
                            reassignedFreqHz = binFreqHz * MathF.Exp(logDeviation);
                        }
                        else
                        {
                            // FFT/ZoomFFT frequency reassignment from derivative window
                            double imagPart = (imDeriv * re - reDeriv * im) / denom;
                            double freqShift = imagPart * freqBinScale;
                            float freqShiftBins = (float)Math.Clamp(freqShift, -maxBinShift, maxBinShift);
                            reassignedFreqHz = binFreqHz + freqShiftBins * binResHz;
                        }
                    }

                    // Clamp to valid frequency range
                    if (reassignedFreqHz < minFreqHz || reassignedFreqHz > maxFreqHz)
                    {
                        continue;
                    }

                    float targetFrame = frameId + timeShiftFrames - _reassignLatencyFrames;
                    long frameBase = (long)MathF.Floor(targetFrame);
                    float frameFrac = targetFrame - frameBase;
                    if (frameBase < oldestFrameId || frameBase > frameId)
                    {
                        continue;
                    }

                    // Map reassigned frequency to display position
                    float clamped = Math.Clamp(reassignedFreqHz, minFreqHz, maxFreqHz);
                    float scaled = FrequencyScaleUtils.ToScale(_activeScale, clamped);
                    float norm = (scaled - _scaledMin) / _scaledRange;
                    float displayPos = Math.Clamp(norm * (_activeDisplayBins - 1), 0f, _activeDisplayBins - 1);
                    int binBase = (int)MathF.Floor(displayPos);
                    float binFrac = displayPos - binBase;
                    if (binBase < 0 || binBase >= _activeDisplayBins)
                    {
                        continue;
                    }

                    float valueBase = adjustedMag;
                    if (valueBase <= 0f)
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
                        float value = valueBase * wFrame0 * wBin0;
                        if (value > _spectrogramBuffer[baseOffset + binBase])
                        {
                            _spectrogramBuffer[baseOffset + binBase] = value;
                        }

                        int bin1 = binBase + 1;
                        if (bin1 < _activeDisplayBins && wBin1 > 0f)
                        {
                            value = valueBase * wFrame0 * wBin1;
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
                        float value = valueBase * wFrame1 * wBin0;
                        if (value > _spectrogramBuffer[baseOffset + binBase])
                        {
                            _spectrogramBuffer[baseOffset + binBase] = value;
                        }

                        int bin1 = binBase + 1;
                        if (bin1 < _activeDisplayBins && wBin1 > 0f)
                        {
                            value = valueBase * wFrame1 * wBin1;
                            if (value > _spectrogramBuffer[baseOffset + bin1])
                            {
                                _spectrogramBuffer[baseOffset + bin1] = value;
                            }
                        }
                    }
                }
            }
            // Non-reassigned display mapping is handled in the UI display pipeline.

            _featureExtractor.Compute(_displaySmoothed, clarityBins, out float centroid, out float slope, out float flux);
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
}
