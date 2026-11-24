using System;
using UnityEngine;
using UnityEngine.Audio;

namespace Sodd.Design
{
    /// <summary>
    /// API estática estilo FMOD.
    /// <code>
    /// SoddAudio.PlayOneShot(jumpRef, transform.position);
    /// var music = SoddAudio.CreateInstance("music/level1"); music.Start();
    /// SoddAudio.SetGlobalParameter("Health", 30);
    /// SoddAudio.GetBus("Music").FadeTo(-12, 1f);
    /// </code>
    /// </summary>
    public static class SoddAudio
    {
        static SoddEngine s_override;

        /// <summary>Motor activo: el del SoddAudioManager en Play Mode, o uno inyectado (preview del Editor / tests).</summary>
        public static SoddEngine Engine
        {
            get
            {
                if (s_override != null) return s_override;
                if (SoddAudioManager.Engine == null && Application.isPlaying) SoddAudioManager.Initialize();
                return SoddAudioManager.Engine;
            }
        }

        /// <summary>Inyecta un motor (preview en Editor o tests). null para volver al de runtime.</summary>
        public static void SetEngineOverride(SoddEngine engine) => s_override = engine;

        static SoddEngine RequireEngine()
        {
            var e = Engine;
            if (e == null) Debug.LogWarning("[SoddDesign] No hay motor de audio activo (¿fuera de Play Mode?).");
            return e;
        }

        static SoddEventAsset Resolve(SoddEventReference r)
        {
            var a = r.Resolve();
            if (a == null && !r.IsNull) Debug.LogWarning($"[SoddDesign] Evento no encontrado: '{r}'.{SoddText.DidYouMean(r.path ?? r.guid, AllPaths())}");
            return a;
        }

        static SoddEventAsset Resolve(string idOrPath)
        {
            var a = SoddEvents.Find(idOrPath);
            if (a == null) Debug.LogWarning($"[SoddDesign] Evento no encontrado: '{idOrPath}'.{SoddText.DidYouMean(idOrPath, AllPaths())}");
            return a;
        }

        static System.Collections.Generic.IEnumerable<string> AllPaths()
        {
            var reg = SoddEvents.Registry;
            if (reg == null) yield break;
            foreach (var e in reg.events) if (e != null) yield return e.Path;
        }

        // ------------------------------------------------------------------ one-shots
        public static SoddEventInstance PlayOneShot(SoddEventReference evt, Vector3 position = default) => PlayOneShot(Resolve(evt), position);
        public static SoddEventInstance PlayOneShot(string idOrPath, Vector3 position = default) => PlayOneShot(Resolve(idOrPath), position);
        public static SoddEventInstance PlayOneShot(SoddEventReference evt, Transform attachTo) => PlayOneShotAttached(Resolve(evt), attachTo);
        public static SoddEventInstance PlayOneShot(string idOrPath, Transform attachTo) => PlayOneShotAttached(Resolve(idOrPath), attachTo);

        public static SoddEventInstance PlayOneShot(SoddEventAsset asset, Vector3 position = default)
        {
            var e = RequireEngine();
            if (e == null || asset == null) return default;
            var inst = e.CreateInstance(asset);
            inst.Set3DPosition(position);
            inst.Start();
            inst.Release();
            return inst;
        }

        public static SoddEventInstance PlayOneShotAttached(SoddEventAsset asset, Transform attachTo)
        {
            var e = RequireEngine();
            if (e == null || asset == null) return default;
            var inst = e.CreateInstance(asset);
            inst.AttachTo(attachTo);
            inst.Start();
            inst.Release();
            return inst;
        }

        // ------------------------------------------------------------------ instancias
        public static SoddEventInstance CreateInstance(SoddEventReference evt) { var e = RequireEngine(); return e != null ? e.CreateInstance(Resolve(evt)) : default; }
        public static SoddEventInstance CreateInstance(string idOrPath) { var e = RequireEngine(); return e != null ? e.CreateInstance(Resolve(idOrPath)) : default; }
        public static SoddEventInstance CreateInstance(SoddEventAsset asset) { var e = RequireEngine(); return e != null ? e.CreateInstance(asset) : default; }

        // ------------------------------------------------------------------ parámetros globales
        public static int GetGlobalParameterId(string name) => Engine?.Parameters.IndexOf(name) ?? -1;

        public static bool SetGlobalParameter(string name, float value, bool immediate = false)
        {
            var e = RequireEngine();
            if (e == null) return false;
            int i = e.Parameters.IndexOf(name);
            if (i < 0)
            {
                var names = new System.Collections.Generic.List<string>();
                foreach (var d in e.Parameters.Definitions) names.Add(d.name);
                Debug.LogWarning($"[SoddDesign] El parámetro global '{name}' no existe.{SoddText.DidYouMean(name, names)}");
                return false;
            }
            return e.Parameters.Set(i, value, immediate);
        }

