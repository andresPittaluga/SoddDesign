using System;
using System.Globalization;
using UnityEngine;
using UnityEngine.UIElements;

namespace Sodd.Design.Editor
{
    /// <summary>
    /// Perilla estilo FMOD: arrastrar arriba/abajo cambia el valor (Shift = fino), la rueda lo ajusta de a pasos,
    /// doble click en la perilla vuelve al valor por defecto y doble click en el número permite escribirlo.
    /// El valor se ve en vivo y se guarda una sola vez al soltar (un paso de undo).
    /// </summary>
    public sealed class SoddKnob : VisualElement
    {
        const float DragPixels = 160f, FineFactor = 10f, WheelStep = 0.02f, WheelCommitMs = 400;

        readonly float _min, _max, _default;
        readonly bool _log;
        readonly string _unit;
        readonly VisualElement _dial;
        readonly Label _number;
        readonly FloatField _edit;
        float _value, _startNorm, _startY;
        bool _dragging, _moved;
        IVisualElementScheduledItem _wheelCommit;

        /// <summary>Se llama con el valor final al soltar, al escribirlo, con la rueda o con el doble click.</summary>
        public Action<float> Committed;

        public float Value => _value;

        public SoddKnob(string label, float value, float min, float max, float defaultValue, string unit, string tooltip)
        {
            _min = Mathf.Min(min, max); _max = Mathf.Max(min, max);
            _default = Mathf.Clamp(defaultValue, _min, _max);
            _unit = unit ?? "";
            _log = _unit == "Hz" && _min > 0f && _max / _min > 50f;
            _value = value;
            this.tooltip = $"{tooltip}\nArrastrá arriba/abajo (Shift = fino) · rueda · doble click = {Format(_default)} · doble click en el número = escribir";
            AddToClassList("sd-knob-row");

            var name = new Label(label); name.AddToClassList("sd-knob-label"); Add(name);

            _dial = new VisualElement(); _dial.AddToClassList("sd-knob");
            _dial.generateVisualContent += DrawDial;
            Add(_dial);

            _number = new Label(); _number.AddToClassList("sd-knob-value"); Add(_number);
            _edit = new FloatField { isDelayed = true }; _edit.AddToClassList("sd-knob-edit");
            _edit.style.display = DisplayStyle.None;
            Add(_edit);
            UpdateVisual();

            _dial.RegisterCallback<PointerDownEvent>(OnDown);
            _dial.RegisterCallback<PointerMoveEvent>(OnMove);
            _dial.RegisterCallback<PointerUpEvent>(OnUp);
            name.RegisterCallback<PointerDownEvent>(OnDown);
            name.RegisterCallback<PointerMoveEvent>(OnMove);
            name.RegisterCallback<PointerUpEvent>(OnUp);
            RegisterCallback<WheelEvent>(OnWheel);
            _number.RegisterCallback<PointerDownEvent>(e => { if (e.button == 0 && e.clickCount == 2) BeginEdit(); });
            _edit.RegisterValueChangedCallback(e => { EndEdit(); SetAndCommit(e.newValue); });
            _edit.RegisterCallback<FocusOutEvent>(_ => EndEdit());
        }

        // ------------------------------------------------------------------ mapeo valor <-> 0..1
        float ToNorm(float v) => _log ? Mathf.Clamp01(Mathf.Log(Mathf.Max(v, _min) / _min) / Mathf.Log(_max / _min)) : Mathf.InverseLerp(_min, _max, v);
        float FromNorm(float n) { n = Mathf.Clamp01(n); return _log ? _min * Mathf.Pow(_max / _min, n) : Mathf.Lerp(_min, _max, n); }

        string Format(float v)
        {
            if (_unit == "Hz" && Mathf.Abs(v) >= 1000f) return (v / 1000f).ToString("0.0#", CultureInfo.InvariantCulture) + " kHz";
            float span = _max - _min;
            string fmt = span >= 500f ? "0" : span >= 20f ? "0.#" : span >= 2f ? "0.0#" : "0.00#";
            string s = v.ToString(fmt, CultureInfo.InvariantCulture);
            if ((_unit == "dB" || _unit == "st") && v > 0f) s = "+" + s;
            return _unit.Length > 0 && _unit != "linear" && _unit != "ratio" && !_unit.Contains("..") ? s + " " + _unit : s;
        }

