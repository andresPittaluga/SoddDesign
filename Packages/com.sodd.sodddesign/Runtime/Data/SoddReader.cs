using System;
using System.Collections.Generic;
using Sodd.Design.Json;
using UnityEngine;

namespace Sodd.Design
{
    /// <summary>
    /// Convierte JSON (.sodd.json, sodd.parameters.json, sodd.settings.json) al modelo de datos, validando
    /// con mensajes claros en español que incluyen la ruta JSON del problema y sugerencias.
    /// </summary>
    public sealed class SoddReader
    {
        public readonly SoddResult Result = new SoddResult();
        /// <summary>Parámetros globales conocidos (para validar referencias). Puede ser null.</summary>
        public IList<SoddParameterDef> KnownParameters;
        /// <summary>Si es true, las referencias a parámetros inexistentes son error (si no, warning).</summary>
        public bool StrictParameters = true;

        static readonly string[] EventKeys = { "$schema", "id", "path", "description", "tags", "is3D", "spatial", "bus", "priority", "maxInstances", "stealMode", "cooldown", "networkMode", "volume", "pitch", "parameters", "tracks", "loopRegions", "markers" };
        static readonly string[] SpatialKeys = { "spatialBlend", "minDistance", "maxDistance", "rolloff", "customRolloff", "doppler", "spread" };
        static readonly string[] TrackKeys = { "name", "type", "volume", "pan", "reverbSend", "mute", "solo", "bus", "effects", "regions" };
        static readonly string[] RegionKeys = { "name", "start", "length", "fadeIn", "fadeOut", "fadeInCurve", "fadeOutCurve", "instrument" };
        static readonly string[] InstrumentKeys = { "type", "clips", "clip", "avoidRepeat", "sequenceScope", "loop", "nestedEvent", "volume", "pitch", "startOffset" };
        static readonly string[] ClipKeys = { "guid", "path", "weight" };
        static readonly string[] ModPropKeys = { "base", "modulators" };
        static readonly string[] ModKeys = { "type", "min", "max", "unit", "perTrigger", "parameter", "curve", "range", "shape", "rate", "depth", "phase", "attack", "hold", "decay", "sustain", "release" };
        static readonly string[] ParamKeys = { "name", "description", "min", "max", "default", "seekSpeed", "scope" };
        static readonly string[] EffectKeys = { "type", "bypass", "params" };
        static readonly string[] LoopKeys = { "name", "start", "end", "exitWhen" };
        static readonly string[] CondKeys = { "parameter", "op", "value" };
        static readonly string[] MarkerKeys = { "name", "time" };

        readonly List<string> _localParams = new List<string>();

        // ------------------------------------------------------------------ helpers
        void Err(string path, string msg) => Result.Error($"{path}: {msg}");
        void Warn(string path, string msg) => Result.Warn($"{path}: {msg}");

        void CheckKeys(JsonNode o, string path, string[] allowed)
        {
            if (o == null || !o.IsObject) return;
            foreach (var kv in o.Props)
            {
                if (Array.IndexOf(allowed, kv.Key) >= 0) continue;
                Warn(path, $"Propiedad desconocida '{kv.Key}' (se ignora).{SoddText.DidYouMean(kv.Key, allowed)}");
            }
        }

        JsonNode Obj(JsonNode parent, string key, string path)
        {
            var n = parent?.Get(key);
            if (n == null || n.IsNull) return null;
            if (!n.IsObject) { Err($"{path}/{key}", "debe ser un objeto"); return null; }
            return n;
        }

        float Num(JsonNode parent, string key, string path, float def, float min = float.NegativeInfinity, float max = float.PositiveInfinity)
        {
            var n = parent?.Get(key);
            if (n == null || n.IsNull) return def;
            if (!n.IsNumber) { Err($"{path}/{key}", $"debe ser un número (vino {n.Kind})"); return def; }
            float v = (float)n.Number;
            if (v < min || v > max) { Warn($"{path}/{key}", $"valor {v} fuera de rango [{min}, {max}], se recorta"); v = Mathf.Clamp(v, min, max); }
            return v;
        }

