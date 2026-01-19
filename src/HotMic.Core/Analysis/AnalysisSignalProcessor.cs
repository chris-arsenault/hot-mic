using System;
using System.Threading;
using HotMic.Core.Dsp;
using HotMic.Core.Dsp.Analysis;
using HotMic.Core.Dsp.Analysis.Formants;
using HotMic.Core.Dsp.Analysis.Pitch;
using HotMic.Core.Dsp.Fft;
using HotMic.Core.Dsp.Filters;
using HotMic.Core.Dsp.Spectrogram;
using HotMic.Core.Dsp.Voice;
using HotMic.Core.Plugins;

namespace HotMic.Core.Analysis;

public readonly record struct AnalysisSignalProcessorSettings
{
    public int AnalysisSize { get; init; }
    public PitchDetectorType PitchDetector { get; init; }
    public FormantProfile FormantProfile { get; init; }
    public float MinFrequency { get; init; }
    public float MaxFrequency { get; init; }
    public WindowFunction WindowFunction { get; init; }
    public bool PreEmphasisEnabled { get; init; }
    public bool HighPassEnabled { get; init; }
    public float HighPassCutoff { get; init; }

    public static AnalysisSignalProcessorSettings Default => new()
    {
        AnalysisSize = 2048,
        PitchDetector = PitchDetectorType.Yin,
        FormantProfile = FormantProfile.Tenor,
        MinFrequency = 80f,
        MaxFrequency = 8000f,
        WindowFunction = WindowFunction.Hann,
        PreEmphasisEnabled = true,
        HighPassEnabled = true,
        HighPassCutoff = 60f
    };
}

public sealed class AnalysisSignalProcessor
{
    private const float SpeechThresholdDb = -50f;
    private const float SpeechRangeDb = 30f;
    private const float FricativeHighHz = 2500f;
    private const float SibilanceCenterHz = 6500f;
    private const float SibilanceQ = 1.2f;
    private const float DcCutoffHz = 10f;
    private const float DefaultPreEmphasis = 0.97f;
    private const float VowelEnergyMinHz = 200f;
    private const float VowelEnergyMaxHz = 1000f;
    private const float VowelEnergyRatioThreshold = 0.15f;
    private const float LpcWindowSeconds = 0.025f;
    private const float LpcGaussianSigma = 0.4f;

    private int _sampleRate;
    private int _hopSize;
    private int _analysisSize;
    private int _analysisBins;
    private float _binResolution;
    private AnalysisSignalProcessorSettings _settings;

    private float[] _analysisBufferRaw = Array.Empty<float>();
    private float[] _analysisBufferProcessed = Array.Empty<float>();
    private float[] _analysisWindow = Array.Empty<float>();
    private float[] _fftReal = Array.Empty<float>();
    private float[] _fftImag = Array.Empty<float>();
    private float[] _fftMagnitudes = Array.Empty<float>();
    private float[] _highFluxPrevious = Array.Empty<float>();
    private float[] _binFrequencies = Array.Empty<float>();

    private FastFft? _fft;
    private readonly SpectralFeatureExtractor _featureExtractor = new();
    private readonly VoicingDetector _voicingDetector = new();

    private OnePoleHighPass _dcHighPass;
    private BiquadFilter _rumbleHighPass = new();
    private PreEmphasisFilter _preEmphasisFilter;

    private readonly EnvelopeFollower _speechEnv = new();
    private readonly EnvelopeFollower _fricativeEnv = new();
    private readonly EnvelopeFollower _sibilanceEnv = new();
    private readonly BiquadFilter _fricativeHighPass = new();
    private readonly BiquadFilter _sibilanceBand = new();

    private YinPitchDetector? _yinPitchDetector;
    private PyinPitchDetector? _pyinPitchDetector;
    private AutocorrelationPitchDetector? _autocorrPitchDetector;
    private CepstralPitchDetector? _cepstralPitchDetector;
    private SwipePitchDetector? _swipePitchDetector;

    private LpcAnalyzer? _lpcAnalyzer;
    private BeamSearchFormantTracker? _beamFormantTracker;
    private DecimatingFilter _lpcDecimator1 = new();
    private DecimatingFilter _lpcDecimator2 = new();
    private PreEmphasisFilter _lpcPreEmphasisFilter;
    private float[] _lpcCoefficients = Array.Empty<float>();
    private float[] _lpcInputBuffer = Array.Empty<float>();
    private float[] _lpcDecimateBuffer1 = Array.Empty<float>();
    private float[] _lpcDecimatedBuffer = Array.Empty<float>();
    private float[] _lpcWindowedBuffer = Array.Empty<float>();
    private float[] _lpcWindow = Array.Empty<float>();
    private int _lpcWindowLength;
    private int _lpcWindowSamples;
    private int _lpcSampleRate;
    private int _lpcDecimationStages;
    private FormantTrackingPreset _formantPreset;
    private float _formantCeilingHz;

    private readonly float[] _formantFreqScratch = new float[3];
    private readonly float[] _formantBwScratch = new float[3];

    private float[] _speechBuffer = Array.Empty<float>();
    private float[] _voicingScoreBuffer = Array.Empty<float>();
    private float[] _voicingStateBuffer = Array.Empty<float>();
    private float[] _fricativeBuffer = Array.Empty<float>();
    private float[] _sibilanceBuffer = Array.Empty<float>();
    private float[] _onsetFluxBuffer = Array.Empty<float>();
    private float[] _pitchBuffer = Array.Empty<float>();
    private float[] _pitchConfidenceBuffer = Array.Empty<float>();
    private float[] _formantF1Buffer = Array.Empty<float>();
    private float[] _formantF2Buffer = Array.Empty<float>();
    private float[] _formantF3Buffer = Array.Empty<float>();
    private float[] _formantConfidenceBuffer = Array.Empty<float>();
    private float[] _spectralFluxBuffer = Array.Empty<float>();
    private float[] _hnrDbBuffer = Array.Empty<float>();

