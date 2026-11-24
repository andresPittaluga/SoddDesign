"""Genera audios de prueba procedurales para SoddDesign (Assets/Sandbox/Audio).
Uso: python Tools/gen_test_audio.py   (determinista: misma semilla, mismos archivos)"""
import math, random, struct, wave, os

SR = 44100
OUT = os.path.join(os.path.dirname(__file__), '..', 'Assets', 'Sandbox', 'Audio')
random.seed(1234)

def write(name, samples, channels=1):
    os.makedirs(OUT, exist_ok=True)
    path = os.path.join(OUT, name + '.wav')
    with wave.open(path, 'wb') as w:
        w.setnchannels(channels); w.setsampwidth(2); w.setframerate(SR)
        data = bytearray()
        for s in samples:
            v = max(-1.0, min(1.0, s))
            data += struct.pack('<h', int(v * 32767))
        w.writeframes(bytes(data))
    print('wrote', path)

def env(i, n, a=0.005, r=0.05):
    t = i / SR; dur = n / SR
    g = 1.0
    if t < a: g = t / a
    if t > dur - r: g = min(g, max(0.0, (dur - t) / r))
    return g

def beep(freq, dur, amp=0.5):
    n = int(SR * dur)
    return [amp * math.sin(2 * math.pi * freq * i / SR) * env(i, n) for i in range(n)]

def noise(dur, amp=0.4):
    n = int(SR * dur)
    return [amp * random.uniform(-1, 1) * env(i, n, 0.01, 0.2) for i in range(n)]

def sweep(f0, f1, dur, amp=0.5):
    n = int(SR * dur); ph = 0.0; out = []
    for i in range(n):
        f = f0 * (f1 / f0) ** (i / n)
        ph += 2 * math.pi * f / SR
        out.append(amp * math.sin(ph) * env(i, n))
    return out

def kick(dur=0.5, amp=0.9):
    n = int(SR * dur); ph = 0.0; out = []
    for i in range(n):
        t = i / SR
        f = 45 + 120 * math.exp(-t * 30)
        ph += 2 * math.pi * f / SR
        out.append(amp * math.sin(ph) * math.exp(-t * 7))
    return out

def snare(dur=0.3, amp=0.6):
    n = int(SR * dur); out = []
    for i in range(n):
        t = i / SR
        out.append(amp * (0.6 * random.uniform(-1, 1) + 0.4 * math.sin(2 * math.pi * 190 * t)) * math.exp(-t * 18))
    return out

def hat(dur=0.08, amp=0.3):
    n = int(SR * dur); prev = 0.0; out = []
    for i in range(n):
        x = random.uniform(-1, 1); hp = x - prev; prev = x
        out.append(amp * hp * math.exp(-i / SR * 60))
    return out

def mix_into(buf, src, at):
    for i, s in enumerate(src):
        j = at + i
        if j < len(buf): buf[j] += s

def music_loop(bpm=120, bars=2):
    beat = 60.0 / bpm; n = int(SR * beat * 4 * bars); buf = [0.0] * n
    bass = [55, 55, 65.41, 73.42, 55, 55, 82.41, 73.42]
    for b in range(4 * bars):
        at = int(b * beat * SR)
        if b % 2 == 0: mix_into(buf, kick(0.4, 0.7), at)
        else: mix_into(buf, snare(0.25, 0.4), at)
        mix_into(buf, hat(0.06, 0.2), at + int(beat * SR / 2))
        f = bass[b % len(bass)]; m = int(beat * SR * 0.9)
        mix_into(buf, [0.25 * math.copysign(1, math.sin(2 * math.pi * f * i / SR)) * 0.5 * env(i, m, 0.005, 0.03) for i in range(m)], at)
    return [s * 0.8 for s in buf]

def drone(dur=4.0, f=110):
    n = int(SR * dur)
    return [0.3 * (math.sin(2*math.pi*f*i/SR) + 0.5*math.sin(2*math.pi*f*1.5*i/SR) + 0.3*math.sin(2*math.pi*f*2.01*i/SR)) / 1.8 for i in range(n)]

def footstep(seed):
    rnd = random.Random(seed); n = int(SR * 0.18); out = []; lp = 0.0
    k = 0.15 + 0.1 * rnd.random()
    for i in range(n):
        lp += k * (rnd.uniform(-1, 1) - lp)
        out.append(1.6 * lp * math.exp(-i / SR * (25 + 10 * rnd.random())))
    return out

write('beep_440', beep(440, 0.25))
write('beep_880', beep(880, 0.25))
write('beep_220', beep(220, 0.4))
write('noise_burst', noise(0.6))
write('sweep_up', sweep(200, 4000, 1.0))
write('sweep_down', sweep(4000, 200, 1.0))
write('kick', kick())
write('snare', snare())
write('hihat', hat())
for k in range(4): write('footstep_%02d' % (k + 1), footstep(100 + k))
write('music_loop_120bpm', music_loop())
write('drone_110', drone())
write('ui_click', beep(1500, 0.04, 0.4))
