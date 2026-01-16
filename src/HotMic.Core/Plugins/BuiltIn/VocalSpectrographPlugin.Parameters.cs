using System.Threading;
using HotMic.Core.Dsp.Spectrogram;

namespace HotMic.Core.Plugins.BuiltIn;

public sealed partial class VocalSpectrographPlugin
{
    public VocalSpectrographPlugin()
    {
        Parameters =
        [
            new PluginParameter
            {
                Index = FftSizeIndex,
                Name = "FFT Size",
                MinValue = 1024f,
                MaxValue = 8192f,
                DefaultValue = 2048f,
                Unit = "samples",
                FormatValue = value => FormatDiscrete(value, FftSizes, "")
            },
            new PluginParameter
            {
                Index = WindowFunctionIndex,
                Name = "Window",
                MinValue = 0f,
                MaxValue = 4f,
                DefaultValue = (float)WindowFunction.Hann,
                Unit = "",
                FormatValue = value => ((WindowFunction)Math.Clamp((int)MathF.Round(value), 0, 4)).ToString()
            },
            new PluginParameter
            {
                Index = OverlapIndex,
                Name = "Overlap",
                MinValue = 0.5f,
                MaxValue = 0.96875f,
                DefaultValue = 0.875f, // 87.5% default for smoother display
                Unit = "%",
                FormatValue = value => $"{SelectOverlap(value) * 100f:0.##}%"
            },
            new PluginParameter
            {
                Index = ScaleIndex,
                Name = "Scale",
                MinValue = 0f,
                MaxValue = 4f,
                DefaultValue = (float)FrequencyScale.Mel,
                Unit = "",
                FormatValue = value => ((FrequencyScale)Math.Clamp((int)MathF.Round(value), 0, 4)).ToString()
            },
            new PluginParameter
            {
                Index = MinFrequencyIndex,
                Name = "Min Freq",
                MinValue = 20f,
                MaxValue = 2000f,
                DefaultValue = DefaultMinFrequency,
                Unit = "Hz"
            },
            new PluginParameter
            {
                Index = MaxFrequencyIndex,
                Name = "Max Freq",
                MinValue = 2000f,
                MaxValue = 12000f,
                DefaultValue = DefaultMaxFrequency,
                Unit = "Hz"
            },
            new PluginParameter
            {
                Index = MinDbIndex,
                Name = "Min dB",
                MinValue = -120f,
                MaxValue = -20f,
                DefaultValue = DefaultMinDb,
                Unit = "dB"
            },
            new PluginParameter
            {
                Index = MaxDbIndex,
                Name = "Max dB",
                MinValue = -40f,
                MaxValue = 0f,
                DefaultValue = DefaultMaxDb,
                Unit = "dB"
            },
            new PluginParameter
            {
                Index = TimeWindowIndex,
                Name = "Time Window",
                MinValue = 1f,
                MaxValue = 60f,
                DefaultValue = DefaultTimeWindow,
                Unit = "s"
            },
            new PluginParameter
            {
                Index = ColorMapIndex,
                Name = "Color Map",
                MinValue = 0f,
                MaxValue = 6f,
                DefaultValue = 6f,
                Unit = "",
                FormatValue = value => ((int)MathF.Round(value)).ToString()
            },
            new PluginParameter
            {
                Index = ShowPitchIndex,
                Name = "Pitch Overlay",
                MinValue = 0f,
                MaxValue = 1f,
                DefaultValue = 1f,
                Unit = ""
            },
            new PluginParameter
            {
                Index = ShowFormantsIndex,
                Name = "Formants",
                MinValue = 0f,
                MaxValue = 1f,
                DefaultValue = 1f,
                Unit = ""
            },
            new PluginParameter
            {
                Index = ShowFormantBandwidthsIndex,
                Name = "Formant Bandwidths",
                MinValue = 0f,
                MaxValue = 1f,
                DefaultValue = 0f, // Default to dots-only like Praat
                Unit = ""
            },
            new PluginParameter
            {
                Index = ShowHarmonicsIndex,
                Name = "Harmonics",
                MinValue = 0f,
                MaxValue = 1f,
                DefaultValue = 1f,
                Unit = ""
            },
            new PluginParameter
            {
                Index = HarmonicDisplayModeIndex,
                Name = "Harmonic Mode",
                MinValue = 0f,
                MaxValue = 2f,
                DefaultValue = (float)HarmonicDisplayMode.Detected,
                Unit = "",
                FormatValue = value => ((HarmonicDisplayMode)Math.Clamp((int)MathF.Round(value), 0, 2)).ToString()
            },
            new PluginParameter
            {
                Index = ShowVoicingIndex,
                Name = "Voicing",
                MinValue = 0f,
                MaxValue = 1f,
                DefaultValue = 1f,
                Unit = ""
            },
            new PluginParameter
            {
                Index = PreEmphasisIndex,
                Name = "Pre-Emphasis",
                MinValue = 0f,
                MaxValue = 1f,
                DefaultValue = 1f,
                Unit = ""
            },
            new PluginParameter
            {
                Index = HighPassEnabledIndex,
                Name = "HPF Enabled",
                MinValue = 0f,
                MaxValue = 1f,
                DefaultValue = 1f,
                Unit = ""
            },
            new PluginParameter
            {
                Index = HighPassCutoffIndex,
                Name = "HPF Cutoff",
                MinValue = 20f,
                MaxValue = 120f,
                DefaultValue = DefaultHighPassHz,
                Unit = "Hz"
            },
            new PluginParameter
            {
                Index = LpcOrderIndex,
                Name = "LPC Order",
                MinValue = 8f,
                MaxValue = 24f,
                DefaultValue = 12f,
                Unit = ""
            },
            new PluginParameter
            {
                Index = ReassignModeIndex,
                Name = "Reassign",
                MinValue = 0f,
                MaxValue = 3f,
                DefaultValue = 0f,
                Unit = "",
                FormatValue = value => ((SpectrogramReassignMode)Math.Clamp((int)MathF.Round(value), 0, 3)).ToString()
            },
            new PluginParameter
            {
                Index = ReassignThresholdIndex,
                Name = "Reassign Threshold",
                MinValue = -120f,
                MaxValue = -20f,
                DefaultValue = ReassignMinDb,
                Unit = "dB"
            },
            new PluginParameter
            {
                Index = ReassignSpreadIndex,
                Name = "Reassign Spread",
                MinValue = 0f,
                MaxValue = 1f,
                DefaultValue = 1f,
                Unit = "%",
                FormatValue = value => $"{Math.Clamp(value, 0f, 1f) * 100f:0}%"
            },
            new PluginParameter
            {
                Index = ClarityModeIndex,
                Name = "Clarity Mode",
                MinValue = 0f,
                MaxValue = 3f,
                DefaultValue = (float)ClarityProcessingMode.None,
                Unit = "",
                FormatValue = value => ((ClarityProcessingMode)Math.Clamp((int)MathF.Round(value), 0, 3)).ToString()
            },
            new PluginParameter
            {
                Index = ClarityNoiseIndex,
                Name = "Clarity Noise",
                MinValue = 0f,
                MaxValue = 1f,
                DefaultValue = 1f,
                Unit = "%",
                FormatValue = value => $"{Math.Clamp(value, 0f, 1f) * 100f:0}%"
            },
            new PluginParameter
            {
                Index = ClarityHarmonicIndex,
                Name = "Clarity Harmonic",
                MinValue = 0f,
                MaxValue = 1f,
                DefaultValue = 1f,
                Unit = "%",
                FormatValue = value => $"{Math.Clamp(value, 0f, 1f) * 100f:0}%"
            },
            new PluginParameter
            {
                Index = ClaritySmoothingIndex,
                Name = "Clarity Smoothing",
                MinValue = 0f,
                MaxValue = 1f,
                DefaultValue = TemporalSmoothingFactor,
                Unit = "%",
                FormatValue = value => $"{Math.Clamp(value, 0f, 1f) * 100f:0}%"
            },
            new PluginParameter
            {
                Index = PitchAlgorithmIndex,
                Name = "Pitch Algorithm",
                MinValue = 0f,
                MaxValue = 4f,
                DefaultValue = (float)PitchDetectorType.Yin,
                Unit = "",
                FormatValue = value => ((PitchDetectorType)Math.Clamp((int)MathF.Round(value), 0, 4)).ToString()
            },
            new PluginParameter
            {
                Index = AxisModeIndex,
                Name = "Axis Mode",
                MinValue = 0f,
                MaxValue = 2f,
                DefaultValue = (float)SpectrogramAxisMode.Hz,
                Unit = "",
                FormatValue = value => ((SpectrogramAxisMode)Math.Clamp((int)MathF.Round(value), 0, 2)).ToString()
            },
            new PluginParameter
            {
                Index = VoiceRangeIndex,
                Name = "Voice Range",
                MinValue = 0f,
                MaxValue = 5f,
                DefaultValue = (float)VocalRangeType.Tenor,
                Unit = "",
                FormatValue = value => ((VocalRangeType)Math.Clamp((int)MathF.Round(value), 0, 5)).ToString()
            },
            new PluginParameter
            {
                Index = ShowRangeIndex,
                Name = "Range Overlay",
                MinValue = 0f,
                MaxValue = 1f,
                DefaultValue = 1f,
                Unit = ""
            },
            new PluginParameter
            {
                Index = ShowGuidesIndex,
                Name = "Guides",
                MinValue = 0f,
                MaxValue = 1f,
                DefaultValue = 1f,
                Unit = ""
            },
            new PluginParameter
            {
                Index = ShowWaveformIndex,
                Name = "Waveform View",
                MinValue = 0f,
                MaxValue = 1f,
                DefaultValue = 1f,
                Unit = ""
            },
            new PluginParameter
            {
                Index = ShowSpectrumIndex,
                Name = "Spectrum View",
                MinValue = 0f,
                MaxValue = 1f,
                DefaultValue = 1f,
                Unit = ""
            },
            new PluginParameter
            {
                Index = ShowPitchMeterIndex,
                Name = "Pitch Meter",
                MinValue = 0f,
                MaxValue = 1f,
                DefaultValue = 1f,
                Unit = ""
            },
            new PluginParameter
            {
                Index = ShowVowelSpaceIndex,
                Name = "Vowel View",
                MinValue = 0f,
                MaxValue = 1f,
                DefaultValue = 1f,
                Unit = ""
            },
            new PluginParameter
            {
                Index = SmoothingModeIndex,
                Name = "Smoothing Mode",
                MinValue = 0f,
                MaxValue = 2f,
                DefaultValue = (float)SpectrogramSmoothingMode.Ema,
                Unit = "",
                FormatValue = value => ((SpectrogramSmoothingMode)Math.Clamp((int)MathF.Round(value), 0, 2)).ToString()
            },
            new PluginParameter
            {
                Index = BrightnessIndex,
                Name = "Brightness",
                MinValue = 0.5f,
                MaxValue = 2f,
                DefaultValue = DefaultBrightness,
                Unit = "x",
                FormatValue = value => $"{Math.Clamp(value, 0.5f, 2f):0.00}"
            },
            new PluginParameter
            {
                Index = GammaIndex,
                Name = "Gamma",
                MinValue = 0.6f,
                MaxValue = 1.2f,
                DefaultValue = DefaultGamma,
                Unit = "",
                FormatValue = value => $"{Math.Clamp(value, 0.6f, 1.2f):0.00}"
            },
            new PluginParameter
            {
                Index = ContrastIndex,
                Name = "Contrast",
                MinValue = 0.8f,
                MaxValue = 1.5f,
                DefaultValue = DefaultContrast,
                Unit = "x",
                FormatValue = value => $"{Math.Clamp(value, 0.8f, 1.5f):0.00}"
            },
            new PluginParameter
            {
                Index = ColorLevelsIndex,
                Name = "Color Levels",
                MinValue = ColorLevelOptions[0],
                MaxValue = ColorLevelOptions[^1],
                DefaultValue = 32f,
                Unit = "",
                FormatValue = value => FormatDiscrete(value, ColorLevelOptions, "")
            },
            new PluginParameter
            {
                Index = NormalizationModeIndex,
                Name = "Normalization",
                MinValue = 0f,
                MaxValue = 3f,
                DefaultValue = (float)SpectrogramNormalizationMode.None,
                Unit = "",
                FormatValue = value => ((SpectrogramNormalizationMode)Math.Clamp((int)MathF.Round(value), 0, 3)).ToString()
            },
            new PluginParameter
            {
                Index = DynamicRangeModeIndex,
                Name = "Dynamic Range",
                MinValue = 0f,
                MaxValue = 4f,
                DefaultValue = (float)SpectrogramDynamicRangeMode.Custom,
                Unit = "",
                FormatValue = value => ((SpectrogramDynamicRangeMode)Math.Clamp((int)MathF.Round(value), 0, 4)).ToString()
            },
            new PluginParameter
            {
                Index = TransformTypeIndex,
                Name = "Transform",
                MinValue = 0f,
                MaxValue = 2f,
                DefaultValue = (float)SpectrogramTransformType.Fft,
                Unit = "",
                FormatValue = value => ((SpectrogramTransformType)Math.Clamp((int)MathF.Round(value), 0, 2)).ToString()
            },
            new PluginParameter
            {
                Index = CqtBinsPerOctaveIndex,
                Name = "CQT Bins/Oct",
                MinValue = 12f,
                MaxValue = 96f,
                DefaultValue = 48f,
                Unit = "",
                FormatValue = value => FormatDiscrete(value, CqtBinsPerOctaveOptions, "")
            }
        ];
    }

