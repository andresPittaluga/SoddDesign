using System;
using UnityEngine;

namespace Sodd.Design
{
    internal enum SoddInstanceState : byte { Created, Playing, Stopping, Stopped }

    /// <summary>Voz del pool: un AudioSource reutilizable (o "headless" en tests).</summary>
    internal sealed class SoddVoice
    {
        public int index;
        public AudioSource source;
        public Transform transform;
        public SoddVoiceEffects effects;
        public int owner = -1;
        public int ownerGen;
        public int track, region;
        public double startDsp, endDsp, regionStartDsp;
        public float regionLength;
        public uint seed;
        public float audibility;
        public int priority = 256;
        public float gain;
        public bool paused;
        public bool Free => owner < 0;
    }

    /// <summary>Estado interno de una instancia de evento (pooled; el usuario solo ve el handle SoddEventInstance).</summary>
    internal sealed class SoddInstance
    {
        public const int MaxVoices = 32, MaxMarkers = 32, MaxSegments = 8, MaxLocalParams = 16, MaxChildren = 8, MaxLoops = 8;

        public int index, gen;
        public bool alive, released;
        public SoddInstanceState state;
        public SoddEventAsset asset;
        public SoddEventData ev;
        public readonly float[] local = new float[MaxLocalParams];
        public readonly float[] localTarget = new float[MaxLocalParams];
        public Vector3 position;
        public Transform attach;
        public bool hasAttach;
        public double startDsp;
        public double schedT, schedDsp;
        public readonly bool[] loopExited = new bool[MaxLoops];
        public readonly double[] segDsp = new double[MaxSegments];
        public readonly double[] segT = new double[MaxSegments];
        public int segCount;
        public readonly int[] voices = new int[MaxVoices];
        public int voiceCount;
        public readonly int[] mkIndex = new int[MaxMarkers];
        public readonly double[] mkDsp = new double[MaxMarkers];
        public int mkCount;
        public readonly SoddEventInstance[] children = new SoddEventInstance[MaxChildren];
        public int childCount;
        public float elapsed;
        public float stopElapsed = -1f;
        public float stopDuration;
        public uint instanceSeed;
        public uint triggerCounter;
        public float audibility;
        public float startTime;
        public bool paused, userPaused;
        public double pauseStartDsp;
        public int virtualVoices;
        public float volumeScale = 1f;
        public float pitchScale = 1f;
        public Action<SoddEventInstance, string> onMarker;
        public Action<SoddEventInstance> onStopped;
        public Action<SoddEventInstance, string> onLoopExit;

        public void Reset()
        {
            released = false; state = SoddInstanceState.Created; asset = null; ev = null;
            attach = null; hasAttach = false; position = Vector3.zero;
            voiceCount = 0; mkCount = 0; segCount = 0; childCount = 0;
            stopElapsed = -1f; elapsed = 0f; triggerCounter = 0; virtualVoices = 0; volumeScale = 1f; pitchScale = 1f;
            paused = userPaused = false;
            onMarker = null; onStopped = null; onLoopExit = null;
            for (int i = 0; i < MaxLoops; i++) loopExited[i] = false;
        }
    }

    /// <summary>
    /// Motor de SoddDesign: pool de voces, pool de instancias, scheduler sample-accurate y moduladores.
    /// Es una clase normal (no MonoBehaviour) para poder testearlo con reloj simulado.
    /// Todo el Update es sin allocations.
    /// </summary>
    public sealed class SoddEngine
    {
        readonly SoddVoice[] _voices;
        readonly SoddInstance[] _instances;
        int _nextFree;
        double _dsp;
        float _time;
        bool _globalPaused;

        public readonly SoddParameterSystem Parameters = new SoddParameterSystem();
        public readonly SoddBusSystem Buses = new SoddBusSystem();
        public SoddSnapshotSystem Snapshots { get; }
        public double LookaheadSeconds = 0.1;
        public Vector3 ListenerPosition;
        public bool Headless { get; }
        /// <summary>Longitud de clip a usar en modo headless (tests) cuando no hay AudioClip.</summary>
        public float HeadlessClipLength = 1f;

        // Estadísticas para el Profiler
        public int StatRealVoices { get; private set; }
        public int StatVirtualVoices { get; private set; }
        public int StatInstances { get; private set; }
        public int StatStolen { get; private set; }

        public int VoiceCount => _voices.Length;
        public int InstanceCapacity => _instances.Length;
        public double DspTime => _dsp;
        public float Time => _time;

        /// <summary>Se dispara con (asset, posición) cuando una instancia arranca: lo usa el módulo de red.</summary>
        public static event Action<SoddEventAsset, Vector3> GlobalStarted;