        public static bool SetGlobalParameter(int id, float value, bool immediate = false) => Engine != null && Engine.Parameters.Set(id, value, immediate);
        public static float GetGlobalParameter(string name) { var e = Engine; return e != null ? e.Parameters.Get(e.Parameters.IndexOf(name)) : 0f; }
        public static float GetGlobalParameter(int id) => Engine?.Parameters.Get(id) ?? 0f;

        // ------------------------------------------------------------------ buses, snapshots, global
        /// <summary>Bus lógico por nombre (Master, Music, SFX, UI, Voice, Ambience o los de sodd.settings.json).</summary>
        public static SoddBus GetBus(string name) => Engine?.Buses.Find(name);

        public static bool SetSnapshot(string name, float fadeTime = 0.5f) => Engine != null && Engine.Snapshots.Set(name, fadeTime);
        public static void ClearSnapshot(float fadeTime = 0.5f) => Engine?.Snapshots.Set(null, fadeTime);
        public static string CurrentSnapshot => Engine?.Snapshots.Current;

        /// <summary>Detiene todo (o solo lo de un bus si bus != null).</summary>
        public static void StopAll(string bus = null, SoddStopMode mode = SoddStopMode.AllowFadeOut) => Engine?.StopAll(bus, mode);

        /// <summary>Pausa/reanuda todo el audio de SoddDesign (ej. menú de pausa).</summary>
        public static void Pause(bool paused) => Engine?.SetPaused(paused);
    }

    /// <summary>Marca qué transform es el "oído" para cálculos de audibilidad/stealing (el AudioListener de Unity sigue espacializando).</summary>
    [AddComponentMenu("Sodd/Sodd Listener")]
    public sealed class SoddListener : MonoBehaviour
    {
        public static SoddListener Current { get; private set; }
        void OnEnable() => Current = this;
        void OnDisable() { if (Current == this) Current = null; }
    }

    /// <summary>Driver de runtime: crea el motor al arrancar el juego y lo actualiza cada frame.</summary>
    [AddComponentMenu("")]
    [DefaultExecutionOrder(1000)]
    public sealed class SoddAudioManager : MonoBehaviour
    {
        public static SoddEngine Engine { get; private set; }
        public static SoddSettingsData Settings { get; private set; }
        static SoddAudioManager s_instance;
        /// <summary>Desactivar antes de la carga de escena para inicializar manualmente.</summary>
        public static bool AutoInitialize = true;

        Transform _listener;
        float _listenerSearch;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            Engine = null; s_instance = null; Settings = null;
            SoddEvents.ResetCache();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void AutoInit()
        {
            if (AutoInitialize) Initialize();
        }

        public static void Initialize()
        {
            if (s_instance != null) return;
            var go = new GameObject("[SoddAudio]");
            if (Application.isPlaying) DontDestroyOnLoad(go);
            s_instance = go.AddComponent<SoddAudioManager>();
            Engine = CreateEngine(go.transform, out var settings);
            Settings = settings;
        }

        /// <summary>Crea un motor con la configuración del proyecto (registro → settings + parámetros + mixer).</summary>
        public static SoddEngine CreateEngine(Transform voiceRoot, out SoddSettingsData settings)
        {
            var reg = SoddEvents.Registry;
            settings = SoddSettingsData.CreateDefault();
            if (reg != null && reg.settingsJson != null)
            {
                settings = SoddReader.ParseSettings(reg.settingsJson.text, out var r);
                if (!r.ok) Debug.LogError("[SoddDesign] sodd.settings.json: " + r);
            }
            var engine = new SoddEngine(settings, voiceRoot);
            var mixer = Resources.Load<AudioMixer>("SoddMixer");
            if (mixer != null)
            {
                engine.Buses.Load(settings.buses, name =>
                {
                    var groups = mixer.FindMatchingGroups(name);
                    foreach (var g in groups) if (g.name == name) return g;
                    return null;
                });
                engine.Snapshots.Mixer = mixer;
            }
            if (reg != null && reg.parametersJson != null)
            {
                var p = SoddReader.ParseParameters(reg.parametersJson.text, out var r);
                if (!r.ok) Debug.LogError("[SoddDesign] sodd.parameters.json: " + r);
                engine.Parameters.Load(p.parameters);
            }
            return engine;
        }

        void Update()
        {
            if (Engine == null) return;
            if (SoddListener.Current != null) _listener = SoddListener.Current.transform;
            else if (_listener == null && (_listenerSearch -= Time.unscaledDeltaTime) <= 0f)
            {
                _listenerSearch = 1f;
                var al = FindAnyObjectByType<AudioListener>();
                _listener = al != null ? al.transform : null;
            }
            if (_listener != null) Engine.ListenerPosition = _listener.position;
            Engine.Update(AudioSettings.dspTime, Time.unscaledDeltaTime);
        }

        void OnDestroy()
        {
            if (s_instance != this) return;
            Engine?.Dispose();
            Engine = null; s_instance = null;
        }
    }
}
