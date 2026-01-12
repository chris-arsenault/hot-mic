using System;
using System.IO;
using HotMic.Core.Dsp;

namespace HotMic.Core.Plugins.BuiltIn;

internal sealed class DeepFilterNetProcessor : IDisposable
{
    private const float MeanNormMin = -60f;
    private const float MeanNormMax = -90f;
    private const float UnitNormMin = 0.001f;
    private const float UnitNormMax = 0.0001f;
    private const float DefaultPostFilterBeta = 0.02f;
    private const float MinDbThresh = -10f;
    private const float MaxDbErbThresh = 30f;
    private const float MaxDbDfThresh = 20f;
    private readonly DeepFilterNetConfig _config;
    private readonly DeepFilterNetInference _inference;
    private readonly DeepFilterNetStft _stft;
    private readonly int[] _erbBands;
    private readonly float[] _meanNormState;
    private readonly float[] _unitNormState;
    private readonly float[] _specBuffer;
    private readonly float[] _specOut;
    private readonly float[] _erbFeatures;
    private readonly float[] _specFeatures;
    private readonly float[] _gains;
    private readonly float[] _coefs;
    private readonly float[][] _noisyHistory;
    private readonly float[][] _enhHistory;
    private readonly int _historySize;
    private readonly int _specLength;
    private readonly float _alpha;
    private int _historyIndex = -1;
    private int _framesProcessed;
    private int _skipCounter;

