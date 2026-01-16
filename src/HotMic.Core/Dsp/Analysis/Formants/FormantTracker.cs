using System.Diagnostics;
using System.Numerics;

namespace HotMic.Core.Dsp.Analysis.Formants;

/// <summary>
/// Tracks vocal formants from LPC coefficients via polynomial root finding.
/// Uses companion matrix eigenvalue method for robust root finding.
/// </summary>
public sealed class FormantTracker
{
    private int _order;
    private Complex[] _roots = Array.Empty<Complex>();
    private double[] _polyCoefficients = Array.Empty<double>();
    private Complex[] _newRoots = Array.Empty<Complex>(); // Work buffer for iteration
    private float[] _freqScratch = Array.Empty<float>();
    private float[] _bwScratch = Array.Empty<float>();

    // Diagnostics
    private static long _lastDiagnosticTicks;
    private static readonly long DiagnosticIntervalTicks = Stopwatch.Frequency; // 1 second

    public FormantTracker(int order)
    {
        Configure(order);
    }

    public int Order => _order;

    public void Configure(int order)
    {
        _order = Math.Clamp(order, 4, 32);
        int size = _order + 1;
        if (_polyCoefficients.Length != size)
        {
            _polyCoefficients = new double[size];
            _roots = new Complex[_order];
            _newRoots = new Complex[_order];
        }

        if (_freqScratch.Length != _order)
        {
            _freqScratch = new float[_order];
            _bwScratch = new float[_order];
        }
    }

    /// <summary>
    /// Extract formant frequencies and bandwidths. Returns count of formants written.
    /// </summary>
    public int Track(ReadOnlySpan<float> lpcCoefficients, int sampleRate,
        Span<float> formantFrequencies, Span<float> formantBandwidths,
        float minFrequency, float maxFrequency, int maxFormants)
    {
        if (lpcCoefficients.Length < _order + 1 || formantFrequencies.IsEmpty || formantBandwidths.IsEmpty)
        {
            return 0;
        }

        maxFormants = Math.Min(maxFormants, Math.Min(formantFrequencies.Length, formantBandwidths.Length));
        if (maxFormants <= 0)
        {
            return 0;
        }

        // Validate LPC coefficients - check for NaN/Inf and unreasonable values
        for (int i = 0; i <= _order; i++)
        {
            float c = lpcCoefficients[i];
            if (float.IsNaN(c) || float.IsInfinity(c) || MathF.Abs(c) > 100f)
            {
                return 0;
            }
        }

        // Copy coefficients to double precision for numerical stability
        for (int i = 0; i <= _order; i++)
        {
            _polyCoefficients[i] = lpcCoefficients[i];
        }

        // Find roots using companion matrix eigenvalue method
        SolveRootsCompanionMatrix();

        int count = 0;
        float nyquist = sampleRate * 0.5f;
        // Formant detection uses fixed voice-appropriate range (not display range)
        // Human formants F1-F4 are typically in 150-5000 Hz range
        // F1: ~200-800 Hz, F2: ~800-2500 Hz, F3: ~2000-3500 Hz, F4: ~3000-4500 Hz
        float minHz = 100f;
        float maxHz = Math.Min(5500f, nyquist * 0.8f);

        // Diagnostics
        long now = Stopwatch.GetTimestamp();
        bool shouldLog = now - _lastDiagnosticTicks > DiagnosticIntervalTicks;
        if (shouldLog) _lastDiagnosticTicks = now;

        int skippedNegImag = 0, skippedMagLow = 0, skippedMagHigh = 0;
        int skippedFreqLow = 0, skippedFreqHigh = 0, skippedBwLow = 0, skippedBwHigh = 0;

        for (int i = 0; i < _roots.Length; i++)
        {
            Complex root = _roots[i];

            // Only consider roots with positive imaginary part (conjugate pairs)
            if (root.Imaginary <= 0.001)
            {
                skippedNegImag++;
                continue;
            }

            double magnitude = root.Magnitude;

            // Formant poles should be reasonably close to unit circle
            // At 48kHz with LPC order 24, poles typically have magnitude 0.8-0.95
            // Magnitude 0.80 = ~3410Hz bw, 0.85 = ~2484Hz, 0.88 = ~1955Hz, 0.90 = ~1610Hz
            // Use 0.80 threshold to accept typical LPC poles; bandwidth filter handles the rest
            if (magnitude <= 0.80)
            {
                skippedMagLow++;
                continue;
            }
            if (magnitude >= 0.9995)
            {
                skippedMagHigh++;
                continue;
            }

            double angle = Math.Atan2(root.Imaginary, root.Real);
            float freq = (float)(angle * sampleRate / (2.0 * Math.PI));
            float bandwidth = (float)(-sampleRate / Math.PI * Math.Log(magnitude));

            // Log first few roots with positive imaginary for debugging
            if (shouldLog && i < 6)
            {
                Console.WriteLine($"[Formant] root[{i}]: re={root.Real:F4}, im={root.Imaginary:F4}, mag={magnitude:F4}, freq={freq:F1}Hz, bw={bandwidth:F1}Hz");
            }

            // Filter by frequency range and bandwidth
            // With magnitude > 0.80 filter, max bandwidth is ~3400 Hz
            if (freq < minHz) { skippedFreqLow++; continue; }
            if (freq > maxHz) { skippedFreqHigh++; continue; }
            if (bandwidth < 10f) { skippedBwLow++; continue; }
            if (bandwidth > 3500f) { skippedBwHigh++; continue; }

            _freqScratch[count] = freq;
            _bwScratch[count] = bandwidth;
            count++;
        }

        if (shouldLog)
        {
            Console.WriteLine($"[Formant] filters: negImag={skippedNegImag}, magLow={skippedMagLow}, magHigh={skippedMagHigh}, freqLo={skippedFreqLow}, freqHi={skippedFreqHigh}, bwLo={skippedBwLow}, bwHi={skippedBwHigh}");
            Console.WriteLine($"[Formant] passed={count}, minHz={minHz}, maxHz={maxHz}, sr={sampleRate}");
        }

        if (count == 0)
        {
            return 0;
        }

        // Sort by frequency (small N, insertion sort).
        for (int i = 1; i < count; i++)
        {
            float f = _freqScratch[i];
            float b = _bwScratch[i];
            int j = i - 1;
            while (j >= 0 && _freqScratch[j] > f)
            {
                _freqScratch[j + 1] = _freqScratch[j];
                _bwScratch[j + 1] = _bwScratch[j];
                j--;
            }
            _freqScratch[j + 1] = f;
            _bwScratch[j + 1] = b;
        }

        int outputCount = Math.Min(count, maxFormants);
        for (int i = 0; i < outputCount; i++)
        {
            formantFrequencies[i] = _freqScratch[i];
            formantBandwidths[i] = _bwScratch[i];
        }

        return outputCount;
    }