        public SoddEngine(SoddSettingsData settings, Transform voiceRoot)
        {
            settings ??= SoddSettingsData.CreateDefault();
            Headless = voiceRoot == null;
            LookaheadSeconds = settings.lookaheadMs / 1000.0;
            _voices = new SoddVoice[Mathf.Clamp(settings.maxVoices, 1, 255)];
            for (int i = 0; i < _voices.Length; i++)
            {
                var v = new SoddVoice { index = i };
                if (!Headless)
                {
                    var go = new GameObject("Voice " + i);
                    go.hideFlags = voiceRoot.gameObject.hideFlags;
                    go.transform.SetParent(voiceRoot, false);
                    v.transform = go.transform;
                    v.source = go.AddComponent<AudioSource>();
                    v.source.playOnAwake = false;
                    v.effects = new SoddVoiceEffects(go);
                }
                _voices[i] = v;
            }
            _instances = new SoddInstance[Mathf.Clamp(settings.maxInstances, 1, 4096)];
            for (int i = 0; i < _instances.Length; i++) _instances[i] = new SoddInstance { index = i, gen = 1 };
            Buses.Load(settings.buses);
            Snapshots = new SoddSnapshotSystem(this, settings.snapshots);
        }

        public void Dispose()
        {
            StopAll(null, SoddStopMode.Immediate);
            foreach (var v in _voices)
                if (v.transform != null)
                {
                    if (Application.isPlaying) UnityEngine.Object.Destroy(v.transform.gameObject);
                    else UnityEngine.Object.DestroyImmediate(v.transform.gameObject);
                }
        }

        // ------------------------------------------------------------------ instancias
        internal SoddInstance Get(int index, int gen)
        {
            if (index < 0 || index >= _instances.Length) return null;
            var s = _instances[index];
            return s.alive && s.gen == gen ? s : null;
        }

        public SoddEventInstance CreateInstance(SoddEventAsset asset)
        {
            if (asset == null || asset.data == null) return default;
            asset.Resolve(Parameters);
            for (int n = 0; n < _instances.Length; n++)
            {
                int i = (_nextFree + n) % _instances.Length;
                var s = _instances[i];
                if (s.alive) continue;
                _nextFree = (i + 1) % _instances.Length;
                s.Reset();
                s.alive = true;
                s.asset = asset; s.ev = asset.data;
                s.instanceSeed = (uint)(i * 7919 + s.gen * 104729) ^ (uint)Environment.TickCount;
                var ps = s.ev.parameters;
                for (int k = 0; k < SoddInstance.MaxLocalParams; k++)
                    s.local[k] = s.localTarget[k] = k < ps.Length ? ps[k].defaultValue : 0f;
                return new SoddEventInstance(this, i, s.gen);
            }
            Debug.LogWarning($"[SoddDesign] Sin instancias libres ({_instances.Length}). Subí maxInstances en sodd.settings.json.");
            return default;
        }

        /// <summary>Reloj actual: en modo real se sincroniza con AudioSettings.dspTime (por si no hubo Update todavía).</summary>
        double Now
        {
            get
            {
                if (!Headless) { double d = AudioSettings.dspTime; if (d > _dsp) _dsp = d; }
                return _dsp;
            }
        }

        internal void Start(SoddInstance s) => StartAt(s, Now + LookaheadSeconds * 0.5);

        internal void StartFrom(SoddInstance s, float seconds) => StartAt(s, Now + LookaheadSeconds * 0.5, seconds);

        internal void StartAt(SoddInstance s, double dsp) => StartAt(s, dsp, 0f);

        internal void StartAt(SoddInstance s, double dsp, float fromTime)
        {
            if (s.state == SoddInstanceState.Playing || s.state == SoddInstanceState.Stopping) StopInternal(s, SoddStopMode.Immediate, false);
            var ev = s.ev;
            var a = s.asset;
            if (ev.cooldown > 0f && _time - a.lastStartTime < ev.cooldown) { s.state = SoddInstanceState.Stopped; return; }
            if (ev.maxInstances > 0 && !EnforceMaxInstances(s)) { s.state = SoddInstanceState.Stopped; return; }
            a.lastStartTime = _time;
            s.state = SoddInstanceState.Playing;
            s.startTime = _time;
            s.elapsed = 0f; s.stopElapsed = -1f;
            s.startDsp = dsp;
            fromTime = Mathf.Max(0f, fromTime);
            s.schedT = fromTime; s.schedDsp = dsp;
            s.segCount = 0; AddSegment(s, dsp, fromTime);
            s.mkCount = 0;
            for (int i = 0; i < SoddInstance.MaxLoops; i++) s.loopExited[i] = false;
            if (s.hasAttach && s.attach != null) s.position = s.attach.position;
            GlobalStarted?.Invoke(a, s.position);
            if (fromTime > 0f) ScheduleSpanning(s, fromTime, dsp);
            Schedule(s);
        }

