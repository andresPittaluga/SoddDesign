using System;
using System.Collections.Generic;

namespace Sodd.Design
{
    /// <summary>Catálogo de efectos disponibles por track, con sus parámetros, rangos y descripciones (usado por el reader, el schema, la UI y la IA).</summary>
    public static class SoddEffectCatalog
    {
        public sealed class ParamInfo
        {
            public string name, unit, description;
            public float defaultValue, min, max;
        }

        public sealed class EffectInfo
        {
            public string name, description;
            public bool native;
            public ParamInfo[] parameters;
            public ParamInfo FindParam(string n) { foreach (var p in parameters) if (p.name == n) return p; return null; }
            public IEnumerable<string> ParamNames { get { foreach (var p in parameters) yield return p.name; } }
        }

        static ParamInfo P(string n, float def, float min, float max, string unit, string desc) =>
            new ParamInfo { name = n, defaultValue = def, min = min, max = max, unit = unit, description = desc };

        public static readonly EffectInfo[] All =
        {
            new EffectInfo { name = "LowPass", native = true, description = "Filtro pasa-bajos: deja pasar los graves. Bajar 'cutoff' = más apagado/lejano/bajo el agua.", parameters = new[] {
                P("cutoff", 5000, 10, 22000, "Hz", "Frecuencia de corte"), P("resonance", 1, 1, 10, "Q", "Resonancia en el corte") } },
            new EffectInfo { name = "HighPass", native = true, description = "Filtro pasa-altos: quita graves. Subir 'cutoff' = más fino/telefónico.", parameters = new[] {
                P("cutoff", 500, 10, 22000, "Hz", "Frecuencia de corte"), P("resonance", 1, 1, 10, "Q", "Resonancia en el corte") } },
            new EffectInfo { name = "Echo", native = true, description = "Eco con realimentación (nativo de Unity).", parameters = new[] {
                P("delay", 500, 10, 5000, "ms", "Tiempo entre repeticiones"), P("decay", 0.5f, 0, 1, "ratio", "Cuánto baja cada repetición"),
                P("wet", 1, 0, 1, "linear", "Nivel de las repeticiones"), P("dry", 1, 0, 1, "linear", "Nivel de la señal original") } },
            new EffectInfo { name = "Reverb", native = true, description = "Reverb por voz (AudioReverbFilter). Para reverb compartida usar reverbSend + SoddReverbZone.", parameters = new[] {
                P("decayTime", 1.5f, 0.1f, 20, "s", "Duración de la cola"), P("room", -1000, -10000, 0, "mB", "Nivel de la reverb"),
                P("dryLevel", 0, -10000, 0, "mB", "Nivel de la señal original") } },
            new EffectInfo { name = "Distortion", native = true, description = "Distorsión nativa.", parameters = new[] {
                P("level", 0.5f, 0, 1, "linear", "Cantidad de distorsión") } },
            new EffectInfo { name = "Chorus", native = true, description = "Chorus nativo: engrosa y desafina levemente.", parameters = new[] {
                P("dryMix", 0.5f, 0, 1, "linear", "Señal original"), P("wetMix", 0.5f, 0, 1, "linear", "Señal procesada"),
                P("delay", 40, 0.1f, 100, "ms", "Retardo base"), P("rate", 0.8f, 0, 20, "Hz", "Velocidad de modulación"), P("depth", 0.03f, 0, 1, "linear", "Profundidad") } },
            new EffectInfo { name = "Bitcrusher", description = "Reduce bits y frecuencia de muestreo: sonido lo-fi, retro, roto.", parameters = new[] {
                P("bits", 8, 1, 16, "bits", "Resolución"), P("downsample", 4, 1, 64, "x", "Factor de reducción de sample rate"), P("mix", 1, 0, 1, "linear", "Mezcla procesado/original") } },
            new EffectInfo { name = "Tremolo", description = "Modula el volumen periódicamente (vibra, titila).", parameters = new[] {
                P("rate", 5, 0.01f, 40, "Hz", "Velocidad"), P("depth", 0.5f, 0, 1, "linear", "Profundidad"),
                P("shape", 1, 0, 1, "linear", "Forma: 0 triángulo, 1 seno"), P("skew", 0, -1, 1, "linear", "Corre el pico dentro de cada mitad del ciclo"),
                P("duty", 0.5f, 0, 1, "linear", "Parte del ciclo en la mitad fuerte"), P("square", 0, 0, 1, "linear", "Aplana hacia onda cuadrada"),
                P("phase", 0, 0, 1, "linear", "Desfase del inicio del ciclo") } },
            new EffectInfo { name = "ThreeEQ", description = "Ecualizador de 3 bandas (graves, medios, agudos) con cruces ajustables.", parameters = new[] {
                P("lowGain", 0, -80, 10, "dB", "Ganancia de graves"), P("midGain", 0, -80, 10, "dB", "Ganancia de medios"),
                P("highGain", 0, -80, 10, "dB", "Ganancia de agudos"), P("lowCrossover", 400, 10, 22000, "Hz", "Cruce graves/medios"),
                P("highCrossover", 4000, 10, 22000, "Hz", "Cruce medios/agudos"), P("slope", 24, 12, 48, "dB/oct", "Pendiente de los cruces (12, 24 o 48)") } },
            new EffectInfo { name = "AutoPan", description = "Mueve el sonido de izquierda a derecha periódicamente.", parameters = new[] {
                P("rate", 1, 0.01f, 20, "Hz", "Velocidad"), P("depth", 1, 0, 1, "linear", "Profundidad") } },
            new EffectInfo { name = "RingMod", description = "Modulador en anillo: metálico, robótico.", parameters = new[] {
                P("frequency", 440, 1, 5000, "Hz", "Frecuencia portadora"), P("mix", 0.5f, 0, 1, "linear", "Mezcla") } },
            new EffectInfo { name = "Saturator", description = "Saturación suave (tanh): calidez, presencia, 'que pegue más'.", parameters = new[] {
                P("drive", 2, 1, 20, "x", "Ganancia de entrada"), P("mix", 1, 0, 1, "linear", "Mezcla"), P("output", 0, -24, 12, "dB", "Ganancia de salida") } },
            new EffectInfo { name = "SimpleDelay", description = "Delay estéreo simple con feedback.", parameters = new[] {
                P("time", 0.3f, 0.01f, 2, "s", "Tiempo de delay"), P("feedback", 0.4f, 0, 0.95f, "ratio", "Realimentación"), P("mix", 0.3f, 0, 1, "linear", "Mezcla") } },
            new EffectInfo { name = "SimpleCompressor", description = "Compresor: controla picos y da punch.", parameters = new[] {
                P("threshold", -18, -60, 0, "dB", "Umbral"), P("ratio", 4, 1, 20, "x", "Relación"), P("attack", 10, 0.1f, 200, "ms", "Ataque"),
                P("release", 100, 5, 2000, "ms", "Relajación"), P("makeup", 0, 0, 24, "dB", "Ganancia de compensación") } },
        };

        public static IEnumerable<string> Names { get { foreach (var e in All) yield return e.name; } }

        public static EffectInfo Find(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            foreach (var e in All) if (string.Equals(e.name, name, StringComparison.OrdinalIgnoreCase)) return e;
            return null;
        }
    }
}
