#!/usr/bin/env python3
"""PeaHeads music + sound effects, synthesised from scratch (no samples, deterministic).

    python tools/make_audio.py            # everything
    python tools/make_audio.py sfx        # only sound effects
    python tools/make_audio.py menu game  # only some music tracks

Writes 16-bit WAVs into PeaHeadsApp/Assets/PeaHeads/Audio/Resources/{Music,Sfx}; Unity loads them by name through
GameAudio (Resources.Load). Music loops seamlessly: every track is rendered twice in a row and the second pass is
kept, so reverb/delay tails from the end of the loop are already present at its start.
"""
import os
import sys
import wave

import numpy as np
from scipy import signal

MUSIC_SR = 32000
SFX_SR = 44100
ROOT = os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', 'PeaHeadsApp', 'Assets', 'PeaHeads', 'Audio',
                    'Resources')


def hz(midi):
    return 440.0 * 2.0 ** ((midi - 69) / 12.0)


def rng(seed):
    return np.random.default_rng(seed)


def time_axis(seconds, sr):
    return np.arange(int(seconds * sr)) / sr


def lowpass(x, cutoff, sr, order=2):
    b, a = signal.butter(order, min(cutoff / (sr / 2), 0.99), 'low')
    return signal.lfilter(b, a, x, axis=-1)


def highpass(x, cutoff, sr, order=2):
    b, a = signal.butter(order, min(cutoff / (sr / 2), 0.99), 'high')
    return signal.lfilter(b, a, x, axis=-1)


def bandpass(x, lo, hi, sr, order=2):
    b, a = signal.butter(order, [lo / (sr / 2), min(hi / (sr / 2), 0.99)], 'band')
    return signal.lfilter(b, a, x, axis=-1)


def fade(n, attack, release, sr):
    env = np.ones(n)
    a, r = min(n, int(attack * sr)), min(n, int(release * sr))
    if a > 0:
        env[:a] = np.linspace(0.0, 1.0, a)
    if r > 0:
        env[n - r:] *= np.linspace(1.0, 0.0, r)
    return env


# ---------------------------------------------------------------- instruments (mono float arrays)

def soft_saw(freq, sr, harmonics=8, tilt=1.6):
    """Band-limited soft sawtooth; freq is a per-sample array (vibrato-safe phase integration)."""
    phase = 2 * np.pi * np.cumsum(freq) / sr
    out = np.zeros_like(phase)
    for k in range(1, harmonics + 1):
        if np.max(freq) * k > 9000:
            break
        out += np.sin(k * phase) / k ** tilt
    return out


def pad_note(midi, seconds, sr, attack=1.4, release=2.2, cutoff=2400.0):
    """Warm detuned pad. Returns (left, right)."""
    t = time_axis(seconds + release, sr)
    f = hz(midi) * np.ones_like(t)
    wobble = 1.0 + 0.0015 * np.sin(2 * np.pi * 0.23 * t)
    left = soft_saw(f * 0.996 * wobble, sr) + 0.6 * soft_saw(f * 1.002, sr)
    right = soft_saw(f * 1.004 * wobble, sr) + 0.6 * soft_saw(f * 0.999, sr)
    env = np.minimum(1.0, t / attack) * np.where(t > seconds, np.exp(-(t - seconds) / (release / 3)), 1.0)
    env *= 0.85 + 0.15 * np.sin(2 * np.pi * 0.11 * t + midi)
    return lowpass(left * env, cutoff, sr), lowpass(right * env, cutoff, sr)


def bell(midi, sr, length=2.6, bright=1.0):
    t = time_axis(length, sr)
    f = hz(midi)
    ratios = [1.0, 2.0, 3.0, 4.16, 5.43, 6.79]
    amps = [1.0, 0.45, 0.2, 0.16 * bright, 0.1 * bright, 0.05 * bright]
    decays = [1.5, 0.9, 0.6, 0.35, 0.25, 0.16]
    out = np.zeros_like(t)
    for r, a, d in zip(ratios, amps, decays):
        if f * r < sr / 2.2:
            out += a * np.sin(2 * np.pi * f * r * t) * np.exp(-t / (d * length / 2.6))
    return out * np.minimum(1.0, t / 0.002)


def music_box(midi, sr, length=2.2):
    t = time_axis(length, sr)
    f = hz(midi)
    out = (np.sin(2 * np.pi * f * t) * np.exp(-t / 0.9)
           + 0.35 * np.sin(2 * np.pi * f * 4.0 * t) * np.exp(-t / 0.18)
           + 0.12 * np.sin(2 * np.pi * f * 9.2 * t) * np.exp(-t / 0.06))
    return out * np.minimum(1.0, t / 0.0015)


def harp(midi, sr, length=2.4, brightness=1.0):
    t = time_axis(length, sr)
    f = hz(midi)
    out = np.zeros_like(t)
    for k in range(1, 12):
        if f * k > sr / 2.2:
            break
        amp = abs(np.sin(np.pi * k * 0.17)) / k ** (1.4 - 0.3 * brightness)
        decay = 2.4 / (1.0 + 0.45 * k ** 1.25)
        out += amp * np.sin(2 * np.pi * f * k * t + k) * np.exp(-t / decay)
    return out * np.minimum(1.0, t / 0.003)