    private readonly float[] _lastValues = new float[(int)AnalysisSignalId.Count];
    private float _lastCpp;
    private float _lastCentroid;
    private float _lastSlope;
    private float _lastFormantBandwidth1;
    private float _lastFormantBandwidth2;
    private float _lastFormantBandwidth3;

    public int SampleRate => _sampleRate;
    public int HopSize => _hopSize;
    public int AnalysisSize => _analysisSize;
    public float LastCpp => Volatile.Read(ref _lastCpp);
    public float LastCentroid => Volatile.Read(ref _lastCentroid);
    public float LastSlope => Volatile.Read(ref _lastSlope);
    public float LastFormantBandwidth1 => Volatile.Read(ref _lastFormantBandwidth1);
    public float LastFormantBandwidth2 => Volatile.Read(ref _lastFormantBandwidth2);
    public float LastFormantBandwidth3 => Volatile.Read(ref _lastFormantBandwidth3);

    public float GetLastValue(AnalysisSignalId signal)
    {
        int index = (int)signal;
        if ((uint)index >= (uint)_lastValues.Length)
        {
            return 0f;
        }

        return Volatile.Read(ref _lastValues[index]);
    }

    public void Configure(int sampleRate, int hopSize, AnalysisSignalProcessorSettings settings)
    {
        _sampleRate = Math.Max(1, sampleRate);
        _hopSize = Math.Max(1, hopSize);
        _settings = settings;

        int desiredSize = Math.Max(_hopSize, Math.Max(64, settings.AnalysisSize));
        _analysisSize = NextPowerOfTwo(desiredSize);
        _analysisBins = _analysisSize / 2;
        _binResolution = _sampleRate / (float)_analysisSize;

        EnsureBuffer(ref _analysisBufferRaw, _analysisSize);
        EnsureBuffer(ref _analysisBufferProcessed, _analysisSize);
        EnsureBuffer(ref _analysisWindow, _analysisSize);
        EnsureBuffer(ref _fftReal, _analysisSize);
        EnsureBuffer(ref _fftImag, _analysisSize);
        EnsureBuffer(ref _fftMagnitudes, _analysisBins);
        EnsureBuffer(ref _highFluxPrevious, _analysisBins);
        EnsureBuffer(ref _binFrequencies, _analysisBins);

        _fft ??= new FastFft(_analysisSize);
        if (_fft.Size != _analysisSize)
        {
            _fft = new FastFft(_analysisSize);
        }

        WindowFunctions.Fill(_analysisWindow, settings.WindowFunction);

        for (int i = 0; i < _analysisBins; i++)
        {
            _binFrequencies[i] = i * _binResolution;
        }

        _featureExtractor.EnsureCapacity(_analysisBins);
        _featureExtractor.UpdateFrequencies(_binFrequencies);

        EnsureSignalBuffers(_hopSize);

        ConfigureFilters(settings);
        ConfigurePitchDetectors(settings.MinFrequency, settings.MaxFrequency);
        ConfigureFormantTracking(settings.FormantProfile);
    }

    public void Reset()
    {
        _dcHighPass.Reset();
        _rumbleHighPass.Reset();
        _preEmphasisFilter.Reset();
        _speechEnv.Reset();
        _fricativeEnv.Reset();
        _sibilanceEnv.Reset();
        _fricativeHighPass.Reset();
        _sibilanceBand.Reset();
        _featureExtractor.Reset();
        Array.Clear(_highFluxPrevious, 0, _highFluxPrevious.Length);

        _beamFormantTracker?.Reset();
    }

