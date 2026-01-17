namespace HotMic.Core.Plugins;

public sealed class SidechainTapProcessor
{
    private const float SpeechThresholdDb = -50f;
    private const float SpeechRangeDb = 30f;
    private const float VoicedLowHz = 120f;
    private const float VoicedHighHz = 400f;
    private const float UnvoicedHighHz = 2500f;
    private const float SibilanceCenterHz = 6500f;
    private const float SibilanceQ = 1.2f;

    private readonly EnvelopeFollower _speechEnv = new();
    private readonly EnvelopeFollower _lowEnv = new();
    private readonly EnvelopeFollower _highEnv = new();
    private readonly EnvelopeFollower _sibilanceEnv = new();

    private readonly BiquadFilter _lowPass = new();
    private readonly BiquadFilter _highPass = new();
    private readonly BiquadFilter _sibilanceBand = new();

    private float[] _speechBuffer = Array.Empty<float>();
    private float[] _voicedBuffer = Array.Empty<float>();
    private float[] _unvoicedBuffer = Array.Empty<float>();
    private float[] _sibilanceBuffer = Array.Empty<float>();

    // Metering - last sample values for UI display
    private float _meterSpeechPresence;
    private float _meterVoicedProbability;
    private float _meterUnvoicedEnergy;
    private float _meterSibilanceEnergy;
    public void Configure(int sampleRate, int blockSize)
    {
        _speechEnv.Configure(5f, 80f, sampleRate);
        _lowEnv.Configure(4f, 80f, sampleRate);
        _highEnv.Configure(2f, 60f, sampleRate);
        _sibilanceEnv.Configure(2f, 60f, sampleRate);

        _lowPass.SetLowPass(sampleRate, VoicedHighHz, 0.707f);
        _highPass.SetHighPass(sampleRate, UnvoicedHighHz, 0.707f);
        _sibilanceBand.SetBandPass(sampleRate, SibilanceCenterHz, SibilanceQ);

        if (_speechBuffer.Length != blockSize)
        {
            _speechBuffer = new float[blockSize];
            _voicedBuffer = new float[blockSize];
            _unvoicedBuffer = new float[blockSize];
            _sibilanceBuffer = new float[blockSize];
        }

        Reset();
    }

    public void Reset()
    {
        _speechEnv.Reset();
        _lowEnv.Reset();
        _highEnv.Reset();
        _sibilanceEnv.Reset();
        _lowPass.Reset();
        _highPass.Reset();
        _sibilanceBand.Reset();
    }

    public void ProcessBlock(ReadOnlySpan<float> buffer, long sampleTime, in SidechainWriter writer, SidechainSignalMask mask)
    {
        int count = buffer.Length;
        if (count == 0 || sampleTime < 0)
        {
            return;
        }

        if (_speechBuffer.Length != count)
        {
            // Block size should be fixed during a session. Skip processing if mismatched.
            return;
        }

        if (!writer.IsEnabled || mask == SidechainSignalMask.None)
        {
            return;
        }

        for (int i = 0; i < count; i++)
        {
            float sample = buffer[i];

            float env = _speechEnv.Process(sample);
            float envDb = DspUtils.LinearToDb(env);
            float presence = Math.Clamp((envDb - SpeechThresholdDb) / SpeechRangeDb, 0f, 1f);

            float low = _lowPass.Process(sample);
            float lowEnv = _lowEnv.Process(low);

            float high = _highPass.Process(sample);
            float highEnv = _highEnv.Process(high);

            float total = MathF.Max(env, 1e-6f);
            float voiced = Math.Clamp(lowEnv / total, 0f, 1f);
            float unvoiced = Math.Clamp(highEnv / total, 0f, 1f);

            float sib = _sibilanceBand.Process(sample);
            float sibEnv = _sibilanceEnv.Process(sib);
            float sibNorm = Math.Clamp(sibEnv / total, 0f, 1f);

            _speechBuffer[i] = presence;
            _voicedBuffer[i] = voiced;
            _unvoicedBuffer[i] = unvoiced;
            _sibilanceBuffer[i] = sibNorm;

            // Update metering with last sample values
            _meterSpeechPresence = presence;
            _meterVoicedProbability = voiced;
            _meterUnvoicedEnergy = unvoiced;
            _meterSibilanceEnergy = sibNorm;
        }

        if ((mask & SidechainSignalMask.SpeechPresence) != 0)
        {
            writer.WriteBlock(SidechainSignalId.SpeechPresence, sampleTime, _speechBuffer);
        }
        if ((mask & SidechainSignalMask.VoicedProbability) != 0)
        {
            writer.WriteBlock(SidechainSignalId.VoicedProbability, sampleTime, _voicedBuffer);
        }
        if ((mask & SidechainSignalMask.UnvoicedEnergy) != 0)
        {
            writer.WriteBlock(SidechainSignalId.UnvoicedEnergy, sampleTime, _unvoicedBuffer);
        }
        if ((mask & SidechainSignalMask.SibilanceEnergy) != 0)
        {
            writer.WriteBlock(SidechainSignalId.SibilanceEnergy, sampleTime, _sibilanceBuffer);
        }
    }

    /// <summary>Gets the current speech presence level (0-1).</summary>
    public float GetSpeechPresence() => _meterSpeechPresence;

    /// <summary>Gets the current voiced probability (0-1).</summary>
    public float GetVoicedProbability() => _meterVoicedProbability;

    /// <summary>Gets the current unvoiced energy (0-1).</summary>
    public float GetUnvoicedEnergy() => _meterUnvoicedEnergy;

    /// <summary>Gets the current sibilance energy (0-1).</summary>
    public float GetSibilanceEnergy() => _meterSibilanceEnergy;
}
