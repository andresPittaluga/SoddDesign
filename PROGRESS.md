# SoddDesign — PROGRESS

> Para retomar en otra sesión: "Leé CLAUDE.md y PROGRESS.md y continuá".

## Entorno
- Unity: `C:\Program Files\Unity\Hub\Editor\6000.3.5f2\Editor\Unity.exe` (6000.3.5f2)
- Proyecto: `C:\Users\andys\Documents\SoddDesign`
- Python 3.12 (`python`), Node 24 (`node`).
- **CI local**: `bash Tools/ci.sh [EditMode|PlayMode|both|compile]`.
  Copia Assets/Packages/ProjectSettings a `_ci/project` (espejo, gitignored) y corre Unity en batchmode
  ahí, porque el humano suele tener el Editor abierto con el proyecto (batchmode no puede abrir un
  proyecto ya abierto). Al final copia de vuelta los `.meta` nuevos que generó Unity.
  Logs en `_ci/log-*.txt`, resultados en `_ci/results-*.xml`. Primera corrida ~3 min, después ~1 min.

## Estado de fases
- [x] Fase 0 — Setup
- [ ] Fase 1 — Runtime MVP
- [ ] Fase 2 — Timeline, loops, markers, parámetros, SoddCommands
- [ ] Fase 3 — Efectos, buses, mixer, snapshots, reverb zones
- [ ] Fase 4 — Mockup UI → Studio + Mixer + Profiler
- [ ] Fase 5 — Bridge + MCP + AI_GUIDE
- [ ] Fase 6 — Netcode
- [ ] Fase 7 — Addressables, docs, samples, v0.1.0
- [ ] Fase 8 — Warpmart (solo cuando el humano lo pida)

## Decisiones
- Paquete embebido en `Packages/com.sodd.sodddesign` + `"testables"` en el manifest para correr sus tests.
- Audios de prueba: `Tools/gen_test_audio.py` (determinista) → `Assets/Sandbox/Audio/*.wav`.
- Datos del "juego" (eventos, parámetros, settings) en `Assets/SoddDesign/`.

## QUÉ PROBAR EN UNITY
### Fase 0
1. Volvé al Editor (va a importar el paquete nuevo).
2. En Project: `Packages/SoddDesign` debe aparecer; `Assets/Sandbox/Audio` tiene 16 wavs
   (beeps, kick, snare, footsteps, music_loop_120bpm, drone...). Hacé click en alguno y escuchalo
   en el Inspector.
3. Window > General > Test Runner > EditMode: debe aparecer `Sodd.Design.Tests` con 1 test en verde.