    public string Id => "builtin:vocal-spectrograph";

    public string Name => "Vocal Spectrograph";

    public bool IsBypassed { get; set; }

    public int LatencySamples => 0;

    public IReadOnlyList<PluginParameter> Parameters { get; }

    public int SampleRate => _sampleRate;

    public int FftSize => Volatile.Read(ref _activeFftSize);

    public int DisplayBins => Volatile.Read(ref _activeDisplayBins);

    public int AnalysisBins => Volatile.Read(ref _activeAnalysisBins);

    /// <summary>
    /// Current analysis layout descriptor for display mapping.
    /// </summary>
    public SpectrogramAnalysisDescriptor? AnalysisDescriptor => Volatile.Read(ref _analysisDescriptor);

    /// <summary>
    /// Gets discontinuity events that occurred at or after the specified frame ID.
    /// Used by the renderer to display markers indicating parameter changes.
    /// </summary>
    /// <param name="oldestFrameId">Only return events at or after this frame ID.</param>
    /// <returns>List of discontinuity events within the visible range.</returns>
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

    /// <summary>
    /// Hop size in samples used for analysis.
    /// </summary>
    public int HopSize => Volatile.Read(ref _activeHopSize);

    public int FrameCount => Volatile.Read(ref _activeFrameCapacity);

