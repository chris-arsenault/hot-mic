using System.Numerics;

namespace HotMic.Core.Dsp.Analysis.Formants;

/// <summary>
/// Beam-search formant tracker for F1/F2 using LPC roots and continuity constraints.
/// </summary>
public sealed class BeamSearchFormantTracker
{
    private const int TrackedFormants = 2;
    private const int MaxCandidatesPerFormant = 5;
    private const int MaxFrameGap = 8;

    // Candidate filters
    private const float MaxPoleMagnitude = 0.9995f;
    private const float MinBandwidthHz = 20f;
    private const float MaxBandwidthHz = 800f;
    private const float MinSeparationHz = 150f;

    // Cost weights (lower is better)
    private const float ContinuityWeight = 4.0f;
    private const float QualityWeight = 1.5f;
    private const float SeparationWeight = 1.0f;
    private const float OverDeltaPenalty = 6.0f;
    private const float SwapPenalty = 8.0f;

    private int _order;
    private int _beamWidth;

    private FormantTrackingPreset _preset;
    private float _frameSeconds;
    private float _smoothingCoeff;

    private Complex[] _roots = Array.Empty<Complex>();
    private double[] _polyCoefficients = Array.Empty<double>();
    private Complex[] _newRoots = Array.Empty<Complex>();

    private FormantCandidate[] _f1Candidates = Array.Empty<FormantCandidate>();
    private FormantCandidate[] _f2Candidates = Array.Empty<FormantCandidate>();
    private int _f1Count;
    private int _f2Count;

    private BeamState[] _beam = Array.Empty<BeamState>();
    private BeamState[] _newBeam = Array.Empty<BeamState>();
    private int _beamCount;
    private bool _hasPrevious;
    private int _framesSinceUpdate;
    private float _lastConfidence;

    private static readonly Comparison<BeamState> BeamStateComparison =
        (a, b) => a.Cost.CompareTo(b.Cost);

    public BeamSearchFormantTracker(int order = 10,
        FormantTrackingPreset? preset = null,
        float frameSeconds = 0.01f,
        int beamWidth = 5)
    {
        Configure(order, preset ?? FormantProfileInfo.GetTrackingPreset(FormantProfile.Tenor), frameSeconds, beamWidth);
    }

    public int Order => _order;

    public float LastConfidence => _lastConfidence;

    public void Configure(int order, FormantTrackingPreset preset, float frameSeconds = 0.01f, int beamWidth = 5)
    {
        _order = Math.Clamp(order, 4, 32);
        _beamWidth = Math.Clamp(beamWidth, 1, 16);
        _preset = preset;
        _frameSeconds = MathF.Max(frameSeconds, 1e-4f);
        _smoothingCoeff = ComputeSmoothingCoeff(_frameSeconds, _preset.SmoothingTauMs);

        int size = _order + 1;
        if (_polyCoefficients.Length != size)
        {
            _polyCoefficients = new double[size];
            _roots = new Complex[_order];
            _newRoots = new Complex[_order];
        }

        if (_f1Candidates.Length != MaxCandidatesPerFormant)
        {
            _f1Candidates = new FormantCandidate[MaxCandidatesPerFormant];
            _f2Candidates = new FormantCandidate[MaxCandidatesPerFormant];
        }

        if (_beam.Length != _beamWidth)
        {
            _beam = new BeamState[_beamWidth];
        }

        int maxPairs = Math.Max(1, _beamWidth * MaxCandidatesPerFormant * MaxCandidatesPerFormant);
        if (_newBeam.Length != maxPairs)
        {
            _newBeam = new BeamState[maxPairs];
        }

        Reset();
    }

    public void UpdateFrameSeconds(float frameSeconds)
    {
        _frameSeconds = MathF.Max(frameSeconds, 1e-4f);
        _smoothingCoeff = ComputeSmoothingCoeff(_frameSeconds, _preset.SmoothingTauMs);
    }

    public void UpdatePreset(FormantTrackingPreset preset)
    {
        _preset = preset;
        _smoothingCoeff = ComputeSmoothingCoeff(_frameSeconds, _preset.SmoothingTauMs);
    }

    /// <summary>
    /// Reset tracking state (call when audio restarts or after a long gap).
    /// </summary>
    public void Reset()
    {
        _beamCount = 0;
        _hasPrevious = false;
        _framesSinceUpdate = 0;
    }

