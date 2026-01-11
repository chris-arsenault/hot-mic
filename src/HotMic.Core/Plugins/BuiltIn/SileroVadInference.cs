using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace HotMic.Core.Plugins.BuiltIn;

internal sealed class SileroVadInference : IDisposable
{
    private const int DefaultFrameSize = 512;

    private readonly InferenceSession _session;
    private readonly string _inputName;
    private readonly string? _sampleRateName;
    private readonly Type? _sampleRateType;
    private readonly int[]? _sampleRateShape;
    private readonly string? _stateHName;
    private readonly string? _stateCName;
    private readonly string _outputName;
    private readonly string? _outputHName;
    private readonly string? _outputCName;
    private readonly int _frameSize;
    private readonly int[] _inputShape;
    private readonly int[]? _stateShape;

    private readonly float[] _inputBuffer;
    private readonly float[]? _stateH;
    private readonly float[]? _stateC;
    private readonly DenseTensor<float> _inputTensor;
    private readonly DenseTensor<float>? _stateHTensor;
    private readonly DenseTensor<float>? _stateCTensor;

    public SileroVadInference(string modelPath)
    {
        var options = new SessionOptions();
        options.GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL;
        options.InterOpNumThreads = 1;
        options.IntraOpNumThreads = 1;

        _session = new InferenceSession(modelPath, options);

        (_inputName, _sampleRateName, _sampleRateType, _sampleRateShape, _stateHName, _stateCName, _inputShape, _stateShape, _frameSize) = ResolveInputs();
        (_outputName, _outputHName, _outputCName) = ResolveOutputs();

        _inputBuffer = new float[_frameSize];
        _inputTensor = new DenseTensor<float>(_inputBuffer, _inputShape);

        if (_stateShape is not null && _stateHName is not null)
        {
            int stateLen = 1;
            for (int i = 0; i < _stateShape.Length; i++)
            {
                stateLen *= Math.Max(1, _stateShape[i]);
            }

            _stateH = new float[stateLen];
            _stateHTensor = new DenseTensor<float>(_stateH, _stateShape);
            if (_stateCName is not null)
            {
                _stateC = new float[stateLen];
                _stateCTensor = new DenseTensor<float>(_stateC, _stateShape);
            }
        }
    }

    public int FrameSize => _frameSize;

    public void ResetState()
    {
        if (_stateH is not null)
        {
            Array.Clear(_stateH, 0, _stateH.Length);
        }
        if (_stateC is not null)
        {
            Array.Clear(_stateC, 0, _stateC.Length);
        }
    }

    public float Process(ReadOnlySpan<float> frame16k)
    {
        if (frame16k.Length != _frameSize)
        {
            throw new ArgumentException("Silero VAD frame size mismatch.");
        }

        frame16k.CopyTo(_inputBuffer);

        var inputs = new List<NamedOnnxValue>(4)
        {
            NamedOnnxValue.CreateFromTensor(_inputName, _inputTensor)
        };

        if (_sampleRateName is not null && _sampleRateType is not null)
        {
            inputs.Add(CreateSampleRateInput(_sampleRateName, _sampleRateType, _sampleRateShape));
        }

        if (_stateHTensor is not null && _stateHName is not null)
        {
            inputs.Add(NamedOnnxValue.CreateFromTensor(_stateHName, _stateHTensor));
        }
        if (_stateCTensor is not null && _stateCName is not null)
        {
            inputs.Add(NamedOnnxValue.CreateFromTensor(_stateCName, _stateCTensor));
        }

        using var results = _session.Run(inputs);

        float prob = ReadScalar(results, _outputName);
        if (_outputHName is not null && _stateH is not null)
        {
            CopyTensor(results, _outputHName, _stateH);
        }
        if (_outputCName is not null && _stateC is not null)
        {
            CopyTensor(results, _outputCName, _stateC);
        }

        return prob;
    }

    public void Dispose()
    {
        _session.Dispose();
    }

