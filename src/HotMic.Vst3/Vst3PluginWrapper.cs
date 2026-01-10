using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using HotMic.Core.Plugins;
using Jacobi.Vst.Core;
using Jacobi.Vst.Core.Host;
using Jacobi.Vst.Host.Interop;

namespace HotMic.Vst3;

public sealed class Vst3PluginWrapper : IPlugin
{
    private readonly Vst3PluginInfo _info;
    private VstPluginContext? _context;
    private IVstPluginCommandStub? _commandStub;
    private IVstPluginCommands24? _commands;
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

    public string Id => $"vst3:{_info.Path}";

    public string Name => _info.Name;

    public bool IsBypassed { get; set; }

    public IReadOnlyList<PluginParameter> Parameters => _parameters;

    public bool IsFaulted => Volatile.Read(ref _isFaulted) == 1;

    public void Initialize(int sampleRate, int blockSize)
    {
        _blockSize = blockSize;
        _host = new Vst3PluginHost(sampleRate, blockSize, Path.GetDirectoryName(_info.Path) ?? string.Empty);
        _context = VstPluginContext.Create(_info.Path, _host);
        _commandStub = _context.PluginCommandStub;
        _commands = _commandStub.Commands;
        if (_commands is null)
        {
            throw new InvalidOperationException("VST3 plugin command interface is not available.");
        }

        _commands.Open();
        _commands.SetSampleRate(sampleRate);
        _commands.SetBlockSize(blockSize);
        _commands.MainsChanged(true);

        _inputChannels = Math.Max(1, _context.PluginInfo.AudioInputCount);
        _outputChannels = Math.Max(1, _context.PluginInfo.AudioOutputCount);

        _inputBufferManager = new VstAudioBufferManager(_inputChannels, blockSize);
        _outputBufferManager = new VstAudioBufferManager(_outputChannels, blockSize);
        _inputBuffers = _inputBufferManager.Buffers.ToArray();
        _outputBuffers = _outputBufferManager.Buffers.ToArray();
        _parameters = BuildParameters();
    }

    public void Process(Span<float> buffer)
    {
        if (IsBypassed || _commands is null || IsFaulted)
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
                var inputSpan = _inputBuffers[0].AsSpan();
                buffer.Slice(0, frames).CopyTo(inputSpan);
                if (frames < inputSpan.Length)
                {
                    inputSpan.Slice(frames).Clear();
                }
            }
            else
            {
                for (int channel = 0; channel < _inputChannels; channel++)
                {
                    var inputSpan = _inputBuffers[channel].AsSpan();
                    buffer.Slice(0, frames).CopyTo(inputSpan);
                    if (frames < inputSpan.Length)
                    {
                        inputSpan.Slice(frames).Clear();
                    }
                }
            }

            _commands.ProcessReplacing(_inputBuffers, _outputBuffers);

            if (_outputChannels == 1)
            {
                _outputBuffers[0].AsReadOnlySpan().Slice(0, frames).CopyTo(buffer);
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

    public void SetParameter(int index, float value)
    {
        _commands?.SetParameter(index, Math.Clamp(value, 0f, 1f));
    }

    public byte[] GetState()
    {
        if (_commands is null)
        {
            return Array.Empty<byte>();
        }

        var data = _commands.GetChunk(false);
        if (data is not null && data.Length > 0)
        {
            return data;
        }

        return GetParameterState();
    }

    public void SetState(byte[] state)
    {
        if (_commands is null || state.Length == 0)
        {
            return;
        }

        int read = _commands.SetChunk(state, false);
        if (read <= 0)
        {
            ApplyParameterState(state);
        }
    }

    public bool TryGetEditorRect(out Rectangle rect)
    {
        rect = Rectangle.Empty;
        return _commands is not null && _commands.EditorGetRect(out rect);
    }

    public bool OpenEditor(IntPtr parentHandle)
    {
        if (_commands is null)
        {
            return false;
        }

        _editorOpen = _commands.EditorOpen(parentHandle);
        return _editorOpen;
    }

    public void CloseEditor()
    {
        if (_commands is null || !_editorOpen)
        {
            return;
        }

        _commands.EditorClose();
        _editorOpen = false;
    }

    public void EditorIdle()
    {
        if (_commands is null || !_editorOpen)
        {
            return;
        }

        _commands.EditorIdle();
    }

    public void Dispose()
    {
        CloseEditor();
        if (_commands is not null)
        {
            _commands.MainsChanged(false);
            _commands.Close();
        }

        _inputBufferManager?.Dispose();
        _outputBufferManager?.Dispose();
        _context?.Dispose();
    }

    private PluginParameter[] BuildParameters()
    {
        if (_context is null || _commands is null)
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
            string name = _commands.GetParameterName(index) ?? string.Empty;
            string label = _commands.GetParameterLabel(index) ?? string.Empty;
            float defaultValue = _commands.GetParameter(index);
            parameters[index] = new PluginParameter
            {
                Index = index,
                Name = string.IsNullOrWhiteSpace(name) ? $"Param {index + 1}" : name,
                MinValue = 0f,
                MaxValue = 1f,
                DefaultValue = defaultValue,
                Unit = label,
                FormatValue = _ => _commands.GetParameterDisplay(index) ?? string.Empty
            };
        }

        return parameters;
    }

    private byte[] GetParameterState()
    {
        if (_commands is null || _parameters.Length == 0)
        {
            return Array.Empty<byte>();
        }

        var bytes = new byte[_parameters.Length * sizeof(float)];
        for (int i = 0; i < _parameters.Length; i++)
        {
            float value = _commands.GetParameter(i);
            Buffer.BlockCopy(BitConverter.GetBytes(value), 0, bytes, i * sizeof(float), sizeof(float));
        }

        return bytes;
    }

    private void ApplyParameterState(byte[] state)
    {
        if (_commands is null || _parameters.Length == 0)
        {
            return;
        }

        int count = Math.Min(_parameters.Length, state.Length / sizeof(float));
        for (int i = 0; i < count; i++)
        {
            float value = BitConverter.ToSingle(state, i * sizeof(float));
            _commands.SetParameter(i, Math.Clamp(value, 0f, 1f));
        }
    }
}