    public int MaxFormantCount => MaxFormants;

    public int MaxHarmonicCount => MaxHarmonics;

    public int DataVersion => Volatile.Read(ref _dataVersion);

    public long LatestFrameId => Volatile.Read(ref _latestFrameId);

    public int AvailableFrames => Volatile.Read(ref _availableFrames);

    /// <summary>
    /// Total samples dropped while enqueueing analysis input.
    /// </summary>
    public long DroppedSamples => _captureBuffer.DroppedSamples;

    public FrequencyScale Scale => (FrequencyScale)Math.Clamp(Volatile.Read(ref _requestedScale), 0, 4);

    public WindowFunction WindowFunction => (WindowFunction)Math.Clamp(Volatile.Read(ref _requestedWindow), 0, 4);

    public float Overlap => OverlapOptions[Math.Clamp(Volatile.Read(ref _requestedOverlapIndex), 0, OverlapOptions.Length - 1)];

    public SpectrogramReassignMode ReassignMode =>
        (SpectrogramReassignMode)Math.Clamp(Volatile.Read(ref _requestedReassignMode), 0, 3);

    public float ReassignThresholdDb => Volatile.Read(ref _requestedReassignThreshold);

    public float ReassignSpread => Volatile.Read(ref _requestedReassignSpread);

