using UnityEngine;

namespace Sodd.Design
{
    /// <summary>
    /// Cadena de efectos de una voz. Los componentes se crean UNA vez por voz (desactivados) y se habilitan
    /// según los efectos del track. Orden fijo de proceso: HighPass → LowPass → ThreeEQ → Distortion → Saturator →
    /// Bitcrusher → RingMod → Chorus → Tremolo → AutoPan → Compressor → SimpleDelay → Echo → Reverb.
    /// LowPass/HighPass también aplican los filtros de snapshot del bus (se combina con el del track).
    /// </summary>
    internal sealed class SoddVoiceEffects
    {
        readonly AudioHighPassFilter _hp;
        readonly AudioLowPassFilter _lp;
        readonly SoddThreeEQ _eq;
        readonly AudioDistortionFilter _dist;
        readonly SoddSaturator _sat;
        readonly SoddBitcrusher _crush;
        readonly SoddRingMod _ring;
        readonly AudioChorusFilter _chorus;
        readonly SoddTremolo _trem;
        readonly SoddAutoPan _pan;
        readonly SoddSimpleCompressor _comp;
        readonly SoddSimpleDelay _delay;
        readonly AudioEchoFilter _echo;
        readonly AudioReverbFilter _reverb;

        public SoddVoiceEffects(GameObject go)
        {
            // El orden de AddComponent define el orden de proceso.
            _hp = Add<AudioHighPassFilter>(go);
            _lp = Add<AudioLowPassFilter>(go);
            _eq = Add<SoddThreeEQ>(go);
            _dist = Add<AudioDistortionFilter>(go);
            _sat = Add<SoddSaturator>(go);
            _crush = Add<SoddBitcrusher>(go);
            _ring = Add<SoddRingMod>(go);
            _chorus = Add<AudioChorusFilter>(go);
            _trem = Add<SoddTremolo>(go);
            _pan = Add<SoddAutoPan>(go);
            _comp = Add<SoddSimpleCompressor>(go);
            _delay = Add<SoddSimpleDelay>(go);
            _echo = Add<AudioEchoFilter>(go);
            _reverb = Add<AudioReverbFilter>(go);
            _reverb.reverbPreset = AudioReverbPreset.User;
        }

        static T Add<T>(GameObject go) where T : Behaviour
        {
            var c = go.AddComponent<T>();
            c.enabled = false;
            return c;
        }

        public void Disable()
        {
            _hp.enabled = _lp.enabled = _eq.enabled = _dist.enabled = _sat.enabled = _crush.enabled = _ring.enabled = false;
            _chorus.enabled = _trem.enabled = _pan.enabled = _comp.enabled = _delay.enabled = _echo.enabled = _reverb.enabled = false;
        }

        public void Configure(SoddEffect[] effects)
        {
            Disable();
            _eq.RequestReset(); _sat.RequestReset(); _crush.RequestReset(); _ring.RequestReset(); _trem.RequestReset();
            _pan.RequestReset(); _comp.RequestReset(); _delay.RequestReset();
        }

        static float P(SoddEffect fx, int i, ref SoddModContext ctx, SoddEffectCatalog.EffectInfo info)
        {
            var pi = info.parameters[i];
            float v = i < fx.parameters.Length ? SoddModulation.Evaluate(fx.parameters[i].value, SoddPropKind.Generic, ref ctx) : pi.defaultValue;
            return Mathf.Clamp(v, pi.min, pi.max);
        }

