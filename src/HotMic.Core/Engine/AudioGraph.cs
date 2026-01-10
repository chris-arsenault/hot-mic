namespace HotMic.Core.Engine;

public sealed class AudioGraph
{
    public AudioGraph(AudioEngine engine)
    {
        Engine = engine;
    }

    public AudioEngine Engine { get; }
}
