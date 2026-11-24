using System;
using System.Collections.Generic;
using UnityEngine;

namespace Sodd.Design
{
    /// <summary>
    /// Evento importado desde un archivo .sodd.json (la fuente de verdad es el JSON).
    /// Lo genera SoddEventImporter; no se edita a mano.
    /// </summary>
    public class SoddEventAsset : ScriptableObject
    {
        public SoddEventData data = new SoddEventData();
        /// <summary>Clips resueltos al importar, indexados por SoddClipRef.clipIndex.</summary>
        public AudioClip[] clips = Array.Empty<AudioClip>();
        /// <summary>Advertencias del último import (para mostrar en el Studio / inspector).</summary>
        public string[] importWarnings = Array.Empty<string>();

        // ---- caches de runtime (no serializados)
        [NonSerialized] internal bool resolved;
        [NonSerialized] internal object resolvedFor;
        [NonSerialized] internal AnimationCurve rolloffCurve;
        [NonSerialized] internal float length;
        [NonSerialized] internal int[] lastPick;
        [NonSerialized] internal int[] seqIndex;
        [NonSerialized] internal int[] regionBase;
        [NonSerialized] internal float lastStartTime = -1e9f;
        [NonSerialized] internal float stopFadeTime;

        public string Id => data?.id;
        public string Path => data?.path;
        public uint Hash => SoddText.Fnv1a(data?.id);

        void OnEnable() { resolved = false; }

        /// <summary>Resuelve slots de parámetros y prepara caches (una vez por motor de parámetros).</summary>
        internal void Resolve(SoddParameterSystem globals)
        {
            if (resolved && ReferenceEquals(resolvedFor, globals)) return;
            resolved = true; resolvedFor = globals;
            var d = data;
            length = d.Length;
            int regionCount = 0;
            regionBase = new int[d.tracks.Length];
            for (int t = 0; t < d.tracks.Length; t++) { regionBase[t] = regionCount; regionCount += d.tracks[t].regions.Length; }
            lastPick = new int[regionCount];
            seqIndex = new int[regionCount];
            for (int i = 0; i < regionCount; i++) lastPick[i] = -1;

            ForEachModProp(p =>
            {
                if (p.modulators == null) return;
                foreach (var m in p.modulators) m.paramSlot = ResolveSlot(m.parameter, globals);
            });
            foreach (var l in d.loopRegions) if (l.exitWhen != null) l.exitWhen.paramSlot = ResolveSlot(l.exitWhen.parameter, globals);

            rolloffCurve = null;
            if (d.spatial.rolloff == SoddRolloff.Custom && d.spatial.customRolloff.points.Length > 0)
            {
                // Las curvas de AudioSource van en distancia normalizada 0..1 (0 = min, 1 = maxDistance).
                var pts = d.spatial.customRolloff.points;
                var keys = new Keyframe[pts.Length];
                float max = Mathf.Max(0.001f, d.spatial.maxDistance);
                for (int i = 0; i < pts.Length; i++) keys[i] = new Keyframe(Mathf.Clamp01(pts[i].x / max), Mathf.Clamp01(pts[i].y));
                rolloffCurve = new AnimationCurve(keys);
            }
            stopFadeTime = Mathf.Max(0.05f, SoddModulation.MaxRelease(d.volume));
        }

        int ResolveSlot(string name, SoddParameterSystem globals)
        {
            if (string.IsNullOrEmpty(name)) return -1;
            var ps = data.parameters;
            for (int i = 0; i < ps.Length; i++) if (ps[i].name == name) return i;
            int g = globals != null ? globals.IndexOf(name) : -1;
            return g >= 0 ? SoddModulation.GlobalOffset + g : -1;
        }

        public void ForEachModProp(Action<SoddModProp> fn)
        {
            var d = data;
            fn(d.volume); fn(d.pitch);
            foreach (var t in d.tracks)
            {
                fn(t.volume); fn(t.pan); fn(t.reverbSend);
                foreach (var fx in t.effects) foreach (var p in fx.parameters) fn(p.value);
                foreach (var r in t.regions) { fn(r.instrument.volume); fn(r.instrument.pitch); fn(r.instrument.startOffset); }
            }
        }

        /// <summary>Reemplaza los datos (usado por el importer y por live-update).</summary>
        public void SetData(SoddEventData d, AudioClip[] c)
        {
            data = d; clips = c ?? Array.Empty<AudioClip>(); resolved = false;
        }
    }

    /// <summary>Registro de eventos y archivos de proyecto. Vive en Resources/SoddRegistry.asset (lo mantiene el Editor).</summary>
    public class SoddRegistry : ScriptableObject
    {
        public const string ResourcePath = "SoddRegistry";
        public SoddEventAsset[] events = Array.Empty<SoddEventAsset>();
        public TextAsset parametersJson;
        public TextAsset settingsJson;

