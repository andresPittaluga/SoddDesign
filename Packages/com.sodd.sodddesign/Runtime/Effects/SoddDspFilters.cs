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

    [AddComponentMenu(""), ExecuteAlways]
    public sealed class SoddTremolo : SoddDspFilter
    {
        volatile float _rate = 5, _depth = 0.5f;
        double _phase;
        public override void SetParam(int i, float v) { if (i == 0) _rate = v; else _depth = v; }
        protected override void ResetState() => _phase = 0;
        protected override void Process(float[] d, int ch)
        {
            double inc = _rate / sampleRate; float depth = Mathf.Clamp01(_depth);
            for (int i = 0; i < d.Length; i += ch)
            {
                float g = 1f - depth * 0.5f * (1f + Mathf.Sin((float)(_phase * 2.0 * Mathf.PI)));
                _phase += inc; if (_phase >= 1.0) _phase -= 1.0;
                for (int c = 0; c < ch; c++) d[i + c] *= g;
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
