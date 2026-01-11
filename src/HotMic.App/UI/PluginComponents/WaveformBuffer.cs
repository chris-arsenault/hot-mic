using System.Threading;

namespace HotMic.App.UI.PluginComponents;

/// <summary>
/// Thread-safe circular buffer for storing audio level history.
/// Used by WaveformDisplay to show rolling waveform visualization.
/// </summary>
public sealed class WaveformBuffer
{
    private readonly float[] _levels;
    private readonly bool[] _gateStates;
    private int _writeIndex;
    private readonly object _lock = new();

    public WaveformBuffer(int capacity = 256)
    {
        _levels = new float[capacity];
        _gateStates = new bool[capacity];
    }

    public int Capacity => _levels.Length;

    /// <summary>
    /// Push a new level sample and gate state. Thread-safe.
    /// </summary>
    public void Push(float level, bool gateOpen)
    {
        lock (_lock)
        {
            _levels[_writeIndex] = level;
            _gateStates[_writeIndex] = gateOpen;
            _writeIndex = (_writeIndex + 1) % _levels.Length;
        }
    }

    /// <summary>
    /// Copy the current waveform data to the provided arrays.
    /// Data is ordered from oldest to newest.
    /// </summary>
    public void CopyTo(float[] levels, bool[] gateStates)
    {
        if (levels.Length != _levels.Length || gateStates.Length != _gateStates.Length)
            return;

        lock (_lock)
        {
            int start = _writeIndex;
            for (int i = 0; i < _levels.Length; i++)
            {
                int srcIdx = (start + i) % _levels.Length;
                levels[i] = _levels[srcIdx];
                gateStates[i] = _gateStates[srcIdx];
            }
        }
    }

    /// <summary>
    /// Get a snapshot of current levels ordered oldest to newest.
    /// </summary>
    public (float[] levels, bool[] gateStates) GetSnapshot()
    {
        var levels = new float[_levels.Length];
        var states = new bool[_gateStates.Length];
        CopyTo(levels, states);
        return (levels, states);
    }

    public void Clear()
    {
        lock (_lock)
        {
            Array.Clear(_levels);
            Array.Clear(_gateStates);
            _writeIndex = 0;
        }
    }
}
