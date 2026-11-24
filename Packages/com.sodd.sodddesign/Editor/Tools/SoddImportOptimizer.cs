using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Sodd.Design.Editor
{
    /// <summary>
    /// "Optimize import settings": ajusta los AudioClips usados por eventos según cómo se usan.
    /// - Cortos (&lt; 1 s, SFX/UI/Voice): Decompress On Load + ADPCM (CPU mínima al disparar mucho).
    /// - Medios (1–6 s): Compressed In Memory + Vorbis 70%.
    /// - Largos (&gt; 6 s) o en tracks Music/Ambience con loop: Streaming + Vorbis 60%.
    /// - Usados solo en eventos 3D: Force To Mono (la espacialización es mono igual).
    /// - Siempre: Optimize Sample Rate, Load In Background para streaming.
    /// </summary>
    public static class SoddImportOptimizer
    {
        public sealed class Change
        {
            public string path;
            public AudioClipLoadType loadType;
            public AudioCompressionFormat format;
            public float quality;
            public bool mono;
            public string reason;
            public bool differs;
        }

        public static List<Change> Plan()
        {
            var usage = new Dictionary<string, (bool only3D, bool musicLoop, float maxLen)>();
            foreach (var a in SoddProjectFiles.AllEventAssets())
            {
                var d = a.data;
                foreach (var t in d.tracks)
                    foreach (var r in t.regions)
                        foreach (var c in r.instrument.clips)
                        {
                            if (c.clipIndex < 0 || c.clipIndex >= a.clips.Length || a.clips[c.clipIndex] == null) continue;
                            string path = AssetDatabase.GetAssetPath(a.clips[c.clipIndex]);
                            bool music = (t.type == SoddTrackType.Music || t.type == SoddTrackType.Ambience) && (r.instrument.loop || d.loopRegions.Length > 0);
                            usage.TryGetValue(path, out var u);
                            if (!usage.ContainsKey(path)) u = (true, false, 0f);
                            usage[path] = (u.only3D && d.is3D, u.musicLoop || music, Mathf.Max(u.maxLen, a.clips[c.clipIndex].length));
                        }
            }
            var plan = new List<Change>();
            foreach (var kv in usage.OrderBy(k => k.Key))
            {
                var imp = AssetImporter.GetAtPath(kv.Key) as AudioImporter;
                if (imp == null) continue;
                var (only3D, musicLoop, len) = kv.Value;
                var ch = new Change { path = kv.Key, mono = only3D };
                if (len > 6f || musicLoop) { ch.loadType = AudioClipLoadType.Streaming; ch.format = AudioCompressionFormat.Vorbis; ch.quality = 0.6f; ch.reason = musicLoop ? "música/ambiente en loop" : "largo (> 6 s)"; }
                else if (len < 1f) { ch.loadType = AudioClipLoadType.DecompressOnLoad; ch.format = AudioCompressionFormat.ADPCM; ch.quality = 1f; ch.reason = "corto (< 1 s)"; }
                else { ch.loadType = AudioClipLoadType.CompressedInMemory; ch.format = AudioCompressionFormat.Vorbis; ch.quality = 0.7f; ch.reason = "medio (1–6 s)"; }
                var s = imp.defaultSampleSettings;
                ch.differs = s.loadType != ch.loadType || s.compressionFormat != ch.format || imp.forceToMono != ch.mono ||
                             (ch.format == AudioCompressionFormat.Vorbis && Mathf.Abs(s.quality - ch.quality) > 0.01f) ||
                             s.sampleRateSetting != AudioSampleRateSetting.OptimizeSampleRate;
                plan.Add(ch);
            }
            return plan;
        }

        public static int Apply(List<Change> plan)
        {
            int n = 0;
            AssetDatabase.StartAssetEditing();
            try
            {
                foreach (var ch in plan)
                {
                    if (!ch.differs) continue;
                    var imp = (AudioImporter)AssetImporter.GetAtPath(ch.path);
                    var s = imp.defaultSampleSettings;
                    s.loadType = ch.loadType; s.compressionFormat = ch.format; s.quality = ch.quality;
                    s.sampleRateSetting = AudioSampleRateSetting.OptimizeSampleRate;
                    imp.defaultSampleSettings = s;
                    imp.forceToMono = ch.mono;
                    imp.loadInBackground = ch.loadType == AudioClipLoadType.Streaming;
                    imp.SaveAndReimport();
                    n++;
                }
            }
            finally { AssetDatabase.StopAssetEditing(); }
            return n;
        }

        [MenuItem("Window/Sodd/Optimize Audio Import Settings…")]
        public static void Run()
        {
            var plan = Plan();
            var changes = plan.Where(c => c.differs).ToList();
            if (changes.Count == 0) { EditorUtility.DisplayDialog("SoddDesign", $"Los {plan.Count} clips usados por eventos ya están optimizados.", "OK"); return; }
            var sb = new StringBuilder();
            foreach (var c in changes.Take(25))
                sb.AppendLine($"• {System.IO.Path.GetFileName(c.path)} → {c.loadType}, {c.format}{(c.mono ? ", mono" : "")} ({c.reason})");
            if (changes.Count > 25) sb.AppendLine($"… y {changes.Count - 25} más");
            if (!EditorUtility.DisplayDialog("Optimizar import settings", $"Se van a cambiar {changes.Count} clips:\n\n{sb}\nEsto reimporta los clips (no tiene Undo; están en control de versiones).", "Optimizar", "Cancelar")) return;
            int n = Apply(plan);
            Debug.Log($"[SoddDesign] Import settings optimizados en {n} clips.\n{sb}");
        }
    }
}
