using System.Reflection;
using HotMic.Core.Plugins.BuiltIn;
using Xunit;

namespace HotMic.Core.Tests;

public class VocalSpectrographBufferSyncTests
{
    private const float Sentinel = -999f;
    private const byte SentinelByte = 0xEE;
    private const float Tolerance = 1e-5f;

    [Fact]
    public void CopySpectrogramUpdates_PartialCopy_PreservesRingAlignment()
    {
        var plugin = new VocalSpectrographPlugin();
        plugin.SetParameter(VocalSpectrographPlugin.FftSizeIndex, 1024f);
        plugin.SetParameter(VocalSpectrographPlugin.TimeWindowIndex, 1f);
        plugin.Initialize(48000, 512);

        try
        {
            int frames = GetField<int>(plugin, "_activeFrameCapacity");
            int bins = GetField<int>(plugin, "_activeDisplayBins");
            float[] spectrogramBuffer = GetField<float[]>(plugin, "_spectrogramBuffer");
            float[] pitchTrackBuffer = GetField<float[]>(plugin, "_pitchTrack");
            float[] formantFrequencyBuffer = GetField<float[]>(plugin, "_formantFrequencies");
            byte[] voicingBuffer = GetField<byte[]>(plugin, "_voicingStates");

            int maxFormants = formantFrequencyBuffer.Length / frames;

            long latestFrameId = frames + 3;
            int availableFrames = Math.Min(frames, 10);
            long oldestFrameId = latestFrameId - availableFrames + 1;

            Array.Clear(spectrogramBuffer, 0, spectrogramBuffer.Length);
            Array.Clear(pitchTrackBuffer, 0, pitchTrackBuffer.Length);
            Array.Clear(formantFrequencyBuffer, 0, formantFrequencyBuffer.Length);
            Array.Clear(voicingBuffer, 0, voicingBuffer.Length);

            for (long frameId = oldestFrameId; frameId <= latestFrameId; frameId++)
            {
                int ringIndex = (int)(frameId % frames);
                spectrogramBuffer[ringIndex * bins] = frameId + 0.25f;
                pitchTrackBuffer[ringIndex] = 1000f + frameId;
                int offset = ringIndex * maxFormants;
                if (maxFormants > 0)
                {
                    formantFrequencyBuffer[offset] = 2000f + frameId;
                    if (maxFormants > 1)
                    {
                        formantFrequencyBuffer[offset + 1] = 3000f + frameId;
                    }
                }
                voicingBuffer[ringIndex] = (byte)(frameId % 3);
            }

            SetField(plugin, "_latestFrameId", latestFrameId);
            SetField(plugin, "_availableFrames", availableFrames);
            SetField(plugin, "_dataVersion", 0);

            float[] spectrogramOut = CreateFilled(spectrogramBuffer.Length, Sentinel);
            float[] pitchOut = CreateFilled(pitchTrackBuffer.Length, Sentinel);
            float[] pitchConfidenceOut = CreateFilled(GetField<float[]>(plugin, "_pitchConfidence").Length, Sentinel);
            float[] formantOut = CreateFilled(formantFrequencyBuffer.Length, Sentinel);
            float[] formantBwOut = CreateFilled(GetField<float[]>(plugin, "_formantBandwidths").Length, Sentinel);
            byte[] voicingOut = CreateFilled(GetField<byte[]>(plugin, "_voicingStates").Length, SentinelByte);
            float[] harmonicOut = CreateFilled(GetField<float[]>(plugin, "_harmonicFrequencies").Length, Sentinel);
            float[] harmonicMagOut = CreateFilled(GetField<float[]>(plugin, "_harmonicMagnitudes").Length, Sentinel);
            float[] waveformMinOut = CreateFilled(GetField<float[]>(plugin, "_waveformMin").Length, Sentinel);
            float[] waveformMaxOut = CreateFilled(GetField<float[]>(plugin, "_waveformMax").Length, Sentinel);
            float[] hnrOut = CreateFilled(GetField<float[]>(plugin, "_hnrTrack").Length, Sentinel);
            float[] cppOut = CreateFilled(GetField<float[]>(plugin, "_cppTrack").Length, Sentinel);
            float[] centroidOut = CreateFilled(GetField<float[]>(plugin, "_spectralCentroid").Length, Sentinel);
            float[] slopeOut = CreateFilled(GetField<float[]>(plugin, "_spectralSlope").Length, Sentinel);
            float[] fluxOut = CreateFilled(GetField<float[]>(plugin, "_spectralFlux").Length, Sentinel);

            bool ok = plugin.CopySpectrogramUpdates(
                sinceFrameId: latestFrameId - 2,
                spectrogramOut,
                pitchOut,
                pitchConfidenceOut,
                formantOut,
                formantBwOut,
                voicingOut,
                harmonicOut,
                harmonicMagOut,
                waveformMinOut,
                waveformMaxOut,
                hnrOut,
                cppOut,
                centroidOut,
                slopeOut,
                fluxOut,
                out long latest,
                out int available,
                out bool fullCopy);

            Assert.True(ok);
            Assert.False(fullCopy);
            Assert.Equal(latestFrameId, latest);
            Assert.Equal(availableFrames, available);

            int ringPrev = (int)((latestFrameId - 1) % frames);
            int ringLast = (int)(latestFrameId % frames);

            Assert.InRange(spectrogramOut[ringPrev * bins], (latestFrameId - 1) + 0.25f - Tolerance, (latestFrameId - 1) + 0.25f + Tolerance);
            Assert.InRange(spectrogramOut[ringLast * bins], latestFrameId + 0.25f - Tolerance, latestFrameId + 0.25f + Tolerance);
            Assert.InRange(pitchOut[ringPrev], 1000f + latestFrameId - 1 - Tolerance, 1000f + latestFrameId - 1 + Tolerance);
            Assert.InRange(pitchOut[ringLast], 1000f + latestFrameId - Tolerance, 1000f + latestFrameId + Tolerance);

            if (maxFormants > 0)
            {
                int prevOffset = ringPrev * maxFormants;
                int lastOffset = ringLast * maxFormants;
                Assert.InRange(formantOut[prevOffset], 2000f + latestFrameId - 1 - Tolerance, 2000f + latestFrameId - 1 + Tolerance);
                Assert.InRange(formantOut[lastOffset], 2000f + latestFrameId - Tolerance, 2000f + latestFrameId + Tolerance);
                if (maxFormants > 1)
                {
                    Assert.InRange(formantOut[prevOffset + 1], 3000f + latestFrameId - 1 - Tolerance, 3000f + latestFrameId - 1 + Tolerance);
                    Assert.InRange(formantOut[lastOffset + 1], 3000f + latestFrameId - Tolerance, 3000f + latestFrameId + Tolerance);
                }
            }

            int ringUntouched = (int)((latestFrameId - 5) % frames);
            if (ringUntouched != ringPrev && ringUntouched != ringLast)
            {
                Assert.Equal(Sentinel, spectrogramOut[ringUntouched * bins]);
                Assert.Equal(Sentinel, pitchOut[ringUntouched]);
            }
        }
        finally
        {
            plugin.Dispose();
        }
    }

    private static T GetField<T>(object target, string name)
    {
        var field = target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
        return (T)field!.GetValue(target)!;
    }

    private static void SetField<T>(object target, string name, T value)
    {
        var field = target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
        field!.SetValue(target, value);
    }

    private static float[] CreateFilled(int length, float value)
    {
        var data = new float[length];
        Array.Fill(data, value);
        return data;
    }

    private static byte[] CreateFilled(int length, byte value)
    {
        var data = new byte[length];
        Array.Fill(data, value);
        return data;
    }
}
