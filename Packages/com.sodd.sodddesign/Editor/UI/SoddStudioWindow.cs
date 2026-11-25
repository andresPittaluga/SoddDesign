using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.ShortcutManagement;
using UnityEngine;
using UnityEngine.UIElements;

namespace Sodd.Design.Editor
{
    /// <summary>Ganchos que llena el bridge de IA (fase 5) para mostrar estado en el Studio.</summary>
    public static class SoddStudioHooks
    {
        public static Func<bool> BridgeRunning;
        public static Action ToggleBridge;
    }

    /// <summary>SoddDesign Studio: navegador | timeline | inspector + parámetros, transporte y medidor.</summary>
    public sealed class SoddStudioWindow : EditorWindow
    {
        const string Uxml = "Packages/com.sodd.sodddesign/Editor/UI/SoddStudio.uxml";

        [MenuItem(SoddMenu.StudioMenu, priority = 0)]
        public static void Open()
        {
            var w = GetWindow<SoddStudioWindow>();
            w.titleContent = new GUIContent("SoddDesign Studio", EditorGUIUtility.IconContent("AudioMixerController Icon").image);
            w.minSize = new Vector2(900, 480);
        }

        public static void OpenEvent(string idOrPath)
        {
            Open();
            var w = GetWindow<SoddStudioWindow>();
            w._pendingSelect = idOrPath;
            w.RefreshAll();
        }

        /// <summary>Selecciona un evento por id o path (también lo usan los tests y el bridge).</summary>
        public void SelectEvent(string idOrPath) { _pendingSelect = idOrPath; RefreshAll(); }
        internal SoddTimelineState State => _s;
        internal VisualElement Root => _root;

        // ---- estado
        readonly SoddTimelineState _s = new SoddTimelineState();
        [SerializeField] string _selectedId;
        [SerializeField] string _search = "";
        [SerializeField] string _tag;
        [SerializeField] List<string> _collapsed = new List<string>();
        string _pendingSelect;
        SoddEventInstance _inst;
        bool _refreshQueued;

        // ---- UI
        VisualElement _root, _tagsBox, _headers, _canvasHost, _rulerHost, _inspector, _params, _empty, _meterL, _meterR;
        Label _title, _summary, _time, _meterLabel, _emptyTitle, _emptyText, _bridge, _inspTitle;
        ListView _tree;
        SoddTimelineCanvas _canvas;
        SoddRuler _ruler;
        Button _play;
        readonly List<Row> _rows = new List<Row>();

        sealed class Row
        {
            public bool folder;
            public string key, label;
            public int depth;
            public SoddEventAsset asset;
        }

        // ================================================================== ciclo de vida
        void OnEnable()
        {
            SoddCommands.Changed += OnDataChanged;
            SoddUndo.Applied += OnUndo;
            SoddAssetPostprocessor.Changed += QueueRefresh;
            _s.Changed += OnStateChanged;
            _s.SelectionChanged += BuildInspector;
        }

        void OnDisable()
        {
            SoddCommands.Changed -= OnDataChanged;
            SoddUndo.Applied -= OnUndo;
            SoddAssetPostprocessor.Changed -= QueueRefresh;
            _s.Changed -= OnStateChanged;
            _s.SelectionChanged -= BuildInspector;
            if (_inst.IsValid) { _inst.Stop(SoddStopMode.Immediate); _inst.Release(); }
        }

        void OnDataChanged(string key) => QueueRefresh();
        void OnUndo() => QueueRefresh();

        void QueueRefresh()
        {
            if (_refreshQueued || _root == null) return;
            _refreshQueued = true;
            _root.schedule.Execute(() => { _refreshQueued = false; RefreshAll(); });
        }

