using UnityEngine;

namespace Sodd.Design
{
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
}
