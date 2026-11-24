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
- [x] Fase 1 — Runtime MVP (33 EditMode + 2 PlayMode tests en verde)
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
- JSON propio (`Runtime/Json/SoddJson.cs`): parser con línea/columna en errores, tolera comentarios y comas
  finales, preserva orden de claves, JSON Pointer y JSON Patch (RFC 6902). JsonUtility no sirve (curvas [[x,y]],
  props número-o-objeto).
- El JSON se mapea a mano (`SoddReader`) → valida con rutas JSON y sugerencias ("¿Quisiste decir...?", con
  sinónimos en español: Vida→Health). Claves desconocidas = warning; refs a parámetros inexistentes = error.
- Una ModProp acepta número (atajo) u objeto `{base, modulators}`. Unidades: volume en dB, pitch en semitonos.
  Modulador `unit: Auto` = la unidad natural de la propiedad. `range: x` = atajo de min=-x,max=+x.
- Importer: `[ScriptedImporter] "sodd.json"` → `SoddEventAsset` como objeto principal. Clips resueltos por GUID
  (fallback path) y guardados en `asset.clips[]`; `clipIndex` en cada SoddClipRef. Depende de sodd.parameters.json.
- `sodd.parameters.json` y `sodd.settings.json` se importan como TextAsset; el `SoddRegistry`
  (`Assets/SoddDesign/Resources/SoddRegistry.asset`, lo mantiene un AssetPostprocessor) referencia eventos +
  ambos JSON. El runtime parsea los JSON una vez al iniciar.
- Motor = clase `SoddEngine` (no MonoBehaviour) → testeable con reloj dsp simulado y voces "headless".
  `SoddAudioManager` (auto-init BeforeSceneLoad) solo lo maneja. Update sin allocations (hay test que lo mide).
- Handle `SoddEventInstance` = struct {engine, index, generation}: seguro tras Release.
- Prioridad estilo Unity: 0 = más importante, 256 = menos. Stealing: roba la voz de peor prioridad y, a igual
  prioridad, la menos audible. Si no puede, la región queda "virtual" (se cuenta, no se resucita).
- Scheduler: cursor de timeline + mapeo dsp por segmentos (para el playhead con loops). Lookahead 100 ms.
  Las condiciones de salida de loop se evalúan al llegar al final del loop (≈100 ms antes, por el lookahead).
- Stop(AllowFadeOut): usa el release del ADSR del volumen del evento si hay; si no, fade lineal de 50 ms.
- Pausa (global/bus/instancia): congela el reloj de la instancia y desplaza sus anclajes dsp al reanudar.
- Snapshots lógicos (volumen/lowpass/highpass/reverb por bus) interpolados; opcionalmente TransitionTo del mixer.
- Sandbox: `Assets/Sandbox/Sandbox.unity` se genera con `Sodd/Sandbox/Rebuild Sandbox Scene`
  (también por batchmode: `bash Tools/ci.sh exec SandboxSceneBuilder.Build`). Panel de debug con OnGUI
  (runtime, no es UI del Editor). Usa el Input System nuevo (el proyecto lo tiene activo).
- Datos del Sandbox regenerables: `python Tools/gen_sandbox_data.py`; schema: `python Tools/gen_schema.py`.

## QUÉ PROBAR EN UNITY
### Fase 0
1. Volvé al Editor (va a importar el paquete nuevo).
2. En Project: `Packages/SoddDesign` debe aparecer; `Assets/Sandbox/Audio` tiene 16 wavs
   (beeps, kick, snare, footsteps, music_loop_120bpm, drone...). Hacé click en alguno y escuchalo
   en el Inspector.
3. Window > General > Test Runner > EditMode: debe aparecer `Sodd.Design.Tests` con tests en verde.

### Fase 1 (≈2 min)
1. Abrí `Assets/Sandbox/Sandbox.unity` y apretá Play. Arriba a la izquierda aparece el panel "SoddDesign Sandbox".
2. Al arrancar tiene que sonar un **drone** grave que "respira" (cubo de la izquierda, 3D: si movés la cámara
   en la Scene se nota la distancia) con un fade-in de 1.5 s.
3. Apretá **1** varias veces: click de UI con leve variación de tono.
4. Apretá **2** rápido varias veces: pasos, nunca el mismo dos veces seguidas, con tono/volumen variable.
5. Apretá **3**: salto. Bajá el slider **Health** a 0 y apretá 3 varias veces: el tono se vuelve inestable.
   Con Health en 100 siempre suena igual.
6. Apretá **4** varias veces: golpe en 3 capas que viene de la esfera que orbita (se mueve de lado a lado).
7. Apretá **M**: loop musical. Subí **Intensity**: aparece el drone musical. Llevalo a > 0.9: al terminar el
   compás sale del loop, toca un golpe final y se detiene. Abajo del panel se ven los markers Bar1/Bar2.
8. Botones **Underwater / Menú pausa / Normal**: hoy cambian el volumen de los buses (el filtro lowpass
   llega en la Fase 3). **Espacio** pausa/reanuda todo.
9. Seleccioná el cubo "Drone Emitter": en la Scene se ven las esferas de min/max distance; en el Inspector el
   campo Event Ref tiene un buscador de eventos.
10. Abrí cualquier `Assets/SoddDesign/Events/**/*.sodd.json` en un editor de texto, cambiá algo (ej. `"volume": -20`)
    y guardá: se reimporta; si escribís un parámetro mal (`"parameter": "Vida"`) la consola dice
    "El parámetro 'Vida' no existe. ¿Quisiste decir 'Health'?".