    public void ProcessBlock(ReadOnlySpan<float> hop, long sampleTime, in AnalysisSignalWriter writer, AnalysisSignalMask requestedSignals)
    {
        int count = hop.Length;
        if (count == 0 || sampleTime < 0)
        {
            return;
        }

        if (count > _analysisSize)
        {
            hop = hop.Slice(count - _analysisSize);
            count = hop.Length;
        }

        if (_speechBuffer.Length < count || _analysisBufferRaw.Length < _analysisSize)
        {
            return;
        }

        int tail = Math.Max(0, _analysisSize - count);
        if (tail > 0)
        {
            Array.Copy(_analysisBufferRaw, count, _analysisBufferRaw, 0, tail);
            Array.Copy(_analysisBufferProcessed, count, _analysisBufferProcessed, 0, tail);
        }

        bool needSpeech = (requestedSignals & AnalysisSignalMask.SpeechPresence) != 0;
        bool needFricative = (requestedSignals & AnalysisSignalMask.FricativeActivity) != 0;
        bool needSibilance = (requestedSignals & AnalysisSignalMask.SibilanceEnergy) != 0;
        bool needStreaming = needSpeech || needFricative || needSibilance;

        float speechSum = 0f;
        float fricativeSum = 0f;
        float sibilanceSum = 0f;

        for (int i = 0; i < count; i++)
        {
            float input = hop[i];
            float dcRemoved = _dcHighPass.Process(input);
            float filtered = _settings.HighPassEnabled
                ? _rumbleHighPass.Process(dcRemoved)
                : dcRemoved;
            float emphasized = _settings.PreEmphasisEnabled
                ? _preEmphasisFilter.Process(filtered)
                : filtered;

            int index = tail + i;
            if ((uint)index < (uint)_analysisBufferRaw.Length)
            {
                _analysisBufferRaw[index] = filtered;
                _analysisBufferProcessed[index] = emphasized;
            }

            if (!needStreaming)
            {
                continue;
            }

            float env = _speechEnv.Process(filtered);
            float envDb = DspUtils.LinearToDb(env);
            float presence = Math.Clamp((envDb - SpeechThresholdDb) / SpeechRangeDb, 0f, 1f);
            speechSum += presence;
            if (needSpeech)
            {
                _speechBuffer[i] = presence;
            }

            float total = MathF.Max(env, 1e-6f);

            if (needFricative)
            {
                float high = _fricativeHighPass.Process(filtered);
                float highEnv = _fricativeEnv.Process(high);
                float fricative = Math.Clamp(highEnv / total, 0f, 1f);
                _fricativeBuffer[i] = fricative;
                fricativeSum += fricative;
            }

            if (needSibilance)
            {
                float sib = _sibilanceBand.Process(filtered);
                float sibEnv = _sibilanceEnv.Process(sib);
                float sibilance = Math.Clamp(sibEnv / total, 0f, 1f);
                _sibilanceBuffer[i] = sibilance;
                sibilanceSum += sibilance;
            }
        }

        if (needSpeech)
        {
            WriteLastValue(AnalysisSignalId.SpeechPresence, speechSum / Math.Max(1, count));
        }
        if (needFricative)
        {
            WriteLastValue(AnalysisSignalId.FricativeActivity, fricativeSum / Math.Max(1, count));
        }
        if (needSibilance)
        {
            WriteLastValue(AnalysisSignalId.SibilanceEnergy, sibilanceSum / Math.Max(1, count));
        }

        bool needsFrame = NeedsFrameSignals(requestedSignals);
        if (needsFrame)
        {
            EnsureFftMagnitudes();
            ProcessFrameCore(_analysisBufferRaw, _analysisBufferProcessed, _fftMagnitudes, requestedSignals);
            FillFrameBuffers(count, requestedSignals);
        }

        WriteSignals(writer, requestedSignals, sampleTime, count);
    }

    public void ProcessStreaming(ReadOnlySpan<float> hop, AnalysisSignalMask requestedSignals)
    {
        int count = hop.Length;
        if (count == 0)
        {
            return;
        }

        bool needSpeech = (requestedSignals & AnalysisSignalMask.SpeechPresence) != 0;
        bool needFricative = (requestedSignals & AnalysisSignalMask.FricativeActivity) != 0;
        bool needSibilance = (requestedSignals & AnalysisSignalMask.SibilanceEnergy) != 0;
        bool needStreaming = needSpeech || needFricative || needSibilance;
        if (!needStreaming)
        {
            return;
        }

        float speechSum = 0f;
        float fricativeSum = 0f;
        float sibilanceSum = 0f;

        for (int i = 0; i < count; i++)
        {
            float input = hop[i];
            float dcRemoved = _dcHighPass.Process(input);
            float filtered = _settings.HighPassEnabled
                ? _rumbleHighPass.Process(dcRemoved)
                : dcRemoved;

            float env = _speechEnv.Process(filtered);
            float envDb = DspUtils.LinearToDb(env);
            float presence = Math.Clamp((envDb - SpeechThresholdDb) / SpeechRangeDb, 0f, 1f);
            speechSum += presence;

            float total = MathF.Max(env, 1e-6f);

            if (needFricative)
            {
                float high = _fricativeHighPass.Process(filtered);
                float highEnv = _fricativeEnv.Process(high);
                fricativeSum += Math.Clamp(highEnv / total, 0f, 1f);
            }

            if (needSibilance)
            {
                float sib = _sibilanceBand.Process(filtered);
                float sibEnv = _sibilanceEnv.Process(sib);
                sibilanceSum += Math.Clamp(sibEnv / total, 0f, 1f);
            }
        }

        if (needSpeech)
        {
            WriteLastValue(AnalysisSignalId.SpeechPresence, speechSum / Math.Max(1, count));
        }
        if (needFricative)
        {
            WriteLastValue(AnalysisSignalId.FricativeActivity, fricativeSum / Math.Max(1, count));
        }
        if (needSibilance)
        {
            WriteLastValue(AnalysisSignalId.SibilanceEnergy, sibilanceSum / Math.Max(1, count));
        }
    }

    public void ProcessFrame(ReadOnlySpan<float> rawFrame, ReadOnlySpan<float> processedFrame, ReadOnlySpan<float> magnitudes, AnalysisSignalMask requestedSignals)
    {
        if (rawFrame.IsEmpty || magnitudes.IsEmpty)
        {
            return;
        }

        ProcessFrameCore(rawFrame, processedFrame, magnitudes, requestedSignals);
    }

