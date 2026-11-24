# SoddDesign — Audio middleware para Unity (estilo FMOD, gratis, de Sodd Studio)

Este archivo es la especificación completa del proyecto. Leelo entero al empezar cada sesión.
Después leé `PROGRESS.md` para saber en qué fase estamos. Trabajá de forma autónoma: el humano
(dueño del proyecto) tiene poco tiempo y solo va a probar en Unity y escuchar resultados.

## Objetivo
Middleware de audio para Unity 6 (URP) con:
- Eventos con timeline, capas, clips aleatorios, loops, markers.
- Audio 3D completo y muchos efectos.
- Parámetros + moduladores (cualquier propiedad modulable por parámetros del juego).
- API de código tipo FMOD (`SoddAudio.PlayOneShot`, instancias, buses, snapshots).
- Editor visual lindo y responsive para técnicos de audio (SoddDesign Studio).
- **Muy compatible con IA**: datos en JSON legible + JSON Schema + servidor MCP.
- Soporte opcional para Netcode for GameObjects (NGO 2.x).
- Destino final: integrarse en el juego **Warpmart** (Unity 6, NGO, URP).

## Reglas técnicas
- Unity 6, C#. Namespace raíz `Sodd.Design`.
- Paquete UPM en `Packages/com.sodd.sodddesign/` con:
  - `Runtime/` → `Sodd.Design.Runtime.asmdef`
  - `Editor/` → `Sodd.Design.Editor.asmdef` (solo Editor)
  - `Runtime/Netcode/` → `Sodd.Design.Netcode.asmdef`, compila solo si existe
    `com.unity.netcode.gameobjects` (usar `versionDefines` → `SODD_NGO`).
  - `Tests/Editor`, `Tests/Runtime`
  - `Samples~/`
  - `Documentation~/`
- `Assets/Sandbox/` = escena de prueba con ejemplos de cada feature.
- Cero dependencias externas en runtime (salvo NGO opcional y Addressables opcional).
- Editor 100% en **UI Toolkit** (UXML + USS). Nada de IMGUI salvo PropertyDrawers simples.
- Sin allocations por frame en el runtime (pool de voces, sin LINQ en Update, sin boxing).
- Toda edición de datos pasa por `SoddCommands` (ver abajo), con Undo.
- Audios de prueba: generalos vos proceduralmente (script Python/C# que escribe .wav:
  beeps, ruido, sweeps, kicks sintéticos, un loop musical simple) en `Assets/Sandbox/Audio/`.

## Verificación (no podés escuchar ni apretar Play, así que verificá así)
- Compilar y correr tests en batchmode:
  `"<UNITY_PATH>" -batchmode -nographics -projectPath . -runTests -testPlatform EditMode -testResults results-edit.xml -logFile -`
  y lo mismo con `PlayMode`. La ruta de Unity está en `PROGRESS.md` (si no está, pedila una vez).
- Cero errores y cero warnings nuevos antes de cerrar una fase.
- Tests mínimos por fase (pool, stealing, moduladores, parser JSON, schema, timeline scheduling,
  comandos + undo, serialización de RPC).
- Al terminar cada fase: actualizar `PROGRESS.md`, escribir en él "QUÉ PROBAR EN UNITY"
  (pasos concretos para que el humano escuche/verifique en 2 minutos) y hacer commit
  `feat(fase-N): ...`.

## Modelo de datos (compatible con IA)
- Cada evento es un archivo **`.sodd.json`** importado con `ScriptedImporter` →
  genera un `SoddEventAsset` (ScriptableObject) como sub-asset. El JSON es la fuente de verdad.
- `Packages/com.sodd.sodddesign/Schema/sodd.event.schema.json` (JSON Schema draft 2020-12),
  documentado con `description` en cada campo. Validar al importar con mensajes claros
  ("El parámetro 'Vida' no existe. ¿Quisiste decir 'Health'?").
- `sodd.parameters.json` (en el proyecto del juego): registro de parámetros globales y locales
  con nombre, rango, default, seekSpeed, descripción en lenguaje natural.
- `sodd.settings.json`: buses, voces máximas, mixer asociado, snapshots.
- Evento:
  - id (GUID), path ("sfx/player/jump"), description
  - tags, is3D, ajustes 3D (spatialBlend, minDistance, maxDistance, rolloff + curva custom,
    doppler, spread)
  - bus, priority, maxInstances, stealMode (Oldest/Quietest/None), cooldown
  - networkMode (Local / Broadcast / OwnerOnly)
  - parameters locales
  - tracks[], loopRegions[], markers[]