        bool EnforceMaxInstances(SoddInstance s)
        {
            int count = 0; SoddInstance oldest = null, quietest = null;
            for (int i = 0; i < _instances.Length; i++)
            {
                var o = _instances[i];
                if (!o.alive || o == s || o.asset != s.asset || o.state != SoddInstanceState.Playing) continue;
                count++;
                if (oldest == null || o.startTime < oldest.startTime) oldest = o;
                if (quietest == null || o.audibility < quietest.audibility) quietest = o;
            }
            if (count < s.ev.maxInstances) return true;
            switch (s.ev.stealMode)
            {
                case SoddStealMode.Oldest: StopInternal(oldest, SoddStopMode.Immediate, true); StatStolen++; return true;
                case SoddStealMode.Quietest: StopInternal(quietest, SoddStopMode.Immediate, true); StatStolen++; return true;
                default: return false;
            }
        }

        internal void Stop(SoddInstance s, SoddStopMode mode) => StopInternal(s, mode, true);

        void StopInternal(SoddInstance s, SoddStopMode mode, bool notify)
        {
            if (s == null) return;
            if (s.state != SoddInstanceState.Playing && s.state != SoddInstanceState.Stopping) return;
            if (mode == SoddStopMode.AllowFadeOut && s.state == SoddInstanceState.Stopping) return;
            for (int c = 0; c < s.childCount; c++) s.children[c].Stop(mode);
            if (mode == SoddStopMode.AllowFadeOut && s.state == SoddInstanceState.Playing)
            {
                s.state = SoddInstanceState.Stopping;
                s.stopElapsed = 0f;
                s.stopDuration = s.asset.stopFadeTime;
                return;
            }
            for (int k = s.voiceCount - 1; k >= 0; k--) FreeVoice(_voices[s.voices[k]]);
            s.voiceCount = 0; s.mkCount = 0; s.childCount = 0;
            s.state = SoddInstanceState.Stopped;
            if (notify) s.onStopped?.Invoke(new SoddEventInstance(this, s.index, s.gen));
        }

        internal void Release(SoddInstance s)
        {
            s.released = true;
            if (s.state == SoddInstanceState.Created || s.state == SoddInstanceState.Stopped) Free(s);
        }

        void Free(SoddInstance s)
        {
            for (int k = s.voiceCount - 1; k >= 0; k--) FreeVoice(_voices[s.voices[k]]);
            s.voiceCount = 0;
            s.alive = false;
            s.gen++;
            s.onMarker = null; s.onStopped = null; s.onLoopExit = null;
            s.asset = null; s.ev = null; s.attach = null;
        }

        public void StopAll(string bus, SoddStopMode mode)
        {
            for (int i = 0; i < _instances.Length; i++)
            {
                var s = _instances[i];
                if (!s.alive) continue;
                if (bus != null && !string.Equals(s.ev.bus, bus, StringComparison.OrdinalIgnoreCase)) continue;
                StopInternal(s, mode, true);
            }
        }

        /// <summary>Detiene inmediatamente todas las instancias de un evento (ej. al descargar un banco).</summary>
        public void StopAllOf(SoddEventAsset asset)
        {
            for (int i = 0; i < _instances.Length; i++)
            {
                var s = _instances[i];
                if (s.alive && s.asset == asset) { StopInternal(s, SoddStopMode.Immediate, true); if (s.released) Free(s); }
            }
        }

        public void SetPaused(bool paused) => _globalPaused = paused;
        public bool IsPaused => _globalPaused;

        // ------------------------------------------------------------------ parámetros locales
        internal bool SetLocal(SoddInstance s, string name, float value, bool immediate)
        {
            var ps = s.ev.parameters;
            for (int i = 0; i < ps.Length && i < SoddInstance.MaxLocalParams; i++)
                if (ps[i].name == name) return SetLocal(s, i, value, immediate);
            // Si no es local, se permite setear un global por comodidad.
            int g = Parameters.IndexOf(name);
            return g >= 0 && Parameters.Set(g, value, immediate);
        }

        internal bool SetLocal(SoddInstance s, int index, float value, bool immediate)
        {
            var ps = s.ev.parameters;
            if (index < 0 || index >= ps.Length || index >= SoddInstance.MaxLocalParams) return false;
            value = Mathf.Clamp(value, ps[index].min, ps[index].max);
            s.localTarget[index] = value;
            if (immediate || ps[index].seekSpeed <= 0f) s.local[index] = value;
            return true;
        }

        internal float GetLocal(SoddInstance s, string name)
        {
            var ps = s.ev.parameters;
            for (int i = 0; i < ps.Length && i < SoddInstance.MaxLocalParams; i++) if (ps[i].name == name) return s.local[i];
            int g = Parameters.IndexOf(name);
            return g >= 0 ? Parameters.Get(g) : 0f;
        }

        internal void AddChild(SoddInstance s, SoddEventInstance child)
        {
            if (s.childCount < SoddInstance.MaxChildren) s.children[s.childCount++] = child;
            else child.Release();
        }