    private void ProcessFrameCore(ReadOnlySpan<float> rawFrame, ReadOnlySpan<float> processedFrame,
        ReadOnlySpan<float> magnitudes, AnalysisSignalMask requestedSignals)
    {
        bool needsPitch = (requestedSignals & (AnalysisSignalMask.PitchHz | AnalysisSignalMask.PitchConfidence)) != 0;
        bool needsVoicing = (requestedSignals & (AnalysisSignalMask.VoicingScore | AnalysisSignalMask.VoicingState)) != 0;
        bool needsFormants = (requestedSignals & (AnalysisSignalMask.FormantF1Hz | AnalysisSignalMask.FormantF2Hz | AnalysisSignalMask.FormantF3Hz | AnalysisSignalMask.FormantConfidence)) != 0;
        bool needsSpectralFlux = (requestedSignals & AnalysisSignalMask.SpectralFlux) != 0;
        bool needsOnsetFlux = (requestedSignals & AnalysisSignalMask.OnsetFluxHigh) != 0;
        bool needsHnr = (requestedSignals & AnalysisSignalMask.HnrDb) != 0;

        if (needsVoicing)
        {
            needsPitch = true;
        }

        if (needsFormants)
        {
            needsPitch = true;
            needsVoicing = true;
        }

        float pitch = 0f;
        float pitchConfidence = 0f;

        if (needsPitch)
        {
            DetectPitch(_settings.PitchDetector, rawFrame, magnitudes, ref pitch, ref pitchConfidence);
            WriteLastValue(AnalysisSignalId.PitchHz, pitch);
            WriteLastValue(AnalysisSignalId.PitchConfidence, pitchConfidence);
        }

        VoicingState voicing = VoicingState.Silence;
        float voicingScore = 0f;

        if (needsVoicing)
        {
            voicing = _voicingDetector.Detect(rawFrame, magnitudes, pitchConfidence);
            voicingScore = voicing == VoicingState.Voiced ? pitchConfidence : 0f;
            WriteLastValue(AnalysisSignalId.VoicingState, (float)voicing);
            WriteLastValue(AnalysisSignalId.VoicingScore, voicingScore);
        }

        if (needsFormants)
        {
            float f1 = 0f;
            float f2 = 0f;
            float f3 = 0f;
            float confidence = 0f;
            float bw1 = 0f;
            float bw2 = 0f;
            float bw3 = 0f;

            bool vowelLike = false;
            if (voicing == VoicingState.Voiced)
            {
                float vowelMinHz = MathF.Max(VowelEnergyMinHz, _formantPreset.F1MinHz);
                float vowelMaxHz = MathF.Min(VowelEnergyMaxHz, _formantPreset.F1MaxHz);
                if (vowelMaxHz <= vowelMinHz)
                {
                    vowelMinHz = VowelEnergyMinHz;
                    vowelMaxHz = VowelEnergyMaxHz;
                }

                float ratio = DspUtils.ComputeBandEnergyRatio(magnitudes, _binResolution, vowelMinHz, vowelMaxHz);
                vowelLike = ratio >= VowelEnergyRatioThreshold;
            }

            if (voicing == VoicingState.Voiced && vowelLike && TryExtractFormants(rawFrame, out int count))
            {
                if (count > 0)
                {
                    f1 = _formantFreqScratch[0];
                    if (count > 1)
                    {
                        f2 = _formantFreqScratch[1];
                    }
                    if (count > 2)
                    {
                        f3 = _formantFreqScratch[2];
                    }
                    bw1 = _formantBwScratch[0];
                    bw2 = count > 1 ? _formantBwScratch[1] : 0f;
                    bw3 = count > 2 ? _formantBwScratch[2] : 0f;
                    // Confidence derives from LPC + beam tracking cost, not pitch.
                    confidence = _beamFormantTracker?.LastConfidence ?? 0f;
                }
            }
            else
            {
                _beamFormantTracker?.MarkNoUpdate();
            }

            WriteLastValue(AnalysisSignalId.FormantF1Hz, f1);
            WriteLastValue(AnalysisSignalId.FormantF2Hz, f2);
            WriteLastValue(AnalysisSignalId.FormantF3Hz, f3);
            WriteLastValue(AnalysisSignalId.FormantConfidence, Math.Clamp(confidence, 0f, 1f));
            Volatile.Write(ref _lastFormantBandwidth1, bw1);
            Volatile.Write(ref _lastFormantBandwidth2, bw2);
            Volatile.Write(ref _lastFormantBandwidth3, bw3);
        }

        if (needsSpectralFlux || needsOnsetFlux)
        {
            if (needsSpectralFlux)
            {
                _featureExtractor.Compute(magnitudes, Math.Min(_analysisBins, magnitudes.Length),
                    out float centroid, out float slope, out float flux);
                WriteLastValue(AnalysisSignalId.SpectralFlux, flux);
                Volatile.Write(ref _lastCentroid, centroid);
                Volatile.Write(ref _lastSlope, slope);
            }

            if (needsOnsetFlux)
            {
                float fluxHigh = ComputeHighBandFlux(magnitudes);
                WriteLastValue(AnalysisSignalId.OnsetFluxHigh, fluxHigh);
            }
        }

        if (needsHnr)
        {
            float flatness = ComputeSpectralFlatness(magnitudes);
            // HNR approximation: -10 * log10(flatness), flatness in [0,1].
            float hnr = -10f * MathF.Log10(MathF.Max(flatness, 1e-6f));
            WriteLastValue(AnalysisSignalId.HnrDb, hnr);
        }
    }