- Track: nombre, tipo (Music/SFX/Voice/Ambience), volumen, pan, mute/solo, effects[], regions[].
- Region (instrumento en timeline): start, length, fadeIn, fadeOut (+ curva), y un instrumento:
  `SingleClip`, `RandomClip` (avoidRepeat, weights), `Sequential`, `Silence`, `NestedEvent`.
  Clips referenciados por GUID de asset + path legible (para que la IA entienda).
- **Moduladores**: TODA propiedad numérica (volume, pitch, pan, cutoff, reverbSend, parámetros
  de efectos, etc.) es `{ "base": x, "modulators": [...] }`. Tipos:
  - `Random` (min/max, unit: dB | semitones | linear, perTrigger)
  - `ParameterCurve` (parameter, curve [[x,y]...])
  - `RandomScaledByParameter` (parameter, range, curve que escala el rango)
  - `LFO` (shape, rate, depth, sync opcional a parámetro)
  - `Envelope` (attack/hold/release)
  - `ADSR` sobre start/stop
  Ejemplo: "pitch aleatorio según la vida" → `RandomScaledByParameter` con parameter "Health".

## Runtime
- `SoddAudio` (API estática):
  `PlayOneShot(SoddEventReference, Vector3 | Transform)`, `CreateInstance(ref)`,
  `SetGlobalParameter(name|id, value)`, `GetBus(name)` (volume, mute, pause, fade),
  `SetSnapshot(name, fadeTime)`, `StopAll(bus, mode)`, `Pause(bool)`.
- `SoddEventInstance` (struct handle, no class expuesta → seguro tras Release):
  Start, Stop(Immediate|FadeOut), SetParameter, AttachTo(Transform), Set3DPosition,
  IsPlaying, eventos OnMarker / OnStopped / OnLoopExit, Release.
- `SoddEventReference`: struct serializable con GUID + PropertyDrawer con buscador.
- `SoddEmitter` (MonoBehaviour): play on Start/Enable/Trigger, parámetros, stop on destroy.
- `SoddAudioManager`: auto-init con `[RuntimeInitializeOnLoadMethod]`, pool de voces
  (AudioSource), voces virtuales por prioridad/distancia, `SoddListener`.
- Timeline sample-accurate: `AudioSettings.dspTime` + `AudioSource.PlayScheduled` /
  `SetScheduledEndTime`. Lookahead de scheduling ~100 ms. Loops con condiciones
  ("salir del loop si parámetro X > Y").
- Moduladores evaluados por instancia en Update (no por sample), interpolados.
- Buses lógicos propios + routing a un `SoddMixer.mixer` fijo que viene en el paquete
  (grupos: Master, Music, SFX, UI, Voice, Ambience, ReverbSend). El AudioMixer NO se puede
  crear en runtime: usar parámetros expuestos y snapshots del mixer.
- Efectos por track: filtros nativos (LowPass, HighPass, Echo, Reverb, Distortion, Chorus)
  + DSP propios con `OnAudioFilterRead` (Bitcrusher, Tremolo, AutoPan, RingMod, Saturator,
  SimpleDelay, SimpleCompressor). Thread-safe: parámetros copiados con volatile/atomics,
  nada de API de Unity en el audio thread.
- `SoddReverbZone`: send a ReverbSend según distancia/volumen.
- Bancos opcionales con Addressables (`SODD_ADDRESSABLES`).

## SoddCommands (capa única de edición)
API en Editor usada por la UI, el servidor MCP y los tests:
CreateEvent, DeleteEvent, RenameEvent, MoveEvent, AddTrack, AddRegion, MoveRegion,
SetProperty(path JSON-pointer, value), AddModulator, RemoveModulator, AddParameter,
AddEffect, AddMarker, AddLoopRegion, PreviewEvent, StopPreview, Validate.
Todo con Undo, validación y resultado `{ ok, errors[], warnings[] }`.

## SoddDesign Studio (Editor UI) — tiene que verse y sentirse PRO
- Ventana `Window > Sodd > SoddDesign Studio`. Tema oscuro propio vía USS con tokens
  (colores, radios, espaciados, tipografía) en `SoddTheme.uss`.
- Layout: navegador de eventos (árbol con búsqueda y tags) | timeline | inspector
  (propiedades + moduladores) | abajo: parámetros (sliders en vivo) + transporte + medidor.
- Timeline dibujado con `generateVisualContent` / `Painter2D` (no cientos de VisualElements).
  Waveforms cacheadas en textura. Zoom (rueda), pan (botón medio / alt+drag), snapping,
  playhead en vivo, regiones arrastrables/redimensionables con fades editables,
  loop regions y markers.