        // ------------------------------------------------------------------ timeline
        static void AddSegment(SoddInstance s, double dsp, double t)
        {
            if (s.segCount == SoddInstance.MaxSegments)
            {
                for (int i = 1; i < SoddInstance.MaxSegments; i++) { s.segDsp[i - 1] = s.segDsp[i]; s.segT[i - 1] = s.segT[i]; }
                s.segCount--;
            }
            s.segDsp[s.segCount] = dsp; s.segT[s.segCount] = t; s.segCount++;
        }

        internal float TimelinePosition(SoddInstance s)
        {
            if (s.state != SoddInstanceState.Playing && s.state != SoddInstanceState.Stopping) return 0f;
            double now = s.paused ? s.pauseStartDsp : _dsp;
            for (int i = s.segCount - 1; i >= 0; i--)
                if (s.segDsp[i] <= now) return (float)(s.segT[i] + (now - s.segDsp[i]));
            return 0f;
        }

        int FindLoop(SoddInstance s, double t)
        {
            var loops = s.ev.loopRegions;
            for (int i = 0; i < loops.Length && i < SoddInstance.MaxLoops; i++)
            {
                if (s.loopExited[i]) continue;
                if (t >= loops[i].start - 1e-6 && t < loops[i].end - 1e-6) return i;
            }
            return -1;
        }

        void Schedule(SoddInstance s)
        {
            double horizon = _dsp + LookaheadSeconds;
            double length = s.asset.length;
            int guard = 0;
            while (s.schedDsp < horizon && guard++ < 16)
            {
                double t0 = s.schedT;
                double t1 = t0 + (horizon - s.schedDsp);
                int li = FindLoop(s, t0);
                bool wrap = false;
                if (li >= 0)
                {
                    var loop = s.ev.loopRegions[li];
                    if (t1 >= loop.end)
                    {
                        bool exit = loop.HasExitCondition && loop.exitWhen.Test(ReadSlot(s, loop.exitWhen.paramSlot));
                        if (exit)
                        {
                            s.loopExited[li] = true;
                            s.onLoopExit?.Invoke(new SoddEventInstance(this, s.index, s.gen), loop.name);
                        }
                        else { t1 = loop.end; wrap = true; }
                    }
                }
                else
                {
                    if (t0 >= length) { s.schedDsp = horizon; s.schedT = t1; break; }
                    // No cruzar el inicio de un loop dentro de la misma ventana.
                    var loops = s.ev.loopRegions;
                    for (int i = 0; i < loops.Length && i < SoddInstance.MaxLoops; i++)
                        if (!s.loopExited[i] && loops[i].start > t0 + 1e-6 && loops[i].start < t1) t1 = loops[i].start;
                }

                ScheduleWindow(s, t0, t1, s.schedDsp);
                s.schedDsp += t1 - t0;
                s.schedT = t1;
                if (wrap)
                {
                    var loop = s.ev.loopRegions[li];
                    CutVoicesAt(s, s.schedDsp);
                    s.schedT = loop.start;
                    AddSegment(s, s.schedDsp, loop.start);
                    ScheduleSpanning(s, loop.start, s.schedDsp);
                    if (loop.end - loop.start < 0.005f) break;
                }
            }
        }

        float ReadSlot(SoddInstance s, int slot)
        {
            if (slot < 0) return 0f;
            if (slot >= SoddModulation.GlobalOffset) return Parameters.Get(slot - SoddModulation.GlobalOffset);
            return slot < SoddInstance.MaxLocalParams ? s.local[slot] : 0f;
        }

        bool TrackAudible(SoddEventData ev, int ti)
        {
            var t = ev.tracks[ti];
            if (t.mute) return false;
            for (int i = 0; i < ev.tracks.Length; i++) if (ev.tracks[i].solo) return t.solo;
            return true;
        }

        void ScheduleWindow(SoddInstance s, double t0, double t1, double dspAtT0)
        {
            var ev = s.ev;
            for (int ti = 0; ti < ev.tracks.Length; ti++)
            {
                if (!TrackAudible(ev, ti)) continue;
                var regions = ev.tracks[ti].regions;
                for (int ri = 0; ri < regions.Length; ri++)
                {
                    double st = regions[ri].start;
                    if (st >= t0 - 1e-9 && st < t1 - 1e-9) TriggerRegion(s, ti, ri, dspAtT0 + (st - t0), 0f);
                }
            }
            var mk = ev.markers;
            for (int m = 0; m < mk.Length; m++)
            {
                double mt = mk[m].time;
                if (mt >= t0 - 1e-9 && mt < t1 - 1e-9 && s.mkCount < SoddInstance.MaxMarkers)
                {
                    s.mkIndex[s.mkCount] = m; s.mkDsp[s.mkCount] = dspAtT0 + (mt - t0); s.mkCount++;
                }
            }
        }