    private (string inputName, string? sampleRateName, Type? sampleRateType, int[]? sampleRateShape,
        string? stateHName, string? stateCName, int[] inputShape, int[]? stateShape, int frameSize) ResolveInputs()
    {
        string? inputName = null;
        string? sampleRateName = null;
        Type? sampleRateType = null;
        int[]? sampleRateShape = null;
        string? stateHName = null;
        string? stateCName = null;
        int[]? stateShape = null;
        int[]? inputShape = null;
        int frameSize = DefaultFrameSize;
        var stateCandidates = new List<string>();

        foreach (var entry in _session.InputMetadata)
        {
            string name = entry.Key;
            var meta = entry.Value;
            int[] dims = NormalizeDims(meta.Dimensions);

            if (string.Equals(name, "input", StringComparison.OrdinalIgnoreCase))
            {
                inputName = name;
                inputShape = dims;
                frameSize = ResolveFrameSize(dims);
                continue;
            }

            if (string.Equals(name, "sr", StringComparison.OrdinalIgnoreCase))
            {
                sampleRateName = name;
                sampleRateType = meta.ElementType;
                sampleRateShape = dims;
                continue;
            }

            if (string.Equals(name, "h", StringComparison.OrdinalIgnoreCase))
            {
                stateHName = name;
                stateShape = dims;
                continue;
            }

            if (string.Equals(name, "c", StringComparison.OrdinalIgnoreCase))
            {
                stateCName = name;
                stateShape = dims;
                continue;
            }

            if (IsSampleRateInput(meta, dims))
            {
                sampleRateName ??= name;
                sampleRateType ??= meta.ElementType;
                sampleRateShape ??= dims;
                continue;
            }

            if (IsStateInput(dims))
            {
                stateCandidates.Add(name);
                stateShape ??= dims;
                continue;
            }

            if (IsAudioInput(dims))
            {
                inputName ??= name;
                inputShape ??= dims;
                frameSize = ResolveFrameSize(dims);
            }
        }

        if (inputName is null)
        {
            foreach (var entry in _session.InputMetadata)
            {
                inputName = entry.Key;
                inputShape = NormalizeDims(entry.Value.Dimensions);
                frameSize = ResolveFrameSize(inputShape);
                break;
            }
        }

        if (stateHName is null || stateCName is null)
        {
            if (stateCandidates.Count >= 2)
            {
                stateHName ??= stateCandidates[0];
                stateCName ??= stateCandidates[1];
            }
            else if (stateCandidates.Count == 1)
            {
                stateHName ??= stateCandidates[0];
            }
        }

        if (inputName is null || inputShape is null)
        {
            throw new InvalidOperationException("Silero VAD model missing audio input.");
        }

        inputShape = NormalizeInputShape(inputShape, frameSize);
        if (inputShape.Length != 2)
        {
            // Silero VAD expects [batch, samples].
            inputShape = new[] { 1, frameSize };
        }
        if (sampleRateShape is not null)
        {
            sampleRateShape = NormalizeSampleRateShape(sampleRateShape);
        }
        if (stateShape is not null)
        {
            stateShape = NormalizeStateShape(stateShape);
        }

        return (inputName, sampleRateName, sampleRateType, sampleRateShape, stateHName, stateCName, inputShape, stateShape, frameSize);
    }

