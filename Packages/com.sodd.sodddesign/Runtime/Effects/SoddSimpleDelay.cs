using UnityEngine;

namespace Sodd.Design
{
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
}
