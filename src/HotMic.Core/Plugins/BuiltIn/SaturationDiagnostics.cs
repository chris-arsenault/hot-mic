namespace HotMic.Core.Plugins.BuiltIn;

/// <summary>
/// Thread-safe diagnostic data capture for saturation visualization.
/// All data is captured without allocation on the audio thread.
/// </summary>
public sealed class SaturationDiagnostics
{
    private const int TransferCurveSamples = 256;
    private const int ScopeSamples = 512;
    private const int HarmonicBins = 8; // DC + harmonics 1-7

    // Transfer curve: (input, output, envelope) tuples for scatter plot
    private readonly float[] _tcInput = new float[TransferCurveSamples];
    private readonly float[] _tcOutput = new float[TransferCurveSamples];
    private readonly float[] _tcEnvelope = new float[TransferCurveSamples];
    private int _tcWriteIndex;
    private int _tcVersion;

    // Null-difference scope: delta = wet - dry
    private readonly float[] _scopeDelta = new float[ScopeSamples];
    private int _scopeWriteIndex;
    private int _scopeVersion;

    // Harmonic analysis buffers (filled from UI thread via FFT)
    private readonly float[] _harmonicMagnitudes = new float[HarmonicBins];
    private float _evenOddRatio;
    private int _harmonicVersion;

    // FFT input buffer (circular, written from audio thread)
    private const int FftBufferSize = 2048;
    private readonly float[] _fftInputBuffer = new float[FftBufferSize];
    private int _fftWriteIndex;
    private int _fftVersion;

    /// <summary>
    /// Called from audio thread. Records a sample point for transfer curve visualization.
    /// </summary>
    public void RecordTransferSample(float input, float output, float envelope)
    {
        int idx = _tcWriteIndex;
        _tcInput[idx] = input;
        _tcOutput[idx] = output;
        _tcEnvelope[idx] = envelope;
        _tcWriteIndex = (idx + 1) & (TransferCurveSamples - 1);
        Interlocked.Increment(ref _tcVersion);
    }

    /// <summary>
    /// Called from audio thread. Records delta samples for null-difference scope.
    /// </summary>
    public void RecordScopeSample(float delta)
    {
        int idx = _scopeWriteIndex;
        _scopeDelta[idx] = delta;
        _scopeWriteIndex = (idx + 1) & (ScopeSamples - 1);
        Interlocked.Increment(ref _scopeVersion);
    }

    /// <summary>
    /// Called from audio thread. Records output samples for FFT analysis.
    /// </summary>
    public void RecordFftSample(float sample)
    {
        int idx = _fftWriteIndex;
        _fftInputBuffer[idx] = sample;
        _fftWriteIndex = (idx + 1) & (FftBufferSize - 1);
        Interlocked.Increment(ref _fftVersion);
    }

    /// <summary>
    /// Gets transfer curve sample points for visualization. Called from UI thread.
    /// Returns number of valid samples copied.
    /// </summary>
    public int GetTransferCurveSamples(float[] inputs, float[] outputs, float[] envelopes)
    {
        int count = Math.Min(inputs.Length, TransferCurveSamples);
        int writeIdx = Volatile.Read(ref _tcWriteIndex);

        for (int i = 0; i < count; i++)
        {
            int srcIdx = (writeIdx - count + i + TransferCurveSamples) & (TransferCurveSamples - 1);
            inputs[i] = _tcInput[srcIdx];
            outputs[i] = _tcOutput[srcIdx];
            envelopes[i] = _tcEnvelope[srcIdx];
        }

        return count;
    }

    /// <summary>
    /// Gets null-difference scope samples. Called from UI thread.
    /// </summary>
    public int GetScopeSamples(float[] deltas)
    {
        int count = Math.Min(deltas.Length, ScopeSamples);
        int writeIdx = Volatile.Read(ref _scopeWriteIndex);

        for (int i = 0; i < count; i++)
        {
            int srcIdx = (writeIdx - count + i + ScopeSamples) & (ScopeSamples - 1);
            deltas[i] = _scopeDelta[srcIdx];
        }

        return count;
    }

    /// <summary>
    /// Gets FFT input samples for harmonic analysis. Called from UI thread.
    /// </summary>
    public int GetFftSamples(float[] buffer)
    {
        int count = Math.Min(buffer.Length, FftBufferSize);
        int writeIdx = Volatile.Read(ref _fftWriteIndex);

        for (int i = 0; i < count; i++)
        {
            int srcIdx = (writeIdx - count + i + FftBufferSize) & (FftBufferSize - 1);
            buffer[i] = _fftInputBuffer[srcIdx];
        }

        return count;
    }

    /// <summary>
    /// Stores computed harmonic magnitudes from UI thread FFT analysis.
    /// </summary>
    public void SetHarmonicMagnitudes(ReadOnlySpan<float> magnitudes, float evenOddRatio)
    {
        int count = Math.Min(magnitudes.Length, HarmonicBins);
        for (int i = 0; i < count; i++)
        {
            _harmonicMagnitudes[i] = magnitudes[i];
        }
        _evenOddRatio = evenOddRatio;
        Interlocked.Increment(ref _harmonicVersion);
    }

    /// <summary>
    /// Gets harmonic magnitudes for display. Index 0 = fundamental, 1-6 = harmonics 2-7.
    /// </summary>
    public void GetHarmonicMagnitudes(float[] magnitudes, out float evenOddRatio)
    {
        int count = Math.Min(magnitudes.Length, HarmonicBins);
        for (int i = 0; i < count; i++)
        {
            magnitudes[i] = _harmonicMagnitudes[i];
        }
        evenOddRatio = _evenOddRatio;
    }

    public int TransferCurveVersion => Volatile.Read(ref _tcVersion);
    public int ScopeVersion => Volatile.Read(ref _scopeVersion);
    public int FftVersion => Volatile.Read(ref _fftVersion);
    public int HarmonicVersion => Volatile.Read(ref _harmonicVersion);

    public int TransferCurveSampleCount => TransferCurveSamples;
    public int ScopeSampleCount => ScopeSamples;
    public int FftSampleCount => FftBufferSize;
}