        public void CreateGUI()
        {
            var tree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(Uxml);
            if (tree == null) { rootVisualElement.Add(new Label("No se encontró " + Uxml)); return; }
            tree.CloneTree(rootVisualElement);
            _root = rootVisualElement.Q("root");
            rootVisualElement.style.flexGrow = 1;

            _title = _root.Q<Label>("event-title");
            _summary = _root.Q<Label>("event-summary");
            _time = _root.Q<Label>("time");
            _meterL = _root.Q("meter-l"); _meterR = _root.Q("meter-r");
            _meterLabel = _root.Q<Label>("meter-label");
            _empty = _root.Q("empty"); _emptyTitle = _root.Q<Label>("empty-title"); _emptyText = _root.Q<Label>("empty-text");
            _headers = _root.Q("track-headers"); _canvasHost = _root.Q("canvas-host"); _rulerHost = _root.Q("ruler-host");
            _inspector = _root.Q("inspector"); _inspTitle = _root.Q<Label>("inspector-title");
            _params = _root.Q("params");
            _bridge = _root.Q<Label>("bridge-status");
            _tagsBox = _root.Q("tags");

            // toolbar
            _root.Q<Button>("btn-new-event").clicked += () => SoddPrompt.Show("Nuevo evento", "Path (ej. sfx/player/jump)", "sfx/new_event", p =>
            {
                var r = SoddCommands.CreateEvent(p);
                Report(r);
                if (r.ok) { _pendingSelect = Sodd.Design.Json.SoddJson.Parse(r.data)["id"].String; QueueRefresh(); }
            });
            _root.Q<Button>("btn-new-track").clicked += () => { if (HasEvent) Report(SoddCommands.AddTrack(_selectedId, "Track " + (_s.Asset.data.tracks.Length + 1))); };
            var snap = _root.Q<DropdownField>("snap");
            var snaps = new List<string> { "Snap: off", "Snap: 10 ms", "Snap: 50 ms", "Snap: 100 ms", "Snap: 250 ms", "Snap: 1/16 @120", "Snap: 1 s" };
            var snapVals = new[] { 0f, 0.01f, 0.05f, 0.1f, 0.25f, 0.125f, 1f };
            snap.choices = snaps; snap.index = 2;
            snap.RegisterValueChangedCallback(e => _s.Snap = snapVals[Mathf.Max(0, snaps.IndexOf(e.newValue))]);
            _root.Q<Button>("btn-zoom-in").clicked += () => { _s.PixelsPerSecond = Mathf.Min(4000, _s.PixelsPerSecond * 1.3f); _s.NotifyChanged(); };
            _root.Q<Button>("btn-zoom-out").clicked += () => { _s.PixelsPerSecond = Mathf.Max(10, _s.PixelsPerSecond / 1.3f); _s.NotifyChanged(); };
            _root.Q<Button>("btn-zoom-fit").clicked += ZoomToFit;
            _root.Q<Button>("btn-mixer").clicked += SoddMixerWindow.Open;
            _root.Q<Button>("btn-profiler").clicked += SoddProfilerWindow.Open;
            _root.Q<Button>("btn-validate").clicked += () =>
            {
                var r = SoddCommands.Validate();
                ShowNotification(new GUIContent(r.ok ? $"✔ Eventos válidos ({r.warnings.Count} avisos)" : $"✖ {r.errors.Count} errores (ver consola)"));
                if (!r.ok || r.warnings.Count > 0) Debug.Log("[SoddDesign] Validación:\n" + r);
            };
            _root.Q<Button>("btn-optimize").clicked += SoddImportOptimizer.Run;
            _root.Q<Button>("btn-help").clicked += () => ShowHelp(_help == null);
            _bridge.RegisterCallback<ClickEvent>(_ => { SoddStudioHooks.ToggleBridge?.Invoke(); UpdateBridge(); });

            // browser
            var search = _root.Q<TextField>("search");
            search.value = _search;
            search.RegisterValueChangedCallback(e => { _search = e.newValue; BuildRows(); });
            _tree = _root.Q<ListView>("tree");
            _tree.makeItem = MakeRow;
            _tree.bindItem = BindRow;
            _tree.itemsSource = _rows;
            _tree.selectionChanged += OnTreeSelection;
            _tree.itemsChosen += _ => TogglePlay();
            _tree.AddManipulator(new ContextualMenuManipulator(TreeContext));

            // timeline
            _ruler = new SoddRuler(_s);
            _rulerHost.Add(_ruler);
            _ruler.ContextRequested = (t, pos) => TimelineContext(-1, -1, t, pos);
            _ruler.MarkerMoved = (i, t) => Report(SoddCommands.MoveMarker(_selectedId, i, t));
            _ruler.MarkerRenameRequested = RenameMarker;
            _ruler.MarkerContextRequested = MarkerContext;
            _canvas = new SoddTimelineCanvas(_s);
            _canvasHost.Add(_canvas);
            _canvas.CommitMove = (t, r, start, len, nt) =>
            {
                var reg = _s.Asset.data.tracks[t].regions[r];
                string p = $"/tracks/{t}/regions/{r}";
                if (nt != t) { Report(SoddCommands.MoveRegion(_selectedId, t, r, start, nt)); return; }
                var ops = new List<string>();
                if (Mathf.Abs(start - reg.start) > 1e-4f) ops.Add($"{{\"op\":\"add\",\"path\":\"{p}/start\",\"value\":{Sodd.Design.Json.SoddJson.FormatNumber(start)}}}");
                if (Mathf.Abs(len - reg.length) > 1e-4f) ops.Add($"{{\"op\":\"add\",\"path\":\"{p}/length\",\"value\":{Sodd.Design.Json.SoddJson.FormatNumber(len)}}}");
                if (ops.Count > 0) Report(SoddCommands.EditEvent(_selectedId, "[" + string.Join(",", ops) + "]"));
            };
            _canvas.CommitProperty = (t, r, prop, v) => Report(SoddCommands.SetProperty(_selectedId, $"/tracks/{t}/regions/{r}/{prop}", Sodd.Design.Json.SoddJson.FormatNumber(Mathf.Max(0, v))));
            _canvas.ClipsDropped = OnClipsDropped;
            _canvas.ContextRequested = TimelineContext;

            // transporte
            _play = _root.Q<Button>("btn-play");
            _play.clicked += TogglePlay;
            _loopBtn = _root.Q<Button>("btn-loop");
            _loopBtn.clicked += ToggleLoopPreview;
            _loopBtn.EnableInClassList("sd-tbtn--on", _loopPreview);
            _root.Q<Button>("btn-stop").clicked += () => StopPreview(true);
            _root.Q<Button>("btn-rewind").clicked += Rewind;

            // Transport buttons never keep keyboard focus, so Space reaches the Play shortcut instead of re-clicking them.
            foreach (var b in _root.Query<Button>(className: "sd-tbtn").ToList()) b.focusable = false;

            // loop de UI: solo el playhead/medidor se actualizan seguido
            _root.schedule.Execute(Live).Every(33);
            _root.schedule.Execute(UpdateBridge).Every(1000);
            RefreshAll();
            if (_helpPending) ShowHelp(true);
        }

        VisualElement _help;
        bool _helpPending;

        /// <summary>Muestra u oculta el panel de atajos.</summary>
        public void ShowHelp(bool show)
        {
            if (_root == null) { _helpPending = show; return; }
            _helpPending = false;
            _help?.RemoveFromHierarchy();
            _help = null;
            if (!show) return;
            _help = SoddHelp.BuildPanel(() => ShowHelp(false));
            _root.Add(_help);
        }

        bool HasEvent => _s.Asset != null && !string.IsNullOrEmpty(_selectedId);

        void Report(SoddResult r)
        {
            if (r == null) return;
            if (!r.ok)
            {
                ShowNotification(new GUIContent("✖ " + r.errors[0]), 4);
                Debug.LogWarning("[SoddDesign] " + r);
            }
            else if (r.warnings.Count > 0) Debug.Log("[SoddDesign] " + r);
        }

