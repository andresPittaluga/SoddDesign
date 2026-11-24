using System.Collections.Generic;
using System.Linq;
using Sodd.Design.Json;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Sodd.Design.Editor
{
    /// <summary>Mixer: faders de buses (en vivo, preview o Play Mode), mute/solo, medidores y snapshots.</summary>
    public sealed class SoddMixerWindow : EditorWindow
    {
        [MenuItem("Window/Sodd/Mixer", priority = 1)]
        public static void Open()
        {
            var w = GetWindow<SoddMixerWindow>();
            w.titleContent = new GUIContent("Sodd Mixer");
            w.minSize = new Vector2(420, 300);
        }

        VisualElement _strips, _snaps;
        readonly List<(SoddBus bus, VisualElement fill, Label db)> _meters = new List<(SoddBus, VisualElement, Label)>();
        SoddEngine _builtFor;
        float _fade = 1f;

        public void CreateGUI()
        {
            var root = rootVisualElement;
            root.styleSheets.Add(AssetDatabase.LoadAssetAtPath<StyleSheet>("Packages/com.sodd.sodddesign/Editor/UI/SoddTheme.uss"));
            root.AddToClassList("sd-root");
            var bar = new VisualElement(); bar.AddToClassList("sd-toolbar");
            bar.Add(new Label("Mixer") { name = "title" }); bar[0].AddToClassList("sd-logo");
            bar.Add(new VisualElement { style = { flexGrow = 1 } });
            var save = new Button(SaveToSettings) { text = "Guardar volúmenes en sodd.settings.json", tooltip = "Los faders son en vivo; esto los hace permanentes (con Undo)" };
            save.AddToClassList("sd-btn"); bar.Add(save);
            root.Add(bar);
            _strips = new VisualElement { style = { flexDirection = FlexDirection.Row, flexWrap = Wrap.Wrap, flexGrow = 1, paddingLeft = 6, paddingTop = 6 } };
            root.Add(_strips);
            var snapBox = new VisualElement(); snapBox.AddToClassList("sd-section");
            var st = new Label("Snapshots"); st.AddToClassList("sd-section-title"); snapBox.Add(st);
            var fade = new FloatField("Fade (s)") { value = _fade };
            fade.RegisterValueChangedCallback(e => _fade = Mathf.Max(0, e.newValue));
            snapBox.Add(fade);
            _snaps = new VisualElement { style = { flexDirection = FlexDirection.Row, flexWrap = Wrap.Wrap } };
            snapBox.Add(_snaps);
            root.Add(snapBox);
            root.schedule.Execute(Tick).Every(50);
        }

        void Build(SoddEngine e)
        {
            _builtFor = e;
            _strips.Clear(); _snaps.Clear(); _meters.Clear();
            if (e == null) { _strips.Add(new Label("Sin motor activo: abrí el Studio o entrá en Play Mode.") { style = { color = new Color(0.55f, 0.58f, 0.64f) } }); return; }
            foreach (var bus in e.Buses.All)
            {
                var b = bus;
                var strip = new VisualElement(); strip.AddToClassList("sd-strip");
                var name = new Label(b.Name) { tooltip = b.Parent != null ? "→ " + b.Parent.Name : "salida final" }; name.AddToClassList("sd-strip-name");
                strip.Add(name);
                var row = new VisualElement { style = { flexDirection = FlexDirection.Row, height = 170 } };
                var fader = new Slider(-80, 6) { direction = SliderDirection.Vertical, value = b.Volume, tooltip = "Volumen (dB)" };
                fader.AddToClassList("sd-fader");
                var meterBar = new VisualElement(); meterBar.AddToClassList("sd-meter-bar"); meterBar.style.height = 160;
                var fill = new VisualElement(); fill.AddToClassList("sd-meter-fill"); meterBar.Add(fill);
                row.Add(fader); row.Add(meterBar);
                strip.Add(row);
                var db = new Label(b.Volume.ToString("0.0") + " dB");
                fader.RegisterValueChangedCallback(ev => { b.Volume = ev.newValue; db.text = ev.newValue.ToString("0.0") + " dB"; });
                strip.Add(db);
                var btns = new VisualElement { style = { flexDirection = FlexDirection.Row } };
                Button Toggle(string text, bool on, System.Action<Button> click)
                {
                    var t = new Button { text = text }; t.AddToClassList("sd-chip"); t.EnableInClassList("sd-btn--on", on);
                    t.clicked += () => click(t); btns.Add(t); return t;
                }
                Toggle("M", b.Mute, t => { b.Mute = !b.Mute; t.EnableInClassList("sd-btn--on", b.Mute); });
                Toggle("S", b.Solo, t => { b.Solo = !b.Solo; t.EnableInClassList("sd-btn--on", b.Solo); });
                Toggle("⏸", b.Paused, t => { b.Paused = !b.Paused; t.EnableInClassList("sd-btn--on", b.Paused); });
                strip.Add(btns);
                _strips.Add(strip);
                _meters.Add((b, fill, db));
            }
            var normal = new Button(() => e.Snapshots.Set(null, _fade)) { text = "Normal" }; normal.AddToClassList("sd-btn"); _snaps.Add(normal);
            foreach (var s in e.Snapshots.Definitions)
            {
                var name = s.name;
                var btn = new Button(() => e.Snapshots.Set(name, _fade)) { text = name, tooltip = s.description }; btn.AddToClassList("sd-btn");
                _snaps.Add(btn);
            }
        }

        void Tick()
        {
            var e = Application.isPlaying ? SoddAudio.Engine : (SoddPreview.IsRunning ? SoddPreview.Engine : null);
            if (e != _builtFor) Build(e);
            if (e == null) return;
            foreach (var (bus, fill, _) in _meters)
            {
                float db = SoddModulation.LinearToDb(bus.Meter);
                fill.style.height = Length.Percent(Mathf.Clamp01((db + 60f) / 66f) * 100f);
            }
            if (e.Snapshots.Current != null) titleContent = new GUIContent("Sodd Mixer · " + e.Snapshots.Current);
        }

        void SaveToSettings()
        {
            var e = _builtFor;
            if (e == null) return;
            var settings = SoddProjectFiles.LoadSettings();
            var ops = new List<string>();
            for (int i = 0; i < settings.buses.Length; i++)
            {
                var b = e.Buses.Find(settings.buses[i].name);
                if (b == null) continue;
                ops.Add($"{{\"op\":\"add\",\"path\":\"/buses/{i}/volume\",\"value\":{SoddJson.FormatNumber(Mathf.Round(b.Volume * 10) / 10)}}}");
                ops.Add($"{{\"op\":\"add\",\"path\":\"/buses/{i}/mute\",\"value\":{(b.Mute ? "true" : "false")}}}");
            }
            var r = SoddCommands.EditSettings("[" + string.Join(",", ops) + "]");
            ShowNotification(new GUIContent(r.ok ? "✔ Guardado en sodd.settings.json" : "✖ " + r.errors.FirstOrDefault()));
        }
    }

    /// <summary>Profiler: instancias vivas, voces reales/virtuales, robos.</summary>
    public sealed class SoddProfilerWindow : EditorWindow
    {
        [MenuItem("Window/Sodd/Profiler", priority = 2)]
        public static void Open()
        {
            var w = GetWindow<SoddProfilerWindow>();
            w.titleContent = new GUIContent("Sodd Profiler");
            w.minSize = new Vector2(420, 240);
        }

        readonly SoddEngine.InstanceInfo[] _buf = new SoddEngine.InstanceInfo[512];
        readonly List<SoddEngine.InstanceInfo> _items = new List<SoddEngine.InstanceInfo>();
        Label _inst, _real, _virt, _stolen, _source;
        ListView _list;

        public void CreateGUI()
        {
            var root = rootVisualElement;
            root.styleSheets.Add(AssetDatabase.LoadAssetAtPath<StyleSheet>("Packages/com.sodd.sodddesign/Editor/UI/SoddTheme.uss"));
            root.AddToClassList("sd-root");
            var stats = new VisualElement { style = { flexDirection = FlexDirection.Row, flexShrink = 0 } };
            Label Stat(string title)
            {
                var box = new VisualElement(); box.AddToClassList("sd-stat");
                var v = new Label("0"); v.AddToClassList("sd-stat-value");
                box.Add(v); box.Add(new Label(title) { style = { color = new Color(0.55f, 0.58f, 0.64f) } });
                stats.Add(box); return v;
            }
            _inst = Stat("instancias"); _real = Stat("voces reales"); _virt = Stat("virtuales (total)"); _stolen = Stat("robadas (total)");
            root.Add(stats);
            _source = new Label { style = { paddingLeft = 10, color = new Color(0.55f, 0.58f, 0.64f) } };
            root.Add(_source);
            _list = new ListView(_items, 22, () =>
            {
                var r = new VisualElement { style = { flexDirection = FlexDirection.Row, paddingLeft = 10 } };
                r.Add(new Label { name = "p", style = { flexGrow = 1 } });
                r.Add(new Label { name = "i", style = { width = 220, color = new Color(0.55f, 0.58f, 0.64f) } });
                return r;
            }, (ve, i) =>
            {
                var it = _items[i];
                ve.Q<Label>("p").text = (it.stopping ? "↘ " : "▶ ") + it.path;
                ve.Q<Label>("i").text = $"t={it.timeline:0.00}s · voces {it.voices} · virt {it.virtualVoices} · aud {it.audibility:0.00}";
            }) { style = { flexGrow = 1 } };
            root.Add(_list);
            root.schedule.Execute(Tick).Every(100);
        }

        void Tick()
        {
            var e = Application.isPlaying ? SoddAudio.Engine : (SoddPreview.IsRunning ? SoddPreview.Engine : null);
            _source.text = e == null ? "Sin motor activo." : Application.isPlaying ? "Motor del juego (Play Mode)" : "Motor de preview del Editor";
            _items.Clear();
            if (e != null)
            {
                int n = e.GetInstances(_buf);
                for (int i = 0; i < n; i++) _items.Add(_buf[i]);
                _inst.text = e.StatInstances.ToString();
                _real.text = $"{e.StatRealVoices}/{e.VoiceCount}";
                _virt.text = e.StatVirtualVoices.ToString();
                _stolen.text = e.StatStolen.ToString();
            }
            _list.RefreshItems();
        }
    }
}
