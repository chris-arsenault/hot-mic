using System.Numerics;

namespace HotMic.Core.Dsp.Analysis.Formants;

/// <summary>
/// Beam-search formant tracker that uses LPC roots as candidates and finds
/// optimal trajectories across frames using a cost function that considers
/// local evidence and continuity constraints.
/// </summary>
public sealed class BeamSearchFormantTracker
{
    // Configuration
    private int _order;
    private int _maxFormants;
    private int _beamWidth;

    // Root finding state (shared with simple tracker)
    private Complex[] _roots = Array.Empty<Complex>();
    private double[] _polyCoefficients = Array.Empty<double>();
    private Complex[] _newRoots = Array.Empty<Complex>();

    // Candidate storage
    private FormantCandidate[] _candidates = Array.Empty<FormantCandidate>();
    private int _candidateCount;

    // Beam search state
    private BeamState[] _beam = Array.Empty<BeamState>();
    private BeamState[] _newBeam = Array.Empty<BeamState>();
    private int _beamCount;

    // Previous frame's best estimate (for continuity)
    private float[] _prevFormants = Array.Empty<float>();
    private bool _hasPrevious;

    // Cost function weights
    private const float MagnitudeWeight = 2.0f;      // Prefer poles closer to unit circle
    private const float BandwidthWeight = 1.0f;      // Prefer narrower bandwidths
    private const float ContinuityWeight = 3.0f;     // Strongly prefer smooth trajectories
    private const float SpacingWeight = 1.5f;        // Prefer reasonable formant spacing

    // Thresholds (more permissive than simple tracker)
    private const float MinMagnitude = 0.55f;        // Lower threshold to catch F1
    private const float MaxMagnitude = 0.9995f;
    private const float MinBandwidth = 10f;
    private const float MaxBandwidth = 2500f;        // Allow wider bandwidths
    private const float MinFrequency = 80f;
    private const float MaxContinuityJump = 400f;    // Max Hz jump between frames

    // Diagnostic
    private int _diagCounter;

    public BeamSearchFormantTracker(int order = 12, int maxFormants = 5, int beamWidth = 8)
    {
        Configure(order, maxFormants, beamWidth);
    }

    public int Order => _order;

    public void Configure(int order, int maxFormants = 5, int beamWidth = 8)
    {
        _order = Math.Clamp(order, 4, 32);
        _maxFormants = Math.Clamp(maxFormants, 1, 8);
        _beamWidth = Math.Clamp(beamWidth, 1, 32);

        int size = _order + 1;
        if (_polyCoefficients.Length != size)
        {
            _polyCoefficients = new double[size];
            _roots = new Complex[_order];
            _newRoots = new Complex[_order];
            _candidates = new FormantCandidate[_order];
        }

        if (_prevFormants.Length != _maxFormants)
        {
            _prevFormants = new float[_maxFormants];
            // Beam states: each state tracks up to maxFormants assignments
            _beam = new BeamState[_beamWidth * 2];
            _newBeam = new BeamState[_beamWidth * 2];
            for (int i = 0; i < _beam.Length; i++)
            {
                _beam[i] = new BeamState(_maxFormants);
                _newBeam[i] = new BeamState(_maxFormants);
            }
        }
    }

    /// <summary>
    /// Reset tracking state (call when audio restarts or after silence).
    /// </summary>
    public void Reset()
    {
        _hasPrevious = false;
        _beamCount = 0;
        Array.Clear(_prevFormants);
    }