        // ================================================================== refresco
        void RefreshAll()
        {
            if (_root == null) return;
            if (_pendingSelect != null)
            {
                var f = SoddCommands.LocateFile(_pendingSelect);
                var a = f != null ? AssetDatabase.LoadAssetAtPath<SoddEventAsset>(f) : null;
                if (a != null) _selectedId = a.Id;
                _pendingSelect = null;
            }
            // recargar el asset seleccionado (puede haberse reimportado o movido)
            SoddEventAsset sel = null;
            if (!string.IsNullOrEmpty(_selectedId))
            {
                var f = SoddCommands.LocateFile(_selectedId);
                sel = f != null ? AssetDatabase.LoadAssetAtPath<SoddEventAsset>(f) : null;
                if (sel == null) _selectedId = null;
            }
            if (_s.Asset != sel && _inst.IsValid) StopPreview(true);
            if (_s.Asset != sel) { _triggers.Clear(); _lastTrigger = null; _s.LastPickedClip.Clear(); }
            _s.Asset = sel; _s.EventId = _selectedId;
            if (sel != null)
            {
                if (_s.SelTrack >= sel.data.tracks.Length) { _s.SelTrack = -1; _s.SelRegion = -1; }
                else if (_s.SelTrack >= 0 && _s.SelRegion >= sel.data.tracks[_s.SelTrack].regions.Length) _s.SelRegion = -1;
                if (_s.SelMarker >= sel.data.markers.Length) _s.SelMarker = -1;
            }
            else _s.SelMarker = -1;
            _s.DragMarker = -1;
            BuildTags();
            BuildRows();
            BuildHeader();
            BuildTrackHeaders();
            BuildInspector();
            BuildParams();
            OnStateChanged();
        }

        void OnStateChanged()
        {
            if (_canvas == null) return;
            _canvas.RepaintAll();
            _ruler.Refresh();
            BuildTrackHeadersSelection();
            _time.text = _s.Playhead.ToString("0.00") + " s";
        }

        void BuildHeader()
        {
            var a = _s.Asset;
            _empty.style.display = DisplayStyle.None;
            if (a == null)
            {
                _title.text = ""; _summary.text = "";
                ShowEmpty("Elegí un evento", "Seleccioná un evento del navegador de la izquierda, o creá uno con ＋ Evento.\nTip: doble click en un evento lo reproduce.");
                return;
            }
            var d = a.data;
            _title.text = d.path;
            _summary.text = $"{(d.is3D ? "3D" : "2D")} · bus {d.bus} · {d.Length:0.00}s · prioridad {d.priority}" +
                            (d.maxInstances > 0 ? $" · máx {d.maxInstances}" : "") + (d.loopRegions.Length > 0 ? " · ⟳ loop" : "") +
                            (d.description.Length > 0 ? "  —  " + d.description : "");
            _summary.tooltip = d.description;
            if (d.tracks.Length == 0) ShowEmpty("Evento vacío", "Arrastrá AudioClips desde la ventana Project hasta acá para crear tracks y regiones.\nO usá ＋ Track en la barra de arriba.");
        }

        void ShowEmpty(string title, string text)
        {
            _empty.style.display = DisplayStyle.Flex;
            _emptyTitle.text = title; _emptyText.text = text;
        }

        void ZoomToFit()
        {
            if (!HasEvent) return;
            float len = Mathf.Max(0.5f, _s.Asset.data.Length);
            float w = Mathf.Max(100, _canvasHost.contentRect.width - 20);
            _s.PixelsPerSecond = Mathf.Clamp(w / len, 10, 4000);
            _s.Offset = 0;
            _s.NotifyChanged();
        }

        // ================================================================== navegador
        void BuildTags()
        {
            _tagsBox.Clear();
            var tags = new SortedSet<string>();
            foreach (var a in SoddProjectFiles.AllEventAssets()) foreach (var t in a.data.tags) tags.Add(t);
            void Chip(string text, string value)
            {
                var b = new Button(() => { _tag = value; BuildTags(); BuildRows(); }) { text = text };
                b.AddToClassList("sd-tag");
                if (_tag == value) b.AddToClassList("sd-tag--on");
                _tagsBox.Add(b);
            }
            Chip("todos", null);
            foreach (var t in tags) Chip(t, t);
        }

        void BuildRows()
        {
            _rows.Clear();
            var events = SoddProjectFiles.AllEventAssets().Where(a => a.data != null).OrderBy(a => a.Path, StringComparer.Ordinal).ToList();
            string q = (_search ?? "").Trim();
            var added = new HashSet<string>();
            foreach (var a in events)
            {
                var d = a.data;
                if (_tag != null && Array.IndexOf(d.tags, _tag) < 0) continue;
                if (q.Length > 0 && d.path.IndexOf(q, StringComparison.OrdinalIgnoreCase) < 0 && (d.description ?? "").IndexOf(q, StringComparison.OrdinalIgnoreCase) < 0
                    && !d.tags.Any(t => t.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0)) continue;
                var parts = d.path.Split('/');
                string acc = "";
                bool hidden = false;
                for (int i = 0; i < parts.Length - 1; i++)
                {
                    acc += parts[i] + "/";
                    if (!hidden && added.Add(acc)) _rows.Add(new Row { folder = true, key = acc, label = parts[i], depth = i });
                    if (q.Length == 0 && _collapsed.Contains(acc)) hidden = true;
                }
                if (!hidden) _rows.Add(new Row { key = d.id, label = parts[parts.Length - 1], depth = parts.Length - 1, asset = a });
            }
            _tree.Rebuild();
            int idx = _rows.FindIndex(r => !r.folder && r.key == _selectedId);
            if (idx >= 0) _tree.SetSelectionWithoutNotify(new[] { idx });
            else _tree.ClearSelection();
        }

        VisualElement MakeRow()
        {
            var r = new VisualElement(); r.AddToClassList("sd-tree-row");
            var dot = new VisualElement { name = "dot" }; dot.AddToClassList("sd-tree-dot");
            r.Add(dot); r.Add(new Label { name = "name" });
            var meta = new Label { name = "meta" }; meta.AddToClassList("sd-tree-meta"); r.Add(meta);
            return r;
        }

