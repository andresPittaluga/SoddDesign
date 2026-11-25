#!/usr/bin/env node
/**
 * sodddesign-mcp — servidor MCP (stdio) para SoddDesign.
 * Habla con el bridge local del Editor de Unity (JSON-RPC en 127.0.0.1, token en Library/SoddDesign/bridge.json).
 *
 * Uso:  node dist/index.js [--project <ruta del proyecto Unity>]
 * Si no se pasa --project, se usa SODD_PROJECT o se busca hacia arriba desde este archivo y desde el cwd.
 */
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { StdioServerTransport } from "@modelcontextprotocol/sdk/server/stdio.js";
import { z } from "zod";
import { existsSync, readFileSync } from "node:fs";
import { dirname, join, resolve } from "node:path";
import { fileURLToPath } from "node:url";

const VERSION = "0.1.0";

// ------------------------------------------------------------------ localizar proyecto y bridge
function argValue(name: string): string | undefined {
  const i = process.argv.indexOf(name);
  return i >= 0 ? process.argv[i + 1] : undefined;
}

function findProject(): string | undefined {
  const explicit = argValue("--project") ?? process.env.SODD_PROJECT;
  if (explicit) return resolve(explicit);
  const starts = [dirname(fileURLToPath(import.meta.url)), process.cwd()];
  for (const start of starts) {
    let dir = start;
    for (let i = 0; i < 10; i++) {
      if (existsSync(join(dir, "ProjectSettings")) && existsSync(join(dir, "Assets"))) return dir;
      const up = dirname(dir);
      if (up === dir) break;
      dir = up;
    }
  }
  return undefined;
}

const PROJECT = findProject();

interface BridgeConfig { port: number; token: string; host: string; project: string }

function readConfig(): BridgeConfig {
  if (!PROJECT) throw new Error("No encontré el proyecto Unity. Pasá --project <ruta> o definí SODD_PROJECT.");
  const file = join(PROJECT, "Library", "SoddDesign", "bridge.json");
  if (!existsSync(file))
    throw new Error(`El bridge de SoddDesign no está activo (falta ${file}). Abrí Unity con el proyecto; ` +
      "el bridge arranca solo, o usá Sodd > AI Bridge > Start.");
  return JSON.parse(readFileSync(file, "utf8")) as BridgeConfig;
}

let nextId = 1;

interface SoddResult { ok: boolean; errors: string[]; warnings: string[]; data?: unknown }

async function rpc(method: string, params: Record<string, unknown> = {}): Promise<SoddResult> {
  const cfg = readConfig();
  const res = await fetch(`http://127.0.0.1:${cfg.port}/`, {
    method: "POST",
    headers: { "Content-Type": "application/json", Authorization: `Bearer ${cfg.token}` },
    body: JSON.stringify({ jsonrpc: "2.0", id: nextId++, method, params }),
  }).catch((e: Error) => {
    throw new Error(`No pude conectar con Unity en 127.0.0.1:${cfg.port} (${e.message}). ¿Está abierto el Editor?`);
  });
  const body = (await res.json()) as { result?: SoddResult; error?: { message: string } };
  if (body.error) throw new Error(body.error.message);
  return body.result as SoddResult;
}

// ------------------------------------------------------------------ servidor
const server = new McpServer(
  { name: "sodddesign", version: VERSION },
  {
    instructions:
      "SoddDesign es un middleware de audio para Unity (estilo FMOD). Los eventos son archivos JSON (.sodd.json). " +
      "Antes de diseñar sonido llamá a get_ai_guide (traduce pedidos vagos como 'más grave' o 'que suene lejos' a técnica) " +
      "y a get_schema si necesitás el formato exacto. Flujo típico: list_audio_clips → create_event → add_track → add_region → " +
      "add_modulator/add_effect → validate → preview_event (el humano escucha) → ajustar. " +
      "Volumen en dB, pitch en semitonos. Los clips se pueden referenciar por path o por nombre corto ('kick').",
  },
);

type Args = Record<string, unknown>;

function asText(r: SoddResult) {
  const parts: string[] = [];
  parts.push(r.ok ? "OK" : "ERROR");
  for (const e of r.errors ?? []) parts.push("✖ " + e);
  for (const w of r.warnings ?? []) parts.push("⚠ " + w);
  if (r.data !== undefined) parts.push(typeof r.data === "string" ? r.data : JSON.stringify(r.data, null, 2));
  return { content: [{ type: "text" as const, text: parts.join("\n") }], isError: !r.ok };
}

function tool(name: string, description: string, shape: z.ZodRawShape, method = name, map?: (a: Args) => Args) {
  server.registerTool(name, { description, inputSchema: shape }, async (args: Args) => {
    try {
      return asText(await rpc(method, map ? map(args) : args));
    } catch (e) {
      return { content: [{ type: "text" as const, text: (e as Error).message }], isError: true };
    }
  });
}

const ev = z.string().describe("Evento: id (GUID) o path, ej. 'sfx/player/jump'");
const json = z.any();

tool("list_events", "Lista los eventos del proyecto (id, path, descripción, tags, 3D, bus, duración). Filtro opcional por texto o tag.",
  { filter: z.string().optional().describe("Texto o tag para filtrar") });
tool("get_event", "Devuelve el JSON completo de un evento (.sodd.json).", { event: ev });
tool("create_event",
  "Crea un evento. Pasá 'path' (+ description, is3D, bus) para uno vacío, o 'event' con el JSON completo (si falta id se genera).",
  {
    path: z.string().optional().describe("Path con carpetas, ej. 'sfx/weapons/laser'"),
    description: z.string().optional().describe("Cómo suena y cuándo se usa"),
    is3D: z.boolean().optional(),
    bus: z.string().optional().describe("Master, Music, SFX, UI, Voice, Ambience"),
    event: json.optional().describe("JSON completo del evento (ver get_schema)"),
  });