def flute(midi, seconds, sr, seed=0):
    release = 0.15
    t = time_axis(seconds + release, sr)
    f = hz(midi)
    vib = 1.0 + 0.0045 * np.sin(2 * np.pi * 5.2 * t) * np.clip((t - 0.25) / 0.3, 0.0, 1.0)
    phase = 2 * np.pi * np.cumsum(f * vib) / sr
    tone = np.sin(phase) + 0.22 * np.sin(2 * phase) + 0.1 * np.sin(3 * phase) + 0.04 * np.sin(4 * phase)
    breath = bandpass(rng(seed).standard_normal(len(t)), f * 1.5, min(f * 4, 12000), sr) * 0.12
    env = np.minimum(1.0, t / 0.06) * np.where(t > seconds, np.exp(-(t - seconds) / 0.05), 1.0)
    return (tone + breath) * env


def bass(midi, seconds, sr):
    release = 0.12
    t = time_axis(seconds + release, sr)
    f = hz(midi)
    tone = np.sin(2 * np.pi * f * t) + 0.35 * np.sin(4 * np.pi * f * t) + 0.12 * np.sin(6 * np.pi * f * t)
    env = np.minimum(1.0, t / 0.006) * (0.65 + 0.35 * np.exp(-t / 0.15))
    env *= np.where(t > seconds, np.exp(-(t - seconds) / 0.04), 1.0)
    return lowpass(tone * env, 900, sr)


def kick(sr, seed=0):
    t = time_axis(0.35, sr)
    f = 48 + 95 * np.exp(-t / 0.028)
    body = np.sin(2 * np.pi * np.cumsum(f) / sr) * np.exp(-t / 0.2)
    click = lowpass(rng(seed).standard_normal(len(t)), 3000, sr) * np.exp(-t / 0.004) * 0.3
    return body + click


def hand_drum(sr, seed=0, pitch=1.0):
    t = time_axis(0.4, sr)
    f = (135 + 70 * np.exp(-t / 0.018)) * pitch
    body = np.sin(2 * np.pi * np.cumsum(f) / sr) * np.exp(-t / 0.16)
    slap = bandpass(rng(seed).standard_normal(len(t)), 250, 2500, sr) * np.exp(-t / 0.02) * 0.45
    return body + slap


def shaker(sr, seed=0):
    t = time_axis(0.12, sr)
    noise = highpass(rng(seed).standard_normal(len(t)), 5500, sr)
    return noise * np.minimum(1.0, t / 0.008) * np.exp(-t / 0.035)


def woodblock(sr, freq=1050.0):
    t = time_axis(0.15, sr)
    return (np.sin(2 * np.pi * freq * t) + 0.4 * np.sin(2 * np.pi * freq * 2.47 * t)) * np.exp(-t / 0.03)


def clap(sr, seed=0):
    t = time_axis(0.25, sr)
    noise = bandpass(rng(seed).standard_normal(len(t)), 900, 4000, sr)
    env = np.exp(-t / 0.06)
    for offset in (0.0, 0.011, 0.022):
        env += 0.6 * np.exp(-np.clip(t - offset, 0, None) / 0.004) * (t >= offset)
    return noise * env * 0.5


def bird(sr, seed):
    r = rng(seed)
    out = np.zeros(int(0.6 * sr))
    pos = 0
    for _ in range(r.integers(2, 4)):
        n = int(r.uniform(0.05, 0.09) * sr)
        t = np.arange(n) / sr
        f0, f1 = r.uniform(2800, 3600), r.uniform(3800, 5200)
        f = f0 + (f1 - f0) * (t / t[-1]) ** 0.7
        chirp = np.sin(2 * np.pi * np.cumsum(f) / sr) * np.sin(np.pi * t / t[-1]) ** 2
        if pos + n > len(out):
            break
        out[pos:pos + n] += chirp
        pos += n + int(r.uniform(0.03, 0.07) * sr)
    return out


# ---------------------------------------------------------------- track rendering

