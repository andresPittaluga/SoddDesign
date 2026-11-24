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
- [x] Fase 2 — Timeline, loops, markers, parámetros, SoddCommands (40 EditMode + 2 PlayMode)
- [x] Fase 3 — Efectos, buses, mixer, snapshots, reverb zones (47 EditMode + 2 PlayMode)
- [x] Fase 4 — Mockup UI → Studio + Mixer + Profiler (49 EditMode + 2 PlayMode)
- [x] Fase 5 — Bridge + MCP + AI_GUIDE (52 EditMode + 2 PlayMode; MCP: 26 tools, smoke test stdio OK)
- [x] Fase 6 — Netcode (55 EditMode + 2 PlayMode; incluye serialización del RPC)
- [x] Fase 7 — Bancos/Addressables, optimizador, docs, samples, tag v0.1.0 (59 EditMode + 2 PlayMode)
- [ ] Fase 8 — Warpmart (**esperando que el humano lo pida y pase la ruta del proyecto**; no tocar antes)

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
- **SoddCommands** (`Editor/Commands/SoddCommands.cs`): edita el JSON DOM, normaliza clips (acepta "kick",
  "kick.wav" o path; escribe {guid, path}), valida (reader + buses + clips + nested) y si hay errores NO escribe.
  `SetProperty` con número sobre una prop modulada cambia solo el `base`. `RemoveModulator` deja el atajo numérico
  si no quedan moduladores. Extras además de los de la spec: Duplicate/Split/RemoveRegion, RemoveTrack, RemoveEffect,
  EditEvent (JSON Patch), CreateEventFromJson, GetEvent, ListEvents, ListParameters, EditSettings.
- **Undo** (`SoddUndo`): ScriptableObject oculto con {clave=id del evento, archivo, contenido, existe}.
  Unity guarda sus versiones; al deshacer se aplican a disco solo las entradas que cambiaron (moves con
  AssetDatabase.MoveAsset → conserva el GUID del .meta). Borrar + undo recrea el archivo (GUID de asset nuevo,
  pero el id del evento —lo que usan las referencias— es el mismo).
- MoveEvent/RenameEvent cambian el `path` lógico Y mueven el archivo a `Events/<path>.sodd.json`.
- Preview en Editor: `SoddPreview` (motor propio en GameObject HideAndDontSave + EditorApplication.update,
  lookahead 200 ms porque el tick del Editor es irregular). En Play Mode usa el motor del juego.
- `SoddEngine.Start` sincroniza con `AudioSettings.dspTime` si todavía no hubo Update (bug encontrado por test).
- **Efectos**: cada voz tiene TODOS los componentes de efecto pre-creados y desactivados (13 por voz) y se
  habilitan según el track → cero AddComponent en runtime. Orden de proceso FIJO (HP→LP→Dist→Sat→Crush→Ring→
  Chorus→Trem→AutoPan→Comp→Delay→Echo→Reverb), no el orden del JSON. Los efectos de track se aplican por voz
  (equivalente para filtros lineales; el compresor comprime cada voz por separado).
- DSP propios (`SoddDspFilters.cs`): params `volatile float`, buffers reservados en Awake, `[ExecuteAlways]`
  para que suenen en el preview del Editor. `Render()` público para tests offline.
- Snapshots: lowpass/highpass del bus se combinan con el LowPass/HighPass del track (min/max de cortes).
- **SoddMixer.mixer** (`Runtime/Resources`) generado por reflection de la API interna del Editor
  (`Window/Sodd/Dev/Rebuild SoddMixer (package)` o `bash Tools/ci.sh exec Sodd.Design.Editor.SoddMixerBuilder.Build`).
  Grupos Master>Music,SFX,UI,Voice,Ambience,ReverbSend; SFX/Ambience/Voice tienen Send → ReverbSend
  (Receive + SFX Reverb). Expuestos: `<Grupo>_Volume`. El volumen de los buses lógicos se aplica en la voz
  (funciona aunque no haya mixer); el mixer da ruteo y reverb compartida.
- **SoddReverbZone**: usa un AudioReverbZone de Unity (depende de la posición del LISTENER) + sube
  `ReverbSend_Volume` del mixer según distancia del listener. `reverbSend` del track → `AudioSource.reverbZoneMix`.
