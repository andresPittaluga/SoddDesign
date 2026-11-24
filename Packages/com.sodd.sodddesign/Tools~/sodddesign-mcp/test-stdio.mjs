// Smoke test del protocolo MCP: initialize + tools/list por stdio (no requiere Unity).
import { spawn } from "node:child_process";
const p = spawn(process.execPath, ["dist/index.js"], { stdio: ["pipe", "pipe", "inherit"] });
let buf = "";
const send = (o) => p.stdin.write(JSON.stringify(o) + "\n");
p.stdout.on("data", (d) => {
  buf += d;
  for (const line of buf.split("\n").slice(0, -1)) {
    const m = JSON.parse(line);
    if (m.id === 1) { send({ jsonrpc: "2.0", method: "notifications/initialized" }); send({ jsonrpc: "2.0", id: 2, method: "tools/list" }); }
    if (m.id === 2) {
      const names = m.result.tools.map((t) => t.name);
      console.log(names.length + " tools:", names.join(", "));
      const req = ["list_events","get_event","create_event","edit_event","add_modulator","list_parameters","add_parameter","list_audio_clips","preview_event","stop_preview","validate","get_schema"];
      const missing = req.filter((n) => !names.includes(n));
      if (missing.length) { console.error("FALTAN:", missing); process.exit(1); }
      send({ jsonrpc: "2.0", id: 3, method: "tools/call", params: { name: "list_events", arguments: {} } });
    }
    if (m.id === 3) { console.log("call sin Unity →", m.result.isError ? "error controlado: " + m.result.content[0].text.slice(0, 60) : "ok"); p.kill(); process.exit(0); }
  }
  buf = buf.split("\n").pop();
});
send({ jsonrpc: "2.0", id: 1, method: "initialize", params: { protocolVersion: "2025-06-18", capabilities: {}, clientInfo: { name: "test", version: "1" } } });
setTimeout(() => { console.error("timeout"); process.exit(1); }, 10000);
