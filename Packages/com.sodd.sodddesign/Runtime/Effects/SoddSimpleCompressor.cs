using UnityEngine;

namespace Sodd.Design
{
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
