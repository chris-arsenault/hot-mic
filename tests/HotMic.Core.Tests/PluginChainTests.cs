using HotMic.Core.Plugins;
using HotMic.Core.Plugins.BuiltIn;
using Xunit;

namespace HotMic.Core.Tests;

/// <summary>
/// Tests for PluginChain insert, remove, swap, reorder, bypass, and process operations.
/// Uses real GainPlugin instances as lightweight test subjects.
/// </summary>
public class PluginChainTests : IDisposable
{
    private const int SampleRate = 48000;
    private const int BlockSize = 256;
    private readonly PluginChain _chain = new(SampleRate, BlockSize);
    private readonly List<IPlugin> _plugins = new();

    private GainPlugin CreateGain()
    {
        var p = new GainPlugin();
        p.Initialize(SampleRate, BlockSize);
        _plugins.Add(p);
        return p;
    }

    public void Dispose()
    {
        foreach (var p in _plugins) p.Dispose();
    }

    [Fact]
    public void AddSlot_ThreePlugins_CorrectOrderAndCount()
    {
        var a = CreateGain(); var b = CreateGain(); var c = CreateGain();
        int idA = _chain.AddSlot(a);
        int idB = _chain.AddSlot(b);
        int idC = _chain.AddSlot(c);

        var snapshot = _chain.GetSnapshot();
        Assert.Equal(3, snapshot.Length);
        Assert.Same(a, snapshot[0]!.Plugin);
        Assert.Same(b, snapshot[1]!.Plugin);
        Assert.Same(c, snapshot[2]!.Plugin);
        Assert.True(idA > 0 && idB > idA && idC > idB, "IDs should be positive and monotonically increasing");
    }

    [Fact]
    public void InsertSlot_AtBeginning_ShiftsExisting()
    {
        var a = CreateGain(); var b = CreateGain(); var c = CreateGain();
        _chain.AddSlot(a);
        _chain.AddSlot(b);
        _chain.InsertSlot(0, c);

        var snapshot = _chain.GetSnapshot();
        Assert.Equal(3, snapshot.Length);
        Assert.Same(c, snapshot[0]!.Plugin);
        Assert.Same(a, snapshot[1]!.Plugin);
        Assert.Same(b, snapshot[2]!.Plugin);
    }

    [Fact]
    public void InsertSlot_AtMiddle_CorrectPlacement()
    {
        var a = CreateGain(); var b = CreateGain(); var c = CreateGain();
        _chain.AddSlot(a);
        _chain.AddSlot(c);
        _chain.InsertSlot(1, b);

        var snapshot = _chain.GetSnapshot();
        Assert.Same(a, snapshot[0]!.Plugin);
        Assert.Same(b, snapshot[1]!.Plugin);
        Assert.Same(c, snapshot[2]!.Plugin);
    }

    [Fact]
    public void RemoveSlot_Middle_PreservesOrder()
    {
        var a = CreateGain(); var b = CreateGain(); var c = CreateGain();
        _chain.AddSlot(a);
        int idB = _chain.AddSlot(b);
        _chain.AddSlot(c);

        var removed = _chain.RemoveSlot(1);
        Assert.Same(b, removed!.Plugin);

        var snapshot = _chain.GetSnapshot();
        Assert.Equal(2, snapshot.Length);
        Assert.Same(a, snapshot[0]!.Plugin);
        Assert.Same(c, snapshot[1]!.Plugin);
    }

    [Fact]
    public void RemoveSlot_OutOfBounds_Throws()
    {
        _chain.AddSlot(CreateGain());
        Assert.Throws<ArgumentOutOfRangeException>(() => _chain.RemoveSlot(5));
        Assert.Throws<ArgumentOutOfRangeException>(() => _chain.RemoveSlot(-1));
    }

    [Fact]
    public void Swap_AdjacentPlugins_ReversesOrder()
    {
        var a = CreateGain(); var b = CreateGain(); var c = CreateGain();
        _chain.AddSlot(a);
        _chain.AddSlot(b);
        _chain.AddSlot(c);

        _chain.Swap(1, 2);

        var snapshot = _chain.GetSnapshot();
        Assert.Same(a, snapshot[0]!.Plugin);
        Assert.Same(c, snapshot[1]!.Plugin);
        Assert.Same(b, snapshot[2]!.Plugin);
    }