- `CI_COPYBACK="ruta1 ruta2" bash Tools/ci.sh exec Metodo` trae de vuelta archivos regenerados (ej. la escena).
- **UI**: mockup en `Packages/com.sodd.sodddesign/Documentation~/ui-mockup.html` (abrilo en el navegador);
  el humano no respondió todavía → se siguió el mockup como guía. Tokens en `Editor/UI/SoddTheme.uss`
  (mismos colores que el mockup), layout en `SoddStudio.uxml`.
- Timeline: `SoddTimelineCanvas` dibuja lanes/regiones con Painter2D (no un VisualElement por región);
  waveforms = textura por clip (1024x64, `SoddWaveformCache`) dibujada como quad texturizado en una capa hija;
  overlay (fades, handles, selección, markers, playhead) en otra capa hija → solo el overlay se repinta por frame.
  Labels de región/regla: pool de Labels (pocas decenas). Drag = valores temporales; se commitea al soltar →
  un solo paso de Undo (EditEvent con patch start+length). Shift al arrastrar = sin snap.
- Inspector: todo campo usa `isDelayed` y llama a SoddCommands; tras cada cambio el Studio se reconstruye
  (SoddCommands.Changed / SoddUndo.Applied / postprocessor). Moduladores: botón ＋/∿N por propiedad, cards por
  tipo, `SoddCurveEditor` (arrastrar puntos, doble click agrega, click derecho borra).
- Parámetros de la barra inferior: globales → motor de preview; locales → instancia en preview (se recuerdan).
- Mixer: faders en vivo sobre el motor activo (preview o juego); "Guardar volúmenes" escribe sodd.settings.json
  vía `SoddCommands.EditSettings` (con Undo). Profiler: refresco 10 Hz.
- Atajos (ventana con foco y sin estar escribiendo): Espacio play/stop, S split en playhead, D duplicar,
  Supr/Backspace borrar, F zoom-to-fit, Home inicio. Ctrl+Z/Y = Undo global de Unity.
- `SoddStudioHooks` = punto de enganche para el bridge de IA (estado "IA: on/off" en la toolbar).
- **Bridge** (`Editor/Bridge/SoddBridge.cs`): TcpListener en `IPAddress.Loopback` con HTTP/1.1 mínimo hecho a
  mano (más predecible que HttpListener en Mono). Auth `Authorization: Bearer <token>`; token nuevo por sesión
  en `Library/SoddDesign/bridge.json` (se borra al cerrar). Rechaza `Origin` (navegadores) y Host no local
  (DNS rebinding). Arranca solo (EditorPrefs `SoddBridge.Enabled`, default true; no arranca en batchmode).
  Ejecuta en main thread vía cola bombeada en `EditorApplication.update`, timeout 30 s.
- **MCP** (`Tools~/sodddesign-mcp`): TypeScript + SDK oficial 1.32 (`registerTool`) + zod 4. Se bundlea con
  esbuild a `dist/index.js` (un archivo, sin node_modules) y **se commitea** para que funcione con solo Node.
  Encuentra el proyecto con `--project`, `SODD_PROJECT` o subiendo desde su ubicación. `--check` hace ping.
  `node test-stdio.mjs` = smoke test del protocolo. Para reconstruir: `npm install && npm run build`.
- `Documentation~/AI_GUIDE.md`: 38 entradas pedido→técnica + recetas + pointers útiles. Tool `get_ai_guide`.
- **Netcode**: el Sandbox tiene NGO 2.14.0 en el manifest (para compilar/testear `Sodd.Design.Netcode`).
  El runtime NO depende de NGO: `SoddAudio.Net.*` usa la interfaz `ISoddNetAudio` (`SoddNet.Handler`) que registra
  `SoddNetworkAudio` al spawnear; sin red → reproduce local. Anti-duplicados elegido: **el que dispara reproduce
  local al instante + RPC `SendTo.NotMe`** (Broadcast). OwnerOnly: RPC `SpecifiedInParams` a
  `RpcTarget.Single(owner)`, o local si el que dispara es el dueño. Mensaje = hash FNV-1a del id del evento +
  posición o NetworkObjectReference + hasta 4 params (hash del nombre + float). `SoddNetworkEmitter`: Playing +
  4 NetworkVariable<float> (write = Owner) con umbral y frecuencia máxima. No se especifica InvokePermission en
  los RPC (default Everyone en NGO 2.x; RequireOwnership está deprecado en 2.14).