        void BindRow(VisualElement ve, int i)
        {
            var row = _rows[i];
            ve.style.paddingLeft = 6 + row.depth * 14;
            var name = ve.Q<Label>("name");
            var dot = ve.Q("dot");
            var meta = ve.Q<Label>("meta");
            if (row.folder)
            {
                name.text = (_collapsed.Contains(row.key) ? "▸ " : "▾ ") + row.label;
                name.AddToClassList("sd-tree-folder");
                dot.style.display = DisplayStyle.None;
                meta.text = "";
                ve.tooltip = "";
                return;
            }
            name.RemoveFromClassList("sd-tree-folder");
            name.text = row.label;
            dot.style.display = DisplayStyle.Flex;
            var d = row.asset.data;
            var type = d.tracks.Length > 0 ? d.tracks[0].type : SoddTrackType.SFX;
            dot.style.backgroundColor = SoddTimelineState.TrackColor(type);
            bool err = row.asset.importWarnings.Any(w => w.StartsWith("✖"));
            meta.text = (err ? "⚠ " : "") + d.Length.ToString("0.0") + "s" + (d.loopRegions.Length > 0 ? " ⟳" : "") + (d.is3D ? " 3D" : "");
            ve.tooltip = string.IsNullOrEmpty(d.description) ? d.path : d.path + "\n" + d.description;
        }

        void OnTreeSelection(IEnumerable<object> sel)
        {
            var row = sel.FirstOrDefault() as Row;
            if (row == null) return;
            if (row.folder)
            {
                if (!_collapsed.Remove(row.key)) _collapsed.Add(row.key);
                BuildRows();
                return;
            }
            if (row.key == _selectedId) return;
            _selectedId = row.key;
            _s.SelTrack = _s.SelRegion = _s.SelMarker = -1;
            _s.Playhead = 0;
            RefreshAll();
            ZoomToFit();
        }

        void TreeContext(ContextualMenuPopulateEvent e)
        {
            if (!(_tree.selectedItem is Row row) || row.folder) { e.menu.AppendAction("Nuevo evento…", _ => _root.Q<Button>("btn-new-event").SendEvent(new NavigationSubmitEvent())); return; }
            string id = row.key;
            e.menu.AppendAction("▶ Preview", _ => SoddPreview.Play(row.asset));
            e.menu.AppendAction("Renombrar…", _ => SoddPrompt.Show("Renombrar", "Nombre", row.asset.data.Name, n => Report(SoddCommands.RenameEvent(id, n))));
            e.menu.AppendAction("Mover…", _ => SoddPrompt.Show("Mover", "Path nuevo", row.asset.Path, n => Report(SoddCommands.MoveEvent(id, n))));
            e.menu.AppendAction("Duplicar", _ => Report(SoddCommands.DuplicateEvent(id)));
            e.menu.AppendAction("Mostrar archivo", _ => EditorGUIUtility.PingObject(row.asset));
            e.menu.AppendSeparator();
            e.menu.AppendAction("Borrar", _ => { if (EditorUtility.DisplayDialog("Borrar evento", $"¿Borrar '{row.asset.Path}'? (se puede deshacer con Ctrl+Z)", "Borrar", "Cancelar")) Report(SoddCommands.DeleteEvent(id)); });
        }

        // ================================================================== tracks
        void BuildTrackHeaders()
        {
            _headers.Clear();
            var a = _s.Asset;
            if (a == null) return;
            for (int t = 0; t < a.data.tracks.Length; t++)
            {
                var tr = a.data.tracks[t];
                int ti = t;
                var h = new VisualElement(); h.AddToClassList("sd-thead");
                h.style.borderLeftColor = SoddTimelineState.TrackColor(tr.type);
                var n = new Label(tr.name); n.AddToClassList("sd-thead-name"); h.Add(n);
                var chips = new VisualElement(); chips.AddToClassList("sd-row");
                Button Chip(string text, string tip, bool on, string onCls, Action a2)
                {
                    var b = new Button(a2) { text = text, tooltip = tip }; b.AddToClassList("sd-chip"); if (on) b.AddToClassList(onCls); chips.Add(b); return b;
                }
                Chip("M", "Mute", tr.mute, "sd-chip--mute", () => Report(SoddCommands.SetProperty(_selectedId, $"/tracks/{ti}/mute", tr.mute ? "false" : "true")));
                Chip("S", "Solo", tr.solo, "sd-chip--solo", () => Report(SoddCommands.SetProperty(_selectedId, $"/tracks/{ti}/solo", tr.solo ? "false" : "true")));
                var vol = new Label(VolumeText(tr.volume.@base, tr.volume.HasModulators)); vol.AddToClassList("sd-chip"); vol.AddToClassList("sd-chip--drag"); chips.Add(vol);
                DraggableVolume(vol, ti, tr.volume.@base, tr.volume.HasModulators);
                if (tr.effects.Length > 0) { var fx = new Label("FX " + tr.effects.Length) { tooltip = string.Join(", ", tr.effects.Select(f => f.type)) }; fx.AddToClassList("sd-chip"); chips.Add(fx); }
                h.Add(chips);
                int count = a.data.tracks.Length;
                h.tooltip = "Click: seleccionar · Arrastrar arriba/abajo: reordenar · Click derecho: más opciones";
                h.RegisterCallback<PointerDownEvent>(e => { if (e.button != 0) return; _s.Select(ti, -1); BeginTrackDrag(h, ti, e); });
                h.RegisterCallback<PointerMoveEvent>(e => DragTrack(h, e));
                h.RegisterCallback<PointerUpEvent>(e => EndTrackDrag(h, e));
                h.AddManipulator(new ContextualMenuManipulator(e =>
                {
                    e.menu.AppendAction("Renombrar…", _ => SoddPrompt.Show("Renombrar track", "Nombre", tr.name, v => Report(SoddCommands.SetProperty(_selectedId, $"/tracks/{ti}/name", Sodd.Design.Json.SoddJson.Write(Sodd.Design.Json.JsonNode.From(v), false)))));
                    e.menu.AppendAction("Subir", _ => MoveTrackTo(ti, ti - 1), ti > 0 ? DropdownMenuAction.Status.Normal : DropdownMenuAction.Status.Disabled);
                    e.menu.AppendAction("Bajar", _ => MoveTrackTo(ti, ti + 1), ti < count - 1 ? DropdownMenuAction.Status.Normal : DropdownMenuAction.Status.Disabled);
                    e.menu.AppendAction("Duplicar track", _ => Report(SoddCommands.DuplicateTrack(_selectedId, ti)));
                    e.menu.AppendSeparator();
                    e.menu.AppendAction("Borrar track", _ => Report(SoddCommands.RemoveTrack(_selectedId, ti)));
                }));
                _headers.Add(h);
            }
            var add = new Button(() => { if (HasEvent) Report(SoddCommands.AddTrack(_selectedId, "Track " + (a.data.tracks.Length + 1))); }) { text = "＋ Track", tooltip = "Agregar track (o soltá clips en el timeline)" };
            add.AddToClassList("sd-btn"); add.style.marginTop = 8; add.style.marginLeft = 8; add.style.alignSelf = Align.FlexStart;
            _headers.Add(add);
        }

