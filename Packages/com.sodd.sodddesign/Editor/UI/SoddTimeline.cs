using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Sodd.Design.Editor
{
    /// <summary>Estado compartido del timeline (zoom, scroll, selección, playhead).</summary>
    public sealed class SoddTimelineState
    {
        public const float TrackHeight = 64f;
        public float PixelsPerSecond = 120f;
        public float Offset; // segundos visibles a la izquierda
        public float Snap = 0.05f;
        public int SelTrack = -1, SelRegion = -1, SelMarker = -1;
        /// <summary>Marker que se está arrastrando (-1 = ninguno) y su tiempo provisorio.</summary>
        public int DragMarker = -1;
        public float DragMarkerTime;
        public float Playhead;
        public bool Playing;
        public SoddEventAsset Asset;
        public string EventId;
        public event Action Changed;
        public event Action SelectionChanged;

        public float TimeToX(float t) => (t - Offset) * PixelsPerSecond;
        public float XToTime(float x) => x / PixelsPerSecond + Offset;
        public float SnapTime(float t, bool bypass = false) => bypass || Snap <= 0f ? Mathf.Max(0f, t) : Mathf.Max(0f, Mathf.Round(t / Snap) * Snap);
        public void NotifyChanged() => Changed?.Invoke();
        public void Select(int track, int region) { SelTrack = track; SelRegion = region; SelMarker = -1; SelectionChanged?.Invoke(); Changed?.Invoke(); }
        public void SelectMarker(int marker) { SelTrack = -1; SelRegion = -1; SelMarker = marker; SelectionChanged?.Invoke(); Changed?.Invoke(); }
        public float MarkerTime(int i) => i == DragMarker ? DragMarkerTime : Asset.data.markers[i].time;

        public static Color TrackColor(SoddTrackType t)
        {
            switch (t)
            {
                case SoddTrackType.Music: return new Color32(0xb4, 0x8c, 0xff, 0xff);
                case SoddTrackType.Voice: return new Color32(0x5b, 0xe0, 0xa0, 0xff);
                case SoddTrackType.Ambience: return new Color32(0x5a, 0xb8, 0xff, 0xff);
                default: return new Color32(0xff, 0x8a, 0x5b, 0xff);
            }
        }
    }

    /// <summary>Cache de waveforms: una textura por clip (picos min/max), generada una vez.</summary>
    public static class SoddWaveformCache
    {
        const int Width = 1024, Height = 64;
        static readonly Dictionary<int, Texture2D> s_cache = new Dictionary<int, Texture2D>();

        public static Texture2D Get(AudioClip clip)
        {
            if (clip == null) return null;
            int id = clip.GetInstanceID();
            if (s_cache.TryGetValue(id, out var t) && t != null) return t;
            t = Build(clip);
            s_cache[id] = t;
            return t;
        }

        static Texture2D Build(AudioClip clip)
        {
            var tex = new Texture2D(Width, Height, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave, wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Bilinear };
            var px = new Color32[Width * Height];
            try
            {
                if (clip.loadState != AudioDataLoadState.Loaded) clip.LoadAudioData();
                int ch = Mathf.Max(1, clip.channels);
                var data = new float[clip.samples * ch];
                if (data.Length > 0 && clip.GetData(data, 0))
                {
                    int frames = clip.samples;
                    for (int x = 0; x < Width; x++)
                    {
                        int a = (int)((long)frames * x / Width), b = Mathf.Max(a + 1, (int)((long)frames * (x + 1) / Width));
                        float mn = 0, mx = 0;
                        for (int f = a; f < b && f < frames; f++)
                        {
                            float v = data[f * ch];
                            if (v < mn) mn = v; if (v > mx) mx = v;
                        }
                        int y0 = Mathf.Clamp((int)((mn * 0.5f + 0.5f) * (Height - 1)), 0, Height - 1);
                        int y1 = Mathf.Clamp((int)((mx * 0.5f + 0.5f) * (Height - 1)), 0, Height - 1);
                        for (int y = y0; y <= y1; y++) px[y * Width + x] = new Color32(255, 255, 255, 255);
                        px[(Height / 2) * Width + x] = new Color32(255, 255, 255, 160);
                    }
                }
            }
            catch (Exception) { /* clips streaming/comprimidos sin datos: textura vacía */ }
            tex.SetPixels32(px);
            tex.Apply(false, true);
            return tex;
        }
    }

    /// <summary>
    /// Regla de tiempo: ticks, loop regions, markers y playhead. Click o arrastre mueve el playhead;
    /// sobre un marker: click lo selecciona, arrastrar lo mueve (Shift = sin snap), doble click lo renombra.
    /// </summary>
    public sealed class SoddRuler : VisualElement
    {
        const float MarkerHitPx = 7f;

        readonly SoddTimelineState _s;
        readonly List<Label> _labels = new List<Label>();
        public Action<float, Vector2> ContextRequested;
        public Action<int, float> MarkerMoved;
        public Action<int> MarkerRenameRequested;
        public Action<int, Vector2> MarkerContextRequested;

        bool _scrubbing;
        float _downX;

        public SoddRuler(SoddTimelineState s)
        {
            _s = s;
            style.flexGrow = 1;
            generateVisualContent += Draw;
            RegisterCallback<PointerDownEvent>(OnDown);
            RegisterCallback<PointerMoveEvent>(OnMove);
            RegisterCallback<PointerUpEvent>(OnUp);
        }

        /// <summary>Índice del marker más cercano a x (en px locales) dentro del área de click, o -1.</summary>
        public int MarkerAt(float x)
        {
            var a = _s.Asset;
            if (a == null) return -1;
            int best = -1; float bestD = MarkerHitPx;
            for (int i = 0; i < a.data.markers.Length; i++)
            {
                float d = Mathf.Abs(_s.TimeToX(a.data.markers[i].time) - x);
                if (d <= bestD) { bestD = d; best = i; }
            }
            return best;
        }

        void OnDown(PointerDownEvent e)
        {
            float x = e.localPosition.x;
            int m = MarkerAt(x);
            if (e.button == 1)
            {
                if (m >= 0) { _s.SelectMarker(m); MarkerContextRequested?.Invoke(m, e.position); }
                else ContextRequested?.Invoke(_s.XToTime(x), e.position);
                e.StopPropagation();
                return;
            }
            if (e.button != 0) return;
            if (m >= 0)
            {
                if (e.clickCount == 2) { MarkerRenameRequested?.Invoke(m); e.StopPropagation(); return; }
                _s.SelectMarker(m);
                _s.DragMarker = m; _s.DragMarkerTime = _s.Asset.data.markers[m].time;
                _downX = x;
            }
            else
            {
                _scrubbing = true;
                _s.Playhead = _s.SnapTime(_s.XToTime(x)); _s.NotifyChanged();
            }
            this.CapturePointer(e.pointerId);
            e.StopPropagation();
        }

        void OnMove(PointerMoveEvent e)
        {
            if (!this.HasPointerCapture(e.pointerId)) return;
            float x = e.localPosition.x;
            if (_s.DragMarker >= 0)
            {
                if (Mathf.Abs(x - _downX) < 2f) return;
                _s.DragMarkerTime = _s.SnapTime(_s.XToTime(x), e.shiftKey);
                _s.NotifyChanged();
            }
            else if (_scrubbing)
            {
                _s.Playhead = _s.SnapTime(_s.XToTime(x), e.shiftKey); _s.NotifyChanged();
            }
        }

        void OnUp(PointerUpEvent e)
        {
            if (!this.HasPointerCapture(e.pointerId)) return;
            this.ReleasePointer(e.pointerId);
            _scrubbing = false;
            int m = _s.DragMarker;
            if (m < 0) return;
            float t = _s.DragMarkerTime;
            _s.DragMarker = -1;
            if (_s.Asset != null && m < _s.Asset.data.markers.Length && Mathf.Abs(t - _s.Asset.data.markers[m].time) > 1e-4f) MarkerMoved?.Invoke(m, t);
            else _s.NotifyChanged();
        }

        Label L(int i, string cls)
        {
            while (_labels.Count <= i) { var l = new Label { pickingMode = PickingMode.Ignore }; _labels.Add(l); Add(l); }
            var lab = _labels[i];
            lab.ClearClassList(); lab.AddToClassList(cls); lab.style.display = DisplayStyle.Flex;
            return lab;
        }

        public void Refresh()
        {
            int n = 0;
            float w = contentRect.width;
            float step = TickStep(_s.PixelsPerSecond);
            float t0 = Mathf.Floor(_s.Offset / step) * step;
            for (float t = t0; _s.TimeToX(t) < w && n < 200; t += step)
            {
                if (t < 0) continue;
                var l = L(n++, "sd-ruler-label");
                l.text = t.ToString(step < 1 ? "0.0#" : "0", CultureInfo.InvariantCulture);
                l.style.left = _s.TimeToX(t) + 3;
            }
            var a = _s.Asset;
            if (a != null)
                for (int i = 0; i < a.data.markers.Length; i++)
                {
                    var l = L(n++, "sd-marker-label");
                    l.EnableInClassList("sd-marker-label--sel", i == _s.SelMarker);
                    l.text = a.data.markers[i].name;
                    l.style.left = _s.TimeToX(_s.MarkerTime(i)) + 6;
                }
            for (int i = n; i < _labels.Count; i++) _labels[i].style.display = DisplayStyle.None;
            MarkDirtyRepaint();
        }

        public static float TickStep(float pps)
        {
            float[] steps = { 0.01f, 0.025f, 0.05f, 0.1f, 0.25f, 0.5f, 1, 2, 5, 10, 30, 60 };
            foreach (var s in steps) if (s * pps >= 60f) return s;
            return 60f;
        }

        void Draw(MeshGenerationContext mgc)
        {
            var p = mgc.painter2D;
            var r = contentRect;
            float step = TickStep(_s.PixelsPerSecond);
            p.strokeColor = new Color(1, 1, 1, 0.15f);
            p.lineWidth = 1;
            float sub = step / 4f;
            for (float t = Mathf.Floor(_s.Offset / sub) * sub; _s.TimeToX(t) < r.width; t += sub)
            {
                if (t < 0) continue;
                float x = _s.TimeToX(t);
                bool major = Mathf.Abs(t / step - Mathf.Round(t / step)) < 1e-3f;
                p.BeginPath(); p.MoveTo(new Vector2(x, major ? 0 : r.height - 6)); p.LineTo(new Vector2(x, r.height)); p.Stroke();
            }
            var a = _s.Asset;
            if (a != null)
            {
                foreach (var l in a.data.loopRegions)
                {
                    p.fillColor = new Color(0.35f, 0.78f, 0.98f, l.HasExitCondition ? 0.35f : 0.55f);
                    float x0 = _s.TimeToX(l.start), x1 = _s.TimeToX(l.end);
                    p.BeginPath(); p.MoveTo(new Vector2(x0, 0)); p.LineTo(new Vector2(x1, 0)); p.LineTo(new Vector2(x1, 5)); p.LineTo(new Vector2(x0, 5)); p.ClosePath(); p.Fill();
                }
                for (int i = 0; i < a.data.markers.Length; i++)
                {
                    bool sel = i == _s.SelMarker;
                    float x = _s.TimeToX(_s.MarkerTime(i));
                    p.strokeColor = sel ? Color.white : new Color(1f, 0.82f, 0.4f, 0.9f);
                    p.lineWidth = sel ? 2 : 1;
                    p.BeginPath(); p.MoveTo(new Vector2(x, 16)); p.LineTo(new Vector2(x, r.height)); p.Stroke();
                    p.fillColor = sel ? Color.white : new Color(1f, 0.82f, 0.4f, 1f);
                    p.BeginPath(); p.MoveTo(new Vector2(x, 16)); p.LineTo(new Vector2(x + 5, 20)); p.LineTo(new Vector2(x, 24)); p.ClosePath(); p.Fill();
                }
            }
            DrawPlayhead(p, _s, r.height);
        }

        internal static void DrawPlayhead(Painter2D p, SoddTimelineState s, float h)
        {
            float x = s.TimeToX(s.Playhead);
            p.strokeColor = new Color(0.35f, 0.78f, 0.98f, 1f);
            p.lineWidth = 2;
            p.BeginPath(); p.MoveTo(new Vector2(x, 0)); p.LineTo(new Vector2(x, h)); p.Stroke();
        }
    }

    /// <summary>
    /// Lienzo del timeline: dibuja lanes y regiones con Painter2D (no un VisualElement por región),
    /// waveforms como quads texturizados (capa hija) y un overlay con fades, handles y playhead.
    /// Interacción: mover (también entre tracks), redimensionar bordes, fades (handles arriba), zoom, pan, drop de clips.
    /// </summary>
    public sealed class SoddTimelineCanvas : VisualElement
    {
        enum Drag { None, Move, ResizeL, ResizeR, FadeIn, FadeOut, Pan }

        readonly SoddTimelineState _s;
        readonly VisualElement _waveLayer, _overlay, _labelLayer;
        readonly List<Label> _labels = new List<Label>();
        Drag _drag;
        Vector2 _downPos;
        float _origStart, _origLen, _origFade, _origOffset;
        int _dragTrack = -1, _dragRegion = -1, _hoverTrack = -1;
        // valores temporales durante el drag (se commitean al soltar = un solo paso de undo)
        float _tStart, _tLen, _tFadeIn, _tFadeOut;
        int _tTrack;

        public Action<int, int, float, float, int> CommitMove; // track, region, start, length, newTrack
        public Action<int, int, string, float> CommitProperty;   // track, region, prop, value
        public Action<int, float, AudioClip[]> ClipsDropped;      // track (-1 = nuevo), time, clips
        public Action<int, int, float, Vector2> ContextRequested; // track, region, time, screen pos

        public SoddTimelineCanvas(SoddTimelineState s)
        {
            _s = s;
            focusable = true;
            style.flexGrow = 1;
            generateVisualContent += DrawBase;
            _waveLayer = new VisualElement { pickingMode = PickingMode.Ignore };
            _waveLayer.AddToClassList("sd-layer");
            _waveLayer.generateVisualContent += DrawWaves;
            _labelLayer = new VisualElement { pickingMode = PickingMode.Ignore };
            _labelLayer.AddToClassList("sd-layer");
            _overlay = new VisualElement { pickingMode = PickingMode.Ignore };
            _overlay.AddToClassList("sd-layer");
            _overlay.generateVisualContent += DrawOverlay;
            Add(_waveLayer); Add(_labelLayer); Add(_overlay);

            RegisterCallback<PointerDownEvent>(OnDown);
            RegisterCallback<PointerMoveEvent>(OnMove);
            RegisterCallback<PointerUpEvent>(OnUp);
            RegisterCallback<WheelEvent>(OnWheel);
            RegisterCallback<DragUpdatedEvent>(OnDragUpdated);
            RegisterCallback<DragPerformEvent>(OnDragPerform);
            RegisterCallback<DragLeaveEvent>(_ => { _hoverTrack = -1; RepaintAll(); });
        }

        SoddEventData Data => _s.Asset != null ? _s.Asset.data : null;

        public void RepaintAll()
        {
            var d = Data;
            int tracks = d != null ? d.tracks.Length : 0;
            style.height = Mathf.Max(1, tracks + 1) * SoddTimelineState.TrackHeight;
            MarkDirtyRepaint(); _waveLayer.MarkDirtyRepaint(); _overlay.MarkDirtyRepaint();
            UpdateLabels();
        }

        public void RepaintPlayhead() => _overlay.MarkDirtyRepaint();

        // ------------------------------------------------------------------ geometría
        bool RegionValues(int t, int r, out float start, out float len, out float fi, out float fo, out int track)
        {
            var reg = Data.tracks[t].regions[r];
            start = reg.start; len = reg.length; fi = reg.fadeIn; fo = reg.fadeOut; track = t;
            if (_drag != Drag.None && _drag != Drag.Pan && t == _dragTrack && r == _dragRegion)
            {
                start = _tStart; len = _tLen; fi = _tFadeIn; fo = _tFadeOut; track = _tTrack;
                return true;
            }
            return false;
        }

        Rect RegionRect(float start, float len, int track) =>
            new Rect(_s.TimeToX(start), track * SoddTimelineState.TrackHeight + 6, len * _s.PixelsPerSecond, SoddTimelineState.TrackHeight - 12);

        bool HitRegion(Vector2 pos, out int track, out int region)
        {
            track = region = -1;
            var d = Data;
            if (d == null) return false;
            int t = (int)(pos.y / SoddTimelineState.TrackHeight);
            if (t < 0 || t >= d.tracks.Length) return false;
            var regs = d.tracks[t].regions;
            for (int r = regs.Length - 1; r >= 0; r--)
            {
                var rect = RegionRect(regs[r].start, regs[r].length, t);
                if (rect.Contains(pos)) { track = t; region = r; return true; }
            }
            track = t;
            return false;
        }

        // ------------------------------------------------------------------ dibujo
        void DrawBase(MeshGenerationContext mgc)
        {
            var p = mgc.painter2D;
            var r = contentRect;
            var d = Data;
            float th = SoddTimelineState.TrackHeight;
            int tracks = d != null ? d.tracks.Length : 0;
            // lanes
            for (int t = 0; t <= tracks; t++)
            {
                p.fillColor = t == _hoverTrack ? new Color(0.35f, 0.78f, 0.98f, 0.08f) : (t % 2 == 0 ? new Color(1, 1, 1, 0.015f) : new Color(0, 0, 0, 0));
                FillRect(p, new Rect(0, t * th, r.width, th));
                p.strokeColor = new Color(1, 1, 1, 0.06f); p.lineWidth = 1;
                p.BeginPath(); p.MoveTo(new Vector2(0, (t + 1) * th)); p.LineTo(new Vector2(r.width, (t + 1) * th)); p.Stroke();
            }
            // grilla
            float step = SoddRuler.TickStep(_s.PixelsPerSecond);
            p.strokeColor = new Color(1, 1, 1, 0.05f);
            for (float t = Mathf.Max(0, Mathf.Floor(_s.Offset / step) * step); _s.TimeToX(t) < r.width; t += step)
            {
                float x = _s.TimeToX(t);
                p.BeginPath(); p.MoveTo(new Vector2(x, 0)); p.LineTo(new Vector2(x, tracks * th)); p.Stroke();
            }
            if (d == null) return;
            // loops (banda tenue en todo el alto)
            foreach (var l in d.loopRegions)
            {
                p.fillColor = new Color(0.35f, 0.78f, 0.98f, 0.04f);
                FillRect(p, new Rect(_s.TimeToX(l.start), 0, (l.end - l.start) * _s.PixelsPerSecond, tracks * th));
            }
            // regiones
            for (int t = 0; t < tracks; t++)
            {
                var tr = d.tracks[t];
                var col = SoddTimelineState.TrackColor(tr.type);
                bool muted = tr.mute;
                for (int g = 0; g < tr.regions.Length; g++)
                {
                    RegionValues(t, g, out float st, out float len, out _, out _, out int vt);
                    var rect = RegionRect(st, len, vt);
                    if (rect.xMax < 0 || rect.xMin > r.width) continue;
                    var c = col; c.a = muted ? 0.35f : (tr.regions[g].instrument.type == SoddInstrumentType.Silence ? 0.4f : 0.92f);
                    p.fillColor = c;
                    RoundRect(p, rect, 5); p.Fill();
                }
            }
        }

        void DrawWaves(MeshGenerationContext mgc)
        {
            var d = Data;
            if (d == null) return;
            var a = _s.Asset;
            var tint = new Color32(13, 15, 19, 150);
            for (int t = 0; t < d.tracks.Length; t++)
            {
                var regs = d.tracks[t].regions;
                for (int g = 0; g < regs.Length; g++)
                {
                    var ins = regs[g].instrument;
                    if (ins.clips.Length == 0) continue;
                    int ci = ins.clips[0].clipIndex;
                    if (ci < 0 || ci >= a.clips.Length || a.clips[ci] == null) continue;
                    var clip = a.clips[ci];
                    var tex = SoddWaveformCache.Get(clip);
                    if (tex == null) continue;
                    RegionValues(t, g, out float st, out float len, out _, out _, out int vt);
                    var rect = RegionRect(st, len, vt);
                    rect.yMin += 14;
                    float clipLen = Mathf.Max(0.001f, clip.length);
                    float off = ins.startOffset.@base;
                    float u0 = off / clipLen;
                    float u1 = ins.loop ? (off + len) / clipLen : Mathf.Min(1f, (off + len) / clipLen);
                    float drawW = ins.loop ? rect.width : Mathf.Min(rect.width, (clipLen - off) * _s.PixelsPerSecond);
                    if (drawW <= 1) continue;
                    var md = mgc.Allocate(4, 6, tex);
                    float x0 = rect.x, x1 = rect.x + drawW, y0 = rect.y, y1 = rect.yMax;
                    float uEnd = ins.loop ? u1 : Mathf.Min(1f, u0 + drawW / _s.PixelsPerSecond / clipLen);
                    md.SetAllVertices(new[]
                    {
                        new Vertex { position = new Vector3(x0, y0, Vertex.nearZ), tint = tint, uv = new Vector2(u0, 1) },
                        new Vertex { position = new Vector3(x1, y0, Vertex.nearZ), tint = tint, uv = new Vector2(uEnd, 1) },
                        new Vertex { position = new Vector3(x1, y1, Vertex.nearZ), tint = tint, uv = new Vector2(uEnd, 0) },
                        new Vertex { position = new Vector3(x0, y1, Vertex.nearZ), tint = tint, uv = new Vector2(u0, 0) },
                    });
                    md.SetAllIndices(new ushort[] { 0, 1, 2, 2, 3, 0 });
                }
            }
        }

        void DrawOverlay(MeshGenerationContext mgc)
        {
            var p = mgc.painter2D;
            var d = Data;
            float th = SoddTimelineState.TrackHeight;
            if (d != null)
            {
                for (int t = 0; t < d.tracks.Length; t++)
                {
                    var regs = d.tracks[t].regions;
                    for (int g = 0; g < regs.Length; g++)
                    {
                        RegionValues(t, g, out float st, out float len, out float fi, out float fo, out int vt);
                        var rect = RegionRect(st, len, vt);
                        p.fillColor = new Color(0, 0, 0, 0.35f);
                        if (fi > 0)
                        {
                            float fx = rect.x + fi * _s.PixelsPerSecond;
                            p.BeginPath(); p.MoveTo(new Vector2(rect.x, rect.y)); p.LineTo(new Vector2(fx, rect.y)); p.LineTo(new Vector2(rect.x, rect.yMax)); p.ClosePath(); p.Fill();
                        }
                        if (fo > 0)
                        {
                            float fx = rect.xMax - fo * _s.PixelsPerSecond;
                            p.BeginPath(); p.MoveTo(new Vector2(fx, rect.y)); p.LineTo(new Vector2(rect.xMax, rect.y)); p.LineTo(new Vector2(rect.xMax, rect.yMax)); p.ClosePath(); p.Fill();
                        }
                        bool sel = t == _s.SelTrack && g == _s.SelRegion;
                        // handles de fade (círculos arriba)
                        p.fillColor = sel ? Color.white : new Color(1, 1, 1, 0.55f);
                        Circle(p, new Vector2(rect.x + fi * _s.PixelsPerSecond, rect.y + 1), sel ? 4 : 3);
                        Circle(p, new Vector2(rect.xMax - fo * _s.PixelsPerSecond, rect.y + 1), sel ? 4 : 3);
                        if (sel)
                        {
                            p.strokeColor = Color.white; p.lineWidth = 2;
                            RoundRect(p, rect, 5); p.Stroke();
                        }
                    }
                }
                // markers (líneas punteadas)
                for (int i = 0; i < d.markers.Length; i++)
                {
                    p.strokeColor = i == _s.SelMarker ? new Color(1f, 1f, 1f, 0.7f) : new Color(1f, 0.82f, 0.4f, 0.5f); p.lineWidth = 1;
                    float x = _s.TimeToX(_s.MarkerTime(i));
                    for (float y = 0; y < d.tracks.Length * th; y += 8) { p.BeginPath(); p.MoveTo(new Vector2(x, y)); p.LineTo(new Vector2(x, y + 4)); p.Stroke(); }
                }
            }
            SoddRuler.DrawPlayhead(p, _s, contentRect.height);
        }

        static void FillRect(Painter2D p, Rect r)
        {
            p.BeginPath(); p.MoveTo(r.min); p.LineTo(new Vector2(r.xMax, r.y)); p.LineTo(r.max); p.LineTo(new Vector2(r.x, r.yMax)); p.ClosePath(); p.Fill();
        }

        static void RoundRect(Painter2D p, Rect r, float rad)
        {
            rad = Mathf.Min(rad, r.width * 0.5f, r.height * 0.5f);
            p.BeginPath();
            p.MoveTo(new Vector2(r.x + rad, r.y));
            p.ArcTo(new Vector2(r.xMax, r.y), new Vector2(r.xMax, r.yMax), rad);
            p.ArcTo(new Vector2(r.xMax, r.yMax), new Vector2(r.x, r.yMax), rad);
            p.ArcTo(new Vector2(r.x, r.yMax), new Vector2(r.x, r.y), rad);
            p.ArcTo(new Vector2(r.x, r.y), new Vector2(r.xMax, r.y), rad);
            p.ClosePath();
        }

        static void Circle(Painter2D p, Vector2 c, float rad)
        {
            p.BeginPath(); p.Arc(c, rad, Angle.Degrees(0), Angle.Degrees(360)); p.ClosePath(); p.Fill();
        }

        void UpdateLabels()
        {
            int n = 0;
            var d = Data;
            if (d != null)
                for (int t = 0; t < d.tracks.Length; t++)
                    for (int g = 0; g < d.tracks[t].regions.Length; g++)
                    {
                        var reg = d.tracks[t].regions[g];
                        RegionValues(t, g, out float st, out float len, out _, out _, out int vt);
                        var rect = RegionRect(st, len, vt);
                        if (rect.width < 24) continue;
                        while (_labels.Count <= n) { var l = new Label { pickingMode = PickingMode.Ignore }; l.AddToClassList("sd-region-label"); _labels.Add(l); _labelLayer.Add(l); }
                        var lab = _labels[n++];
                        lab.style.display = DisplayStyle.Flex;
                        lab.style.left = rect.x + 6; lab.style.top = rect.y + 1; lab.style.width = rect.width - 10;
                        lab.text = RegionLabel(reg);
                    }
            for (int i = n; i < _labels.Count; i++) _labels[i].style.display = DisplayStyle.None;
        }

        static string RegionLabel(SoddRegion r)
        {
            var ins = r.instrument;
            string what;
            switch (ins.type)
            {
                case SoddInstrumentType.Silence: what = "silencio"; break;
                case SoddInstrumentType.NestedEvent: what = "↳ " + ins.nestedEvent; break;
                case SoddInstrumentType.RandomClip: what = $"🎲 Random ×{ins.clips.Length}"; break;
                case SoddInstrumentType.Sequential: what = $"⇢ Secuencia ×{ins.clips.Length}"; break;
                default: what = ins.clips.Length > 0 ? System.IO.Path.GetFileNameWithoutExtension(ins.clips[0].path ?? "") : "(sin clip)"; break;
            }
            if (ins.loop) what = "⟳ " + what;
            return string.IsNullOrEmpty(r.name) ? what : r.name + " · " + what;
        }

        // ------------------------------------------------------------------ interacción
        void OnDown(PointerDownEvent e)
        {
            Focus();
            _downPos = e.localPosition;
            if (e.button == 2 || (e.button == 0 && e.altKey))
            {
                _drag = Drag.Pan; _origOffset = _s.Offset;
                this.CapturePointer(e.pointerId);
                return;
            }
            bool hit = HitRegion(e.localPosition, out int t, out int g);
            if (e.button == 1) { ContextRequested?.Invoke(t, hit ? g : -1, _s.XToTime(e.localPosition.x), e.position); return; }
            if (e.button != 0) return;
            if (!hit) { _s.Select(t < Data?.tracks.Length ? t : -1, -1); return; }
            _s.Select(t, g);
            var reg = Data.tracks[t].regions[g];
            var rect = RegionRect(reg.start, reg.length, t);
            var lp = (Vector2)e.localPosition;
            float fiX = rect.x + reg.fadeIn * _s.PixelsPerSecond, foX = rect.xMax - reg.fadeOut * _s.PixelsPerSecond;
            if (lp.y < rect.y + 10 && Mathf.Abs(lp.x - fiX) < 7) _drag = Drag.FadeIn;
            else if (lp.y < rect.y + 10 && Mathf.Abs(lp.x - foX) < 7) _drag = Drag.FadeOut;
            else if (lp.x < rect.x + 6) _drag = Drag.ResizeL;
            else if (lp.x > rect.xMax - 6) _drag = Drag.ResizeR;
            else _drag = Drag.Move;
            _dragTrack = t; _dragRegion = g;
            _origStart = reg.start; _origLen = reg.length; _origFade = _drag == Drag.FadeIn ? reg.fadeIn : reg.fadeOut;
            _tStart = reg.start; _tLen = reg.length; _tFadeIn = reg.fadeIn; _tFadeOut = reg.fadeOut; _tTrack = t;
            this.CapturePointer(e.pointerId);
        }

        void OnMove(PointerMoveEvent e)
        {
            if (_drag == Drag.None)
            {
                // cursor según zona
                return;
            }
            float dx = e.localPosition.x - _downPos.x;
            float dt = dx / _s.PixelsPerSecond;
            bool noSnap = e.shiftKey;
            switch (_drag)
            {
                case Drag.Pan:
                    _s.Offset = Mathf.Max(0f, _origOffset - dt);
                    _s.NotifyChanged();
                    return;
                case Drag.Move:
                    _tStart = _s.SnapTime(_origStart + dt, noSnap);
                    _tTrack = Mathf.Clamp((int)(e.localPosition.y / SoddTimelineState.TrackHeight), 0, Data.tracks.Length - 1);
                    break;
                case Drag.ResizeL:
                    {
                        float end = _origStart + _origLen;
                        _tStart = Mathf.Min(_s.SnapTime(_origStart + dt, noSnap), end - 0.01f);
                        _tLen = end - _tStart;
                        break;
                    }
                case Drag.ResizeR:
                    _tLen = Mathf.Max(0.01f, _s.SnapTime(_origStart + _origLen + dt, noSnap) - _origStart);
                    break;
                case Drag.FadeIn:
                    _tFadeIn = Mathf.Clamp(_origFade + dt, 0f, _origLen - _tFadeOut);
                    break;
                case Drag.FadeOut:
                    _tFadeOut = Mathf.Clamp(_origFade - dt, 0f, _origLen - _tFadeIn);
                    break;
            }
            RepaintAll();
        }

        void OnUp(PointerUpEvent e)
        {
            if (!this.HasPointerCapture(e.pointerId)) return;
            this.ReleasePointer(e.pointerId);
            var drag = _drag;
            _drag = Drag.None;
            if (drag == Drag.Pan || drag == Drag.None) return;
            bool moved = Mathf.Abs(e.localPosition.x - _downPos.x) > 2 || Mathf.Abs(e.localPosition.y - _downPos.y) > 2;
            if (!moved) { RepaintAll(); return; }
            switch (drag)
            {
                case Drag.Move:
                case Drag.ResizeL:
                case Drag.ResizeR:
                    CommitMove?.Invoke(_dragTrack, _dragRegion, _tStart, _tLen, _tTrack);
                    break;
                case Drag.FadeIn: CommitProperty?.Invoke(_dragTrack, _dragRegion, "fadeIn", _tFadeIn); break;
                case Drag.FadeOut: CommitProperty?.Invoke(_dragTrack, _dragRegion, "fadeOut", _tFadeOut); break;
            }
            RepaintAll();
        }

        void OnWheel(WheelEvent e)
        {
            float mouseT = _s.XToTime(e.localMousePosition.x);
            float factor = e.delta.y > 0 ? 1f / 1.15f : 1.15f;
            _s.PixelsPerSecond = Mathf.Clamp(_s.PixelsPerSecond * factor, 10f, 4000f);
            _s.Offset = Mathf.Max(0f, mouseT - e.localMousePosition.x / _s.PixelsPerSecond);
            _s.NotifyChanged();
            e.StopPropagation();
        }

        static List<AudioClip> DraggedClips()
        {
            var list = new List<AudioClip>();
            foreach (var o in DragAndDrop.objectReferences) if (o is AudioClip c) list.Add(c);
            return list;
        }

        void OnDragUpdated(DragUpdatedEvent e)
        {
            if (_s.Asset == null || DraggedClips().Count == 0) return;
            DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
            int t = (int)(e.localMousePosition.y / SoddTimelineState.TrackHeight);
            if (t != _hoverTrack) { _hoverTrack = t; MarkDirtyRepaint(); }
        }

        void OnDragPerform(DragPerformEvent e)
        {
            var clips = DraggedClips();
            _hoverTrack = -1;
            if (_s.Asset == null || clips.Count == 0) return;
            DragAndDrop.AcceptDrag();
            int t = (int)(e.localMousePosition.y / SoddTimelineState.TrackHeight);
            if (t >= Data.tracks.Length) t = -1;
            ClipsDropped?.Invoke(t, _s.SnapTime(_s.XToTime(e.localMousePosition.x)), clips.ToArray());
        }
    }
}
