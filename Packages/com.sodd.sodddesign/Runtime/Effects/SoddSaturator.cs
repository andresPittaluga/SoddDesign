using UnityEngine;

namespace Sodd.Design
{
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
}
