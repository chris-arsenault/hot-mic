#!/usr/bin/env python3
"""
Compute reference values for DSP math tests (Burg LPC + root extraction).

Requires: numpy
"""
import numpy as np


def burg_coeffs(signal, order):
    x = np.asarray(signal, dtype=np.float64)
    n = len(x)
    ef = x.copy()
    eb = x.copy()
    a = np.zeros(order + 1, dtype=np.float64)
    a[0] = 1.0
    for m in range(1, order + 1):
        num = np.sum(ef[m:] * eb[m - 1 : -1])
        den = np.sum(ef[m:] ** 2 + eb[m - 1 : -1] ** 2)
        if den <= 1e-12:
            break
        k = -2.0 * num / den
        a_new = a.copy()
        a_new[m] = k
        for i in range(1, m):
            a_new[i] = a[i] + k * a[m - i]
        ef_m = ef[m:] + k * eb[m - 1 : -1]
        eb_m = eb[m - 1 : -1] + k * ef[m:]
        ef[m:] = ef_m
        eb[m - 1 : -1] = eb_m
        a = a_new
    return a


def resonance_coeffs(freq_hz, bw_hz, sr):
    theta = 2 * np.pi * freq_hz / sr
    r = np.exp(-np.pi * bw_hz / sr)
    return np.array([1.0, -2 * r * np.cos(theta), r**2])


def extract_formants(coeffs, sr, min_hz, max_hz):
    roots = np.roots(coeffs)
    nyquist = sr * 0.5
    min_hz = max(0.0, min_hz)
    max_hz = min(max_hz, nyquist * 0.9)
    results = []
    for r in roots:
        if np.imag(r) <= 0.001:
            continue
        mag = np.abs(r)
        if mag <= 0.80 or mag >= 0.9995:
            continue
        freq = np.arctan2(np.imag(r), np.real(r)) * sr / (2 * np.pi)
        bw = -sr / np.pi * np.log(mag)
        if freq < min_hz or freq > max_hz:
            continue
        if bw <= 0 or bw > 3500:
            continue
        results.append((freq, bw))
    results.sort(key=lambda x: x[0])
    return results

def lcg_uniform(state):
    state = (state * 1664525 + 1013904223) & 0xFFFFFFFF
    return state, (state + 1.0) / 4294967296.0

def lcg_gaussian(seed, n):
    state = seed & 0xFFFFFFFF
    out = np.zeros(n, dtype=np.float64)
    for i in range(n):
        state, u1 = lcg_uniform(state)
        state, u2 = lcg_uniform(state)
        out[i] = np.sqrt(-2.0 * np.log(u1)) * np.sin(2.0 * np.pi * u2)
    return out


def yin_pitch(frame, sample_rate, fmin, fmax, threshold):
    frame = np.asarray(frame, dtype=np.float64)
    n = len(frame)
    tau_min = int(sample_rate / fmax)
    tau_max = int(sample_rate / fmin)
    if tau_max >= n:
        tau_max = n - 1
    if tau_min < 1:
        tau_min = 1

    d = np.zeros(tau_max + 1, dtype=np.float64)
    for tau in range(1, tau_max + 1):
        diff = frame[:-tau] - frame[tau:]
        d[tau] = np.dot(diff, diff)

    cmnd = np.zeros_like(d)
    cmnd[0] = 1.0
    running_sum = 0.0
    for tau in range(1, tau_max + 1):
        running_sum += d[tau]
        cmnd[tau] = d[tau] * tau / running_sum if running_sum > 0 else 1.0

    tau = None
    for t in range(tau_min, tau_max + 1):
        if cmnd[t] < threshold:
            while t + 1 <= tau_max and cmnd[t + 1] < cmnd[t]:
                t += 1
            tau = t
            break

    if tau is None:
        return None

    if 1 < tau < tau_max:
        s0, s1, s2 = cmnd[tau - 1], cmnd[tau], cmnd[tau + 1]
        denom = 2 * (2 * s1 - s2 - s0)
        if denom != 0:
            tau = tau + (s2 - s0) / denom

    return sample_rate / tau

def fmt(arr):
    return "[" + ", ".join(f"{v:.9f}" for v in arr) + "]"