class Track:
    """Stereo mix of a loop of `length` seconds, rendered twice so the kept second pass has wrapped tails."""

    def __init__(self, length, sr=MUSIC_SR, tail=6.0):
        self.length, self.sr = length, sr
        n = int((2 * length + tail) * sr)
        self.dry = np.zeros((2, n))
        self.wet = np.zeros((2, n))
        self.echo = np.zeros((2, n))

    def add(self, sound, start, gain=1.0, pan=0.0, reverb=0.3, echo=0.0):
        if isinstance(sound, tuple):
            left, right = sound
        else:
            angle = (pan + 1.0) * np.pi / 4.0
            left, right = sound * np.cos(angle), sound * np.sin(angle)
        for copy in (start, start + self.length):
            i = int(copy * self.sr)
            n = min(len(left), self.dry.shape[1] - i)
            if n <= 0:
                continue
            for ch, s in ((0, left), (1, right)):
                self.dry[ch, i:i + n] += s[:n] * gain
                self.wet[ch, i:i + n] += s[:n] * gain * reverb
                self.echo[ch, i:i + n] += s[:n] * gain * echo

    def render(self, reverb_seconds=2.8, echo_time=0.375, echo_feedback=0.38, brightness=6000.0):
        sr = self.sr
        mix = self.dry.copy()
        # Ping-pong delay.
        d = int(echo_time * sr)
        if d > 0 and np.any(self.echo):
            left, right = self.echo[0].copy(), self.echo[1].copy()
            gain = 0.55
            for tap in range(1, 7):
                shift = d * tap
                src = right if tap % 2 else left
                dst = 0 if tap % 2 else 1
                mix[dst, shift:] += lowpass(src[:-shift], 4500, sr) * gain
                self.wet[dst, shift:] += src[:-shift] * gain * 0.5
                gain *= echo_feedback
        # Convolution reverb with a synthetic stereo hall.
        ir_t = time_axis(reverb_seconds, sr)
        r = rng(99)
        ir = []
        for ch in range(2):
            noise = r.standard_normal(len(ir_t))
            env = np.exp(-ir_t * 6.9 / reverb_seconds)
            bright = noise * env
            dark = lowpass(noise, 1800, sr) * env * 2.0
            blend = np.exp(-ir_t * 2.5)
            ir_ch = bright * blend + dark * (1 - blend)
            ir_ch = np.concatenate([np.zeros(int(0.025 * sr)), ir_ch])
            ir.append(ir_ch / np.sqrt(np.sum(ir_ch ** 2)))
        for ch in range(2):
            mix[ch] += signal.fftconvolve(self.wet[ch], ir[ch])[:mix.shape[1]] * 0.9
        mix = lowpass(mix, brightness, sr, 1)
        start, end = int(self.length * sr), int(2 * self.length * sr)
        return mix[:, start:end]


def master(stereo, target_rms_db):
    stereo = stereo - stereo.mean(axis=1, keepdims=True)
    rms = np.sqrt(np.mean(stereo ** 2))
    stereo *= 10 ** (target_rms_db / 20) / max(rms, 1e-9)
    return np.tanh(stereo * 1.1) / np.tanh(1.1) * 0.95  # gentle soft clip, peak < 1


def write_wav(path, data, sr):
    os.makedirs(os.path.dirname(path), exist_ok=True)
    data = np.atleast_2d(data)
    pcm = (np.clip(data, -1.0, 1.0) * 32767).astype('<i2').T.copy()
    with wave.open(path, 'wb') as w:
        w.setnchannels(data.shape[0])
        w.setsampwidth(2)
        w.setframerate(sr)
        w.writeframes(pcm.tobytes())
    print(f'  {os.path.relpath(path, ROOT)}  {data.shape[1] / sr:.2f}s  peak {np.max(np.abs(data)):.2f}')


# ---------------------------------------------------------------- music

def track_menu():
    """'Whispering Grove': calm, airy D-major forest. 80 bpm, 16 bars, 48 s."""
    sr, beat = MUSIC_SR, 60 / 80
    bar = 4 * beat
    tr = Track(16 * bar)
    chords = [
        [50, 57, 61, 64, 66],  # Dmaj9
        [47, 54, 57, 62, 64],  # Bm11
        [43, 50, 54, 59, 61],  # Gmaj7#11
        [45, 52, 59, 61, 64],  # Aadd9
        [42, 50, 57, 64, 66],  # D/F#
        [40, 47, 55, 62, 66],  # Em9
        [43, 50, 54, 57, 59],  # Gmaj9
        [45, 52, 57, 62, 64],  # Asus4
    ]
    r = rng(1)
    for i, chord in enumerate(chords):
        start = i * 2 * bar
        for m in chord:
            tr.add(pad_note(m, 2 * bar, sr, cutoff=2000), start, gain=0.05, reverb=0.45)
        tr.add(pad_note(chord[0] - 12, 2 * bar, sr, attack=2.0, cutoff=500), start, gain=0.07, reverb=0.2)
        # Twinkling celesta over the chord.
        tones = sorted(set(chord[1:]))
        for step in range(16):
            if r.random() < 0.5:
                m = tones[r.integers(len(tones))] + (24 if r.random() < 0.4 else 12)
                tr.add(bell(m, sr, 2.4, bright=0.7), start + step * beat / 2 + r.uniform(0, 0.02),
                       gain=r.uniform(0.025, 0.055), pan=r.uniform(-0.7, 0.7), reverb=0.6, echo=0.35)
    phrase = [  # (bar, beat, midi, beats)
        (0, 0, 78, 2), (0, 2, 81, 2), (1, 0, 76, 3), (1, 3, 74, 1),
        (2, 0, 74, 2), (2, 2, 78, 1), (2, 3, 76, 1), (3, 0, 71, 4),
        (4, 0, 71, 1), (4, 1, 74, 1), (4, 2, 78, 2), (5, 0, 81, 2), (5, 2, 78, 1), (5, 3, 76, 1),
        (6, 0, 76, 2), (6, 2, 73, 2), (7, 0, 76, 4),
    ]
    answer = phrase[:12] + [(6, 0, 71, 1), (6, 1, 74, 1), (6, 2, 78, 1), (6, 3, 81, 1), (7, 0, 81, 2), (7, 2, 76, 2)]
    for half, notes in ((0, phrase), (8, answer)):
        for b, bt, m, length in notes:
            t0 = (half + b) * bar + bt * beat
            tr.add(harp(m, sr, 2.8, 0.8), t0, gain=0.16, pan=-0.15, reverb=0.45, echo=0.2)
            if half:
                tr.add(bell(m + 12, sr, 2.0, 0.5), t0, gain=0.035, pan=0.3, reverb=0.6)
    # Forest air: slow wind and a few birds.
    n = int(tr.length * sr)
    for ch, seed in ((0, 5), (1, 6)):
        wind = lowpass(rng(seed).standard_normal(n), 450, sr) * (0.6 + 0.4 * np.sin(2 * np.pi * np.arange(n) / sr / 12.0 + ch))
        stereo = (wind * 0.06, np.zeros(n)) if ch == 0 else (np.zeros(n), wind * 0.06)
        tr.add(stereo, 0.0, reverb=0.1)
    for k, t0 in enumerate([3.1, 11.8, 19.4, 27.6, 36.2, 43.0]):
        tr.add(highpass(bird(sr, 30 + k), 2000, sr), t0, gain=0.03, pan=r.uniform(-0.9, 0.9), reverb=0.5)
    return master(tr.render(reverb_seconds=3.4, echo_time=beat * 0.75), -20.0)


