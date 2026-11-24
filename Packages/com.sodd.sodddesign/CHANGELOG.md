# Changelog

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
