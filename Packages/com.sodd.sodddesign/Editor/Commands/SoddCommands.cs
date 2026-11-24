using System;
using System.Collections.Generic;
using System.IO;
using Sodd.Design.Json;
using UnityEditor;
using UnityEngine;

namespace Sodd.Design.Editor
{
    /// <summary>
    /// Capa ÚNICA de edición de datos de SoddDesign. La usan el Studio, el bridge MCP y los tests.
    /// Cada comando: localiza el JSON, lo modifica, normaliza clips, valida, y lo escribe con Undo.
    /// Si la validación da errores, NO se escribe nada. Siempre devuelve { ok, errors[], warnings[], data }.
    /// Los eventos se identifican por id (GUID) o por path ("sfx/player/jump").
    /// </summary>
    public static class SoddCommands
    {
        public const string ParametersKey = "__sodd_parameters";
        public const string SettingsKey = "__sodd_settings";

        /// <summary>Se dispara después de cada comando que modifica datos (la UI se refresca).</summary>
        public static event Action<string> Changed;

        // ================================================================== localización
        public static string LocateFile(string evt)
        {
            if (string.IsNullOrEmpty(evt)) return null;
            if (evt.EndsWith(SoddProjectFiles.EventExt, StringComparison.OrdinalIgnoreCase) && File.Exists(evt)) return evt.Replace('\\', '/');
            foreach (var a in SoddProjectFiles.AllEventAssets())
                if (a.data != null && (string.Equals(a.data.id, evt, StringComparison.OrdinalIgnoreCase) || string.Equals(a.data.path, evt, StringComparison.OrdinalIgnoreCase)))
                    return AssetDatabase.GetAssetPath(a);
            // Fallback (eventos con JSON inválido que no importaron): buscar el texto.
            foreach (var f in SoddProjectFiles.AllEventFiles())
            {
                if (!SoddJson.TryParse(File.ReadAllText(f), out var n, out _)) continue;
                if (string.Equals(n["id"]?.AsString(), evt, StringComparison.OrdinalIgnoreCase) || string.Equals(n["path"]?.AsString(), evt, StringComparison.OrdinalIgnoreCase)) return f;
            }
            return null;
        }

        static IEnumerable<string> AllEventPaths()
        {
            foreach (var a in SoddProjectFiles.AllEventAssets()) if (a.data != null) yield return a.data.path;
        }

        static SoddResult NotFound(string evt) => SoddResult.Fail($"Evento '{evt}' no encontrado.{SoddText.DidYouMean(evt, AllEventPaths())}");

        public static string FileForPath(string eventPath) => SoddProjectFiles.EventsFolder + "/" + eventPath + SoddProjectFiles.EventExt;

        // ================================================================== núcleo de edición
        static SoddResult Edit(string evt, string undoName, Func<JsonNode, SoddResult> mutate)
        {
            string file = LocateFile(evt);
            if (file == null) return NotFound(evt);
            if (!SoddJson.TryParse(File.ReadAllText(file), out var dom, out var perr)) return SoddResult.Fail($"{file}: {perr}");
            var r = mutate(dom) ?? new SoddResult();
            if (!r.ok) return r;
            return Commit(dom, file, file, undoName, r);
        }

        static SoddResult Commit(JsonNode dom, string currentFile, string newFile, string undoName, SoddResult r)
        {
            NormalizeClips(dom, r);
            r.Merge(ValidateDom(dom));
            if (!r.ok) return r;
            string key = dom["id"]?.AsString() ?? newFile;
            SoddUndo.Change(key, currentFile, newFile, SoddJson.Write(dom), undoName);
            Changed?.Invoke(key);
            return r;
        }

        /// <summary>Clips: acepta "path" string o {guid|path}; completa guid y path para que IA y Unity se entiendan.</summary>
        static void NormalizeClips(JsonNode dom, SoddResult r)
        {
            var tracks = dom["tracks"];
            if (tracks == null || !tracks.IsArray) return;
            for (int t = 0; t < tracks.Count; t++)
            {
                var regions = tracks[t]["regions"];
                if (regions == null || !regions.IsArray) continue;
                for (int g = 0; g < regions.Count; g++)
                {
                    var ins = regions[g]["instrument"];
                    if (ins == null || !ins.IsObject) continue;
                    var single = ins["clip"];
                    if (single != null && !single.IsNull) ins.Set("clip", NormalizeClip(single, $"/tracks/{t}/regions/{g}/instrument/clip", r));
                    var clips = ins["clips"];
                    if (clips != null && clips.IsArray)
                        for (int c = 0; c < clips.Count; c++) clips.Items[c] = NormalizeClip(clips[c], $"/tracks/{t}/regions/{g}/instrument/clips/{c}", r);
                }
            }
        }