        void UpdateVisual()
        {
            _number.text = Format(_value);
            _dial.MarkDirtyRepaint();
        }

        // ------------------------------------------------------------------ interacción
        void OnDown(PointerDownEvent e)
        {
            if (e.button != 0) return;
            if (e.clickCount == 2) { SetAndCommit(_default); e.StopPropagation(); return; }
            _dragging = true; _moved = false;
            _startNorm = ToNorm(_value); _startY = e.position.y;
            ((VisualElement)e.currentTarget).CapturePointer(e.pointerId);
            e.StopPropagation();
        }

        void OnMove(PointerMoveEvent e)
        {
            var target = (VisualElement)e.currentTarget;
            if (!_dragging || !target.HasPointerCapture(e.pointerId)) return;
            float dy = _startY - e.position.y;
            if (!_moved && Mathf.Abs(dy) < 2f) return;
            _moved = true;
            float n = _startNorm + dy / DragPixels / (e.shiftKey ? FineFactor : 1f);
            _value = FromNorm(n);
            UpdateVisual();
        }

        void OnUp(PointerUpEvent e)
        {
            var target = (VisualElement)e.currentTarget;
            if (!target.HasPointerCapture(e.pointerId)) return;
            target.ReleasePointer(e.pointerId);
            _dragging = false;
            if (_moved) Committed?.Invoke(_value);
        }

        void OnWheel(WheelEvent e)
        {
            float step = WheelStep / (e.shiftKey ? FineFactor : 1f);
            _value = FromNorm(ToNorm(_value) - Mathf.Sign(e.delta.y) * step);
            UpdateVisual();
            _wheelCommit?.Pause();
            _wheelCommit = schedule.Execute(() => Committed?.Invoke(_value)).StartingIn((long)WheelCommitMs);
            e.StopPropagation();
        }

        void SetAndCommit(float v)
        {
            _value = Mathf.Clamp(v, _min, _max);
            UpdateVisual();
            Committed?.Invoke(_value);
        }

        void BeginEdit()
        {
            _edit.SetValueWithoutNotify(_value);
            _number.style.display = DisplayStyle.None;
            _edit.style.display = DisplayStyle.Flex;
            _edit.schedule.Execute(() => _edit.Q(TextInputBaseField<float>.textInputUssName)?.Focus());
        }

        void EndEdit()
        {
            _edit.style.display = DisplayStyle.None;
            _number.style.display = DisplayStyle.Flex;
        }

        // ------------------------------------------------------------------ dibujo
        void DrawDial(MeshGenerationContext mgc)
        {
            var p = mgc.painter2D;
            var r = _dial.contentRect;
            var c = r.center;
            float rad = Mathf.Min(r.width, r.height) * 0.5f - 2f;
            const float a0 = 135f, sweep = 270f;
            float n = ToNorm(_value);
            float zero = _min < 0f && _max > 0f ? ToNorm(0f) : 0f;

            p.lineWidth = 3f; p.lineCap = LineCap.Round;
            p.strokeColor = new Color(1f, 1f, 1f, 0.12f);
            p.BeginPath(); p.Arc(c, rad, Angle.Degrees(a0), Angle.Degrees(a0 + sweep)); p.Stroke();

            float from = a0 + sweep * Mathf.Min(zero, n), to = a0 + sweep * Mathf.Max(zero, n);
            if (to - from > 0.5f)
            {
                p.strokeColor = new Color32(0x5a, 0xb8, 0xff, 0xff);
                p.BeginPath(); p.Arc(c, rad, Angle.Degrees(from), Angle.Degrees(to)); p.Stroke();
            }

            float ang = (a0 + sweep * n) * Mathf.Deg2Rad;
            p.lineWidth = 2f; p.strokeColor = Color.white;
            p.BeginPath(); p.MoveTo(c + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * (rad * 0.25f)); p.LineTo(c + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * rad); p.Stroke();
        }
    }
}