    private void WriteSignals(in AnalysisSignalWriter writer, AnalysisSignalMask requestedSignals, long sampleTime, int count)
    {
        if (!writer.IsEnabled)
        {
            return;
        }

        if ((requestedSignals & AnalysisSignalMask.SpeechPresence) != 0)
        {
            writer.WriteBlock(AnalysisSignalId.SpeechPresence, sampleTime, _speechBuffer.AsSpan(0, count));
        }
        if ((requestedSignals & AnalysisSignalMask.VoicingScore) != 0)
        {
            writer.WriteBlock(AnalysisSignalId.VoicingScore, sampleTime, _voicingScoreBuffer.AsSpan(0, count));
        }
        if ((requestedSignals & AnalysisSignalMask.VoicingState) != 0)
        {
            writer.WriteBlock(AnalysisSignalId.VoicingState, sampleTime, _voicingStateBuffer.AsSpan(0, count));
        }
        if ((requestedSignals & AnalysisSignalMask.FricativeActivity) != 0)
        {
            writer.WriteBlock(AnalysisSignalId.FricativeActivity, sampleTime, _fricativeBuffer.AsSpan(0, count));
        }
        if ((requestedSignals & AnalysisSignalMask.SibilanceEnergy) != 0)
        {
            writer.WriteBlock(AnalysisSignalId.SibilanceEnergy, sampleTime, _sibilanceBuffer.AsSpan(0, count));
        }
        if ((requestedSignals & AnalysisSignalMask.OnsetFluxHigh) != 0)
        {
            writer.WriteBlock(AnalysisSignalId.OnsetFluxHigh, sampleTime, _onsetFluxBuffer.AsSpan(0, count));
        }
        if ((requestedSignals & AnalysisSignalMask.PitchHz) != 0)
        {
            writer.WriteBlock(AnalysisSignalId.PitchHz, sampleTime, _pitchBuffer.AsSpan(0, count));
        }
        if ((requestedSignals & AnalysisSignalMask.PitchConfidence) != 0)
        {
            writer.WriteBlock(AnalysisSignalId.PitchConfidence, sampleTime, _pitchConfidenceBuffer.AsSpan(0, count));
        }
        if ((requestedSignals & AnalysisSignalMask.FormantF1Hz) != 0)
        {
            writer.WriteBlock(AnalysisSignalId.FormantF1Hz, sampleTime, _formantF1Buffer.AsSpan(0, count));
        }
        if ((requestedSignals & AnalysisSignalMask.FormantF2Hz) != 0)
        {
            writer.WriteBlock(AnalysisSignalId.FormantF2Hz, sampleTime, _formantF2Buffer.AsSpan(0, count));
        }
        if ((requestedSignals & AnalysisSignalMask.FormantF3Hz) != 0)
        {
            writer.WriteBlock(AnalysisSignalId.FormantF3Hz, sampleTime, _formantF3Buffer.AsSpan(0, count));
        }
        if ((requestedSignals & AnalysisSignalMask.FormantConfidence) != 0)
        {
            writer.WriteBlock(AnalysisSignalId.FormantConfidence, sampleTime, _formantConfidenceBuffer.AsSpan(0, count));
        }
        if ((requestedSignals & AnalysisSignalMask.SpectralFlux) != 0)
        {
            writer.WriteBlock(AnalysisSignalId.SpectralFlux, sampleTime, _spectralFluxBuffer.AsSpan(0, count));
        }
        if ((requestedSignals & AnalysisSignalMask.HnrDb) != 0)
        {
            writer.WriteBlock(AnalysisSignalId.HnrDb, sampleTime, _hnrDbBuffer.AsSpan(0, count));
        }
    }

    private void FillFrameBuffers(int count, AnalysisSignalMask requestedSignals)
    {
        if ((requestedSignals & AnalysisSignalMask.VoicingScore) != 0)
        {
            _voicingScoreBuffer.AsSpan(0, count).Fill(GetLastValue(AnalysisSignalId.VoicingScore));
        }
        if ((requestedSignals & AnalysisSignalMask.VoicingState) != 0)
        {
            _voicingStateBuffer.AsSpan(0, count).Fill(GetLastValue(AnalysisSignalId.VoicingState));
        }
        if ((requestedSignals & AnalysisSignalMask.OnsetFluxHigh) != 0)
        {
            _onsetFluxBuffer.AsSpan(0, count).Fill(GetLastValue(AnalysisSignalId.OnsetFluxHigh));
        }
        if ((requestedSignals & AnalysisSignalMask.PitchHz) != 0)
        {
            _pitchBuffer.AsSpan(0, count).Fill(GetLastValue(AnalysisSignalId.PitchHz));
        }
        if ((requestedSignals & AnalysisSignalMask.PitchConfidence) != 0)
        {
            _pitchConfidenceBuffer.AsSpan(0, count).Fill(GetLastValue(AnalysisSignalId.PitchConfidence));
        }
        if ((requestedSignals & AnalysisSignalMask.FormantF1Hz) != 0)
        {
            _formantF1Buffer.AsSpan(0, count).Fill(GetLastValue(AnalysisSignalId.FormantF1Hz));
        }
        if ((requestedSignals & AnalysisSignalMask.FormantF2Hz) != 0)
        {
            _formantF2Buffer.AsSpan(0, count).Fill(GetLastValue(AnalysisSignalId.FormantF2Hz));
        }
        if ((requestedSignals & AnalysisSignalMask.FormantF3Hz) != 0)
        {
            _formantF3Buffer.AsSpan(0, count).Fill(GetLastValue(AnalysisSignalId.FormantF3Hz));
        }
        if ((requestedSignals & AnalysisSignalMask.FormantConfidence) != 0)
        {
            _formantConfidenceBuffer.AsSpan(0, count).Fill(GetLastValue(AnalysisSignalId.FormantConfidence));
        }
        if ((requestedSignals & AnalysisSignalMask.SpectralFlux) != 0)
        {
            _spectralFluxBuffer.AsSpan(0, count).Fill(GetLastValue(AnalysisSignalId.SpectralFlux));
        }
        if ((requestedSignals & AnalysisSignalMask.HnrDb) != 0)
        {
            _hnrDbBuffer.AsSpan(0, count).Fill(GetLastValue(AnalysisSignalId.HnrDb));
        }
    }