        static JsonNode NormalizeClip(JsonNode n, string where, SoddResult r)
        {
            string guid = null, path = null;
            JsonNode weight = null;
            if (n.IsString) path = n.String;
            else if (n.IsObject) { guid = n["guid"]?.AsString(); path = n["path"]?.AsString(); weight = n["weight"]; }
            else return n;
            if (!string.IsNullOrEmpty(guid))
            {
                var p = AssetDatabase.GUIDToAssetPath(guid);
                if (!string.IsNullOrEmpty(p)) path = p;
            }
            if (!string.IsNullOrEmpty(path) && AssetDatabase.LoadAssetAtPath<AudioClip>(path) == null)
            {
                // Permitir nombres cortos ("kick" o "kick.wav") si son únicos.
                string match = FindClipByName(path);
                if (match != null) path = match;
                else { r.Error($"{where}: AudioClip '{path}' no existe.{SoddText.DidYouMean(Path.GetFileNameWithoutExtension(path), SoddProjectFiles.AudioClipNames())}"); return n; }
            }
            if (string.IsNullOrEmpty(path)) { r.Error($"{where}: el clip necesita 'guid' o 'path'"); return n; }
            var o = JsonNode.NewObject();
            o.Set("guid", AssetDatabase.AssetPathToGUID(path));
            o.Set("path", path);
            if (weight != null) o.Set("weight", weight);
            return o;
        }

        static string FindClipByName(string nameOrPath)
        {
            string name = Path.GetFileNameWithoutExtension(nameOrPath);
            string found = null;
            foreach (var g in AssetDatabase.FindAssets(name + " t:AudioClip"))
            {
                var p = AssetDatabase.GUIDToAssetPath(g);
                if (!string.Equals(Path.GetFileNameWithoutExtension(p), name, StringComparison.OrdinalIgnoreCase)) continue;
                if (found != null) return null; // ambiguo
                found = p;
            }
            return found;
        }

        // ================================================================== validación
        public static SoddResult ValidateDom(JsonNode dom)
        {
            var reader = new SoddReader { KnownParameters = SoddProjectFiles.LoadParameters()?.parameters ?? Array.Empty<SoddParameterDef>(), StrictParameters = true };
            var data = reader.ReadEvent(dom);
            var r = reader.Result;
            var settings = SoddProjectFiles.LoadSettings();
            var busNames = new List<string>();
            foreach (var b in settings.buses) busNames.Add(b.name);
            if (!string.IsNullOrEmpty(data.bus) && !busNames.Contains(data.bus)) r.Error($"/bus: el bus '{data.bus}' no existe.{SoddText.DidYouMean(data.bus, busNames)}");
            for (int t = 0; t < data.tracks.Length; t++)
            {
                var tr = data.tracks[t];
                if (!string.IsNullOrEmpty(tr.bus) && !busNames.Contains(tr.bus)) r.Error($"/tracks/{t}/bus: el bus '{tr.bus}' no existe.{SoddText.DidYouMean(tr.bus, busNames)}");
                for (int g = 0; g < tr.regions.Length; g++)
                {
                    var ins = tr.regions[g].instrument;
                    if (ins.type == SoddInstrumentType.NestedEvent && !string.IsNullOrEmpty(ins.nestedEvent) && LocateFile(ins.nestedEvent) == null)
                        r.Error($"/tracks/{t}/regions/{g}/instrument/nestedEvent: el evento '{ins.nestedEvent}' no existe.{SoddText.DidYouMean(ins.nestedEvent, AllEventPaths())}");
                    if (ins.type == SoddInstrumentType.NestedEvent && (ins.nestedEvent == data.id || ins.nestedEvent == data.path))
                        r.Error($"/tracks/{t}/regions/{g}/instrument/nestedEvent: un evento no puede anidarse a sí mismo");
                }
            }
            foreach (var l in data.loopRegions)
                if (l.end > data.Length + 1e-3f) r.Warn($"el loop '{l.name}' termina después de la última región");
            return r;
        }