        string Str(JsonNode parent, string key, string path, string def = null)
        {
            var n = parent?.Get(key);
            if (n == null || n.IsNull) return def;
            if (!n.IsString) { Err($"{path}/{key}", "debe ser un string"); return def; }
            return n.String;
        }

        bool Bool(JsonNode parent, string key, string path, bool def)
        {
            var n = parent?.Get(key);
            if (n == null || n.IsNull) return def;
            if (n.Kind != JsonKind.Bool) { Err($"{path}/{key}", "debe ser true o false"); return def; }
            return n.Bool;
        }

        T Enum<T>(JsonNode parent, string key, string path, T def) where T : struct
        {
            var s = Str(parent, key, path);
            if (s == null) return def;
            if (System.Enum.TryParse<T>(s, true, out var v) && System.Enum.IsDefined(typeof(T), v)) return v;
            Err($"{path}/{key}", $"valor '{s}' inválido. Opciones: {string.Join(", ", System.Enum.GetNames(typeof(T)))}.{SoddText.DidYouMean(s, System.Enum.GetNames(typeof(T)))}");
            return def;
        }

        JsonNode Arr(JsonNode parent, string key, string path)
        {
            var n = parent?.Get(key);
            if (n == null || n.IsNull) return null;
            if (!n.IsArray) { Err($"{path}/{key}", "debe ser un array"); return null; }
            return n;
        }

        // ------------------------------------------------------------------ curves & modulation
        public SoddCurve ReadCurve(JsonNode n, string path)
        {
            var c = new SoddCurve();
            if (n == null || n.IsNull) return c;
            if (!n.IsArray) { Err(path, "una curva es un array de puntos [[x,y], ...]"); return c; }
            var pts = new List<Vector2>();
            for (int i = 0; i < n.Count; i++)
            {
                var p = n[i];
                if (p.IsArray && p.Count == 2 && p[0].IsNumber && p[1].IsNumber) pts.Add(new Vector2((float)p[0].Number, (float)p[1].Number));
                else if (p.IsObject) pts.Add(new Vector2(p["x"]?.AsFloat() ?? 0, p["y"]?.AsFloat() ?? 0));
                else Err($"{path}/{i}", "cada punto debe ser [x, y]");
            }
            pts.Sort((a, b) => a.x.CompareTo(b.x));
            c.points = pts.ToArray();
            return c;
        }

        public SoddModProp ReadModProp(JsonNode parent, string key, string path, float def)
        {
            var n = parent?.Get(key);
            var p = new SoddModProp(def);
            string here = $"{path}/{key}";
            if (n == null || n.IsNull) return p;
            if (n.IsNumber) { p.@base = (float)n.Number; return p; }
            if (!n.IsObject) { Err(here, "debe ser un número o { \"base\": x, \"modulators\": [...] }"); return p; }
            CheckKeys(n, here, ModPropKeys);
            p.@base = Num(n, "base", here, def);
            var mods = Arr(n, "modulators", here);
            if (mods != null)
            {
                p.modulators = new SoddModulator[mods.Count];
                for (int i = 0; i < mods.Count; i++) p.modulators[i] = ReadModulator(mods[i], $"{here}/modulators/{i}");
            }
            return p;
        }

