using UnityEngine;

namespace Sodd.Design.Tests
{
    /// <summary>Helpers para tests: crear eventos desde JSON y un motor headless con reloj simulado.</summary>
    public static class TestUtil
    {
        public static SoddEventAsset MakeAsset(string json, SoddParameterDef[] known = null)
        {
            var data = SoddReader.ParseEvent(json, out var r, known);
            if (!r.ok) throw new System.Exception("Evento de test inválido: " + r);
            var a = ScriptableObject.CreateInstance<SoddEventAsset>();
            a.SetData(data, null);
            return a;
        }

        public static SoddEngine Headless(int voices = 8, int instances = 32)
        {
            var s = SoddSettingsData.CreateDefault();
            s.maxVoices = voices; s.maxInstances = instances;
            return new SoddEngine(s, null);
        }

        /// <summary>Avanza el motor en pasos de dt hasta el tiempo dsp indicado.</summary>
        public static double Run(SoddEngine e, double from, double to, float dt = 1f / 60f)
        {
            double t = from;
            while (t < to) { t += dt; e.Update(t, dt); }
            return t;
        }

        public static string Event(string id, string extra, string tracks) =>
            "{ \"id\": \"" + id + "\", \"path\": \"test/" + id + "\"" + (extra.Length > 0 ? ", " + extra : "") + ", \"tracks\": [" + tracks + "] }";

        public static string Track(string regions, string extra = "") => "{ \"name\": \"T\"" + (extra.Length > 0 ? ", " + extra : "") + ", \"regions\": [" + regions + "] }";
        public static string Region(float start, float length, string extra = "") =>
            "{ \"start\": " + start.ToString(System.Globalization.CultureInfo.InvariantCulture) + ", \"length\": " + length.ToString(System.Globalization.CultureInfo.InvariantCulture) +
            (extra.Length > 0 ? ", " + extra : "") + ", \"instrument\": { \"type\": \"SingleClip\", \"clip\": \"Assets/x.wav\" } }";
    }
}