        /// <summary>Valida un evento (o todos si evt es null). Incluye ids/paths duplicados.</summary>
        public static SoddResult Validate(string evt = null)
        {
            var r = new SoddResult();
            var files = evt != null ? new List<string> { LocateFile(evt) } : SoddProjectFiles.AllEventFiles();
            if (evt != null && files[0] == null) return NotFound(evt);
            var ids = new Dictionary<string, string>(); var paths = new Dictionary<string, string>();
            foreach (var f in SoddProjectFiles.AllEventFiles())
            {
                if (!SoddJson.TryParse(File.ReadAllText(f), out var n, out _)) continue;
                string id = n["id"]?.AsString(), p = n["path"]?.AsString();
                if (id != null) { if (ids.TryGetValue(id, out var other)) r.Error($"id duplicado '{id}' en {f} y {other}"); else ids[id] = f; }
                if (p != null) { if (paths.TryGetValue(p, out var other)) r.Error($"path duplicado '{p}' en {f} y {other}"); else paths[p] = f; }
            }
            foreach (var f in files)
            {
                if (!SoddJson.TryParse(File.ReadAllText(f), out var dom, out var err)) { r.Error($"{f}: {err}"); continue; }
                var v = ValidateDom(dom);
                foreach (var e in v.errors) r.Error($"{f}{e}");
                foreach (var w in v.warnings) r.Warn($"{f}{w}");
            }
            r.data = SoddJson.Write(JsonNode.From(files.Count), false);
            return r;
        }

        // ================================================================== eventos
        static JsonNode Template(string id, string path, string description, bool is3D, string bus)
        {
            var o = JsonNode.NewObject();
            o.Set("$schema", string.Concat(System.Linq.Enumerable.Repeat("../", path.Split('/').Length + 2)) + "Packages/com.sodd.sodddesign/Schema/sodd.event.schema.json");
            o.Set("id", id); o.Set("path", path); o.Set("description", description ?? "");
            o.Set("tags", JsonNode.NewArray());
            o.Set("is3D", is3D);
            if (is3D) { var sp = JsonNode.NewObject(); sp.Set("minDistance", 1); sp.Set("maxDistance", 30); o.Set("spatial", sp); }
            o.Set("bus", bus ?? "SFX");
            o.Set("tracks", JsonNode.NewArray());
            return o;
        }

        static string NormalizeEventPath(string p) => (p ?? "").Trim().Trim('/').Replace('\\', '/').Replace(' ', '_');

        public static SoddResult CreateEvent(string path, string description = "", bool is3D = false, string bus = null)
        {
            path = NormalizeEventPath(path);
            if (string.IsNullOrEmpty(path)) return SoddResult.Fail("Falta el path (ej. \"sfx/player/jump\")");
            if (LocateFile(path) != null) return SoddResult.Fail($"Ya existe un evento con path '{path}'");
            string id = Guid.NewGuid().ToString();
            var dom = Template(id, path, description, is3D, bus);
            string file = FileForPath(path);
            var r = Commit(dom, null, file, "Crear evento " + path, new SoddResult());
            if (r.ok) r.data = $"{{\"id\":\"{id}\",\"path\":\"{path}\",\"file\":\"{file}\"}}";
            return r;
        }

        /// <summary>Crea un evento a partir de un JSON completo (lo usa la IA). Si falta id, se genera.</summary>
        public static SoddResult CreateEventFromJson(string json)
        {
            if (!SoddJson.TryParse(json, out var dom, out var err)) return SoddResult.Fail(err);
            if (!dom.IsObject) return SoddResult.Fail("El evento debe ser un objeto JSON");
            string path = NormalizeEventPath(dom["path"]?.AsString());
            if (string.IsNullOrEmpty(path)) return SoddResult.Fail("Falta 'path'");
            if (LocateFile(path) != null) return SoddResult.Fail($"Ya existe un evento con path '{path}'");
            if (string.IsNullOrEmpty(dom["id"]?.AsString())) dom.Set("id", Guid.NewGuid().ToString());
            dom.Set("path", path);
            if (dom["tracks"] == null) dom.Set("tracks", JsonNode.NewArray());
            string file = FileForPath(path);
            var r = Commit(dom, null, file, "Crear evento " + path, new SoddResult());
            if (r.ok) r.data = $"{{\"id\":\"{dom["id"].String}\",\"path\":\"{path}\",\"file\":\"{file}\"}}";
            return r;
        }