        void ScheduleSpanning(SoddInstance s, double t, double dsp)
        {
            var ev = s.ev;
            for (int ti = 0; ti < ev.tracks.Length; ti++)
            {
                if (!TrackAudible(ev, ti)) continue;
                var regions = ev.tracks[ti].regions;
                for (int ri = 0; ri < regions.Length; ri++)
                {
                    var r = regions[ri];
                    if (r.start < t - 1e-6 && r.End > t + 1e-6) TriggerRegion(s, ti, ri, dsp, (float)(t - r.start));
                }
            }
        }

        void CutVoicesAt(SoddInstance s, double dsp)
        {
            for (int k = 0; k < s.voiceCount; k++)
            {
                var v = _voices[s.voices[k]];
                if (v.endDsp > dsp)
                {
                    v.endDsp = dsp;
                    if (v.source != null) v.source.SetScheduledEndTime(dsp);
                }
            }
        }

        SoddModContext Ctx(SoddInstance s, uint triggerSeed, float time)
        {
            return new SoddModContext
            {
                local = s.local, global = Parameters.Values, time = time,
                stopTime = s.stopElapsed, triggerSeed = triggerSeed, instanceSeed = s.instanceSeed,
            };
        }

        void TriggerRegion(SoddInstance s, int ti, int ri, double dsp, float offset)
        {
            var ev = s.ev;
            var region = ev.tracks[ti].regions[ri];
            var ins = region.instrument;
            uint seed = s.instanceSeed * 2654435761u + (++s.triggerCounter) * 40503u;
            switch (ins.type)
            {
                case SoddInstrumentType.Silence:
                    return;
                case SoddInstrumentType.NestedEvent:
                    {
                        var nested = SoddEvents.Find(ins.nestedEvent);
                        if (nested == null || nested == s.asset) return;
                        var child = CreateInstance(nested);
                        var cs = child.State;
                        if (cs == null) return;
                        cs.position = s.position; cs.attach = s.attach; cs.hasAttach = s.hasAttach;
                        StartAt(cs, dsp);
                        AddChild(s, child);
                        return;
                    }
            }
            int clipIdx = PickClip(s, ti, ri, seed);
            if (clipIdx < 0) return;
            AudioClip clip = clipIdx < s.asset.clips.Length ? s.asset.clips[clipIdx] : null;
            if (clip == null && !Headless) return;

            var ctx = Ctx(s, seed, s.elapsed);
            float semis = SoddModulation.Evaluate(ev.pitch, SoddPropKind.Semitones, ref ctx) + SoddModulation.Evaluate(ins.pitch, SoddPropKind.Semitones, ref ctx);
            float ratio = Mathf.Clamp(SoddModulation.SemitonesToRatio(semis) * s.pitchScale, 0.05f, 4f);
            float clipLen = clip != null ? clip.length : HeadlessClipLength;
            float clipOffset = offset * ratio + Mathf.Max(0f, SoddModulation.Evaluate(ins.startOffset, SoddPropKind.Generic, ref ctx));
            if (ins.loop && clipLen > 0f) clipOffset %= clipLen;
            else if (clipOffset >= clipLen) return;

            float audibility = EstimateAudibility(s);
            var v = AcquireVoice(ev.priority, audibility);
            if (v == null) { s.virtualVoices++; StatVirtualVoices++; return; }
            if (s.voiceCount >= SoddInstance.MaxVoices) { FreeVoice(v); return; }

            double remaining = region.length - offset;
            double playDur = ins.loop ? remaining : Math.Min(remaining, (clipLen - clipOffset) / ratio);
            v.owner = s.index; v.ownerGen = s.gen;
            v.track = ti; v.region = ri;
            v.startDsp = dsp; v.endDsp = dsp + playDur;
            v.regionStartDsp = dsp - offset;
            v.regionLength = region.length;
            v.seed = seed; v.priority = ev.priority; v.audibility = audibility;
            v.paused = false; v.gain = 0f;
            s.voices[s.voiceCount++] = v.index;

            var src = v.source;
            if (src != null)
            {
                src.clip = clip;
                src.loop = ins.loop;
                src.pitch = ratio;
                src.volume = 0f;
                ConfigureSpatial(src, s);
                var bus = Buses.FindOrMaster(TrackBus(ev, ev.tracks[ti]));
                src.outputAudioMixerGroup = bus.MixerGroup;
                v.transform.position = s.position;
                v.effects?.Configure(ev.tracks[ti].effects);
                src.timeSamples = Mathf.Clamp((int)(clipOffset * clip.frequency), 0, Mathf.Max(0, clip.samples - 1));
                src.PlayScheduled(dsp);
                src.SetScheduledEndTime(v.endDsp);
            }
            UpdateVoice(s, v, 0f);
        }

