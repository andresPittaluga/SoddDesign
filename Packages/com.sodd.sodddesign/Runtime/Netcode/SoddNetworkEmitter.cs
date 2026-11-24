#if SODD_NGO
using Unity.Netcode;
using UnityEngine;

namespace Sodd.Design.Netcode
{
    /// <summary>
    /// Evento persistente sincronizado (motores, loops, ambientes de un objeto en red).
    /// - "Playing" y hasta 4 parámetros viajan como NetworkVariable (los escribe el dueño; con umbral para no saturar).
    /// - Cada cliente reproduce SU instancia local adjunta a este objeto; nada de audio viaja por red.
    /// </summary>
    [AddComponentMenu("Sodd/Sodd Network Emitter")]
    public sealed class SoddNetworkEmitter : NetworkBehaviour
    {
        public const int MaxParams = 4;

        [Tooltip("Evento (normalmente con loop).")]
        public SoddEventReference eventRef;
        [Tooltip("Arranca solo al spawnear.")]
        public bool playOnSpawn = true;
        [Tooltip("Nombres de parámetros sincronizados (máx 4).")]
        public string[] parameterNames = new string[0];
        [Tooltip("Cambio mínimo para enviar un parámetro (evita saturar la red).")]
        public float sendThreshold = 0.02f;
        [Tooltip("Intervalo mínimo entre envíos del mismo parámetro (s).")]
        public float minSendInterval = 0.05f;

        readonly NetworkVariable<bool> _playing = new NetworkVariable<bool>(false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
        readonly NetworkVariable<float> _p0 = Param(), _p1 = Param(), _p2 = Param(), _p3 = Param();

        static NetworkVariable<float> Param() => new NetworkVariable<float>(0f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

        readonly float[] _pending = new float[MaxParams];
        readonly float[] _lastSent = new float[MaxParams];
        readonly float[] _lastTime = new float[MaxParams];
        readonly bool[] _dirty = new bool[MaxParams];
        SoddEventInstance _inst;

        NetworkVariable<float> Var(int i) => i == 0 ? _p0 : i == 1 ? _p1 : i == 2 ? _p2 : _p3;

        public bool IsPlaying => _playing.Value;

        public override void OnNetworkSpawn()
        {
            _playing.OnValueChanged += (_, now) => Apply(now);
            for (int i = 0; i < MaxParams; i++)
            {
                int idx = i;
                Var(i).OnValueChanged += (_, v) => ApplyParam(idx, v);
            }
            if (IsOwner && playOnSpawn) _playing.Value = true;
            Apply(_playing.Value);
        }

        public override void OnNetworkDespawn()
        {
            _inst.Stop(SoddStopMode.AllowFadeOut);
            _inst.Release();
            _inst = default;
        }

        /// <summary>Solo el dueño: inicia/detiene en todos los clientes.</summary>
        public void SetPlaying(bool play)
        {
            if (!IsOwner) { Debug.LogWarning("[SoddDesign] SoddNetworkEmitter: solo el dueño puede cambiar Playing", this); return; }
            _playing.Value = play;
        }

        /// <summary>Solo el dueño: setea un parámetro sincronizado (se aplica local al instante y se envía con umbral).</summary>
        public void SetParameter(string paramName, float value)
        {
            int i = System.Array.IndexOf(parameterNames, paramName);
            if (i < 0 || i >= MaxParams) { _inst.SetParameter(paramName, value); return; }
            _pending[i] = value;
            _dirty[i] = true;
            _inst.SetParameter(paramName, value);
        }

        void Update()
        {
            if (!IsSpawned || !IsOwner) return;
            float now = Time.unscaledTime;
            for (int i = 0; i < MaxParams; i++)
            {
                if (!_dirty[i] || now - _lastTime[i] < minSendInterval) continue;
                if (Mathf.Abs(_pending[i] - _lastSent[i]) < sendThreshold) { _dirty[i] = false; continue; }
                Var(i).Value = _pending[i];
                _lastSent[i] = _pending[i]; _lastTime[i] = now; _dirty[i] = false;
            }
        }

        void Apply(bool play)
        {
            if (play)
            {
                if (_inst.IsPlaying) return;
                var asset = eventRef.Resolve();
                if (asset == null) { Debug.LogWarning($"[SoddDesign] SoddNetworkEmitter: evento '{eventRef}' no encontrado", this); return; }
                _inst.Release();
                _inst = SoddAudio.CreateInstance(asset);
                _inst.AttachTo(transform);
                for (int i = 0; i < MaxParams && i < parameterNames.Length; i++) _inst.SetParameter(parameterNames[i], Var(i).Value, true);
                _inst.Start();
            }
            else _inst.Stop(SoddStopMode.AllowFadeOut);
        }

        void ApplyParam(int i, float v)
        {
            if (IsOwner || i >= parameterNames.Length) return; // el dueño ya lo aplicó local
            _inst.SetParameter(parameterNames[i], v);
        }
    }
}
#endif