        public static SoddResult DuplicateEvent(string evt, string newPath = null)
        {
            string file = LocateFile(evt);
            if (file == null) return NotFound(evt);
            var dom = SoddJson.Parse(File.ReadAllText(file));
            string p = NormalizeEventPath(newPath ?? dom["path"]?.AsString() + "_copy");
            for (int i = 2; newPath == null && LocateFile(p) != null; i++) p = NormalizeEventPath(dom["path"]?.AsString() + "_copy" + i);
            dom.Set("id", Guid.NewGuid().ToString());
            dom.Set("path", p);
            return CreateEventFromJson(SoddJson.Write(dom));
        }

        public static SoddResult DeleteEvent(string evt)
        {
            string file = LocateFile(evt);
            if (file == null) return NotFound(evt);
            var dom = SoddJson.Parse(File.ReadAllText(file));
            string key = dom["id"]?.AsString() ?? file;
            SoddUndo.Change(key, file, file, null, "Borrar evento " + evt);
            Changed?.Invoke(key);
            return SoddResult.Ok();
        }

        public static SoddResult RenameEvent(string evt, string newName)
        {
            if (string.IsNullOrEmpty(newName) || newName.Contains("/")) return SoddResult.Fail("El nombre nuevo no puede estar vacío ni tener '/'. Para cambiar carpeta usá MoveEvent.");
            string file = LocateFile(evt);
            if (file == null) return NotFound(evt);
            var dom = SoddJson.Parse(File.ReadAllText(file));
            string old = dom["path"]?.AsString() ?? "";
            int s = old.LastIndexOf('/');
            return MoveEvent(evt, (s >= 0 ? old.Substring(0, s + 1) : "") + newName);
        }

        public static SoddResult MoveEvent(string evt, string newPath)
        {
            newPath = NormalizeEventPath(newPath);
            if (string.IsNullOrEmpty(newPath)) return SoddResult.Fail("Path vacío");
            string file = LocateFile(evt);
            if (file == null) return NotFound(evt);
            var existing = LocateFile(newPath);
            if (existing != null && existing != file) return SoddResult.Fail($"Ya existe un evento con path '{newPath}'");
            var dom = SoddJson.Parse(File.ReadAllText(file));
            dom.Set("path", newPath);
            string newFile = FileForPath(newPath);
            // Mantener el $schema relativo correcto
            if (dom.Has("$schema")) dom.Set("$schema", string.Concat(System.Linq.Enumerable.Repeat("../", newPath.Split('/').Length + 2)) + "Packages/com.sodd.sodddesign/Schema/sodd.event.schema.json");
            return Commit(dom, file, newFile, $"Mover evento a {newPath}", new SoddResult());
        }

        // ================================================================== propiedades genéricas
        static JsonNode EnsureArray(JsonNode o, string key)
        {
            var a = o[key];
            if (a == null || !a.IsArray) { a = JsonNode.NewArray(); o.Set(key, a); }
            return a;
        }

        static JsonNode ParseValue(string json, SoddResult r)
        {
            if (json == null) return JsonNode.Null();
            if (SoddJson.TryParse(json, out var n, out var err)) return n;
            // Permitir strings sin comillas ("SFX") por comodidad.
            if (!json.TrimStart().StartsWith("{") && !json.TrimStart().StartsWith("[")) return JsonNode.From(json);
            r.Error(err);
            return null;
        }

        /// <summary>Asigna un valor (JSON) en un JSON Pointer del evento, ej. "/tracks/0/volume" = "-6".</summary>
        public static SoddResult SetProperty(string evt, string pointer, string valueJson)
        {
            return Edit(evt, "Cambiar " + pointer, dom =>
            {
                var r = new SoddResult();
                var v = ParseValue(valueJson, r);
                if (!r.ok) return r;
                if (pointer == "/id") return r.Error("El id no se puede cambiar (las referencias del juego lo usan)");
                if (pointer == "/path") return r.Error("Para cambiar el path usá MoveEvent/RenameEvent");
                // Si la propiedad es modulable y ya tiene moduladores, un número cambia solo el base.
                var cur = SoddJson.Resolve(dom, pointer);
                if (cur != null && cur.IsObject && cur.Has("base") && v.IsNumber) { cur.Set("base", v); return r; }
                if (!SoddJson.SetAt(dom, pointer, v, out var err)) r.Error(err);
                return r;
            });
        }

