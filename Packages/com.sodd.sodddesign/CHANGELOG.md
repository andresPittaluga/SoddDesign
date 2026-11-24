# Changelog

## [0.1.0] - 2026-11-24
Primera versión.
- Runtime: eventos JSON (`*.sodd.json`) con importer + JSON Schema, timeline sample-accurate, loops con condición
  de salida, markers, moduladores (Random, ParameterCurve, RandomScaledByParameter, LFO, Envelope, ADSR), pool de
  voces con robo por prioridad, maxInstances/stealMode/cooldown, audio 3D, buses, snapshots, efectos nativos y DSP
  propios, SoddMixer + reverb zones, bancos (+ Addressables opcional).
- API: `SoddAudio`, `SoddEventInstance` (handle seguro), `SoddEventReference`, `SoddEmitter`, `SoddListener`.
- Editor: SoddDesign Studio (UI Toolkit), Mixer, Profiler, preview sin Play Mode, live update, optimizador de import settings.
- `SoddCommands` con Undo + validación; bridge JSON-RPC local con token; servidor MCP (26 tools); AI_GUIDE.
- Netcode for GameObjects 2.x opcional: `SoddNetworkAudio`, `SoddNetworkEmitter`, `SoddAudio.Net`.