        SoddModulator ReadModulator(JsonNode n, string path)
        {
            var m = new SoddModulator();
            if (n == null || !n.IsObject) { Err(path, "un modulador debe ser un objeto con 'type'"); return m; }
            CheckKeys(n, path, ModKeys);
            if (!n.Has("type")) Err(path, $"falta 'type'. Opciones: {string.Join(", ", System.Enum.GetNames(typeof(SoddModulatorType)))}");
            m.type = Enum(n, "type", path, SoddModulatorType.Random);
            m.unit = Enum(n, "unit", path, SoddUnit.Auto);
            m.perTrigger = Bool(n, "perTrigger", path, true);
            m.min = Num(n, "min", path, 0f);
            m.max = Num(n, "max", path, 0f);
            if (n.Has("range")) { float r = Num(n, "range", path, 0f); m.min = -r; m.max = r; }
            m.parameter = Str(n, "parameter", path);
            m.curve = ReadCurve(n["curve"], path + "/curve");
            m.shape = Enum(n, "shape", path, SoddLfoShape.Sine);
            m.rate = Num(n, "rate", path, 1f, 0f, 100f);
            m.depth = Num(n, "depth", path, 1f);
            m.phase = Num(n, "phase", path, 0f);
            m.attack = Num(n, "attack", path, 0f, 0f);
            m.hold = Num(n, "hold", path, 0f, 0f);
            m.decay = Num(n, "decay", path, 0f, 0f);
            m.sustain = Num(n, "sustain", path, 1f, 0f, 1f);
            m.release = Num(n, "release", path, 0f, 0f);

            bool needsParam = m.type == SoddModulatorType.ParameterCurve || m.type == SoddModulatorType.RandomScaledByParameter;
            if (needsParam && string.IsNullOrEmpty(m.parameter)) Err(path, $"el modulador {m.type} necesita 'parameter'");
            if (!string.IsNullOrEmpty(m.parameter)) CheckParameterRef(m.parameter, path + "/parameter");
            if (needsParam && (m.curve.points == null || m.curve.points.Length == 0) && m.type == SoddModulatorType.ParameterCurve)
                Err(path, "ParameterCurve necesita 'curve' con al menos un punto [[x,y], ...]");
            return m;
        }

        void CheckParameterRef(string name, string path)
        {
            if (_localParams.Contains(name)) return;
            var names = new List<string>(_localParams);
            if (KnownParameters != null)
            {
                foreach (var p in KnownParameters) { if (p.name == name) return; names.Add(p.name); }
            }
            else if (!StrictParameters) return;
            string msg = $"El parámetro '{name}' no existe.{SoddText.DidYouMean(name, names)}";
            if (StrictParameters) Err(path, msg); else Warn(path, msg);
        }

        // ------------------------------------------------------------------ event
        public static SoddEventData ParseEvent(string json, out SoddResult result, IList<SoddParameterDef> knownParams = null)
        {
            var r = new SoddReader { KnownParameters = knownParams, StrictParameters = knownParams != null };
            result = r.Result;
            if (!SoddJson.TryParse(json, out var root, out var err)) { r.Result.Error(err); return null; }
            return r.ReadEvent(root);
        }

