using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Sodd.Design.Editor
{
    /// <summary>Inspector de un evento (.sodd.json): resumen, problemas de import y preview sin Play Mode.</summary>
    [CustomEditor(typeof(SoddEventAsset))]
    public sealed class SoddEventAssetEditor : UnityEditor.Editor
    {
        public override VisualElement CreateInspectorGUI()
        {
            var a = (SoddEventAsset)target;
            var root = new VisualElement { style = { paddingTop = 6 } };
            var d = a.data;
            var title = new Label(d.path) { style = { unityFontStyleAndWeight = FontStyle.Bold, fontSize = 14 } };
            root.Add(title);
            if (!string.IsNullOrEmpty(d.description)) root.Add(new Label(d.description) { style = { whiteSpace = WhiteSpace.Normal, marginBottom = 4, opacity = 0.8f } });
            root.Add(new Label($"{(d.is3D ? "3D" : "2D")} · bus {d.bus} · {d.tracks.Length} tracks · {d.Length:0.00}s · prioridad {d.priority}" +
                               (d.loopRegions.Length > 0 ? " · loop" : "") + (d.tags.Length > 0 ? " · #" + string.Join(" #", d.tags) : "")));

            var row = new VisualElement { style = { flexDirection = FlexDirection.Row, marginTop = 6, marginBottom = 6 } };
            row.Add(new Button(() => SoddPreview.Play(a)) { text = "▶ Preview", tooltip = "Escuchar sin entrar en Play Mode" });
            row.Add(new Button(SoddPreview.StopAll) { text = "■ Stop" });
            row.Add(new Button(() => EditorApplication.ExecuteMenuItem(SoddMenu.StudioMenu)) { text = "Abrir en Studio" });
            root.Add(row);

            foreach (var w in a.importWarnings)
                root.Add(new HelpBox(w.Substring(2), w.StartsWith("✖") ? HelpBoxMessageType.Error : HelpBoxMessageType.Warning));

            if (d.parameters.Length > 0) root.Add(new Label("Parámetros locales: " + string.Join(", ", System.Array.ConvertAll(d.parameters, p => $"{p.name} [{p.min}..{p.max}]"))));
            var clips = new Foldout { text = $"Clips ({a.clips.Length})", value = false };
            foreach (var c in a.clips) clips.Add(new UnityEditor.UIElements.ObjectField { value = c, objectType = typeof(AudioClip) });
            root.Add(clips);
            return root;
        }
    }

    internal static class SoddMenu
    {
        public const string StudioMenu = "Window/Sodd/SoddDesign Studio";

        [MenuItem("Assets/Sodd/Preview Event", true)]
        static bool CanPreview() => Selection.activeObject is SoddEventAsset;

        [MenuItem("Assets/Sodd/Preview Event")]
        static void Preview() => SoddPreview.Play((SoddEventAsset)Selection.activeObject);

        [MenuItem("Window/Sodd/Stop All Previews")]
        static void Stop() => SoddPreview.StopAll();

        [MenuItem("Window/Sodd/Validate All Events")]
        static void ValidateAll()
        {
            var r = SoddCommands.Validate();
            if (r.ok && r.warnings.Count == 0) Debug.Log("[SoddDesign] Todos los eventos son válidos.");
            else Debug.Log("[SoddDesign] Validación:\n" + r);
        }

        [MenuItem("Window/Sodd/Dev/Create Demo Event (SoddCommands)")]
        static void Demo()
        {
            const string p = "demo/commands_demo";
            if (SoddCommands.LocateFile(p) != null) SoddCommands.DeleteEvent(p);
            var log = new System.Text.StringBuilder();
            void Do(string what, SoddResult r) => log.AppendLine((r.ok ? "✔ " : "✖ ") + what + (r.ok ? "" : " → " + r));
            Do("CreateEvent", SoddCommands.CreateEvent(p, "Evento creado por SoddCommands (demo)."));
            Do("AddTrack Beeps", SoddCommands.AddTrack(p, "Beeps"));
            Do("AddRegion beep_440 @0", SoddCommands.AddRegion(p, 0, 0f, 0.3f, "{\"type\":\"SingleClip\",\"clip\":\"beep_440\"}"));
            Do("AddRegion beep_880 @0.4", SoddCommands.AddRegion(p, 0, 0.4f, 0.3f, "{\"type\":\"SingleClip\",\"clip\":\"beep_880\"}"));
            Do("AddTrack Kick", SoddCommands.AddTrack(p, "Kick"));
            Do("AddRegion kick @0.8", SoddCommands.AddRegion(p, 1, 0.8f, 0.5f, "{\"type\":\"SingleClip\",\"clip\":\"kick\"}"));
            Do("AddModulator pitch Random", SoddCommands.AddModulator(p, "/pitch", "{\"type\":\"Random\",\"range\":3,\"unit\":\"Semitones\"}"));
            Do("AddMarker Kick", SoddCommands.AddMarker(p, "Kick", 0.8f));
            Do("Validate", SoddCommands.Validate(p));
            Debug.Log("[SoddDesign] Demo de SoddCommands (Ctrl+Z deshace paso a paso):\n" + log);
            var a = AssetDatabase.LoadAssetAtPath<SoddEventAsset>(SoddCommands.LocateFile(p));
            Selection.activeObject = a;
            SoddPreview.Play(a);
        }

        [MenuItem("Window/Sodd/Rebuild Registry")]
        static void Rebuild() { SoddProjectFiles.ClearCache(); SoddProjectFiles.RebuildRegistry(); Debug.Log("[SoddDesign] Registro reconstruido."); }
    }
}