    public ClarityProcessingMode ClarityMode =>
        (ClarityProcessingMode)Math.Clamp(Volatile.Read(ref _requestedClarityMode), 0, 3);

    public float ClarityNoise => Volatile.Read(ref _requestedClarityNoise);

    public float ClarityHarmonic => Volatile.Read(ref _requestedClarityHarmonic);

    public float ClaritySmoothing => Volatile.Read(ref _requestedClaritySmoothing);

    public PitchDetectorType PitchAlgorithm =>
        (PitchDetectorType)Math.Clamp(Volatile.Read(ref _requestedPitchAlgorithm), 0, 4);

    public SpectrogramAxisMode AxisMode =>
        (SpectrogramAxisMode)Math.Clamp(Volatile.Read(ref _requestedAxisMode), 0, 2);

    public VocalRangeType VoiceRange =>
        (VocalRangeType)Math.Clamp(Volatile.Read(ref _requestedVoiceRange), 0, 5);

    public bool ShowRange => Volatile.Read(ref _requestedShowRange) != 0;

    public bool ShowGuides => Volatile.Read(ref _requestedShowGuides) != 0;

    public bool ShowWaveform => Volatile.Read(ref _requestedShowWaveform) != 0;

    public bool ShowSpectrum => Volatile.Read(ref _requestedShowSpectrum) != 0;

    public bool ShowPitchMeter => Volatile.Read(ref _requestedShowPitchMeter) != 0;

    public bool ShowVowelSpace => Volatile.Read(ref _requestedShowVowelSpace) != 0;

    public SpectrogramSmoothingMode SmoothingMode =>
        (SpectrogramSmoothingMode)Math.Clamp(Volatile.Read(ref _requestedSmoothingMode), 0, 2);

    public float Brightness => Volatile.Read(ref _requestedBrightness);

    public float Gamma => Volatile.Read(ref _requestedGamma);

    public float Contrast => Volatile.Read(ref _requestedContrast);

    public int ColorLevels => SelectDiscrete(Volatile.Read(ref _requestedColorLevels), ColorLevelOptions);

    public SpectrogramNormalizationMode NormalizationMode =>
        (SpectrogramNormalizationMode)Math.Clamp(Volatile.Read(ref _requestedNormalizationMode), 0, 3);

    public SpectrogramDynamicRangeMode DynamicRangeMode =>
        (SpectrogramDynamicRangeMode)Math.Clamp(Volatile.Read(ref _requestedDynamicRangeMode), 0, 4);

    public SpectrogramTransformType TransformType =>
        (SpectrogramTransformType)Math.Clamp(Volatile.Read(ref _requestedTransformType), 0, 2);

    public int CqtBinsPerOctave => SelectDiscrete(Volatile.Read(ref _requestedCqtBinsPerOctave), CqtBinsPerOctaveOptions);

    public float MinFrequency => Volatile.Read(ref _requestedMinFrequency);

    public float MaxFrequency => Volatile.Read(ref _requestedMaxFrequency);

    public float MinDb => Volatile.Read(ref _requestedMinDb);

    public float MaxDb => Volatile.Read(ref _requestedMaxDb);

    public float TimeWindowSeconds => Volatile.Read(ref _requestedTimeWindow);

    public int ColorMap => Volatile.Read(ref _requestedColorMap);

    public bool ShowPitch => Volatile.Read(ref _requestedShowPitch) != 0;

    public bool ShowFormants => Volatile.Read(ref _requestedShowFormants) != 0;