def track_story():
    """'The Sporelights': mysterious, wondrous B minor with a music-box motif. 72 bpm, 16 bars."""
    sr, beat = MUSIC_SR, 60 / 72
    bar = 4 * beat
    tr = Track(16 * bar)
    chords = [
        [47, 54, 61, 62, 66],  # Bm(add9)
        [43, 50, 54, 59, 62],  # Gmaj7
        [40, 47, 55, 62, 66],  # Em9
        [42, 49, 59, 61, 66],  # F#sus4
        [43, 50, 54, 61, 66],  # Gmaj7#11
        [42, 50, 57, 62, 66],  # D/F#
        [40, 47, 55, 59, 62],  # Em7
        [42, 49, 58, 61, 64],  # F#7
    ]
    r = rng(2)
    for i, chord in enumerate(chords):
        start = i * 2 * bar
        for m in chord:
            tr.add(pad_note(m, 2 * bar, sr, attack=1.8, cutoff=1600), start, gain=0.055, reverb=0.5)
        tr.add(pad_note(chord[0] - 12, 2 * bar, sr, attack=2.5, cutoff=400), start, gain=0.08, reverb=0.2)
        # Rolling harp: up and down the chord in eighths.
        tones = chord[1:] + [chord[2] + 12, chord[3] + 12]
        order = [0, 1, 2, 3, 4, 5, 4, 3, 2, 1, 2, 3, 4, 5, 6, 5]
        for step, idx in enumerate(order):
            m = tones[min(idx, len(tones) - 1)]
            tr.add(harp(m, sr, 2.0, 0.6), start + step * beat / 2, gain=0.05 + 0.02 * (step % 4 == 0),
                   pan=-0.4 + 0.8 * idx / 6, reverb=0.4)
        # Heartbeat drum, like a story being told.
        for b in (0, 2 * bar):
            tr.add(lowpass(hand_drum(sr, i, 0.6), 600, sr), start + b, gain=0.12, reverb=0.35)
            tr.add(lowpass(hand_drum(sr, i + 50, 0.6), 600, sr), start + b + beat * 0.5, gain=0.06, reverb=0.35)
    motif = [  # (bar, beat, midi, beats)
        (0, 0, 78, 1), (0, 1, 81, 0.5), (0, 1.5, 83, 1.5), (0, 3, 86, 1),
        (1, 0, 85, 2), (1, 2, 81, 2),
        (2, 0, 79, 1), (2, 1, 83, 1), (2, 2, 86, 2),
        (3, 0, 85, 3), (3, 3, 82, 1),
        (4, 0, 78, 1), (4, 1, 81, 0.5), (4, 1.5, 83, 1.5), (4, 3, 90, 1),
        (5, 0, 88, 2), (5, 2, 86, 2),
        (6, 0, 83, 1), (6, 1, 86, 1), (6, 2, 79, 2),
        (7, 0, 82, 2), (7, 2, 85, 2),
    ]
    for half in (0, 8):
        for b, bt, m, _ in motif:
            t0 = (half + b) * bar + bt * beat
            tr.add(music_box(m - 12 * (half == 0), sr), t0, gain=0.13, pan=0.2, reverb=0.55, echo=0.3)
    for k, t0 in enumerate(np.arange(0, tr.length, bar)):
        if r.random() < 0.5:
            tr.add(bell(r.choice([90, 93, 95, 97]), sr, 2.4, 0.4), t0 + beat * r.integers(4), gain=0.02,
                   pan=r.uniform(-0.8, 0.8), reverb=0.7, echo=0.4)
    return master(tr.render(reverb_seconds=3.8, echo_time=beat * 0.75), -20.0)