        [NonSerialized] Dictionary<string, SoddEventAsset> _byKey;
        [NonSerialized] Dictionary<uint, SoddEventAsset> _byHash;

        public void Invalidate() { _byKey = null; _byHash = null; }

        void Build()
        {
            _byKey = new Dictionary<string, SoddEventAsset>(StringComparer.OrdinalIgnoreCase);
            _byHash = new Dictionary<uint, SoddEventAsset>();
            foreach (var e in events)
            {
                if (e == null || e.data == null) continue;
                if (!string.IsNullOrEmpty(e.data.id)) { _byKey[e.data.id] = e; _byHash[e.Hash] = e; }
                if (!string.IsNullOrEmpty(e.data.path)) _byKey[e.data.path] = e;
            }
        }

        public SoddEventAsset Find(string idOrPath)
        {
            if (string.IsNullOrEmpty(idOrPath)) return null;
            if (_byKey == null) Build();
            return _byKey.TryGetValue(idOrPath, out var e) ? e : null;
        }

        public SoddEventAsset FindByHash(uint hash)
        {
            if (_byHash == null) Build();
            return _byHash.TryGetValue(hash, out var e) ? e : null;
        }
    }

    /// <summary>Referencia serializable a un evento (GUID del evento + path legible + asset directo).</summary>
    [Serializable]
    public struct SoddEventReference
    {
        public string guid;
        public string path;
        public SoddEventAsset asset;

        public SoddEventReference(SoddEventAsset a)
        {
            asset = a; guid = a != null ? a.Id : null; path = a != null ? a.Path : null;
        }

        public static SoddEventReference FromPath(string p) => new SoddEventReference { path = p };

        public bool IsNull => asset == null && string.IsNullOrEmpty(guid) && string.IsNullOrEmpty(path);

        public SoddEventAsset Resolve()
        {
            if (asset != null) return asset;
            return SoddEvents.Find(!string.IsNullOrEmpty(guid) ? guid : path) ?? SoddEvents.Find(path);
        }

        public override string ToString() => !string.IsNullOrEmpty(path) ? path : guid ?? "(none)";
    }

    /// <summary>Búsqueda de eventos: registro + eventos registrados manualmente (tests, bancos, Addressables).</summary>
    public static class SoddEvents
    {
        static SoddRegistry s_registry;
        static bool s_triedLoad;
        static readonly Dictionary<string, SoddEventAsset> s_extra = new Dictionary<string, SoddEventAsset>(StringComparer.OrdinalIgnoreCase);
        static readonly Dictionary<uint, SoddEventAsset> s_extraHash = new Dictionary<uint, SoddEventAsset>();

        /// <summary>Hook para que el Editor resuelva eventos vía AssetDatabase sin registro.</summary>
        public static Func<string, SoddEventAsset> EditorResolver;

        public static SoddRegistry Registry
        {
            get
            {
                if (s_registry == null && !s_triedLoad) { s_triedLoad = true; s_registry = Resources.Load<SoddRegistry>(SoddRegistry.ResourcePath); }
                return s_registry;
            }
            set { s_registry = value; s_triedLoad = true; }
        }

        public static void ResetCache() { s_registry = null; s_triedLoad = false; }

        public static void Register(SoddEventAsset a)
        {
            if (a == null || a.data == null) return;
            if (!string.IsNullOrEmpty(a.data.id)) { s_extra[a.data.id] = a; s_extraHash[a.Hash] = a; }
            if (!string.IsNullOrEmpty(a.data.path)) s_extra[a.data.path] = a;
        }

        public static void Unregister(SoddEventAsset a)
        {
            if (a == null || a.data == null) return;
            if (a.data.id != null) { s_extra.Remove(a.data.id); s_extraHash.Remove(a.Hash); }
            if (a.data.path != null) s_extra.Remove(a.data.path);
        }

        public static void ClearRegistered() { s_extra.Clear(); s_extraHash.Clear(); }

        public static SoddEventAsset Find(string idOrPath)
        {
            if (string.IsNullOrEmpty(idOrPath)) return null;
            if (s_extra.TryGetValue(idOrPath, out var e) && e != null) return e;
            var r = Registry != null ? Registry.Find(idOrPath) : null;
            if (r == null && EditorResolver != null) r = EditorResolver(idOrPath);
            return r;
        }

        public static SoddEventAsset FindByHash(uint hash)
        {
            if (s_extraHash.TryGetValue(hash, out var e) && e != null) return e;
            return Registry != null ? Registry.FindByHash(hash) : null;
        }
    }
}
