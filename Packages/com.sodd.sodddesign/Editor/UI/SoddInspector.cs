using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Sodd.Design.Json;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace Sodd.Design.Editor
{
    /// <summary>Construye el panel Inspector del Studio. Toda edición pasa por SoddCommands (con Undo).</summary>
    internal sealed class SoddInspector
    {
        static readonly HashSet<string> s_openMods = new HashSet<string>();
        readonly string _evt;
        readonly SoddEventAsset _a;
        readonly Action<SoddResult> _report;

        public SoddInspector(string evt, SoddEventAsset a, Action<SoddResult> report) { _evt = evt; _a = a; _report = report; }

        static string J(string s) => SoddJson.Write(JsonNode.From(s), false);
        static string J(float f) => SoddJson.FormatNumber(f);
        void Set(string ptr, string json) => _report(SoddCommands.SetProperty(_evt, ptr, json));
        void Patch(string patch) => _report(SoddCommands.EditEvent(_evt, patch));

        // ------------------------------------------------------------------ widgets
        static VisualElement Section(VisualElement parent, string title, string help = null)
        {
            var s = new VisualElement();
            s.AddToClassList("sd-section");
            if (title != null) { var l = new Label(title); l.AddToClassList("sd-section-title"); s.Add(l); }
            if (help != null) { var h = new Label(help); h.AddToClassList("sd-help"); s.Add(h); }
            parent.Add(s);
            return s;
        }

        static Button Btn(string text, Action a, string tooltip = null, string extra = null)
        {
            var b = new Button(a) { text = text, tooltip = tooltip };
            b.AddToClassList("sd-btn");
            if (extra != null) b.AddToClassList(extra);
            return b;
        }

        VisualElement Text(string label, string value, string ptr, string tooltip, bool multiline = false)
        {
            var f = new TextField(label) { value = value ?? "", isDelayed = true, multiline = multiline, tooltip = tooltip };
            if (multiline) f.style.whiteSpace = WhiteSpace.Normal;
            f.RegisterValueChangedCallback(e => Set(ptr, J(e.newValue)));
            return Row(f);
        }

        VisualElement Num(string label, float value, string ptr, string tooltip)
        {
            var f = new FloatField(label) { value = value, isDelayed = true, tooltip = tooltip };
            f.RegisterValueChangedCallback(e => Set(ptr, J(e.newValue)));
            return Row(f);
        }

        VisualElement Int(string label, int value, string ptr, string tooltip)
        {
            var f = new IntegerField(label) { value = value, isDelayed = true, tooltip = tooltip };
            f.RegisterValueChangedCallback(e => Set(ptr, e.newValue.ToString(CultureInfo.InvariantCulture)));
            return Row(f);
        }

        VisualElement Bool(string label, bool value, string ptr, string tooltip)
        {
            var f = new Toggle(label) { value = value, tooltip = tooltip };
            f.RegisterValueChangedCallback(e => Set(ptr, e.newValue ? "true" : "false"));
            return Row(f);
        }

        VisualElement Enum<T>(string label, T value, string ptr, string tooltip) where T : struct, Enum
        {
            var f = new EnumField(label, value) { tooltip = tooltip };
            f.RegisterValueChangedCallback(e => Set(ptr, J(e.newValue.ToString())));
            return Row(f);
        }

        VisualElement Choice(string label, List<string> options, string value, string ptr, string tooltip)
        {
            if (value != null && !options.Contains(value)) options.Add(value);
            var f = new DropdownField(label, options, value ?? (options.Count > 0 ? options[0] : "")) { tooltip = tooltip };
            f.RegisterValueChangedCallback(e => Set(ptr, J(e.newValue)));
            return Row(f);
        }

        static VisualElement Row(VisualElement field)
        {
            var r = new VisualElement();
            r.AddToClassList("sd-prop");
            r.Add(field);
            return r;
        }

        // ------------------------------------------------------------------ props modulables
        VisualElement ModProp(string label, string ptr, SoddModProp prop, string unit, string tooltip, string paramForX = null)
        {
            var box = new VisualElement();
            var row = new VisualElement(); row.AddToClassList("sd-prop");
            var f = new FloatField($"{label} ({unit})") { value = prop.@base, isDelayed = true, tooltip = tooltip + (prop.HasModulators ? "\nValor base: los moduladores se suman a este valor." : "") };
            f.RegisterValueChangedCallback(e => Set(ptr, J(e.newValue)));
            row.Add(f);
            bool open = s_openMods.Contains(_evt + ptr);
            var mb = new Button { text = prop.HasModulators ? "∿" + prop.modulators.Length : "＋", tooltip = prop.HasModulators ? "Ver/editar moduladores" : "Agregar modulador (Random, curva por parámetro, LFO, ADSR...)" };
            mb.AddToClassList("sd-mod-btn");
            if (prop.HasModulators) mb.AddToClassList("sd-mod-btn--on");
            row.Add(mb);
            box.Add(row);
            var panel = new VisualElement { style = { display = open || false ? DisplayStyle.Flex : DisplayStyle.None, marginLeft = 8 } };
            box.Add(panel);
            mb.clicked += () =>
            {
                if (!prop.HasModulators) { AddModulatorMenu(mb, ptr); return; }
                bool now = panel.style.display == DisplayStyle.None;
                panel.style.display = now ? DisplayStyle.Flex : DisplayStyle.None;
                if (now) s_openMods.Add(_evt + ptr); else s_openMods.Remove(_evt + ptr);
            };
            if (prop.HasModulators)
            {
                for (int i = 0; i < prop.modulators.Length; i++) panel.Add(ModCard(ptr, i, prop.modulators[i], unit));
                panel.Add(Btn("＋ Modulador", null, "Agregar otro modulador"));
                var add = (Button)panel[panel.childCount - 1];
                add.clicked += () => AddModulatorMenu(add, ptr);
            }
            return box;
        }

        void AddModulatorMenu(VisualElement anchor, string ptr)
        {
            var menu = new GenericDropdownMenu();
            void Item(string name, string json) => menu.AddItem(name, false, () => { s_openMods.Add(_evt + ptr); _report(SoddCommands.AddModulator(_evt, ptr, json)); });
            string firstParam = FirstParameter();
            Item("Random (aleatorio por disparo)", "{\"type\":\"Random\",\"min\":-1,\"max\":1}");
            Item("ParameterCurve (según parámetro)", "{\"type\":\"ParameterCurve\",\"parameter\":" + J(firstParam) + ",\"curve\":[[0,0],[1,1]]}");
            Item("RandomScaledByParameter", "{\"type\":\"RandomScaledByParameter\",\"parameter\":" + J(firstParam) + ",\"range\":2,\"curve\":[[0,1],[1,0]]}");
            Item("LFO (oscilador)", "{\"type\":\"LFO\",\"shape\":\"Sine\",\"rate\":1,\"depth\":1}");
            Item("Envelope (attack/hold/release)", "{\"type\":\"Envelope\",\"attack\":0.1,\"hold\":0.5,\"release\":0.5,\"depth\":1}");
            Item("ADSR (release al detener)", "{\"type\":\"ADSR\",\"attack\":0.05,\"decay\":0.1,\"sustain\":1,\"release\":0.5}");
            menu.DropDown(anchor.worldBound, anchor, default(DropdownMenuSizeMode));
        }

        string FirstParameter()
        {
            if (_a.data.parameters.Length > 0) return _a.data.parameters[0].name;
            var g = SoddProjectFiles.LoadParameters();
            return g != null && g.parameters.Length > 0 ? g.parameters[0].name : "Health";
        }

        List<string> ParameterNames()
        {
            var list = _a.data.parameters.Select(p => p.name).ToList();
            var g = SoddProjectFiles.LoadParameters();
            if (g != null) list.AddRange(g.parameters.Where(p => p.isGlobal).Select(p => p.name));
            return list;
        }

        SoddParameterDef FindParam(string name)
        {
            var l = _a.data.parameters.FirstOrDefault(p => p.name == name);
            if (l != null) return l;
            return SoddProjectFiles.LoadParameters()?.parameters.FirstOrDefault(p => p.name == name);
        }

        VisualElement ModCard(string ptr, int i, SoddModulator m, string unit)
        {
            string mp = $"{ptr}/modulators/{i}";
            var card = new VisualElement(); card.AddToClassList("sd-card");
            var head = new VisualElement(); head.AddToClassList("sd-card-head");
            var title = new Label(m.type.ToString()); title.AddToClassList("sd-card-title");
            head.Add(title);
            head.Add(Btn("✕", () => _report(SoddCommands.RemoveModulator(_evt, ptr, i)), "Quitar modulador", "sd-btn--icon"));
            card.Add(head);
            switch (m.type)
            {
                case SoddModulatorType.Random:
                    card.Add(Num("min", m.min, mp + "/min", "Mínimo")); card.Add(Num("max", m.max, mp + "/max", "Máximo"));
                    card.Add(Enum("unidad", m.unit, mp + "/unit", "Auto = " + unit));
                    card.Add(Bool("por disparo", m.perTrigger, mp + "/perTrigger", "true: nuevo valor en cada región disparada; false: uno por instancia"));
                    break;
                case SoddModulatorType.ParameterCurve:
                case SoddModulatorType.RandomScaledByParameter:
                    card.Add(Choice("parámetro", ParameterNames(), m.parameter, mp + "/parameter", "Parámetro que mueve la curva"));
                    if (m.type == SoddModulatorType.RandomScaledByParameter)
                    {
                        card.Add(Num("min", m.min, mp + "/min", "Mínimo aleatorio")); card.Add(Num("max", m.max, mp + "/max", "Máximo aleatorio"));
                        card.Add(Enum("unidad", m.unit, mp + "/unit", "Auto = " + unit));
                    }
                    var pd = FindParam(m.parameter);
                    var editor = new SoddCurveEditor(m.curve, pd != null ? pd.min : 0, pd != null ? pd.max : 1,
                        m.type == SoddModulatorType.ParameterCurve ? unit : "escala",
                        json => Set(mp + "/curve", json));
                    card.Add(editor);
                    var help = new Label(m.type == SoddModulatorType.ParameterCurve
                        ? $"x = {m.parameter} · y = valor que se suma ({unit}). Arrastrá puntos, doble click agrega, click derecho borra."
                        : $"x = {m.parameter} · y = cuánto del rango aleatorio se aplica (0..1).");
                    help.AddToClassList("sd-help");
                    card.Add(help);
                    break;
                case SoddModulatorType.LFO:
                    card.Add(Enum("forma", m.shape, mp + "/shape", "Forma de onda"));
                    card.Add(Num("rate (Hz)", m.rate, mp + "/rate", "Ciclos por segundo"));
                    card.Add(Num($"depth ({unit})", m.depth, mp + "/depth", "Amplitud"));
                    card.Add(Num("fase", m.phase, mp + "/phase", "Fase inicial 0..1"));
                    break;
                case SoddModulatorType.Envelope:
                    card.Add(Num("attack (s)", m.attack, mp + "/attack", "Subida")); card.Add(Num("hold (s)", m.hold, mp + "/hold", "Sostén"));
                    card.Add(Num("release (s)", m.release, mp + "/release", "Bajada")); card.Add(Num("depth", m.depth, mp + "/depth", "Profundidad (no aplica a volumen)"));
                    break;
                case SoddModulatorType.ADSR:
                    card.Add(Num("attack (s)", m.attack, mp + "/attack", "Subida")); card.Add(Num("decay (s)", m.decay, mp + "/decay", "Caída al sustain"));
                    card.Add(Num("sustain", m.sustain, mp + "/sustain", "Nivel 0..1")); card.Add(Num("release (s)", m.release, mp + "/release", "Fade-out al detener"));
                    break;
            }
            return card;
        }

        // ------------------------------------------------------------------ evento
        public void BuildEvent(VisualElement root)
        {
            var d = _a.data;
            var s = Section(root, "Evento", "Propiedades generales. Todo se guarda en el .sodd.json (con Undo).");
            var pathRow = new VisualElement(); pathRow.AddToClassList("sd-row");
            pathRow.Add(new Label(d.path) { style = { flexGrow = 1, unityFontStyleAndWeight = FontStyle.Bold } });
            pathRow.Add(Btn("Renombrar", () => SoddPrompt.Show("Renombrar evento", "Nombre nuevo", d.Name, n => _report(SoddCommands.RenameEvent(_evt, n)))));
            pathRow.Add(Btn("Mover", () => SoddPrompt.Show("Mover evento", "Path nuevo (carpetas/nombre)", d.path, n => _report(SoddCommands.MoveEvent(_evt, n)))));
            s.Add(pathRow);
            s.Add(Text("Descripción", d.description, "/description", "Cómo suena y cuándo se usa (la IA también la lee)", true));
            var tags = new TextField("Tags") { value = string.Join(", ", d.tags), isDelayed = true, tooltip = "Separados por coma" };
            tags.RegisterValueChangedCallback(e =>
            {
                var arr = JsonNode.NewArray();
                foreach (var t in e.newValue.Split(',')) if (t.Trim().Length > 0) arr.Add(JsonNode.From(t.Trim()));
                Set("/tags", SoddJson.Write(arr, false));
            });
            s.Add(Row(tags));
            var buses = SoddProjectFiles.LoadSettings().buses.Select(b => b.name).ToList();
            s.Add(Choice("Bus", buses, d.bus, "/bus", "Bus de salida"));
            s.Add(ModProp("Volumen", "/volume", d.volume, "dB", "Volumen maestro del evento"));
            s.Add(ModProp("Pitch", "/pitch", d.pitch, "st", "Pitch maestro en semitonos"));

            var s3 = Section(root, "Audio 3D");
            s3.Add(Bool("Es 3D", d.is3D, "/is3D", "Posicionado en el mundo"));
            if (d.is3D)
            {
                var sp = d.spatial;
                s3.Add(Num("Spatial blend", sp.spatialBlend, "/spatial/spatialBlend", "0 = 2D, 1 = 3D"));
                s3.Add(Num("Min distance", sp.minDistance, "/spatial/minDistance", "Hasta acá volumen completo"));
                s3.Add(Num("Max distance", sp.maxDistance, "/spatial/maxDistance", "Desde acá no se oye/atenúa"));
                s3.Add(Enum("Rolloff", sp.rolloff, "/spatial/rolloff", "Curva de atenuación"));
                s3.Add(Num("Doppler", sp.doppler, "/spatial/doppler", "0 = sin doppler"));
                s3.Add(Num("Spread", sp.spread, "/spatial/spread", "Apertura en grados"));
            }

            var sv = Section(root, "Voces y red");
            sv.Add(Int("Prioridad", d.priority, "/priority", "0 = más importante, 256 = menos"));
            sv.Add(Int("Máx. instancias", d.maxInstances, "/maxInstances", "0 = sin límite"));
            sv.Add(Enum("Al superar", d.stealMode, "/stealMode", "Oldest / Quietest / None"));
            sv.Add(Num("Cooldown (s)", d.cooldown, "/cooldown", "Tiempo mínimo entre disparos"));
            sv.Add(Enum("Red", d.networkMode, "/networkMode", "Local / Broadcast / OwnerOnly (Netcode)"));

            var spm = Section(root, "Parámetros locales", "Valores por instancia (ej. Speed de un motor). Los globales están abajo en la barra.");
            for (int i = 0; i < d.parameters.Length; i++)
            {
                var p = d.parameters[i]; int idx = i;
                var r = new VisualElement(); r.AddToClassList("sd-row");
                r.Add(new Label($"{p.name}  [{p.min}..{p.max}] def {p.defaultValue}" + (p.seekSpeed > 0 ? $" seek {p.seekSpeed}/s" : "")) { style = { flexGrow = 1 } });
                r.Add(Btn("✕", () => Patch($"[{{\"op\":\"remove\",\"path\":\"/parameters/{idx}\"}}]"), "Quitar parámetro", "sd-btn--icon"));
                spm.Add(r);
            }
            spm.Add(Btn("＋ Parámetro local", () => SoddPrompt.Show("Nuevo parámetro local", "Nombre (ej. Speed)", "Speed",
                n => _report(SoddCommands.AddParameter(_evt, "{\"name\":" + J(n) + ",\"min\":0,\"max\":1,\"default\":0}")))));

            var sl = Section(root, "Loops y markers", "Click derecho en la regla del timeline para agregar.");
            for (int i = 0; i < d.loopRegions.Length; i++)
            {
                var l = d.loopRegions[i]; int idx = i;
                var card = new VisualElement(); card.AddToClassList("sd-card"); card.AddToClassList("sd-card--fx");
                var head = new VisualElement(); head.AddToClassList("sd-card-head");
                var t = new Label("⟳ " + l.name); t.AddToClassList("sd-card-title"); head.Add(t);
                head.Add(Btn("✕", () => Patch($"[{{\"op\":\"remove\",\"path\":\"/loopRegions/{idx}\"}}]"), "Quitar loop", "sd-btn--icon"));
                card.Add(head);
                card.Add(Num("Inicio (s)", l.start, $"/loopRegions/{i}/start", "Inicio del loop"));
                card.Add(Num("Fin (s)", l.end, $"/loopRegions/{i}/end", "Fin del loop"));
                var names = ParameterNames(); names.Insert(0, "(siempre)");
                var cond = new DropdownField("Salir si", names, l.HasExitCondition ? l.exitWhen.parameter : "(siempre)") { tooltip = "Parámetro que hace salir del loop" };
                cond.RegisterValueChangedCallback(e =>
                {
                    if (e.newValue == "(siempre)") Patch($"[{{\"op\":\"remove\",\"path\":\"/loopRegions/{idx}/exitWhen\"}}]");
                    else Set($"/loopRegions/{idx}/exitWhen", "{\"parameter\":" + J(e.newValue) + ",\"op\":\"Greater\",\"value\":0.5}");
                });
                card.Add(Row(cond));
                if (l.HasExitCondition)
                {
                    card.Add(Enum("es", l.exitWhen.op, $"/loopRegions/{i}/exitWhen/op", "Comparación"));
                    card.Add(Num("valor", l.exitWhen.value, $"/loopRegions/{i}/exitWhen/value", "Umbral"));
                }
                sl.Add(card);
            }
            for (int i = 0; i < d.markers.Length; i++)
            {
                var m = d.markers[i]; int idx = i;
                var r = new VisualElement(); r.AddToClassList("sd-row");
                var nf = new TextField { value = m.name, isDelayed = true, style = { flexGrow = 1 } };
                nf.RegisterValueChangedCallback(e => Set($"/markers/{idx}/name", J(e.newValue)));
                var tf = new FloatField { value = m.time, isDelayed = true, style = { width = 60 } };
                tf.RegisterValueChangedCallback(e => Set($"/markers/{idx}/time", J(e.newValue)));
                r.Add(new Label("◆") { style = { color = new Color(1f, 0.82f, 0.4f) } }); r.Add(nf); r.Add(tf);
                r.Add(Btn("✕", () => Patch($"[{{\"op\":\"remove\",\"path\":\"/markers/{idx}\"}}]"), "Quitar marker", "sd-btn--icon"));
                sl.Add(r);
            }
            Problems(root);
        }

        void Problems(VisualElement root)
        {
            if (_a.importWarnings.Length == 0) return;
            var s = Section(root, "Problemas");
            foreach (var w in _a.importWarnings)
            {
                var l = new Label(w); l.AddToClassList(w.StartsWith("✖") ? "sd-error" : "sd-warning");
                s.Add(l);
            }
        }

        // ------------------------------------------------------------------ track
        public void BuildTrack(VisualElement root, int t)
        {
            var tr = _a.data.tracks[t];
            string tp = $"/tracks/{t}";
            var s = Section(root, "Track");
            s.Add(Text("Nombre", tr.name, tp + "/name", "Nombre del track"));
            s.Add(Enum("Tipo", tr.type, tp + "/type", "Define el color (SFX naranja, Music violeta, Voice verde, Ambience azul)"));
            s.Add(Bool("Mute", tr.mute, tp + "/mute", "Silenciar")); s.Add(Bool("Solo", tr.solo, tp + "/solo", "Solo este track"));
            var buses = SoddProjectFiles.LoadSettings().buses.Select(b => b.name).ToList(); buses.Insert(0, "(del evento)");
            var bus = new DropdownField("Bus", buses, string.IsNullOrEmpty(tr.bus) ? "(del evento)" : tr.bus) { tooltip = "Bus propio del track" };
            bus.RegisterValueChangedCallback(e => { if (e.newValue == "(del evento)") Patch($"[{{\"op\":\"remove\",\"path\":\"{tp}/bus\"}}]"); else Set(tp + "/bus", J(e.newValue)); });
            s.Add(Row(bus));
            s.Add(ModProp("Volumen", tp + "/volume", tr.volume, "dB", "Volumen del track"));
            s.Add(ModProp("Pan", tp + "/pan", tr.pan, "-1..1", "Paneo (solo 2D)"));
            s.Add(ModProp("Reverb send", tp + "/reverbSend", tr.reverbSend, "-1..0", "Envío a reverb de zona"));

            var fx = Section(root, "Efectos", "Se procesan en orden fijo (filtros primero, reverb al final).");
            for (int e = 0; e < tr.effects.Length; e++)
            {
                var effect = tr.effects[e]; int idx = e;
                var info = SoddEffectCatalog.Find(effect.type);
                var card = new VisualElement(); card.AddToClassList("sd-card"); card.AddToClassList("sd-card--fx");
                var head = new VisualElement(); head.AddToClassList("sd-card-head");
                var title = new Label(effect.type) { tooltip = info?.description }; title.AddToClassList("sd-card-title"); head.Add(title);
                var by = new Toggle("bypass") { value = effect.bypass };
                by.RegisterValueChangedCallback(ev => Set($"{tp}/effects/{idx}/bypass", ev.newValue ? "true" : "false"));
                head.Add(by);
                head.Add(Btn("✕", () => _report(SoddCommands.RemoveEffect(_evt, t, idx)), "Quitar efecto", "sd-btn--icon"));
                card.Add(head);
                if (info != null)
                    for (int p = 0; p < info.parameters.Length && p < effect.parameters.Length; p++)
                    {
                        var pi = info.parameters[p];
                        card.Add(ModProp(pi.name, $"{tp}/effects/{idx}/params/{pi.name}", effect.parameters[p].value, pi.unit, $"{pi.description} [{pi.min}..{pi.max}]"));
                    }
                fx.Add(card);
            }
            var add = Btn("＋ Efecto", null, "Agregar efecto al track");
            add.clicked += () =>
            {
                var menu = new GenericDropdownMenu();
                foreach (var info in SoddEffectCatalog.All)
                {
                    var n = info.name;
                    menu.AddItem(n + (info.native ? "" : " (DSP)"), false, () => _report(SoddCommands.AddEffect(_evt, t, J(n))));
                }
                menu.DropDown(add.worldBound, add, default(DropdownMenuSizeMode));
            };
            fx.Add(add);
            var danger = Section(root, null);
            danger.Add(Btn("Borrar track", () => { if (EditorUtility.DisplayDialog("Borrar track", $"¿Borrar el track '{tr.name}'? (se puede deshacer)", "Borrar", "Cancelar")) _report(SoddCommands.RemoveTrack(_evt, t)); }, null, "sd-btn--danger"));
        }

        // ------------------------------------------------------------------ región
        public void BuildRegion(VisualElement root, int t, int g)
        {
            var reg = _a.data.tracks[t].regions[g];
            string rp = $"/tracks/{t}/regions/{g}", ip = rp + "/instrument";
            var s = Section(root, "Región", "Arrastrala en el timeline; bordes = largo; círculos arriba = fades.");
            s.Add(Text("Nombre", reg.name, rp + "/name", "Opcional"));
            s.Add(Num("Inicio (s)", reg.start, rp + "/start", "Inicio en el timeline"));
            s.Add(Num("Largo (s)", reg.length, rp + "/length", "Duración"));
            s.Add(Num("Fade in (s)", reg.fadeIn, rp + "/fadeIn", "Fade de entrada"));
            s.Add(Enum("Curva in", reg.fadeInCurve, rp + "/fadeInCurve", "Forma del fade in"));
            s.Add(Num("Fade out (s)", reg.fadeOut, rp + "/fadeOut", "Fade de salida"));
            s.Add(Enum("Curva out", reg.fadeOutCurve, rp + "/fadeOutCurve", "Forma del fade out"));

            var ins = reg.instrument;
            var si = Section(root, "Instrumento");
            si.Add(Enum("Tipo", ins.type, ip + "/type", "SingleClip, RandomClip (aleatorio), Sequential (en orden), Silence, NestedEvent"));
            if (ins.type == SoddInstrumentType.NestedEvent)
                si.Add(Text("Evento", ins.nestedEvent, ip + "/nestedEvent", "id o path del evento anidado"));
            else if (ins.type != SoddInstrumentType.Silence)
            {
                for (int c = 0; c < ins.clips.Length; c++)
                {
                    int idx = c;
                    var cr = ins.clips[c];
                    var r = new VisualElement(); r.AddToClassList("sd-row");
                    var clipAsset = cr.clipIndex >= 0 && cr.clipIndex < _a.clips.Length ? _a.clips[cr.clipIndex] : null;
                    var of = new ObjectField { objectType = typeof(AudioClip), value = clipAsset, style = { flexGrow = 1 } };
                    of.RegisterValueChangedCallback(e =>
                    {
                        if (e.newValue == null) return;
                        Set($"{ip}/clips/{idx}", J(AssetDatabase.GetAssetPath(e.newValue)));
                    });
                    r.Add(of);
                    if (ins.type == SoddInstrumentType.RandomClip)
                    {
                        var w = new FloatField { value = cr.weight, isDelayed = true, tooltip = "Peso relativo", style = { width = 40 } };
                        w.RegisterValueChangedCallback(e => Set($"{ip}/clips/{idx}/weight", J(e.newValue)));
                        r.Add(w);
                    }
                    r.Add(Btn("✕", () => Patch($"[{{\"op\":\"remove\",\"path\":\"{ip}/clips/{idx}\"}}]"), "Quitar clip", "sd-btn--icon"));
                    si.Add(r);
                }
                var addClip = new ObjectField("＋ Clip") { objectType = typeof(AudioClip), tooltip = "Soltá o elegí un AudioClip para agregarlo" };
                addClip.RegisterValueChangedCallback(e =>
                {
                    if (e.newValue == null) return;
                    // Reescribe la lista completa como "clips" (y quita el atajo "clip") en un solo paso de undo.
                    var arr = JsonNode.NewArray();
                    foreach (var c in ins.clips) arr.Add(JsonNode.From(c.path));
                    arr.Add(JsonNode.From(AssetDatabase.GetAssetPath(e.newValue)));
                    bool hasClipKey = SoddJson.Resolve(SoddJson.Parse(SoddCommands.GetEvent(_evt).data), ip + "/clip") != null;
                    Patch("[" + (hasClipKey ? "{\"op\":\"remove\",\"path\":\"" + ip + "/clip\"}," : "") +
                          "{\"op\":\"add\",\"path\":\"" + ip + "/clips\",\"value\":" + SoddJson.Write(arr, false) + "}]");
                });
                si.Add(Row(addClip));
                if (ins.type == SoddInstrumentType.RandomClip) si.Add(Bool("Evitar repetir", ins.avoidRepeat, ip + "/avoidRepeat", "No elegir el mismo clip dos veces seguidas"));
                if (ins.type == SoddInstrumentType.Sequential)
                    si.Add(Choice("Orden", new List<string> { nameof(SoddSequenceScope.Global), nameof(SoddSequenceScope.Instance) }, ins.sequenceScope.ToString(), ip + "/sequenceScope",
                        "Global: el orden sigue entre todas las instancias del evento. Instance: cada instancia lleva su propio orden"));
                si.Add(Bool("Loop del clip", ins.loop, ip + "/loop", "Repetir el clip hasta el final de la región"));
            }
            si.Add(ModProp("Volumen", ip + "/volume", ins.volume, "dB", "Volumen del instrumento"));
            si.Add(ModProp("Pitch", ip + "/pitch", ins.pitch, "st", "Pitch del instrumento"));
            si.Add(ModProp("Start offset", ip + "/startOffset", ins.startOffset, "s", "Segundos a saltear del clip"));
            var danger = Section(root, null);
            var row = new VisualElement(); row.AddToClassList("sd-row");
            row.Add(Btn("Duplicar (D)", () => _report(SoddCommands.DuplicateRegion(_evt, t, g))));
            row.Add(Btn("Borrar (Supr)", () => _report(SoddCommands.RemoveRegion(_evt, t, g)), null, "sd-btn--danger"));
            danger.Add(row);
        }
    }

    /// <summary>Editor de curvas [[x,y]...]: arrastrar puntos, doble click agrega, click derecho borra.</summary>
    internal sealed class SoddCurveEditor : VisualElement
    {
        readonly List<Vector2> _pts;
        readonly float _xMin, _xMax;
        float _yMin, _yMax;
        readonly Action<string> _commit;
        int _drag = -1;

        public SoddCurveEditor(SoddCurve curve, float xMin, float xMax, string yUnit, Action<string> commit)
        {
            AddToClassList("sd-curve");
            _pts = new List<Vector2>(curve.points ?? Array.Empty<Vector2>());
            if (_pts.Count == 0) { _pts.Add(new Vector2(xMin, 0)); _pts.Add(new Vector2(xMax, 1)); }
            _xMin = Mathf.Min(xMin, _pts.Min(p => p.x)); _xMax = Mathf.Max(xMax, _pts.Max(p => p.x));
            if (_xMax - _xMin < 1e-4f) _xMax = _xMin + 1;
            FitY();
            _commit = commit;
            tooltip = $"x: {_xMin}..{_xMax} · y: {yUnit}";
            generateVisualContent += Draw;
            RegisterCallback<PointerDownEvent>(Down);
            RegisterCallback<PointerMoveEvent>(Move);
            RegisterCallback<PointerUpEvent>(Up);
        }

        void FitY()
        {
            _yMin = Mathf.Min(0, _pts.Min(p => p.y)); _yMax = Mathf.Max(1, _pts.Max(p => p.y));
            float pad = (_yMax - _yMin) * 0.1f; _yMin -= pad; _yMax += pad;
        }

        Vector2 ToPx(Vector2 v)
        {
            var r = contentRect;
            return new Vector2((v.x - _xMin) / (_xMax - _xMin) * r.width, r.height - (v.y - _yMin) / (_yMax - _yMin) * r.height);
        }

        Vector2 FromPx(Vector2 p)
        {
            var r = contentRect;
            return new Vector2(Mathf.Lerp(_xMin, _xMax, Mathf.Clamp01(p.x / r.width)), Mathf.Lerp(_yMin, _yMax, Mathf.Clamp01(1 - p.y / r.height)));
        }

        int Hit(Vector2 p)
        {
            for (int i = 0; i < _pts.Count; i++) if (Vector2.Distance(ToPx(_pts[i]), p) < 8) return i;
            return -1;
        }

        void Down(PointerDownEvent e)
        {
            int h = Hit(e.localPosition);
            if (e.button == 1 && h >= 0 && _pts.Count > 1) { _pts.RemoveAt(h); Commit(); return; }
            if (e.button != 0) return;
            if (h < 0 && e.clickCount == 2)
            {
                _pts.Add(FromPx(e.localPosition)); _pts.Sort((a, b) => a.x.CompareTo(b.x)); Commit(); return;
            }
            if (h >= 0) { _drag = h; this.CapturePointer(e.pointerId); }
        }

        void Move(PointerMoveEvent e)
        {
            if (_drag < 0) return;
            _pts[_drag] = FromPx(e.localPosition);
            MarkDirtyRepaint();
        }

        void Up(PointerUpEvent e)
        {
            if (_drag < 0) return;
            this.ReleasePointer(e.pointerId);
            _drag = -1;
            _pts.Sort((a, b) => a.x.CompareTo(b.x));
            Commit();
        }

        void Commit()
        {
            var arr = JsonNode.NewArray();
            foreach (var p in _pts)
            {
                var pt = JsonNode.NewArray();
                pt.Add(JsonNode.From(Math.Round(p.x, 4))); pt.Add(JsonNode.From(Math.Round(p.y, 4)));
                arr.Add(pt);
            }
            MarkDirtyRepaint();
            _commit(SoddJson.Write(arr, false));
        }

        void Draw(MeshGenerationContext mgc)
        {
            var p = mgc.painter2D;
            var r = contentRect;
            p.strokeColor = new Color(1, 1, 1, 0.08f); p.lineWidth = 1;
            for (int i = 1; i < 4; i++)
            {
                p.BeginPath(); p.MoveTo(new Vector2(r.width * i / 4f, 0)); p.LineTo(new Vector2(r.width * i / 4f, r.height)); p.Stroke();
                p.BeginPath(); p.MoveTo(new Vector2(0, r.height * i / 4f)); p.LineTo(new Vector2(r.width, r.height * i / 4f)); p.Stroke();
            }
            var zero = ToPx(new Vector2(_xMin, 0));
            p.strokeColor = new Color(1, 1, 1, 0.2f);
            p.BeginPath(); p.MoveTo(new Vector2(0, zero.y)); p.LineTo(new Vector2(r.width, zero.y)); p.Stroke();
            p.strokeColor = new Color32(0xb4, 0x8c, 0xff, 0xff); p.lineWidth = 2;
            p.BeginPath();
            p.MoveTo(new Vector2(0, ToPx(_pts[0]).y));
            foreach (var pt in _pts) p.LineTo(ToPx(pt));
            p.LineTo(new Vector2(r.width, ToPx(_pts[_pts.Count - 1]).y));
            p.Stroke();
            p.fillColor = Color.white;
            foreach (var pt in _pts) { p.BeginPath(); p.Arc(ToPx(pt), 4, Angle.Degrees(0), Angle.Degrees(360)); p.Fill(); }
        }
    }

    /// <summary>Diálogo mínimo para pedir un texto (nombre/path).</summary>
    internal sealed class SoddPrompt : EditorWindow
    {
        Action<string> _ok;
        string _label, _value;

        public static void Show(string title, string label, string value, Action<string> ok)
        {
            var w = CreateInstance<SoddPrompt>();
            w.titleContent = new GUIContent(title);
            w._label = label; w._value = value; w._ok = ok;
            w.minSize = w.maxSize = new Vector2(380, 90);
            w.ShowUtility();
        }

        void CreateGUI()
        {
            var root = rootVisualElement;
            root.style.paddingLeft = root.style.paddingRight = root.style.paddingTop = 10;
            var tf = new TextField(_label) { value = _value };
            root.Add(tf);
            var row = new VisualElement { style = { flexDirection = FlexDirection.Row, justifyContent = Justify.FlexEnd, marginTop = 10 } };
            void Accept() { var v = tf.value; Close(); _ok?.Invoke(v); }
            row.Add(new Button(Close) { text = "Cancelar" });
            row.Add(new Button(Accept) { text = "Aceptar" });
            root.Add(row);
            tf.RegisterCallback<KeyDownEvent>(e => { if (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter) Accept(); }, TrickleDown.TrickleDown);
            tf.schedule.Execute(() => tf.Focus());
        }
    }
}