    /// <summary>
    /// Advance the tracker without a voiced update (holds last state).
    /// </summary>
    public void MarkNoUpdate()
    {
        if (_hasPrevious)
        {
            _framesSinceUpdate = Math.Min(_framesSinceUpdate + 1, MaxFrameGap);
        }
    }

    /// <summary>
    /// Track F1/F2 from LPC coefficients. Returns count of formants written (0 or 2).
    /// </summary>
    public int Track(ReadOnlySpan<float> lpcCoefficients, int sampleRate,
        Span<float> formantFrequencies, Span<float> formantBandwidths,
        float minFormantHz, float maxFormantHz, int maxOutput)
    {
        _lastConfidence = 0f;
        if (lpcCoefficients.Length < _order + 1 || formantFrequencies.IsEmpty)
            return 0;

        maxOutput = Math.Min(maxOutput, Math.Min(formantFrequencies.Length,
            formantBandwidths.IsEmpty ? int.MaxValue : formantBandwidths.Length));
        if (maxOutput <= 0)
            return 0;

        for (int i = 0; i <= _order; i++)
        {
            float c = lpcCoefficients[i];
            if (float.IsNaN(c) || float.IsInfinity(c))
                return 0;
        }

        for (int i = 0; i <= _order; i++)
            _polyCoefficients[i] = lpcCoefficients[i];

        SolveRootsAberth();
        ExtractCandidates(sampleRate, minFormantHz, maxFormantHz);

        if (_f1Count == 0 || _f2Count == 0)
        {
            MarkNoUpdate();
            return 0;
        }

        int newBeamCount = BuildBeamStates();
        if (newBeamCount <= 0)
        {
            MarkNoUpdate();
            return 0;
        }

        Array.Sort(_newBeam, 0, newBeamCount, Comparer<BeamState>.Create(BeamStateComparison));
        _beamCount = Math.Min(newBeamCount, _beamWidth);
        Array.Copy(_newBeam, 0, _beam, 0, _beamCount);
        _hasPrevious = true;
        _framesSinceUpdate = 0;

        BeamState best = _beam[0];
        if (_beamCount > 1)
        {
            BeamState runnerUp = _beam[1];
            if (ShouldPreferRunnerUp(best, runnerUp))
                best = runnerUp;
        }

        _lastConfidence = Math.Clamp(CostToConfidence(best.Cost), 0f, 1f);

        int outputCount = Math.Min(TrackedFormants, maxOutput);
        if (outputCount > 0)
        {
            formantFrequencies[0] = best.SmoothedF1;
            if (outputCount > 1)
                formantFrequencies[1] = best.SmoothedF2;

            if (!formantBandwidths.IsEmpty)
            {
                formantBandwidths[0] = best.Bandwidth1;
                if (outputCount > 1)
                    formantBandwidths[1] = best.Bandwidth2;
            }
        }

        return outputCount;
    }

    private void ExtractCandidates(int sampleRate, float minFormantHz, float maxFormantHz)
    {
        _f1Count = 0;
        _f2Count = 0;

        float nyquist = sampleRate * 0.5f;
        float minHz = Math.Max(0f, minFormantHz);
        float maxHz = maxFormantHz > 0f ? Math.Min(maxFormantHz, nyquist * 0.9f) : nyquist * 0.9f;

        float f1Min = Math.Max(_preset.F1MinHz, minHz);
        float f1Max = Math.Min(_preset.F1MaxHz, maxHz);
        float f2Min = Math.Max(_preset.F2MinHz, minHz);
        float f2Max = Math.Min(_preset.F2MaxHz, maxHz);

        if (f1Max <= f1Min || f2Max <= f2Min)
            return;

        for (int i = 0; i < _roots.Length; i++)
        {
            Complex root = _roots[i];
            if (root.Imaginary <= 0.001)
                continue;

            double magnitude = root.Magnitude;
            if (magnitude <= 0.0 || magnitude >= MaxPoleMagnitude)
                continue;

            double angle = Math.Atan2(root.Imaginary, root.Real);
            float freq = (float)(angle * sampleRate / (2.0 * Math.PI));
            float bandwidth = (float)(-sampleRate / Math.PI * Math.Log(magnitude));

            if (freq < minHz || freq > maxHz)
                continue;

            if (bandwidth < MinBandwidthHz || bandwidth > MaxBandwidthHz)
                continue;

            bool inF1 = freq >= f1Min && freq <= f1Max;
            bool inF2 = freq >= f2Min && freq <= f2Max;
            if (!inF1 && !inF2)
                continue;

            var candidate = new FormantCandidate
            {
                Frequency = freq,
                Bandwidth = bandwidth,
                QualityCost = ComputeCandidateCost((float)magnitude, bandwidth)
            };

            if (inF1)
                InsertCandidate(ref _f1Count, _f1Candidates, candidate);

            if (inF2)
                InsertCandidate(ref _f2Count, _f2Candidates, candidate);
        }
    }