    private void EnsureFftMagnitudes()
    {
        int size = _analysisSize;
        if (_fftReal.Length != size || _fftImag.Length != size)
        {
            return;
        }

        for (int i = 0; i < size; i++)
        {
            _fftReal[i] = _analysisBufferProcessed[i] * _analysisWindow[i];
            _fftImag[i] = 0f;
        }

        _fft?.Forward(_fftReal, _fftImag);

        int bins = _analysisBins;
        for (int i = 0; i < bins; i++)
        {
            float re = _fftReal[i];
            float im = _fftImag[i];
            _fftMagnitudes[i] = MathF.Sqrt(re * re + im * im);
        }
    }

    private float ComputeHighBandFlux(ReadOnlySpan<float> magnitudes)
    {
        int bins = Math.Min(magnitudes.Length, _highFluxPrevious.Length);
        if (bins <= 0)
        {
            return 0f;
        }

        int start = (int)MathF.Floor(2000f / MathF.Max(_binResolution, 1e-6f));
        start = Math.Clamp(start, 0, bins - 1);

        double sum = 0.0;
        int count = 0;
        for (int i = start; i < bins; i++)
        {
            float diff = magnitudes[i] - _highFluxPrevious[i];
            sum += diff * diff;
            _highFluxPrevious[i] = magnitudes[i];
            count++;
        }

        if (count <= 0)
        {
            return 0f;
        }

        return (float)(sum / count);
    }

    private void DetectPitch(PitchDetectorType algorithm, ReadOnlySpan<float> rawFrame, ReadOnlySpan<float> magnitudes,
        ref float pitch, ref float confidence)
    {
        Volatile.Write(ref _lastCpp, 0f);
        switch (algorithm)
        {
            case PitchDetectorType.Yin when _yinPitchDetector is not null:
                var yin = _yinPitchDetector.Detect(rawFrame);
                pitch = yin.FrequencyHz ?? 0f;
                confidence = yin.Confidence;
                break;

            case PitchDetectorType.Pyin when _pyinPitchDetector is not null:
                var pyin = _pyinPitchDetector.Detect(rawFrame);
                pitch = pyin.FrequencyHz ?? 0f;
                confidence = pyin.Confidence;
                break;

            case PitchDetectorType.Autocorrelation when _autocorrPitchDetector is not null:
                var auto = _autocorrPitchDetector.Detect(rawFrame);
                pitch = auto.FrequencyHz ?? 0f;
                confidence = auto.Confidence;
                break;

            case PitchDetectorType.Cepstral when _cepstralPitchDetector is not null:
                var cep = _cepstralPitchDetector.Detect(rawFrame);
                pitch = cep.FrequencyHz ?? 0f;
                confidence = cep.Confidence;
                Volatile.Write(ref _lastCpp, _cepstralPitchDetector.LastCpp);
                break;

            case PitchDetectorType.Swipe when _swipePitchDetector is not null:
                var swipe = _swipePitchDetector.Detect(magnitudes);
                pitch = swipe.FrequencyHz ?? 0f;
                confidence = swipe.Confidence;
                break;
        }
    }

    private bool TryExtractFormants(ReadOnlySpan<float> rawFrame, out int count)
    {
        count = 0;
        if (_lpcAnalyzer is null || _beamFormantTracker is null)
        {
            return false;
        }

        int bufferLen = rawFrame.Length;
        int lpcLen = Math.Min(_lpcWindowSamples, bufferLen);
        int lpcStart = Math.Max(0, bufferLen - lpcLen);

        EnsureBuffer(ref _lpcInputBuffer, lpcLen);
        rawFrame.Slice(lpcStart, lpcLen).CopyTo(_lpcInputBuffer.AsSpan(0, lpcLen));

        int decimatedLen = lpcLen;
        if (_lpcDecimationStages >= 1)
        {
            _lpcDecimator1.Reset();
            int decimated1Len = lpcLen / 2;
            EnsureBuffer(ref _lpcDecimateBuffer1, decimated1Len);
            _lpcDecimator1.ProcessDownsample(_lpcInputBuffer.AsSpan(0, lpcLen),
                _lpcDecimateBuffer1.AsSpan(0, decimated1Len));

            if (_lpcDecimationStages == 2)
            {
                _lpcDecimator2.Reset();
                decimatedLen = decimated1Len / 2;
                EnsureBuffer(ref _lpcDecimatedBuffer, decimatedLen);
                _lpcDecimator2.ProcessDownsample(_lpcDecimateBuffer1.AsSpan(0, decimated1Len),
                    _lpcDecimatedBuffer.AsSpan(0, decimatedLen));
            }
            else
            {
                decimatedLen = decimated1Len;
                EnsureBuffer(ref _lpcDecimatedBuffer, decimatedLen);
                _lpcDecimateBuffer1.AsSpan(0, decimatedLen).CopyTo(_lpcDecimatedBuffer.AsSpan(0, decimatedLen));
            }
        }
        else
        {
            EnsureBuffer(ref _lpcDecimatedBuffer, lpcLen);
            _lpcInputBuffer.AsSpan(0, lpcLen).CopyTo(_lpcDecimatedBuffer.AsSpan(0, lpcLen));
        }

        float preEmphasisAlpha = ComputePreEmphasisAlpha(FormantProfileInfo.DefaultPreEmphasisHz, _lpcSampleRate);
        _lpcPreEmphasisFilter.Configure(preEmphasisAlpha);
        _lpcPreEmphasisFilter.Reset();

        EnsureBuffer(ref _lpcWindow, decimatedLen);
        EnsureBuffer(ref _lpcWindowedBuffer, decimatedLen);
        if (_lpcWindowLength != decimatedLen)
        {
            WindowFunctions.FillGaussian(_lpcWindow.AsSpan(0, decimatedLen), LpcGaussianSigma);
            _lpcWindowLength = decimatedLen;
        }

        for (int i = 0; i < decimatedLen; i++)
        {
            float emphasized = _lpcPreEmphasisFilter.Process(_lpcDecimatedBuffer[i]);
            _lpcWindowedBuffer[i] = emphasized * _lpcWindow[i];
        }

        int order = _formantPreset.LpcOrder;
        EnsureBuffer(ref _lpcCoefficients, order + 1);

        if (_lpcAnalyzer.Compute(_lpcWindowedBuffer.AsSpan(0, decimatedLen), _lpcCoefficients))
        {
            count = _beamFormantTracker.Track(_lpcCoefficients, _lpcSampleRate,
                _formantFreqScratch, _formantBwScratch,
                50f, _formantCeilingHz, 3);
            return count > 0;
        }

        _beamFormantTracker.MarkNoUpdate();
        return false;
    }

