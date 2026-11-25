# Changelog

## [0.1.11] - 2026-11-25
- Alto de cada track: arrastrar el borde de abajo de su nombre (40–240 px, doble click = 64). Se guarda por evento en EditorPrefs, no en el `.sodd.json`. Con más alto entran más franjas de un multi-instrumento.
- Barra de vista general bajo la regla: todo el evento en miniatura y un recuadro con la parte visible; se arrastra o se hace click para moverse.
- F2 renombra lo seleccionado (marker, región, track o evento).
- Loops: arrastrar sus bordes en la regla los estira o achica; el medio los mueve.
- Regiones: el cursor cambia sobre los bordes (recortar) y el cuerpo (mover); la zona de borde pasa a 8 px.

## [0.1.10] - 2026-11-24
- Regiones RandomClip/Sequential: cada sonido en su propia franja (waveform + nombre, numerados en la secuencia), como el multi-instrumento de FMOD; tantas franjas como entren con el nombre legible (hasta 6) y "+N más" en la última.
- Preview: botón ⟳ junto a Play (tecla L) que repite la escucha sin cambiar el evento.
- Al lado de cada perilla con modulador Random se ve el valor que tocó en el último play, y la franja del clip elegido queda resaltada. El motor guarda los últimos disparos (`SoddEventInstance.GetRecentTriggers`, `Seed`).
- Formato canónico de los `.sodd.json` (`SoddCommands.Canonical`, `Sodd/Dev/Normalize Event Files`): guardar sin cambios reales deja el archivo idéntico; `markers`/`loopRegions` vacíos no se escriben.
- Fix: guardar un evento ya no borra `volume`, `pitch` ni `startOffset` de cada clip de una playlist.

## [0.1.9] - 2026-11-24
- Menú propio `Sodd` en la barra de Unity (Studio, Mixer, Profiler, herramientas, Guía de uso y Atajos); `Window/Sodd` ya no existe.
- Tracks: arrastrar el nombre los reordena; click derecho Subir/Bajar/Duplicar; el chip de dB se arrastra para cambiar el volumen.
- Markers en la regla: click selecciona, arrastrar mueve (Shift = sin snap), doble click renombra, Supr borra.
- Perillas estilo FMOD para volumen, pitch, pan y parámetros de efectos: arrastre (Shift = fino), rueda, doble click = default, doble click en el número = escribir. Un paso de undo por gesto.
- Comandos `MoveTrack`, `DuplicateTrack`, `MoveMarker`, `RenameMarker`, `RemoveMarker` (también en el bridge MCP).
- Botón ? en el Studio con la tabla de atajos; guía actualizada.
- Los eventos importados antes que sus AudioClips se reimportan solos también después de una recarga de dominio (primer import de un proyecto).

## [0.1.8] - 2026-11-24
- `SoddRegistry` y cada filtro DSP viven en un archivo con su nombre: en un build el registro cargaba sin script y ningún evento sonaba.

## [0.1.7] - 2026-11-24
- Efecto nuevo `ThreeEQ` (graves/medios/agudos con cruces y pendiente, como el de FMOD).
- `Tremolo`: `shape`, `skew`, `duty`, `square` y `phase` (forma del LFO como en FMOD). Sin cambios con los valores por defecto.
- Clips de `RandomClip`/`Sequential` con `volume` (dB), `pitch` (semitonos) y `startOffset` (s) propios.

## [0.1.6] - 2026-11-24
- `Sequential`: nuevo `sequenceScope` (`Global` por defecto, el orden sigue entre todas las instancias del evento; `Instance`, cada instancia lleva su propio orden). Se elige en el inspector del Studio.

## [0.1.5] - 2026-11-24
- Studio: atajos con el `ShortcutManager` de Unity (funcionan aunque ningún control tenga el foco y se cambian en Edit > Shortcuts > SoddDesign Studio): Espacio play/stop, Esc stop, Inicio, Ctrl+Z / Ctrl+Y / Ctrl+Shift+Z, S dividir, D o Ctrl+D duplicar, Supr/Backspace borrar, F encuadrar. Los botones del transporte no toman el foco. La barra muestra los atajos.

## [0.1.4] - 2026-11-24
- Fix: un track sin bus propio usaba el bus Master en vez del bus del evento (Unity serializa el string vacío como `""`). Los sliders de Music/SFX no afectaban a nada; solo andaba Master.

## [0.1.3] - 2026-11-24
- Fix: `SoddListener`, `SoddAudioManager` y `SoddBankLoader` ahora están en archivos con su nombre (antes no se podían serializar en escenas/prefabs). GUIDs fijos de sus scripts.
- `SoddListener` asegura un AudioListener en su GameObject y desactiva los demás; si no hay ningún listener, el manager agrega uno a la cámara principal al cargar cada escena (proyectos que venían de FMOD).
- `SoddBus.VolumeLinear` es un multiplicador aparte del fader (como `Bus.setVolume` de FMOD).
- `SoddFmodCompat`: `Pos(...)`, `State(...)`/`SoddPlaybackState`, `EventExists`, `LengthSeconds`.
- Herramientas: `Tools/fmod_to_sodd.py` (ActionSheets, automatizaciones, buses reales) y `Tools/fmod_code_migrate.py`.

## [0.1.2] - 2026-11-24
- Compatibilidad FMOD: `SoddEventReference` lee los valores serializados de `FMODUnity.EventReference` (Guid Data1..4 + Path) al cambiar el tipo del campo; `SoddEvents.Find` acepta "event:/..." y "{guid}"; `SoddBus.Find` acepta "bus:/...".
- `SoddEventInstance.Pitch` (ratio), `Length`, `StartFrom(segundos)`; `SoddBus.VolumeLinear`.

## [0.1.1] - 2026-11-24
- Fix: en el primer import de un proyecto, los eventos que se importaban antes que sus AudioClips quedaban sin clips. Ahora se reimportan solos cuando llegan los audios.

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