        public SoddEventData ReadEvent(JsonNode root)
        {
            const string P = "";
            var e = new SoddEventData();
            if (root == null || !root.IsObject) { Err("/", "el evento debe ser un objeto JSON"); return e; }
            CheckKeys(root, "/", EventKeys);

            e.id = Str(root, "id", P);
            if (string.IsNullOrEmpty(e.id)) Err("/id", "falta 'id' (GUID del evento)");
            e.path = Str(root, "path", P);
            if (string.IsNullOrEmpty(e.path)) Err("/path", "falta 'path' (ej. \"sfx/player/jump\")");
            e.description = Str(root, "description", P, "");
            var tags = Arr(root, "tags", P);
            if (tags != null) { e.tags = new string[tags.Count]; for (int i = 0; i < tags.Count; i++) e.tags[i] = tags[i].AsString(""); }
            e.is3D = Bool(root, "is3D", P, false);
            var sp = Obj(root, "spatial", P);
            if (sp != null)
            {
                CheckKeys(sp, "/spatial", SpatialKeys);
                e.spatial.spatialBlend = Num(sp, "spatialBlend", "/spatial", 1f, 0f, 1f);
                e.spatial.minDistance = Num(sp, "minDistance", "/spatial", 1f, 0f);
                e.spatial.maxDistance = Num(sp, "maxDistance", "/spatial", 50f, 0f);
                e.spatial.rolloff = Enum(sp, "rolloff", "/spatial", SoddRolloff.Logarithmic);
                e.spatial.customRolloff = ReadCurve(sp["customRolloff"], "/spatial/customRolloff");
                e.spatial.doppler = Num(sp, "doppler", "/spatial", 1f, 0f, 5f);
                e.spatial.spread = Num(sp, "spread", "/spatial", 0f, 0f, 360f);
                if (e.spatial.maxDistance < e.spatial.minDistance) Err("/spatial", "maxDistance debe ser >= minDistance");
                if (e.spatial.rolloff == SoddRolloff.Custom && e.spatial.customRolloff.points.Length == 0) Warn("/spatial", "rolloff Custom sin 'customRolloff': se usa Logarithmic");
            }
            e.bus = Str(root, "bus", P, "SFX");
            e.priority = (int)Num(root, "priority", P, 128, 0, 256);
            e.maxInstances = (int)Num(root, "maxInstances", P, 0, 0, 1024);
            e.stealMode = Enum(root, "stealMode", P, SoddStealMode.Oldest);
            e.cooldown = Num(root, "cooldown", P, 0f, 0f);
            e.networkMode = Enum(root, "networkMode", P, SoddNetworkMode.Local);

            // Parámetros locales primero: los moduladores pueden referenciarlos.
            var ps = Arr(root, "parameters", P);
            var plist = new List<SoddParameterDef>();
            if (ps != null)
                for (int i = 0; i < ps.Count; i++)
                {
                    var pd = ReadParameter(ps[i], $"/parameters/{i}", true);
                    if (pd == null) continue;
                    if (_localParams.Contains(pd.name)) Err($"/parameters/{i}", $"parámetro '{pd.name}' duplicado");
                    _localParams.Add(pd.name); plist.Add(pd);
                }
            e.parameters = plist.ToArray();

            e.volume = ReadModProp(root, "volume", P, 0f);
            e.pitch = ReadModProp(root, "pitch", P, 0f);

            var tr = Arr(root, "tracks", P);
            if (tr != null)
            {
                e.tracks = new SoddTrack[tr.Count];
                for (int i = 0; i < tr.Count; i++) e.tracks[i] = ReadTrack(tr[i], $"/tracks/{i}");
            }
            else Warn("/tracks", "el evento no tiene tracks: no va a sonar nada");

            var lr = Arr(root, "loopRegions", P);
            if (lr != null)
            {
                e.loopRegions = new SoddLoopRegion[lr.Count];
                for (int i = 0; i < lr.Count; i++)
                {
                    string lp = $"/loopRegions/{i}";
                    var n = lr[i]; var l = new SoddLoopRegion();
                    CheckKeys(n, lp, LoopKeys);
                    l.name = Str(n, "name", lp, "Loop");
                    l.start = Num(n, "start", lp, 0f, 0f);
                    l.end = Num(n, "end", lp, 1f, 0f);
                    if (l.end - l.start < 0.01f) Err(lp, "end debe ser mayor que start (mínimo 10 ms)");
                    var c = Obj(n, "exitWhen", lp);
                    if (c != null)
                    {
                        CheckKeys(c, lp + "/exitWhen", CondKeys);
                        l.exitWhen.parameter = Str(c, "parameter", lp + "/exitWhen");
                        l.exitWhen.op = Enum(c, "op", lp + "/exitWhen", SoddCompareOp.Greater);
                        l.exitWhen.value = Num(c, "value", lp + "/exitWhen", 0f);
                        if (!string.IsNullOrEmpty(l.exitWhen.parameter)) CheckParameterRef(l.exitWhen.parameter, lp + "/exitWhen/parameter");
                    }
                    e.loopRegions[i] = l;
                }
            }

            var mk = Arr(root, "markers", P);
            if (mk != null)
            {
                e.markers = new SoddMarker[mk.Count];
                for (int i = 0; i < mk.Count; i++)
                {
                    CheckKeys(mk[i], $"/markers/{i}", MarkerKeys);
                    e.markers[i] = new SoddMarker { name = Str(mk[i], "name", $"/markers/{i}", "Marker"), time = Num(mk[i], "time", $"/markers/{i}", 0f, 0f) };
                }
            }
            return e;
        }

