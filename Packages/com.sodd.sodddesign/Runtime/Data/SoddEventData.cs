using System;
using UnityEngine;

namespace Sodd.Design
{
    public enum SoddStealMode { Oldest, Quietest, None }
    /// <summary>Sequential: Global keeps one position per event across all instances; Instance gives each instance its own.</summary>
    public enum SoddSequenceScope { Global, Instance }
    public enum SoddNetworkMode { Local, Broadcast, OwnerOnly }
    public enum SoddTrackType { SFX, Music, Voice, Ambience }
    public enum SoddInstrumentType { SingleClip, RandomClip, Sequential, Silence, NestedEvent }
    public enum SoddModulatorType { Random, ParameterCurve, RandomScaledByParameter, LFO, Envelope, ADSR }
    public enum SoddUnit { Auto, Linear, dB, Semitones }
    public enum SoddLfoShape { Sine, Triangle, Square, SawUp, SawDown, Random }
    public enum SoddRolloff { Logarithmic, Linear, Custom }
    public enum SoddFadeCurve { Linear, EqualPower, Exponential, SCurve }
    public enum SoddCompareOp { Greater, GreaterOrEqual, Less, LessOrEqual, Equal, NotEqual }
    public enum SoddStopMode { Immediate, AllowFadeOut }

    /// <summary>Qué representa una propiedad modulable: define cómo se convierten las unidades.</summary>
    public enum SoddPropKind { Generic, GainDb, Semitones }

    /// <summary>Curva lineal por tramos [[x,y],...]. Evaluación sin allocations.</summary>
    [Serializable]
    public class SoddCurve
    {
        public Vector2[] points = Array.Empty<Vector2>();

        public SoddCurve() { }
        public SoddCurve(params Vector2[] pts) { points = pts; }

        public float Evaluate(float x)
        {
            var p = points;
            if (p == null || p.Length == 0) return 1f;
            if (x <= p[0].x) return p[0].y;
            int last = p.Length - 1;
            if (x >= p[last].x) return p[last].y;
            for (int i = 0; i < last; i++)
            {
                if (x <= p[i + 1].x)
                {
                    float w = p[i + 1].x - p[i].x;
                    float t = w <= 1e-6f ? 1f : (x - p[i].x) / w;
                    return p[i].y + (p[i + 1].y - p[i].y) * t;
                }
            }
            return p[last].y;
        }
    }

    [Serializable]
    public class SoddModulator
    {
        public SoddModulatorType type;
        // Random / RandomScaledByParameter
        public float min, max;
        public SoddUnit unit = SoddUnit.Auto;
        public bool perTrigger = true;
        // ParameterCurve / RandomScaledByParameter / LFO sync
        public string parameter;
        public SoddCurve curve = new SoddCurve();
        // LFO
        public SoddLfoShape shape;
        public float rate = 1f;
        public float depth = 1f;
        public float phase;
        // Envelope / ADSR (segundos; sustain 0..1)
        public float attack, hold, decay, sustain = 1f, release;

        /// <summary>Slot resuelto del parámetro (local &gt;= 0, global &gt;= GlobalOffset, -1 = sin resolver).</summary>
        [NonSerialized] public int paramSlot = -1;
    }

    /// <summary>Propiedad numérica modulable: { "base": x, "modulators": [...] }.</summary>
    [Serializable]
    public class SoddModProp
    {
        public float @base;
        public SoddModulator[] modulators = Array.Empty<SoddModulator>();

        public SoddModProp() { }
        public SoddModProp(float b) { @base = b; }
        public bool HasModulators => modulators != null && modulators.Length > 0;
    }

    [Serializable]
    public class SoddSpatial
    {
        public float spatialBlend = 1f;
        public float minDistance = 1f;
        public float maxDistance = 50f;
        public SoddRolloff rolloff = SoddRolloff.Logarithmic;
        public SoddCurve customRolloff = new SoddCurve();
        public float doppler = 1f;
        public float spread;
    }

    [Serializable]
    public class SoddParameterDef
    {
        public string name;
        public string description;
        public float min;
        public float max = 1f;
        public float defaultValue;
        /// <summary>Unidades por segundo con las que el valor persigue al objetivo (0 = instantáneo).</summary>
        public float seekSpeed;
        public bool isGlobal;
    }

    [Serializable]
    public class SoddClipRef
    {
        public string guid;
        public string path;
        public float weight = 1f;
        /// <summary>Volume of this clip in dB, added to the instrument's (per sound of a playlist).</summary>
        public float volume;
        /// <summary>Pitch of this clip in semitones, added to the instrument's.</summary>
        public float pitch;
        /// <summary>Seconds of this clip to skip, added to the instrument's start offset.</summary>
        public float startOffset;
        /// <summary>Índice en SoddEventAsset.clips (resuelto al importar; -1 si falta).</summary>
        public int clipIndex = -1;
    }