- Sample `Samples~/NetcodeDemo` (README + `NetDemoPlayer.cs`, copia compilada en `Assets/Sandbox/Netcode/`).
- **Bancos**: `SoddBank` (ScriptableObject con eventos) + `SoddBanks.Load/Unload` (refcount) + `SoddBankLoader`.
  `excludeFromRegistry` = esos eventos no van al registro de Resources. Addressables en assembly aparte
  `Sodd.Design.Addressables` (defineConstraint `SODD_ADDRESSABLES`), `SoddAddressableBanks.LoadAsync(address)`.
  El Sandbox tiene Addressables 2.11.2 instalado solo para verificar que compila.
- **Optimizador** (`Window/Sodd/Optimize Audio Import Settings…` y botón "Optimizar" del Studio): <1 s →
  DecompressOnLoad+ADPCM; 1–6 s → CompressedInMemory+Vorbis 70%; >6 s o música/ambiente en loop → Streaming+Vorbis 60%;
  solo usado en eventos 3D → mono. Muestra el plan y pide confirmación (no tiene Undo).
- Docs: `Packages/com.sodd.sodddesign/README.md`, `Documentation~/QUICKSTART.md`, `GUIA_TECNICO_AUDIO.md`,
  `AI_GUIDE.md`, `CHANGELOG.md`. Sample `Basic` (3 eventos con GUIDs fijos de sus wavs + templates de settings).
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

### Fase 2 (≈2 min, sin Play Mode)
1. En Project, click en `Assets/SoddDesign/Events/music/loop` → el Inspector muestra resumen y botones
   **▶ Preview / ■ Stop**. Apretá Preview: suena el loop SIN entrar en Play Mode. Stop lo corta.
2. Click derecho sobre cualquier evento → `Sodd > Preview Event`.
3. Menú `Window > Sodd > Dev > Create Demo Event (SoddCommands)`: crea `demo/commands_demo` por código
   (2 beeps + kick con pitch aleatorio), lo selecciona y lo reproduce. En la consola sale el log de cada comando.
4. Apretá **Ctrl+Z** varias veces: se deshace paso a paso (marker, modulador, regiones...). Ctrl+Y rehace.
   Abrí el `.sodd.json` en un editor de texto para ver cómo cambia.
5. `Window > Sodd > Validate All Events` → "Todos los eventos son válidos".

### Fase 3 (≈2 min)
1. Play en `Sandbox.unity`. El drone ahora además "se abre y se cierra" (LowPass con LFO en el cutoff).
2. Apretá **5**: arpegio de beeps lo-fi (Bitcrusher) con delay. Apretá **6**: barrido robótico
   (RingMod + Tremolo + Saturator + compresor).
3. Apretá **M** (música) y después **U** (Underwater): en 1 s todo se apaga como bajo el agua (lowpass) y la
   música baja. **N** vuelve a normal. **P** = menú de pausa (SFX/ambiente casi mudos, música filtrada).
4. Reverb: en Play, seleccioná `Main Camera` y poné Position = (8, 1, 0) (adentro de "Reverb Zone (cueva)").
   Apretá **4** y **2**: suenan con reverb de cueva. Volvé la cámara a (0, 9, -14): sin reverb.
5. En el Project, `Packages/SoddDesign/Runtime/Resources/SoddMixer` → Window > Audio > Audio Mixer: se ven los
   grupos (Music, SFX, UI, Voice, Ambience, ReverbSend). Mientras suena, los medidores de los grupos se mueven.

### Fase 4 — SoddDesign Studio (≈3 min, sin Play Mode)
0. (Opcional) Abrí `Packages/com.sodd.sodddesign/Documentation~/ui-mockup.html` en el navegador: es el diseño
   de referencia. Si querés cambios de diseño, decímelo y los aplico.
1. `Window > Sodd > SoddDesign Studio`. A la izquierda el árbol de eventos con buscador y tags.
2. Click en `music/loop`: aparecen 2 tracks violetas (Music) con waveforms, la banda celeste del loop arriba y
   los markers ◆ Bar1/Bar2. **Espacio** (o ▶): suena y el playhead se mueve; ■ corta.
3. Subí **Intensity** en la barra de abajo mientras suena: entra el drone. Llevalo a 1: sale del loop.
4. Arrastrá una región: se mueve con snap (50 ms). Arrastrá el borde derecho: cambia el largo. Arrastrá el
   circulito de arriba a la izquierda: fade-in. **Ctrl+Z** deshace cada gesto.
