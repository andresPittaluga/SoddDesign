using UnityEngine;

namespace Sodd.Design
{
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
}