    private int BuildBeamStates()
    {
        int newBeamCount = 0;
        bool hasPrev = _hasPrevious && _beamCount > 0;

        for (int f1Index = 0; f1Index < _f1Count; f1Index++)
        {
            FormantCandidate f1 = _f1Candidates[f1Index];
            for (int f2Index = 0; f2Index < _f2Count; f2Index++)
            {
                FormantCandidate f2 = _f2Candidates[f2Index];
                float separation = f2.Frequency - f1.Frequency;
                if (separation < MinSeparationHz)
                    continue;

                if (!hasPrev)
                {
                    if (newBeamCount >= _newBeam.Length)
                        continue;

                    float localCost = ComputePairLocalCost(f1.QualityCost, f2.QualityCost, separation);
                    _newBeam[newBeamCount++] = new BeamState(
                        f1.Frequency, f2.Frequency,
                        f1.Bandwidth, f2.Bandwidth,
                        f1.Frequency, f2.Frequency,
                        localCost,
                        overMaxDelta: false,
                        swapPenalty: false);
                    continue;
                }

                for (int b = 0; b < _beamCount; b++)
                {
                    if (newBeamCount >= _newBeam.Length)
                        continue;

                    BeamState prev = _beam[b];
                    float cost = ComputeTransitionCost(prev, f1, f2, separation,
                        out float smoothedF1, out float smoothedF2,
                        out bool overMaxDelta, out bool swapPenalty);

                    _newBeam[newBeamCount++] = new BeamState(
                        f1.Frequency, f2.Frequency,
                        f1.Bandwidth, f2.Bandwidth,
                        smoothedF1, smoothedF2,
                        cost,
                        overMaxDelta,
                        swapPenalty);
                }
            }
        }

        return newBeamCount;
    }

    private float ComputePairLocalCost(float f1Quality, float f2Quality, float separation)
    {
        float separationCost = separation < MinSeparationHz
            ? (MinSeparationHz - separation) / MinSeparationHz
            : 0f;

        return (f1Quality + f2Quality) * QualityWeight + separationCost * SeparationWeight;
    }

    private float ComputeTransitionCost(BeamState prev, FormantCandidate f1, FormantCandidate f2, float separation,
        out float smoothedF1, out float smoothedF2, out bool overMaxDelta, out bool swapPenalty)
    {
        float cost = ComputePairLocalCost(f1.QualityCost, f2.QualityCost, separation);

        float gapScale = 1f + _framesSinceUpdate;
        float maxDeltaF1 = MathF.Max(1f, _preset.MaxDeltaF1Hz * gapScale);
        float maxDeltaF2 = MathF.Max(1f, _preset.MaxDeltaF2Hz * gapScale);

        float deltaF1 = MathF.Abs(f1.Frequency - prev.SmoothedF1);
        float deltaF2 = MathF.Abs(f2.Frequency - prev.SmoothedF2);

        overMaxDelta = deltaF1 > maxDeltaF1 || deltaF2 > maxDeltaF2;

        cost += (deltaF1 / maxDeltaF1 + deltaF2 / maxDeltaF2) * ContinuityWeight;

        if (overMaxDelta)
        {
            float overRatio = MathF.Max(deltaF1 / maxDeltaF1, deltaF2 / maxDeltaF2) - 1f;
            cost += MathF.Max(0f, overRatio) * OverDeltaPenalty;
        }

        swapPenalty = f1.Frequency > prev.SmoothedF1 && f2.Frequency < prev.SmoothedF2;
        if (swapPenalty)
            cost += SwapPenalty;

        smoothedF1 = Smooth(prev.SmoothedF1, f1.Frequency);
        smoothedF2 = Smooth(prev.SmoothedF2, f2.Frequency);

        return cost;
    }

    private static bool ShouldPreferRunnerUp(in BeamState best, in BeamState runnerUp)
    {
        bool bestPenalty = best.OverMaxDelta || best.SwapPenalty;
        bool runnerPenalty = runnerUp.OverMaxDelta || runnerUp.SwapPenalty;

        return bestPenalty && !runnerPenalty;
    }

