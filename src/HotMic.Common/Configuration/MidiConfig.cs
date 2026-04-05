using System.Collections.ObjectModel;
namespace HotMic.Common.Configuration;

/// <summary>
/// MIDI configuration settings.
/// </summary>
public sealed class MidiConfig
{
    public bool Enabled { get; set; }

    public string? DeviceName { get; set; }

    public int? FilterChannel { get; set; }

    public IList<MidiBinding> Bindings { get; } = new List<MidiBinding>();
}

/// <summary>
/// Maps a MIDI CC to a parameter.
/// </summary>
public sealed class MidiBinding
{
    public int CcNumber { get; set; }

    public int? Channel { get; set; }

    public string TargetPath { get; set; } = string.Empty;

    public float MinValue { get; set; }

    public float MaxValue { get; set; } = 1f;
}
