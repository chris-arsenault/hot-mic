namespace HotMic.Core.Engine;

internal sealed class RoutingSnapshot
{
    public RoutingSnapshot(ChannelStrip[] channels, int[] processingOrder, int sampleRate, int blockSize, InputSource?[] inputSources)
    {
        Channels = channels;
        ProcessingOrder = processingOrder;
        Routing = new RoutingContext(Math.Max(1, channels.Length), sampleRate, blockSize);
        Buffers = new float[channels.Length][];
        for (int i = 0; i < channels.Length; i++)
        {
            Buffers[i] = new float[blockSize];
            Routing.SetInputSource(i, i < inputSources.Length ? inputSources[i] : null);
        }
    }

    public ChannelStrip[] Channels { get; }

    public float[][] Buffers { get; }

    public int[] ProcessingOrder { get; }

    public RoutingContext Routing { get; }
}
