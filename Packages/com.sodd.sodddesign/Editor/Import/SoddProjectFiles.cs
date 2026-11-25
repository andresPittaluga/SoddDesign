using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Sodd.Design.Editor
{
    /// <summary>Ubicación de los archivos de proyecto de SoddDesign y mantenimiento del registro.</summary>
    [InitializeOnLoad]
    public static class SoddProjectFiles
    {
        public const string DefaultRoot = "Assets/SoddDesign";
        public const string ParametersFile = "sodd.parameters.json";
        public const string SettingsFile = "sodd.settings.json";
        public const string EventExt = ".sodd.json";

        static SoddProjectFiles()
        {
            SoddEvents.EditorResolver = FindEventAsset;
            EditorApplication.delayCall += EnsureRegistry;
        }

        /// <summary>Crea el registro si falta (primer uso del paquete en un proyecto).</summary>
        public static void EnsureRegistry()
        {
            if (AssetDatabase.LoadAssetAtPath<SoddRegistry>(RegistryPath) != null) return;
            if (SettingsPath == null && AllEventFiles().Count == 0) return;
            RebuildRegistry();
        }

        /// <summary>Carpeta raíz de datos (donde está sodd.settings.json o, si no existe, Assets/SoddDesign).</summary>
        public static string Root
        {
            get
            {
                var s = FindFile(SettingsFile);
                return s != null ? Path.GetDirectoryName(s).Replace('\\', '/') : DefaultRoot;
            }
        }

        public static string EventsFolder => Root + "/Events";
        public static string ParametersPath => FindFile(ParametersFile);
        public static string SettingsPath => FindFile(SettingsFile);

        static readonly Dictionary<string, string> s_cache = new Dictionary<string, string>();

        static string FindFile(string fileName)
        {
            if (s_cache.TryGetValue(fileName, out var p) && p != null && File.Exists(p)) return p;
            p = null;
            string def = DefaultRoot + "/" + fileName;
            if (File.Exists(def)) p = def;
            else if (Directory.Exists("Assets"))
            {
                var found = Directory.GetFiles("Assets", fileName, SearchOption.AllDirectories);
                if (found.Length > 0) p = found[0].Replace('\\', '/');
            }
            s_cache[fileName] = p;
            return p;
        }

        public static void ClearCache() => s_cache.Clear();

        public static SoddParametersData LoadParameters()
        {
            var p = ParametersPath;
            if (p == null) return null;
            return SoddReader.ParseParameters(File.ReadAllText(p), out _);
        }

        public static SoddSettingsData LoadSettings()
        {
            var p = SettingsPath;
            if (p == null) return SoddSettingsData.CreateDefault();
            return SoddReader.ParseSettings(File.ReadAllText(p), out _);
        }

        public static IEnumerable<string> AudioClipNames()
        {
            foreach (var g in AssetDatabase.FindAssets("t:AudioClip"))
                yield return Path.GetFileNameWithoutExtension(AssetDatabase.GUIDToAssetPath(g));
        }

        /// <summary>Todos los archivos .sodd.json del proyecto.</summary>
        public static List<string> AllEventFiles()
        {
            var list = new List<string>();
            if (!Directory.Exists("Assets")) return list;
            foreach (var f in Directory.GetFiles("Assets", "*" + EventExt, SearchOption.AllDirectories))
                list.Add(f.Replace('\\', '/'));
            list.Sort(StringComparer.Ordinal);
            return list;
        }

        public static List<SoddEventAsset> AllEventAssets()
        {
            var list = new List<SoddEventAsset>();
            foreach (var f in AllEventFiles())
            {
                var a = AssetDatabase.LoadAssetAtPath<SoddEventAsset>(f);
                if (a != null) list.Add(a);
            }
            return list;
        }

        static SoddEventAsset FindEventAsset(string idOrPath)
        {
            var reg = SoddEvents.Registry;
            if (reg != null) { var r = reg.Find(idOrPath); if (r != null) return r; }
            foreach (var a in AllEventAssets())
                if (a.data != null && (string.Equals(a.data.id, idOrPath, StringComparison.OrdinalIgnoreCase) || string.Equals(a.data.path, idOrPath, StringComparison.OrdinalIgnoreCase)))
                    return a;
            return null;
        }

        /// <summary>Path del archivo .sodd.json de un evento (por id o path de evento).</summary>
        public static string FileOf(string idOrPath)
        {
            var a = FindEventAsset(idOrPath);
            return a != null ? AssetDatabase.GetAssetPath(a) : null;
        }

        // ------------------------------------------------------------------ registro
        public static string RegistryPath => Root + "/Resources/" + SoddRegistry.ResourcePath + ".asset";

        public static SoddRegistry RebuildRegistry()
        {
            string path = RegistryPath;
            var reg = AssetDatabase.LoadAssetAtPath<SoddRegistry>(path);
            if (reg == null)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                reg = ScriptableObject.CreateInstance<SoddRegistry>();
                AssetDatabase.CreateAsset(reg, path);
            }
            // Los eventos de bancos con excludeFromRegistry no van a Resources (se cargan con el banco).
            var banked = new HashSet<SoddEventAsset>();
            foreach (var g in AssetDatabase.FindAssets("t:SoddBank"))
            {
                var b = AssetDatabase.LoadAssetAtPath<SoddBank>(AssetDatabase.GUIDToAssetPath(g));
                if (b != null && b.excludeFromRegistry) foreach (var e in b.events) if (e != null) banked.Add(e);
            }
            var events = AllEventAssets().Where(e => !banked.Contains(e)).OrderBy(e => e.Path, StringComparer.Ordinal).ToArray();
            var pPath = ParametersPath; var sPath = SettingsPath;
            var pj = pPath != null ? AssetDatabase.LoadAssetAtPath<TextAsset>(pPath) : null;
            var sj = sPath != null ? AssetDatabase.LoadAssetAtPath<TextAsset>(sPath) : null;
            bool changed = reg.parametersJson != pj || reg.settingsJson != sj || reg.events == null || !reg.events.SequenceEqual(events);
            if (changed)
            {
                reg.events = events; reg.parametersJson = pj; reg.settingsJson = sj;
                reg.Invalidate();
                EditorUtility.SetDirty(reg);
                AssetDatabase.SaveAssetIfDirty(reg);
            }
            SoddEvents.Registry = reg;
            return reg;
        }
    }

    /// <summary>Reconstruye el registro cuando cambian eventos o archivos de proyecto.</summary>
    internal sealed class SoddAssetPostprocessor : AssetPostprocessor
    {
        static bool s_pending;
        public static event Action Changed;

        const string MissingClip = "no se encontró el AudioClip '";

        /// <summary>Reimporta los eventos que se importaron antes que sus AudioClips, cuando esos clips ya cargan.</summary>
        internal static int ReimportEventsWithMissingClips()
        {
            int n = 0;
            foreach (var a in SoddProjectFiles.AllEventAssets())
            {
                if (a == null || !a.importWarnings.Any(ClipNowAvailable)) continue;
                AssetDatabase.ImportAsset(AssetDatabase.GetAssetPath(a), ImportAssetOptions.ForceUpdate);
                n++;
            }
            return n;
        }

        internal static bool ClipNowAvailable(string warning)
        {
            int i = warning.IndexOf(MissingClip, StringComparison.Ordinal);
            if (i < 0) return false;
            int start = i + MissingClip.Length, end = warning.IndexOf('\'', start);
            return end > start && AssetDatabase.LoadAssetAtPath<AudioClip>(warning.Substring(start, end - start)) != null;
        }

        // A domain reload drops pending delayCalls, which happens on a project's first open right after the audio imports.
        [InitializeOnLoadMethod]
        static void AfterDomainReload() => EditorApplication.delayCall += () => ReimportEventsWithMissingClips();

        static bool Relevant(string p) =>
            p.EndsWith(SoddProjectFiles.EventExt, StringComparison.OrdinalIgnoreCase) ||
            p.EndsWith(SoddProjectFiles.ParametersFile, StringComparison.OrdinalIgnoreCase) ||
            p.EndsWith(SoddProjectFiles.SettingsFile, StringComparison.OrdinalIgnoreCase) ||
            (p.EndsWith(".asset", StringComparison.OrdinalIgnoreCase) && AssetDatabase.GetMainAssetTypeAtPath(p) == typeof(SoddBank));

        static readonly string[] AudioExts = { ".wav", ".mp3", ".ogg", ".aif", ".aiff", ".flac", ".m4a" };
        static bool IsAudio(string p) { foreach (var e in AudioExts) if (p.EndsWith(e, StringComparison.OrdinalIgnoreCase)) return true; return false; }

        static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
        {
            // Primer import de un proyecto: los .sodd.json pueden importarse antes que sus AudioClips.
            // Si llegan audios nuevos, reimportar los eventos que quedaron con clips faltantes.
            if (imported.Any(IsAudio) || moved.Any(IsAudio))
                EditorApplication.delayCall += () => ReimportEventsWithMissingClips();
            bool any = imported.Any(Relevant) || deleted.Any(Relevant) || moved.Any(Relevant) || movedFrom.Any(Relevant);
            if (!any) return;
            if (deleted.Any(Relevant) || moved.Any(Relevant)) SoddProjectFiles.ClearCache();
            // Live update: si hay un motor corriendo, refrescar instancias que usan los assets reimportados.
            foreach (var p in imported)
            {
                if (!p.EndsWith(SoddProjectFiles.EventExt, StringComparison.OrdinalIgnoreCase)) continue;
                var a = AssetDatabase.LoadAssetAtPath<SoddEventAsset>(p);
                if (a != null) SoddAudio.Engine?.NotifyAssetChanged(a);
            }
            if (s_pending) return;
            s_pending = true;
            EditorApplication.delayCall += () =>
            {
                s_pending = false;
                SoddProjectFiles.RebuildRegistry();
                Changed?.Invoke();
            };
        }
    }
}
