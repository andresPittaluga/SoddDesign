using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.SceneManagement;

namespace Sodd.Design
{
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
            SceneManager.sceneLoaded += (_, __) => EnsureListener();
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

        /// <summary>Si no hay ningún AudioListener activo (ej. el proyecto venía de FMOD), agrega uno a la cámara principal.</summary>
        public static void EnsureListener()
        {
            if (SoddListener.Current != null) return;
            foreach (var l in FindObjectsByType<AudioListener>(FindObjectsSortMode.None)) if (l.isActiveAndEnabled) return;
            var cam = Camera.main;
            if (cam != null && !cam.TryGetComponent<AudioListener>(out _)) cam.gameObject.AddComponent<AudioListener>();
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
            SoddReverbZone.UpdateZones(Engine.ListenerPosition, Engine.Snapshots.Mixer);
        }

        void OnDestroy()
        {
            if (s_instance != this) return;
            Engine?.Dispose();
            Engine = null; s_instance = null;
        }
    }
}
