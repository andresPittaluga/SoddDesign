using UnityEngine;

namespace Sodd.Design
{
    /// <summary>
    /// Base de los DSP propios (OnAudioFilterRead). Thread-safety: el main thread solo escribe floats volatile;
    /// el audio thread solo lee parámetros y escribe su propio estado. Nada de API de Unity en el audio thread.
    /// Los buffers se reservan en Awake (main thread): cero allocations en el audio thread.
    /// </summary>
    public abstract class SoddDspFilter : MonoBehaviour
    {
        protected int sampleRate = 48000;
        volatile bool _reset;

        protected virtual void Awake()
        {
            sampleRate = AudioSettings.outputSampleRate > 0 ? AudioSettings.outputSampleRate : 48000;
            hideFlags = HideFlags.HideInInspector;
        }

        /// <summary>Pide limpiar el estado (colas de delay, envolventes) al reutilizar la voz.</summary>
        public void RequestReset() => _reset = true;

        /// <summary>Setea parámetros desde el main thread (en el orden del catálogo de efectos).</summary>
        public abstract void SetParam(int index, float value);

        void OnAudioFilterRead(float[] data, int channels) => Render(data, channels);

        /// <summary>Procesa un buffer intercalado (lo llama el audio thread; público para tests offline).</summary>
        public void Render(float[] data, int channels)
        {
            if (_reset) { _reset = false; ResetState(); }
            Process(data, channels);
        }

        protected abstract void ResetState();
        protected abstract void Process(float[] data, int channels);
    }

    [AddComponentMenu(""), ExecuteAlways]
    public sealed class SoddBitcrusher : SoddDspFilter
    {
        volatile float _bits = 8, _down = 4, _mix = 1;
        float _hold0, _hold1; int _count;
        public override void SetParam(int i, float v) { if (i == 0) _bits = v; else if (i == 1) _down = v; else _mix = v; }
        protected override void ResetState() { _hold0 = _hold1 = 0; _count = 0; }
        protected override void Process(float[] d, int ch)
        {
            float levels = Mathf.Pow(2f, Mathf.Clamp(_bits, 1f, 16f) - 1f);
            int down = Mathf.Max(1, (int)_down); float mix = _mix;
            for (int i = 0; i < d.Length; i += ch)
            {
                if (_count++ % down == 0)
                {
                    _hold0 = Mathf.Round(d[i] * levels) / levels;
                    _hold1 = ch > 1 ? Mathf.Round(d[i + 1] * levels) / levels : _hold0;
                }
                d[i] += (_hold0 - d[i]) * mix;
                if (ch > 1) d[i + 1] += (_hold1 - d[i + 1]) * mix;
            }
        }
    }

    /// <summary>
    /// Volume LFO shaped like FMOD's tremolo: shape morphs triangle (0) to sine (1), duty is the share of the cycle
    /// spent in the loud half, skew moves the peak inside each half, square flattens toward a square wave and phase
    /// offsets the start of the cycle.
    /// </summary>
    [AddComponentMenu(""), ExecuteAlways]
    public sealed class SoddTremolo : SoddDspFilter
    {
        volatile float _rate = 5, _depth = 0.5f, _shape = 1f, _skew, _duty = 0.5f, _square, _phaseOffset;
        double _phase;

        public override void SetParam(int i, float v)
        {
            switch (i)
            {
                case 0: _rate = v; break;
                case 1: _depth = v; break;
                case 2: _shape = v; break;
                case 3: _skew = v; break;
                case 4: _duty = v; break;
                case 5: _square = v; break;
                default: _phaseOffset = v; break;
            }
        }

        protected override void ResetState() => _phase = 0;

