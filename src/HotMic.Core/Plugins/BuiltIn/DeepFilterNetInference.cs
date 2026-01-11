using System;
using System.Collections.Generic;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace HotMic.Core.Plugins.BuiltIn;

internal sealed class DeepFilterNetInference : IDisposable
{
    private readonly InferenceSession _encSession;
    private readonly InferenceSession _erbSession;
    private readonly InferenceSession _dfSession;

    private readonly string _encInputErb;
    private readonly string _encInputSpec;
    private readonly string _encOutputEmb;
    private readonly string _encOutputC0;
    private readonly string _encOutputE0;
    private readonly string _encOutputE1;
    private readonly string _encOutputE2;
    private readonly string _encOutputE3;
    private readonly string _encOutputLsnr;

    private readonly string _erbInputEmb;
    private readonly string _erbInputE0;
    private readonly string _erbInputE1;
    private readonly string _erbInputE2;
    private readonly string _erbInputE3;
    private readonly string _erbOutputMask;

    private readonly string _dfInputEmb;
    private readonly string _dfInputC0;
    private readonly string _dfOutputCoefs;

    private readonly int _nbErb;
    private readonly int _nbDf;

    public DeepFilterNetInference(string encPath, string erbPath, string dfPath, int nbErb, int nbDf)
    {
        _nbErb = nbErb;
        _nbDf = nbDf;

        var options = new SessionOptions
        {
            GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL,
            ExecutionMode = ExecutionMode.ORT_SEQUENTIAL,
            InterOpNumThreads = 1,
            IntraOpNumThreads = 2
        };

        _encSession = new InferenceSession(encPath, options);
        _erbSession = new InferenceSession(erbPath, options);
        _dfSession = new InferenceSession(dfPath, options);

        _encInputErb = RequireName(_encSession.InputMetadata, "feat_erb");
        _encInputSpec = RequireName(_encSession.InputMetadata, "feat_spec");
        _encOutputEmb = RequireName(_encSession.OutputMetadata, "emb");
        _encOutputC0 = RequireName(_encSession.OutputMetadata, "c0");
        _encOutputE0 = RequireName(_encSession.OutputMetadata, "e0");
        _encOutputE1 = RequireName(_encSession.OutputMetadata, "e1");
        _encOutputE2 = RequireName(_encSession.OutputMetadata, "e2");
        _encOutputE3 = RequireName(_encSession.OutputMetadata, "e3");
        _encOutputLsnr = RequireName(_encSession.OutputMetadata, "lsnr");

        _erbInputEmb = RequireName(_erbSession.InputMetadata, "emb");
        _erbInputE0 = RequireName(_erbSession.InputMetadata, "e0");
        _erbInputE1 = RequireName(_erbSession.InputMetadata, "e1");
        _erbInputE2 = RequireName(_erbSession.InputMetadata, "e2");
        _erbInputE3 = RequireName(_erbSession.InputMetadata, "e3");
        _erbOutputMask = RequireName(_erbSession.OutputMetadata, "m");

        _dfInputEmb = RequireName(_dfSession.InputMetadata, "emb");
        _dfInputC0 = RequireName(_dfSession.InputMetadata, "c0");
        _dfOutputCoefs = RequireName(_dfSession.OutputMetadata, "coefs");
    }

    public EncoderOutput RunEncoder(float[] erbFeatures, float[] specFeatures)
    {
        var erbTensor = new DenseTensor<float>(erbFeatures, new[] { 1, 1, 1, _nbErb });
        var specTensor = new DenseTensor<float>(specFeatures, new[] { 1, 2, 1, _nbDf });

        var inputs = new List<NamedOnnxValue>
        {
            NamedOnnxValue.CreateFromTensor(_encInputErb, erbTensor),
            NamedOnnxValue.CreateFromTensor(_encInputSpec, specTensor)
        };

        var results = _encSession.Run(inputs);
        foreach (var input in inputs)
        {
            input.Dispose();
        }

        return new EncoderOutput(results,
            GetTensor(results, _encOutputEmb),
            GetTensor(results, _encOutputC0),
            GetTensor(results, _encOutputE0),
            GetTensor(results, _encOutputE1),
            GetTensor(results, _encOutputE2),
            GetTensor(results, _encOutputE3),
            GetScalar(results, _encOutputLsnr));
    }

    public void FillGains(EncoderOutput enc, float[] gainsOut)
    {
        var inputs = new List<NamedOnnxValue>
        {
            NamedOnnxValue.CreateFromTensor(_erbInputEmb, enc.Emb),
            NamedOnnxValue.CreateFromTensor(_erbInputE3, enc.E3),
            NamedOnnxValue.CreateFromTensor(_erbInputE2, enc.E2),
            NamedOnnxValue.CreateFromTensor(_erbInputE1, enc.E1),
            NamedOnnxValue.CreateFromTensor(_erbInputE0, enc.E0)
        };

        using var results = _erbSession.Run(inputs);
        foreach (var input in inputs)
        {
            input.Dispose();
        }

        var mask = GetTensor(results, _erbOutputMask);
        CopyTensorTo(gainsOut, mask);
    }

    public void FillCoefs(EncoderOutput enc, float[] coefsOut)
    {
        var inputs = new List<NamedOnnxValue>
        {
            NamedOnnxValue.CreateFromTensor(_dfInputEmb, enc.Emb),
            NamedOnnxValue.CreateFromTensor(_dfInputC0, enc.C0)
        };

        using var results = _dfSession.Run(inputs);
        foreach (var input in inputs)
        {
            input.Dispose();
        }

        var coefs = GetTensor(results, _dfOutputCoefs);
        CopyTensorTo(coefsOut, coefs);
    }

    public void Dispose()
    {
        _encSession.Dispose();
        _erbSession.Dispose();
        _dfSession.Dispose();
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

    private static float GetScalar(IDisposableReadOnlyCollection<DisposableNamedOnnxValue> results, string name)
    {
        var tensor = GetTensor(results, name);
        return tensor.Buffer.Span[0];
    }

    private static void CopyTensorTo(float[] destination, Tensor<float> tensor)
    {
        var span = tensor.Buffer.Span;
        int count = Math.Min(destination.Length, span.Length);
        span[..count].CopyTo(destination);
        if (count < destination.Length)
        {
            Array.Clear(destination, count, destination.Length - count);
        }
    }

    internal sealed class EncoderOutput : IDisposable
    {
        private readonly IDisposableReadOnlyCollection<DisposableNamedOnnxValue> _results;

        public EncoderOutput(
            IDisposableReadOnlyCollection<DisposableNamedOnnxValue> results,
            Tensor<float> emb,
            Tensor<float> c0,
            Tensor<float> e0,
            Tensor<float> e1,
            Tensor<float> e2,
            Tensor<float> e3,
            float lsnr)
        {
            _results = results;
            Emb = emb;
            C0 = c0;
            E0 = e0;
            E1 = e1;
            E2 = e2;
            E3 = e3;
            Lsnr = lsnr;
        }

        public Tensor<float> Emb { get; }
        public Tensor<float> C0 { get; }
        public Tensor<float> E0 { get; }
        public Tensor<float> E1 { get; }
        public Tensor<float> E2 { get; }
        public Tensor<float> E3 { get; }
        public float Lsnr { get; }

        public void Dispose()
        {
            _results.Dispose();
        }
    }
}
