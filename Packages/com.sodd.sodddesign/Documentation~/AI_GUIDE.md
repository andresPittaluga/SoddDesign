# SoddDesign — Guía para IA

Esta guía es para una IA (vía MCP) que trabaja con alguien **no técnico**. Traduce pedidos vagos a técnica concreta
de SoddDesign. Leela antes de diseñar sonido.

## Reglas de oro
1. **Unidades**: `volume` en **dB** (0 = original, -6 ≈ mitad, -20 = bajito, -80 = silencio). `pitch` en
   **semitonos** (12 = una octava). `pan` -1..1. Tiempos en segundos. Filtros en Hz.
2. Toda propiedad numérica acepta un número o `{ "base": x, "modulators": [...] }`. Para "que varíe" agregá
   moduladores, no cambies el base.
3. Flujo: `list_audio_clips` → `create_event` → `add_track` → `add_region` → `add_modulator`/`add_effect` →
   `validate` → `preview_event` → preguntar "¿así?" → ajustar con `set_property`/`edit_event`.
4. Cambios chicos y escuchables. Después de cada cambio importante, `preview_event` y pedí feedback.
5. Si un comando devuelve error, **no se escribió nada**: leé el mensaje (suele traer "¿Quisiste decir…?").
6. Parámetros del juego (vida, velocidad, intensidad): `list_parameters`. No inventes nombres: usá los existentes
   o creá uno con `add_parameter` y avisá al humano que el programador tiene que setearlo
   (`SoddAudio.SetGlobalParameter("Nombre", valor)`).
7. Cuantificá los pedidos con moderación: "un poco" ≈ 2–3 dB o 1–2 semitonos; "mucho" ≈ 6–10 dB o 5–7 semitonos.

## Pointers útiles
| Qué | Pointer |
|---|---|
| Volumen/pitch de todo el evento | `/volume`, `/pitch` |
| Volumen de un track | `/tracks/0/volume` |
| Pitch de una región | `/tracks/0/regions/0/instrument/pitch` |
| Parámetro de un efecto | `/tracks/0/effects/0/params/cutoff` |
| Distancias 3D | `/spatial/minDistance`, `/spatial/maxDistance` |

## Glosario: pedido → técnica