        int PickClip(SoddInstance s, int ti, int ri, uint seed)
        {
            var ins = s.ev.tracks[ti].regions[ri].instrument;
            var clips = ins.clips;
            if (clips.Length == 0) return -1;
            int flat = s.asset.regionBase[ti] + ri;
            int pick;
            switch (ins.type)
            {
                case SoddInstrumentType.RandomClip:
                    {
                        float total = 0f;
                        int last = s.asset.lastPick[flat];
                        bool avoid = ins.avoidRepeat && clips.Length > 1;
                        for (int i = 0; i < clips.Length; i++) if (!(avoid && i == last)) total += Mathf.Max(0f, clips[i].weight);
                        float r = SoddModulation.Hash01(seed, 7) * total;
                        pick = -1;
                        for (int i = 0; i < clips.Length; i++)
                        {
                            if (avoid && i == last) continue;
                            r -= Mathf.Max(0f, clips[i].weight);
                            if (r <= 0f) { pick = i; break; }
                        }
                        if (pick < 0) { pick = clips.Length - 1; if (avoid && pick == last) pick = 0; }
                        break;
                    }
                case SoddInstrumentType.Sequential:
                    pick = s.asset.seqIndex[flat] % clips.Length;
                    s.asset.seqIndex[flat] = (pick + 1) % clips.Length;
                    break;
                default:
                    pick = 0;
                    break;
            }
            s.asset.lastPick[flat] = pick;
            return clips[pick].clipIndex >= 0 ? clips[pick].clipIndex : (Headless ? pick : -1);
        }

        /// <summary>Último clip elegido por una región (para tests/UI).</summary>
        public static int LastPickedClip(SoddEventAsset a, int track, int region) =>
            a.lastPick == null ? -1 : a.lastPick[a.regionBase[track] + region];

        void ConfigureSpatial(AudioSource src, SoddInstance s)
        {
            var ev = s.ev;
            if (!ev.is3D) { src.spatialBlend = 0f; src.dopplerLevel = 0f; src.spread = 0f; return; }
            var sp = ev.spatial;
            src.spatialBlend = sp.spatialBlend;
            src.minDistance = sp.minDistance;
            src.maxDistance = sp.maxDistance;
            src.dopplerLevel = sp.doppler;
            src.spread = sp.spread;
            if (sp.rolloff == SoddRolloff.Custom && s.asset.rolloffCurve != null)
            {
                src.rolloffMode = AudioRolloffMode.Custom;
                src.SetCustomCurve(AudioSourceCurveType.CustomRolloff, s.asset.rolloffCurve);
            }
            else src.rolloffMode = sp.rolloff == SoddRolloff.Linear ? AudioRolloffMode.Linear : AudioRolloffMode.Logarithmic;
        }

        float EstimateAudibility(SoddInstance s)
        {
            var ev = s.ev;
            float a = s.volumeScale;
            if (ev.is3D)
            {
                float d = Vector3.Distance(s.position, ListenerPosition);
                var sp = ev.spatial;
                if (d > sp.maxDistance) a *= 0.01f;
                else if (sp.rolloff == SoddRolloff.Linear) a *= 1f - Mathf.Clamp01((d - sp.minDistance) / Mathf.Max(0.001f, sp.maxDistance - sp.minDistance));
                else a *= sp.minDistance / Mathf.Max(sp.minDistance, d);
            }
            return a;
        }

        // ------------------------------------------------------------------ voces
        SoddVoice AcquireVoice(int priority, float audibility)
        {
            SoddVoice worst = null;
            for (int i = 0; i < _voices.Length; i++)
            {
                var v = _voices[i];
                if (v.Free) return v;
                if (worst == null || v.priority > worst.priority || (v.priority == worst.priority && v.audibility < worst.audibility)) worst = v;
            }
            if (worst != null && (worst.priority > priority || (worst.priority == priority && worst.audibility < audibility)))
            {
                var owner = _instances[worst.owner];
                if (owner.alive && owner.gen == worst.ownerGen)
                {
                    for (int k = 0; k < owner.voiceCount; k++)
                        if (owner.voices[k] == worst.index) { owner.voices[k] = owner.voices[--owner.voiceCount]; break; }
                    owner.virtualVoices++;
                }
                FreeVoice(worst);
                StatStolen++;
                return worst;
            }
            return null;
        }

        void FreeVoice(SoddVoice v)
        {
            if (v.source != null)
            {
                v.source.Stop();
                v.source.clip = null;
                v.effects?.Disable();
            }
            v.owner = -1; v.priority = 256; v.audibility = 0f; v.gain = 0f;
        }