    private float Smooth(float previous, float current)
    {
        return previous + (1f - _smoothingCoeff) * (current - previous);
    }

    private void SolveRootsAberth()
    {
        int n = _order;
        const int maxIterations = 200;
        const double epsilon = 1e-10;

        for (int i = 0; i < n; i++)
        {
            double radius = 0.7 + 0.25 * (i % 5) / 4.0;
            double angle = 2.0 * Math.PI * i / n + 0.1;
            _roots[i] = new Complex(radius * Math.Cos(angle), radius * Math.Sin(angle));
        }

        for (int iter = 0; iter < maxIterations; iter++)
        {
            double maxDelta = 0.0;

            for (int i = 0; i < n; i++)
            {
                Complex z = _roots[i];
                Complex p = _polyCoefficients[0];
                Complex dp = Complex.Zero;
                for (int k = 1; k <= n; k++)
                {
                    dp = dp * z + p;
                    p = p * z + _polyCoefficients[k];
                }

                Complex sum = Complex.Zero;
                for (int j = 0; j < n; j++)
                {
                    if (i != j)
                    {
                        Complex diff = z - _roots[j];
                        if (diff.Magnitude > 1e-14)
                            sum += 1.0 / diff;
                    }
                }

                Complex denom = dp - p * sum;
                Complex delta;
                if (denom.Magnitude > 1e-14)
                    delta = p / denom;
                else if (dp.Magnitude > 1e-14)
                    delta = p / dp;
                else
                    delta = Complex.Zero;

                double deltaMag = delta.Magnitude;
                if (deltaMag > 0.5)
                    delta *= 0.5 / deltaMag;

                _newRoots[i] = z - delta;
                maxDelta = Math.Max(maxDelta, delta.Magnitude);
            }

            for (int i = 0; i < n; i++)
                _roots[i] = _newRoots[i];

            if (maxDelta < epsilon)
                break;
        }
    }

    private static float ComputeCandidateCost(float magnitude, float bandwidth)
    {
        float magCost = 1f - magnitude;
        float bwNorm = (bandwidth - MinBandwidthHz) / (MaxBandwidthHz - MinBandwidthHz);
        float bwCost = Math.Clamp(bwNorm, 0f, 1f);
        return magCost + bwCost;
    }

    private static void InsertCandidate(ref int count, FormantCandidate[] list, FormantCandidate candidate)
    {
        int length = list.Length;
        if (count == 0)
        {
            list[0] = candidate;
            count = 1;
            return;
        }

        if (count < length)
        {
            int i = count - 1;
            while (i >= 0 && list[i].QualityCost > candidate.QualityCost)
            {
                list[i + 1] = list[i];
                i--;
            }
            list[i + 1] = candidate;
            count++;
            return;
        }

        if (candidate.QualityCost >= list[length - 1].QualityCost)
            return;

        int j = length - 2;
        while (j >= 0 && list[j].QualityCost > candidate.QualityCost)
        {
            list[j + 1] = list[j];
            j--;
        }
        list[j + 1] = candidate;
    }

    private static float ComputeSmoothingCoeff(float frameSeconds, float tauMs)
    {
        if (tauMs <= 0f)
            return 0f;

        float tauSeconds = MathF.Max(0.001f, tauMs * 0.001f);
        return MathF.Exp(-frameSeconds / tauSeconds);
    }

    private static float CostToConfidence(float cost)
    {
        return 1f / (1f + MathF.Max(0f, cost));
    }

    private struct FormantCandidate
    {
        public float Frequency;
        public float Bandwidth;
        public float QualityCost;
    }

    private readonly struct BeamState
    {
        public readonly float F1;
        public readonly float F2;
        public readonly float Bandwidth1;
        public readonly float Bandwidth2;
        public readonly float SmoothedF1;
        public readonly float SmoothedF2;
        public readonly float Cost;
        public readonly bool OverMaxDelta;
        public readonly bool SwapPenalty;

        public BeamState(float f1, float f2, float bandwidth1, float bandwidth2,
            float smoothedF1, float smoothedF2, float cost, bool overMaxDelta, bool swapPenalty)
        {
            F1 = f1;
            F2 = f2;
            Bandwidth1 = bandwidth1;
            Bandwidth2 = bandwidth2;
            SmoothedF1 = smoothedF1;
            SmoothedF2 = smoothedF2;
            Cost = cost;
            OverMaxDelta = overMaxDelta;
            SwapPenalty = swapPenalty;
        }
    }
}