def track_game():
    """'Sporelight Skirmish': agitated folk-fantasy battle in E Phrygian. 150 bpm, 32 bars (~51 s).

    A: galloping bass, war drums and a 16th-note harp ostinato. B: urgent flute melody on top. C: everything plus
    octave bells, string stabs and busier drums. D: half-time war-drum breakdown with a low flute, then a riser and
    a tom roll back into the loop.
    """
    sr, beat = MUSIC_SR, 60 / 150
    bar = 4 * beat
    step = beat / 4
    bars = 32
    tr = Track(bars * bar, tail=4.0)
    chords = [  # one bar each: Em F Em D | C D B B7
        [40, 52, 55, 59], [41, 53, 57, 60], [40, 52, 55, 59], [38, 50, 54, 57],
        [36, 52, 55, 60], [38, 50, 54, 57], [47, 51, 54, 59], [47, 51, 54, 57],
    ]
    melody = [  # (bar, beat, midi, beats)
        (0, 0, 71, .5), (0, .5, 76, .5), (0, 1, 77, .5), (0, 1.5, 79, .5), (0, 2, 77, .5), (0, 2.5, 76, .5), (0, 3, 71, 1),
        (1, 0, 72, .5), (1, .5, 77, .5), (1, 1, 79, .5), (1, 1.5, 81, .5), (1, 2, 79, .5), (1, 2.5, 77, .5), (1, 3, 72, 1),
        (2, 0, 76, .25), (2, .25, 76, .25), (2, .5, 76, .5), (2, 1, 79, .5), (2, 1.5, 83, 1), (2, 2.5, 81, .5),
        (2, 3, 79, .5), (2, 3.5, 77, .5),
        (3, 0, 78, 1.5), (3, 1.5, 76, .5), (3, 2, 74, 1), (3, 3, 69, 1),
        (4, 0, 76, .5), (4, .5, 79, .5), (4, 1, 84, 1), (4, 2, 83, .5), (4, 2.5, 81, .5), (4, 3, 79, 1),
        (5, 0, 78, .5), (5, .5, 81, .5), (5, 1, 86, 1), (5, 2, 84, .5), (5, 2.5, 81, .5), (5, 3, 78, 1),
        (6, 0, 75, .5), (6, .5, 78, .5), (6, 1, 83, 1), (6, 2, 81, .5), (6, 2.5, 78, .5), (6, 3, 75, 1),
        (7, 0, 83, .25), (7, .25, 81, .25), (7, .5, 78, .25), (7, .75, 75, .25), (7, 1, 71, 2),
    ]
    for b in range(bars):
        section = b // 8  # 0 groove, 1 melody, 2 full, 3 breakdown
        chord = chords[b % 8]
        root = chord[0]
        t_bar = b * bar
        last_of_section = b % 8 == 7

        # Low strings pad; short bright stabs on the off-beats in the full section.
        for m in chord[1:]:
            tr.add(pad_note(m, bar, sr, attack=0.15, release=0.5, cutoff=2200), t_bar, gain=0.03, reverb=0.3)
        if section == 2:
            for s in (2, 6, 10, 14):
                for m in chord[1:]:
                    tr.add(pad_note(m + 12, step * 1.2, sr, attack=0.005, release=0.12, cutoff=4000),
                           t_bar + s * step, gain=0.022, pan=0.25, reverb=0.25)

        # Drums.
        half_time = section == 3
        for s in range(16):
            t = t_bar + s * step + (0.008 if s % 2 else 0.0)
            accent = s % 2 == 1
            tr.add(shaker(sr, b * 16 + s), t, gain=0.055 if accent else 0.03, pan=0.35, reverb=0.12)
        for s in ((0, 8) if half_time else (0, 4, 8, 12)):
            tr.add(kick(sr, s), t_bar + s * step, gain=0.34, reverb=0.05)
        if not half_time:
            for s in (4, 12):
                tr.add(clap(sr, b * 3 + s), t_bar + s * step, gain=0.11 + 0.03 * section, pan=0.1, reverb=0.28)
            for s in (2, 6, 10, 14):
                tr.add(hand_drum(sr, b * 5 + s, 1.25), t_bar + s * step, gain=0.1, pan=-0.3, reverb=0.2)
            if section == 2:
                for s in (3, 7, 11, 15):
                    tr.add(hand_drum(sr, b * 7 + s, 1.5), t_bar + s * step, gain=0.05, pan=0.4, reverb=0.2)
        taiko_steps = (0, 6, 8, 11) if half_time else (0,)
        for s in taiko_steps:
            tr.add(lowpass(hand_drum(sr, b * 11 + s, 0.45), 900, sr), t_bar + s * step, gain=0.32, reverb=0.35)
        if last_of_section:  # descending tom fill into the next section
            for k, s in enumerate(range(8, 16)):
                tr.add(hand_drum(sr, 400 + b * 8 + k, 1.6 - 0.12 * k), t_bar + s * step, gain=0.14 + 0.01 * k,
                       pan=-0.6 + 0.17 * k, reverb=0.25)

        # Galloping bass: 8th + two 16ths on every beat, octave jump on beat 4.
        bass_root = root
        while bass_root > 47:
            bass_root -= 12
        for beat_index in range(4):
            note = bass_root + (12 if beat_index == 3 else 0)
            for offset, length in ((0, 2), (2, 1), (3, 1)):
                tr.add(bass(note, length * step * 0.85, sr), t_bar + (beat_index * 4 + offset) * step, gain=0.2, reverb=0.04)

        # Harp ostinato in 16ths (staccato), chord tones an octave up.
        tones = [chord[1] + 12, chord[3] + 12, chord[1] + 24, chord[3] + 12]
        for s in range(16):
            tr.add(harp(tones[s % 4], sr, 0.45, 1.0), t_bar + s * step, gain=0.05 + 0.02 * (s % 4 == 0),
                   pan=0.45 if s % 2 else -0.15, reverb=0.25)

        # Melody: flute in B and C (bells an octave up in C), low and sparse in the breakdown.
        if section in (1, 2, 3):
            for mb, bt, m, length in melody:
                if mb != b % 8:
                    continue
                if section == 3 and bt not in (0, 1, 2, 3):
                    continue
                t0 = t_bar + bt * beat
                pitch = m - 12 if section == 3 else m
                tr.add(flute(pitch, length * beat * 0.9, sr, b * 13 + int(bt * 4)), t0, gain=0.13, pan=-0.1,
                       reverb=0.3, echo=0.1)
                if section == 2:
                    tr.add(bell(m + 12, sr, 1.2, 0.7), t0, gain=0.04, pan=0.3, reverb=0.4)

        # Riser over the last two bars back into the loop.
        if b == bars - 2:
            n = int(2 * bar * sr)
            tt = np.arange(n) / sr
            rise = highpass(rng(500).standard_normal(n), 1500, sr) * (tt / tt[-1]) ** 2 * 0.25
            sweep = np.sin(2 * np.pi * np.cumsum(200 + 700 * (tt / tt[-1]) ** 2) / sr) * (tt / tt[-1]) ** 2 * 0.08
            tr.add(rise + sweep, t_bar, reverb=0.4)

    return master(tr.render(reverb_seconds=2.0, echo_time=beat * 0.75, echo_feedback=0.3, brightness=9500), -15.0)


