using System.Threading;

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
            int tail = _activeFftSize - shift;
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

            bool reassignEnabled = _activeReassignMode != SpectrogramReassignMode.Off;
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

            int half = _activeFftSize / 2;
            float normalization = _fftNormalization;
            for (int i = 0; i < half; i++)
            {
                float re = _fftReal[i];
                float im = _fftImag[i];
                _fftMagnitudes[i] = MathF.Sqrt(re * re + im * im) * normalization;
            }

            var normalizationMode = (SpectrogramNormalizationMode)Math.Clamp(
                Volatile.Read(ref _requestedNormalizationMode), 0, 3);
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

            _mapper.MapMax(displayMagnitudes, _spectrumScratch);

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

            if (!clarityEnabled)
            {
                Array.Copy(_spectrumScratch, _displaySmoothed, _activeDisplayBins);
                Array.Copy(_spectrumScratch, _displayProcessed, _activeDisplayBins);
            }
            else
            {
                Array.Copy(_spectrumScratch, _displayWork, _activeDisplayBins);
                if (useNoise && clarityNoise > 0f)
                {
                    _noiseReducer.Apply(_displayWork, clarityNoise, voicing);
                }

                if (useHarmonic && clarityHarmonic > 0f)
                {
                    _hpssProcessor.Apply(_displayWork, _displayProcessed, clarityHarmonic);
                }
                else
                {
                    Array.Copy(_displayWork, _displayProcessed, _activeDisplayBins);
                }

                if (clarityMode == ClarityProcessingMode.Full && clarityHarmonic > 0f)
                {
                    _harmonicComb.UpdateMask(
                        lastPitch,
                        lastConfidence,
                        voicing,
                        _activeScale,
                        _activeMinFrequency,
                        _activeMaxFrequency,
                        _scaledMin,
                        _scaledRange,
                        _activeDisplayBins);
                    lastHnr = _harmonicComb.Apply(_displayProcessed, clarityHarmonic);
                    hnrComputed = true;
                }

                if (claritySmoothing > 0f && smoothingMode != SpectrogramSmoothingMode.Off)
                {
                    if (smoothingMode == SpectrogramSmoothingMode.Bilateral)
                    {
                        _smoother.ApplyBilateral(_displayProcessed, _displaySmoothed, claritySmoothing);
                    }
                    else
                    {
                        _smoother.ApplyEma(_displayProcessed, _displaySmoothed, claritySmoothing);
                    }
                }
                else
                {
                    Array.Copy(_displayProcessed, _displaySmoothed, _activeDisplayBins);
                }
            }
            if (!hnrComputed
                && Volatile.Read(ref _requestedShowPitchMeter) != 0
                && lastPitch > 0f
                && voicing == VoicingState.Voiced)
            {
                Array.Copy(_displaySmoothed, _displayWork, _activeDisplayBins);
                _harmonicComb.UpdateMask(
                    lastPitch,
                    lastConfidence,
                    voicing,
                    _activeScale,
                    _activeMinFrequency,
                    _activeMaxFrequency,
                    _scaledMin,
                    _scaledRange,
                    _activeDisplayBins);
                lastHnr = _harmonicComb.Apply(_displayWork, 1f);
            }

            float minDb = Volatile.Read(ref _requestedMinDb);
            float maxDb = Volatile.Read(ref _requestedMaxDb);
            var dynamicRangeMode = (SpectrogramDynamicRangeMode)Math.Clamp(
                Volatile.Read(ref _requestedDynamicRangeMode), 0, 4);

            float floorDb = minDb;
            float ceilingDb = maxDb;
            switch (dynamicRangeMode)
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
                    float adaptRate = voicing == VoicingState.Silence ? 0.2f : 0.05f;
                    float adaptiveFloor = _dynamicRangeTracker.Update(_displaySmoothed, adaptRate);
                    floorDb = Math.Clamp(adaptiveFloor, minDb, maxDb - 1f);
                    ceilingDb = maxDb;
                    break;
                }
            }

            floorDb = Math.Min(floorDb, ceilingDb - 1f);
            float range = MathF.Max(1f, ceilingDb - floorDb);

            long frameId = _frameCounter;
            int frameIndex = (int)(frameId % _activeFrameCapacity);
            Interlocked.Increment(ref _dataVersion);

            if (reassignEnabled)
            {
                int specOffset = frameIndex * _activeDisplayBins;
                Array.Clear(_spectrogramBuffer, specOffset, _activeDisplayBins);
                BuildDisplayGain();

                float reassignThresholdDb = MathF.Max(Volatile.Read(ref _requestedReassignThreshold), floorDb);
                float reassignThresholdLinear = DspUtils.DbToLinear(reassignThresholdDb);
                float reassignSpread = Math.Clamp(Volatile.Read(ref _requestedReassignSpread), 0f, 1f);
                float maxTimeShift = MaxReassignFrameShift * reassignSpread;
                float maxBinShift = MaxReassignBinShift * reassignSpread;
                float invHop = 1f / MathF.Max(1f, _activeHopSize);
                float freqBinScale = _activeFftSize / (MathF.PI * 2f);
                long oldestFrameId = Math.Max(0, frameId - _activeFrameCapacity + 1);

                for (int bin = 0; bin < half; bin++)
                {
                    float mag = displayMagnitudes[bin];
                    if (mag <= 0f)
                    {
                        continue;
                    }

                    int displayBin = _fftBinToDisplay[bin];
                    float gain = _displayGain[displayBin];
                    if (gain <= 0f)
                    {
                        continue;
                    }

                    float adjustedMag = mag * gain;
                    if (adjustedMag < reassignThresholdLinear)
                    {
                        continue;
                    }

                    float re = _fftReal[bin];
                    float im = _fftImag[bin];
                    float denom = re * re + im * im + 1e-12f;

                    float timeShiftFrames = 0f;
                    if (_activeReassignMode.HasFlag(SpectrogramReassignMode.Time))
                    {
                        float reTime = _fftTimeReal[bin];
                        float imTime = _fftTimeImag[bin];
                        // STFT reassignment time shift from the time-weighted window.
                        float timeShiftSamples = (reTime * re + imTime * im) / denom;
                        timeShiftFrames = Math.Clamp(timeShiftSamples * invHop, -maxTimeShift, maxTimeShift);
                    }

                    float freqShiftBins = 0f;
                    if (_activeReassignMode.HasFlag(SpectrogramReassignMode.Frequency))
                    {
                        float reDeriv = _fftDerivReal[bin];
                        float imDeriv = _fftDerivImag[bin];
                        // STFT reassignment frequency shift from the window derivative FFT.
                        float imag = (imDeriv * re - reDeriv * im) / denom;
                        freqShiftBins = Math.Clamp(imag * freqBinScale, -maxBinShift, maxBinShift);
                    }

                    float targetFrame = frameId + timeShiftFrames - _reassignLatencyFrames;
                    long frameBase = (long)MathF.Floor(targetFrame);
                    float frameFrac = targetFrame - frameBase;
                    if (frameBase < oldestFrameId || frameBase > frameId)
                    {
                        continue;
                    }

                    float reassignedBin = bin + freqShiftBins;
                    if (reassignedBin < 0f || reassignedBin > half - 1)
                    {
                        continue;
                    }

                    float displayPos = GetDisplayPosition(reassignedBin);
                    int binBase = (int)MathF.Floor(displayPos);
                    float binFrac = displayPos - binBase;
                    if (binBase < 0 || binBase >= _activeDisplayBins)
                    {
                        continue;
                    }

                    float db = DspUtils.LinearToDb(adjustedMag);
                    if (db < reassignThresholdDb)
                    {
                        continue;
                    }

                    float normalized = Math.Clamp((db - floorDb) / range, 0f, 1f);
                    if (normalized <= 0f)
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
                        float value = normalized * wFrame0 * wBin0;
                        if (value > _spectrogramBuffer[baseOffset + binBase])
                        {
                            _spectrogramBuffer[baseOffset + binBase] = value;
                        }

                        int bin1 = binBase + 1;
                        if (bin1 < _activeDisplayBins && wBin1 > 0f)
                        {
                            value = normalized * wFrame0 * wBin1;
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
                        float value = normalized * wFrame1 * wBin0;
                        if (value > _spectrogramBuffer[baseOffset + binBase])
                        {
                            _spectrogramBuffer[baseOffset + binBase] = value;
                        }

                        int bin1 = binBase + 1;
                        if (bin1 < _activeDisplayBins && wBin1 > 0f)
                        {
                            value = normalized * wFrame1 * wBin1;
                            if (value > _spectrogramBuffer[baseOffset + bin1])
                            {
                                _spectrogramBuffer[baseOffset + bin1] = value;
                            }
                        }
                    }
                }
            }
            else
            {
                int specOffset = frameIndex * _activeDisplayBins;
                for (int i = 0; i < _activeDisplayBins; i++)
                {
                    float db = DspUtils.LinearToDb(_displaySmoothed[i]);
                    _spectrogramBuffer[specOffset + i] = Math.Clamp((db - floorDb) / range, 0f, 1f);
                }
            }

            _featureExtractor.Compute(_displaySmoothed, out float centroid, out float slope, out float flux);
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