    public bool ShowFormantBandwidths => Volatile.Read(ref _requestedShowFormantBandwidths) != 0;

    public bool ShowHarmonics => Volatile.Read(ref _requestedShowHarmonics) != 0;

    public HarmonicDisplayMode HarmonicDisplayMode =>
        (HarmonicDisplayMode)Math.Clamp(Volatile.Read(ref _requestedHarmonicDisplayMode), 0, 2);

    public bool ShowVoicing => Volatile.Read(ref _requestedShowVoicing) != 0;

    public bool PreEmphasisEnabled => Volatile.Read(ref _requestedPreEmphasis) != 0;

    public bool HighPassEnabled => Volatile.Read(ref _requestedHighPassEnabled) != 0;

    public float HighPassCutoff => Volatile.Read(ref _requestedHighPassCutoff);

    public int LpcOrder => Volatile.Read(ref _requestedLpcOrder);

    public void SetParameter(int index, float value)
    {
        switch (index)
        {
            case FftSizeIndex:
                Interlocked.Exchange(ref _requestedFftSize, SelectDiscrete(value, FftSizes));
                break;
            case WindowFunctionIndex:
                Interlocked.Exchange(ref _requestedWindow, Math.Clamp((int)MathF.Round(value), 0, 4));
                break;
            case OverlapIndex:
                Interlocked.Exchange(ref _requestedOverlapIndex, SelectOverlapIndex(value));
                break;
            case ScaleIndex:
                Interlocked.Exchange(ref _requestedScale, Math.Clamp((int)MathF.Round(value), 0, 4));
                break;
            case MinFrequencyIndex:
            {
                float max = Volatile.Read(ref _requestedMaxFrequency);
                float next = Math.Clamp(value, 20f, MathF.Max(100f, max - 10f));
                Interlocked.Exchange(ref _requestedMinFrequency, next);
                break;
            }
            case MaxFrequencyIndex:
            {
                float min = Volatile.Read(ref _requestedMinFrequency);
                float next = Math.Clamp(value, MathF.Min(20000f, min + 10f), 20000f);
                Interlocked.Exchange(ref _requestedMaxFrequency, next);
                break;
            }
            case MinDbIndex:
            {
                float max = Volatile.Read(ref _requestedMaxDb);
                float next = Math.Clamp(value, -120f, MathF.Min(-1f, max - 1f));
                Interlocked.Exchange(ref _requestedMinDb, next);
                Interlocked.Exchange(ref _requestedDynamicRangeMode, (int)SpectrogramDynamicRangeMode.Custom);
                break;
            }
            case MaxDbIndex:
            {
                float min = Volatile.Read(ref _requestedMinDb);
                float next = Math.Clamp(value, MathF.Max(-120f, min + 1f), 0f);
                Interlocked.Exchange(ref _requestedMaxDb, next);
                Interlocked.Exchange(ref _requestedDynamicRangeMode, (int)SpectrogramDynamicRangeMode.Custom);
                break;
            }
            case TimeWindowIndex:
                Interlocked.Exchange(ref _requestedTimeWindow, Math.Clamp(value, 1f, 60f));
                break;
            case ColorMapIndex:
                Interlocked.Exchange(ref _requestedColorMap, Math.Clamp((int)MathF.Round(value), 0, 6));
                break;
            case ShowPitchIndex:
                Interlocked.Exchange(ref _requestedShowPitch, value >= 0.5f ? 1 : 0);
                break;
            case ShowFormantsIndex:
                Interlocked.Exchange(ref _requestedShowFormants, value >= 0.5f ? 1 : 0);
                break;
            case ShowFormantBandwidthsIndex:
                Interlocked.Exchange(ref _requestedShowFormantBandwidths, value >= 0.5f ? 1 : 0);
                break;
            case ShowHarmonicsIndex:
                Interlocked.Exchange(ref _requestedShowHarmonics, value >= 0.5f ? 1 : 0);
                break;
            case HarmonicDisplayModeIndex:
                Interlocked.Exchange(ref _requestedHarmonicDisplayMode, Math.Clamp((int)MathF.Round(value), 0, 2));
                break;
            case ShowVoicingIndex:
                Interlocked.Exchange(ref _requestedShowVoicing, value >= 0.5f ? 1 : 0);
                break;
            case PreEmphasisIndex:
                Interlocked.Exchange(ref _requestedPreEmphasis, value >= 0.5f ? 1 : 0);
                break;
            case HighPassEnabledIndex:
                Interlocked.Exchange(ref _requestedHighPassEnabled, value >= 0.5f ? 1 : 0);
                break;
            case HighPassCutoffIndex:
                Interlocked.Exchange(ref _requestedHighPassCutoff, Math.Clamp(value, 20f, 120f));
                break;
            case LpcOrderIndex:
                Interlocked.Exchange(ref _requestedLpcOrder, Math.Clamp((int)MathF.Round(value), 8, 24));
                break;
            case ReassignModeIndex:
                Interlocked.Exchange(ref _requestedReassignMode, Math.Clamp((int)MathF.Round(value), 0, 3));
                break;
            case ReassignThresholdIndex:
                Interlocked.Exchange(ref _requestedReassignThreshold, Math.Clamp(value, -120f, -20f));
                break;
            case ReassignSpreadIndex:
                Interlocked.Exchange(ref _requestedReassignSpread, Math.Clamp(value, 0f, 1f));
                break;
            case ClarityModeIndex:
                Interlocked.Exchange(ref _requestedClarityMode, Math.Clamp((int)MathF.Round(value), 0, 3));
                break;
            case ClarityNoiseIndex:
                Interlocked.Exchange(ref _requestedClarityNoise, Math.Clamp(value, 0f, 1f));
                break;
            case ClarityHarmonicIndex:
                Interlocked.Exchange(ref _requestedClarityHarmonic, Math.Clamp(value, 0f, 1f));
                break;
            case ClaritySmoothingIndex:
                Interlocked.Exchange(ref _requestedClaritySmoothing, Math.Clamp(value, 0f, 1f));
                break;
            case PitchAlgorithmIndex:
                int pitchAlgorithm = Math.Clamp((int)MathF.Round(value), 0, 4);
                if (pitchAlgorithm == (int)PitchDetectorType.Swipe
                    && Volatile.Read(ref _requestedTransformType) == (int)SpectrogramTransformType.Cqt)
                {
                    pitchAlgorithm = (int)PitchDetectorType.Yin;
                }
                Interlocked.Exchange(ref _requestedPitchAlgorithm, pitchAlgorithm);
                break;
            case AxisModeIndex:
                Interlocked.Exchange(ref _requestedAxisMode, Math.Clamp((int)MathF.Round(value), 0, 2));
                break;
            case VoiceRangeIndex:
                Interlocked.Exchange(ref _requestedVoiceRange, Math.Clamp((int)MathF.Round(value), 0, 5));
                break;
            case ShowRangeIndex:
                Interlocked.Exchange(ref _requestedShowRange, value >= 0.5f ? 1 : 0);
                break;
            case ShowGuidesIndex:
                Interlocked.Exchange(ref _requestedShowGuides, value >= 0.5f ? 1 : 0);
                break;
            case ShowWaveformIndex:
                Interlocked.Exchange(ref _requestedShowWaveform, value >= 0.5f ? 1 : 0);
                break;
            case ShowSpectrumIndex:
                Interlocked.Exchange(ref _requestedShowSpectrum, value >= 0.5f ? 1 : 0);
                break;
            case ShowPitchMeterIndex:
                Interlocked.Exchange(ref _requestedShowPitchMeter, value >= 0.5f ? 1 : 0);
                break;
            case ShowVowelSpaceIndex:
                Interlocked.Exchange(ref _requestedShowVowelSpace, value >= 0.5f ? 1 : 0);
                break;
            case SmoothingModeIndex:
                Interlocked.Exchange(ref _requestedSmoothingMode, Math.Clamp((int)MathF.Round(value), 0, 2));
                break;
            case BrightnessIndex:
                Interlocked.Exchange(ref _requestedBrightness, Math.Clamp(value, 0.5f, 2f));
                break;
            case GammaIndex:
                Interlocked.Exchange(ref _requestedGamma, Math.Clamp(value, 0.6f, 1.2f));
                break;
            case ContrastIndex:
                Interlocked.Exchange(ref _requestedContrast, Math.Clamp(value, 0.8f, 1.5f));
                break;
            case ColorLevelsIndex:
                Interlocked.Exchange(ref _requestedColorLevels, SelectDiscrete(value, ColorLevelOptions));
                break;
            case NormalizationModeIndex:
                Interlocked.Exchange(ref _requestedNormalizationMode, Math.Clamp((int)MathF.Round(value), 0, 3));
                break;
            case DynamicRangeModeIndex:
                Interlocked.Exchange(ref _requestedDynamicRangeMode, Math.Clamp((int)MathF.Round(value), 0, 4));
                break;
            case TransformTypeIndex:
                int transformType = Math.Clamp((int)MathF.Round(value), 0, 2);
                Interlocked.Exchange(ref _requestedTransformType, transformType);
                if (transformType == (int)SpectrogramTransformType.Cqt
                    && (PitchDetectorType)Math.Clamp(Volatile.Read(ref _requestedPitchAlgorithm), 0, 4) == PitchDetectorType.Swipe)
                {
                    Interlocked.Exchange(ref _requestedPitchAlgorithm, (int)PitchDetectorType.Yin);
                }
                break;
            case CqtBinsPerOctaveIndex:
                Interlocked.Exchange(ref _requestedCqtBinsPerOctave, SelectDiscrete(value, CqtBinsPerOctaveOptions));
                break;
        }
    }

