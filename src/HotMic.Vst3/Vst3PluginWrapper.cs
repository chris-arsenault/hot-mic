using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using HotMic.Core.Plugins;
using Jacobi.Vst.Core;
using Jacobi.Vst.Interop.Host;

namespace HotMic.Vst3;

public sealed class Vst3PluginWrapper : IContextualPlugin
{
    private readonly Vst3PluginInfo _info;
    private VstPluginContext? _context;
    private object? _commandStub;
    private Action? _open;
    private Action? _close;
    private Action<float>? _setSampleRate;
    private Action<int>? _setBlockSize;
    private Action<bool>? _mainsChanged;
    private ProcessReplacingDelegate? _processReplacing;
    private GetChunkDelegate? _getChunk;
    private SetChunkDelegate? _setChunk;
    private GetParameterDelegate? _getParameter;
    private SetParameterDelegate? _setParameter;
    private GetParameterTextDelegate? _getParameterName;
    private GetParameterTextDelegate? _getParameterLabel;
    private GetParameterTextDelegate? _getParameterDisplay;
    private EditorGetRectDelegate? _editorGetRect;
    private EditorOpenDelegate? _editorOpenDelegate;
    private Action? _editorClose;
    private Action? _editorIdle;
    private Vst3PluginHost? _host;
    private VstAudioBufferManager? _inputBufferManager;
    private VstAudioBufferManager? _outputBufferManager;
    private VstAudioBuffer[] _inputBuffers = Array.Empty<VstAudioBuffer>();
    private VstAudioBuffer[] _outputBuffers = Array.Empty<VstAudioBuffer>();
    private PluginParameter[] _parameters = Array.Empty<PluginParameter>();
    private int _blockSize;
    private int _inputChannels;
    private int _outputChannels;
    private int _isFaulted;
    private bool _editorOpen;

    public Vst3PluginWrapper(Vst3PluginInfo info)
    {
        _info = info;
    }

    public string Id => _info.Format == VstPluginFormat.Vst2
        ? $"vst2:{_info.Path}"
        : $"vst3:{_info.Path}";

    public string Name => _info.Name;

    public bool IsBypassed { get; set; }

    public int LatencySamples => 0;

    public IReadOnlyList<PluginParameter> Parameters => _parameters;

    public bool IsFaulted => Volatile.Read(ref _isFaulted) == 1;

    public void Initialize(int sampleRate, int blockSize)
    {
        _blockSize = blockSize;
        _host = new Vst3PluginHost(sampleRate, blockSize, Path.GetDirectoryName(_info.Path) ?? string.Empty);
        _context = VstPluginContext.Create(_info.Path, _host);
        _commandStub = _context.PluginCommandStub;
        if (_commandStub is null)
        {
            throw new InvalidOperationException("VST3 plugin command stub is not available.");
        }

        _open = CreateDelegate<Action>(_commandStub, "Open");
        _close = CreateDelegate<Action>(_commandStub, "Close");
        _setSampleRate = CreateDelegate<Action<float>>(_commandStub, "SetSampleRate");
        _setBlockSize = CreateDelegate<Action<int>>(_commandStub, "SetBlockSize");
        _mainsChanged = CreateDelegate<Action<bool>>(_commandStub, "MainsChanged");
        _processReplacing = CreateDelegate<ProcessReplacingDelegate>(
            _commandStub,
            "ProcessReplacing",
            typeof(VstAudioBuffer[]),
            typeof(VstAudioBuffer[]));
        _getChunk = CreateDelegate<GetChunkDelegate>(_commandStub, "GetChunk");
        _setChunk = CreateDelegate<SetChunkDelegate>(_commandStub, "SetChunk");
        _getParameter = CreateDelegate<GetParameterDelegate>(_commandStub, "GetParameter");
        _setParameter = CreateDelegate<SetParameterDelegate>(_commandStub, "SetParameter");
        _getParameterName = CreateDelegate<GetParameterTextDelegate>(_commandStub, "GetParameterName");
        _getParameterLabel = CreateDelegate<GetParameterTextDelegate>(_commandStub, "GetParameterLabel");
        _getParameterDisplay = CreateDelegate<GetParameterTextDelegate>(_commandStub, "GetParameterDisplay");
        _editorGetRect = CreateDelegate<EditorGetRectDelegate>(_commandStub, "EditorGetRect");
        _editorOpenDelegate = CreateDelegate<EditorOpenDelegate>(_commandStub, "EditorOpen");
        _editorClose = CreateDelegate<Action>(_commandStub, "EditorClose");
        _editorIdle = CreateDelegate<Action>(_commandStub, "EditorIdle");

        _open();
        _setSampleRate(sampleRate);
        _setBlockSize(blockSize);
        _mainsChanged(true);

        _inputChannels = Math.Max(1, _context.PluginInfo.AudioInputCount);
        _outputChannels = Math.Max(1, _context.PluginInfo.AudioOutputCount);

        _inputBufferManager = new VstAudioBufferManager(_inputChannels, blockSize);
        _outputBufferManager = new VstAudioBufferManager(_outputChannels, blockSize);
        _inputBuffers = GetBuffers(_inputBufferManager);
        _outputBuffers = GetBuffers(_outputBufferManager);
        _parameters = BuildParameters();
    }

