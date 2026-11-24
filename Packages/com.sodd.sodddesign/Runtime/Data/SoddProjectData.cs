using System;

namespace Sodd.Design
{
    [Serializable]
    public class SoddBusDef
    {
        public string name;
        public string parent = "Master";
        public float volume;
        public bool mute;
        /// <summary>Nombre del grupo en SoddMixer.mixer al que se rutea (default: el mismo nombre si existe).</summary>
        public string mixerGroup;
    }

    [Serializable]
    public class SoddSnapshotBus
    {
        public string bus;
        public float volume;
        /// <summary>Corte del low-pass en Hz (22000 = sin filtro).</summary>
        public float lowpass = 22000f;
        public float highpass = 10f;
        public float reverb;
    }

    [Serializable]
    public class SoddSnapshotDef
    {
        public string name;
        public string description;
        /// <summary>Snapshot opcional de SoddMixer.mixer con el que se hace TransitionTo.</summary>
        public string mixerSnapshot;
        public SoddSnapshotBus[] buses = Array.Empty<SoddSnapshotBus>();
    }

    /// <summary>Contenido de sodd.settings.json.</summary>
    [Serializable]
    public class SoddSettingsData
    {
        public int maxVoices = 32;
        public int maxInstances = 128;
        public float lookaheadMs = 100f;
        public SoddBusDef[] buses = Array.Empty<SoddBusDef>();
        public SoddSnapshotDef[] snapshots = Array.Empty<SoddSnapshotDef>();

        public static SoddSettingsData CreateDefault()
        {
            return new SoddSettingsData
            {
                buses = new[]
                {
                    new SoddBusDef { name = "Master", parent = null },
                    new SoddBusDef { name = "Music" }, new SoddBusDef { name = "SFX" }, new SoddBusDef { name = "UI" },
                    new SoddBusDef { name = "Voice" }, new SoddBusDef { name = "Ambience" },
                }
            };
        }
    }

    /// <summary>Contenido de sodd.parameters.json.</summary>
    [Serializable]
    public class SoddParametersData
    {
        public SoddParameterDef[] parameters = Array.Empty<SoddParameterDef>();
    }
}