        void UpdateVoice(SoddInstance s, SoddVoice v, float stopGain)
        {
            var ev = s.ev;
            var track = ev.tracks[v.track];
            var region = track.regions[v.region];
            var ins = region.instrument;
            float local = (float)(_dsp - v.regionStartDsp);
            var ctx = Ctx(s, v.seed, s.elapsed);

            float db = SoddModulation.Evaluate(ev.volume, SoddPropKind.GainDb, ref ctx)
                     + SoddModulation.Evaluate(track.volume, SoddPropKind.GainDb, ref ctx);
            var vctx = ctx; vctx.time = Mathf.Max(0f, local);
            db += SoddModulation.Evaluate(ins.volume, SoddPropKind.GainDb, ref vctx);

            float fade = 1f;
            if (region.fadeIn > 0f) fade *= SoddModulation.FadeShape(local / region.fadeIn, region.fadeInCurve);
            if (region.fadeOut > 0f) fade *= SoddModulation.FadeShape((region.length - local) / region.fadeOut, region.fadeOutCurve);

            var bus = Buses.FindOrMaster(TrackBus(ev, track));
            float gain = SoddModulation.DbToLinear(db) * fade * bus.EffectiveGain * s.volumeScale;
            if (s.state == SoddInstanceState.Stopping) gain *= stopGain;
            v.gain = gain;
            v.audibility = gain * EstimateAudibility(s);
            bus.Meter += gain;

            var src = v.source;
            if (src == null) return;
            src.volume = gain;
            float semis = SoddModulation.Evaluate(ev.pitch, SoddPropKind.Semitones, ref ctx) + SoddModulation.Evaluate(ins.pitch, SoddPropKind.Semitones, ref vctx);
            src.pitch = Mathf.Clamp(SoddModulation.SemitonesToRatio(semis) * s.pitchScale, 0.05f, 4f);
            src.panStereo = Mathf.Clamp(SoddModulation.Evaluate(track.pan, SoddPropKind.Generic, ref ctx), -1f, 1f);
            src.reverbZoneMix = Mathf.Clamp01(1f + SoddModulation.Evaluate(track.reverbSend, SoddPropKind.Generic, ref ctx));
            if (ev.is3D) v.transform.position = s.position;
            v.effects?.Update(track.effects, ref ctx, bus);
        }

        /// <summary>The track's bus, or the event's when the track has none (Unity serializes a missing string as "").</summary>
        static string TrackBus(SoddEventData ev, SoddTrack track) => string.IsNullOrEmpty(track.bus) ? ev.bus : track.bus;

        // ------------------------------------------------------------------ update
        /// <summary>Avanza el motor. dsp = AudioSettings.dspTime (o simulado en tests).</summary>
        public void Update(double dsp, float dt)
        {
            _dsp = dsp;
            _time += dt;
            Parameters.Update(dt);
            Snapshots.Update(dt);
            Buses.Update(dt);
            int live = 0, real = 0;
            for (int i = 0; i < _instances.Length; i++)
            {
                var s = _instances[i];
                if (!s.alive) continue;
                live++;
                UpdateInstance(s, dt);
                if (s.alive) real += s.voiceCount;
            }
            StatInstances = live;
            StatRealVoices = real;
        }

        void UpdateInstance(SoddInstance s, float dt)
        {
            if (s.state == SoddInstanceState.Stopped || s.state == SoddInstanceState.Created)
            {
                if (s.released && s.state == SoddInstanceState.Stopped) Free(s);
                return;
            }
            var ev = s.ev;
            // pausa (global, del bus, o del usuario): se congela el reloj de la instancia
            bool paused = _globalPaused || s.userPaused || Buses.FindOrMaster(ev.bus).EffectivePaused;
            if (paused != s.paused) SetInstancePaused(s, paused);
            if (paused) return;

            s.elapsed += dt;
            var ps = ev.parameters;
            for (int k = 0; k < ps.Length && k < SoddInstance.MaxLocalParams; k++)
                if (ps[k].seekSpeed > 0f) s.local[k] = Mathf.MoveTowards(s.local[k], s.localTarget[k], ps[k].seekSpeed * dt);
            if (s.hasAttach)
            {
                if (s.attach != null) s.position = s.attach.position;
                else s.hasAttach = false;
            }
            s.audibility = EstimateAudibility(s);

            float stopGain = 1f;
            if (s.state == SoddInstanceState.Stopping)
            {
                s.stopElapsed += dt;
                bool adsr = SoddModulation.MaxRelease(ev.volume) > 0f;
                stopGain = adsr ? 1f : Mathf.Clamp01(1f - s.stopElapsed / s.stopDuration);
                if (s.stopElapsed >= s.stopDuration) { StopInternal(s, SoddStopMode.Immediate, true); if (s.released) Free(s); return; }
            }
            else Schedule(s);

            // markers
            for (int m = s.mkCount - 1; m >= 0; m--)
            {
                if (s.mkDsp[m] > _dsp) continue;
                int idx = s.mkIndex[m];
                s.mkIndex[m] = s.mkIndex[s.mkCount - 1]; s.mkDsp[m] = s.mkDsp[s.mkCount - 1]; s.mkCount--;
                s.onMarker?.Invoke(new SoddEventInstance(this, s.index, s.gen), ev.markers[idx].name);
                if (!s.alive) return;
            }

            // voces
            for (int k = s.voiceCount - 1; k >= 0; k--)
            {
                var v = _voices[s.voices[k]];
                if (_dsp >= v.endDsp) { FreeVoice(v); s.voices[k] = s.voices[--s.voiceCount]; continue; }
                UpdateVoice(s, v, stopGain);
            }
            for (int c = s.childCount - 1; c >= 0; c--)
                if (!s.children[c].IsValid) s.children[c] = s.children[--s.childCount];

            // ¿terminó? (sin loop activo, cursor más allá del final, sin voces, sin markers ni hijos)
            if (s.state == SoddInstanceState.Playing && s.voiceCount == 0 && s.mkCount == 0 && s.childCount == 0
                && s.schedT >= s.asset.length && FindLoop(s, s.schedT) < 0 && TimelinePosition(s) >= s.asset.length)
            {
                StopInternal(s, SoddStopMode.Immediate, true);
                if (s.released) Free(s);
            }
        }

