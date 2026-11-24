using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Sodd.Design.Editor
{
    /// <summary>
    /// Undo/Redo para archivos JSON. El estado (contenido + ubicación de cada archivo, por clave estable = id del
    /// evento) vive en un ScriptableObject oculto; Unity guarda sus versiones en el stack de Undo. Al deshacer,
    /// se aplican al disco solo las entradas que cambiaron (así no se pisan ediciones externas a otros archivos).
    /// </summary>
    internal sealed class SoddUndoState : ScriptableObject
    {
        [Serializable]
        public class Entry
        {
            public string key;
            public string file;
            public string content;
            public bool exists;
            public Entry Clone() => new Entry { key = key, file = file, content = content, exists = exists };
            public bool SameAs(Entry o) => o != null && exists == o.exists && file == o.file && content == o.content;
        }

        public List<Entry> entries = new List<Entry>();

        public Entry Find(string key)
        {
            foreach (var e in entries) if (e.key == key) return e;
            return null;
        }
    }

    public static class SoddUndo
    {
        static SoddUndoState s_state;
        static readonly Dictionary<string, SoddUndoState.Entry> s_applied = new Dictionary<string, SoddUndoState.Entry>();
        /// <summary>Se dispara después de aplicar un undo/redo a disco (la UI se refresca).</summary>
        public static event Action Applied;

        static SoddUndoState State
        {
            get
            {
                if (s_state != null) return s_state;
                foreach (var s in Resources.FindObjectsOfTypeAll<SoddUndoState>()) { s_state = s; break; }
                if (s_state == null)
                {
                    s_state = ScriptableObject.CreateInstance<SoddUndoState>();
                    s_state.hideFlags = HideFlags.HideAndDontSave;
                }
                s_applied.Clear();
                foreach (var e in s_state.entries) s_applied[e.key] = e.Clone();
                Undo.undoRedoPerformed -= OnUndoRedo;
                Undo.undoRedoPerformed += OnUndoRedo;
                return s_state;
            }
        }

        /// <summary>Asegura que exista una entrada con el estado actual de disco (antes de grabar el undo).</summary>
        static SoddUndoState.Entry Ensure(string key, string currentFile)
        {
            var st = State;
            var e = st.Find(key);
            bool exists = currentFile != null && File.Exists(currentFile);
            string content = exists ? File.ReadAllText(currentFile) : null;
            if (e == null)
            {
                e = new SoddUndoState.Entry { key = key, file = currentFile, content = content, exists = exists };
                st.entries.Add(e);
            }
            else
            {
                // Sincronizar con disco (por si se editó afuera) antes de grabar.
                e.file = currentFile ?? e.file; e.content = content; e.exists = exists;
            }
            s_applied[key] = e.Clone();
            return e;
        }

        /// <summary>
        /// Cambia un archivo con Undo. newFile/newContent = estado final (newContent null = borrar).
        /// currentFile = dónde está hoy (null si no existe).
        /// </summary>
        public static void Change(string key, string currentFile, string newFile, string newContent, string undoName)
        {
            var e = Ensure(key, currentFile);
            Undo.RecordObject(State, undoName);
            e.file = newFile ?? currentFile;
            e.content = newContent;
            e.exists = newContent != null;
            Apply(s_applied[key], e);
            s_applied[key] = e.Clone();
            Undo.SetCurrentGroupName(undoName);
            Undo.IncrementCurrentGroup();
        }

        static void OnUndoRedo()
        {
            if (s_state == null) return;
            bool any = false;
            AssetDatabase.StartAssetEditing();
            try
            {
                foreach (var e in s_state.entries)
                {
                    s_applied.TryGetValue(e.key, out var prev);
                    if (e.SameAs(prev)) continue;
                    ApplyNoRefresh(prev, e);
                    s_applied[e.key] = e.Clone();
                    any = true;
                }
            }
            finally { AssetDatabase.StopAssetEditing(); }
            if (!any) return;
            AssetDatabase.Refresh();
            Applied?.Invoke();
        }

        static void Apply(SoddUndoState.Entry prev, SoddUndoState.Entry next)
        {
            ApplyNoRefresh(prev, next);
            if (next.exists) AssetDatabase.ImportAsset(next.file, ImportAssetOptions.ForceUpdate);
            else AssetDatabase.Refresh();
        }

        static void ApplyNoRefresh(SoddUndoState.Entry prev, SoddUndoState.Entry next)
        {
            bool prevExists = prev != null && prev.exists && prev.file != null && File.Exists(prev.file);
            if (!next.exists)
            {
                if (prevExists) AssetDatabase.DeleteAsset(prev.file);
                return;
            }
            if (prevExists && prev.file != next.file)
            {
                EnsureFolder(Path.GetDirectoryName(next.file));
                string err = AssetDatabase.MoveAsset(prev.file, next.file);
                if (!string.IsNullOrEmpty(err)) Debug.LogWarning("[SoddDesign] No se pudo mover " + prev.file + " → " + next.file + ": " + err);
            }
            EnsureFolder(Path.GetDirectoryName(next.file));
            if (!File.Exists(next.file) || File.ReadAllText(next.file) != next.content)
                File.WriteAllText(next.file, next.content);
        }

        /// <summary>Crea carpetas del AssetDatabase (con .meta) recursivamente.</summary>
        public static void EnsureFolder(string folder)
        {
            folder = folder.Replace('\\', '/');
            if (AssetDatabase.IsValidFolder(folder)) return;
            string parent = Path.GetDirectoryName(folder).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(folder));
        }

        /// <summary>Olvida el historial interno (tests).</summary>
        public static void ClearHistory()
        {
            if (s_state != null) Undo.ClearUndo(s_state);
            if (s_state != null) s_state.entries.Clear();
            s_applied.Clear();
        }
    }
}