tool("edit_event",
  "Edita un evento con JSON Patch (RFC 6902): [{op:'replace',path:'/tracks/0/volume',value:-6}, ...]. Valida antes de guardar; si hay errores no se escribe nada. Con Undo en Unity.",
  { event: ev, patch: json.describe("Array de operaciones JSON Patch") });
tool("set_property",
  "Cambia una propiedad por JSON Pointer, ej. pointer '/pitch' value -3. Si la propiedad tiene moduladores, un número cambia solo el 'base'.",
  { event: ev, pointer: z.string(), value: json.describe("Valor JSON") });
tool("delete_event", "Borra un evento (se puede deshacer con Ctrl+Z en Unity).", { event: ev });
tool("move_event", "Cambia el path del evento (y mueve el archivo).", { event: ev, newPath: z.string() });
tool("add_track", "Agrega un track al evento.", {
  event: ev, name: z.string(), type: z.enum(["SFX", "Music", "Voice", "Ambience"]).optional(),
});
tool("add_region",
  "Agrega una región a un track. instrument ej.: {type:'SingleClip',clip:'kick'} · {type:'RandomClip',avoidRepeat:true,clips:['step1','step2']} · {type:'Silence'} · {type:'NestedEvent',nestedEvent:'sfx/x'}.",
  {
    event: ev, track: z.number().int().describe("Índice del track (0 = primero)"),
    start: z.number().describe("Segundos"), length: z.number().describe("Segundos"),
    instrument: json.describe("Objeto instrumento"), name: z.string().optional(),
  });
tool("add_effect", "Agrega un efecto a un track: 'LowPass' o {type:'LowPass',params:{cutoff:800}}. Ver list_effects.", {
  event: ev, track: z.number().int(), effect: json,
});
tool("add_marker", "Agrega un marker (llega al juego como callback OnMarker).", { event: ev, name: z.string(), time: z.number() });
tool("move_marker", "Mueve un marker (index = posición en la lista markers) a otro tiempo en segundos.", { event: ev, index: z.number().int(), time: z.number() });
tool("rename_marker", "Renombra un marker por índice.", { event: ev, index: z.number().int(), name: z.string() });
tool("remove_marker", "Borra un marker por índice.", { event: ev, index: z.number().int() });
tool("move_track", "Mueve un track a otra posición (from y to son índices; to es la posición final).", { event: ev, from: z.number().int(), to: z.number().int() });
tool("duplicate_track", "Duplica un track con sus regiones y efectos, justo debajo del original.", { event: ev, track: z.number().int() });
tool("add_loop_region", "Agrega un loop. exitWhen opcional: {parameter:'Intensity',op:'Greater',value:0.9}.", {
  event: ev, start: z.number(), end: z.number(), exitWhen: json.optional(), name: z.string().optional(),
});
tool("add_modulator",
  "Agrega un modulador a una propiedad numérica (pointer '/pitch', '/volume', '/tracks/0/volume', '/tracks/0/effects/0/params/cutoff'...). " +
  "Tipos: Random{min,max,unit}, ParameterCurve{parameter,curve:[[x,y]]}, RandomScaledByParameter{parameter,range,curve}, LFO{shape,rate,depth}, Envelope{attack,hold,release}, ADSR{attack,decay,sustain,release}.",
  { event: ev, pointer: z.string(), modulator: json });
tool("remove_modulator", "Quita un modulador por índice.", { event: ev, pointer: z.string(), index: z.number().int() });
tool("list_parameters", "Lista los parámetros globales/locales registrados (sodd.parameters.json) con rango y descripción.", {});
tool("add_parameter", "Agrega un parámetro. Sin 'event' = global (sodd.parameters.json); con 'event' = local del evento.", {
  event: ev.optional(),
  parameter: json.describe("{name, min, max, default, seekSpeed, description}"),
});
tool("list_audio_clips", "Lista AudioClips del proyecto (path, guid, duración, canales, tags por carpeta/labels).", {
  filter: z.string().optional().describe("Texto de búsqueda (nombre)"),
});
tool("list_effects", "Catálogo de efectos con parámetros, rangos y descripciones.", {});
tool("get_settings", "Devuelve sodd.settings.json (buses, snapshots, voces).", {});
tool("edit_settings", "Edita sodd.settings.json con JSON Patch (buses, snapshots).", { patch: json });
tool("preview_event", "Reproduce un evento en el Editor (sin Play Mode) para que el humano lo escuche. parameters opcional: {Health:20}.", {
  event: ev, parameters: json.optional(),
});
tool("stop_preview", "Detiene todos los previews.", {});
tool("validate", "Valida un evento (o todos si no se pasa event): schema, parámetros, clips, buses, ids duplicados.", { event: ev.optional() });
tool("get_schema", "Devuelve el JSON Schema de los eventos (con descripción de cada campo).", {});
tool("get_ai_guide", "Guía para traducir pedidos vagos ('más grave', 'que suene lejos', 'que no se repita') a técnica concreta de SoddDesign.", {});
tool("open_in_studio", "Abre el evento en SoddDesign Studio para que el humano lo vea.", { event: ev });

async function main() {
  if (process.argv.includes("--check")) {
    const r = await rpc("ping");
    console.log(JSON.stringify(r));
    return;
  }
  await server.connect(new StdioServerTransport());
}

main().catch((e) => {
  console.error("[sodddesign-mcp]", e);
  process.exit(1);
});