    public void Process(Span<float> buffer, in PluginProcessContext context)
    {
        Process(buffer);
    }

    public void Process(Span<float> buffer)
    {
        if (IsBypassed || _processReplacing is null || IsFaulted)
        {
            return;
        }

        int frames = Math.Min(buffer.Length, _blockSize);
        if (frames <= 0)
        {
            return;
        }

        try
        {
            _outputBufferManager?.ClearAllBuffers();
            if (_inputBuffers.Length == 0 || _outputBuffers.Length == 0)
            {
                return;
            }

            if (_inputChannels == 1)
            {
                CopyToBuffer(buffer, _inputBuffers[0], frames);
            }
            else
            {
                for (int channel = 0; channel < _inputChannels; channel++)
                {
                    CopyToBuffer(buffer, _inputBuffers[channel], frames);
                }
            }

            _processReplacing(_inputBuffers, _outputBuffers);

            if (_outputChannels == 1)
            {
                CopyFromBuffer(_outputBuffers[0], buffer, frames);
            }
            else
            {
                for (int i = 0; i < frames; i++)
                {
                    float sum = 0f;
                    for (int channel = 0; channel < _outputChannels; channel++)
                    {
                        sum += _outputBuffers[channel][i];
                    }

                    buffer[i] = sum / _outputChannels;
                }
            }

            if (frames < buffer.Length)
            {
                buffer.Slice(frames).Clear();
            }

            _host?.AdvanceSamples(frames);
        }
        catch
        {
            IsBypassed = true;
            Interlocked.Exchange(ref _isFaulted, 1);
        }
    }

    private static void CopyToBuffer(ReadOnlySpan<float> source, VstAudioBuffer destination, int frames)
    {
        int sampleCount = destination.SampleCount;
        int copyCount = frames <= sampleCount ? frames : sampleCount;
        for (int i = 0; i < copyCount; i++)
        {
            destination[i] = source[i];
        }

        for (int i = copyCount; i < sampleCount; i++)
        {
            destination[i] = 0f;
        }
    }

    private static void CopyFromBuffer(VstAudioBuffer source, Span<float> destination, int frames)
    {
        int sampleCount = source.SampleCount;
        int copyCount = frames <= sampleCount ? frames : sampleCount;
        for (int i = 0; i < copyCount; i++)
        {
            destination[i] = source[i];
        }
    }

    public void SetParameter(int index, float value)
    {
        _setParameter?.Invoke(index, Math.Clamp(value, 0f, 1f));
    }

    public byte[] GetState()
    {
        if (_getChunk is null)
        {
            return Array.Empty<byte>();
        }

        var data = _getChunk(false);
        if (data is not null && data.Length > 0)
        {
            return data;
        }

        return GetParameterState();
    }

    public void SetState(byte[] state)
    {
        if (_setChunk is null || state.Length == 0)
        {
            return;
        }

        int read = _setChunk(state, false);
        if (read <= 0)
        {
            ApplyParameterState(state);
        }
    }

    public bool TryGetEditorRect(out Rectangle rect)
    {
        rect = Rectangle.Empty;
        return _editorGetRect is not null && _editorGetRect(out rect);
    }

    public bool OpenEditor(IntPtr parentHandle)
    {
        if (_editorOpenDelegate is null)
        {
            return false;
        }

        _editorOpen = _editorOpenDelegate(parentHandle);
        return _editorOpen;
    }

    public void CloseEditor()
    {
        if (_editorClose is null || !_editorOpen)
        {
            return;
        }

        _editorClose();
        _editorOpen = false;
    }

    public void EditorIdle()
    {
        if (_editorIdle is null || !_editorOpen)
        {
            return;
        }

        _editorIdle();
    }