5. Click en el header del track "Drone" → Inspector del track: Volumen tiene "∿1" → click → se ve la curva de
   Intensity; arrastrá un punto y escuchá el cambio. "＋ Efecto" → LowPass → bajá cutoff a 500.
6. `＋ Evento` → `sfx/test/mi_evento`. Arrastrá 2-3 wavs de `Assets/Sandbox/Audio` desde el Project al timeline:
   se crea un track con regiones. Seleccioná una región y apretá **S** (corta en el playhead), **D** (duplica),
   **Supr** (borra).
7. Toolbar → **Mixer**: faders de buses, M/S, y botones de snapshots (Underwater/Pause/Normal) mientras suena
   algo. **Profiler**: instancias y voces en vivo.
8. En Play Mode del Sandbox, editá un volumen en el Studio: se escucha el cambio al instante (live update).

### Fase 5 — IA por MCP (≈3 min)
1. Con Unity abierto, mirá la toolbar del Studio: arriba a la derecha dice **"IA: ● on"** (click = on/off).
2. En Unity: `Window > Sodd > AI Bridge > Copy Claude Code command`. Pegalo en una terminal y ejecutalo
   (registra el servidor MCP `sodddesign` en Claude Code). Reiniciá Claude Code.
3. Pedile a Claude, en castellano y sin tecnicismos, por ejemplo:
   - "Listame los sonidos que hay." → usa `list_events`.
   - "Creá un sonido de moneda con los beeps, que suba de tono, y hacémelo escuchar." → crea el evento,
     lo valida y lo reproduce en Unity (sin Play Mode).
   - "Hacé el salto más grave y que suene lejos." → cambia pitch, agrega LowPass, baja volumen.
4. Abrí el Studio: el evento nuevo aparece en el árbol; Ctrl+Z deshace lo que hizo la IA.

### Fase 6 — Netcode (opcional, ≈5 min)
- El proyecto ahora tiene NGO 2.14 instalado (Package Manager lo baja al abrir).
- Seguí `Packages/com.sodd.sodddesign/Samples~/NetcodeDemo/README.md` (setup de 5 min con NetworkManager,
  un objeto con SoddNetworkAudio y un player prefab con `NetDemoPlayer`). Con Multiplayer Play Mode o un build:
  J (salto) se escucha en los dos sin duplicarse en el host; F solo local; H solo para el dueño.
- Si no querés probar red ahora, alcanza con ver que compila sin errores.

### Fase 7 (≈2 min)
1. Studio → botón **Optimizar**: muestra qué clips cambiaría (music_loop → Streaming, footsteps → ADPCM mono…).
   Aceptá y mirá un clip en el Inspector.
2. `Create > Sodd > Sound Bank`, arrastrá 1–2 eventos, dejá "Exclude From Registry" ✔. Poné un objeto con
   `Sodd Bank Loader` en la escena apuntando al banco: esos eventos solo suenan mientras el objeto existe.
3. Package Manager → SoddDesign → Samples: importá **Basic** y **Netcode Demo** (ver sus README).
4. Leé `Packages/com.sodd.sodddesign/Documentation~/GUIA_TECNICO_AUDIO.md` (pensada para tu técnico de audio).

## Próximos pasos sugeridos (post v0.1.0)
- Fase 8 (Warpmart) cuando el humano lo pida: branch `feature/sodddesign`, paquete por git URL con tag `v0.1.0`.
  Para eso hay que publicar este repo en un remoto (GitHub) — pedir la URL al humano.
- Mejoras posibles: resucitar voces virtuales cuando se liberan voces; orden de efectos configurable;
  editor de envolventes/curvas de fade; drag de loops/markers en la regla; tests de UI con eventos sintéticos;
  vista de 500+ eventos medida con profiler (el navegador usa ListView virtualizado, pero el refresco
  recorre todos los assets).

## Limitaciones conocidas
- Los efectos de track se aplican por voz (el compresor comprime cada voz por separado).
- Cambiar el pitch con moduladores mientras suena no reprograma el `SetScheduledEndTime` (el final se calcula
  con el pitch del disparo).
- Las condiciones de salida de loop se evalúan ~100 ms antes del final del loop (lookahead).
- Reverb zones: el AudioReverbZone de Unity depende de la posición del listener, no de la fuente.
- Batchmode no puede abrir el proyecto si el Editor lo tiene abierto → por eso `Tools/ci.sh` usa una copia espejo.
