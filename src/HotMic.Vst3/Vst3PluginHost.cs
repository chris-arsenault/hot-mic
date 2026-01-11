using System.Threading;
using Jacobi.Vst.Core;
using Jacobi.Vst.Core.Host;

namespace HotMic.Vst3;

public sealed class Vst3PluginHost : IVstHostCommandStub, IVstHostCommands20
{
    private readonly int _sampleRate;
    private readonly int _blockSize;
    private readonly string _directory;
    private long _samplePosition;

    public Vst3PluginHost(int sampleRate, int blockSize, string directory)
    {
        _sampleRate = sampleRate;
        _blockSize = blockSize;
        _directory = directory;
    }

    public IVstPluginContext PluginContext { get; set; } = null!;

    public IVstHostCommands20 Commands => this;

    public void AdvanceSamples(int samples)
    {
        Interlocked.Add(ref _samplePosition, samples);
    }

    public VstTimeInfo GetTimeInfo(VstTimeInfoFlags filterFlags)
    {
        return new VstTimeInfo
        {
            SamplePosition = Interlocked.Read(ref _samplePosition),
            SampleRate = _sampleRate,
            Tempo = 120,
            TimeSignatureNumerator = 4,
            TimeSignatureDenominator = 4,
            Flags = VstTimeInfoFlags.TransportPlaying | VstTimeInfoFlags.TempoValid | VstTimeInfoFlags.TimeSignatureValid
        };
    }

    public bool ProcessEvents(VstEvent[] events) => false;

    public bool IoChanged() => true;

    public bool SizeWindow(int width, int height) => false;

    public float GetSampleRate() => _sampleRate;

    public int GetBlockSize() => _blockSize;

    public int GetInputLatency() => 0;

    public int GetOutputLatency() => 0;

    public VstProcessLevels GetProcessLevel() => VstProcessLevels.Realtime;

    public VstAutomationStates GetAutomationState() => VstAutomationStates.Off;

    public string GetVendorString() => "HotMic";

    public string GetProductString() => "HotMic";

    public int GetVendorVersion() => 1000;

    public VstCanDoResult CanDo(string cando) => VstCanDoResult.Unknown;

    public VstHostLanguage GetLanguage() => VstHostLanguage.English;

    public string GetDirectory() => _directory;

    public bool UpdateDisplay() => false;

    public bool BeginEdit(int index) => true;

    public bool EndEdit(int index) => true;

    public bool OpenFileSelector(VstFileSelect fileSelect) => false;

    public bool CloseFileSelector(VstFileSelect fileSelect) => false;

    public void SetParameterAutomated(int index, float value)
    {
    }

    public int GetVersion() => 2400;

    public int GetCurrentPluginID() => PluginContext?.PluginInfo?.PluginID ?? 0;

    public void ProcessIdle()
    {
    }
}
