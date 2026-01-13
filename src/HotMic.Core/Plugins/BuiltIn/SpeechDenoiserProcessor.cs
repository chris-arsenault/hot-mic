using System;
using System.Collections.Generic;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace HotMic.Core.Plugins.BuiltIn;

internal sealed class SpeechDenoiserProcessor : IDeepFilterNetProcessor
{
    private const int DefaultHopSize = 480;
    private const int DefaultStateSize = 45304;

    private readonly InferenceSession _session;
    private readonly string _inputFrameName;
    private readonly string _stateName;
    private readonly string _attenName;
    private readonly string _outputFrameName;
    private readonly string _outputStateName;
    private readonly string _outputLsnrName;
    private readonly int _hopSize;
    private readonly int _latencySamples;
    private readonly int _stateSize;
    private readonly int[] _inputFrameDims;
    private readonly int[] _stateDims;
    private readonly int[] _attenDims;
    private readonly float[] _state;
    private readonly float[] _atten;
    private readonly float[] _inputFrame;
    private readonly DenseTensor<float> _inputTensor;
    private readonly DenseTensor<float> _stateTensor;
    private readonly DenseTensor<float> _attenTensor;
    private int _framesProcessed;
    private float _lastGainReductionDb;
    private float _lastLsnrDb;

    public SpeechDenoiserProcessor(string modelPath)
    {
        var options = new SessionOptions
        {
            GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL,
            ExecutionMode = ExecutionMode.ORT_SEQUENTIAL,
            InterOpNumThreads = 1,
            IntraOpNumThreads = 2
        };

        _session = new InferenceSession(modelPath, options);

        _inputFrameName = RequireName(_session.InputMetadata, "input_frame");
        _stateName = RequireName(_session.InputMetadata, "states");
        _attenName = RequireName(_session.InputMetadata, "atten_lim_db");
        _outputFrameName = RequireName(_session.OutputMetadata, "enhanced_audio_frame");
        _outputStateName = RequireName(_session.OutputMetadata, "new_states");
        _outputLsnrName = RequireName(_session.OutputMetadata, "lsnr");

        _hopSize = ResolveLength(_session.InputMetadata[_inputFrameName], DefaultHopSize);
        _stateSize = ResolveLength(_session.InputMetadata[_stateName], DefaultStateSize);
        _inputFrameDims = ResolveDims(_session.InputMetadata[_inputFrameName], _hopSize);
        _stateDims = ResolveDims(_session.InputMetadata[_stateName], _stateSize);
        _attenDims = ResolveDims(_session.InputMetadata[_attenName], 1);

        _inputFrame = new float[_hopSize];
        _state = new float[_stateSize];
        _atten = new float[1];
        _inputTensor = new DenseTensor<float>(_inputFrame, _inputFrameDims);
        _stateTensor = new DenseTensor<float>(_state, _stateDims);
        _attenTensor = new DenseTensor<float>(_atten, _attenDims);

        _latencySamples = _hopSize;
    }

    public int HopSize => _hopSize;

    public int LatencySamples => _latencySamples;

    public float LastGainReductionDb => _lastGainReductionDb;

    public float LastLsnrDb => _lastLsnrDb;

    public float LastMaskMin => 0f;

    public float LastMaskMean => 0f;

    public float LastMaskMax => 0f;

    public bool LastApplyGains => false;

    public bool LastApplyGainZeros => false;

    public bool LastApplyDf => false;

    public void Reset()
    {
        Array.Clear(_state, 0, _state.Length);
        _framesProcessed = 0;
        _lastGainReductionDb = 0f;
        _lastLsnrDb = 0f;
    }

    public void ProcessHop(
        ReadOnlySpan<float> input,
        Span<float> output,
        bool postFilterEnabled,
        float attenLimitDb)
    {
        if (input.Length != _hopSize || output.Length != _hopSize)
        {
            throw new ArgumentException("SpeechDenoiser hop size mismatch.");
        }

        _ = postFilterEnabled;

        input.CopyTo(_inputFrame);
        _ = attenLimitDb;
        // Match SpeechDenoiser reference: atten_lim_db is fixed at 0.
        _atten[0] = 0f;

        using var inputs = new List<NamedOnnxValue>(3)
        {
            NamedOnnxValue.CreateFromTensor(_inputFrameName, _inputTensor),
            NamedOnnxValue.CreateFromTensor(_stateName, _stateTensor),
            NamedOnnxValue.CreateFromTensor(_attenName, _attenTensor)
        };

        using var results = _session.Run(inputs);

        var enhanced = GetTensor(results, _outputFrameName);
        var newStates = GetTensor(results, _outputStateName);
        var lsnr = GetTensor(results, _outputLsnrName);

        CopyTensorToSpan(enhanced, output);
        CopyTensorToArray(newStates, _state);
        _lastLsnrDb = GetLastValue(lsnr);

        _framesProcessed++;
        if (_framesProcessed <= 1)
        {
            output.Clear();
            _lastGainReductionDb = 0f;
            return;
        }

        float inputEnergy = 0f;
        float outputEnergy = 0f;
        for (int i = 0; i < input.Length; i++)
        {
            float inSample = input[i];
            float outSample = output[i];
            inputEnergy += inSample * inSample;
            outputEnergy += outSample * outSample;
        }

        if (inputEnergy > 1e-10f && outputEnergy < inputEnergy)
        {
            float ratio = outputEnergy / inputEnergy;
            _lastGainReductionDb = -10f * MathF.Log10(ratio + 1e-10f);
        }
        else
        {
            _lastGainReductionDb = 0f;
        }
    }

