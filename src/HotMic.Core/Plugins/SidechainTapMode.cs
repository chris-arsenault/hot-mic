namespace HotMic.Core.Plugins;

public enum SidechainTapMode
{
    /// <summary>Do not forward or generate this signal.</summary>
    Disabled = 0,
    /// <summary>Generate a new signal at the tap position.</summary>
    Generate = 1,
    /// <summary>Use the upstream signal if available.</summary>
    UseExisting = 2
}
