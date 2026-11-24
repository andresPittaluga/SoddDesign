using System;
using UnityEngine;

namespace Sodd.Design
{
    public enum SoddPlayTrigger { None, Start, Enable, TriggerEnter, TriggerExit, CollisionEnter }

    /// <summary>Componente para reproducir un evento sin código.</summary>
    [AddComponentMenu("Sodd/Sodd Emitter")]
    public sealed class SoddEmitter : MonoBehaviour
    {
        [Serializable]
        public struct ParamValue
        {
            public string name;
            public float value;
        }

        [Tooltip("Evento a reproducir.")]
        public SoddEventReference eventRef;
        [Tooltip("Cuándo se reproduce automáticamente.")]
        public SoddPlayTrigger playOn = SoddPlayTrigger.Start;
        [Tooltip("Cuándo se detiene automáticamente.")]
        public SoddPlayTrigger stopOn = SoddPlayTrigger.None;
        [Tooltip("Si se requiere un tag para los triggers/colisiones (vacío = cualquiera).")]
        public string triggerTag = "";
        [Tooltip("Detener al destruir/desactivar el objeto.")]
        public bool stopOnDestroy = true;
        [Tooltip("Permitir fade-out (release) al detener.")]
        public bool allowFadeOut = true;
        [Tooltip("Valores iniciales de parámetros (locales o globales).")]
        public ParamValue[] parameters = Array.Empty<ParamValue>();

        SoddEventInstance _instance;

        public SoddEventInstance Instance => _instance;
        public bool IsPlaying => _instance.IsPlaying;

        void Start() { if (playOn == SoddPlayTrigger.Start) Play(); if (stopOn == SoddPlayTrigger.Start) Stop(); }
        void OnEnable() { if (playOn == SoddPlayTrigger.Enable) Play(); if (stopOn == SoddPlayTrigger.Enable) Stop(); }

        void OnDisable()
        {
            if (stopOnDestroy) Stop();
        }

        void OnDestroy()
        {
            if (stopOnDestroy) Stop();
            _instance.Release();
        }

        bool TagOk(Component other) => string.IsNullOrEmpty(triggerTag) || other.CompareTag(triggerTag);

        void OnTriggerEnter(Collider other) { if (TagOk(other)) Handle(SoddPlayTrigger.TriggerEnter); }
        void OnTriggerExit(Collider other) { if (TagOk(other)) Handle(SoddPlayTrigger.TriggerExit); }
        void OnCollisionEnter(Collision c) { if (TagOk(c.collider)) Handle(SoddPlayTrigger.CollisionEnter); }

        void Handle(SoddPlayTrigger t)
        {
            if (playOn == t) Play();
            if (stopOn == t) Stop();
        }

        public void Play()
        {
            var asset = eventRef.Resolve();
            if (asset == null) { if (!eventRef.IsNull) Debug.LogWarning($"[SoddDesign] {name}: evento '{eventRef}' no encontrado", this); return; }
            if (!_instance.IsValid || _instance.Asset != asset)
            {
                _instance.Release();
                _instance = SoddAudio.CreateInstance(asset);
            }
            _instance.AttachTo(transform);
            foreach (var p in parameters) _instance.SetParameter(p.name, p.value, true);
            _instance.Start();
        }

        public void Stop()
        {
            if (_instance.IsValid) _instance.Stop(allowFadeOut ? SoddStopMode.AllowFadeOut : SoddStopMode.Immediate);
        }

        public void SetParameter(string paramName, float value)
        {
            for (int i = 0; i < parameters.Length; i++) if (parameters[i].name == paramName) parameters[i].value = value;
            _instance.SetParameter(paramName, value);
        }

        void OnDrawGizmosSelected()
        {
            var a = eventRef.asset;
            if (a == null || a.data == null || !a.data.is3D) return;
            var p = transform.position;
            Gizmos.color = new Color(0.35f, 0.85f, 1f, 0.9f);
            Gizmos.DrawWireSphere(p, a.data.spatial.minDistance);
            Gizmos.color = new Color(0.35f, 0.85f, 1f, 0.35f);
            Gizmos.DrawWireSphere(p, a.data.spatial.maxDistance);
        }
    }
}
