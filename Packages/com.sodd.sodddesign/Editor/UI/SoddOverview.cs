using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace Sodd.Design.Editor
{
    /// <summary>
    /// Barra de vista general bajo la regla: todo el evento en miniatura (regiones, loops, markers, playhead) y un
    /// rectángulo con la parte que se ve en el timeline. Arrastrar el rectángulo desplaza la vista; click afuera la centra ahí.
    /// </summary>
    public sealed class SoddOverview : VisualElement
    {
        readonly SoddTimelineState _s;
        readonly Func<float> _visibleWidth;
        bool _dragging;
        float _grabOffset;

        public SoddOverview(SoddTimelineState s, Func<float> visibleWidth)
        {
            _s = s; _visibleWidth = visibleWidth;
            style.flexGrow = 1;
            tooltip = "Vista general del evento: el recuadro es lo que se ve. Arrastralo o hacé click para moverte.";
            generateVisualContent += Draw;
            RegisterCallback<PointerDownEvent>(OnDown);
            RegisterCallback<PointerMoveEvent>(OnMove);
            RegisterCallback<PointerUpEvent>(OnUp);
        }

        /// <summary>Segundos que cubre la barra: el largo del evento o lo visible, lo que sea mayor (mínimo 1 s).</summary>
        public float Span
        {
            get
            {
                float len = _s.Asset != null ? _s.Asset.data.Length : 0f;
                return Mathf.Max(1f, len, _s.Offset + VisibleSeconds);
            }
        }

        float VisibleSeconds => Mathf.Max(0.01f, _visibleWidth() / Mathf.Max(1f, _s.PixelsPerSecond));
        float ToX(float t) => t / Span * contentRect.width;
        float ToT(float x) => x / Mathf.Max(1f, contentRect.width) * Span;

        void Draw(MeshGenerationContext mgc)
        {
            var p = mgc.painter2D;
            var r = contentRect;
            if (r.width <= 0) return;
            p.fillColor = new Color(1f, 1f, 1f, 0.04f);
            Fill(p, new Rect(0, 0, r.width, r.height));
            var a = _s.Asset;
            if (a != null)
            {
                var d = a.data;
                int n = Mathf.Max(1, d.tracks.Length);
                float lane = (r.height - 2f) / n;
                for (int t = 0; t < d.tracks.Length; t++)
                {
                    var c = SoddTimelineState.TrackColor(d.tracks[t].type); c.a = 0.85f;
                    p.fillColor = c;
                    foreach (var reg in d.tracks[t].regions)
                        Fill(p, new Rect(ToX(reg.start), 1f + t * lane, Mathf.Max(1f, ToX(reg.length)), Mathf.Max(1f, lane - 1f)));
                }
                p.fillColor = new Color(0.35f, 0.78f, 0.98f, 0.35f);
                for (int i = 0; i < d.loopRegions.Length; i++)
                    Fill(p, new Rect(ToX(_s.LoopStart(i)), 0, ToX(_s.LoopEnd(i) - _s.LoopStart(i)), 2f));
                p.fillColor = new Color(1f, 0.82f, 0.4f, 1f);
                for (int i = 0; i < d.markers.Length; i++)
                    Fill(p, new Rect(ToX(_s.MarkerTime(i)) - 0.5f, 0, 1f, r.height));
            }
            p.fillColor = new Color(0.35f, 0.78f, 0.98f, 1f);
            Fill(p, new Rect(ToX(_s.Playhead) - 1f, 0, 2f, r.height));

            var view = new Rect(ToX(_s.Offset), 0, Mathf.Max(6f, ToX(VisibleSeconds)), r.height);
            p.fillColor = new Color(1f, 1f, 1f, 0.12f);
            Fill(p, view);
            p.strokeColor = new Color(1f, 1f, 1f, 0.85f); p.lineWidth = 1.5f;
            p.BeginPath(); p.MoveTo(view.min); p.LineTo(new Vector2(view.xMax, view.y)); p.LineTo(view.max); p.LineTo(new Vector2(view.x, view.yMax)); p.ClosePath(); p.Stroke();
        }

        static void Fill(Painter2D p, Rect r)
        {
            p.BeginPath(); p.MoveTo(r.min); p.LineTo(new Vector2(r.xMax, r.y)); p.LineTo(r.max); p.LineTo(new Vector2(r.x, r.yMax)); p.ClosePath(); p.Fill();
        }

        void OnDown(PointerDownEvent e)
        {
            if (e.button != 0) return;
            float t = ToT(e.localPosition.x);
            float vis = VisibleSeconds;
            bool inside = t >= _s.Offset && t <= _s.Offset + vis;
            _grabOffset = inside ? t - _s.Offset : vis * 0.5f;
            ScrollTo(t - _grabOffset);
            _dragging = true;
            this.CapturePointer(e.pointerId);
            e.StopPropagation();
        }

        void OnMove(PointerMoveEvent e)
        {
            if (!_dragging || !this.HasPointerCapture(e.pointerId)) return;
            ScrollTo(ToT(e.localPosition.x) - _grabOffset);
        }

        void OnUp(PointerUpEvent e)
        {
            if (!this.HasPointerCapture(e.pointerId)) return;
            this.ReleasePointer(e.pointerId);
            _dragging = false;
        }

        void ScrollTo(float offset)
        {
            float maxOffset = Mathf.Max(0f, Span - VisibleSeconds);
            _s.Offset = Mathf.Clamp(offset, 0f, maxOffset);
            _s.NotifyChanged();
        }
    }
}
