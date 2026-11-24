#if SODD_NGO
using Unity.Netcode;
using UnityEngine;

namespace Sodd.Design.Netcode
{
    /// <summary>Payload de red: hash del evento + posición/attach + hasta 4 parámetros. Sin strings ni audio.</summary>
    public struct SoddNetMessage : INetworkSerializable
    {
        public uint EventHash;
        public Vector3 Position;
        public bool HasAttach;
        public NetworkObjectReference Attach;
        public SoddNetParams Params;

        public void NetworkSerialize<T>(BufferSerializer<T> s) where T : IReaderWriter
        {
            s.SerializeValue(ref EventHash);
            s.SerializeValue(ref Position);
            s.SerializeValue(ref HasAttach);
            if (HasAttach) s.SerializeValue(ref Attach);
            s.SerializeValue(ref Params.Count);
            int n = Mathf.Min(Params.Count, SoddNetParams.Max);
            if (n > 0) { s.SerializeValue(ref Params.Id0); s.SerializeValue(ref Params.V0); }
            if (n > 1) { s.SerializeValue(ref Params.Id1); s.SerializeValue(ref Params.V1); }
            if (n > 2) { s.SerializeValue(ref Params.Id2); s.SerializeValue(ref Params.V2); }
            if (n > 3) { s.SerializeValue(ref Params.Id3); s.SerializeValue(ref Params.V3); }
        }
    }

    /// <summary>
    /// Audio en red para Netcode for GameObjects 2.x. Poné UN SoddNetworkAudio en un NetworkObject de la escena
    /// (o en un prefab spawneado por el server). Después usá <c>SoddAudio.Net.PlayOneShot(...)</c> desde cualquier lado.
    ///
    /// Estrategia anti-duplicados (elegida y documentada): el que dispara reproduce LOCAL al instante (sin latencia)
    /// y envía un RPC <c>SendTo.NotMe</c>; así el host/cliente que dispara nunca lo escucha dos veces.
    /// OwnerOnly: se envía solo al cliente dueño (RpcTarget.Single); si el que dispara es el dueño, solo local.
    /// </summary>
    [AddComponentMenu("Sodd/Sodd Network Audio")]
    public sealed class SoddNetworkAudio : NetworkBehaviour, ISoddNetAudio
    {
        public static SoddNetworkAudio Instance { get; private set; }

        public bool IsActive => IsSpawned && NetworkManager != null && NetworkManager.IsListening;

        public override void OnNetworkSpawn()
        {
            Instance = this;
            SoddNet.Handler = this;
        }

        public override void OnNetworkDespawn()
        {
            if (Instance == this) Instance = null;
            if (ReferenceEquals(SoddNet.Handler, this)) SoddNet.Handler = null;
        }

        public SoddEventInstance PlayOneShot(SoddEventAsset asset, Vector3 position, Transform attachTo, in SoddNetParams parameters, ulong ownerClientId)
        {
            var msg = new SoddNetMessage { EventHash = asset.Hash, Position = position, Params = parameters };
            if (attachTo != null && attachTo.TryGetComponent<NetworkObject>(out var no) && no.IsSpawned)
            {
                msg.HasAttach = true;
                msg.Attach = new NetworkObjectReference(no);
            }
            ulong me = NetworkManager.LocalClientId;
            switch (asset.data.networkMode)
            {
                case SoddNetworkMode.OwnerOnly:
                    {
                        ulong owner = ownerClientId != ulong.MaxValue ? ownerClientId : (msg.HasAttach && msg.Attach.TryGet(out var o) ? o.OwnerClientId : me);
                        if (owner == me) return SoddAudio.Net.PlayLocal(asset, position, attachTo, parameters);
                        PlayTargetRpc(msg, RpcTarget.Single(owner, RpcTargetUse.Temp));
                        return default;
                    }
                case SoddNetworkMode.Broadcast:
                    PlayNotMeRpc(msg);
                    return SoddAudio.Net.PlayLocal(asset, position, attachTo, parameters);
                default:
                    return SoddAudio.Net.PlayLocal(asset, position, attachTo, parameters);
            }
        }

        [Rpc(SendTo.NotMe)]
        void PlayNotMeRpc(SoddNetMessage msg) => Receive(msg);

        [Rpc(SendTo.SpecifiedInParams)]
        void PlayTargetRpc(SoddNetMessage msg, RpcParams rpcParams = default) => Receive(msg);

        /// <summary>Reproduce un mensaje recibido. Público para tests.</summary>
        public static SoddEventInstance Receive(in SoddNetMessage msg)
        {
            var asset = SoddEvents.FindByHash(msg.EventHash);
            if (asset == null)
            {
                Debug.LogWarning($"[SoddDesign] Red: evento con hash {msg.EventHash:X8} no está en el registro de este cliente.");
                return default;
            }
            Transform attach = null;
            if (msg.HasAttach && msg.Attach.TryGet(out var no)) attach = no.transform;
            return SoddAudio.Net.PlayLocal(asset, msg.Position, attach, msg.Params);
        }
    }
}
#endif
