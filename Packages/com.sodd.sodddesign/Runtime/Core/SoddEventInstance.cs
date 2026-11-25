using System;
using UnityEngine;

namespace Sodd.Design
{
    /// <summary>
    /// Handle a una instancia de evento. Es un struct con generación: después de Release (o cuando la
    /// instancia se recicla) todas las llamadas son no-ops seguras e IsValid devuelve false.
    /// </summary>
    public readonly struct SoddEventInstance : IEquatable<SoddEventInstance>
    {
        readonly SoddEngine _engine;
        readonly int _index;
        readonly int _gen;

        internal SoddEventInstance(SoddEngine engine, int index, int gen) { _engine = engine; _index = index; _gen = gen; }

        internal SoddInstance State => _engine?.Get(_index, _gen);
        public SoddEngine Engine => _engine;

        public bool IsValid => State != null;
        public SoddEventAsset Asset => State?.asset;
        public string Path => State?.ev?.path;

        /// <summary>True mientras suena (incluye el fade-out de Stop).</summary>
        public bool IsPlaying
        {
            get { var s = State; return s != null && (s.state == SoddInstanceState.Playing || s.state == SoddInstanceState.Stopping); }
        }

        public bool IsStopping { get { var s = State; return s != null && s.state == SoddInstanceState.Stopping; } }

        /// <summary>Posición actual en el timeline del evento (segundos).</summary>
        public float TimelinePosition { get { var s = State; return s != null ? _engine.TimelinePosition(s) : 0f; } }

        public void Start() { var s = State; if (s != null) _engine.Start(s); }

        /// <summary>Arranca en un dspTime exacto (sample-accurate).</summary>
        public void StartAt(double dspTime) { var s = State; if (s != null) _engine.StartAt(s, dspTime); }

        public void Stop(SoddStopMode mode = SoddStopMode.AllowFadeOut) { var s = State; if (s != null) _engine.Stop(s, mode); }

        /// <summary>Libera la instancia: se recicla cuando termine de sonar. El handle queda inválido.</summary>
        public void Release() { var s = State; if (s != null) _engine.Release(s); }

        public bool SetParameter(string name, float value, bool immediate = false) { var s = State; return s != null && _engine.SetLocal(s, name, value, immediate); }
        public bool SetParameter(int localIndex, float value, bool immediate = false) { var s = State; return s != null && _engine.SetLocal(s, localIndex, value, immediate); }
        /// <summary>Setea un parámetro (local o global) por hash FNV-1a de su nombre (lo usa la red).</summary>
        public bool SetParameterByHash(uint hash, float value, bool immediate = false)
        {
            var s = State;
            if (s == null) return false;
            var ps = s.ev.parameters;
            for (int i = 0; i < ps.Length; i++) if (SoddText.Fnv1a(ps[i].name) == hash) return _engine.SetLocal(s, i, value, immediate);
            var g = _engine.Parameters.Definitions;
            for (int i = 0; i < g.Length; i++) if (SoddText.Fnv1a(g[i].name) == hash) return _engine.Parameters.Set(i, value, immediate);
            return false;
        }

        public float GetParameter(string name) { var s = State; return s != null ? _engine.GetLocal(s, name) : 0f; }

        public void Set3DPosition(Vector3 p) { var s = State; if (s != null) { s.position = p; s.hasAttach = false; } }
        public Vector3 Position => State?.position ?? Vector3.zero;

        public void AttachTo(Transform t)
        {
            var s = State;
            if (s == null) return;
            s.attach = t; s.hasAttach = t != null;
            if (t != null) s.position = t.position;
        }

        /// <summary>Multiplicador de pitch (ratio, 1 = normal), como EventInstance.setPitch de FMOD.</summary>
        public float Pitch
        {
            get => State?.pitchScale ?? 1f;
            set { var s = State; if (s != null) s.pitchScale = Mathf.Clamp(value, 0.05f, 4f); }
        }

        /// <summary>Duración del timeline del evento (s), como EventDescription.getLength.</summary>
        public float Length => State?.asset?.length ?? 0f;

        /// <summary>Arranca desde una posición del timeline (s), como setTimelinePosition + start.</summary>
        public void StartFrom(float seconds) { var s = State; if (s != null) _engine.StartFrom(s, seconds); }

        public void SetPaused(bool paused) { var s = State; if (s != null) s.userPaused = paused; }

        /// <summary>Instance seed: with a trigger seed it reproduces the value every Random modulator picked.</summary>
        public uint Seed { get { var s = State; return s != null ? s.instanceSeed : 0u; } }

        /// <summary>Copies the most recent region triggers, newest first, into buffer. Returns how many were written.</summary>
        public int GetRecentTriggers(SoddTriggerInfo[] buffer)
        {
            var s = State;
            if (s == null || buffer == null) return 0;
            int n = Math.Min(Math.Min(s.tlCount, SoddInstance.MaxTriggerLog), buffer.Length);
            for (int k = 0; k < n; k++)
            {
                int slot = (s.tlCount - 1 - k) % SoddInstance.MaxTriggerLog;
                buffer[k] = new SoddTriggerInfo { track = s.tlTrack[slot], region = s.tlRegion[slot], clip = s.tlClip[slot], seed = s.tlSeed[slot], instanceSeed = s.instanceSeed };
            }
            return n;
        }

        /// <summary>Multiplicador de volumen lineal extra (0..1+), útil para mezclas desde código.</summary>
        public float Volume
        {
            get => State?.volumeScale ?? 0f;
            set { var s = State; if (s != null) s.volumeScale = Mathf.Max(0f, value); }
        }

        public event Action<SoddEventInstance, string> OnMarker
        {
            add { var s = State; if (s != null) s.onMarker += value; }
            remove { var s = State; if (s != null) s.onMarker -= value; }
        }

        public event Action<SoddEventInstance> OnStopped
        {
            add { var s = State; if (s != null) s.onStopped += value; }
            remove { var s = State; if (s != null) s.onStopped -= value; }
        }

        public event Action<SoddEventInstance, string> OnLoopExit
        {
            add { var s = State; if (s != null) s.onLoopExit += value; }
            remove { var s = State; if (s != null) s.onLoopExit -= value; }
        }

        public bool Equals(SoddEventInstance o) => ReferenceEquals(_engine, o._engine) && _index == o._index && _gen == o._gen;
        public override bool Equals(object obj) => obj is SoddEventInstance o && Equals(o);
        public override int GetHashCode() => (_index * 397) ^ _gen;
        public static bool operator ==(SoddEventInstance a, SoddEventInstance b) => a.Equals(b);
        public static bool operator !=(SoddEventInstance a, SoddEventInstance b) => !a.Equals(b);
        public override string ToString() => IsValid ? $"SoddEventInstance({Path}#{_index}.{_gen})" : "SoddEventInstance(invalid)";
    }
}

namespace Sodd.Design
{
    /// <summary>One region trigger: which region played, which clip of its instrument and the seeds its random modulators used.</summary>
    public struct SoddTriggerInfo
    {
        public int track, region, clip;
        public uint seed, instanceSeed;
    }
}
