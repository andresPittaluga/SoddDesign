using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Sodd.Design.Editor
{
    /// <summary>Guía de uso y tabla de atajos del Studio (menú Sodd y botón ? de la barra).</summary>
    public static class SoddHelp
    {
        public const string GuideFile = "Documentation~/GUIA_TECNICO_AUDIO.md";

        /// <summary>Qué hace cada gesto en el Studio, en el orden en que se muestra.</summary>
        public static readonly (string keys, string action)[] Shortcuts =
        {
            ("Espacio", "Play / Stop del evento"),
            ("Esc", "Stop y volver al inicio"),
            ("Inicio", "Volver al inicio"),
            ("L o el botón ⟳", "Repetir la preview: cuando el evento termina vuelve a sonar (no cambia el evento)"),
            ("▶ al lado de una perilla", "Valor que tocó en el último play, si la perilla tiene un modulador Random"),
            ("F", "Encuadrar el evento"),
            ("Rueda sobre el timeline", "Zoom centrado en el mouse"),
            ("Botón del medio o Alt+arrastrar", "Desplazar el timeline"),
            ("Click en la regla", "Mover el playhead (arrastrar = scrub, Shift = sin snap)"),
            ("Arrastrar región", "Moverla en el tiempo o a otro track (Shift = sin snap)"),
            ("Bordes de la región", "Recortar el inicio o el final"),
            ("Esquinas de arriba", "Fade in / fade out"),
            ("S", "Dividir la región en el playhead"),
            ("D o Ctrl+D", "Duplicar la región"),
            ("Supr / Backspace", "Borrar la región, el marker o el track seleccionado"),
            ("Arrastrar el nombre del track", "Reordenar tracks"),
            ("Arrastrar el chip de dB", "Volumen del track (Shift = fino, doble click = 0 dB)"),
            ("Click derecho en la regla", "Agregar marker o loop"),
            ("Click en un marker", "Seleccionarlo (arrastrar = moverlo, doble click = renombrar)"),
            ("Perillas", "Arrastrar arriba/abajo (Shift = fino), rueda, doble click = valor por defecto"),
            ("Doble click en el número", "Escribir el valor exacto"),
            ("Ctrl+Z / Ctrl+Y", "Deshacer / rehacer"),
            ("Soltar AudioClips", "Crear regiones (sobre un track) o un track nuevo"),
        };

        public static string GuidePath
        {
            get
            {
                var pkg = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(SoddHelp).Assembly);
                return pkg == null ? null : Path.Combine(pkg.resolvedPath, GuideFile);
            }
        }

        [MenuItem("Sodd/Guía de uso", priority = 40)]
        public static void OpenGuide()
        {
            string path = GuidePath;
            if (path == null || !File.Exists(path)) { Debug.LogWarning("[SoddDesign] No se encontró la guía: " + path); return; }
            EditorUtility.OpenWithDefaultApp(path);
        }

        [MenuItem("Sodd/Atajos de Studio", priority = 41)]
        static void OpenShortcuts()
        {
            SoddStudioWindow.Open();
            EditorWindow.GetWindow<SoddStudioWindow>().ShowHelp(true);
        }

        /// <summary>Panel con la tabla de atajos y un botón a la guía completa.</summary>
        public static VisualElement BuildPanel(System.Action close)
        {
            var panel = new VisualElement();
            panel.AddToClassList("sd-help-panel");
            var head = new VisualElement(); head.AddToClassList("sd-card-head");
            var title = new Label("Cómo se usa el Studio"); title.AddToClassList("sd-card-title"); head.Add(title);
            var x = new Button(close) { text = "✕", tooltip = "Cerrar" }; x.AddToClassList("sd-btn"); x.AddToClassList("sd-btn--icon"); head.Add(x);
            panel.Add(head);
            var scroll = new ScrollView(); scroll.AddToClassList("sd-grow");
            foreach (var (keys, action) in Shortcuts)
            {
                var row = new VisualElement(); row.AddToClassList("sd-help-row");
                var k = new Label(keys); k.AddToClassList("sd-help-keys");
                var a = new Label(action); a.AddToClassList("sd-help-action");
                row.Add(k); row.Add(a);
                scroll.Add(row);
            }
            panel.Add(scroll);
            var guide = new Button(OpenGuide) { text = "Abrir la guía completa", tooltip = "Sodd > Guía de uso" };
            guide.AddToClassList("sd-btn"); guide.AddToClassList("sd-btn--primary");
            panel.Add(guide);
            return panel;
        }
    }
}