    public void Dispose()
    {
        CloseEditor();
        if (_mainsChanged is not null && _close is not null)
        {
            _mainsChanged(false);
            _close();
        }

        _inputBufferManager?.Dispose();
        _outputBufferManager?.Dispose();
        _context?.Dispose();
    }

    private PluginParameter[] BuildParameters()
    {
        if (_context is null || _getParameter is null || _getParameterName is null || _getParameterLabel is null || _getParameterDisplay is null)
        {
            return Array.Empty<PluginParameter>();
        }

        int count = _context.PluginInfo.ParameterCount;
        if (count <= 0)
        {
            return Array.Empty<PluginParameter>();
        }

        var parameters = new PluginParameter[count];
        for (int i = 0; i < count; i++)
        {
            int index = i;
            string name = _getParameterName(index) ?? string.Empty;
            string label = _getParameterLabel(index) ?? string.Empty;
            float defaultValue = _getParameter(index);
            parameters[index] = new PluginParameter
            {
                Index = index,
                Name = string.IsNullOrWhiteSpace(name) ? $"Param {index + 1}" : name,
                MinValue = 0f,
                MaxValue = 1f,
                DefaultValue = defaultValue,
                Unit = label,
                FormatValue = _ => _getParameterDisplay(index) ?? string.Empty
            };
        }

        return parameters;
    }

    private byte[] GetParameterState()
    {
        if (_getParameter is null || _parameters.Length == 0)
        {
            return Array.Empty<byte>();
        }

        var bytes = new byte[_parameters.Length * sizeof(float)];
        for (int i = 0; i < _parameters.Length; i++)
        {
            float value = _getParameter(i);
            Buffer.BlockCopy(BitConverter.GetBytes(value), 0, bytes, i * sizeof(float), sizeof(float));
        }

        return bytes;
    }

    private void ApplyParameterState(byte[] state)
    {
        if (_setParameter is null || _parameters.Length == 0)
        {
            return;
        }

        int count = Math.Min(_parameters.Length, state.Length / sizeof(float));
        for (int i = 0; i < count; i++)
        {
            float value = BitConverter.ToSingle(state, i * sizeof(float));
            _setParameter(i, Math.Clamp(value, 0f, 1f));
        }
    }

    private static T CreateDelegate<T>(object target, string methodName, params Type[] parameterTypes) where T : Delegate
    {
        var method = GetMethod(target, methodName, parameterTypes);
        if (method is null)
        {
            throw new InvalidOperationException($"VST3 plugin command stub is missing {methodName}.");
        }

        try
        {
            return (T)method.CreateDelegate(typeof(T), target);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to bind VST3 method {methodName}.", ex);
        }
    }

    private static MethodInfo? GetMethod(object target, string methodName, Type[] parameterTypes)
    {
        var methods = target.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .Where(method => method.Name == methodName);

        if (parameterTypes.Length > 0)
        {
            methods = methods.Where(method =>
            {
                var parameters = method.GetParameters();
                if (parameters.Length != parameterTypes.Length)
                {
                    return false;
                }

                for (int i = 0; i < parameters.Length; i++)
                {
                    if (parameters[i].ParameterType != parameterTypes[i])
                    {
                        return false;
                    }
                }

                return true;
            });
        }

        MethodInfo? match = null;
        foreach (var method in methods)
        {
            if (match is not null)
            {
                throw new InvalidOperationException($"VST3 plugin command stub has multiple matches for {methodName}.");
            }

            match = method;
        }

        return match;
    }

    private static VstAudioBuffer[] GetBuffers(VstAudioBufferManager manager)
    {
        var buffers = new VstAudioBuffer[manager.BufferCount];
        int index = 0;
        foreach (VstAudioBuffer buffer in manager)
        {
            if (index >= buffers.Length)
            {
                break;
            }

            buffers[index++] = buffer;
        }

        return buffers;
    }

    private delegate void ProcessReplacingDelegate(VstAudioBuffer[] inputs, VstAudioBuffer[] outputs);

    private delegate byte[]? GetChunkDelegate(bool isPreset);

    private delegate int SetChunkDelegate(byte[] data, bool isPreset);

    private delegate float GetParameterDelegate(int index);

    private delegate void SetParameterDelegate(int index, float value);

    private delegate string? GetParameterTextDelegate(int index);

    private delegate bool EditorGetRectDelegate(out Rectangle rect);

    private delegate bool EditorOpenDelegate(IntPtr parentHandle);
}