- Drag & drop de AudioClips desde Project al timeline.
- Atajos: Espacio play/stop, S split, D duplicar, Supr borrar, Ctrl+Z/Y.
- Moduladores visuales: cada propiedad modulada muestra un ícono; panel con la curva editable.
- Estados vacíos con ayuda, tooltips en todo, transiciones USS suaves, colores por tipo de track.
- Preview sin entrar en Play Mode (GameObject oculto HideAndDontSave + EditorApplication.update).
- Live update en Play Mode (editar y escuchar al instante).
- Ventanas extra: Mixer (faders de buses, mute/solo, snapshots), Profiler (instancias, voces
  reales/virtuales, CPU de DSP), gizmos 3D de min/max distance en la Scene.
- Rendimiento: ListView virtualizado, nada de repintar por frame salvo playhead,
  ventana fluida con 500+ eventos.
- Antes de codear la UI: generar `Documentation~/ui-mockup.html` (mockup estático del layout)
  para que el humano lo apruebe. Si no responde, seguir con el mockup como guía.

## IA / MCP
- `Editor/Bridge/`: servidor local HTTP **solo en 127.0.0.1**, puerto configurable
  (default 47821), token aleatorio guardado en `Library/SoddDesign/bridge.json`.
  Expone SoddCommands como JSON-RPC. Se ejecuta en el main thread vía cola.
- `Tools~/sodddesign-mcp/`: servidor MCP en Node (TypeScript, stdio, SDK oficial
  @modelcontextprotocol/sdk) que habla con el bridge. Tools:
  list_events, get_event, create_event, edit_event (JSON patch), add_modulator,
  list_parameters, add_parameter, list_audio_clips (duración, canales, tags),
  preview_event, stop_preview, validate, get_schema.
- README con cómo registrarlo en Claude Code (`claude mcp add sodddesign -- node ...`).
- `Documentation~/AI_GUIDE.md`: glosario de pedidos vagos → técnica, para que una IA
  traduzca lo que pide alguien no técnico. Ejemplos:
  "más grave" → pitch negativo en semitonos; "que suene lejos" → lowpass + reverbSend +
  menos volumen; "que se ponga nervioso" → LFO en pitch/volumen; "con menos vida más
  inestable" → RandomScaledByParameter(Health); "que no se repita" → RandomClip avoidRepeat;
  "que pegue más" → compresor + transiente + capa grave; "debajo del agua" → snapshot
  Underwater (lowpass fuerte + reverb). Incluir 30+ entradas.

## Netcode for GameObjects (opcional, assembly aparte)
- `SoddNetworkAudio : NetworkBehaviour` usando `[Rpc(SendTo.Everyone)]` (NGO 2.x).
- Se envía: hash 32-bit del GUID del evento, posición (o NetworkObjectReference para
  attach), hasta 4 parámetros (id + float). Nada de audio por red.
- Respeta `networkMode` del evento: Local (no se envía), Broadcast, OwnerOnly.
- Evitar duplicados en host (el que dispara reproduce local y el RPC excluye al emisor,
  o se usa SendTo.Everyone sin reproducción local; elegir uno y documentarlo).
- Eventos persistentes (motores, loops): parámetros por `NetworkVariable` en un componente
  `SoddNetworkEmitter`, con umbral de cambio para no saturar.
- Helper `SoddAudio.Net.PlayOneShot(...)`.
- Sample `Samples~/NetcodeDemo`.

## Fases (actualizar PROGRESS.md)
0. Setup repo, paquete, asmdefs, git, .gitignore de Unity, audios procedurales, PROGRESS.md.
1. Runtime MVP: JSON + importer + schema + moduladores básicos + pool + API + reference + 3D.
2. Timeline sample-accurate, loops, markers, parámetros, SoddCommands + Undo.
3. Efectos (nativos + DSP), buses, mixer, snapshots, reverb zones.
4. Mockup UI → SoddDesign Studio completo + Mixer + Profiler.
5. Bridge + servidor MCP + AI_GUIDE.md.
6. Netcode.
7. Addressables/bancos, botón "Optimize import settings", docs (README, QUICKSTART,
   guía para técnico de audio sin código), samples, tag v0.1.0.
8. Integración en Warpmart (solo cuando el humano lo pida y dé la ruta del proyecto):
   branch `feature/sodddesign`, agregar el paquete por git URL con tag, inventario de
   AudioSource/PlayOneShot existentes, migrar por partes (UI → SFX jugador → sonidos en red
   → música), documentar qué cambió. No borrar audio viejo hasta que el humano confirme.

## Estilo de trabajo
- Autónomo. No pidas confirmación para decisiones técnicas reversibles: decidí, anotá la
  decisión en `PROGRESS.md` (sección Decisiones) y seguí.
- Preguntá solo si algo es irreversible o si falta un dato que solo el humano tiene.
- Si el contexto se llena, dejá `PROGRESS.md` impecable para que otra sesión retome.
- Commits chicos y frecuentes. Nunca push --force. Nunca tocar Warpmart antes de la fase 8.
