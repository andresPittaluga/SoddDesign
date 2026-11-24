using System;
using System.Collections.Generic;
using UnityEngine;

namespace Sodd.Design
{
    /// <summary>
    /// Banco de eventos: agrupa eventos para cargarlos/descargarlos juntos (ej. por nivel).
    /// Con <c>excludeFromRegistry</c> los eventos NO van al SoddRegistry de Resources (no se incluyen siempre en el build):
    /// se cargan con <see cref="SoddBanks.Load"/>, un <see cref="SoddBankLoader"/> o vía Addressables.
    /// </summary>
    [CreateAssetMenu(menuName = "Sodd/Sound Bank", fileName = "NewSoddBank")]
    public sealed class SoddBank : ScriptableObject
    {
        public string description;
        public SoddEventAsset[] events = Array.Empty<SoddEventAsset>();
        [Tooltip("Si está activo, estos eventos no se incluyen en el registro global (hay que cargar el banco).")]
        public bool excludeFromRegistry = true;
    }

    /// <summary>Carga/descarga de bancos con conteo de referencias.</summary>
    public static class SoddBanks
    {
        static readonly Dictionary<SoddBank, int> s_loaded = new Dictionary<SoddBank, int>();

        public static bool IsLoaded(SoddBank bank) => bank != null && s_loaded.ContainsKey(bank);

        public static void Load(SoddBank bank)
        {
            if (bank == null) return;
            s_loaded.TryGetValue(bank, out int n);
            s_loaded[bank] = n + 1;
            if (n > 0) return;
            foreach (var e in bank.events)
            {
                if (e == null) continue;
                SoddEvents.Register(e);
                foreach (var c in e.clips) if (c != null && c.loadState == AudioDataLoadState.Unloaded && !c.preloadAudioData) c.LoadAudioData();
            }
        }

        public static void Unload(SoddBank bank)
        {
            if (bank == null || !s_loaded.TryGetValue(bank, out int n)) return;
            if (n > 1) { s_loaded[bank] = n - 1; return; }
            s_loaded.Remove(bank);
            foreach (var e in bank.events)
            {
                if (e == null) continue;
                SoddAudio.Engine?.StopAllOf(e);
                SoddEvents.Unregister(e);
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => s_loaded.Clear();
    }
}