        /// <summary>The LFO in -1..1 at cycle position t (public for tests).</summary>
        public static float Lfo(float t, float shape, float skew, float duty, float square)
        {
            t -= Mathf.Floor(t);
            duty = Mathf.Clamp(duty, 0.01f, 0.99f);
            bool high = t < duty;
            float x = high ? t / duty : (t - duty) / (1f - duty);
            float peak = Mathf.Clamp(0.5f * (1f + skew), 0.01f, 0.99f);
            x = x < peak ? 0.5f * x / peak : 0.5f + 0.5f * (x - peak) / (1f - peak);
            float tri = 1f - Mathf.Abs(2f * x - 1f);
            float sine = Mathf.Sin(Mathf.PI * x);
            float h = Mathf.Lerp(tri, sine, Mathf.Clamp01(shape));
            h = Mathf.Lerp(h, h > 0f ? 1f : 0f, Mathf.Clamp01(square));
            return high ? h : -h;
        }

        protected override void Process(float[] d, int ch)
        {
            double inc = _rate / sampleRate; float depth = Mathf.Clamp01(_depth);
            float shape = _shape, skew = _skew, duty = _duty, square = _square, offset = _phaseOffset;
            for (int i = 0; i < d.Length; i += ch)
            {
                float lfo = Lfo((float)_phase + offset, shape, skew, duty, square);
                float g = 1f - depth * 0.5f * (1f - lfo);
                _phase += inc; if (_phase >= 1.0) _phase -= 1.0;
                for (int c = 0; c < ch; c++) d[i + c] *= g;
            }
        }
    }

    /// <summary>
    /// Three band EQ like FMOD's: a low shelf at the low crossover and a high shelf at the high crossover, relative to
    /// the mid gain. Steeper slopes cascade more shelves. With every gain at 0 dB the signal passes unchanged.
    /// </summary>
    [AddComponentMenu(""), ExecuteAlways]
    public sealed class SoddThreeEQ : SoddDspFilter
    {
        const int MaxChannels = 8, MaxStages = 4;
        volatile float _low, _mid, _high, _lowX = 400f, _highX = 4000f, _slope = 24f;
        float _cLow = float.NaN, _cMid, _cHigh, _cLowX, _cHighX, _cSlope;
        readonly Biquad[] _ls = new Biquad[MaxChannels * MaxStages], _hs = new Biquad[MaxChannels * MaxStages];
        Coeffs _lsC, _hsC;
        float _midGain = 1f;
        int _stages = 2;

        struct Coeffs { public float b0, b1, b2, a1, a2; }

        struct Biquad
        {
            float _z1, _z2;
            public float Run(float x, in Coeffs c)
            {
                float y = c.b0 * x + _z1;
                _z1 = c.b1 * x - c.a1 * y + _z2;
                _z2 = c.b2 * x - c.a2 * y;
                return y;
            }
        }

        public override void SetParam(int i, float v)
        {
            switch (i)
            {
                case 0: _low = v; break;
                case 1: _mid = v; break;
                case 2: _high = v; break;
                case 3: _lowX = v; break;
                case 4: _highX = v; break;
                default: _slope = v; break;
            }
        }

        protected override void ResetState()
        {
            for (int i = 0; i < _ls.Length; i++) { _ls[i] = default; _hs[i] = default; }
        }

        // RBJ cookbook shelf, slope S = 1.
        Coeffs Shelf(float freq, float db, bool high)
        {
            float a = Mathf.Pow(10f, db / 40f);
            float w = 2f * Mathf.PI * Mathf.Clamp(freq, 10f, sampleRate * 0.45f) / sampleRate;
            float cos = Mathf.Cos(w), alpha = Mathf.Sin(w) * 0.5f * Mathf.Sqrt(2f), k = 2f * Mathf.Sqrt(a) * alpha;
            float b0, b1, b2, a0, a1, a2;
            if (high)
            {
                b0 = a * ((a + 1) + (a - 1) * cos + k); b1 = -2 * a * ((a - 1) + (a + 1) * cos); b2 = a * ((a + 1) + (a - 1) * cos - k);
                a0 = (a + 1) - (a - 1) * cos + k; a1 = 2 * ((a - 1) - (a + 1) * cos); a2 = (a + 1) - (a - 1) * cos - k;
            }
            else
            {
                b0 = a * ((a + 1) - (a - 1) * cos + k); b1 = 2 * a * ((a - 1) - (a + 1) * cos); b2 = a * ((a + 1) - (a - 1) * cos - k);
                a0 = (a + 1) + (a - 1) * cos + k; a1 = -2 * ((a - 1) + (a + 1) * cos); a2 = (a + 1) + (a - 1) * cos - k;
            }
            return new Coeffs { b0 = b0 / a0, b1 = b1 / a0, b2 = b2 / a0, a1 = a1 / a0, a2 = a2 / a0 };
        }