        /// <summary>Aplica un JSON Patch (RFC 6902) al evento.</summary>
        public static SoddResult EditEvent(string evt, string patchJson)
        {
            return Edit(evt, "Editar evento", dom =>
            {
                var r = new SoddResult();
                if (!SoddJson.TryParse(patchJson, out var patch, out var err)) return r.Error(err);
                string id = dom["id"]?.AsString();
                if (!SoddJson.ApplyPatch(dom, patch, out err)) return r.Error(err);
                if (dom["id"]?.AsString() != id) return r.Error("El id no se puede cambiar");
                return r;
            });
        }

        public static SoddResult GetEvent(string evt)
        {
            string file = LocateFile(evt);
            if (file == null) return NotFound(evt);
            return SoddResult.Ok(File.ReadAllText(file));
        }

        public static SoddResult ListEvents(string filter = null)
        {
            var arr = JsonNode.NewArray();
            foreach (var a in SoddProjectFiles.AllEventAssets())
            {
                var d = a.data;
                if (d == null) continue;
                if (!string.IsNullOrEmpty(filter) && d.path.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0 && Array.IndexOf(d.tags, filter) < 0) continue;
                var o = JsonNode.NewObject();
                o.Set("id", d.id); o.Set("path", d.path); o.Set("description", d.description ?? "");
                var tags = JsonNode.NewArray(); foreach (var t in d.tags) tags.Add(JsonNode.From(t)); o.Set("tags", tags);
                o.Set("is3D", d.is3D); o.Set("bus", d.bus); o.Set("length", Math.Round(d.Length, 3)); o.Set("tracks", d.tracks.Length);
                o.Set("file", AssetDatabase.GetAssetPath(a));
                arr.Add(o);
            }
            return SoddResult.Ok(SoddJson.Write(arr));
        }

        // ================================================================== tracks / regiones
        public static SoddResult AddTrack(string evt, string name, string type = "SFX")
        {
            int index = -1;
            var r = Edit(evt, "Agregar track " + name, dom =>
            {
                var tracks = EnsureArray(dom, "tracks");
                var t = JsonNode.NewObject();
                t.Set("name", string.IsNullOrEmpty(name) ? "Track " + (tracks.Count + 1) : name);
                t.Set("type", type ?? "SFX");
                t.Set("regions", JsonNode.NewArray());
                tracks.Add(t);
                index = tracks.Count - 1;
                return null;
            });
            if (r.ok) r.data = $"{{\"index\":{index}}}";
            return r;
        }

        public static SoddResult RemoveTrack(string evt, int track) => Remove(evt, $"/tracks/{track}", "Borrar track");

        static SoddResult Remove(string evt, string pointer, string undo) => Edit(evt, undo, dom =>
        {
            var r = new SoddResult();
            if (!SoddJson.RemoveAt(dom, pointer, out var err)) r.Error(err);
            return r;
        });

        /// <summary>Agrega una región. instrumentJson ej: {"type":"SingleClip","clip":"Assets/Audio/kick.wav"}.</summary>
        public static SoddResult AddRegion(string evt, int track, float start, float length, string instrumentJson, string name = null)
        {
            int index = -1;
            var r = Edit(evt, "Agregar región", dom =>
            {
                var res = new SoddResult();
                var t = SoddJson.Resolve(dom, $"/tracks/{track}");
                if (t == null) return res.Error($"No existe el track {track}");
                var ins = ParseValue(instrumentJson ?? "{\"type\":\"Silence\"}", res);
                if (!res.ok) return res;
                var reg = JsonNode.NewObject();
                if (!string.IsNullOrEmpty(name)) reg.Set("name", name);
                reg.Set("start", Math.Round(Mathf.Max(0, start), 4)); reg.Set("length", Math.Round(Mathf.Max(0.001f, length), 4));
                reg.Set("instrument", ins);
                var regions = EnsureArray(t, "regions");
                regions.Add(reg);
                index = regions.Count - 1;
                return res;
            });
            if (r.ok) r.data = $"{{\"index\":{index}}}";
            return r;
        }