        void SetInstancePaused(SoddInstance s, bool paused)
        {
            s.paused = paused;
            if (paused)
            {
                s.pauseStartDsp = _dsp;
                for (int k = 0; k < s.voiceCount; k++) _voices[s.voices[k]].source?.Pause();
                return;
            }
            // Reanudar: desplazar todos los anclajes de tiempo dsp por lo que duró la pausa.
            double d = _dsp - s.pauseStartDsp;
            s.startDsp += d; s.schedDsp += d;
            for (int i = 0; i < s.segCount; i++) s.segDsp[i] += d;
            for (int i = 0; i < s.mkCount; i++) s.mkDsp[i] += d;
            for (int k = 0; k < s.voiceCount; k++)
            {
                var v = _voices[s.voices[k]];
                v.startDsp += d; v.endDsp += d; v.regionStartDsp += d;
                if (v.source != null) { v.source.UnPause(); v.source.SetScheduledEndTime(v.endDsp); }
            }
        }

        // ------------------------------------------------------------------ introspección (Profiler / tests)
        public struct InstanceInfo
        {
            public SoddEventInstance handle;
            public string path;
            public float timeline;
            public int voices, virtualVoices;
            public float audibility;
            public bool stopping;
        }

        /// <summary>Copia info de las instancias vivas en el buffer dado; devuelve cuántas escribió.</summary>
        public int GetInstances(InstanceInfo[] buffer)
        {
            int n = 0;
            for (int i = 0; i < _instances.Length && n < buffer.Length; i++)
            {
                var s = _instances[i];
                if (!s.alive || s.state == SoddInstanceState.Created || s.state == SoddInstanceState.Stopped) continue;
                buffer[n++] = new InstanceInfo
                {
                    handle = new SoddEventInstance(this, i, s.gen), path = s.ev.path, timeline = TimelinePosition(s),
                    voices = s.voiceCount, virtualVoices = s.virtualVoices, audibility = s.audibility,
                    stopping = s.state == SoddInstanceState.Stopping,
                };
            }
            return n;
        }

        /// <summary>Datos de una voz (tests): owner, inicio y fin programados en dsp.</summary>
        public bool GetVoice(int i, out int ownerIndex, out double startDsp, out double endDsp, out float gain)
        {
            var v = _voices[i];
            ownerIndex = v.owner; startDsp = v.startDsp; endDsp = v.endDsp; gain = v.gain;
            return !v.Free;
        }

        public int ActiveVoiceCount
        {
            get { int n = 0; foreach (var v in _voices) if (!v.Free) n++; return n; }
        }

        /// <summary>Recarga datos de un evento en caliente (live update): reinicia caches del asset.</summary>
        public void NotifyAssetChanged(SoddEventAsset a)
        {
            if (a == null) return;
            a.resolved = false;
            a.Resolve(Parameters);
            for (int i = 0; i < _instances.Length; i++)
            {
                var s = _instances[i];
                if (s.alive && s.asset == a)
                {
                    s.ev = a.data;
                    // Las voces quedan con índices de track/región viejos: se cortan y se re-programa desde la posición actual.
                    if (s.state == SoddInstanceState.Playing)
                    {
                        float pos = TimelinePosition(s);
                        for (int k = s.voiceCount - 1; k >= 0; k--) FreeVoice(_voices[s.voices[k]]);
                        s.voiceCount = 0; s.mkCount = 0;
                        double dsp = _dsp + 0.02;
                        s.schedT = pos; s.schedDsp = dsp; AddSegment(s, dsp, pos);
                        ScheduleSpanning(s, pos, dsp);
                        Schedule(s);
                    }
                }
            }
        }
    }
}