        /// <summary>Por frame: habilita/actualiza componentes según los efectos del track y el snapshot del bus.</summary>
        public void Update(SoddEffect[] effects, ref SoddModContext ctx, SoddBus bus)
        {
            float lpCut = bus != null ? bus.EffectiveLowpass : 22000f;
            float hpCut = bus != null ? bus.EffectiveHighpass : 10f;
            float lpQ = 1f, hpQ = 1f;
            bool eq = false, dist = false, sat = false, crush = false, ring = false, chorus = false, trem = false, pan = false, comp = false, delay = false, echo = false, reverb = false;

            for (int e = 0; e < effects.Length; e++)
            {
                var fx = effects[e];
                if (fx.bypass) continue;
                var info = SoddEffectCatalog.Find(fx.type);
                if (info == null) continue;
                switch (info.name)
                {
                    case "LowPass": lpCut = Mathf.Min(lpCut, P(fx, 0, ref ctx, info)); lpQ = P(fx, 1, ref ctx, info); break;
                    case "HighPass": hpCut = Mathf.Max(hpCut, P(fx, 0, ref ctx, info)); hpQ = P(fx, 1, ref ctx, info); break;
                    case "Distortion": dist = true; _dist.distortionLevel = P(fx, 0, ref ctx, info); break;
                    case "Saturator": sat = true; for (int i = 0; i < 3; i++) _sat.SetParam(i, P(fx, i, ref ctx, info)); break;
                    case "Bitcrusher": crush = true; for (int i = 0; i < 3; i++) _crush.SetParam(i, P(fx, i, ref ctx, info)); break;
                    case "RingMod": ring = true; for (int i = 0; i < 2; i++) _ring.SetParam(i, P(fx, i, ref ctx, info)); break;
                    case "Chorus":
                        chorus = true;
                        _chorus.dryMix = P(fx, 0, ref ctx, info); _chorus.wetMix1 = _chorus.wetMix2 = _chorus.wetMix3 = P(fx, 1, ref ctx, info);
                        _chorus.delay = P(fx, 2, ref ctx, info); _chorus.rate = P(fx, 3, ref ctx, info); _chorus.depth = P(fx, 4, ref ctx, info);
                        break;
                    case "Tremolo": trem = true; for (int i = 0; i < 7; i++) _trem.SetParam(i, P(fx, i, ref ctx, info)); break;
                    case "ThreeEQ": eq = true; for (int i = 0; i < 6; i++) _eq.SetParam(i, P(fx, i, ref ctx, info)); break;
                    case "AutoPan": pan = true; for (int i = 0; i < 2; i++) _pan.SetParam(i, P(fx, i, ref ctx, info)); break;
                    case "SimpleCompressor": comp = true; for (int i = 0; i < 5; i++) _comp.SetParam(i, P(fx, i, ref ctx, info)); break;
                    case "SimpleDelay": delay = true; for (int i = 0; i < 3; i++) _delay.SetParam(i, P(fx, i, ref ctx, info)); break;
                    case "Echo":
                        echo = true;
                        _echo.delay = P(fx, 0, ref ctx, info); _echo.decayRatio = P(fx, 1, ref ctx, info);
                        _echo.wetMix = P(fx, 2, ref ctx, info); _echo.dryMix = P(fx, 3, ref ctx, info);
                        break;
                    case "Reverb":
                        reverb = true;
                        _reverb.decayTime = P(fx, 0, ref ctx, info); _reverb.room = P(fx, 1, ref ctx, info); _reverb.dryLevel = P(fx, 2, ref ctx, info);
                        break;
                }
            }
            bool lp = lpCut < 21990f, hp = hpCut > 10.5f;
            if (lp) { _lp.cutoffFrequency = lpCut; _lp.lowpassResonanceQ = lpQ; }
            if (hp) { _hp.cutoffFrequency = hpCut; _hp.highpassResonanceQ = hpQ; }
            Set(_lp, lp); Set(_hp, hp); Set(_eq, eq); Set(_dist, dist); Set(_sat, sat); Set(_crush, crush); Set(_ring, ring); Set(_chorus, chorus);
            Set(_trem, trem); Set(_pan, pan); Set(_comp, comp); Set(_delay, delay); Set(_echo, echo); Set(_reverb, reverb);
        }

        static void Set(Behaviour b, bool on) { if (b.enabled != on) b.enabled = on; }
    }
}