# ---------------------------------------------------------------- sound effects (mono)

def sfx_pop(f0, seed):
    """Bubble / lip pop: a fast upward pitch blip with a tiny click and some body."""
    sr = SFX_SR
    t = time_axis(0.14, sr)
    f = f0 * (1.0 + 2.3 * (1.0 - np.exp(-t / 0.011)))
    tone = np.sin(2 * np.pi * np.cumsum(f) / sr)
    tone += 0.18 * np.sin(4 * np.pi * np.cumsum(f) / sr)
    env = (1.0 - np.exp(-t / 0.0007)) * np.exp(-t / 0.03)
    click = bandpass(rng(seed).standard_normal(len(t)), 1200, 6000, sr) * np.exp(-t / 0.0012) * 0.35
    body = np.sin(2 * np.pi * 115 * t) * np.exp(-t / 0.018) * 0.35
    return tone * env + click + body


def sfx_splat(seed):
    sr = SFX_SR
    t = time_axis(0.32, sr)
    r = rng(seed)
    f = 140 + 320 * np.exp(-t / 0.025)
    drop = np.sin(2 * np.pi * np.cumsum(f) / sr) * np.exp(-t / 0.05)
    grains = np.zeros_like(t)
    for _ in range(14):
        i = int(r.uniform(0.0, 0.09) * sr)
        n = int(r.uniform(0.003, 0.012) * sr)
        grains[i:i + n] += r.standard_normal(min(n, len(t) - i)) * r.uniform(0.3, 1.0)
    squelch = bandpass(grains + r.standard_normal(len(t)) * 0.3 * np.exp(-t / 0.03), 500, 2600, sr)
    squelch *= np.exp(-t / 0.07)
    thud = np.sin(2 * np.pi * 85 * t) * np.exp(-t / 0.045)
    return drop * 0.8 + squelch * 0.9 + thud * 0.6


def grains_window(seconds, start, end, count, seed, lo, hi, rising=False):
    """Soil grains (short noise bursts) scattered between start and end, band-limited to sound earthy."""
    sr = SFX_SR
    n_total = int(seconds * sr)
    r = rng(seed)
    out = np.zeros(n_total)
    for _ in range(count):
        center = r.uniform(start, end)
        i = int(center * sr)
        n = min(int(r.uniform(0.004, 0.025) * sr), n_total - i)
        if n <= 0:
            continue
        weight = (center - start) / max(end - start, 1e-6) if rising else 1.0
        out[i:i + n] += r.standard_normal(n) * np.hanning(n) * r.uniform(0.3, 1.0) * (0.3 + 0.7 * weight)
    return bandpass(out, lo, hi, sr)


def thud(seconds, at, freq, decay, seed):
    """Dull earth impact: a low sine knock plus a muffled noise puff."""
    sr = SFX_SR
    t = time_axis(seconds, sr)
    dt = np.clip(t - at, 0, None)
    on = t >= at
    knock = np.sin(2 * np.pi * np.cumsum(freq * (1 + 0.6 * np.exp(-dt / 0.02)) * on) / sr) * np.exp(-dt / decay)
    puff = lowpass(rng(seed).standard_normal(len(t)), 320, sr) * np.exp(-dt / (decay * 0.5)) * 1.4
    return (knock + puff) * on