        protected override void Process(float[] d, int ch)
        {
            float low = _low, mid = _mid, high = _high, lowX = _lowX, highX = Mathf.Max(_highX, _lowX + 1f), slope = _slope;
            if (low != _cLow || mid != _cMid || high != _cHigh || lowX != _cLowX || highX != _cHighX || slope != _cSlope)
            {
                _cLow = low; _cMid = mid; _cHigh = high; _cLowX = lowX; _cHighX = highX; _cSlope = slope;
                _stages = slope <= 12f ? 1 : slope <= 24f ? 2 : MaxStages;
                _lsC = Shelf(lowX, (low - mid) / _stages, false);
                _hsC = Shelf(highX, (high - mid) / _stages, true);
                _midGain = Mathf.Pow(10f, mid / 20f);
            }
            int n = Mathf.Min(ch, MaxChannels);
            for (int i = 0; i < d.Length; i += ch)
                for (int c = 0; c < n; c++)
                {
                    float x = d[i + c] * _midGain;
                    for (int s = 0; s < _stages; s++) x = _ls[c * MaxStages + s].Run(x, _lsC);
                    for (int s = 0; s < _stages; s++) x = _hs[c * MaxStages + s].Run(x, _hsC);
                    d[i + c] = x;
                }
        }
    }

    [AddComponentMenu(""), ExecuteAlways]
    public sealed class SoddAutoPan : SoddDspFilter
    {
        volatile float _rate = 1, _depth = 1;
        double _phase;
        public override void SetParam(int i, float v) { if (i == 0) _rate = v; else _depth = v; }
        protected override void ResetState() => _phase = 0;
        protected override void Process(float[] d, int ch)
        {
            if (ch < 2) return;
            double inc = _rate / sampleRate; float depth = Mathf.Clamp01(_depth);
            for (int i = 0; i < d.Length; i += ch)
            {
                float pan = depth * Mathf.Sin((float)(_phase * 2.0 * Mathf.PI)); // -1..1
                _phase += inc; if (_phase >= 1.0) _phase -= 1.0;
                float a = (pan + 1f) * 0.25f * Mathf.PI;
                d[i] *= Mathf.Cos(a) * 1.4142f;
                d[i + 1] *= Mathf.Sin(a) * 1.4142f;
            }
        }
    }

    [AddComponentMenu(""), ExecuteAlways]
    public sealed class SoddRingMod : SoddDspFilter
    {
        volatile float _freq = 440, _mix = 0.5f;
        double _phase;
        public override void SetParam(int i, float v) { if (i == 0) _freq = v; else _mix = v; }
        protected override void ResetState() => _phase = 0;
        protected override void Process(float[] d, int ch)
        {
            double inc = _freq / sampleRate; float mix = Mathf.Clamp01(_mix);
            for (int i = 0; i < d.Length; i += ch)
            {
                float m = Mathf.Sin((float)(_phase * 2.0 * Mathf.PI));
                _phase += inc; if (_phase >= 1.0) _phase -= 1.0;
                for (int c = 0; c < ch; c++) d[i + c] = d[i + c] * (1f - mix) + d[i + c] * m * mix;
            }
        }
    }

