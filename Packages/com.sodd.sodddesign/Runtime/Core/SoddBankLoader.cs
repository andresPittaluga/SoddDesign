using UnityEngine;

namespace Sodd.Design
{
    /// <summary>Carga un banco mientras el objeto existe (ej. en la escena de un nivel).</summary>
    [AddComponentMenu("Sodd/Sodd Bank Loader")]
    public sealed class SoddBankLoader : MonoBehaviour
    {
        public SoddBank bank;
        void Awake() => SoddBanks.Load(bank);
        void OnDestroy() => SoddBanks.Unload(bank);
    }
}
