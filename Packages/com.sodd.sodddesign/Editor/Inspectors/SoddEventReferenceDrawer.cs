using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace Sodd.Design.Editor
{
    /// <summary>Drawer de SoddEventReference: botón con buscador jerárquico (por path) + ping + preview.</summary>
    [CustomPropertyDrawer(typeof(SoddEventReference))]
    public sealed class SoddEventReferenceDrawer : PropertyDrawer
    {
        public override VisualElement CreatePropertyGUI(SerializedProperty property)
        {
            var guidP = property.FindPropertyRelative("guid");
            var pathP = property.FindPropertyRelative("path");
            var assetP = property.FindPropertyRelative("asset");

            var row = new VisualElement { style = { flexDirection = FlexDirection.Row } };
            var label = new Label(property.displayName) { style = { minWidth = 120, unityTextAlign = TextAnchor.MiddleLeft } };
            label.AddToClassList(BaseField<int>.labelUssClassName);
            var button = new Button { tooltip = "Elegí un evento de SoddDesign (con búsqueda)", style = { flexGrow = 1, unityTextAlign = TextAnchor.MiddleLeft } };
            var ping = new Button { text = "◎", tooltip = "Mostrar el archivo .sodd.json en el Project" };
            var clear = new Button { text = "✕", tooltip = "Quitar referencia" };
            row.Add(label); row.Add(button); row.Add(ping); row.Add(clear);

            void Refresh()
            {
                var a = assetP.objectReferenceValue as SoddEventAsset;
                string p = a != null ? a.Path : pathP.stringValue;
                bool missing = a == null && !string.IsNullOrEmpty(guidP.stringValue) && SoddEvents.Find(guidP.stringValue) == null;
                button.text = string.IsNullOrEmpty(p) ? "(ninguno) — click para buscar" : (missing ? "⚠ " : "♪ ") + p;
                button.style.color = missing ? new Color(1f, 0.6f, 0.4f) : StyleKeyword.Null;
            }

            void Assign(SoddEventAsset a)
            {
                property.serializedObject.Update();
                assetP.objectReferenceValue = a;
                guidP.stringValue = a != null ? a.Id : "";
                pathP.stringValue = a != null ? a.Path : "";
                property.serializedObject.ApplyModifiedProperties();
                Refresh();
            }

            button.clicked += () =>
            {
                var dd = new SoddEventDropdown(new AdvancedDropdownState(), Assign);
                dd.Show(button.worldBound);
            };
            ping.clicked += () =>
            {
                var a = assetP.objectReferenceValue as SoddEventAsset ?? SoddEvents.Find(guidP.stringValue);
                if (a != null) EditorGUIUtility.PingObject(a);
            };
            clear.clicked += () => Assign(null);

            // Drag & drop de un .sodd.json sobre el campo
            row.RegisterCallback<DragUpdatedEvent>(_ =>
            {
                if (DragAndDrop.objectReferences.Length > 0 && DragAndDrop.objectReferences[0] is SoddEventAsset) DragAndDrop.visualMode = DragAndDropVisualMode.Link;
            });
            row.RegisterCallback<DragPerformEvent>(_ =>
            {
                if (DragAndDrop.objectReferences.Length > 0 && DragAndDrop.objectReferences[0] is SoddEventAsset a) { DragAndDrop.AcceptDrag(); Assign(a); }
            });

            row.TrackPropertyValue(property, _ => Refresh());
            Refresh();
            return row;
        }
    }

    /// <summary>Dropdown con búsqueda para elegir eventos, agrupados por carpetas del path.</summary>
    public sealed class SoddEventDropdown : AdvancedDropdown
    {
        readonly Action<SoddEventAsset> _onPick;
        readonly Dictionary<int, SoddEventAsset> _map = new Dictionary<int, SoddEventAsset>();

        public SoddEventDropdown(AdvancedDropdownState state, Action<SoddEventAsset> onPick) : base(state)
        {
            _onPick = onPick;
            minimumSize = new Vector2(320, 380);
        }

        protected override AdvancedDropdownItem BuildRoot()
        {
            var root = new AdvancedDropdownItem("Eventos SoddDesign");
            var folders = new Dictionary<string, AdvancedDropdownItem>();
            int id = 1;
            foreach (var a in SoddProjectFiles.AllEventAssets())
            {
                string path = a.Path ?? a.name;
                var parts = path.Split('/');
                var parent = root;
                string acc = "";
                for (int i = 0; i < parts.Length - 1; i++)
                {
                    acc += parts[i] + "/";
                    if (!folders.TryGetValue(acc, out var f)) { f = new AdvancedDropdownItem(parts[i]); folders[acc] = f; parent.AddChild(f); }
                    parent = f;
                }
                var item = new AdvancedDropdownItem(parts[parts.Length - 1]) { id = id, icon = SoddEditorIcons.EventIcon };
                _map[id++] = a;
                parent.AddChild(item);
            }
            if (id == 1) root.AddChild(new AdvancedDropdownItem("(no hay eventos: creá uno en SoddDesign Studio)") { enabled = false });
            return root;
        }

        protected override void ItemSelected(AdvancedDropdownItem item)
        {
            if (_map.TryGetValue(item.id, out var a)) _onPick?.Invoke(a);
        }
    }
}