        static string VolumeText(float db, bool modulated) => (db >= 0 ? "+" : "") + db.ToString("0.#") + " dB" + (modulated ? " ∿" : "");

        /// <summary>Chip de volumen del track: arrastrar arriba/abajo cambia los dB (Shift = fino), doble click = 0 dB; se guarda al soltar.</summary>
        void DraggableVolume(Label chip, int track, float start, bool modulated)
        {
            float value = start, downY = 0f;
            bool moved = false;
            string ptr = $"/tracks/{track}/volume";
            chip.tooltip = "Volumen del track: arrastrá arriba/abajo (Shift = fino) · doble click = 0 dB";
            chip.RegisterCallback<PointerDownEvent>(e =>
            {
                if (e.button != 0) return;
                e.StopPropagation();
                if (e.clickCount == 2) { Report(SoddCommands.SetProperty(_selectedId, ptr, "0")); return; }
                _s.Select(track, -1);
                downY = e.position.y; moved = false; value = start;
                chip.CapturePointer(e.pointerId);
            });
            chip.RegisterCallback<PointerMoveEvent>(e =>
            {
                if (!chip.HasPointerCapture(e.pointerId)) return;
                float dy = downY - e.position.y;
                if (!moved && Mathf.Abs(dy) < 2f) return;
                moved = true;
                value = Mathf.Clamp(start + dy * (e.shiftKey ? 0.025f : 0.25f), -80f, 10f);
                chip.text = VolumeText(value, modulated);
            });
            chip.RegisterCallback<PointerUpEvent>(e =>
            {
                if (!chip.HasPointerCapture(e.pointerId)) return;
                chip.ReleasePointer(e.pointerId);
                e.StopPropagation();
                if (moved) Report(SoddCommands.SetProperty(_selectedId, ptr, Sodd.Design.Json.SoddJson.FormatNumber((float)Math.Round(value, 2))));
            });
        }

        // ---- reordenar tracks arrastrando el header
        int _dragTrack = -1, _dropIndex = -1;
        float _dragStartY;
        bool _trackDragging;
        VisualElement _dropLine;

        void BeginTrackDrag(VisualElement h, int track, PointerDownEvent e)
        {
            if (e.target is Button) return;
            _dragTrack = track; _dropIndex = -1; _trackDragging = false; _dragStartY = e.position.y;
            h.CapturePointer(e.pointerId);
        }

        void DragTrack(VisualElement h, PointerMoveEvent e)
        {
            if (_dragTrack < 0 || !h.HasPointerCapture(e.pointerId)) return;
            if (!_trackDragging)
            {
                if (Mathf.Abs(e.position.y - _dragStartY) < 5f) return;
                _trackDragging = true;
                h.AddToClassList("sd-thead--drag");
            }
            var heads = _headers.Children().Where(c => c.ClassListContains("sd-thead")).ToList();
            if (heads.Count == 0) return;
            float y = _headers.WorldToLocal(e.position).y;
            int idx = heads.Count;
            float lineY = heads[heads.Count - 1].layout.yMax;
            for (int i = 0; i < heads.Count; i++)
                if (y < heads[i].layout.center.y) { idx = i; lineY = heads[i].layout.yMin; break; }
            _dropIndex = idx;
            if (_dropLine == null) { _dropLine = new VisualElement { pickingMode = PickingMode.Ignore }; _dropLine.AddToClassList("sd-thead-drop"); }
            if (_dropLine.parent != _headers) _headers.Add(_dropLine);
            _dropLine.style.top = lineY - 1;
        }

        void EndTrackDrag(VisualElement h, PointerUpEvent e)
        {
            if (!h.HasPointerCapture(e.pointerId)) return;
            h.ReleasePointer(e.pointerId);
            h.RemoveFromClassList("sd-thead--drag");
            _dropLine?.RemoveFromHierarchy();
            int from = _dragTrack, drop = _dropIndex;
            bool moved = _trackDragging;
            _dragTrack = _dropIndex = -1; _trackDragging = false;
            if (!moved || drop < 0) return;
            int to = drop > from ? drop - 1 : drop;
            if (to != from) MoveTrackTo(from, to);
        }

        void MoveTrackTo(int from, int to)
        {
            var r = SoddCommands.MoveTrack(_selectedId, from, to);
            Report(r);
            if (r.ok) { _s.SelTrack = Mathf.Clamp(to, 0, _s.Asset.data.tracks.Length - 1); _s.SelRegion = -1; }
        }

        // ---- markers
        void RenameMarker(int i)
        {
            if (!HasEvent || i < 0 || i >= _s.Asset.data.markers.Length) return;
            SoddPrompt.Show("Renombrar marker", "Nombre", _s.Asset.data.markers[i].name, v => Report(SoddCommands.RenameMarker(_selectedId, i, v)));
        }

        void MarkerContext(int i, Vector2 screenPos)
        {
            if (!HasEvent) return;
            var menu = new GenericDropdownMenu();
            menu.AddItem("Renombrar… (doble click)", false, () => RenameMarker(i));
            menu.AddItem("Llevar el playhead acá", false, () => { _s.Playhead = _s.Asset.data.markers[i].time; _s.NotifyChanged(); });
            menu.AddSeparator("");
            menu.AddItem("Borrar marker (Supr)", false, () => { Report(SoddCommands.RemoveMarker(_selectedId, i)); _s.SelMarker = -1; });
            var local = rootVisualElement.WorldToLocal(screenPos);
            menu.DropDown(new Rect(local, Vector2.one), rootVisualElement, default(DropdownMenuSizeMode));
        }

