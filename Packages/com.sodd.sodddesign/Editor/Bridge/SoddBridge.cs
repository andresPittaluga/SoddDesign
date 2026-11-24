using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using Sodd.Design.Json;
using UnityEditor;
using UnityEngine;

namespace Sodd.Design.Editor
{
    /// <summary>
    /// Bridge local para IA: servidor HTTP mínimo SOLO en 127.0.0.1 que expone SoddCommands como JSON-RPC 2.0.
    /// - Puerto configurable (EditorPrefs "SoddBridge.Port", default 47821).
    /// - Token aleatorio por sesión en Library/SoddDesign/bridge.json (header "Authorization: Bearer &lt;token&gt;").
    /// - Rechaza Host que no sea localhost (anti DNS-rebinding) y requests con Origin (navegadores).
    /// - Los comandos se ejecutan en el main thread vía cola (EditorApplication.update).
    /// </summary>
    [InitializeOnLoad]
    public static class SoddBridge
    {
        public const int DefaultPort = 47821;
        const string PrefEnabled = "SoddBridge.Enabled", PrefPort = "SoddBridge.Port";
        public static string ConfigPath => Path.GetFullPath("Library/SoddDesign/bridge.json");

        static TcpListener s_listener;
        static Thread s_thread;
        static volatile bool s_running;
        static string s_token;
        static int s_port;
        static readonly ConcurrentQueue<Action> s_main = new ConcurrentQueue<Action>();

        public static bool IsRunning => s_running;
        public static int Port => s_port;
        public static bool Enabled
        {
            get => EditorPrefs.GetBool(PrefEnabled, true);
            set { EditorPrefs.SetBool(PrefEnabled, value); if (value) Start(); else Stop(); }
        }

        static SoddBridge()
        {
            SoddStudioHooks.BridgeRunning = () => s_running;
            SoddStudioHooks.ToggleBridge = () => Enabled = !Enabled;
            EditorApplication.update += Pump;
            AssemblyReloadEvents.beforeAssemblyReload += Stop;
            EditorApplication.quitting += Stop;
            if (Enabled && !Application.isBatchMode) EditorApplication.delayCall += Start;
        }

        [MenuItem("Window/Sodd/AI Bridge/Start")]
        static void MenuStart() => Enabled = true;
        [MenuItem("Window/Sodd/AI Bridge/Stop")]
        static void MenuStop() => Enabled = false;
        [MenuItem("Window/Sodd/AI Bridge/Copy Claude Code command")]
        static void MenuCopy()
        {
            string js = Path.GetFullPath("Packages/com.sodd.sodddesign/Tools~/sodddesign-mcp/dist/index.js").Replace('\\', '/');
            string proj = Path.GetFullPath(".").Replace('\\', '/');
            EditorGUIUtility.systemCopyBuffer = $"claude mcp add sodddesign -- node \"{js}\" --project \"{proj}\"";
            Debug.Log("[SoddDesign] Copiado al portapapeles:\n" + EditorGUIUtility.systemCopyBuffer);
        }

        public static void Start() => Start(EditorPrefs.GetInt(PrefPort, DefaultPort));

        /// <summary>Arranca en un puerto dado (0 = cualquiera libre; lo usan los tests).</summary>
        public static void Start(int port)
        {
            if (s_running) return;
            try
            {
                s_listener = new TcpListener(IPAddress.Loopback, port);
                s_listener.Start();
                s_port = ((IPEndPoint)s_listener.LocalEndpoint).Port;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[SoddDesign] No se pudo abrir el bridge en 127.0.0.1:{port} ({e.Message}). Cambiá el puerto con EditorPrefs '{PrefPort}'.");
                s_listener = null;
                return;
            }
            s_token = NewToken();
            WriteConfig();
            s_running = true;
            s_thread = new Thread(AcceptLoop) { IsBackground = true, Name = "SoddBridge" };
            s_thread.Start();
        }

        public static void Stop()
        {
            if (!s_running) return;
            s_running = false;
            try { s_listener?.Stop(); } catch { /* ignorar */ }
            s_listener = null;
            try { if (File.Exists(ConfigPath)) File.Delete(ConfigPath); } catch { /* ignorar */ }
        }

        static string NewToken()
        {
            var b = new byte[24];
            using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(b);
            return BitConverter.ToString(b).Replace("-", "").ToLowerInvariant();
        }