def sfx_plant():
    """Roots driving into the soil: three heavy earthy crunches, a low rumble and a dull settling thud."""
    sr = SFX_SR
    seconds = 1.05
    t = time_axis(seconds, sr)
    out = np.zeros_like(t)
    for k, (at, gain) in enumerate([(0.0, 0.7), (0.27, 0.85), (0.54, 1.0)]):
        out += grains_window(seconds, at, at + 0.11, 34, 30 + k, 90, 1100) * gain
        out += thud(seconds, at, 72, 0.05, 40 + k) * 0.45 * gain
    rumble = lowpass(rng(13).standard_normal(len(t)), 140, sr) * np.sin(np.pi * np.clip(t / 0.95, 0, 1)) * 1.6
    crumble = grains_window(seconds, 0.6, 1.0, 26, 14, 150, 800) * 0.35
    settle = thud(seconds, 0.8, 48, 0.12, 15)
    return out + rumble + crumble + settle


def sfx_unplant():
    """Roots tearing out of the ground: a woody creak, crackling soil, then a heavy dull pull and falling earth."""
    sr = SFX_SR
    seconds = 0.95
    t = time_axis(seconds, sr)
    r = rng(24)
    f = 95 + 70 * (t / 0.6) + r.normal(0, 6, len(t)).cumsum() * 0.002
    phase = 2 * np.pi * np.cumsum(f) / sr
    stick_slip = (np.sin(2 * np.pi * 23 * t) > 0.2).astype(float)
    creak = lowpass((np.sin(phase) + 0.6 * np.sin(2 * phase) + 0.3 * np.sin(3 * phase)) * stick_slip, 1200, sr)
    creak *= np.clip(t / 0.15, 0, 1) * np.clip((0.66 - t) / 0.08, 0, 1) * 0.35
    crackle = grains_window(seconds, 0.05, 0.66, 70, 25, 120, 1400, rising=True)
    pull = thud(seconds, 0.62, 62, 0.11, 26) * 1.1
    spill = grains_window(seconds, 0.64, 0.92, 40, 27, 100, 900) * 0.6
    return creak + crackle + pull + spill


def sfx_root_lock():
    """'Ready': roots locked in. A soft low double knock with a little crumble (no bright chime)."""
    seconds = 0.5
    return (thud(seconds, 0.0, 66, 0.07, 50) + thud(seconds, 0.09, 58, 0.09, 51) * 0.8 +
            grains_window(seconds, 0.02, 0.3, 14, 52, 120, 700) * 0.4)


def sfx_chord(notes, spacing, length, voice=bell, shimmer=0.0, gains=None):
    sr = SFX_SR
    total = int((length + spacing * len(notes)) * sr)
    out = np.zeros(total)
    for k, m in enumerate(notes):
        s = voice(m, sr, length)
        i = int(k * spacing * sr)
        out[i:i + len(s)] += s * (gains[k] if gains else 1.0)
    if shimmer:
        t = np.arange(total) / sr
        sparkle = highpass(rng(len(notes)).standard_normal(total), 7000, sr) * np.exp(-t / 0.35)
        out += sparkle * shimmer
    return out


def sfx_tick(freq):
    sr = SFX_SR
    t = time_axis(0.12, sr)
    return (np.sin(2 * np.pi * freq * t) + 0.35 * np.sin(2 * np.pi * freq * 2.4 * t)) * np.exp(-t / 0.025)


def sfx_hitmarker():
    sr = SFX_SR
    t = time_axis(0.11, sr)
    return (np.sin(2 * np.pi * 1900 * t) + 0.3 * np.sin(2 * np.pi * 3800 * t)) * np.exp(-t / 0.03)


def sfx_hurt():
    sr = SFX_SR
    t = time_axis(0.4, sr)
    f = 60 + 90 * np.exp(-t / 0.06)
    thump = np.sin(2 * np.pi * np.cumsum(f) / sr) * np.exp(-t / 0.12)
    muffle = lowpass(rng(41).standard_normal(len(t)), 700, sr) * np.exp(-t / 0.05) * 0.6
    return thump + muffle


def sfx_knockout():
    """A wilting slide down with a soft plop at the end."""
    sr = SFX_SR
    t = time_axis(1.1, sr)
    f = 150 + 450 * np.exp(-t / 0.35)
    f *= 1.0 + 0.03 * np.sin(2 * np.pi * 7 * t)
    tone = np.sin(2 * np.pi * np.cumsum(f) / sr) + 0.3 * np.sin(4 * np.pi * np.cumsum(f) / sr)
    tone *= np.minimum(1.0, t / 0.02) * np.exp(-t / 0.45)
    plop = np.zeros_like(t)
    s = sfx_splat(42)
    i = int(0.75 * sr)
    plop[i:i + len(s)] = s[:len(t) - i]
    return lowpass(tone, 3000, sr) * 0.7 + plop * 0.5


