#if SODD_ADDRESSABLES
using System;
using System.Collections.Generic;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace Sodd.Design
{
    /// <summary>
    /// Bancos vía Addressables: marcá el asset SoddBank como Addressable (los eventos y clips viajan como dependencias)
    /// y cargalo por address/label. <code>await SoddAddressableBanks.LoadAsync("Banks/Level1").Task;</code>
    /// </summary>
    public static class SoddAddressableBanks
    {
        static readonly Dictionary<string, AsyncOperationHandle<SoddBank>> s_handles = new Dictionary<string, AsyncOperationHandle<SoddBank>>();

        public static AsyncOperationHandle<SoddBank> LoadAsync(string address, Action<SoddBank> onLoaded = null)
        {
            if (s_handles.TryGetValue(address, out var existing))
            {
                if (existing.IsDone) onLoaded?.Invoke(existing.Result);
                else existing.Completed += h => onLoaded?.Invoke(h.Result);
                return existing;
            }
            var handle = Addressables.LoadAssetAsync<SoddBank>(address);
            s_handles[address] = handle;
            handle.Completed += h =>
            {
                if (h.Status == AsyncOperationStatus.Succeeded) { SoddBanks.Load(h.Result); onLoaded?.Invoke(h.Result); }
                else UnityEngine.Debug.LogError($"[SoddDesign] No se pudo cargar el banco Addressable '{address}'");
            };
            return handle;
        }

        public static void Unload(string address)
        {
            if (!s_handles.TryGetValue(address, out var h)) return;
            s_handles.Remove(address);
            if (h.IsDone && h.Status == AsyncOperationStatus.Succeeded) SoddBanks.Unload(h.Result);
            Addressables.Release(h);
        }
    }
}
#endif