        static void WriteConfig()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath));
            var o = JsonNode.NewObject();
            o.Set("port", s_port); o.Set("token", s_token); o.Set("host", "127.0.0.1");
            o.Set("project", Path.GetFullPath(".").Replace('\\', '/'));
            o.Set("unity", Application.unityVersion); o.Set("version", SoddVersion.Value);
            File.WriteAllText(ConfigPath, SoddJson.Write(o));
        }

        static void Pump()
        {
            int n = 0;
            while (n++ < 32 && s_main.TryDequeue(out var a))
            {
                try { a(); } catch (Exception e) { Debug.LogException(e); }
            }
        }

        // ------------------------------------------------------------------ red (thread de fondo)
        static void AcceptLoop()
        {
            while (s_running)
            {
                TcpClient client;
                try { client = s_listener.AcceptTcpClient(); }
                catch { break; }
                ThreadPool.QueueUserWorkItem(_ => HandleClient(client));
            }
        }

        static void HandleClient(TcpClient client)
        {
            using (client)
            {
                try
                {
                    client.ReceiveTimeout = 10000;
                    var stream = client.GetStream();
                    if (!ReadRequest(stream, out var method, out var headers, out var body)) return;
                    var (status, resp) = Process(method, headers, body, RunOnMain);
                    var bytes = Encoding.UTF8.GetBytes(resp);
                    var head = $"HTTP/1.1 {status} {(status == 200 ? "OK" : "Error")}\r\nContent-Type: application/json; charset=utf-8\r\nContent-Length: {bytes.Length}\r\nConnection: close\r\n\r\n";
                    var hb = Encoding.ASCII.GetBytes(head);
                    stream.Write(hb, 0, hb.Length);
                    stream.Write(bytes, 0, bytes.Length);
                }
                catch (Exception) { /* cliente cortó */ }
            }
        }

        static bool ReadRequest(Stream s, out string method, out Dictionary<string, string> headers, out string body)
        {
            method = null; body = null;
            headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var buf = new List<byte>(4096);
            while (true)
            {
                int b = s.ReadByte();
                if (b < 0) return false;
                buf.Add((byte)b);
                if (buf.Count >= 4 && buf[buf.Count - 4] == '\r' && buf[buf.Count - 3] == '\n' && buf[buf.Count - 2] == '\r' && buf[buf.Count - 1] == '\n') break;
                if (buf.Count > 16384) return false;
            }
            var lines = Encoding.ASCII.GetString(buf.ToArray()).Split(new[] { "\r\n" }, StringSplitOptions.RemoveEmptyEntries);
            if (lines.Length == 0) return false;
            method = lines[0].Split(' ')[0];
            for (int i = 1; i < lines.Length; i++)
            {
                int c = lines[i].IndexOf(':');
                if (c > 0) headers[lines[i].Substring(0, c).Trim()] = lines[i].Substring(c + 1).Trim();
            }
            int len = headers.TryGetValue("Content-Length", out var l) && int.TryParse(l, out var n) ? n : 0;
            if (len > 8 * 1024 * 1024) return false;
            var data = new byte[len];
            int read = 0;
            while (read < len) { int r = s.Read(data, read, len - read); if (r <= 0) break; read += r; }
            body = Encoding.UTF8.GetString(data, 0, read);
            return true;
        }

        static string RunOnMain(string request)
        {
            string result = null;
            using (var done = new ManualResetEventSlim(false))
            {
                s_main.Enqueue(() => { try { result = Dispatch(request); } finally { done.Set(); } });
                if (!done.Wait(30000)) return RpcError(null, -32000, "Timeout: Unity no respondió (¿el Editor está ocupado compilando o en un diálogo modal?)");
            }
            return result;
        }

        /// <summary>Valida HTTP (método, host, origin, token) y delega. Público para tests.</summary>
        public static (int status, string body) Process(string method, IDictionary<string, string> headers, string body, Func<string, string> invoke, string token = null)
        {
            token ??= s_token;
            if (method != "POST") return (405, RpcError(null, -32600, "Usá POST con JSON-RPC 2.0"));
            if (headers.TryGetValue("Origin", out var origin) && !string.IsNullOrEmpty(origin)) return (403, RpcError(null, -32600, "Requests de navegador no permitidas"));
            if (headers.TryGetValue("Host", out var host))
            {
                string h = host.Split(':')[0];
                if (h != "127.0.0.1" && h != "localhost") return (403, RpcError(null, -32600, "Host no permitido"));
            }
            string auth = headers.TryGetValue("Authorization", out var a) ? a : null;
            if (token == null || auth != "Bearer " + token) return (401, RpcError(null, -32001, "Token inválido. Leelo de Library/SoddDesign/bridge.json"));
            return (200, invoke(body));
        }

        static string RpcError(JsonNode id, int code, string msg)
        {
            var o = JsonNode.NewObject();
            o.Set("jsonrpc", "2.0");
            o.Set("id", id ?? JsonNode.Null());
            var e = JsonNode.NewObject(); e.Set("code", code); e.Set("message", msg);
            o.Set("error", e);
            return SoddJson.Write(o, false);
        }

        // ------------------------------------------------------------------ JSON-RPC (main thread)
        /// <summary>Ejecuta una request JSON-RPC 2.0 (debe llamarse en el main thread).</summary>
        public static string Dispatch(string request)
        {
            if (!SoddJson.TryParse(request, out var req, out var perr)) return RpcError(null, -32700, perr);
            var id = req["id"];
            string method = req["method"]?.AsString();
            var p = req["params"] ?? JsonNode.NewObject();
            if (method == null) return RpcError(id, -32600, "Falta 'method'");
            SoddResult r;
            try { r = Call(method, p); }
            catch (Exception e) { return RpcError(id, -32603, e.Message); }
            if (r == null) return RpcError(id, -32601, $"Método '{method}' desconocido.{SoddText.DidYouMean(method, Methods)}");
            var res = JsonNode.NewObject();
            res.Set("ok", r.ok);
            var errs = JsonNode.NewArray(); foreach (var e in r.errors) errs.Add(JsonNode.From(e)); res.Set("errors", errs);
            var warns = JsonNode.NewArray(); foreach (var w in r.warnings) warns.Add(JsonNode.From(w)); res.Set("warnings", warns);
            if (r.data != null) res.Set("data", SoddJson.TryParse(r.data, out var dn, out _) ? dn : JsonNode.From(r.data));
            var o = JsonNode.NewObject();
            o.Set("jsonrpc", "2.0"); o.Set("id", id ?? JsonNode.Null()); o.Set("result", res);
            return SoddJson.Write(o, false);
        }

        public static readonly string[] Methods =
        {
            "ping", "list_events", "get_event", "create_event", "edit_event", "set_property", "delete_event", "move_event",
            "add_track", "add_region", "add_effect", "add_marker", "add_loop_region", "add_modulator", "remove_modulator",
            "list_parameters", "add_parameter", "list_audio_clips", "list_effects", "get_settings", "edit_settings",
            "preview_event", "stop_preview", "validate", "get_schema", "get_ai_guide", "open_in_studio",
        };

        static string S(JsonNode p, string k, string def = null) => p[k]?.AsString(def) ?? def;
        static string Raw(JsonNode p, string k) { var n = p[k]; return n == null ? null : n.IsString ? n.String : SoddJson.Write(n, false); }
        static string RawJson(JsonNode p, string k) { var n = p[k]; return n == null ? null : SoddJson.Write(n, false); }
        static float F(JsonNode p, string k, float def = 0) => p[k]?.AsFloat(def) ?? def;
        static int I(JsonNode p, string k, int def = 0) => p[k]?.AsInt(def) ?? def;

        static SoddResult Call(string m, JsonNode p)
        {
            string ev = S(p, "event");
            switch (m)
            {
                case "ping": return SoddResult.Ok($"{{\"pong\":true,\"unity\":\"{Application.unityVersion}\",\"version\":\"{SoddVersion.Value}\",\"playing\":{(Application.isPlaying ? "true" : "false")}}}");
                case "list_events": return SoddCommands.ListEvents(S(p, "filter"));
                case "get_event": return SoddCommands.GetEvent(ev);
                case "create_event":
                    if (p["event"] != null && p["event"].IsObject) return SoddCommands.CreateEventFromJson(RawJson(p, "event"));
                    return SoddCommands.CreateEvent(S(p, "path"), S(p, "description", ""), p["is3D"]?.AsBool() ?? false, S(p, "bus"));
                case "edit_event": return SoddCommands.EditEvent(ev, RawJson(p, "patch"));
                case "set_property": return SoddCommands.SetProperty(ev, S(p, "pointer"), RawJson(p, "value"));
                case "delete_event": return SoddCommands.DeleteEvent(ev);
                case "move_event": return SoddCommands.MoveEvent(ev, S(p, "newPath"));
                case "add_track": return SoddCommands.AddTrack(ev, S(p, "name"), S(p, "type", "SFX"));
                case "add_region": return SoddCommands.AddRegion(ev, I(p, "track"), F(p, "start"), F(p, "length", 1), RawJson(p, "instrument"), S(p, "name"));
                case "add_effect": return SoddCommands.AddEffect(ev, I(p, "track"), Raw(p, "effect"));
                case "add_marker": return SoddCommands.AddMarker(ev, S(p, "name"), F(p, "time"));
                case "add_loop_region": return SoddCommands.AddLoopRegion(ev, F(p, "start"), F(p, "end"), RawJson(p, "exitWhen"), S(p, "name", "Loop"));
                case "add_modulator": return SoddCommands.AddModulator(ev, S(p, "pointer"), RawJson(p, "modulator"));
                case "remove_modulator": return SoddCommands.RemoveModulator(ev, S(p, "pointer"), I(p, "index"));
                case "list_parameters": return SoddCommands.ListParameters();
                case "add_parameter": return SoddCommands.AddParameter(ev, RawJson(p, "parameter"));
                case "list_audio_clips": return ListClips(S(p, "filter"));
                case "list_effects": return ListEffects();
                case "get_settings":
                    {
                        var f = SoddProjectFiles.SettingsPath;
                        return f != null ? SoddResult.Ok(File.ReadAllText(f)) : SoddResult.Fail("No hay sodd.settings.json");
                    }
                case "edit_settings": return SoddCommands.EditSettings(RawJson(p, "patch"));
                case "preview_event": return SoddCommands.PreviewEvent(ev, RawJson(p, "parameters"));
                case "stop_preview": return SoddCommands.StopPreview();
                case "validate": return SoddCommands.Validate(ev);
                case "get_schema": return SoddResult.Ok(File.ReadAllText("Packages/com.sodd.sodddesign/Schema/sodd.event.schema.json"));
                case "get_ai_guide":
                    {
                        var f = "Packages/com.sodd.sodddesign/Documentation~/AI_GUIDE.md";
                        return File.Exists(f) ? SoddResult.Ok(SoddJson.Write(JsonNode.From(File.ReadAllText(f)), false)) : SoddResult.Fail("Falta AI_GUIDE.md");
                    }
                case "open_in_studio": SoddStudioWindow.OpenEvent(ev); return SoddResult.Ok();
            }
            return null;
        }

        static SoddResult ListClips(string filter)
        {
            var arr = JsonNode.NewArray();
            foreach (var g in AssetDatabase.FindAssets("t:AudioClip" + (string.IsNullOrEmpty(filter) ? "" : " " + filter)))
            {
                string path = AssetDatabase.GUIDToAssetPath(g);
                if (!path.StartsWith("Assets/")) continue;
                var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
                if (clip == null) continue;
                var o = JsonNode.NewObject();
                o.Set("path", path); o.Set("guid", g); o.Set("name", clip.name);
                o.Set("length", Math.Round(clip.length, 3)); o.Set("channels", clip.channels); o.Set("frequency", clip.frequency);
                var tags = JsonNode.NewArray();
                foreach (var l in AssetDatabase.GetLabels(clip)) tags.Add(JsonNode.From(l));
                foreach (var part in Path.GetDirectoryName(path).Replace('\\', '/').Split('/').Skip(1)) tags.Add(JsonNode.From(part.ToLowerInvariant()));
                o.Set("tags", tags);
                arr.Add(o);
            }
            return SoddResult.Ok(SoddJson.Write(arr, false));
        }

        static SoddResult ListEffects()
        {
            var arr = JsonNode.NewArray();
            foreach (var e in SoddEffectCatalog.All)
            {
                var o = JsonNode.NewObject();
                o.Set("type", e.name); o.Set("description", e.description); o.Set("native", e.native);
                var ps = JsonNode.NewArray();
                foreach (var pi in e.parameters)
                {
                    var q = JsonNode.NewObject();
                    q.Set("name", pi.name); q.Set("default", pi.defaultValue); q.Set("min", pi.min); q.Set("max", pi.max); q.Set("unit", pi.unit); q.Set("description", pi.description);
                    ps.Add(q);
                }
                o.Set("params", ps);
                arr.Add(o);
            }
            return SoddResult.Ok(SoddJson.Write(arr, false));
        }
    }
}