        public SoddParameterDef ReadParameter(JsonNode n, string path, bool local)
        {
            if (n == null || !n.IsObject) { Err(path, "un parámetro debe ser un objeto"); return null; }
            CheckKeys(n, path, ParamKeys);
            var p = new SoddParameterDef
            {
                name = Str(n, "name", path),
                description = Str(n, "description", path, ""),
                min = Num(n, "min", path, 0f),
                max = Num(n, "max", path, 1f),
                seekSpeed = Num(n, "seekSpeed", path, 0f, 0f),
            };
            p.defaultValue = Num(n, "default", path, p.min);
            string scope = Str(n, "scope", path, local ? "local" : "global");
            p.isGlobal = scope == "global";
            if (scope != "global" && scope != "local") Err(path + "/scope", "debe ser 'global' o 'local'");
            if (string.IsNullOrEmpty(p.name)) { Err(path, "falta 'name'"); return null; }
            if (p.max <= p.min) Err(path, $"max ({p.max}) debe ser mayor que min ({p.min})");
            if (p.defaultValue < p.min || p.defaultValue > p.max) Warn(path, "default fuera de [min, max]");
            return p;
        }

        SoddTrack ReadTrack(JsonNode n, string path)
        {
            var t = new SoddTrack();
            if (n == null || !n.IsObject) { Err(path, "un track debe ser un objeto"); return t; }
            CheckKeys(n, path, TrackKeys);
            t.name = Str(n, "name", path, "Track");
            t.type = Enum(n, "type", path, SoddTrackType.SFX);
            t.volume = ReadModProp(n, "volume", path, 0f);
            t.pan = ReadModProp(n, "pan", path, 0f);
            t.reverbSend = ReadModProp(n, "reverbSend", path, 0f);
            t.mute = Bool(n, "mute", path, false);
            t.solo = Bool(n, "solo", path, false);
            t.bus = Str(n, "bus", path);
            var fx = Arr(n, "effects", path);
            if (fx != null)
            {
                t.effects = new SoddEffect[fx.Count];
                for (int i = 0; i < fx.Count; i++) t.effects[i] = ReadEffect(fx[i], $"{path}/effects/{i}");
            }
            var rg = Arr(n, "regions", path);
            if (rg != null)
            {
                t.regions = new SoddRegion[rg.Count];
                for (int i = 0; i < rg.Count; i++) t.regions[i] = ReadRegion(rg[i], $"{path}/regions/{i}");
            }
            return t;
        }

        SoddEffect ReadEffect(JsonNode n, string path)
        {
            var fx = new SoddEffect();
            if (n == null || !n.IsObject) { Err(path, "un efecto debe ser un objeto"); return fx; }
            CheckKeys(n, path, EffectKeys);
            fx.type = Str(n, "type", path, "");
            var info = SoddEffectCatalog.Find(fx.type);
            if (info == null) { Err(path + "/type", $"efecto '{fx.type}' desconocido.{SoddText.DidYouMean(fx.type, SoddEffectCatalog.Names)}"); return fx; }
            fx.type = info.name;
            fx.bypass = Bool(n, "bypass", path, false);
            var ps = Obj(n, "params", path);
            var list = new List<SoddEffectParam>();
            foreach (var pi in info.parameters)
                list.Add(new SoddEffectParam { name = pi.name, value = ReadModProp(ps, pi.name, path + "/params", pi.defaultValue) });
            if (ps != null)
                foreach (var kv in ps.Props)
                    if (info.FindParam(kv.Key) == null)
                        Warn($"{path}/params/{kv.Key}", $"'{fx.type}' no tiene el parámetro '{kv.Key}'.{SoddText.DidYouMean(kv.Key, info.ParamNames)}");
            fx.parameters = list.ToArray();
            return fx;
        }

