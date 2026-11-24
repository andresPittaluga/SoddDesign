using UnityEngine;

namespace Sodd.Design
{
    /// <summary>Contexto para evaluar moduladores de una instancia/voz. Struct: sin allocations.</summary>
    public struct SoddModContext
    {
        public float[] local;
        public float[] global;
        /// <summary>Segundos desde que arrancó la instancia (o la voz, para props de instrumento).</summary>
        public float time;
        /// <summary>Segundos desde que se pidió Stop (&lt; 0 si no se está deteniendo).</summary>
        public float stopTime;
        /// <summary>Semilla por disparo (cambia en cada voz/región disparada).</summary>
        public uint triggerSeed;
        /// <summary>Semilla por instancia (fija durante la vida de la instancia).</summary>
        public uint instanceSeed;
    }

    /// <summary>Evaluación de propiedades modulables. Se llama por instancia en Update (no por sample).</summary>
    public static class SoddModulation
    {
        /// <summary>Los slots &gt;= GlobalOffset referencian parámetros globales.</summary>
        public const int GlobalOffset = 1024;

        public static float GetParam(int slot, ref SoddModContext ctx)
        {
            if (slot < 0) return 0f;
            if (slot >= GlobalOffset)
            {
                int g = slot - GlobalOffset;
                return ctx.global != null && g < ctx.global.Length ? ctx.global[g] : 0f;
            }
            return ctx.local != null && slot < ctx.local.Length ? ctx.local[slot] : 0f;
        }

        public static float Hash01(uint seed, int salt)
        {
            uint h = seed ^ (uint)(salt * 0x9E3779B1);
            h ^= h >> 16; h *= 0x7feb352d; h ^= h >> 15; h *= 0x846ca68b; h ^= h >> 16;
            return (h & 0xFFFFFF) / 16777216f;
        }

        public static float ConvertUnit(float v, SoddUnit unit, SoddPropKind kind)
        {
            if (unit == SoddUnit.Auto) return v;
            switch (kind)
            {
                case SoddPropKind.GainDb:
                    return unit == SoddUnit.Linear ? LinearToDb(v) : v;
                case SoddPropKind.Semitones:
                    return unit == SoddUnit.Linear ? 12f * Mathf.Log(Mathf.Max(v, 1e-4f), 2f) : v;
                default:
                    return v;
            }
        }

        public static float LinearToDb(float v) => v <= 1e-4f ? -80f : 20f * Mathf.Log10(v);
        public static float DbToLinear(float db) => db <= -79.9f ? 0f : Mathf.Pow(10f, db / 20f);
        public static float SemitonesToRatio(float s) => Mathf.Pow(2f, s / 12f);

        public static float Evaluate(SoddModProp p, SoddPropKind kind, ref SoddModContext ctx)
        {
            if (p == null) return 0f;
            float v = p.@base;
            var mods = p.modulators;
            if (mods == null) return v;
            for (int i = 0; i < mods.Length; i++) v += Contribution(mods[i], i, kind, ref ctx);
            return v;
        }

        public static float Contribution(SoddModulator m, int index, SoddPropKind kind, ref SoddModContext ctx)
        {
            switch (m.type)
            {
                case SoddModulatorType.Random:
                    {
                        uint seed = m.perTrigger ? ctx.triggerSeed : ctx.instanceSeed;
                        float r = Mathf.Lerp(m.min, m.max, Hash01(seed, index + 17));
                        return ConvertUnit(r, m.unit, kind);
                    }
                case SoddModulatorType.RandomScaledByParameter:
                    {
                        uint seed = m.perTrigger ? ctx.triggerSeed : ctx.instanceSeed;
                        float r = Mathf.Lerp(m.min, m.max, Hash01(seed, index + 31));
                        float scale = m.curve.points != null && m.curve.points.Length > 0 ? m.curve.Evaluate(GetParam(m.paramSlot, ref ctx)) : 1f;
                        if (m.unit == SoddUnit.Linear) return ConvertUnit(1f + (r - 1f) * scale, m.unit, kind);
                        return ConvertUnit(r * scale, m.unit, kind);
                    }
                case SoddModulatorType.ParameterCurve:
                    return ConvertUnit(m.curve.Evaluate(GetParam(m.paramSlot, ref ctx)), m.unit, kind);
                case SoddModulatorType.LFO:
                    {
                        float rate = m.rate;
                        if (m.paramSlot >= 0) rate *= GetParam(m.paramSlot, ref ctx);
                        float ph = ctx.time * rate + m.phase;
                        return Lfo(m.shape, ph, ctx.instanceSeed) * m.depth;
                    }
                case SoddModulatorType.Envelope:
                    {
                        float env = EnvelopeAHR(ctx.time, m.attack, m.hold, m.release);
                        return kind == SoddPropKind.GainDb ? LinearToDb(env) : env * m.depth;
                    }
                case SoddModulatorType.ADSR:
                    {
                        float env = Adsr(ctx.time, ctx.stopTime, m);
                        return kind == SoddPropKind.GainDb ? LinearToDb(env) : env * m.depth;
                    }
            }
            return 0f;
        }

        public static float Lfo(SoddLfoShape shape, float phase, uint seed)
        {
            float f = phase - Mathf.Floor(phase);
            switch (shape)
            {
                case SoddLfoShape.Sine: return Mathf.Sin(f * Mathf.PI * 2f);
                case SoddLfoShape.Triangle: return f < 0.5f ? f * 4f - 1f : 3f - f * 4f;
                case SoddLfoShape.Square: return f < 0.5f ? 1f : -1f;
                case SoddLfoShape.SawUp: return f * 2f - 1f;
                case SoddLfoShape.SawDown: return 1f - f * 2f;
                default: return Hash01(seed, (int)Mathf.Floor(phase)) * 2f - 1f;
            }
        }

        public static float EnvelopeAHR(float t, float a, float h, float r)
        {
            if (t < 0f) return 0f;
            if (t < a) return t / a;
            t -= a;
            if (t < h) return 1f;
            t -= h;
            if (r <= 0f) return 0f;
            return t < r ? 1f - t / r : 0f;
        }

        public static float Adsr(float t, float stopT, SoddModulator m)
        {
            float level;
            if (t < m.attack) level = m.attack > 0f ? t / m.attack : 1f;
            else if (t < m.attack + m.decay) level = Mathf.Lerp(1f, m.sustain, (t - m.attack) / m.decay);
            else level = m.sustain;
            if (stopT >= 0f)
            {
                if (m.release <= 0f) return 0f;
                level *= Mathf.Clamp01(1f - stopT / m.release);
            }
            return level;
        }

        /// <summary>Tiempo de release más largo de los ADSR de la propiedad (para Stop con fade-out).</summary>
        public static float MaxRelease(SoddModProp p)
        {
            float r = 0f;
            if (p?.modulators == null) return r;
            foreach (var m in p.modulators) if (m.type == SoddModulatorType.ADSR) r = Mathf.Max(r, m.release);
            return r;
        }

        public static float FadeShape(float x, SoddFadeCurve c)
        {
            x = Mathf.Clamp01(x);
            switch (c)
            {
                case SoddFadeCurve.EqualPower: return Mathf.Sin(x * Mathf.PI * 0.5f);
                case SoddFadeCurve.Exponential: return x * x;
                case SoddFadeCurve.SCurve: return x * x * (3f - 2f * x);
                default: return x;
            }
        }
    }
}