    public byte[] GetState()
    {
        var bytes = new byte[sizeof(float) * 44];
        Buffer.BlockCopy(BitConverter.GetBytes((float)_requestedFftSize), 0, bytes, 0, 4);
        Buffer.BlockCopy(BitConverter.GetBytes((float)_requestedWindow), 0, bytes, 4, 4);
        Buffer.BlockCopy(BitConverter.GetBytes(OverlapOptions[_requestedOverlapIndex]), 0, bytes, 8, 4);
        Buffer.BlockCopy(BitConverter.GetBytes((float)_requestedScale), 0, bytes, 12, 4);
        Buffer.BlockCopy(BitConverter.GetBytes(_requestedMinFrequency), 0, bytes, 16, 4);
        Buffer.BlockCopy(BitConverter.GetBytes(_requestedMaxFrequency), 0, bytes, 20, 4);
        Buffer.BlockCopy(BitConverter.GetBytes(_requestedMinDb), 0, bytes, 24, 4);
        Buffer.BlockCopy(BitConverter.GetBytes(_requestedMaxDb), 0, bytes, 28, 4);
        Buffer.BlockCopy(BitConverter.GetBytes(_requestedTimeWindow), 0, bytes, 32, 4);
        Buffer.BlockCopy(BitConverter.GetBytes((float)_requestedColorMap), 0, bytes, 36, 4);
        Buffer.BlockCopy(BitConverter.GetBytes(_requestedShowPitch), 0, bytes, 40, 4);
        Buffer.BlockCopy(BitConverter.GetBytes(_requestedShowFormants), 0, bytes, 44, 4);
        Buffer.BlockCopy(BitConverter.GetBytes(_requestedShowHarmonics), 0, bytes, 48, 4);
        Buffer.BlockCopy(BitConverter.GetBytes(_requestedShowVoicing), 0, bytes, 52, 4);
        Buffer.BlockCopy(BitConverter.GetBytes(_requestedPreEmphasis), 0, bytes, 56, 4);
        Buffer.BlockCopy(BitConverter.GetBytes(_requestedHighPassEnabled), 0, bytes, 60, 4);
        Buffer.BlockCopy(BitConverter.GetBytes(_requestedHighPassCutoff), 0, bytes, 64, 4);
        Buffer.BlockCopy(BitConverter.GetBytes((float)_requestedLpcOrder), 0, bytes, 68, 4);
        Buffer.BlockCopy(BitConverter.GetBytes((float)_requestedReassignMode), 0, bytes, 72, 4);
        Buffer.BlockCopy(BitConverter.GetBytes(_requestedReassignThreshold), 0, bytes, 76, 4);
        Buffer.BlockCopy(BitConverter.GetBytes(_requestedReassignSpread), 0, bytes, 80, 4);
        Buffer.BlockCopy(BitConverter.GetBytes((float)_requestedClarityMode), 0, bytes, 84, 4);
        Buffer.BlockCopy(BitConverter.GetBytes(_requestedClarityNoise), 0, bytes, 88, 4);
        Buffer.BlockCopy(BitConverter.GetBytes(_requestedClarityHarmonic), 0, bytes, 92, 4);
        Buffer.BlockCopy(BitConverter.GetBytes(_requestedClaritySmoothing), 0, bytes, 96, 4);
        Buffer.BlockCopy(BitConverter.GetBytes((float)_requestedPitchAlgorithm), 0, bytes, 100, 4);
        Buffer.BlockCopy(BitConverter.GetBytes((float)_requestedAxisMode), 0, bytes, 104, 4);
        Buffer.BlockCopy(BitConverter.GetBytes((float)_requestedVoiceRange), 0, bytes, 108, 4);
        Buffer.BlockCopy(BitConverter.GetBytes(_requestedShowRange), 0, bytes, 112, 4);
        Buffer.BlockCopy(BitConverter.GetBytes(_requestedShowGuides), 0, bytes, 116, 4);
        Buffer.BlockCopy(BitConverter.GetBytes(_requestedShowWaveform), 0, bytes, 120, 4);
        Buffer.BlockCopy(BitConverter.GetBytes(_requestedShowSpectrum), 0, bytes, 124, 4);
        Buffer.BlockCopy(BitConverter.GetBytes(_requestedShowPitchMeter), 0, bytes, 128, 4);
        Buffer.BlockCopy(BitConverter.GetBytes(_requestedShowVowelSpace), 0, bytes, 132, 4);
        Buffer.BlockCopy(BitConverter.GetBytes((float)_requestedSmoothingMode), 0, bytes, 136, 4);
        Buffer.BlockCopy(BitConverter.GetBytes(_requestedBrightness), 0, bytes, 140, 4);
        Buffer.BlockCopy(BitConverter.GetBytes(_requestedGamma), 0, bytes, 144, 4);
        Buffer.BlockCopy(BitConverter.GetBytes(_requestedContrast), 0, bytes, 148, 4);
        Buffer.BlockCopy(BitConverter.GetBytes((float)_requestedColorLevels), 0, bytes, 152, 4);
        Buffer.BlockCopy(BitConverter.GetBytes((float)_requestedNormalizationMode), 0, bytes, 156, 4);
        Buffer.BlockCopy(BitConverter.GetBytes((float)_requestedDynamicRangeMode), 0, bytes, 160, 4);
        Buffer.BlockCopy(BitConverter.GetBytes((float)_requestedTransformType), 0, bytes, 164, 4);
        Buffer.BlockCopy(BitConverter.GetBytes((float)_requestedCqtBinsPerOctave), 0, bytes, 168, 4);
        Buffer.BlockCopy(BitConverter.GetBytes((float)_requestedHarmonicDisplayMode), 0, bytes, 172, 4);
        return bytes;
    }

