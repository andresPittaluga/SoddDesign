using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Audio;

namespace Sodd.Design
{
    /// <summary>Parámetros globales (de sodd.parameters.json) con seek-speed.</summary>
    public sealed class SoddParameterSystem
    {
        SoddParameterDef[] _defs = Array.Empty<SoddParameterDef>();
        float[] _values = Array.Empty<float>();
        float[] _targets = Array.Empty<float>();
        readonly Dictionary<string, int> _byName = new Dictionary<string, int>(StringComparer.Ordinal);

        public float[] Values => _values;
        public SoddParameterDef[] Definitions => _defs;
        public int Count => _defs.Length;

        public void Load(SoddParameterDef[] defs)
        {
            var list = new List<SoddParameterDef>();
            if (defs != null) foreach (var d in defs) if (d != null && d.isGlobal) list.Add(d);
            _defs = list.ToArray();
            _values = new float[_defs.Length];
            _targets = new float[_defs.Length];
            _byName.Clear();
            for (int i = 0; i < _defs.Length; i++)
            {
                _byName[_defs[i].name] = i;
                _values[i] = _targets[i] = _defs[i].defaultValue;
            }
        }

        public int IndexOf(string name) => name != null && _byName.TryGetValue(name, out int i) ? i : -1;

        /// <summary>Agrega un parámetro global en runtime si no existe (útil para tests/prototipos).</summary>
        public int Ensure(SoddParameterDef def)
        {
            int i = IndexOf(def.name);
            if (i >= 0) return i;
            def.isGlobal = true;
            var defs = new SoddParameterDef[_defs.Length + 1];
            Array.Copy(_defs, defs, _defs.Length); defs[_defs.Length] = def;
            var v = _values; var t = _targets;
            Load(defs);
            Array.Copy(v, _values, v.Length); Array.Copy(t, _targets, t.Length);
            return _defs.Length - 1;
        }

        public bool Set(int index, float value, bool immediate = false)
        {
            if (index < 0 || index >= _defs.Length) return false;
            var d = _defs[index];
            value = Mathf.Clamp(value, d.min, d.max);
            _targets[index] = value;
            if (immediate || d.seekSpeed <= 0f) _values[index] = value;
            return true;
        }

        public float Get(int index) => index >= 0 && index < _values.Length ? _values[index] : 0f;
        public float GetTarget(int index) => index >= 0 && index < _targets.Length ? _targets[index] : 0f;

        public void Update(float dt)
        {
            for (int i = 0; i < _values.Length; i++)
            {
                float s = _defs[i].seekSpeed;
                if (s > 0f) _values[i] = Mathf.MoveTowards(_values[i], _targets[i], s * dt);
            }
        }
    }

    /// <summary>Bus lógico (volumen, mute, pausa, fade, ruteo a grupo del mixer).</summary>
    public sealed class SoddBus
    {
        public readonly string Name;
        public SoddBus Parent { get; internal set; }
        public AudioMixerGroup MixerGroup { get; internal set; }

        float _volumeDb;
        float _fadeFrom, _fadeTo, _fadeDur, _fadeT;
        bool _fading;

        public bool Mute;
        public bool Paused;
        public bool Solo;

        // Offsets de snapshot (los maneja SoddSnapshotSystem)
        internal float snapshotDb;
        internal float snapshotLowpass = 22000f;
        internal float snapshotHighpass = 10f;
        internal float snapshotReverb;

        /// <summary>Ganancia lineal efectiva (incluye padres, mute y snapshots). Se recalcula por frame.</summary>
        public float EffectiveGain { get; internal set; } = 1f;
        public bool EffectivePaused { get; internal set; }
        public float EffectiveLowpass { get; internal set; } = 22000f;
        public float EffectiveHighpass { get; internal set; } = 10f;
        /// <summary>Medidor aproximado (suma de ganancias de voces activas), para la UI.</summary>
        public float Meter { get; internal set; }

