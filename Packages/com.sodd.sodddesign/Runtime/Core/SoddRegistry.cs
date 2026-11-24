using System;
using System.Collections.Generic;
using UnityEngine;

namespace Sodd.Design
{
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
}