        public static SoddResult RemoveRegion(string evt, int track, int region) => Remove(evt, $"/tracks/{track}/regions/{region}", "Borrar región");

        /// <summary>Mueve una región en el tiempo y opcionalmente a otro track (newTrack &lt; 0 = mismo track).</summary>
        public static SoddResult MoveRegion(string evt, int track, int region, float newStart, int newTrack = -1)
        {
            return Edit(evt, "Mover región", dom =>
            {
                var r = new SoddResult();
                var reg = SoddJson.Resolve(dom, $"/tracks/{track}/regions/{region}");
                if (reg == null) return r.Error($"No existe la región {region} del track {track}");
                reg.Set("start", Math.Round(Mathf.Max(0f, newStart), 4));
                if (newTrack >= 0 && newTrack != track)
                {
                    var dst = SoddJson.Resolve(dom, $"/tracks/{newTrack}");
                    if (dst == null) return r.Error($"No existe el track {newTrack}");
                    SoddJson.RemoveAt(dom, $"/tracks/{track}/regions/{region}", out _);
                    EnsureArray(dst, "regions").Add(reg);
                }
                return r;
            });
        }

        /// <summary>Divide una región en el tiempo dado (absoluto en el timeline).</summary>
        public static SoddResult SplitRegion(string evt, int track, int region, float time)
        {
            return Edit(evt, "Dividir región", dom =>
            {
                var r = new SoddResult();
                var reg = SoddJson.Resolve(dom, $"/tracks/{track}/regions/{region}");
                if (reg == null) return r.Error("Región inexistente");
                float st = reg["start"].AsFloat(), len = reg["length"].AsFloat(1);
                if (time <= st + 0.01f || time >= st + len - 0.01f) return r.Error("El punto de corte tiene que estar dentro de la región");
                var right = reg.Clone();
                reg.Set("length", Math.Round(time - st, 4)); reg.Remove("fadeOut");
                right.Set("start", Math.Round(time, 4)); right.Set("length", Math.Round(st + len - time, 4)); right.Remove("fadeIn");
                var ins = right["instrument"];
                if (ins != null && ins.IsObject)
                {
                    // La mitad derecha arranca el clip donde quedó (startOffset).
                    double off = (ins["startOffset"]?.IsNumber ?? false) ? ins["startOffset"].Number : 0;
                    ins.Set("startOffset", Math.Round(off + (time - st), 4));
                }
                var regions = SoddJson.Resolve(dom, $"/tracks/{track}/regions");
                regions.Items.Insert(region + 1, right);
                return r;
            });
        }

        public static SoddResult DuplicateRegion(string evt, int track, int region)
        {
            return Edit(evt, "Duplicar región", dom =>
            {
                var r = new SoddResult();
                var reg = SoddJson.Resolve(dom, $"/tracks/{track}/regions/{region}");
                if (reg == null) return r.Error("Región inexistente");
                var copy = reg.Clone();
                copy.Set("start", Math.Round(reg["start"].AsFloat() + reg["length"].AsFloat(1), 4));
                SoddJson.Resolve(dom, $"/tracks/{track}/regions").Add(copy);
                return r;
            });
        }

        // ================================================================== moduladores
        /// <summary>Agrega un modulador a una propiedad (pointer a la propiedad, ej. "/pitch" o "/tracks/0/volume").</summary>
        public static SoddResult AddModulator(string evt, string propPointer, string modulatorJson)
        {
            int index = -1;
            var r = Edit(evt, "Agregar modulador", dom =>
            {
                var res = new SoddResult();
                var mod = ParseValue(modulatorJson, res);
                if (!res.ok) return res;
                if (mod == null || !mod.IsObject || !mod.Has("type")) return res.Error("El modulador debe ser un objeto con 'type'");
                var prop = SoddJson.Resolve(dom, propPointer);
                JsonNode obj;
                if (prop == null || prop.IsNumber)
                {
                    obj = JsonNode.NewObject();
                    obj.Set("base", prop?.Number ?? DefaultBase(propPointer));
                    obj.Set("modulators", JsonNode.NewArray());
                    if (!SoddJson.SetAt(dom, propPointer, obj, out var err)) return res.Error(err);
                }
                else if (prop.IsObject) obj = prop;
                else return res.Error($"'{propPointer}' no es una propiedad numérica");
                var mods = EnsureArray(obj, "modulators");
                mods.Add(mod);
                index = mods.Count - 1;
                return res;
            });
            if (r.ok) r.data = $"{{\"index\":{index}}}";
            return r;
        }