| # | El humano dice | Técnica en SoddDesign |
|---|---|---|
| 1 | "más grave" / "más gordo" | `set_property /pitch` −2 a −5 (semitonos). Si ya tiene moduladores, cambia el base. |
| 2 | "más agudo" / "más finito" | `/pitch` +2 a +5; o efecto `HighPass` cutoff 300–800. |
| 3 | "más fuerte" | `/volume` +3 dB (no pases de +6; si satura, `SimpleCompressor`). |
| 4 | "más bajito" / "que moleste menos" | `/volume` −4 a −8 dB. |
| 5 | "que suene lejos" | `LowPass` cutoff 1500–3000 + `/volume` −6 + `reverbSend` hacia 0 (track) + si es 3D subir `maxDistance`. |
| 6 | "que suene cerca / en la cara" | Quitar LowPass, `/volume` +2, `is3D` false o `spatialBlend` 0.3, reverbSend −1. |
| 7 | "debajo del agua" | Snapshot `Underwater` (`SoddAudio.SetSnapshot("Underwater")`) o en el evento `LowPass` cutoff 400–700 + `Chorus` suave. |
| 8 | "que no se repita" / "suena a robot" | Región `RandomClip` con varios clips + `avoidRepeat: true` + `Random` en pitch (±1.5 st) y volumen (−3..0 dB). |
| 9 | "que varíe un poco cada vez" | Modulador `Random` en `/pitch` (min −1, max 1, unit Semitones) y en `/volume` (min −2, max 0, unit dB). |
| 10 | "que se ponga nervioso / tenso" | `LFO` en `/pitch` (rate 6–8 Hz, depth 0.3) o `Tremolo` (rate 8, depth 0.4). |
| 11 | "con menos vida más inestable" | `RandomScaledByParameter` en `/pitch`: parameter `Health`, range 4, curve `[[0,1],[50,0.4],[100,0]]`. |
| 12 | "con menos vida más apagado" | `ParameterCurve` en `/tracks/N/effects/K/params/cutoff` (LowPass): curve `[[0,-4000],[100,0]]` sobre base 5000. |
| 13 | "que pegue más" / "más punch" | `SimpleCompressor` (threshold −20, ratio 4, makeup 4) + capa grave: nuevo track con `kick` a −6 dB + `Saturator` drive 3. |
| 14 | "que tenga más cuerpo" | Track extra con versión del clip a pitch −12 a −10 dB, o `Saturator` drive 2 mix 0.5. |
| 15 | "más brillante / con aire" | Quitar LowPass; `HighPass` 150 para limpiar; pitch +1. |
| 16 | "sucio / roto / retro / 8-bit" | `Bitcrusher` bits 5–8, downsample 4–8. |
| 17 | "metálico / robótico / alien" | `RingMod` frequency 200–600, mix 0.6 (+ `LFO` en frequency para que se mueva). |
| 18 | "con eco" | `SimpleDelay` time 0.25–0.4, feedback 0.35, mix 0.3 (o `Echo` nativo). |
| 19 | "como en una cueva / catedral" | `Reverb` decayTime 3–6, room −500; o colocar una `SoddReverbZone` preset Cave en la escena. |
| 20 | "como en un baño / cuarto chico" | `Reverb` decayTime 0.6–1.0, room −1500. |
| 21 | "como por teléfono / radio" | `HighPass` 400 + `LowPass` 3000 + `Saturator` drive 2. |
| 22 | "que entre suave" | `fadeIn` de la región 0.2–1.0 s; para eventos largos `ADSR` en `/volume` attack 1–2 s. |
| 23 | "que se vaya apagando / que no corte de golpe" | `fadeOut` de la región, o `ADSR` en `/volume` con `release` 1–3 s (se usa al detener con fade). |
| 24 | "que respire / que ondule" | `LFO` Sine lento en `/volume` (rate 0.2, depth 3 dB) y/o en el cutoff de un LowPass. |
| 25 | "que suba con la acción / intensidad" | Track extra con `ParameterCurve` en volume sobre `Intensity`: `[[0,-40],[0.5,-12],[1,0]]`. |
| 26 | "que el motor suba con la velocidad" | Parámetro local `Speed` (0..1, seekSpeed 3) + `ParameterCurve` en `/pitch` `[[0,-5],[1,7]]` + en volume `[[0,-8],[1,0]]`. |
| 27 | "que la música repita hasta X" | `add_loop_region` con `exitWhen: {parameter, op:'Greater', value}`; al cumplirse, termina el compás y sigue. |
| 28 | "que avise al juego en tal momento" | `add_marker` (el programador recibe `OnMarker(nombre)`). |
| 29 | "que no se amontonen / que no sature si se dispara mucho" | `maxInstances` 3–6 + `stealMode` `Oldest`; o `cooldown` 0.05–0.1. |
| 30 | "que la música/diálogo nunca se corte" | `priority` 0–20 (más importante). Sonidos de ambiente: 180–220. |
| 31 | "que se escuche desde más lejos" | `/spatial/maxDistance` ×2; para que no baje tan rápido `/spatial/minDistance` 3–5. |
| 32 | "que se escuche en todos los jugadores (online)" | `networkMode: "Broadcast"`; si es solo para el que lo hizo: `"OwnerOnly"`; UI: `"Local"`. |
| 33 | "que se mueva de un lado al otro" | `AutoPan` rate 0.5–2, depth 0.8 (2D) o mover el emisor en 3D. |
| 34 | "más corto / que dure menos" | Reducir `length` de la región + `fadeOut` 0.05–0.1 para que no haga click. |
| 35 | "que empiece más rápido / sin silencio inicial" | `startOffset` del instrumento 0.02–0.1 s, o mover la región a `start: 0`. |
| 36 | "que cada paso suene distinto según la superficie" | Parámetro local `Surface` (0..3) + regiones/tracks con `ParameterCurve` en volumen que habilitan cada set (−80 / 0). |
| 37 | "ambiente que no se note el loop" | Clip con `loop: true` + `LFO` muy lento en volume (rate 0.07) + capa de detalles `RandomClip` esporádica. |
| 38 | "que suene en cámara lenta" | `/pitch` −7 a −12 + `LowPass` 2000 (o snapshot si es global). |

## Recetas completas

**Disparo con variación y punch**
```json
{ "path": "sfx/weapons/shot", "is3D": true, "spatial": { "minDistance": 2, "maxDistance": 60 }, "maxInstances": 6,
  "pitch": { "base": 0, "modulators": [ { "type": "Random", "min": -1, "max": 1, "unit": "Semitones" } ] },
  "tracks": [
    { "name": "Body", "regions": [ { "start": 0, "length": 0.5, "instrument": { "type": "RandomClip", "avoidRepeat": true, "clips": ["shot_01", "shot_02", "shot_03"] } } ],
      "effects": [ { "type": "SimpleCompressor", "params": { "threshold": -18, "ratio": 4, "makeup": 3 } } ] },
    { "name": "Low", "volume": -6, "regions": [ { "start": 0, "length": 0.4, "instrument": { "type": "SingleClip", "clip": "kick" } } ] }
  ] }
```

**Motor que sigue a la velocidad** (parámetro local `Speed`)
```json
{ "path": "sfx/vehicles/engine", "is3D": true, "parameters": [ { "name": "Speed", "min": 0, "max": 1, "seekSpeed": 3 } ],
  "pitch":  { "base": 0, "modulators": [ { "type": "ParameterCurve", "parameter": "Speed", "curve": [[0, -5], [1, 7]] } ] },
  "volume": { "base": 0, "modulators": [ { "type": "ParameterCurve", "parameter": "Speed", "curve": [[0, -8], [1, 0]] } ] },
  "tracks": [ { "name": "Loop", "regions": [ { "start": 0, "length": 4, "instrument": { "type": "SingleClip", "loop": true, "clip": "engine_loop" } } ] } ],
  "loopRegions": [ { "start": 0, "end": 4 } ] }
```

## Qué decirle al humano
- Después de un `preview_event`: "¿Lo escuchaste? ¿Más grave, más corto, más lejos…?".
- Si creaste un parámetro: "Pedile al programador que llame `SoddAudio.SetGlobalParameter("X", valor)`".
- Si el evento tiene loop: "Suena hasta que lo pares (stop_preview)".
- Todo se puede deshacer en Unity con **Ctrl+Z**.