        void BuildTrackHeadersSelection()
        {
            int i = 0;
            foreach (var h in _headers.Children())
            {
                if (!h.ClassListContains("sd-thead")) continue;
                h.EnableInClassList("sd-thead--sel", i == _s.SelTrack && _s.SelRegion < 0);
                i++;
            }
        }

        void OnClipsDropped(int track, float time, AudioClip[] clips)
        {
            if (!HasEvent) return;
            if (track < 0)
            {
                var r = SoddCommands.AddTrack(_selectedId, clips[0].name, "SFX");
                Report(r);
                if (!r.ok) return;
                track = Sodd.Design.Json.SoddJson.Parse(r.data)["index"].AsInt();
            }
            float t = time;
            foreach (var c in clips)
            {
                Report(SoddCommands.AddRegion(_selectedId, track, t, c.length, "{\"type\":\"SingleClip\",\"clip\":" + Sodd.Design.Json.SoddJson.Write(Sodd.Design.Json.JsonNode.From(AssetDatabase.GetAssetPath(c)), false) + "}"));
                t += c.length;
            }
        }

        void TimelineContext(int track, int region, float time, Vector2 screenPos)
        {
            if (!HasEvent) return;
            var menu = new GenericDropdownMenu();
            string id = _selectedId;
            time = _s.SnapTime(time);
            if (track >= 0 && region >= 0)
            {
                menu.AddItem("Dividir en el playhead (S)", false, () => Report(SoddCommands.SplitRegion(id, track, region, _s.Playhead)));
                menu.AddItem("Duplicar (D)", false, () => Report(SoddCommands.DuplicateRegion(id, track, region)));
                menu.AddItem("Borrar (Supr)", false, () => Report(SoddCommands.RemoveRegion(id, track, region)));
                menu.AddSeparator("");
            }
            if (track >= 0 && track < _s.Asset.data.tracks.Length && region < 0)
                menu.AddItem("Agregar región silenciosa aquí", false, () => Report(SoddCommands.AddRegion(id, track, time, 1f, "{\"type\":\"Silence\"}")));
            menu.AddItem($"Agregar marker en {time:0.00}s", false, () => Report(SoddCommands.AddMarker(id, "Marker", time)));
            menu.AddItem($"Agregar loop desde {time:0.00}s (2 s)", false, () => Report(SoddCommands.AddLoopRegion(id, time, time + 2f)));
            var local = rootVisualElement.WorldToLocal(screenPos);
            menu.DropDown(new Rect(local, Vector2.one), rootVisualElement, default(DropdownMenuSizeMode));
        }

        // ================================================================== inspector y parámetros
        void BuildInspector()
        {
            if (_inspector == null) return;
            _inspector.Clear();
            SoddInspector.RandomReadouts.Clear();
            _inspector.schedule.Execute(UpdateRandomReadouts);
            var a = _s.Asset;
            if (a == null)
            {
                _inspTitle.text = "INSPECTOR";
                var l = new Label("Sin selección.\n\nElegí un evento para ver y editar sus propiedades. Todo cambio se guarda en el .sodd.json y se puede deshacer con Ctrl+Z.");
                l.AddToClassList("sd-help"); l.style.paddingLeft = l.style.paddingRight = l.style.paddingTop = 12;
                _inspector.Add(l);
                return;
            }
            var b = new SoddInspector(_selectedId, a, Report);
            if (_s.SelTrack >= 0 && _s.SelRegion >= 0 && _s.SelTrack < a.data.tracks.Length && _s.SelRegion < a.data.tracks[_s.SelTrack].regions.Length)
            {
                _inspTitle.text = $"INSPECTOR · REGIÓN ({a.data.tracks[_s.SelTrack].name})";
                b.BuildRegion(_inspector, _s.SelTrack, _s.SelRegion);
            }
            else if (_s.SelTrack >= 0 && _s.SelTrack < a.data.tracks.Length)
            {
                _inspTitle.text = "INSPECTOR · TRACK";
                b.BuildTrack(_inspector, _s.SelTrack);
            }
            else
            {
                _inspTitle.text = "INSPECTOR · EVENTO";
                b.BuildEvent(_inspector);
            }
        }

        void BuildParams()
        {
            _params.Clear();
            var engine = SoddPreview.Engine;
            var defs = new List<(SoddParameterDef def, bool global)>();
            var g = SoddProjectFiles.LoadParameters();
            if (g != null) foreach (var p in g.parameters) if (p.isGlobal) defs.Add((p, true));
            if (_s.Asset != null) foreach (var p in _s.Asset.data.parameters) defs.Add((p, false));
            if (defs.Count == 0)
            {
                var l = new Label("Sin parámetros. Definí globales en sodd.parameters.json o locales en el Inspector del evento.");
                l.AddToClassList("sd-help"); _params.Add(l);
                return;
            }
            foreach (var (def, global) in defs)
            {
                var box = new VisualElement(); box.AddToClassList("sd-param");
                var head = new VisualElement(); head.AddToClassList("sd-param-head");
                var name = new Label(def.name) { tooltip = def.description }; name.AddToClassList("sd-param-name");
                var scope = new Label(global ? "global" : "local"); scope.AddToClassList("sd-param-scope");
                float cur = global && engine != null ? engine.Parameters.GetTarget(engine.Parameters.IndexOf(def.name)) : (_inst.IsValid ? _inst.GetParameter(def.name) : def.defaultValue);
                var val = new Label(cur.ToString("0.##")); val.AddToClassList("sd-param-value");
                head.Add(name); head.Add(scope); head.Add(val);
                var slider = new Slider(def.min, def.max) { value = cur, tooltip = def.description };
                slider.RegisterValueChangedCallback(e =>
                {
                    val.text = e.newValue.ToString("0.##");
                    var eng = SoddPreview.Engine;
                    if (global && eng != null) eng.Parameters.Set(eng.Parameters.IndexOf(def.name), e.newValue);
                    else if (_inst.IsValid) _inst.SetParameter(def.name, e.newValue);
                    _localParamCache[def.name] = e.newValue;
                });
                box.Add(head); box.Add(slider);
                _params.Add(box);
            }
        }