        static double DefaultBase(string pointer)
        {
            if (pointer.EndsWith("/cutoff")) return 5000;
            return 0;
        }

        public static SoddResult RemoveModulator(string evt, string propPointer, int index)
        {
            return Edit(evt, "Quitar modulador", dom =>
            {
                var r = new SoddResult();
                var prop = SoddJson.Resolve(dom, propPointer);
                if (prop == null || !prop.IsObject) return r.Error($"'{propPointer}' no tiene moduladores");
                if (!SoddJson.RemoveAt(prop, $"/modulators/{index}", out var err)) return r.Error(err);
                // Si no quedan moduladores, volver al atajo numérico.
                if (prop["modulators"].Count == 0) SoddJson.SetAt(dom, propPointer, prop["base"] ?? JsonNode.From(0), out _);
                return r;
            });
        }

        // ================================================================== parámetros
        /// <summary>Agrega un parámetro. evt null → global (sodd.parameters.json); si no, local del evento.</summary>
        public static SoddResult AddParameter(string evt, string parameterJson)
        {
            var r0 = new SoddResult();
            var p = ParseValue(parameterJson, r0);
            if (!r0.ok) return r0;
            if (p == null || !p.IsObject || string.IsNullOrEmpty(p["name"]?.AsString())) return SoddResult.Fail("El parámetro necesita al menos 'name'");
            string name = p["name"].String;
            var check = new SoddReader();
            check.ReadParameter(p, "/parameter", evt != null);
            if (!check.Result.ok) return check.Result;

            if (evt != null)
                return Edit(evt, "Agregar parámetro " + name, dom =>
                {
                    var arr = EnsureArray(dom, "parameters");
                    foreach (var it in arr.Items) if (it["name"]?.AsString() == name) return SoddResult.Fail($"Ya existe el parámetro local '{name}'");
                    p.Set("scope", "local");
                    arr.Add(p);
                    return null;
                });

            string file = SoddProjectFiles.ParametersPath ?? SoddProjectFiles.Root + "/" + SoddProjectFiles.ParametersFile;
            JsonNode root = File.Exists(file) ? SoddJson.Parse(File.ReadAllText(file)) : JsonNode.NewObject();
            var list = EnsureArray(root, "parameters");
            foreach (var it in list.Items) if (it["name"]?.AsString() == name) return SoddResult.Fail($"Ya existe el parámetro global '{name}'");
            if (!p.Has("scope")) p.Set("scope", "global");
            list.Add(p);
            var text = SoddJson.Write(root);
            SoddReader.ParseParameters(text, out var vr);
            if (!vr.ok) return vr;
            SoddUndo.Change(ParametersKey, File.Exists(file) ? file : null, file, text, "Agregar parámetro global " + name);
            SoddProjectFiles.ClearCache();
            Changed?.Invoke(ParametersKey);
            return vr;
        }

        public static SoddResult ListParameters()
        {
            var arr = JsonNode.NewArray();
            var data = SoddProjectFiles.LoadParameters();
            if (data != null)
                foreach (var p in data.parameters)
                {
                    var o = JsonNode.NewObject();
                    o.Set("name", p.name); o.Set("scope", p.isGlobal ? "global" : "local"); o.Set("min", p.min); o.Set("max", p.max);
                    o.Set("default", p.defaultValue); o.Set("seekSpeed", p.seekSpeed); o.Set("description", p.description ?? "");
                    arr.Add(o);
                }
            return SoddResult.Ok(SoddJson.Write(arr));
        }

