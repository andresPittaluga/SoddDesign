# sodddesign-mcp

Servidor MCP (stdio) para que una IA (Claude Code, Claude Desktop, etc.) cree y edite eventos de audio de
SoddDesign dentro de Unity.

```
IA ──MCP stdio──► sodddesign-mcp (Node) ──HTTP JSON-RPC 127.0.0.1 + token──► Unity Editor (SoddBridge) ──► SoddCommands
```

- El bridge del Editor arranca solo al abrir el proyecto (`Window > Sodd > AI Bridge > Start/Stop`).
  Escucha **solo en 127.0.0.1** (puerto 47821, configurable con EditorPrefs `SoddBridge.Port`) y genera un token
  nuevo por sesión en `Library/SoddDesign/bridge.json`. Rechaza requests de navegadores y Hosts no locales.
- Todo cambio pasa por `SoddCommands`: se valida, se guarda en el `.sodd.json` y se puede deshacer con Ctrl+Z.

## Instalar en Claude Code

`dist/index.js` ya viene compilado (un solo archivo, sin `node_modules`). Requiere Node 18+.

```bash
claude mcp add sodddesign -- node "<RUTA_PROYECTO>/Packages/com.sodd.sodddesign/Tools~/sodddesign-mcp/dist/index.js" --project "<RUTA_PROYECTO>"
```

Atajo: en Unity, `Window > Sodd > AI Bridge > Copy Claude Code command` copia el comando exacto al portapapeles.

Probar la conexión (con Unity abierto):
```bash
node dist/index.js --project "<RUTA_PROYECTO>" --check
```

## Tools
`list_events`, `get_event`, `create_event`, `edit_event` (JSON Patch), `set_property`, `delete_event`, `move_event`,
`add_track`, `add_region`, `add_effect`, `add_marker`, `add_loop_region`, `add_modulator`, `remove_modulator`,
`list_parameters`, `add_parameter`, `list_audio_clips`, `list_effects`, `get_settings`, `edit_settings`,
`preview_event`, `stop_preview`, `validate`, `get_schema`, `get_ai_guide`, `open_in_studio`.

## Desarrollo
```bash
npm install
npm run build        # typecheck + bundle con esbuild → dist/index.js
node test-stdio.mjs  # smoke test del protocolo (no requiere Unity)
```