    [Fact]
    public void Swap_NonAdjacentPlugins_CorrectResult()
    {
        var a = CreateGain(); var b = CreateGain(); var c = CreateGain(); var d = CreateGain();
        _chain.AddSlot(a);
        _chain.AddSlot(b);
        _chain.AddSlot(c);
        _chain.AddSlot(d);

        _chain.Swap(0, 3);

        var snapshot = _chain.GetSnapshot();
        Assert.Same(d, snapshot[0]!.Plugin);
        Assert.Same(b, snapshot[1]!.Plugin);
        Assert.Same(c, snapshot[2]!.Plugin);
        Assert.Same(a, snapshot[3]!.Plugin);
    }

    [Fact]
    public void Swap_OutOfBounds_Throws()
    {
        _chain.AddSlot(CreateGain());
        Assert.Throws<ArgumentOutOfRangeException>(() => _chain.Swap(0, 5));
    }

    [Fact]
    public void TryGetSlotById_AfterReorder_FindsAllPlugins()
    {
        var a = CreateGain(); var b = CreateGain(); var c = CreateGain();
        int idA = _chain.AddSlot(a);
        int idB = _chain.AddSlot(b);
        int idC = _chain.AddSlot(c);

        // Reorder: swap A and C
        _chain.Swap(0, 2);

        Assert.True(_chain.TryGetSlotById(idA, out var slotA, out int indexA));
        Assert.True(_chain.TryGetSlotById(idB, out var slotB, out int indexB));
        Assert.True(_chain.TryGetSlotById(idC, out var slotC, out int indexC));

        Assert.Same(a, slotA!.Plugin);
        Assert.Same(b, slotB!.Plugin);
        Assert.Same(c, slotC!.Plugin);

        // After swap(0,2): C is at 0, B at 1, A at 2
        Assert.Equal(2, indexA);
        Assert.Equal(1, indexB);
        Assert.Equal(0, indexC);
    }

    [Fact]
    public void TryGetSlotById_RemovedPlugin_ReturnsFalse()
    {
        var a = CreateGain();
        int idA = _chain.AddSlot(a);
        _chain.RemoveSlot(0);

        Assert.False(_chain.TryGetSlotById(idA, out _, out _));
    }

    [Fact]
    public void InstanceIds_AreUniqueAndIncreasing()
    {
        var ids = new List<int>();
        for (int i = 0; i < 20; i++)
        {
            ids.Add(_chain.AddSlot(CreateGain()));
        }

        // All unique
        Assert.Equal(ids.Count, ids.Distinct().Count());

        // All positive
        Assert.All(ids, id => Assert.True(id > 0));

        // Monotonically increasing
        for (int i = 1; i < ids.Count; i++)
        {
            Assert.True(ids[i] > ids[i - 1]);
        }
    }

    [Fact]
    public void Process_EmptyChain_BufferUnchanged()
    {
        var buffer = new float[] { 0.1f, 0.2f, 0.3f, 0.4f };
        var expected = (float[])buffer.Clone();

        _chain.Process(buffer, sampleClock: 0, channelId: 0, routingContext: null!);

        Assert.Equal(expected, buffer);
    }

    [Fact]
    public void Process_BypassedPlugin_BufferUnchanged()
    {
        var gain = CreateGain();
        gain.SetParameter(0, 6.0f); // +6dB gain — would change the buffer if not bypassed
        gain.IsBypassed = true;
        _chain.AddSlot(gain);

        var buffer = new float[] { 0.5f, 0.5f, 0.5f, 0.5f };
        var expected = (float[])buffer.Clone();

        _chain.Process(buffer, sampleClock: 0, channelId: 0, routingContext: null!);

        Assert.Equal(expected, buffer);
    }

    [Fact]
    public void GetSnapshot_IsImmutableAfterModification()
    {
        var a = CreateGain();
        _chain.AddSlot(a);

        var snapshot1 = _chain.GetSnapshot();
        Assert.Single(snapshot1);

        _chain.AddSlot(CreateGain());

        // First snapshot should still show 1 plugin (it's a separate array)
        Assert.Single(snapshot1);
        Assert.Equal(2, _chain.GetSnapshot().Length);
    }

    [Fact]
    public void DetachAll_ReturnsPluginsAndClearsChain()
    {
        _chain.AddSlot(CreateGain());
        _chain.AddSlot(CreateGain());
        _chain.AddSlot(CreateGain());

        var detached = _chain.DetachAll();
        Assert.Equal(3, detached.Length);
        Assert.Equal(0, _chain.Count);
    }

    [Fact]
    public void SetSlot_ReplacesExistingPlugin()
    {
        var a = CreateGain(); var b = CreateGain();
        int idA = _chain.AddSlot(a);

        var previous = _chain.SetSlot(0, b);
        Assert.Same(a, previous!.Plugin);
        Assert.Same(b, _chain.GetSnapshot()[0]!.Plugin);
    }
}