        public SoddBus(string name) { Name = name; }

        /// <summary>Volumen del bus en dB (-80 = silencio).</summary>
        public float Volume
        {
            get => _volumeDb;
            set { _volumeDb = Mathf.Clamp(value, -80f, 24f); _fading = false; }
        }

        public void FadeTo(float db, float seconds)
        {
            if (seconds <= 0f) { Volume = db; return; }
            _fadeFrom = _volumeDb; _fadeTo = Mathf.Clamp(db, -80f, 24f); _fadeDur = seconds; _fadeT = 0f; _fading = true;
        }

        public bool IsFading => _fading;

        internal void Update(float dt)
        {
            if (!_fading) return;
            _fadeT += dt;
            float k = Mathf.Clamp01(_fadeT / _fadeDur);
            _volumeDb = Mathf.Lerp(_fadeFrom, _fadeTo, k);
            if (k >= 1f) _fading = false;
        }
    }

    public sealed class SoddBusSystem
    {
        readonly List<SoddBus> _buses = new List<SoddBus>();
        public IReadOnlyList<SoddBus> All => _buses;
        public SoddBus Master { get; private set; }

        public void Load(SoddBusDef[] defs, Func<string, AudioMixerGroup> groupLookup = null)
        {
            _buses.Clear();
            if (defs == null || defs.Length == 0) defs = SoddSettingsData.CreateDefault().buses;
            foreach (var d in defs)
            {
                if (Find(d.name) != null) continue;
                var b = new SoddBus(d.name) { Mute = d.mute };
                b.Volume = d.volume;
                b.MixerGroup = groupLookup?.Invoke(string.IsNullOrEmpty(d.mixerGroup) ? d.name : d.mixerGroup);
                _buses.Add(b);
            }
            Master = Find("Master");
            if (Master == null) { Master = new SoddBus("Master"); _buses.Insert(0, Master); }
            foreach (var d in defs)
            {
                var b = Find(d.name);
                if (b == null || b == Master) continue;
                b.Parent = Find(d.parent) ?? Master;
                if (b.Parent == b) b.Parent = Master;
            }
            // Grupo de mixer por defecto: hereda del padre si no hay propio.
            foreach (var b in _buses) if (b.MixerGroup == null && b.Parent != null) b.MixerGroup = b.Parent.MixerGroup;
        }

        public SoddBus Find(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            for (int i = 0; i < _buses.Count; i++) if (string.Equals(_buses[i].Name, name, StringComparison.OrdinalIgnoreCase)) return _buses[i];
            return null;
        }

        public SoddBus FindOrMaster(string name) => Find(name) ?? Master;

        public void Update(float dt)
        {
            bool anySolo = false;
            for (int i = 0; i < _buses.Count; i++) { _buses[i].Update(dt); anySolo |= _buses[i].Solo; }
            for (int i = 0; i < _buses.Count; i++)
            {
                var b = _buses[i];
                float db = b.Volume + b.snapshotDb;
                float gain = b.Mute ? 0f : SoddModulation.DbToLinear(db);
                bool paused = b.Paused;
                float lp = b.snapshotLowpass, hp = b.snapshotHighpass;
                bool soloOk = !anySolo || b.Solo || b == Master;
                for (var p = b.Parent; p != null; p = p.Parent)
                {
                    gain *= p.Mute ? 0f : SoddModulation.DbToLinear(p.Volume + p.snapshotDb);
                    paused |= p.Paused;
                    lp = Mathf.Min(lp, p.snapshotLowpass);
                    hp = Mathf.Max(hp, p.snapshotHighpass);
                    if (p.Solo) soloOk = true;
                }
                if (!soloOk) gain = 0f;
                b.EffectiveGain = gain; b.EffectivePaused = paused; b.EffectiveLowpass = lp; b.EffectiveHighpass = hp;
                b.Meter = 0f;
            }
        }
    }
}
