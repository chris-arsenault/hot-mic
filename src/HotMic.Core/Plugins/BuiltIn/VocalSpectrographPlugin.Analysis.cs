using System.Diagnostics;
using System.Runtime.CompilerServices;
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

            long droppedSamples = _captureBuffer.DroppedSamples;
            long droppedHops = _activeHopSize > 0 ? droppedSamples / _activeHopSize : droppedSamples;
            if (droppedHops != _lastDroppedHops)
            {
                _lastDroppedHops = droppedHops;
                ResetAfterDrop();
                Thread.Sleep(1);
                continue;
            }

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

            bool preEmphasis = Volatile.Read(ref _requestedPreEmphasis) != 0;
            bool hpfEnabled = Volatile.Read(ref _requestedHighPassEnabled) != 0;

            long frameStartTicks = Stopwatch.GetTimestamp();
            long stageStartTicks = frameStartTicks;
            bool ready = ProcessHopBuffer(shift, analysisSize, preEmphasis, hpfEnabled, out float waveformMin, out float waveformMax);
            long preprocessTicks = Stopwatch.GetTimestamp() - stageStartTicks;
            if (!ready)
            {
                continue;
            }

            var transformType = _activeTransformType;
            // Reassignment now supported for all transform types
            bool reassignEnabled = _activeReassignMode != SpectrogramReassignMode.Off;

            int half = _activeFftSize / 2;
            ReadOnlySpan<float> displayMagnitudes = _fftMagnitudes;
            int clarityBins = _activeAnalysisBins;
            long transformTicks = 0;
            long normalizationTicks = 0;

            if (transformType == SpectrogramTransformType.Cqt && _cqt is not null)
            {
                stageStartTicks = Stopwatch.GetTimestamp();
                clarityBins = ComputeCqtTransform(reassignEnabled);
                transformTicks = Stopwatch.GetTimestamp() - stageStartTicks;
            }
            else if (transformType == SpectrogramTransformType.ZoomFft && _zoomFft is not null)
            {
                stageStartTicks = Stopwatch.GetTimestamp();
                clarityBins = ComputeZoomFftTransform(reassignEnabled);
                transformTicks = Stopwatch.GetTimestamp() - stageStartTicks;
                displayMagnitudes = _fftDisplayMagnitudes;
            }
            else
            {
                stageStartTicks = Stopwatch.GetTimestamp();
                ComputeFftTransform(reassignEnabled);
                transformTicks = Stopwatch.GetTimestamp() - stageStartTicks;

                var normalizationMode = (SpectrogramNormalizationMode)Math.Clamp(
                    Volatile.Read(ref _requestedNormalizationMode), 0, 3);

                stageStartTicks = Stopwatch.GetTimestamp();
                displayMagnitudes = NormalizeFftMagnitudes(normalizationMode, half);
                normalizationTicks = Stopwatch.GetTimestamp() - stageStartTicks;
                clarityBins = half;
            }

            stageStartTicks = Stopwatch.GetTimestamp();
            AnalyzePitchAndOverlays(ref pitchFrameCounter, ref cppFrameCounter, ref lastPitch, ref lastConfidence,
                ref lastCpp, _fftMagnitudes, out var voicing, out int formantCount, out int harmonicCount);
            long pitchTicks = Stopwatch.GetTimestamp() - stageStartTicks;

            var clarityMode = (ClarityProcessingMode)Math.Clamp(Volatile.Read(ref _requestedClarityMode), 0, 3);
            float clarityNoise = Volatile.Read(ref _requestedClarityNoise);
            float clarityHarmonic = Volatile.Read(ref _requestedClarityHarmonic);
            float claritySmoothing = Volatile.Read(ref _requestedClaritySmoothing);
            var smoothingMode = (SpectrogramSmoothingMode)Math.Clamp(Volatile.Read(ref _requestedSmoothingMode), 0, 2);

            stageStartTicks = Stopwatch.GetTimestamp();
            lastHnr = ProcessClarity(clarityBins, voicing, lastPitch, lastConfidence, smoothingMode,
                clarityMode, clarityNoise, clarityHarmonic, claritySmoothing);
            long clarityTicks = Stopwatch.GetTimestamp() - stageStartTicks;

            long frameId = _frameCounter;
            int frameIndex = (int)(frameId % _activeFrameCapacity);
            Interlocked.Increment(ref _dataVersion);

            stageStartTicks = Stopwatch.GetTimestamp();
            WriteLinearMagnitudes(frameIndex, clarityBins);
            long writeLinearTicks = Stopwatch.GetTimestamp() - stageStartTicks;

            long reassignTicks = 0;
            if (reassignEnabled)
            {
                stageStartTicks = Stopwatch.GetTimestamp();
                ApplyReassignment(frameId, displayMagnitudes, transformType, half);
                reassignTicks = Stopwatch.GetTimestamp() - stageStartTicks;
            }

            stageStartTicks = Stopwatch.GetTimestamp();
            ComputeSpectralFeatures(clarityBins, out float centroid, out float slope, out float flux);
            long featureTicks = Stopwatch.GetTimestamp() - stageStartTicks;

            stageStartTicks = Stopwatch.GetTimestamp();
            WriteOverlayData(frameId, frameIndex, waveformMin, waveformMax, lastHnr, lastCpp, lastPitch,
                lastConfidence, voicing, formantCount, harmonicCount, centroid, slope, flux);
            long writebackTicks = writeLinearTicks + (Stopwatch.GetTimestamp() - stageStartTicks);

            long frameTicks = Stopwatch.GetTimestamp() - frameStartTicks;
            _timing.RecordFrame(frameTicks, preprocessTicks, transformTicks, normalizationTicks, pitchTicks, clarityTicks,
                reassignTicks, featureTicks, writebackTicks);

        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private bool ProcessHopBuffer(int shift, int analysisSize, bool preEmphasis, bool hpfEnabled,
        out float waveformMin, out float waveformMax)
    {
        int tail = analysisSize - shift;
        Array.Copy(_analysisBufferRaw, shift, _analysisBufferRaw, 0, tail);
        Array.Copy(_analysisBufferProcessed, shift, _analysisBufferProcessed, 0, tail);

        waveformMin = float.MaxValue;
        waveformMax = float.MinValue;
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
        return filled >= analysisSize;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private int ComputeCqtTransform(bool reassignEnabled)
    {
        int clarityBins = Math.Min(_cqt!.BinCount, _activeAnalysisBins);

        if (reassignEnabled)
        {
            bool needsTimeData = _activeReassignMode.HasFlag(SpectrogramReassignMode.Time);
            bool needsFreqData = _activeReassignMode.HasFlag(SpectrogramReassignMode.Frequency);
            _cqt.ForwardWithReassignment(
                _analysisBufferProcessed,
                _cqtMagnitudes,
                _cqtReal,
                _cqtImag,
                _cqtTimeReal,
                _cqtTimeImag,
                _cqtPhaseDiff,
                needsTimeData,
                needsFreqData);
        }
        else
        {
            _cqt.Forward(_analysisBufferProcessed, _cqtMagnitudes);
        }

        Array.Copy(_cqtMagnitudes, _spectrumScratch, clarityBins);
        if (clarityBins < _activeAnalysisBins)
        {
            Array.Clear(_spectrumScratch, clarityBins, _activeAnalysisBins - clarityBins);
        }

        return clarityBins;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private int ComputeZoomFftTransform(bool reassignEnabled)
    {
        int zoomBins = _zoomFft!.OutputBins;
        int clarityBins = Math.Min(zoomBins, _activeAnalysisBins);
        Span<float> zoomMagnitudes = _fftDisplayMagnitudes.AsSpan(0, zoomBins);

        bool needsTimeData = reassignEnabled && _activeReassignMode.HasFlag(SpectrogramReassignMode.Time);
        bool needsFreqData = reassignEnabled && _activeReassignMode.HasFlag(SpectrogramReassignMode.Frequency);

        if (needsTimeData || needsFreqData)
        {
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
        if (clarityBins < _activeAnalysisBins)
        {
            Array.Clear(_spectrumScratch, clarityBins, _activeAnalysisBins - clarityBins);
        }

        return clarityBins;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void ComputeFftTransform(bool reassignEnabled)
    {
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
            for (int i = 0; i < _activeFftSize; i++)
            {
                _fftReal[i] = _analysisBufferProcessed[i] * _fftWindow[i];
                _fftImag[i] = 0f;
            }

            _fft?.Forward(_fftReal, _fftImag);
        }

        float normalization = _fftNormalization;
        int half = _activeFftSize / 2;
        for (int i = 0; i < half; i++)
        {
            float re = _fftReal[i];
            float im = _fftImag[i];
            _fftMagnitudes[i] = MathF.Sqrt(re * re + im * im) * normalization;
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private ReadOnlySpan<float> NormalizeFftMagnitudes(SpectrogramNormalizationMode normalizationMode, int half)
    {
        ReadOnlySpan<float> displayMagnitudes = _fftMagnitudes;

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

        displayMagnitudes.Slice(0, half).CopyTo(_spectrumScratch);
        return displayMagnitudes;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void AnalyzePitchAndOverlays(ref int pitchFrameCounter, ref int cppFrameCounter, ref float lastPitch,
        ref float lastConfidence, ref float lastCpp, ReadOnlySpan<float> fftMagnitudes, out VoicingState voicing,
        out int formantCount, out int harmonicCount)
    {
        var pitchAlgorithm = (PitchDetectorType)Math.Clamp(Volatile.Read(ref _requestedPitchAlgorithm), 0, 4);
        if (_activeTransformType == SpectrogramTransformType.Cqt && pitchAlgorithm == PitchDetectorType.Swipe)
        {
            pitchAlgorithm = PitchDetectorType.Yin;
        }

        bool showPitch = Volatile.Read(ref _requestedShowPitch) != 0;
        bool showPitchMeter = Volatile.Read(ref _requestedShowPitchMeter) != 0;
        bool showFormants = Volatile.Read(ref _requestedShowFormants) != 0;
        bool showVowelSpace = Volatile.Read(ref _requestedShowVowelSpace) != 0;
        bool showHarmonics = Volatile.Read(ref _requestedShowHarmonics) != 0;
        bool showVoicing = Volatile.Read(ref _requestedShowVoicing) != 0;
        var clarityMode = (ClarityProcessingMode)Math.Clamp(Volatile.Read(ref _requestedClarityMode), 0, 3);
        float clarityHarmonic = Volatile.Read(ref _requestedClarityHarmonic);

        bool needsFormants = showFormants || showVowelSpace;
        bool needsVoicing = showVoicing || needsFormants || showPitchMeter || clarityMode != ClarityProcessingMode.None;
        bool needsPitch = showPitch || showPitchMeter || showHarmonics || needsVoicing
            || (clarityMode == ClarityProcessingMode.Full && clarityHarmonic > 0f);
        bool needsCpp = showPitchMeter || (pitchAlgorithm == PitchDetectorType.Cepstral && needsPitch);

        if (!needsPitch)
        {
            pitchFrameCounter = 0;
            lastPitch = 0f;
            lastConfidence = 0f;
        }
        else
        {
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
                    var pitch = _swipePitchDetector.Detect(fftMagnitudes);
                    lastPitch = pitch.FrequencyHz ?? 0f;
                    lastConfidence = pitch.Confidence;
                }
            }
        }

        if (!needsCpp)
        {
            cppFrameCounter = 0;
            lastCpp = 0f;
        }
        else
        {
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
        }

        voicing = needsVoicing
            ? _voicingDetector.Detect(_analysisBufferRaw, fftMagnitudes, lastConfidence)
            : VoicingState.Silence;

        formantCount = 0;
        bool lpcOk = _lpcAnalyzer is not null;
        bool trackerOk = _formantTracker is not null;
        bool voiced = voicing == VoicingState.Voiced;

        _formantDiagCounter++;

        if (needsFormants && lpcOk && trackerOk && voiced)
        {
            // LPC formant analysis: downsample to 12kHz like professional tools (Praat uses 10-11kHz)
            // This produces poles closer to unit circle with realistic bandwidths (50-400Hz vs 2000-3000Hz)
            // Pipeline: 48kHz raw → 24kHz → 12kHz → pre-emphasis → LPC
            // Pre-emphasis AFTER decimation to avoid over-boosting highs relative to 0-6kHz range
            int bufferLen = _analysisBufferRaw.Length;
            int lpcLen = Math.Min(LpcWindowSamples, bufferLen);
            int lpcStart = bufferLen - lpcLen; // Use most recent samples

            // Step 1: Copy raw samples to input buffer
            _analysisBufferRaw.AsSpan(lpcStart, lpcLen).CopyTo(_lpcInputBuffer.AsSpan(0, lpcLen));

            // Step 2: Decimate 48kHz → 24kHz (2x)
            _lpcDecimator1.Reset();
            int decimated1Len = lpcLen / 2;
            _lpcDecimator1.ProcessDownsample(_lpcInputBuffer.AsSpan(0, lpcLen), _lpcDecimateBuffer1.AsSpan(0, decimated1Len));

            // Step 3: Decimate 24kHz → 12kHz (2x)
            _lpcDecimator2.Reset();
            int decimatedLen = decimated1Len / 2;
            _lpcDecimator2.ProcessDownsample(_lpcDecimateBuffer1.AsSpan(0, decimated1Len), _lpcDecimatedBuffer.AsSpan(0, decimatedLen));

            // Step 4: Apply pre-emphasis at 12kHz (after decimation for proper spectral balance)
            _lpcPreEmphasisFilter.Reset();
            for (int i = 0; i < decimatedLen; i++)
            {
                _lpcDecimatedBuffer[i] = _lpcPreEmphasisFilter.Process(_lpcDecimatedBuffer[i]);
            }

            // Step 5: Run LPC on decimated signal at 12kHz
            // At 12kHz, use order 10-12 for 4-5 formants (rule: order = 2*formants + 2)
            const int LpcOrderAt12kHz = 12;
            if (_lpcAnalyzer.Order != LpcOrderAt12kHz)
            {
                _lpcAnalyzer.Configure(LpcOrderAt12kHz);
                _formantTracker.Configure(LpcOrderAt12kHz);
            }

            if (_lpcAnalyzer.Compute(_lpcDecimatedBuffer.AsSpan(0, decimatedLen), _lpcCoefficients))
            {
                // Pass LpcTargetSampleRate (12kHz) to formant tracker for correct frequency calculation
                formantCount = _formantTracker.Track(_lpcCoefficients, LpcTargetSampleRate,
                    _formantFreqScratch, _formantBwScratch,
                    _activeMinFrequency, _activeMaxFrequency, MaxFormants);
            }
        }

        harmonicCount = 0;
        if (showHarmonics && lastPitch > 0f)
        {
            var descriptor = Volatile.Read(ref _analysisDescriptor);
            if (descriptor is not null)
            {
                // Use the active transform's magnitudes for harmonic detection
                ReadOnlySpan<float> activeMagnitudes = _activeTransformType switch
                {
                    SpectrogramTransformType.Cqt when _cqtMagnitudes.Length > 0 => _cqtMagnitudes,
                    SpectrogramTransformType.ZoomFft when _fftDisplayMagnitudes.Length > 0 => _fftDisplayMagnitudes,
                    _ => fftMagnitudes
                };

                harmonicCount = HarmonicPeakDetector.Detect(activeMagnitudes, descriptor, lastPitch,
                    _harmonicScratch, _harmonicMagScratch);
            }
            // If descriptor is null (during initialization), skip harmonic detection for this frame
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private float ProcessClarity(int clarityBins, VoicingState voicing, float lastPitch, float lastConfidence,
        SpectrogramSmoothingMode smoothingMode, ClarityProcessingMode clarityMode, float clarityNoise,
        float clarityHarmonic, float claritySmoothing)
    {
        bool clarityEnabled = clarityMode != ClarityProcessingMode.None;
        bool useNoise = clarityMode is ClarityProcessingMode.Noise or ClarityProcessingMode.Full;
        bool useHarmonic = clarityMode is ClarityProcessingMode.Harmonic or ClarityProcessingMode.Full;
        float lastHnr = 0f;
        bool hnrComputed = false;

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

        return lastHnr;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void WriteLinearMagnitudes(int frameIndex, int clarityBins)
    {
        int linearOffset = frameIndex * _activeAnalysisBins;
        Array.Copy(_displaySmoothed, 0, _linearMagnitudeBuffer, linearOffset, clarityBins);
        if (clarityBins < _activeAnalysisBins)
        {
            Array.Clear(_linearMagnitudeBuffer, linearOffset + clarityBins, _activeAnalysisBins - clarityBins);
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void ApplyReassignment(long frameId, ReadOnlySpan<float> displayMagnitudes, SpectrogramTransformType transformType, int half)
    {
        int frameIndex = (int)(frameId % _activeFrameCapacity);
        int specOffset = frameIndex * _activeDisplayBins;
        Array.Clear(_spectrogramBuffer, specOffset, _activeDisplayBins);
        BuildDisplayGain();

        float reassignThresholdDb = Volatile.Read(ref _requestedReassignThreshold);
        float reassignThresholdLinear = DspUtils.DbToLinear(reassignThresholdDb);
        float reassignSpread = Math.Clamp(Volatile.Read(ref _requestedReassignSpread), 0f, 1f);
        float maxTimeShift = MaxReassignFrameShift * reassignSpread;
        float maxBinShift = MaxReassignBinShift * reassignSpread;
        float invHop = 1f / MathF.Max(1f, _activeHopSize);
        long oldestFrameId = Math.Max(0, frameId - _activeFrameCapacity + 1);

        int numBins;
        float freqBinScale;
        float binResHz;
        float minFreqHz = _activeMinFrequency;
        float maxFreqHz = _activeMaxFrequency;
        ReadOnlySpan<float> centerFreqs = ReadOnlySpan<float>.Empty;

        if (transformType == SpectrogramTransformType.Cqt && _cqt is not null)
        {
            numBins = _cqt.BinCount;
            freqBinScale = 0f;
            binResHz = 0f;
            centerFreqs = _cqt.CenterFrequencies;
        }
        else if (transformType == SpectrogramTransformType.ZoomFft && _zoomFft is not null)
        {
            numBins = _zoomFft.OutputBins;
            binResHz = _zoomFft.BinResolutionHz;
            freqBinScale = numBins * 2 / (MathF.PI * 2f);
        }
        else
        {
            numBins = half;
            binResHz = _binResolution;
            freqBinScale = _activeFftSize / (MathF.PI * 2f);
        }

        float scaledMin = FrequencyScaleUtils.ToScale(_activeScale, minFreqHz);
        float scaledMax = FrequencyScaleUtils.ToScale(_activeScale, maxFreqHz);
        float scaledRange = MathF.Max(1e-6f, scaledMax - scaledMin);
        float invScaledRange = 1f / scaledRange;
        float maxPos = Math.Max(1f, _activeDisplayBins - 1);

        for (int bin = 0; bin < numBins; bin++)
        {
            float mag;
            double re, im, reTime, imTime, reDeriv, imDeriv;
            float binFreqHz;
            float phaseDiff = 0f;

            if (transformType == SpectrogramTransformType.Cqt)
            {
                mag = _cqtMagnitudes[bin];
                re = _cqtReal[bin];
                im = _cqtImag[bin];
                reTime = _cqtTimeReal[bin];
                imTime = _cqtTimeImag[bin];
                reDeriv = 0;
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

            float adjustedMag = mag;
            if (transformType == SpectrogramTransformType.Fft)
            {
                float gain = _displayGain[bin];
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
                double timeShiftSamples = (reTime * re + imTime * im) / denom;
                double timeShiftScaled = timeShiftSamples * invHop;
                timeShiftFrames = (float)Math.Clamp(timeShiftScaled, -maxTimeShift, maxTimeShift);
            }

            float reassignedFreqHz = binFreqHz;
            if (_activeReassignMode.HasFlag(SpectrogramReassignMode.Frequency))
            {
                if (transformType == SpectrogramTransformType.Cqt)
                {
                    float hopTime = _activeHopSize / (float)_sampleRate;
                    float twoPi = MathF.PI * 2f;
                    float expectedPhaseAdvance = twoPi * binFreqHz * hopTime;
                    float expectedMod = expectedPhaseAdvance;
                    while (expectedMod > MathF.PI) expectedMod -= twoPi;
                    while (expectedMod < -MathF.PI) expectedMod += twoPi;

                    float deviation = phaseDiff - expectedMod;
                    while (deviation > MathF.PI) deviation -= twoPi;
                    while (deviation < -MathF.PI) deviation += twoPi;

                    float logDeviation = deviation / (twoPi * hopTime * binFreqHz);
                    reassignedFreqHz = binFreqHz * MathF.Exp(logDeviation);
                }
                else
                {
                    double imagPart = (imDeriv * re - reDeriv * im) / denom;
                    double freqShift = imagPart * freqBinScale;
                    float freqShiftBins = (float)Math.Clamp(freqShift, -maxBinShift, maxBinShift);
                    reassignedFreqHz = binFreqHz + freqShiftBins * binResHz;
                }
            }

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

            float clamped = Math.Clamp(reassignedFreqHz, minFreqHz, maxFreqHz);
            float scaled = FrequencyScaleUtils.ToScale(_activeScale, clamped);
            float norm = (scaled - scaledMin) * invScaledRange;
            float displayPos = Math.Clamp(norm * maxPos, 0f, maxPos);

            float binHalfHz;
            if (transformType == SpectrogramTransformType.Cqt)
            {
                float prev = bin > 0 ? centerFreqs[bin - 1] : centerFreqs[bin];
                float next = bin + 1 < numBins ? centerFreqs[bin + 1] : centerFreqs[bin];
                binHalfHz = 0.5f * MathF.Max(1e-3f, next - prev);
            }
            else
            {
                binHalfHz = MathF.Max(1e-3f, binResHz * 0.5f);
            }

            float startHz = reassignedFreqHz - binHalfHz;
            float endHz = reassignedFreqHz + binHalfHz;

            float scaledStart = FrequencyScaleUtils.ToScale(_activeScale, Math.Clamp(startHz, minFreqHz, maxFreqHz));
            float scaledEnd = FrequencyScaleUtils.ToScale(_activeScale, Math.Clamp(endHz, minFreqHz, maxFreqHz));
            float normStart = (scaledStart - scaledMin) * invScaledRange;
            float normEnd = (scaledEnd - scaledMin) * invScaledRange;
            float displayStart = Math.Clamp(normStart * maxPos, 0f, maxPos);
            float displayEnd = Math.Clamp(normEnd * maxPos, 0f, maxPos);

            float low = MathF.Min(displayStart, displayEnd);
            float high = MathF.Max(displayStart, displayEnd);
            int binStart = (int)MathF.Floor(low);
            int binEnd = Math.Max(binStart, (int)MathF.Ceiling(high));
            binStart = Math.Clamp(binStart, 0, _activeDisplayBins - 1);
            binEnd = Math.Clamp(binEnd, binStart, _activeDisplayBins - 1);

            float valueBase = adjustedMag;
            if (valueBase <= 0f)
            {
                continue;
            }

            float wFrame0 = 1f - frameFrac;
            float wFrame1 = frameFrac;

            long frame0 = frameBase;
            if (frame0 >= oldestFrameId)
            {
                int targetIndex = (int)(frame0 % _activeFrameCapacity);
                int baseOffset = targetIndex * _activeDisplayBins;
                if (binStart == binEnd)
                {
                    float value = valueBase * wFrame0;
                    int offset = baseOffset + binStart;
                    if (value > _spectrogramBuffer[offset])
                    {
                        _spectrogramBuffer[offset] = value;
                    }
                }
                else
                {
                    float invSpan = 1f / (binEnd - binStart);
                    for (int targetBin = binStart; targetBin <= binEnd; targetBin++)
                    {
                        float weight = 1f - MathF.Abs(targetBin - displayPos) * invSpan;
                        if (weight <= 0f)
                        {
                            continue;
                        }

                        float value = valueBase * wFrame0 * weight;
                        int offset = baseOffset + targetBin;
                        if (value > _spectrogramBuffer[offset])
                        {
                            _spectrogramBuffer[offset] = value;
                        }
                    }
                }
            }

            long frame1 = frameBase + 1;
            if (wFrame1 > 0f && frame1 <= frameId && frame1 >= oldestFrameId)
            {
                int targetIndex = (int)(frame1 % _activeFrameCapacity);
                int baseOffset = targetIndex * _activeDisplayBins;
                if (binStart == binEnd)
                {
                    float value = valueBase * wFrame1;
                    int offset = baseOffset + binStart;
                    if (value > _spectrogramBuffer[offset])
                    {
                        _spectrogramBuffer[offset] = value;
                    }
                }
                else
                {
                    float invSpan = 1f / (binEnd - binStart);
                    for (int targetBin = binStart; targetBin <= binEnd; targetBin++)
                    {
                        float weight = 1f - MathF.Abs(targetBin - displayPos) * invSpan;
                        if (weight <= 0f)
                        {
                            continue;
                        }

                        float value = valueBase * wFrame1 * weight;
                        int offset = baseOffset + targetBin;
                        if (value > _spectrogramBuffer[offset])
                        {
                            _spectrogramBuffer[offset] = value;
                        }
                    }
                }
            }
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void ComputeSpectralFeatures(int clarityBins, out float centroid, out float slope, out float flux)
    {
        _featureExtractor.Compute(_displaySmoothed, clarityBins, out centroid, out slope, out flux);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void WriteOverlayData(long frameId, int frameIndex, float waveformMin, float waveformMax, float lastHnr,
        float lastCpp, float lastPitch, float lastConfidence, VoicingState voicing, int formantCount, int harmonicCount,
        float centroid, float slope, float flux)
    {
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

        // Diagnostic: print formant results once per second
        if (_formantDiagCounter % 100 == 50 && formantCount > 0)
        {
            Console.WriteLine($"[WriteOverlay] formantCount={formantCount}: F1={_formantFreqScratch[0]:F0}Hz, F2={(formantCount > 1 ? _formantFreqScratch[1] : 0):F0}Hz, F3={(formantCount > 2 ? _formantFreqScratch[2] : 0):F0}Hz");
        }

        int harmonicOffset = frameIndex * MaxHarmonics;
        for (int i = 0; i < MaxHarmonics; i++)
        {
            _harmonicFrequencies[harmonicOffset + i] = i < harmonicCount ? _harmonicScratch[i] : 0f;
            _harmonicMagnitudes[harmonicOffset + i] = i < harmonicCount ? _harmonicMagScratch[i] : float.MinValue;
        }

        long nextFrame = frameId + 1;
        Volatile.Write(ref _frameCounter, nextFrame);
        UpdateDisplayWindow(nextFrame);
        Interlocked.Increment(ref _dataVersion);
    }

    private void ResetAfterDrop()
    {
        Volatile.Write(ref _analysisFilled, 0);
        if (_analysisBufferRaw.Length > 0)
        {
            Array.Clear(_analysisBufferRaw, 0, _analysisBufferRaw.Length);
        }
        if (_analysisBufferProcessed.Length > 0)
        {
            Array.Clear(_analysisBufferProcessed, 0, _analysisBufferProcessed.Length);
        }
        if (_hopBuffer.Length > 0)
        {
            Array.Clear(_hopBuffer, 0, _hopBuffer.Length);
        }

        // Record dropout as discontinuity and reset analysis state (preserve display)
        RecordDiscontinuity(DiscontinuityType.BufferDrop);
        ResetAnalysisState();
    }
}
