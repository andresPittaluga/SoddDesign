using UnityEngine;

namespace Sodd.Design
{
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
}
