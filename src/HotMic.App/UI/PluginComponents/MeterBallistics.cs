namespace HotMic.App.UI.PluginComponents;

/// <summary>
/// PPM-style meter ballistics for audio level metering.
/// Provides quasi-peak (integrated peak) and current level tracking with proper attack/decay.
/// Based on BBC PPM specifications: ~10ms attack, ~24dB in 2.8s decay.
/// </summary>
public sealed class MeterBallistics
{
    // PPM-style timing constants
    private const float DefaultPeakAttackMs = 10f;       // Integration time for quasi-peak
    private const float DefaultPeakHoldMs = 300f;        // Hold before decay (shorter for responsiveness)
    private const float DefaultPeakDecayDbPerSec = 12f;  // Faster decay for visual clarity
    private const float DefaultCurrentSmoothing = 0.15f; // More responsive current level

    // Silence threshold - values below this are treated as silence
    private const float SilenceThreshold = 0.001f;       // ~-60dB linear

    private readonly float _peakAttackMs;
    private readonly float _peakHoldMs;
    private readonly float _peakDecayDbPerSec;
    private readonly float _currentSmoothing;

    private float _quasiPeak;      // Integrated peak (not true peak)
    private float _heldPeak;       // Peak with hold
    private float _current;        // Smoothed current level
    private float _holdTimeRemaining;
    private DateTime _lastUpdate = DateTime.MinValue;

    /// <summary>
    /// Creates meter ballistics with default PPM-style timing.
    /// </summary>
    public MeterBallistics() : this(DefaultPeakAttackMs, DefaultPeakHoldMs, DefaultPeakDecayDbPerSec, DefaultCurrentSmoothing)
    {
    }

    /// <summary>
    /// Creates meter ballistics with custom timing.
    /// </summary>
    public MeterBallistics(float peakAttackMs, float peakHoldMs, float peakDecayDbPerSec, float currentSmoothing)
    {
        _peakAttackMs = peakAttackMs;
        _peakHoldMs = peakHoldMs;
        _peakDecayDbPerSec = peakDecayDbPerSec;
        _currentSmoothing = Math.Clamp(currentSmoothing, 0f, 0.99f);
    }

    /// <summary>
    /// Gets the current smoothed level (for the main bar fill).
    /// </summary>
    public float Current => _current;

    /// <summary>
    /// Gets the held peak level (for the peak marker).
    /// </summary>
    public float Peak => _heldPeak;

    /// <summary>
    /// Updates the meter with a new input value. Call this each frame.
    /// </summary>
    /// <param name="value">The input value (linear 0-1 for levels).</param>
    public void Update(float value)
    {
        value = Math.Max(0f, value);

        // Calculate time delta
        var now = DateTime.UtcNow;
        float deltaMs = _lastUpdate == DateTime.MinValue ? 16f : (float)(now - _lastUpdate).TotalMilliseconds;
        deltaMs = Math.Clamp(deltaMs, 1f, 100f);
        _lastUpdate = now;

        // Silence detection - snap to zero quickly when input is essentially silent
        bool isSilent = value < SilenceThreshold;
        if (isSilent)
        {
            // Fast decay to zero during silence
            float silenceDecay = 1f - MathF.Exp(-deltaMs / 50f); // 50ms time constant
            _quasiPeak *= (1f - silenceDecay);
            _current *= (1f - silenceDecay);

            // Snap to zero when very small
            if (_quasiPeak < SilenceThreshold) _quasiPeak = 0f;
            if (_current < SilenceThreshold) _current = 0f;
        }
        else
        {
            // PPM-style quasi-peak: integrate towards input with attack time
            float attackCoeff = 1f - MathF.Exp(-deltaMs / _peakAttackMs);
            if (value > _quasiPeak)
            {
                _quasiPeak += (value - _quasiPeak) * attackCoeff;
            }
            else
            {
                // Moderate fall for quasi-peak when signal present
                float fallCoeff = 1f - MathF.Exp(-deltaMs / 30f); // 30ms fall time
                _quasiPeak += (value - _quasiPeak) * fallCoeff;
            }

            // Current level: responsive smoothing
            _current = _current * _currentSmoothing + value * (1f - _currentSmoothing);
        }

        // Held peak with hold time and decay
        if (_quasiPeak >= _heldPeak)
        {
            _heldPeak = _quasiPeak;
            _holdTimeRemaining = _peakHoldMs;
        }
        else if (_holdTimeRemaining > 0)
        {
            _holdTimeRemaining -= deltaMs;
        }
        else
        {
            // Linear decay in normalized space
            // For a 60dB meter range, 12dB/s = 0.2 normalized units per second
            float decayPerSec = _peakDecayDbPerSec / 60f; // Normalize to 0-1 range
            float decayAmount = decayPerSec * deltaMs / 1000f;
            _heldPeak = MathF.Max(0f, _heldPeak - decayAmount);
        }
    }

    /// <summary>
    /// Updates the meter with a new dB value. Use this for GR meters.
    /// </summary>
    public void UpdateDb(float valueDb)
    {
        valueDb = Math.Max(0f, valueDb);

        var now = DateTime.UtcNow;
        float deltaMs = _lastUpdate == DateTime.MinValue ? 16f : (float)(now - _lastUpdate).TotalMilliseconds;
        deltaMs = Math.Clamp(deltaMs, 1f, 100f);
        _lastUpdate = now;

        // Silence threshold for dB values (0.1 dB is essentially zero GR)
        const float silenceDbThreshold = 0.1f;
        bool isSilent = valueDb < silenceDbThreshold;

        if (isSilent)
        {
            // Fast decay to zero during silence
            float silenceDecay = 1f - MathF.Exp(-deltaMs / 50f);
            _quasiPeak *= (1f - silenceDecay);
            _current *= (1f - silenceDecay);

            if (_quasiPeak < silenceDbThreshold) _quasiPeak = 0f;
            if (_current < silenceDbThreshold) _current = 0f;
        }
        else
        {
            // PPM-style quasi-peak
            float attackCoeff = 1f - MathF.Exp(-deltaMs / _peakAttackMs);
            if (valueDb > _quasiPeak)
            {
                _quasiPeak += (valueDb - _quasiPeak) * attackCoeff;
            }
            else
            {
                float fallCoeff = 1f - MathF.Exp(-deltaMs / 30f);
                _quasiPeak += (valueDb - _quasiPeak) * fallCoeff;
            }

            // Current level: responsive smoothing
            _current = _current * _currentSmoothing + valueDb * (1f - _currentSmoothing);
        }

        // Held peak with hold and linear dB decay
        if (_quasiPeak >= _heldPeak)
        {
            _heldPeak = _quasiPeak;
            _holdTimeRemaining = _peakHoldMs;
        }
        else if (_holdTimeRemaining > 0)
        {
            _holdTimeRemaining -= deltaMs;
        }
        else
        {
            float decayAmount = _peakDecayDbPerSec * deltaMs / 1000f;
            _heldPeak = MathF.Max(0f, _heldPeak - decayAmount);
        }
    }

    /// <summary>
    /// Resets the meter state.
    /// </summary>
    public void Reset()
    {
        _quasiPeak = 0f;
        _heldPeak = 0f;
        _current = 0f;
        _holdTimeRemaining = 0f;
        _lastUpdate = DateTime.MinValue;
    }
}
