using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.AssetImporters;
using UnityEngine;

namespace Sodd.Design.Editor
{
    /// <summary>Importa archivos *.sodd.json → SoddEventAsset. El JSON es la fuente de verdad.</summary>
    [ScriptedImporter(3, "sodd.json")]
    public sealed class SoddEventImporter : ScriptedImporter
    {
        public override void OnImportAsset(AssetImportContext ctx)
        {
            string text = File.ReadAllText(ctx.assetPath);
            var asset = ScriptableObject.CreateInstance<SoddEventAsset>();
            asset.name = Path.GetFileName(ctx.assetPath).Replace(".sodd.json", "");

            string paramsPath = SoddProjectFiles.ParametersPath;
            if (paramsPath != null) ctx.DependsOnSourceAsset(paramsPath);
            var known = SoddProjectFiles.LoadParameters();

            var data = SoddReader.ParseEvent(text, out var result, known?.parameters);
            if (data == null) data = new SoddEventData { path = asset.name, description = "(JSON inválido)" };
            var clips = ResolveClips(ctx, data, result);
            asset.SetData(data, clips);

            foreach (var e in result.errors) ctx.LogImportError($"[SoddDesign] {ctx.assetPath}{e}");
            foreach (var w in result.warnings) ctx.LogImportWarning($"[SoddDesign] {ctx.assetPath}{w}");
            var all = new List<string>(result.errors.Count + result.warnings.Count);
            foreach (var e in result.errors) all.Add("✖ " + e);
            foreach (var w in result.warnings) all.Add("⚠ " + w);
            asset.importWarnings = all.ToArray();

            ctx.AddObjectToAsset("event", asset, SoddEditorIcons.EventIcon);
            ctx.SetMainObject(asset);
        }

        static AudioClip[] ResolveClips(AssetImportContext ctx, SoddEventData data, SoddResult result)
        {
            var clips = new List<AudioClip>();
            var index = new Dictionary<string, int>();
            for (int t = 0; t < data.tracks.Length; t++)
                for (int r = 0; r < data.tracks[t].regions.Length; r++)
                {
                    var ins = data.tracks[t].regions[r].instrument;
                    for (int c = 0; c < ins.clips.Length; c++)
                    {
                        var cr = ins.clips[c];
                        string where = $"/tracks/{t}/regions/{r}/instrument/clips/{c}";
                        string path = null;
                        if (!string.IsNullOrEmpty(cr.guid))
                        {
                            path = AssetDatabase.GUIDToAssetPath(cr.guid);
                            if (string.IsNullOrEmpty(path)) path = null;
                            else if (!string.IsNullOrEmpty(cr.path) && cr.path != path)
                                result.Warn($"{where}: el path '{cr.path}' está desactualizado; el clip ahora está en '{path}'");
                        }
                        if (path == null && !string.IsNullOrEmpty(cr.path))
                        {
                            path = cr.path;
                            if (!string.IsNullOrEmpty(cr.guid)) result.Warn($"{where}: GUID '{cr.guid}' no encontrado, se usa el path");
                        }
                        if (path == null) { cr.clipIndex = -1; continue; }
                        if (index.TryGetValue(path, out int existing)) { cr.clipIndex = existing; continue; }

                        ctx.DependsOnArtifact(path);
                        var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
                        if (clip == null)
                        {
                            result.Error($"{where}: no se encontró el AudioClip '{path}'.{SoddText.DidYouMean(Path.GetFileNameWithoutExtension(path), SoddProjectFiles.AudioClipNames())}");
                            cr.clipIndex = -1;
                            continue;
                        }
                        if (string.IsNullOrEmpty(cr.guid)) cr.guid = AssetDatabase.AssetPathToGUID(path);
                        cr.path = path;
                        cr.clipIndex = clips.Count;
                        index[path] = clips.Count;
                        clips.Add(clip);
                    }
                }
            return clips.ToArray();
        }
    }

    internal static class SoddEditorIcons
    {
        static Texture2D s_event;
        public static Texture2D EventIcon => s_event != null ? s_event : (s_event = EditorGUIUtility.IconContent("AudioSource Icon").image as Texture2D);
    }
}