    /// <summary>
    /// Track formants using beam search. Returns count of formants found.
    /// </summary>
    public int Track(ReadOnlySpan<float> lpcCoefficients, int sampleRate,
        Span<float> formantFrequencies, Span<float> formantBandwidths,
        float minDisplayFreq, float maxDisplayFreq, int maxOutput)
    {
        if (lpcCoefficients.Length < _order + 1 || formantFrequencies.IsEmpty)
            return 0;

        maxOutput = Math.Min(maxOutput, Math.Min(formantFrequencies.Length,
            formantBandwidths.IsEmpty ? int.MaxValue : formantBandwidths.Length));
        if (maxOutput <= 0)
            return 0;

        // Validate coefficients
        for (int i = 0; i <= _order; i++)
        {
            float c = lpcCoefficients[i];
            if (float.IsNaN(c) || float.IsInfinity(c) || MathF.Abs(c) > 100f)
                return 0;
        }

        // Copy to double precision
        for (int i = 0; i <= _order; i++)
            _polyCoefficients[i] = lpcCoefficients[i];

        // Find roots
        SolveRootsAberth();

        // Extract candidates from roots
        ExtractCandidates(sampleRate);

        if (_candidateCount == 0)
        {
            _hasPrevious = false;
            return 0;
        }

        // Run beam search to find best assignment
        int resultCount = BeamSearch(maxOutput);

        // Diagnostic logging
        bool shouldLog = ++_diagCounter % 100 == 0;
        if (shouldLog && resultCount > 0)
        {
            Console.WriteLine($"[BeamSearch] Found {resultCount} formants: " +
                string.Join(", ", Enumerable.Range(0, resultCount)
                    .Select(i => $"F{i + 1}={formantFrequencies[i]:F0}Hz")));
        }

        // Copy results
        if (resultCount > 0 && _beamCount > 0)
        {
            var best = _beam[0];
            int outCount = Math.Min(resultCount, maxOutput);
            for (int i = 0; i < outCount; i++)
            {
                int candIdx = best.Assignments[i];
                if (candIdx >= 0 && candIdx < _candidateCount)
                {
                    formantFrequencies[i] = _candidates[candIdx].Frequency;
                    if (!formantBandwidths.IsEmpty)
                        formantBandwidths[i] = _candidates[candIdx].Bandwidth;
                }
            }

            // Update previous for next frame
            for (int i = 0; i < _maxFormants; i++)
            {
                if (i < outCount && best.Assignments[i] >= 0)
                    _prevFormants[i] = _candidates[best.Assignments[i]].Frequency;
                else
                    _prevFormants[i] = 0;
            }
            _hasPrevious = true;

            return outCount;
        }

        _hasPrevious = false;
        return 0;
    }

    private void ExtractCandidates(int sampleRate)
    {
        _candidateCount = 0;
        float nyquist = sampleRate * 0.5f;
        float maxHz = Math.Min(5500f, nyquist * 0.9f);

        for (int i = 0; i < _roots.Length; i++)
        {
            Complex root = _roots[i];

            // Only positive imaginary (conjugate pairs)
            if (root.Imaginary <= 0.001)
                continue;

            double magnitude = root.Magnitude;
            if (magnitude <= MinMagnitude || magnitude >= MaxMagnitude)
                continue;

            double angle = Math.Atan2(root.Imaginary, root.Real);
            float freq = (float)(angle * sampleRate / (2.0 * Math.PI));
            float bandwidth = (float)(-sampleRate / Math.PI * Math.Log(magnitude));

            if (freq < MinFrequency || freq > maxHz)
                continue;
            if (bandwidth < MinBandwidth || bandwidth > MaxBandwidth)
                continue;

            _candidates[_candidateCount++] = new FormantCandidate
            {
                Frequency = freq,
                Bandwidth = bandwidth,
                Magnitude = (float)magnitude,
                LocalCost = ComputeLocalCost(freq, bandwidth, (float)magnitude)
            };
        }

        // Sort candidates by frequency
        Array.Sort(_candidates, 0, _candidateCount,
            Comparer<FormantCandidate>.Create((a, b) => a.Frequency.CompareTo(b.Frequency)));
    }

    private float ComputeLocalCost(float freq, float bandwidth, float magnitude)
    {
        // Lower cost = better candidate
        // Prefer poles close to unit circle (high magnitude)
        float magCost = (1f - magnitude) * MagnitudeWeight;

        // Prefer narrow bandwidths (but not too narrow)
        float bwNorm = Math.Clamp(bandwidth / 500f, 0f, 2f);
        float bwCost = bwNorm * BandwidthWeight;

        return magCost + bwCost;
    }