def sfx_page():
    sr = SFX_SR
    t = time_axis(0.45, sr)
    noise = bandpass(rng(51).standard_normal(len(t)), 900, 6000, sr)
    return noise * np.sin(np.pi * t / t[-1]) ** 3 * 0.6


def sfx_click():
    sr = SFX_SR
    s = harp(84, sr, 0.25, 1.0)
    t = np.arange(len(s)) / sr
    return s * np.exp(-t / 0.06) + bandpass(rng(61).standard_normal(len(s)), 2000, 8000, sr) * np.exp(-t / 0.002) * 0.3


def stinger(kind):
    sr = SFX_SR
    length = 3.4
    n = int(length * sr)
    out = np.zeros(n)

    def put(sound, at, gain):
        i = int(at * sr)
        m = min(len(sound), n - i)
        out[i:i + m] += sound[:m] * gain

    if kind == 'win':  # bright E major run into a held chord
        for k, m in enumerate([64, 68, 71, 76, 80, 83]):
            put(harp(m, sr, 2.5), k * 0.08, 0.5)
        for m in (76, 80, 83, 88):
            put(bell(m, sr, 2.8), 0.55, 0.3)
        for m in (52, 59, 64, 68):
            l, _ = pad_note(m, 1.8, sr, attack=0.25, release=1.2, cutoff=3000)
            put(l, 0.5, 0.18)
    elif kind == 'lose':  # drooping minor line
        for k, m in enumerate([76, 74, 72, 71]):
            put(music_box(m, sr, 2.0), k * 0.35, 0.6)
        for m in (40, 47, 52, 55):
            l, _ = pad_note(m, 1.6, sr, attack=0.6, release=1.4, cutoff=1200)
            put(l, 0.9, 0.18)
    else:  # draw: suspended, unresolved
        for k, m in enumerate([64, 69, 71, 76]):
            put(bell(m, sr, 2.5), k * 0.15, 0.4)
        for m in (52, 57, 59):
            l, _ = pad_note(m, 1.6, sr, attack=0.4, release=1.4, cutoff=1800)
            put(l, 0.5, 0.18)
    rev = signal.fftconvolve(out, np.exp(-time_axis(1.5, sr) * 4.6) * rng(71).standard_normal(int(1.5 * sr)))[:n]
    return out + rev / np.max(np.abs(rev)) * np.max(np.abs(out)) * 0.25


def normalize(x, peak=0.9):
    x = x - np.mean(x)
    x = x * fade(len(x), 0.0, 0.01, SFX_SR)
    return x / max(np.max(np.abs(x)), 1e-9) * peak


def make_sfx():
    out = os.path.join(ROOT, 'Sfx')
    sounds = {
        'pop_0': normalize(sfx_pop(250, 1)),
        'pop_1': normalize(sfx_pop(290, 2)),
        'pop_2': normalize(sfx_pop(330, 3)),
        'splat_0': normalize(sfx_splat(4), 0.8),
        'splat_1': normalize(sfx_splat(5), 0.8),
        'plant': normalize(sfx_plant(), 0.8),
        'unplant': normalize(sfx_unplant(), 0.8),
        'ready': normalize(sfx_root_lock(), 0.55),
        'collect': normalize(sfx_chord([86, 90, 93, 98], 0.07, 1.2, bell, shimmer=0.15), 0.8),
        'appear': normalize(sfx_chord([93, 100], 0.12, 1.0, bell, shimmer=0.08), 0.5),
        'hitmarker': normalize(sfx_hitmarker(), 0.6),
        'kill': normalize(sfx_chord([88, 95, 100], 0.09, 0.9, bell, shimmer=0.1), 0.8),
        'hurt': normalize(sfx_hurt(), 0.85),
        'knockout': normalize(sfx_knockout(), 0.8),
        'respawn': normalize(sfx_chord([76, 79, 81, 83, 86, 88], 0.045, 0.9, harp, shimmer=0.06), 0.6),
        'tick': normalize(sfx_tick(1000), 0.5),
        'tick_final': normalize(sfx_tick(1500), 0.6),
        'go': normalize(sfx_chord([64, 71, 76, 80, 83, 88], 0.04, 1.4, harp, shimmer=0.12), 0.85),
        'page': normalize(sfx_page(), 0.35),
        'click': normalize(sfx_click(), 0.5),
        'win': normalize(stinger('win'), 0.85),
        'lose': normalize(stinger('lose'), 0.8),
        'draw': normalize(stinger('draw'), 0.8),
    }
    for name, data in sounds.items():
        write_wav(os.path.join(out, name + '.wav'), data, SFX_SR)


def main(args):
    tracks = {'menu': track_menu, 'story': track_story, 'game': track_game}
    wanted = args or ['sfx'] + list(tracks)
    if 'sfx' in wanted:
        print('Sound effects')
        make_sfx()
    for name, build in tracks.items():
        if name in wanted:
            print(f'Music: {name}')
            write_wav(os.path.join(ROOT, 'Music', name + '.wav'), build(), MUSIC_SR)


if __name__ == '__main__':
    main(sys.argv[1:])
