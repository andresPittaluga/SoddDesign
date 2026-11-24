using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Audio;

namespace Sodd.Design
{
    /// <summary>
    /// Zona de reverb. Hace dos cosas:
    /// 1) Usa un AudioReverbZone de Unity (reverb por distancia aplicada a cada voz según su reverbSend).
    /// 2) Sube el retorno del grupo "ReverbSend" de SoddMixer según qué tan adentro está el listener.
    /// </summary>
    [AddComponentMenu("Sodd/Sodd Reverb Zone")]
    [RequireComponent(typeof(AudioReverbZone))]
    public sealed class SoddReverbZone : MonoBehaviour
    {
        public const string ReverbReturnParam = "ReverbSend_Volume";
        static readonly List<SoddReverbZone> s_zones = new List<SoddReverbZone>();

        [Tooltip("Preset de reverb de Unity.")]
        public AudioReverbPreset preset = AudioReverbPreset.Hallway;
        [Tooltip("Dentro de este radio el efecto es completo.")]
        public float minDistance = 5f;
        [Tooltip("Fuera de este radio no hay efecto.")]
        public float maxDistance = 15f;
        [Tooltip("Nivel (dB) del retorno ReverbSend del mixer con el listener adentro.")]
        [Range(-80f, 6f)] public float sendLevel;

        AudioReverbZone _zone;

        void OnEnable()
        {
            _zone = GetComponent<AudioReverbZone>();
            Apply();
            s_zones.Add(this);
        }

        void OnDisable() => s_zones.Remove(this);
        void OnValidate() { if (_zone != null) Apply(); }

        void Apply()
        {
            _zone.reverbPreset = preset;
            _zone.minDistance = minDistance;
            _zone.maxDistance = Mathf.Max(minDistance, maxDistance);
        }

        public float Weight(Vector3 listener)
        {
            float d = Vector3.Distance(listener, transform.position);
            if (d <= minDistance) return 1f;
            if (d >= maxDistance) return 0f;
            return 1f - (d - minDistance) / Mathf.Max(0.001f, maxDistance - minDistance);
        }

        /// <summary>Lo llama el manager por frame: setea el retorno de reverb del mixer. Sin allocations.</summary>
        public static void UpdateZones(Vector3 listener, AudioMixer mixer)
        {
            if (mixer == null) return;
            float best = -80f;
            for (int i = 0; i < s_zones.Count; i++)
            {
                var z = s_zones[i];
                float w = z.Weight(listener);
                if (w <= 0f) continue;
                float db = Mathf.Lerp(-80f, z.sendLevel, Mathf.Sqrt(w));
                if (db > best) best = db;
            }
            mixer.SetFloat(ReverbReturnParam, best);
        }

        void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(0.7f, 0.45f, 1f, 0.9f);
            Gizmos.DrawWireSphere(transform.position, minDistance);
            Gizmos.color = new Color(0.7f, 0.45f, 1f, 0.35f);
            Gizmos.DrawWireSphere(transform.position, maxDistance);
        }
    }
}
