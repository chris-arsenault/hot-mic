# Spectral Features

## Purpose
Compute high-level spectral features for voice quality analysis.

## Features
- Centroid: `sum(f * mag) / sum(mag)`.
- Slope: linear regression of dB vs frequency (reported in dB/kHz).
- Flux: mean-square change vs previous frame spectrum.

## Parameters and Defaults
- Uses analysis-bin center frequencies from the active transform.
- Flux uses a one-frame history buffer.

## Real-time Considerations
- Feature buffers and previous spectrum are preallocated.

Implementation refs: (src/HotMic.Core/Dsp/Spectrogram/SpectralFeatureExtractor.cs)