    private int BeamSearch(int maxFormants)
    {
        int targetFormants = Math.Min(maxFormants, _maxFormants);
        _beamCount = 0;

        // Initialize beam with empty state
        _beam[0].Reset();
        _beamCount = 1;

        // Greedily assign formants one at a time
        for (int f = 0; f < targetFormants && _candidateCount > 0; f++)
        {
            int newBeamCount = 0;

            // For each current beam state
            for (int b = 0; b < _beamCount && b < _beamWidth; b++)
            {
                var state = _beam[b];

                // Try each unassigned candidate
                for (int c = 0; c < _candidateCount; c++)
                {
                    // Skip if already assigned in this state
                    bool alreadyUsed = false;
                    for (int j = 0; j < f; j++)
                    {
                        if (state.Assignments[j] == c)
                        {
                            alreadyUsed = true;
                            break;
                        }
                    }
                    if (alreadyUsed)
                        continue;

                    // Check ordering constraint (F1 < F2 < F3...)
                    if (f > 0)
                    {
                        int prevCand = state.Assignments[f - 1];
                        if (prevCand >= 0 && _candidates[c].Frequency <= _candidates[prevCand].Frequency)
                            continue;

                        // Check minimum spacing (formants should be at least ~200Hz apart)
                        float gap = _candidates[c].Frequency - _candidates[prevCand].Frequency;
                        if (gap < 150f)
                            continue;
                    }

                    // Compute cost for this assignment
                    float cost = state.TotalCost + _candidates[c].LocalCost;

                    // Add continuity cost if we have previous frame data
                    if (_hasPrevious && f < _prevFormants.Length && _prevFormants[f] > 0)
                    {
                        float jump = MathF.Abs(_candidates[c].Frequency - _prevFormants[f]);
                        if (jump > MaxContinuityJump)
                        {
                            // Large jump - high penalty but don't reject
                            cost += (jump / MaxContinuityJump) * ContinuityWeight;
                        }
                        else
                        {
                            // Small jump - bonus (negative cost)
                            cost -= (1f - jump / MaxContinuityJump) * 0.5f;
                        }
                    }

                    // Add to new beam if there's room
                    if (newBeamCount < _newBeam.Length)
                    {
                        _newBeam[newBeamCount].CopyFrom(state);
                        _newBeam[newBeamCount].Assignments[f] = c;
                        _newBeam[newBeamCount].FormantCount = f + 1;
                        _newBeam[newBeamCount].TotalCost = cost;
                        newBeamCount++;
                    }
                }
            }

            if (newBeamCount == 0)
                break;

            // Sort new beam by cost and keep top beamWidth
            Array.Sort(_newBeam, 0, newBeamCount,
                Comparer<BeamState>.Create((a, b) => a.TotalCost.CompareTo(b.TotalCost)));

            _beamCount = Math.Min(newBeamCount, _beamWidth);

            // Swap beams
            (_beam, _newBeam) = (_newBeam, _beam);
        }

        return _beamCount > 0 ? _beam[0].FormantCount : 0;
    }

    private void SolveRootsAberth()
    {
        int n = _order;
        const int maxIterations = 200;
        const double epsilon = 1e-10;

        // Initialize roots
        for (int i = 0; i < n; i++)
        {
            double radius = 0.7 + 0.25 * (i % 5) / 4.0;
            double angle = 2.0 * Math.PI * i / n + 0.1;
            _roots[i] = new Complex(radius * Math.Cos(angle), radius * Math.Sin(angle));
        }

        // Aberth-Ehrlich iteration
        for (int iter = 0; iter < maxIterations; iter++)
        {
            double maxDelta = 0.0;

            for (int i = 0; i < n; i++)
            {
                Complex z = _roots[i];

                // Horner's method
                Complex p = _polyCoefficients[0];
                Complex dp = Complex.Zero;
                for (int k = 1; k <= n; k++)
                {
                    dp = dp * z + p;
                    p = p * z + _polyCoefficients[k];
                }

                // Sum of 1/(z - z_j)
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

    private struct FormantCandidate
    {
        public float Frequency;
        public float Bandwidth;
        public float Magnitude;
        public float LocalCost;
    }

    private class BeamState
    {
        public int[] Assignments;
        public int FormantCount;
        public float TotalCost;

        public BeamState(int maxFormants)
        {
            Assignments = new int[maxFormants];
            Reset();
        }

        public void Reset()
        {
            Array.Fill(Assignments, -1);
            FormantCount = 0;
            TotalCost = 0f;
        }

        public void CopyFrom(BeamState other)
        {
            Array.Copy(other.Assignments, Assignments, Assignments.Length);
            FormantCount = other.FormantCount;
            TotalCost = other.TotalCost;
        }
    }
}