        /// <summary>Edita sodd.settings.json con un JSON Patch (buses, snapshots, voces).</summary>
        public static SoddResult EditSettings(string patchJson)
        {
            string file = SoddProjectFiles.SettingsPath ?? SoddProjectFiles.Root + "/" + SoddProjectFiles.SettingsFile;
            JsonNode root = File.Exists(file) ? SoddJson.Parse(File.ReadAllText(file)) : JsonNode.NewObject();
            if (!SoddJson.TryParse(patchJson, out var patch, out var err)) return SoddResult.Fail(err);
            if (!SoddJson.ApplyPatch(root, patch, out err)) return SoddResult.Fail(err);
            var text = SoddJson.Write(root);
            SoddReader.ParseSettings(text, out var vr);
            if (!vr.ok) return vr;
            SoddUndo.Change(SettingsKey, File.Exists(file) ? file : null, file, text, "Editar settings");
            SoddProjectFiles.ClearCache();
            Changed?.Invoke(SettingsKey);
            return vr;
        }

        // ================================================================== efectos, markers, loops
        public static SoddResult AddEffect(string evt, int track, string effectJson)
        {
            int index = -1;
            var r = Edit(evt, "Agregar efecto", dom =>
            {
                var res = new SoddResult();
                var fx = ParseValue(effectJson, res);
                if (!res.ok) return res;
                if (fx.IsString) { var o = JsonNode.NewObject(); o.Set("type", fx.String); fx = o; }
                var t = SoddJson.Resolve(dom, $"/tracks/{track}");
                if (t == null) return res.Error($"No existe el track {track}");
                var arr = EnsureArray(t, "effects");
                arr.Add(fx);
                index = arr.Count - 1;
                return res;
            });
            if (r.ok) r.data = $"{{\"index\":{index}}}";
            return r;
        }

        public static SoddResult RemoveEffect(string evt, int track, int index) => Remove(evt, $"/tracks/{track}/effects/{index}", "Quitar efecto");

        public static SoddResult AddMarker(string evt, string name, float time)
        {
            return Edit(evt, "Agregar marker " + name, dom =>
            {
                var m = JsonNode.NewObject();
                m.Set("name", string.IsNullOrEmpty(name) ? "Marker" : name); m.Set("time", Math.Round(Mathf.Max(0, time), 4));
                EnsureArray(dom, "markers").Add(m);
                return null;
            });
        }

        public static SoddResult AddLoopRegion(string evt, float start, float end, string exitWhenJson = null, string name = "Loop")
        {
            return Edit(evt, "Agregar loop", dom =>
            {
                var r = new SoddResult();
                if (end <= start) return r.Error("end debe ser mayor que start");
                var l = JsonNode.NewObject();
                l.Set("name", name ?? "Loop"); l.Set("start", Math.Round(start, 4)); l.Set("end", Math.Round(end, 4));
                if (!string.IsNullOrEmpty(exitWhenJson)) { var c = ParseValue(exitWhenJson, r); if (!r.ok) return r; l.Set("exitWhen", c); }
                EnsureArray(dom, "loopRegions").Add(l);
                return r;
            });
        }

        // ================================================================== preview
        public static SoddResult PreviewEvent(string evt, string parametersJson = null)
        {
            string file = LocateFile(evt);
            if (file == null) return NotFound(evt);
            var asset = AssetDatabase.LoadAssetAtPath<SoddEventAsset>(file);
            if (asset == null) return SoddResult.Fail($"El evento no se pudo importar (¿JSON inválido?): {file}");
            Dictionary<string, float> ps = null;
            if (!string.IsNullOrEmpty(parametersJson))
            {
                if (!SoddJson.TryParse(parametersJson, out var pn, out var err)) return SoddResult.Fail(err);
                ps = new Dictionary<string, float>();
                if (pn.IsObject) foreach (var kv in pn.Props) ps[kv.Key] = kv.Value.AsFloat();
            }
            var inst = SoddPreview.Play(asset, ps);
            if (!inst.IsValid) return SoddResult.Fail("No se pudo iniciar el preview");
            var r = SoddResult.Ok($"{{\"path\":\"{asset.Path}\",\"length\":{SoddJson.FormatNumber(asset.data.Length)},\"loops\":{(asset.data.loopRegions.Length > 0 ? "true" : "false")}}}");
            if (asset.data.loopRegions.Length > 0) r.Warn("El evento tiene loops: sonará hasta que llames a StopPreview");
            return r;
        }

        public static SoddResult StopPreview()
        {
            SoddPreview.StopAll();
            return SoddResult.Ok();
        }
    }
}