    [AddComponentMenu(""), ExecuteAlways]
    public sealed class SoddSaturator : SoddDspFilter
    {
        volatile float _drive = 2, _mix = 1, _out;
        public override void SetParam(int i, float v) { if (i == 0) _drive = v; else if (i == 1) _mix = v; else _out = v; }
        protected override void ResetState() { }
        protected override void Process(float[] d, int ch)
        {
            float drive = Mathf.Max(1f, _drive), mix = Mathf.Clamp01(_mix);
            float outGain = Mathf.Pow(10f, _out / 20f);
            float norm = 1f / (float)System.Math.Tanh(drive);
            for (int i = 0; i < d.Length; i++)
            {
                float x = d[i];
                float y = (float)System.Math.Tanh(x * drive) * norm;
                d[i] = (x + (y - x) * mix) * outGain;
            }
        }
    }

    [AddComponentMenu(""), ExecuteAlways]
    public sealed class SoddSimpleDelay : SoddDspFilter
    {
        const float MaxSeconds = 2.05f;
        volatile float _time = 0.3f, _feedback = 0.4f, _mix = 0.3f;
        float[] _buf;
        int _write, _len;
        protected override void Awake()
        {
            base.Awake();
            _len = (int)(sampleRate * MaxSeconds) * 2;
            _buf = new float[_len];
        }
        public override void SetParam(int i, float v) { if (i == 0) _time = v; else if (i == 1) _feedback = v; else _mix = v; }
        protected override void ResetState() { System.Array.Clear(_buf, 0, _buf.Length); _write = 0; }
        protected override void Process(float[] d, int ch)
        {
            if (_buf == null) return;
            int frames = _len / 2;
            int delay = Mathf.Clamp((int)(_time * sampleRate), 1, frames - 1);
            float fb = Mathf.Clamp(_feedback, 0f, 0.95f), mix = Mathf.Clamp01(_mix);
            for (int i = 0; i < d.Length; i += ch)
            {
                int read = _write - delay; if (read < 0) read += frames;
                for (int c = 0; c < ch && c < 2; c++)
                {
                    float wet = _buf[read * 2 + c];
                    float x = d[i + c];
                    _buf[_write * 2 + c] = x + wet * fb;
                    d[i + c] = x + wet * mix;
                }
                if (++_write >= frames) _write = 0;
            }
        }
    }

    [AddComponentMenu(""), ExecuteAlways]
    public sealed class SoddSimpleCompressor : SoddDspFilter
    {
        volatile float _threshold = -18, _ratio = 4, _attack = 10, _release = 100, _makeup;
        float _env;
        /// <summary>Reducción de ganancia actual en dB (para medidores).</summary>
        public volatile float GainReductionDb;
        public override void SetParam(int i, float v)
        {
            switch (i) { case 0: _threshold = v; break; case 1: _ratio = v; break; case 2: _attack = v; break; case 3: _release = v; break; default: _makeup = v; break; }
        }
        protected override void ResetState() { _env = 0; GainReductionDb = 0; }
        protected override void Process(float[] d, int ch)
        {
            float atk = Mathf.Exp(-1f / (Mathf.Max(0.1f, _attack) * 0.001f * sampleRate));
            float rel = Mathf.Exp(-1f / (Mathf.Max(1f, _release) * 0.001f * sampleRate));
            float thr = _threshold, ratio = Mathf.Max(1f, _ratio), makeup = Mathf.Pow(10f, _makeup / 20f);
            float maxGr = 0f;
            for (int i = 0; i < d.Length; i += ch)
            {
                float peak = 0f;
                for (int c = 0; c < ch; c++) { float a = Mathf.Abs(d[i + c]); if (a > peak) peak = a; }
                _env = peak > _env ? atk * _env + (1f - atk) * peak : rel * _env + (1f - rel) * peak;
                float db = _env > 1e-6f ? 20f * Mathf.Log10(_env) : -120f;
                float over = db - thr;
                float gr = over > 0f ? over - over / ratio : 0f;
                if (gr > maxGr) maxGr = gr;
                float g = Mathf.Pow(10f, -gr / 20f) * makeup;
                for (int c = 0; c < ch; c++) d[i + c] *= g;
            }
            GainReductionDb = maxGr;
        }
    }
}