        readonly Dictionary<string, float> _localParamCache = new Dictionary<string, float>();

        // ================================================================== transporte
        void TogglePlay()
        {
            if (_inst.IsPlaying) { StopPreview(false); return; }
            StartPreview();
        }

        void ToggleLoopPreview()
        {
            _loopPreview = !_loopPreview;
            _loopBtn?.EnableInClassList("sd-tbtn--on", _loopPreview);
        }

        void StartPreview()
        {
            if (!HasEvent) return;
            _userStopped = false;
            _triggers.Clear();
            if (_inst.IsValid) _inst.Release();
            _inst = SoddPreview.CreateInstance(_s.Asset);
            if (!_inst.IsValid) return;
            _inst.Set3DPosition(SoddPreview.Engine.ListenerPosition);
            foreach (var p in _s.Asset.data.parameters)
                if (_localParamCache.TryGetValue(p.name, out var v)) _inst.SetParameter(p.name, v, true);
            _inst.Start();
            _s.Playing = true;
            _play.text = "■";
        }

        void StopPreview(bool immediate)
        {
            _userStopped = true;
            if (_inst.IsValid) _inst.Stop(immediate ? SoddStopMode.Immediate : SoddStopMode.AllowFadeOut);
            if (immediate) { _inst.Release(); _inst = default; }
        }

        // ---- lo que tocó cada región en el último play (para ver los random)
        [SerializeField] bool _loopPreview;
        Button _loopBtn;
        bool _userStopped;
        readonly SoddTriggerInfo[] _trigBuf = new SoddTriggerInfo[SoddInstanceLogSize];
        const int SoddInstanceLogSize = 16;
        /// <summary>Último disparo por región (clave track*4096+region) del play actual o del último.</summary>
        readonly Dictionary<int, SoddTriggerInfo> _triggers = new Dictionary<int, SoddTriggerInfo>();
        SoddTriggerInfo? _lastTrigger;

        void CaptureTriggers()
        {
            if (!_inst.IsValid) return;
            int n = _inst.GetRecentTriggers(_trigBuf);
            bool changed = false;
            for (int k = n - 1; k >= 0; k--)
            {
                var t = _trigBuf[k];
                int key = t.track * 4096 + t.region;
                if (_triggers.TryGetValue(key, out var old) && old.seed == t.seed) continue;
                _triggers[key] = t; _lastTrigger = t; changed = true;
            }
            if (!changed) return;
            _s.LastPickedClip.Clear();
            foreach (var kv in _triggers) _s.LastPickedClip[kv.Key] = kv.Value.clip;
            _canvas.RepaintAll();
            UpdateRandomReadouts();
        }

        /// <summary>Muestra al lado de cada perilla con random el valor que tocó en el último play.</summary>
        void UpdateRandomReadouts()
        {
            var a = _s.Asset;
            if (a == null) return;
            var engine = SoddPreview.Engine;
            var local = new float[16];
            var ps = a.data.parameters;
            for (int k = 0; k < ps.Length && k < local.Length; k++)
                local[k] = _localParamCache.TryGetValue(ps[k].name, out var v) ? v : ps[k].defaultValue;
            foreach (var r in SoddInspector.RandomReadouts)
            {
                if (!TriggerFor(r.ptr, out var t)) { r.label.text = "—"; continue; }
                var ctx = new SoddModContext { local = local, global = engine?.Parameters.Values, time = 0f, stopTime = -1f, triggerSeed = t.seed, instanceSeed = t.instanceSeed };
                float val = SoddModulation.Evaluate(r.prop, r.kind, ref ctx);
                string s = val.ToString(Mathf.Abs(val) >= 100 ? "0" : "0.0#");
                if ((r.unit == "dB" || r.unit == "st") && val > 0) s = "+" + s;
                r.label.text = "▶ " + s + (r.unit == "dB" || r.unit == "st" ? " " + r.unit : "");
            }
        }

        /// <summary>El disparo que corresponde a una propiedad: el de su región, el más reciente de su track o el último del evento.</summary>
        bool TriggerFor(string ptr, out SoddTriggerInfo t)
        {
            t = default;
            var parts = ptr.Split('/');
            if (parts.Length > 4 && parts[1] == "tracks" && parts[3] == "regions" && int.TryParse(parts[2], out int tr) && int.TryParse(parts[4], out int rg))
                return _triggers.TryGetValue(tr * 4096 + rg, out t);
            if (parts.Length > 2 && parts[1] == "tracks" && int.TryParse(parts[2], out int tk))
            {
                if (_lastTrigger.HasValue && _lastTrigger.Value.track == tk) { t = _lastTrigger.Value; return true; }
                foreach (var kv in _triggers) if (kv.Value.track == tk) { t = kv.Value; return true; }
                return false;
            }
            if (_s.SelTrack >= 0 && _s.SelRegion >= 0 && _triggers.TryGetValue(_s.SelTrack * 4096 + _s.SelRegion, out t)) return true;
            if (_lastTrigger.HasValue) { t = _lastTrigger.Value; return true; }
            return false;
        }

        void Live()
        {
            CaptureTriggers();
            bool playing = _inst.IsPlaying;
            if (_play != null) _play.text = playing ? "■" : "▶";
            if (playing)
            {
                _s.Playhead = _inst.TimelinePosition;
                // auto-scroll si el playhead se va de la vista
                float w = _canvasHost.contentRect.width;
                if (w > 0 && _s.TimeToX(_s.Playhead) > w - 20) { _s.Offset = _s.Playhead - 20 / _s.PixelsPerSecond; _s.NotifyChanged(); }
                _canvas.RepaintPlayhead(); _ruler.MarkDirtyRepaint();
                _time.text = _s.Playhead.ToString("0.00") + " s";
            }
            else if (_s.Playing)
            {
                _s.Playing = false;
                if (_inst.IsValid && !_inst.IsPlaying) { _inst.Release(); _inst = default; }
                if (_loopPreview && !_userStopped && HasEvent) StartPreview();
            }
            // medidor (master)
            var e = SoddPreview.IsRunning || Application.isPlaying ? SoddPreview.Engine : null;
            float m = e?.Buses.Master?.Meter ?? 0f;
            float db = SoddModulation.LinearToDb(m);
            float h = Mathf.Clamp01((db + 60f) / 66f) * 100f;
            _meterL.style.height = Length.Percent(h);
            _meterR.style.height = Length.Percent(h * 0.96f);
            var col = db > -1f ? new Color32(0xff, 0x6b, 0x6b, 0xff) : db > -9f ? new Color32(0xff, 0xd1, 0x66, 0xff) : new Color32(0x5b, 0xe0, 0xa0, 0xff);
            _meterL.style.backgroundColor = (Color)col; _meterR.style.backgroundColor = (Color)col;
            _meterLabel.text = m <= 1e-4f ? "-∞" : db.ToString("0.0") + " dB";
        }

