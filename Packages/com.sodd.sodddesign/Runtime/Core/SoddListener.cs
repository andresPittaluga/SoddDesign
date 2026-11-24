using UnityEngine;
using UnityEngine.Audio;

namespace Sodd.Design
{
    /// <summary>
    /// "Oído" de SoddDesign: el transform que se usa para audibilidad/robo de voces. Asegura que su GameObject tenga un
    /// AudioListener de Unity (que es el que espacializa) y que sea el único activo (reemplaza al StudioListener de FMOD).
    /// </summary>
    [AddComponentMenu("Sodd/Sodd Listener")]
    [DisallowMultipleComponent]
    public sealed class SoddListener : MonoBehaviour
    {
        public static SoddListener Current { get; private set; }

        void OnEnable()
        {
            Current = this;
            if (!TryGetComponent<AudioListener>(out var mine)) mine = gameObject.AddComponent<AudioListener>();
            mine.enabled = true;
            foreach (var other in FindObjectsByType<AudioListener>(FindObjectsSortMode.None))
                if (other != mine && other.enabled) other.enabled = false;
        }

        void OnDisable() { if (Current == this) Current = null; }
    }
}
