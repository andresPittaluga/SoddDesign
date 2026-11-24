using System;
using System.Collections.Generic;
using System.Text;

namespace Sodd.Design
{
    /// <summary>Resultado estándar de validación/comandos: { ok, errors[], warnings[] }.</summary>
    [Serializable]
    public class SoddResult
    {
        public List<string> errors = new List<string>();
        public List<string> warnings = new List<string>();
        /// <summary>Dato opcional devuelto por el comando (ej. id creado), como JSON.</summary>
        public string data;

        public bool ok => errors.Count == 0;

        public SoddResult Error(string msg) { errors.Add(msg); return this; }
        public SoddResult Warn(string msg) { warnings.Add(msg); return this; }

        public void Merge(SoddResult other)
        {
            if (other == null) return;
            errors.AddRange(other.errors);
            warnings.AddRange(other.warnings);
        }

        public static SoddResult Ok(string data = null) => new SoddResult { data = data };
        public static SoddResult Fail(string msg) => new SoddResult().Error(msg);

        public override string ToString()
        {
            var sb = new StringBuilder(ok ? "OK" : "ERROR");
            foreach (var e in errors) sb.Append("\n  ✖ ").Append(e);
            foreach (var w in warnings) sb.Append("\n  ⚠ ").Append(w);
            return sb.ToString();
        }
    }

    /// <summary>Utilidades de texto: sugerencias "¿Quisiste decir...?".</summary>
    public static class SoddText
    {
        public static int Levenshtein(string a, string b)
        {
            a = a.ToLowerInvariant(); b = b.ToLowerInvariant();
            var d = new int[a.Length + 1, b.Length + 1];
            for (int i = 0; i <= a.Length; i++) d[i, 0] = i;
            for (int j = 0; j <= b.Length; j++) d[0, j] = j;
            for (int i = 1; i <= a.Length; i++)
                for (int j = 1; j <= b.Length; j++)
                    d[i, j] = Math.Min(Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1), d[i - 1, j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1));
            return d[a.Length, b.Length];
        }

        // Sinónimos comunes en español → nombre técnico (para que la sugerencia acierte aunque la distancia sea grande).
        static readonly Dictionary<string, string> Synonyms = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "vida", "health" }, { "salud", "health" }, { "velocidad", "speed" }, { "volumen", "volume" },
            { "tono", "pitch" }, { "altura", "pitch" }, { "paneo", "pan" }, { "pistas", "tracks" }, { "regiones", "regions" },
            { "marcadores", "markers" }, { "parametros", "parameters" }, { "parámetros", "parameters" }, { "efectos", "effects" },
            { "inicio", "start" }, { "duracion", "length" }, { "duración", "length" }, { "descripcion", "description" },
            { "prioridad", "priority" }, { "distancia", "distance" }, { "lluvia", "rain" }, { "motor", "engine" },
        };

        /// <summary>Devuelve el candidato más parecido o null si ninguno es razonable.</summary>
        public static string Suggest(string input, IEnumerable<string> candidates)
        {
            if (string.IsNullOrEmpty(input) || candidates == null) return null;
            string probe = Synonyms.TryGetValue(input, out var syn) ? syn : input;
            string best = null; int bestD = int.MaxValue;
            foreach (var c in candidates)
            {
                if (string.IsNullOrEmpty(c)) continue;
                if (string.Equals(c, probe, StringComparison.OrdinalIgnoreCase)) return c;
                int dist = Levenshtein(probe, c);
                if (c.IndexOf(probe, StringComparison.OrdinalIgnoreCase) >= 0 || probe.IndexOf(c, StringComparison.OrdinalIgnoreCase) >= 0) dist = Math.Min(dist, 2);
                if (dist < bestD) { bestD = dist; best = c; }
            }
            return best != null && bestD <= Math.Max(2, probe.Length / 2) ? best : null;
        }

        public static string DidYouMean(string input, IEnumerable<string> candidates)
        {
            var s = Suggest(input, candidates);
            return s == null ? "" : $" ¿Quisiste decir '{s}'?";
        }

        /// <summary>Hash FNV-1a de 32 bits (estable entre plataformas; usado para ids de red).</summary>
        public static uint Fnv1a(string s)
        {
            uint h = 2166136261;
            if (s == null) return h;
            for (int i = 0; i < s.Length; i++) { h ^= s[i]; h *= 16777619; }
            return h;
        }
    }
}
