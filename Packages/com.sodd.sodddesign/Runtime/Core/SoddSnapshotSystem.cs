using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Audio;

namespace Sodd.Design
{
    /// <summary>
    /// Snapshots lógicos (de sodd.settings.json): offsets de volumen, low/high-pass y reverb por bus,
    /// interpolados en el tiempo. Opcionalmente hace TransitionTo de un snapshot de SoddMixer.mixer.
    /// </summary>
    public sealed class SoddSnapshotSystem
    {
        readonly SoddEngine _engine;
        SoddSnapshotDef[] _defs;
        public AudioMixer Mixer;
        public string Current { get; private set; }
        public float Progress => _dur <= 0f ? 1f : Mathf.Clamp01(_t / _dur);

        SoddBus[] _buses = Array.Empty<SoddBus>();
        float[] _fromDb, _toDb, _fromLp, _toLp, _fromHp, _toHp, _fromRv, _toRv;
        float _t, _dur;
        bool _active;

        public SoddSnapshotSystem(SoddEngine engine, SoddSnapshotDef[] defs) { _engine = engine; _defs = defs ?? Array.Empty<SoddSnapshotDef>(); }

        public IReadOnlyList<SoddSnapshotDef> Definitions => _defs;
        public void SetDefinitions(SoddSnapshotDef[] defs) => _defs = defs ?? Array.Empty<SoddSnapshotDef>();

        public SoddSnapshotDef Find(string name)
        {
            foreach (var d in _defs) if (string.Equals(d.name, name, StringComparison.OrdinalIgnoreCase)) return d;
            return null;
        }

        /// <summary>Activa un snapshot (null = volver al estado neutro).</summary>
        public bool Set(string name, float fade)
        {
            SoddSnapshotDef def = null;
            if (!string.IsNullOrEmpty(name))
            {
                def = Find(name);
                if (def == null)
                {
                    var names = new List<string>(); foreach (var d in _defs) names.Add(d.name);
                    Debug.LogWarning($"[SoddDesign] Snapshot '{name}' no existe.{SoddText.DidYouMean(name, names)}");
                    return false;
                }
            }
            var all = _engine.Buses.All;
            int n = all.Count;
            _buses = new SoddBus[n];
            _fromDb = new float[n]; _toDb = new float[n]; _fromLp = new float[n]; _toLp = new float[n];
            _fromHp = new float[n]; _toHp = new float[n]; _fromRv = new float[n]; _toRv = new float[n];
            for (int i = 0; i < n; i++)
            {
                var b = all[i];
                _buses[i] = b;
                _fromDb[i] = b.snapshotDb; _fromLp[i] = b.snapshotLowpass; _fromHp[i] = b.snapshotHighpass; _fromRv[i] = b.snapshotReverb;
                _toDb[i] = 0f; _toLp[i] = 22000f; _toHp[i] = 10f; _toRv[i] = 0f;
                if (def != null)
                    foreach (var sb in def.buses)
                        if (string.Equals(sb.bus, b.Name, StringComparison.OrdinalIgnoreCase))
                        { _toDb[i] = sb.volume; _toLp[i] = sb.lowpass; _toHp[i] = sb.highpass; _toRv[i] = sb.reverb; }
            }
            _t = 0f; _dur = Mathf.Max(0f, fade); _active = true;
            Current = def?.name;
            if (Mixer != null)
            {
                var snap = Mixer.FindSnapshot(def != null && !string.IsNullOrEmpty(def.mixerSnapshot) ? def.mixerSnapshot : "Snapshot");
                if (snap != null) snap.TransitionTo(_dur);
            }
            if (_dur <= 0f) Update(0f);
            return true;
        }

        public void Update(float dt)
        {
            if (!_active) return;
            _t += dt;
            float k = _dur <= 0f ? 1f : Mathf.Clamp01(_t / _dur);
            for (int i = 0; i < _buses.Length; i++)
            {
                var b = _buses[i];
                b.snapshotDb = Mathf.Lerp(_fromDb[i], _toDb[i], k);
                // Frecuencias en dominio logarítmico para que el barrido suene natural.
                b.snapshotLowpass = Mathf.Exp(Mathf.Lerp(Mathf.Log(_fromLp[i]), Mathf.Log(_toLp[i]), k));
                b.snapshotHighpass = Mathf.Exp(Mathf.Lerp(Mathf.Log(_fromHp[i]), Mathf.Log(_toHp[i]), k));
                b.snapshotReverb = Mathf.Lerp(_fromRv[i], _toRv[i], k);
            }
            if (k >= 1f) _active = false;
        }
    }
}