    [Serializable]
    public class SoddInstrument
    {
        public SoddInstrumentType type;
        public SoddClipRef[] clips = Array.Empty<SoddClipRef>();
        public bool avoidRepeat = true;
        public SoddSequenceScope sequenceScope;
        public bool loop;
        /// <summary>Para NestedEvent: id (GUID) o path del evento anidado.</summary>
        public string nestedEvent;
        public SoddModProp volume = new SoddModProp(0f);
        public SoddModProp pitch = new SoddModProp(0f);
        public SoddModProp startOffset = new SoddModProp(0f);
    }

    [Serializable]
    public class SoddRegion
    {
        public string name;
        public float start;
        public float length = 1f;
        public float fadeIn;
        public float fadeOut;
        public SoddFadeCurve fadeInCurve = SoddFadeCurve.Linear;
        public SoddFadeCurve fadeOutCurve = SoddFadeCurve.Linear;
        public SoddInstrument instrument = new SoddInstrument();
        public float End => start + length;
    }

    [Serializable]
    public class SoddEffectParam
    {
        public string name;
        public SoddModProp value = new SoddModProp();
    }

    [Serializable]
    public class SoddEffect
    {
        public string type;
        public bool bypass;
        public SoddEffectParam[] parameters = Array.Empty<SoddEffectParam>();

        public SoddModProp Find(string n)
        {
            for (int i = 0; i < parameters.Length; i++) if (parameters[i].name == n) return parameters[i].value;
            return null;
        }
    }

    [Serializable]
    public class SoddTrack
    {
        public string name;
        public SoddTrackType type;
        public SoddModProp volume = new SoddModProp(0f);
        public SoddModProp pan = new SoddModProp(0f);
        public SoddModProp reverbSend = new SoddModProp(0f);
        public bool mute;
        public bool solo;
        public string bus;
        public SoddEffect[] effects = Array.Empty<SoddEffect>();
        public SoddRegion[] regions = Array.Empty<SoddRegion>();
    }

    [Serializable]
    public class SoddCondition
    {
        public string parameter;
        public SoddCompareOp op = SoddCompareOp.Greater;
        public float value;
        [NonSerialized] public int paramSlot = -1;

        public bool Test(float v)
        {
            switch (op)
            {
                case SoddCompareOp.Greater: return v > value;
                case SoddCompareOp.GreaterOrEqual: return v >= value;
                case SoddCompareOp.Less: return v < value;
                case SoddCompareOp.LessOrEqual: return v <= value;
                case SoddCompareOp.Equal: return Mathf.Abs(v - value) < 1e-4f;
                default: return Mathf.Abs(v - value) >= 1e-4f;
            }
        }
    }

    [Serializable]
    public class SoddLoopRegion
    {
        public string name;
        public float start;
        public float end = 1f;
        /// <summary>Si tiene parámetro: se sale del loop cuando la condición es verdadera.</summary>
        public SoddCondition exitWhen = new SoddCondition();
        public bool HasExitCondition => exitWhen != null && !string.IsNullOrEmpty(exitWhen.parameter);
    }

    [Serializable]
    public class SoddMarker
    {
        public string name;
        public float time;
    }

    [Serializable]
    public class SoddEventData
    {
        public string id;
        public string path;
        public string description;
        public string[] tags = Array.Empty<string>();
        public bool is3D;
        public SoddSpatial spatial = new SoddSpatial();
        public string bus = "SFX";
        public int priority = 128;
        public int maxInstances;
        public SoddStealMode stealMode = SoddStealMode.Oldest;
        public float cooldown;
        public SoddNetworkMode networkMode = SoddNetworkMode.Local;
        public SoddModProp volume = new SoddModProp(0f);
        public SoddModProp pitch = new SoddModProp(0f);
        public SoddParameterDef[] parameters = Array.Empty<SoddParameterDef>();
        public SoddTrack[] tracks = Array.Empty<SoddTrack>();
        public SoddLoopRegion[] loopRegions = Array.Empty<SoddLoopRegion>();
        public SoddMarker[] markers = Array.Empty<SoddMarker>();

        /// <summary>Duración del timeline (fin de la última región o marker).</summary>
        public float Length
        {
            get
            {
                float len = 0f;
                foreach (var t in tracks) foreach (var r in t.regions) len = Mathf.Max(len, r.End);
                foreach (var m in markers) len = Mathf.Max(len, m.time);
                foreach (var l in loopRegions) len = Mathf.Max(len, l.end);
                return len;
            }
        }

        public string Name
        {
            get
            {
                if (string.IsNullOrEmpty(path)) return id;
                int s = path.LastIndexOf('/');
                return s < 0 ? path : path.Substring(s + 1);
            }
        }
    }
}
