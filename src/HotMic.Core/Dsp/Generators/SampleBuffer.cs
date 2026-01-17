namespace HotMic.Core.Dsp.Generators;

/// <summary>
/// Pre-loaded audio sample buffer for playback.
/// Supports variable-speed playback, looping modes, and trim points.
/// </summary>
public sealed class SampleBuffer
{
    /// <summary>
    /// Maximum sample length at 48kHz (10 seconds).
    /// </summary>
    public const int MaxSamples = 480000;

    private readonly float[] _samples;
    private int _length;
    private int _sampleRate;

    public SampleBuffer()
    {
        _samples = new float[MaxSamples];
        _length = 0;
        _sampleRate = 48000;
    }

    public int Length => _length;
    public int SampleRate => _sampleRate;
    public bool IsLoaded => _length > 0;

    /// <summary>
    /// Load samples from a float array (mono).
    /// If stereo, should be converted to mono before calling.
    /// </summary>
    public void Load(float[] samples, int sampleRate)
    {
        int count = Math.Min(samples.Length, MaxSamples);
        Array.Copy(samples, 0, _samples, 0, count);
        _length = count;
        _sampleRate = sampleRate;
    }

    /// <summary>
    /// Load samples from a span (mono).
    /// </summary>
    public void Load(ReadOnlySpan<float> samples, int sampleRate)
    {
        int count = Math.Min(samples.Length, MaxSamples);
        samples.Slice(0, count).CopyTo(_samples);
        _length = count;
        _sampleRate = sampleRate;
    }

    /// <summary>
    /// Clear the loaded sample.
    /// </summary>
    public void Clear()
    {
        _length = 0;
    }

    /// <summary>
    /// Get sample at position with linear interpolation.
    /// Position is in samples (can be fractional for variable speed).
    /// </summary>
    public float GetSample(double position)
    {
        if (_length == 0) return 0f;

        int idx0 = (int)position;
        int idx1 = idx0 + 1;

        if (idx0 < 0 || idx0 >= _length) return 0f;
        if (idx1 >= _length) idx1 = idx0;

        float frac = (float)(position - idx0);
        return _samples[idx0] * (1f - frac) + _samples[idx1] * frac;
    }

    /// <summary>
    /// Get the underlying sample array for direct access.
    /// </summary>
    public ReadOnlySpan<float> Samples => new ReadOnlySpan<float>(_samples, 0, _length);

    /// <summary>
    /// Export sample data as a new array (for persistence).
    /// </summary>
    public float[] ExportSamples()
    {
        if (_length == 0) return Array.Empty<float>();
        var result = new float[_length];
        Array.Copy(_samples, 0, result, 0, _length);
        return result;
    }
}

/// <summary>
/// Sample player with loop modes and trim control.
/// </summary>
public struct SamplePlayer
{
    private double _position;
    private float _speed;
    private float _trimStart;
    private float _trimEnd;
    private int _loopMode; // 0=loop, 1=oneshot, 2=pingpong
    private bool _forward;
    private bool _playing;
    private int _playbackSampleRate;

    public void Initialize(int playbackSampleRate)
    {
        _playbackSampleRate = playbackSampleRate;
        _position = 0;
        _speed = 1f;
        _trimStart = 0f;
        _trimEnd = 1f;
        _loopMode = 0;
        _forward = true;
        _playing = true;
    }

    public void SetSpeed(float speed)
    {
        _speed = Math.Clamp(speed, 0.5f, 2f);
    }

    public void SetTrimStart(float normalized)
    {
        _trimStart = Math.Clamp(normalized, 0f, _trimEnd - 0.01f);
    }

    public void SetTrimEnd(float normalized)
    {
        _trimEnd = Math.Clamp(normalized, _trimStart + 0.01f, 1f);
    }

    public void SetLoopMode(int mode)
    {
        _loopMode = Math.Clamp(mode, 0, 2);
    }

    public void Reset()
    {
        _position = 0;
        _forward = true;
        _playing = true;
    }

    public float Next(SampleBuffer buffer)
    {
        if (!_playing || !buffer.IsLoaded) return 0f;

        int length = buffer.Length;
        int startSample = (int)(_trimStart * length);
        int endSample = (int)(_trimEnd * length);
        int playLength = endSample - startSample;

        if (playLength <= 0) return 0f;

        // Calculate actual position in buffer
        double bufferPosition = startSample + _position;
        float sample = buffer.GetSample(bufferPosition);

        // Calculate speed adjustment for sample rate difference
        double speedRatio = (double)buffer.SampleRate / _playbackSampleRate;
        double increment = _speed * speedRatio;

        // Advance position
        if (_forward)
        {
            _position += increment;
            if (_position >= playLength)
            {
                switch (_loopMode)
                {
                    case 0: // loop
                        _position = _position % playLength;
                        break;
                    case 1: // oneshot
                        _position = playLength - 1;
                        _playing = false;
                        break;
                    case 2: // pingpong
                        _position = playLength - 1;
                        _forward = false;
                        break;
                }
            }
        }
        else
        {
            _position -= increment;
            if (_position < 0)
            {
                _position = 0;
                _forward = true;
            }
        }

        return sample;
    }
}