    private void ConfigureFilters(AnalysisSignalProcessorSettings settings)
    {
        _dcHighPass.Configure(DcCutoffHz, _sampleRate);
        _dcHighPass.Reset();
        _rumbleHighPass.SetHighPass(_sampleRate, settings.HighPassCutoff, 0.707f);
        _rumbleHighPass.Reset();
        _preEmphasisFilter.Configure(DefaultPreEmphasis);
        _preEmphasisFilter.Reset();

        _speechEnv.Configure(5f, 80f, _sampleRate);
        _fricativeEnv.Configure(2f, 60f, _sampleRate);
        _sibilanceEnv.Configure(2f, 60f, _sampleRate);

        _fricativeHighPass.SetHighPass(_sampleRate, FricativeHighHz, 0.707f);
        _fricativeHighPass.Reset();
        _sibilanceBand.SetBandPass(_sampleRate, SibilanceCenterHz, SibilanceQ);
        _sibilanceBand.Reset();
    }

    private void ConfigurePitchDetectors(float minFrequency, float maxFrequency)
    {
        float maxPitch = MathF.Min(_sampleRate * 0.45f, MathF.Max(minFrequency + 1f, maxFrequency));
        float minPitch = MathF.Max(20f, MathF.Min(minFrequency, maxPitch - 1f));

        _yinPitchDetector ??= new YinPitchDetector(_sampleRate, _analysisSize, minPitch, maxPitch, 0.15f);
        _yinPitchDetector.Configure(_sampleRate, _analysisSize, minPitch, maxPitch, 0.15f);

        _pyinPitchDetector ??= new PyinPitchDetector(_sampleRate, _analysisSize, minPitch, maxPitch, 0.15f);
        _pyinPitchDetector.Configure(_sampleRate, _analysisSize, minPitch, maxPitch, 0.15f);

        _autocorrPitchDetector ??= new AutocorrelationPitchDetector(_sampleRate, _analysisSize, minPitch, maxPitch, 0.3f);
        _autocorrPitchDetector.Configure(_sampleRate, _analysisSize, minPitch, maxPitch, 0.3f);

        _cepstralPitchDetector ??= new CepstralPitchDetector(_sampleRate, _analysisSize, minPitch, maxPitch, 2f);
        _cepstralPitchDetector.Configure(_sampleRate, _analysisSize, minPitch, maxPitch, 2f);

        _swipePitchDetector ??= new SwipePitchDetector(_sampleRate, _analysisSize, minFrequency, maxFrequency);
        _swipePitchDetector.Configure(_sampleRate, _analysisSize, minFrequency, maxFrequency);
    }

    private void ConfigureFormantTracking(FormantProfile profile)
    {
        _formantPreset = FormantProfileInfo.GetTrackingPreset(profile);
        _formantCeilingHz = _formantPreset.FormantCeilingHz;

        _lpcDecimationStages = GetLpcDecimationStages(_sampleRate, _formantCeilingHz);
        float currentRate = _sampleRate;
        for (int i = 0; i < _lpcDecimationStages; i++)
        {
            currentRate *= 0.5f;
        }

        _lpcSampleRate = Math.Max(1, (int)MathF.Round(currentRate));
        _lpcWindowSamples = ComputeLpcWindowSamples(_lpcSampleRate, _formantPreset.LpcOrder, _analysisSize);

        _lpcAnalyzer ??= new LpcAnalyzer(_formantPreset.LpcOrder);
        _lpcAnalyzer.Configure(_formantPreset.LpcOrder);

        float frameSeconds = _hopSize / (float)Math.Max(1, _sampleRate);
        _beamFormantTracker ??= new BeamSearchFormantTracker(_formantPreset.LpcOrder, _formantPreset, frameSeconds, beamWidth: 5);
        _beamFormantTracker.Configure(_formantPreset.LpcOrder, _formantPreset, frameSeconds, beamWidth: 5);
    }

    private static void EnsureBuffer(ref float[] buffer, int length)
    {
        if (buffer.Length != length)
        {
            buffer = new float[length];
        }
    }