        SoddRegion ReadRegion(JsonNode n, string path)
        {
            var r = new SoddRegion();
            if (n == null || !n.IsObject) { Err(path, "una región debe ser un objeto"); return r; }
            CheckKeys(n, path, RegionKeys);
            r.name = Str(n, "name", path, "");
            r.start = Num(n, "start", path, 0f, 0f);
            r.length = Num(n, "length", path, 1f, 0.001f);
            r.fadeIn = Num(n, "fadeIn", path, 0f, 0f);
            r.fadeOut = Num(n, "fadeOut", path, 0f, 0f);
            if (r.fadeIn + r.fadeOut > r.length + 1e-4f) Warn(path, "fadeIn + fadeOut es mayor que length");
            r.fadeInCurve = Enum(n, "fadeInCurve", path, SoddFadeCurve.Linear);
            r.fadeOutCurve = Enum(n, "fadeOutCurve", path, SoddFadeCurve.Linear);
            var ins = Obj(n, "instrument", path);
            if (ins == null) { Err(path, "falta 'instrument'"); return r; }
            string ip = path + "/instrument";
            CheckKeys(ins, ip, InstrumentKeys);
            var i = r.instrument;
            i.type = Enum(ins, "type", ip, SoddInstrumentType.SingleClip);
            i.avoidRepeat = Bool(ins, "avoidRepeat", ip, true);
            i.sequenceScope = Enum(ins, "sequenceScope", ip, SoddSequenceScope.Global);
            i.loop = Bool(ins, "loop", ip, false);
            i.nestedEvent = Str(ins, "nestedEvent", ip);
            i.volume = ReadModProp(ins, "volume", ip, 0f);
            i.pitch = ReadModProp(ins, "pitch", ip, 0f);
            i.startOffset = ReadModProp(ins, "startOffset", ip, 0f);
            var clips = new List<SoddClipRef>();
            var single = ins.Get("clip");
            if (single != null && !single.IsNull) clips.Add(ReadClip(single, ip + "/clip"));
            var arr = Arr(ins, "clips", ip);
            if (arr != null) for (int k = 0; k < arr.Count; k++) clips.Add(ReadClip(arr[k], $"{ip}/clips/{k}"));
            i.clips = clips.ToArray();
            switch (i.type)
            {
                case SoddInstrumentType.SingleClip:
                    if (i.clips.Length == 0) Err(ip, "SingleClip necesita 'clip' o 'clips' con un elemento");
                    if (i.clips.Length > 1) Warn(ip, "SingleClip con varios clips: solo se usa el primero (¿querías RandomClip?)");
                    break;
                case SoddInstrumentType.RandomClip:
                case SoddInstrumentType.Sequential:
                    if (i.clips.Length == 0) Err(ip, $"{i.type} necesita al menos un clip en 'clips'");
                    break;
                case SoddInstrumentType.NestedEvent:
                    if (string.IsNullOrEmpty(i.nestedEvent)) Err(ip, "NestedEvent necesita 'nestedEvent' (id o path del evento)");
                    break;
            }
            return r;
        }

        SoddClipRef ReadClip(JsonNode n, string path)
        {
            var c = new SoddClipRef();
            if (n == null) return c;
            if (n.IsString) { c.path = n.String; return c; }
            if (!n.IsObject) { Err(path, "un clip es { \"guid\": ..., \"path\": ... }"); return c; }
            CheckKeys(n, path, ClipKeys);
            c.guid = Str(n, "guid", path);
            c.path = Str(n, "path", path);
            c.weight = Num(n, "weight", path, 1f, 0f);
            if (string.IsNullOrEmpty(c.guid) && string.IsNullOrEmpty(c.path)) Err(path, "el clip necesita 'guid' o 'path'");
            return c;
        }