        void UpdateBridge()
        {
            if (_bridge == null) return;
            bool on = SoddStudioHooks.BridgeRunning?.Invoke() ?? false;
            _bridge.text = on ? "IA: ● on" : "IA: off";
            _bridge.EnableInClassList("sd-status--on", on);
            _bridge.tooltip = on ? "Bridge para IA (MCP) activo en 127.0.0.1. Click para apagar." : "Bridge para IA (MCP) apagado. Click para encender.";
        }

        // ================================================================== atajos
        bool IsTyping()
        {
            var f = rootVisualElement.focusController?.focusedElement as VisualElement;
            for (var v = f; v != null; v = v.parent)
                if (v is TextField || v is FloatField || v is IntegerField) return true;
            return false;
        }

        // Window shortcuts, rebindable in Edit > Shortcuts > SoddDesign Studio.
        const string Sc = "SoddDesign Studio/";

        static void Run(ShortcutArguments a, Action<SoddStudioWindow> action, bool needsEvent = true)
        {
            if (!(a.context is SoddStudioWindow w) || w.IsTyping() || (needsEvent && !w.HasEvent)) return;
            action(w);
        }

        static void OnRegion(ShortcutArguments a, Action<SoddStudioWindow, int, int> action) =>
            Run(a, w => { if (w._s.SelTrack >= 0 && w._s.SelRegion >= 0) action(w, w._s.SelTrack, w._s.SelRegion); });

        [Shortcut(Sc + "Play - Stop", typeof(SoddStudioWindow), KeyCode.Space)]
        static void ScPlay(ShortcutArguments a) => Run(a, w => w.TogglePlay());

        [Shortcut(Sc + "Stop y volver al inicio", typeof(SoddStudioWindow), KeyCode.Escape)]
        static void ScStop(ShortcutArguments a) => Run(a, w => { w.StopPreview(true); w.Rewind(); });

        [Shortcut(Sc + "Loop de la preview", typeof(SoddStudioWindow), KeyCode.L)]
        static void ScLoop(ShortcutArguments a) => Run(a, w => w.ToggleLoopPreview(), false);

        [Shortcut(Sc + "Volver al inicio", typeof(SoddStudioWindow), KeyCode.Home)]
        static void ScHome(ShortcutArguments a) => Run(a, w => w.Rewind());

        [Shortcut(Sc + "Deshacer", typeof(SoddStudioWindow), KeyCode.Z, ShortcutModifiers.Action)]
        static void ScUndo(ShortcutArguments a) => Run(a, _ => Undo.PerformUndo(), false);

        [Shortcut(Sc + "Rehacer", typeof(SoddStudioWindow), KeyCode.Y, ShortcutModifiers.Action)]
        static void ScRedo(ShortcutArguments a) => Run(a, _ => Undo.PerformRedo(), false);

        [Shortcut(Sc + "Rehacer (Shift)", typeof(SoddStudioWindow), KeyCode.Z, ShortcutModifiers.Action | ShortcutModifiers.Shift)]
        static void ScRedoShift(ShortcutArguments a) => Run(a, _ => Undo.PerformRedo(), false);

        [Shortcut(Sc + "Encuadrar", typeof(SoddStudioWindow), KeyCode.F)]
        static void ScFit(ShortcutArguments a) => Run(a, w => w.ZoomToFit());

        [Shortcut(Sc + "Dividir region", typeof(SoddStudioWindow), KeyCode.S)]
        static void ScSplit(ShortcutArguments a) => OnRegion(a, (w, t, r) => w.Report(SoddCommands.SplitRegion(w._selectedId, t, r, w._s.Playhead)));

        [Shortcut(Sc + "Duplicar region", typeof(SoddStudioWindow), KeyCode.D, ShortcutModifiers.Action)]
        static void ScDuplicate(ShortcutArguments a) => OnRegion(a, (w, t, r) => w.Report(SoddCommands.DuplicateRegion(w._selectedId, t, r)));

        [Shortcut(Sc + "Duplicar region (D)", typeof(SoddStudioWindow), KeyCode.D)]
        static void ScDuplicatePlain(ShortcutArguments a) => ScDuplicate(a);

        [Shortcut(Sc + "Borrar", typeof(SoddStudioWindow), KeyCode.Delete)]
        static void ScDelete(ShortcutArguments a) => Run(a, w => w.DeleteSelection());

        [Shortcut(Sc + "Borrar (Backspace)", typeof(SoddStudioWindow), KeyCode.Backspace)]
        static void ScBackspace(ShortcutArguments a) => Run(a, w => w.DeleteSelection());

        void Rewind() { _s.Playhead = 0; _s.Offset = 0; _s.NotifyChanged(); }

        void DeleteSelection()
        {
            int t = _s.SelTrack, r = _s.SelRegion;
            if (_s.SelMarker >= 0) { Report(SoddCommands.RemoveMarker(_selectedId, _s.SelMarker)); _s.SelMarker = -1; }
            else if (t >= 0 && r >= 0) { Report(SoddCommands.RemoveRegion(_selectedId, t, r)); _s.SelRegion = -1; }
            else if (t >= 0 && EditorUtility.DisplayDialog("Borrar track", "¿Borrar el track seleccionado?", "Borrar", "Cancelar"))
                Report(SoddCommands.RemoveTrack(_selectedId, t));
        }
    }
}
