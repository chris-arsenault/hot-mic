## Dynamics restoration (more “alive” after denoise/de-ess/compression)

### 1) Multiband upward expander (a.k.a. dynamic “decompression”)

Goal: restore micro-dynamics *within* bands that got flattened, without blowing up breaths/noise.

* Split into ~3–5 bands (e.g., low 80–250, low-mid 250–900, presence 1–4k, air 6–16k).
* Apply **upward expansion** only when speech is confidently present (use your VAD + voiced/unvoiced classifier).
* Use small ratios (e.g., 1.1:1–1.3:1 effective), short attacks in presence band, longer releases in low band.
* Gate the expander’s detector with a smoothed speech-likelihood to prevent “NR pumping.”
  This is a standard way to bring back motion/contrast after heavy control processing. ([iZotope][1])

### 2) Spectral contrast enhancement (lateral inhibition across frequency channels)

Goal: make consonant/vowel structure “pop” again (perceived clarity + articulation), without static EQ boosts.

* Implement a filterbank (ERB-ish or STFT bands).
* For each band, apply **dynamic compressive gain** plus **inhibitory sidebands** (neighboring bands reduce gain) → sharpens spectral peaks and formant structure over time.
  This is an explicit real-time speech contrast method studied for improving salient speech features. ([PubMed][2])

### 3) Envelope-following dynamic EQ (movement, not tone)

Goal: keep the same average spectrum, but reintroduce *time-varying* presence/air that tracks articulation.

* Use 2 detectors:

    * **Voiced detector** (periodicity / harmonicity): drives low-mid warmth + “chest.”
    * **Unvoiced/fricative detector** (HF energy + low periodicity): drives 2–6k “edge” + 8–14k “air.”
* Apply small, fast dynamic boosts/cuts rather than fixed EQ.
  This is essentially “dynamic EQ for intelligibility” and is widely used as an approach (practically, many tools advertise dynamic resonance/EQ workflows for voice). ([Waves][3])

## Psychoacoustic “air” and space (life without obvious EQ/sat)

### 4) Controlled room-tone / micro-ambience reintroduction (the anti-sterile fix)

Noise reduction often removes the low-level bed that makes voice feel present in a space; adding it back *intentionally* is common in dialogue workflows.
Two real-time-friendly options:

* **Captured room tone loop**: record 10–30s of your room with the same mic chain; loop with crossfades; sidechain duck it under speech.
* **Synthetic shaped noise bed**: very low-level bandlimited noise shaped to your mic/room profile, ducked under speech.
  Room tone is a known technique to avoid “dead” dialogue gaps and overly clinical cleanup. ([iZotope][4])

### 5) “Air exciter” but *keyed* and *de-ess-aware*

If you already have an exciter/sat, the missing piece is often **when** it engages:

* Drive HF harmonic generation only on **voiced** regions (periodicity high), and *reduce* it on sibilants (your de-esser detector becomes the exciter’s sidechain).
* Add tiny, slow modulation (very low-rate) to avoid static “sheen,” but clamp it during sibilants.
  HF exciters are commonly framed as adding perceived clarity/air by adding harmonics in upper bands; doing it keyed keeps it natural for speech. ([Apple Support][5])

## “Weight” for male speech without mud

### 6) Psychoacoustic bass enhancement (harmonics imply low end)

Instead of boosting sub/low EQ (which fights compressors and room noise), generate harmonics that imply deeper fundamentals.

* Feed a bandpassed low signal (e.g., 70–160 Hz), generate harmonics, mix back subtly.
  This is the core idea behind common bass enhancers (perceived depth without large LF gain). ([Waves][6])

## Consonant articulation (presence that moves)

### 7) Consonant transient emphasis (speech “edge” without harshness)

Goal: bring back the micro-attacks denoise + compression tend to smear.

* Detect rapid HF onsets in 2–8 kHz (or use a spectral flux measure).
* Apply a transient shaper *only* to those moments (5–30 ms windows), with a hard ceiling to avoid spit/ess.
  This tends to read as “livelier” and more intelligible than static presence EQ because it’s event-based.

## If you want to go more “speech-model aware” (still real-time)

### 8) Formant-aware enhancement / stabilization (very subtle)

Goal: preserve natural vowel identity and motion after processing.

* Track F1/F2 (lightweight formant tracking).
* Use that to steer dynamic EQ bands (boost near moving formants slightly; avoid boosting between them).
  Formant tracking is a well-studied area; you don’t need perfect accuracy—just stable enough to guide small moves. ([ece.mcmaster.ca][7])

## Practical chain order (typical)

1. NR (yours) → 2) De-ess (yours) → 3) **Spectral contrast OR multiband upward expansion** → 4) Dynamic EQ keyed by voiced/unvoiced → 5) keyed exciter/air → 6) psychoacoustic bass (optional) → 7) ambience bed (ducked)

If you tell me your sample rate / block size / latency budget and whether you’re STFT-based already, I can translate the best 2–3 of these into concrete module designs (detectors, time constants, band splits, and safety clamps) for real-time speech.

[1]: https://www.izotope.com/en/learn/expanding-on-compression-3-overlooked-techniques-for-improving-dynamic-range?srsltid=AfmBOoqu7o7uRXbtfliJIpHxqfdJooKvpacssLtmjgL35VFB5BYpNFLd&utm_source=chatgpt.com "Expanding on compression: 3 overlooked techniques for ..."
[2]: https://pubmed.ncbi.nlm.nih.gov/21949736/?utm_source=chatgpt.com "Real-time contrast enhancement to improve speech recognition"
[3]: https://www.waves.com/plugins/dynamic-eq-resonance-suppression?utm_source=chatgpt.com "Dynamic EQ & Resonance Suppression Plugins"
[4]: https://www.izotope.com/en/learn/basics-of-room-tone-audio-editing?srsltid=AfmBOoq3CtKfcihN2he3hVxagINjVQVpEwnkk1rzep6nfypswFGOs4gE&utm_source=chatgpt.com "The Basics of Room Tone in Audio Editing"
[5]: https://support.apple.com/en-ge/guide/logicpro/lgcef2cbe1b9/mac?utm_source=chatgpt.com "Exciter in Logic Pro for Mac"
[6]: https://www.waves.com/plugins/maxxbass?utm_source=chatgpt.com "MaxxBass - Bass Enhancer Plugin"
[7]: https://www.ece.mcmaster.ca/~ibruce/papers/mustafa2006_preprint.pdf?utm_source=chatgpt.com "Robust Formant Tracking for Continuous Speech With ..."
