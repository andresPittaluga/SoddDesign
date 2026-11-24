using UnityEngine;

namespace Sodd.Design
{
    /// <summary>Hasta 4 parámetros (hash del nombre + valor) para enviar por red. Struct: sin allocations.</summary>
    public struct SoddNetParams
    {
        public const int Max = 4;
        public byte Count;
        public uint Id0, Id1, Id2, Id3;
        public float V0, V1, V2, V3;

        public static uint IdOf(string name) => SoddText.Fnv1a(name);

        public SoddNetParams Add(string name, float value) => Add(IdOf(name), value);

        public SoddNetParams Add(uint id, float value)
        {
            switch (Count)
            {
                case 0: Id0 = id; V0 = value; break;
                case 1: Id1 = id; V1 = value; break;
                case 2: Id2 = id; V2 = value; break;
                case 3: Id3 = id; V3 = value; break;
                default: Debug.LogWarning("[SoddDesign] SoddNetParams: máximo 4 parámetros por mensaje"); return this;
            }
            Count++;
            return this;
        }

        public void Get(int i, out uint id, out float value)
        {
            switch (i)
            {
                case 0: id = Id0; value = V0; break;
                case 1: id = Id1; value = V1; break;
                case 2: id = Id2; value = V2; break;
                default: id = Id3; value = V3; break;
            }
        }

        /// <summary>Aplica los parámetros a una instancia (resuelve el hash contra locales y globales del evento).</summary>
        public void ApplyTo(SoddEventInstance inst)
        {
            for (int i = 0; i < Count && i < Max; i++)
            {
                Get(i, out uint id, out float v);
                inst.SetParameterByHash(id, v);
            }
        }
    }

    /// <summary>Implementación de red (la registra el assembly Sodd.Design.Netcode si NGO está instalado).</summary>
    public interface ISoddNetAudio
    {
        bool IsActive { get; }
        SoddEventInstance PlayOneShot(SoddEventAsset asset, Vector3 position, Transform attachTo, in SoddNetParams parameters, ulong ownerClientId);
    }

    public static class SoddNet
    {
        /// <summary>La setea SoddNetworkAudio al spawnear. Null = sin red (todo se reproduce local).</summary>
        public static ISoddNetAudio Handler;
    }
}
