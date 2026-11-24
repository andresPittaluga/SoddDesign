using UnityEngine;

namespace Sodd.Design
{
    /// <summary>
    /// Base de los DSP propios (OnAudioFilterRead). Thread-safety: el main thread solo escribe floats volatile;
    /// el audio thread solo lee parámetros y escribe su propio estado. Nada de API de Unity en el audio thread.
    /// Los buffers se reservan en Awake (main thread): cero allocations en el audio thread.
    /// </summary>
    public abstract class SoddDspFilter : MonoBehaviour
    {
        protected int sampleRate = 48000;
        volatile bool _reset;

        protected virtual void Awake()
        {
            sampleRate = AudioSettings.outputSampleRate > 0 ? AudioSettings.outputSampleRate : 48000;
            hideFlags = HideFlags.HideInInspector;
        }

        /// <summary>Pide limpiar el estado (colas de delay, envolventes) al reutilizar la voz.</summary>
        public void RequestReset() => _reset = true;

        /// <summary>Setea parámetros desde el main thread (en el orden del catálogo de efectos).</summary>
        public abstract void SetParam(int index, float value);

        void OnAudioFilterRead(float[] data, int channels) => Render(data, channels);

        /// <summary>Procesa un buffer intercalado (lo llama el audio thread; público para tests offline).</summary>
        public void Render(float[] data, int channels)
        {
            if (_reset) { _reset = false; ResetState(); }
            Process(data, channels);
        }

        protected abstract void ResetState();
        protected abstract void Process(float[] data, int channels);
    }
}