        // ------------------------------------------------------------------ project files
        public static SoddParametersData ParseParameters(string json, out SoddResult result)
        {
            var r = new SoddReader();
            result = r.Result;
            var data = new SoddParametersData();
            if (!SoddJson.TryParse(json, out var root, out var err)) { r.Result.Error(err); return data; }
            var ps = r.Arr(root, "parameters", "");
            if (ps == null) return data;
            var list = new List<SoddParameterDef>();
            for (int i = 0; i < ps.Count; i++)
            {
                var p = r.ReadParameter(ps[i], $"/parameters/{i}", false);
                if (p == null) continue;
                if (list.Exists(x => x.name == p.name)) r.Err($"/parameters/{i}", $"parámetro '{p.name}' duplicado");
                list.Add(p);
            }
            data.parameters = list.ToArray();
            return data;
        }

        public static SoddSettingsData ParseSettings(string json, out SoddResult result)
        {
            var r = new SoddReader();
            result = r.Result;
            var s = SoddSettingsData.CreateDefault();
            if (!SoddJson.TryParse(json, out var root, out var err)) { r.Result.Error(err); return s; }
            s.maxVoices = (int)r.Num(root, "maxVoices", "", 32, 1, 255);
            s.maxInstances = (int)r.Num(root, "maxInstances", "", 128, 1, 4096);
            s.lookaheadMs = r.Num(root, "lookaheadMs", "", 100f, 20f, 1000f);
            var bs = r.Arr(root, "buses", "");
            if (bs != null)
            {
                var list = new List<SoddBusDef>();
                for (int i = 0; i < bs.Count; i++)
                {
                    string bp = $"/buses/{i}";
                    var n = bs[i];
                    var b = new SoddBusDef
                    {
                        name = r.Str(n, "name", bp),
                        parent = r.Str(n, "parent", bp, "Master"),
                        volume = r.Num(n, "volume", bp, 0f, -80f, 24f),
                        mute = r.Bool(n, "mute", bp, false),
                        mixerGroup = r.Str(n, "mixerGroup", bp),
                    };
                    if (string.IsNullOrEmpty(b.name)) { r.Err(bp, "falta 'name'"); continue; }
                    if (b.name == "Master") b.parent = null;
                    list.Add(b);
                }
                if (!list.Exists(b => b.name == "Master")) list.Insert(0, new SoddBusDef { name = "Master", parent = null });
                s.buses = list.ToArray();
            }
            var ss = r.Arr(root, "snapshots", "");
            if (ss != null)
            {
                s.snapshots = new SoddSnapshotDef[ss.Count];
                for (int i = 0; i < ss.Count; i++)
                {
                    string sp = $"/snapshots/{i}";
                    var n = ss[i];
                    var d = new SoddSnapshotDef { name = r.Str(n, "name", sp), description = r.Str(n, "description", sp, ""), mixerSnapshot = r.Str(n, "mixerSnapshot", sp) };
                    var buses = r.Obj(n, "buses", sp);
                    var list = new List<SoddSnapshotBus>();
                    if (buses != null)
                        foreach (var kv in buses.Props)
                        {
                            string bp = $"{sp}/buses/{kv.Key}";
                            list.Add(new SoddSnapshotBus
                            {
                                bus = kv.Key,
                                volume = r.Num(kv.Value, "volume", bp, 0f, -80f, 24f),
                                lowpass = r.Num(kv.Value, "lowpass", bp, 22000f, 10f, 22000f),
                                highpass = r.Num(kv.Value, "highpass", bp, 10f, 10f, 22000f),
                                reverb = r.Num(kv.Value, "reverb", bp, 0f, 0f, 1f),
                            });
                        }
                    d.buses = list.ToArray();
                    s.snapshots[i] = d;
                }
            }
            return s;
        }
    }
}