    /// <summary>
    /// Find polynomial roots using Aberth-Ehrlich method (improved Durand-Kerner).
    /// More reliable than QR for this use case.
    /// </summary>
    private void SolveRootsCompanionMatrix()
    {
        int n = _order;
        const int maxIterations = 200;
        const double epsilon = 1e-10;

        // Initialize roots with better spacing for LPC polynomials
        // Use roots distributed inside unit circle at varying radii and angles
        for (int i = 0; i < n; i++)
        {
            // Spread roots at different radii (0.7 to 0.95) to better capture formants
            double radius = 0.7 + 0.25 * (i % 5) / 4.0;
            // Distribute angles, but offset slightly to avoid symmetry issues
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

                // Evaluate polynomial and its derivative at z using Horner's method
                Complex p = _polyCoefficients[0];
                Complex dp = Complex.Zero;
                for (int k = 1; k <= n; k++)
                {
                    dp = dp * z + p;
                    p = p * z + _polyCoefficients[k];
                }

                // Compute sum of 1/(z - z_j) for j != i
                Complex sum = Complex.Zero;
                for (int j = 0; j < n; j++)
                {
                    if (i != j)
                    {
                        Complex diff = z - _roots[j];
                        if (diff.Magnitude > 1e-14)
                        {
                            sum += 1.0 / diff;
                        }
                    }
                }

                // Aberth correction: delta = p(z) / (p'(z) - p(z) * sum)
                Complex denom = dp - p * sum;
                Complex delta;
                if (denom.Magnitude > 1e-14)
                {
                    delta = p / denom;
                }
                else if (dp.Magnitude > 1e-14)
                {
                    // Fall back to Newton's method
                    delta = p / dp;
                }
                else
                {
                    delta = Complex.Zero;
                }

                // Limit step size to prevent divergence
                double deltaMag = delta.Magnitude;
                if (deltaMag > 0.5)
                {
                    delta *= 0.5 / deltaMag;
                }

                _newRoots[i] = z - delta;
                maxDelta = Math.Max(maxDelta, delta.Magnitude);
            }

            // Update all roots simultaneously
            for (int i = 0; i < n; i++)
            {
                _roots[i] = _newRoots[i];
            }

            if (maxDelta < epsilon)
            {
                break;
            }
        }

        // Diagnostic: print some roots
        long now = Stopwatch.GetTimestamp();
        if (now - _lastDiagnosticTicks > DiagnosticIntervalTicks)
        {
            Console.WriteLine($"[RootSolver] Sample roots after Aberth iteration:");
            int printed = 0;
            for (int i = 0; i < n && printed < 6; i++)
            {
                var r = _roots[i];
                if (r.Imaginary > 0.01) // Only show positive imaginary
                {
                    double freq = Math.Atan2(r.Imaginary, r.Real) * 48000 / (2 * Math.PI);
                    Console.WriteLine($"  root[{i}]: re={r.Real:F4}, im={r.Imaginary:F4}, mag={r.Magnitude:F4}, ~freq={freq:F0}Hz");
                    printed++;
                }
            }
            if (printed == 0)
            {
                Console.WriteLine("  (no roots with positive imaginary part found)");
                // Print first few anyway
                for (int i = 0; i < Math.Min(4, n); i++)
                {
                    var r = _roots[i];
                    Console.WriteLine($"  root[{i}]: re={r.Real:F4}, im={r.Imaginary:F4}, mag={r.Magnitude:F4}");
                }
            }
        }
    }
}
