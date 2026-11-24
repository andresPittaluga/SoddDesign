using UnityEngine;

namespace Sodd.Design
{
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
}