    public void SetState(byte[] state)
    {
        if (state.Length < sizeof(float) * 19)
        {
            return;
        }

        SetParameter(FftSizeIndex, BitConverter.ToSingle(state, 0));
        SetParameter(WindowFunctionIndex, BitConverter.ToSingle(state, 4));
        SetParameter(OverlapIndex, BitConverter.ToSingle(state, 8));
        SetParameter(ScaleIndex, BitConverter.ToSingle(state, 12));
        SetParameter(MinFrequencyIndex, BitConverter.ToSingle(state, 16));
        SetParameter(MaxFrequencyIndex, BitConverter.ToSingle(state, 20));
        SetParameter(MinDbIndex, BitConverter.ToSingle(state, 24));
        SetParameter(MaxDbIndex, BitConverter.ToSingle(state, 28));
        SetParameter(TimeWindowIndex, BitConverter.ToSingle(state, 32));
        SetParameter(ColorMapIndex, BitConverter.ToSingle(state, 36));
        SetParameter(ShowPitchIndex, BitConverter.ToSingle(state, 40));
        SetParameter(ShowFormantsIndex, BitConverter.ToSingle(state, 44));
        SetParameter(ShowHarmonicsIndex, BitConverter.ToSingle(state, 48));
        SetParameter(ShowVoicingIndex, BitConverter.ToSingle(state, 52));
        SetParameter(PreEmphasisIndex, BitConverter.ToSingle(state, 56));
        SetParameter(HighPassEnabledIndex, BitConverter.ToSingle(state, 60));
        SetParameter(HighPassCutoffIndex, BitConverter.ToSingle(state, 64));
        SetParameter(LpcOrderIndex, BitConverter.ToSingle(state, 68));
        SetParameter(ReassignModeIndex, BitConverter.ToSingle(state, 72));

        if (state.Length >= sizeof(float) * 25)
        {
            SetParameter(ReassignThresholdIndex, BitConverter.ToSingle(state, 76));
            SetParameter(ReassignSpreadIndex, BitConverter.ToSingle(state, 80));
            SetParameter(ClarityModeIndex, BitConverter.ToSingle(state, 84));
            SetParameter(ClarityNoiseIndex, BitConverter.ToSingle(state, 88));
            SetParameter(ClarityHarmonicIndex, BitConverter.ToSingle(state, 92));
            SetParameter(ClaritySmoothingIndex, BitConverter.ToSingle(state, 96));
        }

        if (state.Length >= sizeof(float) * 39)
        {
            SetParameter(PitchAlgorithmIndex, BitConverter.ToSingle(state, 100));
            SetParameter(AxisModeIndex, BitConverter.ToSingle(state, 104));
            SetParameter(VoiceRangeIndex, BitConverter.ToSingle(state, 108));
            SetParameter(ShowRangeIndex, BitConverter.ToSingle(state, 112));
            SetParameter(ShowGuidesIndex, BitConverter.ToSingle(state, 116));
            SetParameter(ShowWaveformIndex, BitConverter.ToSingle(state, 120));
            SetParameter(ShowSpectrumIndex, BitConverter.ToSingle(state, 124));
            SetParameter(ShowPitchMeterIndex, BitConverter.ToSingle(state, 128));
            SetParameter(ShowVowelSpaceIndex, BitConverter.ToSingle(state, 132));
            SetParameter(SmoothingModeIndex, BitConverter.ToSingle(state, 136));
            SetParameter(BrightnessIndex, BitConverter.ToSingle(state, 140));
            SetParameter(GammaIndex, BitConverter.ToSingle(state, 144));
            SetParameter(ContrastIndex, BitConverter.ToSingle(state, 148));
            SetParameter(ColorLevelsIndex, BitConverter.ToSingle(state, 152));
        }

        if (state.Length >= sizeof(float) * 41)
        {
            SetParameter(NormalizationModeIndex, BitConverter.ToSingle(state, 156));
            SetParameter(DynamicRangeModeIndex, BitConverter.ToSingle(state, 160));
        }

        if (state.Length >= sizeof(float) * 43)
        {
            SetParameter(TransformTypeIndex, BitConverter.ToSingle(state, 164));
            SetParameter(CqtBinsPerOctaveIndex, BitConverter.ToSingle(state, 168));
        }

        if (state.Length >= sizeof(float) * 44)
        {
            SetParameter(HarmonicDisplayModeIndex, BitConverter.ToSingle(state, 172));
        }
    }
}
