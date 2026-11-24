using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Sodd.Design.Json
{
    public enum JsonKind { Null, Bool, Number, String, Array, Object }

    /// <summary>
    /// Árbol JSON mínimo, sin dependencias, que preserva el orden de las claves (para que los
    /// archivos .sodd.json editados por SoddCommands sigan siendo legibles y con diffs chicos).
    /// No se usa en el loop de audio: solo al importar/editar.
    /// </summary>
    public sealed class JsonNode
    {
        public JsonKind Kind;
        public bool Bool;
        public double Number;
        public string String;
        public List<JsonNode> Items;
        public List<KeyValuePair<string, JsonNode>> Props;

        public static JsonNode Null() => new JsonNode { Kind = JsonKind.Null };
        public static JsonNode From(bool b) => new JsonNode { Kind = JsonKind.Bool, Bool = b };
        public static JsonNode From(double d) => new JsonNode { Kind = JsonKind.Number, Number = d };
        public static JsonNode From(string s) => s == null ? Null() : new JsonNode { Kind = JsonKind.String, String = s };
        public static JsonNode NewArray() => new JsonNode { Kind = JsonKind.Array, Items = new List<JsonNode>() };
        public static JsonNode NewObject() => new JsonNode { Kind = JsonKind.Object, Props = new List<KeyValuePair<string, JsonNode>>() };

        public bool IsNull => Kind == JsonKind.Null;
        public bool IsObject => Kind == JsonKind.Object;
        public bool IsArray => Kind == JsonKind.Array;
        public bool IsNumber => Kind == JsonKind.Number;
        public bool IsString => Kind == JsonKind.String;
        public int Count => Kind == JsonKind.Array ? Items.Count : Kind == JsonKind.Object ? Props.Count : 0;

        public JsonNode this[string key]
        {
            get => Get(key);
            set => Set(key, value);
        }

        public JsonNode this[int i] => Kind == JsonKind.Array && i >= 0 && i < Items.Count ? Items[i] : null;

        public JsonNode Get(string key)
        {
            if (Kind != JsonKind.Object) return null;
            for (int i = 0; i < Props.Count; i++)
                if (Props[i].Key == key) return Props[i].Value;
            return null;
        }

        public bool Has(string key) => Get(key) != null;

        public JsonNode Set(string key, JsonNode value)
        {
            if (Kind != JsonKind.Object) throw new InvalidOperationException("Set en un nodo que no es objeto");
            for (int i = 0; i < Props.Count; i++)
            {
                if (Props[i].Key == key) { Props[i] = new KeyValuePair<string, JsonNode>(key, value); return value; }
            }
            Props.Add(new KeyValuePair<string, JsonNode>(key, value));
            return value;
        }

        public JsonNode Set(string key, double v) => Set(key, From(v));
        public JsonNode Set(string key, string v) => Set(key, From(v));
        public JsonNode Set(string key, bool v) => Set(key, From(v));

        public bool Remove(string key)
        {
            if (Kind != JsonKind.Object) return false;
            for (int i = 0; i < Props.Count; i++)
                if (Props[i].Key == key) { Props.RemoveAt(i); return true; }
            return false;
        }

        public JsonNode Add(JsonNode v) { Items.Add(v); return v; }

        public float AsFloat(float def = 0f) => Kind == JsonKind.Number ? (float)Number : Kind == JsonKind.Bool ? (Bool ? 1f : 0f) : def;
        public int AsInt(int def = 0) => Kind == JsonKind.Number ? (int)Math.Round(Number) : def;
        public bool AsBool(bool def = false) => Kind == JsonKind.Bool ? Bool : Kind == JsonKind.Number ? Number != 0 : def;
        public string AsString(string def = null) => Kind == JsonKind.String ? String : Kind == JsonKind.Number ? Number.ToString(CultureInfo.InvariantCulture) : def;

        public JsonNode Clone()
        {
            switch (Kind)
            {
                case JsonKind.Array:
                    var a = NewArray();
                    foreach (var it in Items) a.Items.Add(it.Clone());
                    return a;
                case JsonKind.Object:
                    var o = NewObject();
                    foreach (var kv in Props) o.Props.Add(new KeyValuePair<string, JsonNode>(kv.Key, kv.Value.Clone()));
                    return o;
                default:
                    return new JsonNode { Kind = Kind, Bool = Bool, Number = Number, String = String };
            }
        }

        public bool DeepEquals(JsonNode other)
        {
            if (other == null || other.Kind != Kind) return false;
            switch (Kind)
            {
                case JsonKind.Null: return true;
                case JsonKind.Bool: return Bool == other.Bool;
                case JsonKind.Number: return Math.Abs(Number - other.Number) < 1e-12;
                case JsonKind.String: return String == other.String;
                case JsonKind.Array:
                    if (Items.Count != other.Items.Count) return false;
                    for (int i = 0; i < Items.Count; i++) if (!Items[i].DeepEquals(other.Items[i])) return false;
                    return true;
                default:
                    if (Props.Count != other.Props.Count) return false;
                    foreach (var kv in Props) { var o = other.Get(kv.Key); if (o == null || !kv.Value.DeepEquals(o)) return false; }
                    return true;
            }
        }

        public override string ToString() => SoddJson.Write(this, true);
    }

    public class JsonParseException : Exception
    {
        public readonly int Line, Column;
        public JsonParseException(string msg, int line, int col) : base($"JSON inválido (línea {line}, columna {col}): {msg}") { Line = line; Column = col; }
    }

    public static class SoddJson
    {
        // ---------------------------------------------------------------- Parse
        public static JsonNode Parse(string text)
        {
            if (text == null) throw new JsonParseException("texto vacío", 1, 1);
            var p = new Parser(text);
            p.SkipWs();
            var v = p.ParseValue();
            p.SkipWs();
            if (!p.End) p.Fail("contenido extra después del valor raíz");
            return v;
        }

        public static bool TryParse(string text, out JsonNode node, out string error)
        {
            try { node = Parse(text); error = null; return true; }
            catch (JsonParseException e) { node = null; error = e.Message; return false; }
        }

        struct Parser
        {
            readonly string s; int i;
            public Parser(string text) { s = text; i = 0; }
            public bool End => i >= s.Length;

            public void Fail(string msg)
            {
                int line = 1, col = 1;
                for (int k = 0; k < i && k < s.Length; k++) { if (s[k] == '\n') { line++; col = 1; } else col++; }
                throw new JsonParseException(msg, line, col);
            }

            public void SkipWs()
            {
                while (i < s.Length)
                {
                    char c = s[i];
                    if (c == ' ' || c == '\t' || c == '\n' || c == '\r' || c == '﻿') { i++; continue; }
                    // Tolerancia: comentarios // y /* */ (útil para humanos/IA), se descartan al reescribir.
                    if (c == '/' && i + 1 < s.Length && s[i + 1] == '/') { while (i < s.Length && s[i] != '\n') i++; continue; }
                    if (c == '/' && i + 1 < s.Length && s[i + 1] == '*') { int e = s.IndexOf("*/", i + 2, StringComparison.Ordinal); i = e < 0 ? s.Length : e + 2; continue; }
                    break;
                }
            }

            public JsonNode ParseValue()
            {
                if (End) Fail("se esperaba un valor");
                char c = s[i];
                switch (c)
                {
                    case '{': return ParseObject();
                    case '[': return ParseArray();
                    case '"': return JsonNode.From(ParseString());
                    case 't': Expect("true"); return JsonNode.From(true);
                    case 'f': Expect("false"); return JsonNode.From(false);
                    case 'n': Expect("null"); return JsonNode.Null();
                    default:
                        if (c == '-' || (c >= '0' && c <= '9')) return JsonNode.From(ParseNumber());
                        Fail($"carácter inesperado '{c}'");
                        return null;
                }
            }

            void Expect(string word)
            {
                if (string.CompareOrdinal(s, i, word, 0, word.Length) != 0) Fail($"se esperaba '{word}'");
                i += word.Length;
            }

            JsonNode ParseObject()
            {
                var o = JsonNode.NewObject();
                i++; SkipWs();
                if (!End && s[i] == '}') { i++; return o; }
                while (true)
                {
                    SkipWs();
                    if (End || s[i] != '"') Fail("se esperaba una clave entre comillas");
                    string key = ParseString();
                    SkipWs();
                    if (End || s[i] != ':') Fail($"se esperaba ':' después de \"{key}\"");
                    i++; SkipWs();
                    var v = ParseValue();
                    o.Props.Add(new KeyValuePair<string, JsonNode>(key, v));
                    SkipWs();
                    if (End) Fail("objeto sin cerrar");
                    if (s[i] == ',') { i++; SkipWs(); if (!End && s[i] == '}') { i++; return o; } continue; }
                    if (s[i] == '}') { i++; return o; }
                    Fail("se esperaba ',' o '}'");
                }
            }

            JsonNode ParseArray()
            {
                var a = JsonNode.NewArray();
                i++; SkipWs();
                if (!End && s[i] == ']') { i++; return a; }
                while (true)
                {
                    SkipWs();
                    a.Items.Add(ParseValue());
                    SkipWs();
                    if (End) Fail("array sin cerrar");
                    if (s[i] == ',') { i++; SkipWs(); if (!End && s[i] == ']') { i++; return a; } continue; }
                    if (s[i] == ']') { i++; return a; }
                    Fail("se esperaba ',' o ']'");
                }
            }

            string ParseString()
            {
                i++;
                var sb = new StringBuilder();
                while (true)
                {
                    if (End) Fail("string sin cerrar");
                    char c = s[i++];
                    if (c == '"') break;
                    if (c != '\\') { sb.Append(c); continue; }
                    if (End) Fail("escape incompleto");
                    char e = s[i++];
                    switch (e)
                    {
                        case '"': sb.Append('"'); break;
                        case '\\': sb.Append('\\'); break;
                        case '/': sb.Append('/'); break;
                        case 'b': sb.Append('\b'); break;
                        case 'f': sb.Append('\f'); break;
                        case 'n': sb.Append('\n'); break;
                        case 'r': sb.Append('\r'); break;
                        case 't': sb.Append('\t'); break;
                        case 'u':
                            if (i + 4 > s.Length) Fail("escape \\u incompleto");
                            sb.Append((char)Convert.ToInt32(s.Substring(i, 4), 16)); i += 4; break;
                        default: Fail($"escape desconocido \\{e}"); break;
                    }
                }
                return sb.ToString();
            }

            double ParseNumber()
            {
                int st = i;
                if (s[i] == '-') i++;
                while (i < s.Length && (char.IsDigit(s[i]) || s[i] == '.' || s[i] == 'e' || s[i] == 'E' || s[i] == '+' || s[i] == '-')) i++;
                if (!double.TryParse(s.Substring(st, i - st), NumberStyles.Float, CultureInfo.InvariantCulture, out var d))
                    Fail("número inválido");
                return d;
            }
        }

        // ---------------------------------------------------------------- Write
        public static string Write(JsonNode n, bool pretty = true)
        {
            var sb = new StringBuilder();
            WriteNode(sb, n, pretty, 0);
            if (pretty) sb.Append('\n');
            return sb.ToString();
        }

        static bool IsSimple(JsonNode n)
        {
            // Arrays cortos de escalares (ej. puntos de curva [0,1]) van en una sola línea.
            if (n.Kind == JsonKind.Array)
            {
                if (n.Items.Count > 8) return false;
                foreach (var it in n.Items)
                {
                    if (it.Kind == JsonKind.Object) return false;
                    if (it.Kind == JsonKind.Array && !IsSimple(it)) return false;
                    if (it.Kind == JsonKind.String && it.String.Length > 40) return false;
                }
                return true;
            }
            return n.Kind != JsonKind.Object || n.Props.Count == 0;
        }

        static void WriteNode(StringBuilder sb, JsonNode n, bool pretty, int indent)
        {
            switch (n.Kind)
            {
                case JsonKind.Null: sb.Append("null"); break;
                case JsonKind.Bool: sb.Append(n.Bool ? "true" : "false"); break;
                case JsonKind.Number: sb.Append(FormatNumber(n.Number)); break;
                case JsonKind.String: WriteString(sb, n.String); break;
                case JsonKind.Array:
                    if (n.Items.Count == 0) { sb.Append("[]"); break; }
                    if (!pretty || IsSimple(n))
                    {
                        sb.Append('[');
                        for (int k = 0; k < n.Items.Count; k++) { if (k > 0) sb.Append(pretty ? ", " : ","); WriteNode(sb, n.Items[k], pretty, indent); }
                        sb.Append(']');
                        break;
                    }
                    sb.Append('[');
                    for (int k = 0; k < n.Items.Count; k++)
                    {
                        if (k > 0) sb.Append(',');
                        sb.Append('\n').Append(' ', (indent + 1) * 2);
                        WriteNode(sb, n.Items[k], pretty, indent + 1);
                    }
                    sb.Append('\n').Append(' ', indent * 2).Append(']');
                    break;
                case JsonKind.Object:
                    if (n.Props.Count == 0) { sb.Append("{}"); break; }
                    sb.Append('{');
                    for (int k = 0; k < n.Props.Count; k++)
                    {
                        if (k > 0) sb.Append(',');
                        if (pretty) sb.Append('\n').Append(' ', (indent + 1) * 2);
                        WriteString(sb, n.Props[k].Key);
                        sb.Append(pretty ? ": " : ":");
                        WriteNode(sb, n.Props[k].Value, pretty, indent + 1);
                    }
                    if (pretty) sb.Append('\n').Append(' ', indent * 2);
                    sb.Append('}');
                    break;
            }
        }

        public static string FormatNumber(double d)
        {
            if (double.IsNaN(d) || double.IsInfinity(d)) return "0";
            if (Math.Abs(d - Math.Round(d)) < 1e-9 && Math.Abs(d) < 1e15) return ((long)Math.Round(d)).ToString(CultureInfo.InvariantCulture);
            return Math.Round(d, 6).ToString("0.######", CultureInfo.InvariantCulture);
        }

        static void WriteString(StringBuilder sb, string s)
        {
            sb.Append('"');
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
        }

        // ---------------------------------------------------------------- JSON Pointer (RFC 6901)
        public static string[] SplitPointer(string pointer)
        {
            if (string.IsNullOrEmpty(pointer) || pointer == "/") return Array.Empty<string>();
            if (pointer[0] != '/') pointer = "/" + pointer;
            var parts = pointer.Substring(1).Split('/');
            for (int k = 0; k < parts.Length; k++) parts[k] = parts[k].Replace("~1", "/").Replace("~0", "~");
            return parts;
        }

        public static string EscapeToken(string t) => t.Replace("~", "~0").Replace("/", "~1");

        public static JsonNode Resolve(JsonNode root, string pointer)
        {
            var cur = root;
            foreach (var part in SplitPointer(pointer))
            {
                if (cur == null) return null;
                if (cur.IsObject) cur = cur.Get(part);
                else if (cur.IsArray) cur = int.TryParse(part, out int idx) ? cur[idx] : null;
                else return null;
            }
            return cur;
        }

        /// <summary>Asigna un valor en el pointer. Crea objetos intermedios si faltan. "-" agrega al final de un array.</summary>
        public static bool SetAt(JsonNode root, string pointer, JsonNode value, out string error, bool insert = false)
        {
            error = null;
            var parts = SplitPointer(pointer);
            if (parts.Length == 0) { error = "No se puede reemplazar la raíz"; return false; }
            var cur = root;
            for (int k = 0; k < parts.Length - 1; k++)
            {
                JsonNode next = cur.IsObject ? cur.Get(parts[k]) : cur.IsArray && int.TryParse(parts[k], out int ix) ? cur[ix] : null;
                if (next == null)
                {
                    if (!cur.IsObject) { error = $"Ruta inválida en '{parts[k]}'"; return false; }
                    next = cur.Set(parts[k], JsonNode.NewObject());
                }
                cur = next;
            }
            string last = parts[parts.Length - 1];
            if (cur.IsObject) { cur.Set(last, value); return true; }
            if (cur.IsArray)
            {
                if (last == "-") { cur.Items.Add(value); return true; }
                if (!int.TryParse(last, out int idx) || idx < 0 || idx > cur.Items.Count) { error = $"Índice fuera de rango: {last}"; return false; }
                if (insert || idx == cur.Items.Count) cur.Items.Insert(idx, value);
                else cur.Items[idx] = value;
                return true;
            }
            error = "El padre no es objeto ni array";
            return false;
        }

        public static bool RemoveAt(JsonNode root, string pointer, out string error)
        {
            error = null;
            var parts = SplitPointer(pointer);
            if (parts.Length == 0) { error = "No se puede borrar la raíz"; return false; }
            var parentPtr = parts.Length == 1 ? "" : "/" + string.Join("/", Array.ConvertAll(parts, EscapeToken), 0, parts.Length - 1);
            var parent = Resolve(root, parentPtr);
            string last = parts[parts.Length - 1];
            if (parent != null && parent.IsObject && parent.Remove(last)) return true;
            if (parent != null && parent.IsArray && int.TryParse(last, out int idx) && idx >= 0 && idx < parent.Items.Count) { parent.Items.RemoveAt(idx); return true; }
            error = $"No existe '{pointer}'";
            return false;
        }

        // ---------------------------------------------------------------- JSON Patch (RFC 6902)
        public static bool ApplyPatch(JsonNode root, JsonNode patch, out string error)
        {
            error = null;
            if (patch == null || !patch.IsArray) { error = "El patch debe ser un array de operaciones"; return false; }
            foreach (var op in patch.Items)
            {
                string kind = op["op"]?.AsString();
                string path = op["path"]?.AsString();
                if (kind == null || path == null) { error = "Cada operación necesita 'op' y 'path'"; return false; }
                switch (kind)
                {
                    case "add":
                        if (!SetAt(root, path, (op["value"] ?? JsonNode.Null()).Clone(), out error, insert: true)) return false;
                        break;
                    case "replace":
                        if (Resolve(root, path) == null) { error = $"replace: no existe '{path}'"; return false; }
                        if (!SetAt(root, path, (op["value"] ?? JsonNode.Null()).Clone(), out error)) return false;
                        break;
                    case "remove":
                        if (!RemoveAt(root, path, out error)) return false;
                        break;
                    case "move":
                    case "copy":
                        {
                            string from = op["from"]?.AsString();
                            var src = from == null ? null : Resolve(root, from);
                            if (src == null) { error = $"{kind}: no existe '{from}'"; return false; }
                            src = src.Clone();
                            if (kind == "move" && !RemoveAt(root, from, out error)) return false;
                            if (!SetAt(root, path, src, out error, insert: true)) return false;
                            break;
                        }
                    case "test":
                        {
                            var cur = Resolve(root, path);
                            if (cur == null || !cur.DeepEquals(op["value"])) { error = $"test falló en '{path}'"; return false; }
                            break;
                        }
                    default:
                        error = $"Operación desconocida '{kind}'";
                        return false;
                }
            }
            return true;
        }
    }
}
