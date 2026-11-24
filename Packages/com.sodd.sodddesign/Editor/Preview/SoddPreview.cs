using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Sodd.Design.Editor
{
    /// <summary>
    /// Preview de eventos sin entrar en Play Mode: un motor propio con voces en un GameObject oculto
    /// (HideAndDontSave) actualizado por EditorApplication.update. En Play Mode usa el motor del juego.
    /// </summary>
    [InitializeOnLoad]
    public static class SoddPreview
    {
        static SoddEngine s_engine;
        static GameObject s_root;
        static double s_lastTime;
        static readonly List<SoddEventInstance> s_instances = new List<SoddEventInstance>();

        public static event Action Updated;

        static SoddPreview()
        {
            EditorApplication.playModeStateChanged += s =>
            {
                if (s == PlayModeStateChange.ExitingEditMode) Shutdown();
            };
            AssemblyReloadEvents.beforeAssemblyReload += Shutdown;
        }

        /// <summary>Motor activo para preview: el del juego en Play Mode, o el de edición.</summary>
        public static SoddEngine Engine
        {
            get
            {
                if (Application.isPlaying) return SoddAudio.Engine;
                EnsureEngine();
                return s_engine;
            }
        }

        public static bool IsRunning => s_engine != null;

        static void EnsureEngine()
        {
            if (s_engine != null) return;
            s_root = EditorUtility.CreateGameObjectWithHideFlags("[SoddPreview]", HideFlags.HideAndDontSave);
            var settings = SoddProjectFiles.LoadSettings();
            settings.lookaheadMs = Mathf.Max(settings.lookaheadMs, 200f); // el tick del Editor es irregular
            s_engine = new SoddEngine(settings, s_root.transform);
            var mixer = Resources.Load<UnityEngine.Audio.AudioMixer>("SoddMixer");
            s_engine.Buses.Load(settings.buses, mixer == null ? null : (Func<string, UnityEngine.Audio.AudioMixerGroup>)(n =>
            {
                foreach (var g in mixer.FindMatchingGroups(n)) if (g.name == n) return g;
                return null;
            }));
            s_engine.Snapshots.Mixer = mixer;
            var p = SoddProjectFiles.LoadParameters();
            if (p != null) s_engine.Parameters.Load(p.parameters);
            SoddAudio.SetEngineOverride(s_engine);
            s_lastTime = EditorApplication.timeSinceStartup;
            EditorApplication.update += Tick;
        }

        static void Tick()
        {
            if (s_engine == null) return;
            double now = EditorApplication.timeSinceStartup;
            float dt = (float)Math.Min(0.1, now - s_lastTime);
            s_lastTime = now;
            s_engine.Update(AudioSettings.dspTime, dt);
            for (int i = s_instances.Count - 1; i >= 0; i--) if (!s_instances[i].IsValid) s_instances.RemoveAt(i);
            Updated?.Invoke();
        }

        /// <summary>Reproduce un evento (2D en el listener) con parámetros opcionales.</summary>
        public static SoddEventInstance Play(SoddEventAsset asset, IDictionary<string, float> parameters = null)
        {
            var e = Engine;
            if (e == null || asset == null) return default;
            var inst = e.CreateInstance(asset);
            if (!inst.IsValid) return inst;
            inst.Set3DPosition(e.ListenerPosition);
            if (parameters != null)
                foreach (var kv in parameters)
                    if (!inst.SetParameter(kv.Key, kv.Value, true)) Debug.LogWarning($"[SoddDesign] Preview: parámetro '{kv.Key}' no existe en {asset.Path}");
            inst.Start();
            inst.Release();
            s_instances.Add(inst);
            return inst;
        }

        /// <summary>Instancia "viva" para el Studio (no se libera sola: el que la crea llama Release).</summary>
        public static SoddEventInstance CreateInstance(SoddEventAsset asset)
        {
            var e = Engine;
            return e != null && asset != null ? e.CreateInstance(asset) : default;
        }

        public static void StopAll()
        {
            Engine?.StopAll(null, SoddStopMode.Immediate);
            s_instances.Clear();
        }

        public static void Shutdown()
        {
            if (s_engine == null) return;
            EditorApplication.update -= Tick;
            SoddAudio.SetEngineOverride(null);
            s_engine.Dispose();
            s_engine = null;
            if (s_root != null) UnityEngine.Object.DestroyImmediate(s_root);
            s_root = null;
            s_instances.Clear();
        }

        /// <summary>Avanza el preview manualmente (tests en batchmode, donde EditorApplication.update puede no correr).</summary>
        public static void ManualTick(float dt)
        {
            if (s_engine == null) return;
            s_engine.Update(AudioSettings.dspTime + dt, dt);
        }
    }
}
