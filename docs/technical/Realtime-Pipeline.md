# Real-time Analysis Pipeline

## Purpose
Define the threading model, buffering, and synchronization used for analysis output.

## Data Flow
1. Audio callback writes samples into a lock-free ring buffer.
2. Analysis thread reads hop-sized blocks and produces frames.
3. Per-frame metrics are written into ring buffers.
4. Consumer copies a versioned snapshot for rendering or inspection.

## Buffers and Rates
- Capture ring buffer size: 262144 samples (float).
- Hop size: `fftSize * (1 - overlap)`.
- Frame capacity: `ceil(timeWindow * sampleRate / hop)`.
- Display bins: `min(1024, fftSize/2)`.
- Ring buffers store: spectrogram, pitch, formants, voicing, harmonics, waveform min/max,
  HNR, CPP, centroid, slope, flux.

## Synchronization
- `_dataVersion` is incremented before and after frame writes.
- Even `_dataVersion` indicates a stable snapshot.
- Snapshot copy uses a read-verify pattern (two attempts) to avoid tearing.
- Uses `Volatile.Read/Write` and `Interlocked` to avoid locks in the hot path.

## Activation
- Analysis can be paused.
- When re-enabled, capture and visualization buffers are cleared to avoid stale data.

## Real-time Considerations
- Audio callback only writes to the ring buffer; no allocations or locks.
- Analysis thread sleeps when insufficient data is available to avoid busy spinning.

Implementation refs: (src/HotMic.Core/Plugins/BuiltIn/VocalSpectrographPlugin.Analysis.cs,
 src/HotMic.Core/Plugins/BuiltIn/VocalSpectrographPlugin.Buffers.cs,
 src/HotMic.Core/Threading/LockFreeRingBuffer.cs)