    public void Dispose()
    {
        _session.Dispose();
    }

    private static string RequireName(IReadOnlyDictionary<string, NodeMetadata> metadata, params string[] candidates)
    {
        foreach (var candidate in candidates)
        {
            if (metadata.ContainsKey(candidate))
            {
                return candidate;
            }
        }

        foreach (var key in metadata.Keys)
        {
            return key;
        }

        throw new InvalidOperationException("ONNX metadata missing required names.");
    }

    private static int ResolveLength(NodeMetadata metadata, int fallback)
    {
        long product = 1;
        foreach (long dim in metadata.Dimensions)
        {
            if (dim <= 0)
            {
                return fallback;
            }
            product *= dim;
            if (product > int.MaxValue)
            {
                return fallback;
            }
        }

        return (int)product;
    }

    private static int[] ResolveDims(NodeMetadata metadata, int fallbackLength)
    {
        if (metadata.Dimensions.Count == 0)
        {
            return new[] { fallbackLength };
        }

        int dynamicCount = 0;
        int dynamicIndex = -1;
        long product = 1;
        var dims = new int[metadata.Dimensions.Count];
        for (int i = 0; i < metadata.Dimensions.Count; i++)
        {
            long dim = metadata.Dimensions[i];
            if (dim <= 0)
            {
                dynamicCount++;
                dynamicIndex = i;
                dims[i] = 1;
            }
            else
            {
                if (dim > int.MaxValue)
                {
                    return new[] { fallbackLength };
                }
                dims[i] = (int)dim;
                product *= dim;
            }
        }

        if (dynamicCount == 0)
        {
            return dims;
        }

        if (dynamicCount == 1 && product > 0)
        {
            long remaining = fallbackLength / product;
            dims[dynamicIndex] = (int)Math.Max(1, remaining);
            return dims;
        }

        return new[] { fallbackLength };
    }

    private static Tensor<float> GetTensor(IDisposableReadOnlyCollection<DisposableNamedOnnxValue> results, string name)
    {
        foreach (var item in results)
        {
            if (item.Name == name)
            {
                return item.AsTensor<float>();
            }
        }

        throw new InvalidOperationException($"ONNX output '{name}' not found.");
    }

    private static void CopyTensorToSpan(Tensor<float> tensor, Span<float> destination)
    {
        if (tensor is DenseTensor<float> dense)
        {
            var span = dense.Buffer.Span;
            int copyCount = Math.Min(destination.Length, span.Length);
            span.Slice(0, copyCount).CopyTo(destination);
            if (copyCount < destination.Length)
            {
                destination.Slice(copyCount).Clear();
            }
            return;
        }

        int index = 0;
        foreach (var value in tensor)
        {
            if (index >= destination.Length)
            {
                break;
            }
            destination[index++] = value;
        }
        if (index < destination.Length)
        {
            destination.Slice(index).Clear();
        }
    }

    private static void CopyTensorToArray(Tensor<float> tensor, float[] destination)
    {
        if (tensor is DenseTensor<float> dense)
        {
            var span = dense.Buffer.Span;
            int copyCount = Math.Min(destination.Length, span.Length);
            span.Slice(0, copyCount).CopyTo(destination);
            if (copyCount < destination.Length)
            {
                Array.Clear(destination, copyCount, destination.Length - copyCount);
            }
            return;
        }

        int index = 0;
        foreach (var value in tensor)
        {
            if (index >= destination.Length)
            {
                break;
            }
            destination[index++] = value;
        }
        if (index < destination.Length)
        {
            Array.Clear(destination, index, destination.Length - index);
        }
    }

    private static float GetLastValue(Tensor<float> tensor)
    {
        if (tensor is DenseTensor<float> dense)
        {
            var span = dense.Buffer.Span;
            return span.Length > 0 ? span[^1] : 0f;
        }

        float last = 0f;
        foreach (var value in tensor)
        {
            last = value;
        }
        return last;
    }
}