    private void EnsureSignalBuffers(int length)
    {
        EnsureBuffer(ref _speechBuffer, length);
        EnsureBuffer(ref _voicingScoreBuffer, length);
        EnsureBuffer(ref _voicingStateBuffer, length);
        EnsureBuffer(ref _fricativeBuffer, length);
        EnsureBuffer(ref _sibilanceBuffer, length);
        EnsureBuffer(ref _onsetFluxBuffer, length);
        EnsureBuffer(ref _pitchBuffer, length);
        EnsureBuffer(ref _pitchConfidenceBuffer, length);
        EnsureBuffer(ref _formantF1Buffer, length);
        EnsureBuffer(ref _formantF2Buffer, length);
        EnsureBuffer(ref _formantF3Buffer, length);
        EnsureBuffer(ref _formantConfidenceBuffer, length);
        EnsureBuffer(ref _spectralFluxBuffer, length);
        EnsureBuffer(ref _hnrDbBuffer, length);
    }

    private void WriteLastValue(AnalysisSignalId signal, float value)
    {
        int index = (int)signal;
        if ((uint)index >= (uint)_lastValues.Length)
        {
            return;
        }

        value = SanitizeSignalValue(signal, value);
        Volatile.Write(ref _lastValues[index], value);
    }

    private static bool NeedsFrameSignals(AnalysisSignalMask requestedSignals)
    {
        const AnalysisSignalMask frameSignals =
            AnalysisSignalMask.VoicingScore |
            AnalysisSignalMask.VoicingState |
            AnalysisSignalMask.OnsetFluxHigh |
            AnalysisSignalMask.PitchHz |
            AnalysisSignalMask.PitchConfidence |
            AnalysisSignalMask.FormantF1Hz |
            AnalysisSignalMask.FormantF2Hz |
            AnalysisSignalMask.FormantF3Hz |
            AnalysisSignalMask.FormantConfidence |
            AnalysisSignalMask.SpectralFlux |
            AnalysisSignalMask.HnrDb;

        return (requestedSignals & frameSignals) != 0;
    }

    private static float SanitizeSignalValue(AnalysisSignalId signal, float value)
    {
        if (!float.IsFinite(value))
        {
            return 0f;
        }

        return signal switch
        {
            AnalysisSignalId.SpeechPresence => Clamp01(value),
            AnalysisSignalId.VoicingScore => Clamp01(value),
            AnalysisSignalId.VoicingState => Math.Clamp(value, 0f, 2f),
            AnalysisSignalId.FricativeActivity => Clamp01(value),
            AnalysisSignalId.SibilanceEnergy => Clamp01(value),
            AnalysisSignalId.OnsetFluxHigh => MathF.Max(0f, value),
            AnalysisSignalId.PitchHz => MathF.Max(0f, value),
            AnalysisSignalId.PitchConfidence => Clamp01(value),
            AnalysisSignalId.FormantF1Hz => MathF.Max(0f, value),
            AnalysisSignalId.FormantF2Hz => MathF.Max(0f, value),
            AnalysisSignalId.FormantF3Hz => MathF.Max(0f, value),
            AnalysisSignalId.FormantConfidence => Clamp01(value),
            AnalysisSignalId.SpectralFlux => MathF.Max(0f, value),
            AnalysisSignalId.HnrDb => Math.Clamp(value, -120f, 120f),
            _ => value
        };
    }

    private static float Clamp01(float value)
    {
        if (value <= 0f)
        {
            return 0f;
        }

        if (value >= 1f)
        {
            return 1f;
        }

        return value;
    }

    private static int NextPowerOfTwo(int value)
    {
        int power = 1;
        while (power < value)
        {
            power <<= 1;
        }
        return power;
    }

    private static int ComputeLpcWindowSamples(int sampleRate, int lpcOrder, int maxWindowSamples)
    {
        int desired = (int)MathF.Round(LpcWindowSeconds * sampleRate);
        int maxWindow = Math.Max(1, maxWindowSamples);
        int minWindow = Math.Min(maxWindow, Math.Max(lpcOrder + 1, 128));
        return Math.Clamp(desired, minWindow, maxWindow);
    }

    private static int GetLpcDecimationStages(int sampleRate, float formantCeilingHz)
    {
        if (sampleRate <= 0 || formantCeilingHz <= 0f)
        {
            return 0;
        }

        float requiredRate = formantCeilingHz * 2f;
        int stages = 0;
        float currentRate = sampleRate;
        while (stages < 2 && currentRate / 2f >= requiredRate)
        {
            currentRate /= 2f;
            stages++;
        }
        return stages;
    }

    private static float ComputePreEmphasisAlpha(float cutoffHz, int sampleRate)
    {
        if (cutoffHz <= 0f || sampleRate <= 0)
        {
            return 0f;
        }

        return MathF.Exp(-2f * MathF.PI * cutoffHz / sampleRate);
    }

    private static float ComputeSpectralFlatness(ReadOnlySpan<float> magnitudes)
    {
        if (magnitudes.IsEmpty)
        {
            return 1f;
        }

        float logSum = 0f;
        float linSum = 0f;
        int count = magnitudes.Length;
        for (int i = 0; i < count; i++)
        {
            float mag = MathF.Max(magnitudes[i], 1e-12f);
            logSum += MathF.Log(mag);
            linSum += mag;
        }

        float geometric = MathF.Exp(logSum / count);
        float arithmetic = linSum / count;
        return arithmetic > 1e-12f ? geometric / arithmetic : 1f;
    }
}