def main():
    # LPC references
    fs = 1000
    n = 256
    order = 4
    t = np.arange(n) / fs
    x = np.sin(2 * np.pi * 100 * t)
    print("LPC Burg sine 100 Hz, fs=1000, n=256, order=4:", fmt(burg_coeffs(x, order)))

    fs = 12000
    n = 512
    order = 8
    t = np.arange(n) / fs
    x = np.sin(2 * np.pi * 400 * t) + 0.5 * np.sin(2 * np.pi * 1500 * t)
    print("LPC Burg 400+1500 Hz, fs=12000, n=512, order=8:", fmt(burg_coeffs(x, order)))

    fs = 8000
    n = 256
    order = 6
    t = np.arange(n) / fs
    x = np.sin(2 * np.pi * 250 * t) + 0.4 * np.sin(2 * np.pi * 500 * t)
    print("LPC Burg 250+500 Hz, fs=8000, n=256, order=6:", fmt(burg_coeffs(x, order)))

    fs = 12000
    n = 512
    order = 8
    t = np.arange(n) / fs
    x = np.sin(2 * np.pi * 500 * t) + 0.6 * np.sin(2 * np.pi * 1200 * t)
    print("LPC Burg 500+1200 Hz, fs=12000, n=512, order=8:", fmt(burg_coeffs(x, order)))

    fs = 12000
    n = 512
    order = 12
    t = np.arange(n) / fs
    x = (
        np.sin(2 * np.pi * 700 * t)
        + 0.7 * np.sin(2 * np.pi * 1200 * t)
        + 0.4 * np.sin(2 * np.pi * 2500 * t)
    )
    coeffs = burg_coeffs(x, order)
    print("LPC Burg 700+1200+2500 Hz, fs=12000, n=512, order=12:", fmt(coeffs))
    formants = extract_formants(coeffs, fs, 100, 5500)
    print("Formant extract (Burg, 700/1200/2500 sines):", formants)

    noise = lcg_gaussian(9012, n)
    noisy = x + 0.1 * noise
    coeffs = burg_coeffs(noisy, order)
    formants = extract_formants(coeffs, fs, 100, 5500)
    print("Formant extract (Burg, 700/1200/2500 sines + noise 0.1, seed=9012):", formants)

    noisy = x + 0.2 * noise
    coeffs = burg_coeffs(noisy, order)
    formants = extract_formants(coeffs, fs, 100, 5500)
    print("Formant extract (Burg, 700/1200/2500 sines + noise 0.2, seed=9012):", formants)

    # Formant references
    sr = 16000
    coeffs = resonance_coeffs(500, 80, sr)
    print("Formant single 500 Hz, bw 80 Hz, sr=16000:", fmt(coeffs))

    coeffs = np.convolve(
        resonance_coeffs(500, 100, sr),
        resonance_coeffs(2000, 300, sr),
    )
    print("Formant 500/2000 Hz, bw 100/300 Hz, sr=16000:", fmt(coeffs))

    coeffs = resonance_coeffs(2500, 5, 12000)
    print("Formant single 2500 Hz, bw 5 Hz, sr=12000:", fmt(coeffs))

    # YIN references (canonical YIN, CMND + threshold + parabolic interpolation)
    def sine(freq, sr, n):
        return np.sin(2 * np.pi * freq * np.arange(n) / sr)

    sr = 1000
    frame = sine(100, sr, 64)
    print("YIN 100 Hz, fs=1000, n=64:", yin_pitch(frame, sr, 50, 200, 0.15))

    sr = 12000
    n = 1024
    for f in [55, 100, 200, 300, 440, 950]:
        frame = sine(f, sr, n)
        print(f"YIN {f} Hz, fs=12000, n=1024:", yin_pitch(frame, sr, 50, 1000, 0.15))

    fund = 200
    t = np.arange(n) / sr
    frame = (
        np.sin(2 * np.pi * fund * t)
        + 0.5 * np.sin(2 * np.pi * 2 * fund * t)
        + 0.25 * np.sin(2 * np.pi * 3 * fund * t)
    )
    print("YIN complex 200 Hz, fs=12000, n=1024:", yin_pitch(frame, sr, 50, 500, 0.15))

    frame = sine(257.3, sr, n)
    print("YIN 257.3 Hz, fs=12000, n=1024:", yin_pitch(frame, sr, 50, 500, 0.15))

    # LPC noisy references (LCG + Box-Muller, matches test helper)
    n = 1024
    order = 8
    noise = lcg_gaussian(1234, n)
    print("LPC Burg white noise, seed=1234:", fmt(burg_coeffs(noise, order)))

    fs = 12000
    t = np.arange(n) / fs
    signal = np.sin(2 * np.pi * 300 * t) + 0.25 * lcg_gaussian(5678, n)
    print("LPC Burg sine+noise, seed=5678:", fmt(burg_coeffs(signal, order)))


if __name__ == "__main__":
    main()
