# Vitalizer Mk2-T (Tube) - HotMic Approximation

## Purpose
Approximate the SPL Tube Vitalizer Mk2-T behavior (mono only; stereo expander not implemented).
The goal is psychoacoustic de-masking using filter-network timing/phase shifts, bass
intensification, high-frequency enhancement, and a switchable tube stage.

## Hardware Behavior Summary (reference)
- Drive sets the operating level of the filter network (-20 dB to +6 dB). Higher drive
  increases processing intensity; clip LED lights ~3 dB before overload.
- Bass Sound control: soft/warm (left) to tight/percussive (right), center = no bass
  intensification. Audible bass processing begins once Process is above ~9 o'clock.
- Bass Comp: bass-only compressor with soft knee; ratio from 1:1 to 10:1; threshold,
  attack, and release are preset. Fully clockwise nullifies bass processing.
- Process Level controls the ratio between Bass+Hi-Mid processing and the original signal
  and damps dominant mid frequencies. It is adapted to equal-loudness curves.
- Mid-Hi Tune is a broad-band shelving process (1.1 kHz to 22 kHz). Material above the
  frequency is amplified, below is attenuated; the process affects phase as well as
  amplitude over a wide band.
- High Freq / LC-EQ is a linear high-frequency stage (2 kHz to 20 kHz). It is not an
  exciter and should not create distortion; it uses phase relationships to increase
  perceived highs.
- Intensity adjusts the amount of LC-EQ; with Process at zero the LC-EQ can be auditioned.
- High Comp: high-frequency compressor with soft knee; ratio 1:1 to 10:1; threshold/attack/
  release preset. Fully clockwise nullifies high-frequency processing.
- LC filters (Bass/High) use passive coils/caps; coils can add punch or subtle roughness.
- Tube stage (switchable) uses three tubes (L/R then stereo); the Tube Vitalizer adds a
  shunt limiter tuned to the tube stage for tape-like saturation.
- Output Attenuator (-20 dB to +6 dB) is used to control output and avoid overload.

References:
- SPL Tube Vitalizer product page (tube stage + shunt limiter + HF/harmonics stage):
  https://spl.audio/en/spl-produkt/tube-vitalizer/
- SPL Tube Vitalizer manual (controls, ranges, behavior):
  https://www.manualslib.com/manual/302487/Spl-Tube-Vitalizer.html
- SPL Stereo Vitalizer Mk2-T manual (controls, ranges, behavior):
  https://www.manualslib.com/manual/3039482/Spl-Stereo-Vitalizer-Mk2-T.html

## Parameters (HotMic)
- Drive (dB): -20..+6
- Bass (Soft <-> Tight): bipolar, center = 0
- Bass Comp (ratio): 1..10
- Bass LC (toggle)
- Mid-Hi Tune (Hz): 1100..22000
- Process (0..1)
- High Freq (Hz): 2000..20000
- Intensity (0..1)
- High Comp (ratio): 1..10
- High LC (toggle)
- Tube (toggle)
- Output (dB): -20..+6
- Limit (toggle)  // approximates the shunt limiter

Stereo Expander is intentionally omitted (mono-only plugin).

## DSP Approximation

### 1) Drive (pre-gain)
Apply pre-gain to the input: `x = x * dbToLinear(DriveDb)`.
Drive primarily raises the operating level of the network so downstream envelopes,
phase shifting, and tube stage respond more intensely without changing control ranges.

### 2) Bass Sound + Bass LC
Create a bass-enhanced signal using a low-shelf (and optional LC flavor):
- Bass amount = abs(Bass) * BassMaxDb
- Soft side (Bass < 0): lower shelf frequency, gentler Q
- Tight side (Bass > 0): higher shelf frequency, slightly higher Q
- If Bass LC is enabled, add a mild resonant bump and a subtle asymmetric saturator
  on the bass path to emulate coil saturation.

### 3) Bass Comp (bass-only)
Compute envelope from bass band (post-shelf), then apply compression only to the
bass-enhanced signal:
- Soft-knee transfer with fixed threshold/attack/release
- Ratio from knob (1..10)
- Additional scaling so ratio max (10) reduces bass effect towards zero

### 4) Mid-Hi Tune + Process (de-masking + phase shift)
Create a broad tilt around Mid-Hi Tune:
- High shelf (+G) above tune, low shelf (-G) below tune
- G scales with Process and Drive

Add amplitude-correlated phase shifting to damp dominant mids:
- Extract mid-band envelope (wide band-pass)
- Modulate a pair of all-pass filters with the envelope
- Apply a mid-band dynamic dip proportional to envelope (dominant mid damping)
Process is gated so values below ~0.25 yield no effect, matching the manual’s
“audible above ~9 o’clock” behavior. The active region uses a squared ramp.

### 5) High Freq / LC-EQ + Intensity + High Comp
High-frequency stage is linear and uses steep shelving:
- Cascade 2x high-shelf filters at HighFreq
- Intensity crossfades between dry and the high-shelf output
- If High LC enabled, increase shelf Q and add a small resonant bump near cutoff

Apply High Comp to the high-frequency path only (soft knee, fixed threshold/attack/release,
ratio 1..10), and reduce the HF effect as ratio approaches max.

### 6) Tube Stage (switchable) + Shunt Limiter
When Tube is enabled:
- 2x oversample (half-band FIR), process with an asymmetric triode-style waveshaper
  (bias + tanh) and a gentle HF damping filter. The shaper is normalized to keep
  small-signal gain ~unity even with bias.
- Downsample back to base rate.
- Apply an RMS-matched makeup gain (smoothed) so tube insertion does not drop level.

Limit toggle engages a soft limiter above approx +12 dB, implemented as a smooth
saturation curve to emulate the shunt limiter. (Limit is only active when Tube is on.)

### 7) Output Attenuator
Apply Output dB gain at the end.

## Notes / Approximations
- The original hardware uses proprietary LC networks and amplitude-controlled phase
  shifting. We approximate these with cascaded shelves + all-pass filters and envelope
  modulation. The intent is matching perceptual behavior, not circuit-accurate modeling.
- Tube stage is an oversampled asymmetric waveshaper; it is not a full tube circuit model.
- Stereo expander is explicitly out of scope in this version.

## Code Pointers
- `src/HotMic.Core/Plugins/BuiltIn/VitalizerMk2TPlugin.cs`
- `src/HotMic.Core/Dsp/Filters/AllPassFilter.cs`