    private (string outputName, string? outputHName, string? outputCName) ResolveOutputs()
    {
        string? outputName = null;
        string? outputHName = null;
        string? outputCName = null;

        foreach (var entry in _session.OutputMetadata)
        {
            string name = entry.Key;
            int[] dims = NormalizeDims(entry.Value.Dimensions);

            if (string.Equals(name, "output", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(name, "prob", StringComparison.OrdinalIgnoreCase))
            {
                outputName = name;
                continue;
            }

            if (string.Equals(name, "hn", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(name, "h", StringComparison.OrdinalIgnoreCase))
            {
                outputHName = name;
                continue;
            }

            if (string.Equals(name, "cn", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(name, "c", StringComparison.OrdinalIgnoreCase))
            {
                outputCName = name;
                continue;
            }

            if (IsStateOutput(dims))
            {
                if (outputHName is null)
                {
                    outputHName = name;
                }
                else if (outputCName is null)
                {
                    outputCName = name;
                }
                continue;
            }
        }

        if (outputName is null)
        {
            foreach (var entry in _session.OutputMetadata)
            {
                outputName = entry.Key;
                break;
            }
        }

        if (outputName is null)
        {
            throw new InvalidOperationException("Silero VAD model missing output.");
        }

        return (outputName, outputHName, outputCName);
    }

    private static NamedOnnxValue CreateSampleRateInput(string name, Type type, int[]? shape)
    {
        int[] dims = shape is null || shape.Length == 0 ? Array.Empty<int>() : shape;
        if (type == typeof(long))
        {
            return NamedOnnxValue.CreateFromTensor(name, new DenseTensor<long>(new[] { 16000L }, dims));
        }

        if (type == typeof(int))
        {
            return NamedOnnxValue.CreateFromTensor(name, new DenseTensor<int>(new[] { 16000 }, dims));
        }

        return NamedOnnxValue.CreateFromTensor(name, new DenseTensor<float>(new[] { 16000f }, dims));
    }

    private static float ReadScalar(IDisposableReadOnlyCollection<DisposableNamedOnnxValue> results, string name)
    {
        foreach (var item in results)
        {
            if (item.Name == name)
            {
                var tensor = item.AsTensor<float>();
                foreach (var value in tensor)
                {
                    return value;
                }
                return 0f;
            }
        }

        throw new InvalidOperationException($"Silero VAD output '{name}' not found.");
    }

    private static void CopyTensor(IDisposableReadOnlyCollection<DisposableNamedOnnxValue> results, string name, float[] destination)
    {
        foreach (var item in results)
        {
            if (item.Name != name)
            {
                continue;
            }

            var tensor = item.AsTensor<float>();
            int count = 0;
            foreach (var value in tensor)
            {
                if (count >= destination.Length)
                {
                    break;
                }
                destination[count++] = value;
            }
            if (count < destination.Length)
            {
                Array.Clear(destination, count, destination.Length - count);
            }
            return;
        }
    }

    private static bool IsSampleRateInput(NodeMetadata meta, int[] dims)
    {
        return (meta.ElementType == typeof(long) || meta.ElementType == typeof(int)) &&
               (dims.Length == 0 || dims.Length == 1);
    }

    private static bool IsAudioInput(int[] dims)
    {
        if (dims.Length == 2)
        {
            return dims[1] == 256 || dims[1] == 512 || dims[1] == 768 || dims[1] == -1;
        }
        return dims.Length == 3 && (dims[2] == 256 || dims[2] == 512 || dims[2] == 768 || dims[2] == -1);
    }

    private static bool IsStateInput(int[] dims)
    {
        return (dims.Length == 3 && dims[0] == 2) || (dims.Length == 2 && dims[0] == 2);
    }

    private static bool IsStateOutput(int[] dims)
    {
        return (dims.Length == 3 && dims[0] == 2) || (dims.Length == 2 && dims[0] == 2);
    }

    private static int ResolveFrameSize(int[] dims)
    {
        if (dims.Length >= 2 && dims[1] > 0)
        {
            return dims[1];
        }
        if (dims.Length >= 3 && dims[2] > 0)
        {
            return dims[2];
        }

        return DefaultFrameSize;
    }

    private static int[] NormalizeInputShape(int[] dims, int frameSize)
    {
        if (dims.Length == 0)
        {
            return new[] { 1, frameSize };
        }

        if (dims.Length == 1)
        {
            int d0 = dims[0] > 0 ? dims[0] : frameSize;
            return new[] { 1, d0 };
        }

        if (dims.Length == 2)
        {
            int d0 = dims[0] > 0 ? dims[0] : 1;
            int d1 = dims[1] > 0 ? dims[1] : frameSize;
            return new[] { d0, d1 };
        }

        if (dims.Length != 3)
        {
            return new[] { 1, 1, frameSize };
        }

        int d0 = dims[0] > 0 ? dims[0] : 1;
        int d1 = dims[1] > 0 ? dims[1] : 1;
        int d2 = dims[2] > 0 ? dims[2] : frameSize;
        return new[] { d0, d1, d2 };
    }

    private static int[] NormalizeStateShape(int[] dims)
    {
        if (dims.Length == 2)
        {
            int d0 = dims[0] > 0 ? dims[0] : 2;
            int d1 = dims[1] > 0 ? dims[1] : 64;
            return new[] { d0, d1 };
        }

        if (dims.Length != 3)
        {
            return new[] { 2, 1, 64 };
        }

        int d0 = dims[0] > 0 ? dims[0] : 2;
        int d1 = dims[1] > 0 ? dims[1] : 1;
        int d2 = dims[2] > 0 ? dims[2] : 64;
        return new[] { d0, d1, d2 };
    }

    private static int[] NormalizeSampleRateShape(int[] dims)
    {
        if (dims.Length == 0)
        {
            return Array.Empty<int>();
        }

        if (dims.Length == 1)
        {
            int d0 = dims[0] > 0 ? dims[0] : 1;
            return new[] { d0 };
        }

        var output = new int[dims.Length];
        for (int i = 0; i < dims.Length; i++)
        {
            output[i] = dims[i] > 0 ? dims[i] : 1;
        }

        int total = 1;
        for (int i = 0; i < output.Length; i++)
        {
            total *= output[i];
        }

        if (total != 1)
        {
            return new[] { 1 };
        }

        return output;
    }

    private static int[] NormalizeDims(IEnumerable dims)
    {
        var output = new List<int>();
        foreach (var dim in dims)
        {
            int value;
            if (dim is null)
            {
                value = -1;
            }
            else if (dim is int i)
            {
                value = i;
            }
            else if (dim is long l)
            {
                value = (int)Math.Clamp(l, int.MinValue, int.MaxValue);
            }
            else if (dim is IConvertible convertible)
            {
                try
                {
                    value = Convert.ToInt32(convertible, CultureInfo.InvariantCulture);
                }
                catch (Exception)
                {
                    value = -1;
                }
            }
            else
            {
                value = -1;
            }
            output.Add(value);
        }
        return output.ToArray();
    }
}
