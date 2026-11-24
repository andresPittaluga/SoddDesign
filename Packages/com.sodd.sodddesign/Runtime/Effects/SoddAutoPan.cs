using UnityEngine;

namespace Sodd.Design
{
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
}
