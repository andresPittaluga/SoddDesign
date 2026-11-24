using UnityEngine;

namespace Sodd.Design
{
    /// <summary>
    /// Ayudas para migrar código desde FMOD (lo usa Tools/fmod_code_migrate.py).
    /// Reemplaza <c>RuntimeUtils.To3DAttributes(x)</c> aceptando Vector3, Transform, GameObject o Component.
    /// </summary>
    /// <summary>Equivalente de FMOD.Studio.PLAYBACK_STATE.</summary>
    public enum SoddPlaybackState { Playing, Sustaining, Stopped, Starting, Stopping }

    public static class SoddFmodCompat
    {
        /// <summary>Equivalente de EventInstance.getPlaybackState.</summary>
        public static SoddPlaybackState State(SoddEventInstance i) =>
            !i.IsValid ? SoddPlaybackState.Stopped : i.IsStopping ? SoddPlaybackState.Stopping : i.IsPlaying ? SoddPlaybackState.Playing : SoddPlaybackState.Stopped;

        public static Vector3 Pos(Vector3 p) => p;
        public static Vector3 Pos(Transform t) => t != null ? t.position : Vector3.zero;
        public static Vector3 Pos(GameObject g) => g != null ? g.transform.position : Vector3.zero;
        public static Vector3 Pos(Component c) => c != null ? c.transform.position : Vector3.zero;

        /// <summary>Equivalente de StudioSystem.getEvent(path) == OK: ¿existe el evento?</summary>
        public static bool EventExists(string idOrPath) => SoddEvents.Find(idOrPath) != null;

        /// <summary>Equivalente de EventDescription.getLength (en segundos; FMOD devuelve ms).</summary>
        public static float LengthSeconds(SoddEventReference r) { var a = r.Resolve(); return a != null ? a.data.Length : 0f; }
    }
}