    public DeepFilterNetProcessor(string modelDirectory)
    {
        var configPath = Path.Combine(modelDirectory, "config.ini");
        _config = DeepFilterNetConfig.Load(configPath);
        if (!string.Equals(_config.ModelType, "deepfilternet3", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Unsupported model type '{_config.ModelType}'.");
        }

        _alpha = _config.NormAlpha ?? DeepFilterNetDsp.CalcNormAlpha(_config.SampleRate, _config.HopSize, _config.NormTau);
        _stft = new DeepFilterNetStft(_config.FftSize, _config.HopSize);
        _erbBands = DeepFilterNetDsp.BuildErbBands(_config.SampleRate, _config.FftSize, _config.NbErb, _config.MinNbErbFreqs);

        _specLength = _stft.FreqSize * 2;
        _specBuffer = new float[_specLength];
        _specOut = new float[_specLength];
        _erbFeatures = new float[_config.NbErb];
        _specFeatures = new float[_config.NbDf * 2];
        _gains = new float[_config.NbErb];
        _coefs = new float[_config.NbDf * _config.DfOrder * 2];
        _meanNormState = BuildInitState(_config.NbErb, MeanNormMin, MeanNormMax);
        _unitNormState = BuildInitState(_config.NbDf, UnitNormMin, UnitNormMax);

        _historySize = _config.DfOrder + _config.Lookahead;
        _noisyHistory = AllocateHistory(_historySize, _specLength);
        _enhHistory = AllocateHistory(_historySize, _specLength);

        string encPath = Path.Combine(modelDirectory, "enc.onnx");
        string erbPath = Path.Combine(modelDirectory, "erb_dec.onnx");
        string dfPath = Path.Combine(modelDirectory, "df_dec.onnx");
        _inference = new DeepFilterNetInference(encPath, erbPath, dfPath, _config.NbErb, _config.NbDf);
    }

    public int HopSize => _config.HopSize;
    public int LatencySamples => Math.Max(0, (_config.FftSize - _config.HopSize) + _config.Lookahead * _config.HopSize);

    /// <summary>
    /// Gets the average gain reduction in dB from the last processed frame.
    /// Returns 0 if no gains were applied, positive values indicate reduction.
    /// </summary>
    public float LastGainReductionDb { get; private set; }

    public void Reset()
    {
        _stft.Reset();
        _framesProcessed = 0;
        _historyIndex = -1;
        _skipCounter = 0;
        Array.Copy(BuildInitState(_config.NbErb, MeanNormMin, MeanNormMax), _meanNormState, _meanNormState.Length);
        Array.Copy(BuildInitState(_config.NbDf, UnitNormMin, UnitNormMax), _unitNormState, _unitNormState.Length);
        ClearHistory(_noisyHistory);
        ClearHistory(_enhHistory);
    }

    public void ProcessHop(
        ReadOnlySpan<float> input,
        Span<float> output,
        bool postFilterEnabled,
        float attenLimitDb)
    {
        if (input.Length != _config.HopSize || output.Length != _config.HopSize)
        {
            throw new ArgumentException("DeepFilterNet hop size mismatch.");
        }

        float maxAbs = 0f;
        float energy = 0f;
        for (int i = 0; i < input.Length; i++)
        {
            float sample = input[i];
            float abs = MathF.Abs(sample);
            if (abs > maxAbs)
            {
                maxAbs = abs;
            }
            energy += sample * sample;
        }

        if (maxAbs <= 0f)
        {
            output.Clear();
            LastGainReductionDb = 0f;
            return;
        }

        float rms = energy / input.Length;
        if (rms < 1e-7f)
        {
            _skipCounter++;
        }
        else
        {
            _skipCounter = 0;
        }

        if (_skipCounter > 5)
        {
            output.Clear();
            LastGainReductionDb = 0f;
            return;
        }

        _stft.Analyze(input, _specBuffer);
        PushHistory(_specBuffer);

        ComputeFeatures();

        using var enc = _inference.RunEncoder(_erbFeatures, _specFeatures);
        float lsnr = enc.Lsnr;
        var stages = ApplyStages(lsnr);

        if (_framesProcessed <= _config.Lookahead)
        {
            output.Clear();
            LastGainReductionDb = 0f;
            return;
        }

        int targetIndex = GetHistoryIndex(_config.Lookahead);
        float[] noisyTarget = _noisyHistory[targetIndex];
        float[] enhTarget = _enhHistory[targetIndex];

        if (stages.applyGains)
        {
            _inference.FillGains(enc, _gains);
            DeepFilterNetDsp.ApplyInterpBandGain(enhTarget, _gains, _erbBands);
        }
        else if (stages.applyGainZeros)
        {
            Array.Clear(_gains, 0, _gains.Length);
            DeepFilterNetDsp.ApplyInterpBandGain(enhTarget, _gains, _erbBands);
        }

        Array.Copy(enhTarget, _specOut, _specOut.Length);

        if (stages.applyDf)
        {
            _inference.FillCoefs(enc, _coefs);
            ApplyDeepFilter(_specOut, targetIndex);
        }

        if (stages.applyGains && postFilterEnabled)
        {
            DeepFilterNetDsp.PostFilter(noisyTarget, _specOut, postFilterEnabled ? DefaultPostFilterBeta : 0f);
        }

        if (attenLimitDb < 100f)
        {
            float minGain = MathF.Pow(10f, -attenLimitDb / 20f);
            int halfSpec = _specOut.Length / 2;

            for (int bin = 0; bin < halfSpec; bin++)
            {
                int idx = bin * 2;
                float outRe = _specOut[idx];
                float outIm = _specOut[idx + 1];
                float inRe = noisyTarget[idx];
                float inIm = noisyTarget[idx + 1];

                float outMag = MathF.Sqrt(outRe * outRe + outIm * outIm);
                float inMag = MathF.Sqrt(inRe * inRe + inIm * inIm);

                if (inMag > 1e-10f)
                {
                    float gain = outMag / inMag;
                    if (gain < minGain)
                    {
                        // Limit gain while preserving original phase
                        // Output = input * minGain (uses input phase, limited magnitude)
                        _specOut[idx] = inRe * minGain;
                        _specOut[idx + 1] = inIm * minGain;
                    }
                }
            }
        }

        // Calculate actual GR from spectral energy difference (after all processing)
        if (stages.applyGains || stages.applyGainZeros)
        {
            float inputEnergy = 0f;
            float outputEnergy = 0f;
            for (int i = 0; i < _specOut.Length; i++)
            {
                inputEnergy += noisyTarget[i] * noisyTarget[i];
                outputEnergy += _specOut[i] * _specOut[i];
            }

            if (inputEnergy > 1e-12f && outputEnergy < inputEnergy)
            {
                float energyRatio = outputEnergy / inputEnergy;
                LastGainReductionDb = -10f * MathF.Log10(energyRatio + 1e-10f);
            }
            else
            {
                LastGainReductionDb = 0f;
            }
        }
        else
        {
            LastGainReductionDb = 0f;
        }

        _stft.Synthesize(_specOut, output);
    }

    public void Dispose()
    {
        _inference.Dispose();
    }

    private void ComputeFeatures()
    {
        DeepFilterNetDsp.ComputeBandCorr(_erbFeatures, _specBuffer, _erbBands);
        for (int i = 0; i < _erbFeatures.Length; i++)
        {
            _erbFeatures[i] = MathF.Log10(_erbFeatures[i] + 1e-10f) * 10f;
        }
        DeepFilterNetDsp.BandMeanNormErb(_erbFeatures, _meanNormState, _alpha);
        DeepFilterNetDsp.BandUnitNorm(_specBuffer, _config.NbDf, _unitNormState, _alpha, _specFeatures);
    }

    private (bool applyGains, bool applyGainZeros, bool applyDf) ApplyStages(float lsnr)
    {
        if (lsnr < MinDbThresh)
        {
            return (false, true, false);
        }
        if (lsnr > MaxDbErbThresh)
        {
            return (false, false, false);
        }
        if (lsnr > MaxDbDfThresh)
        {
            return (true, false, false);
        }
        return (true, false, true);
    }

    private void PushHistory(float[] spec)
    {
        _historyIndex = (_historyIndex + 1) % _historySize;
        Array.Copy(spec, _noisyHistory[_historyIndex], _specLength);
        Array.Copy(spec, _enhHistory[_historyIndex], _specLength);
        _framesProcessed++;
    }

    private int GetHistoryIndex(int framesBack)
    {
        int idx = _historyIndex - framesBack;
        while (idx < 0)
        {
            idx += _historySize;
        }
        return idx % _historySize;
    }

    private void ApplyDeepFilter(float[] specOut, int targetIndex)
    {
        int dfOrder = _config.DfOrder;
        int nbDf = _config.NbDf;
        int historyStart = targetIndex - (dfOrder - 1);
        for (int bin = 0; bin < nbDf; bin++)
        {
            float outRe = 0f;
            float outIm = 0f;
            for (int tap = 0; tap < dfOrder; tap++)
            {
                int frameIndex = historyStart + tap;
                while (frameIndex < 0)
                {
                    frameIndex += _historySize;
                }
                frameIndex %= _historySize;

                float[] spec = _noisyHistory[frameIndex];
                int specIdx = bin * 2;
                float sRe = spec[specIdx];
                float sIm = spec[specIdx + 1];

                int coefIdx = (bin * dfOrder + tap) * 2;
                float cRe = _coefs[coefIdx];
                float cIm = _coefs[coefIdx + 1];

                outRe += sRe * cRe - sIm * cIm;
                outIm += sRe * cIm + sIm * cRe;
            }

            int outIdx = bin * 2;
            specOut[outIdx] = outRe;
            specOut[outIdx + 1] = outIm;
        }
    }

    private static float[] BuildInitState(int size, float min, float max)
    {
        var state = new float[size];
        float step = (max - min) / Math.Max(1, size - 1);
        for (int i = 0; i < size; i++)
        {
            state[i] = min + i * step;
        }
        return state;
    }

    private static float[][] AllocateHistory(int frames, int specLength)
    {
        var history = new float[frames][];
        for (int i = 0; i < frames; i++)
        {
            history[i] = new float[specLength];
        }
        return history;
    }

    private static void ClearHistory(float[][] history)
    {
        for (int i = 0; i < history.Length; i++)
        {
            Array.Clear(history[i], 0, history[i].Length);
        }
    }

}
