# Guía para técnicos de audio (sin código)

Esta guía es para quien diseña el sonido. No necesitás programar: todo se hace en **SoddDesign Studio**
(`Sodd > SoddDesign Studio`). El programador solo dispara eventos por nombre y te pasa valores del juego.

## Conceptos (en 1 minuto)
- **Evento**: un sonido del juego ("salto", "música del nivel 1", "motor del auto"). Tiene un nombre con
  carpetas: `sfx/player/jump`. Es lo único que el programador conoce.
- **Track**: una capa dentro del evento (como una pista en un DAW). Todos los tracks suenan juntos.
  Colores: naranja = SFX, violeta = música, verde = voz, azul = ambiente.
- **Región**: un bloque en el timeline de un track. Adentro tiene un **instrumento**:
  - *SingleClip*: un audio.
  - *RandomClip*: uno al azar de una lista (con "evitar repetir" y pesos).
  - *Sequential*: en orden, uno por vez.
  - *Silence*: nada (sirve para espaciar).
  - *NestedEvent*: otro evento adentro (ej. una explosión que incluye "escombros").
- **Parámetro**: un número que manda el juego (vida, velocidad, intensidad). Los globales son de todo el juego;
  los locales son de cada instancia (cada auto tiene su velocidad).
- **Modulador**: algo que mueve una perilla automáticamente. Toda perilla numérica puede tener moduladores.
- **Bus**: grupo de mezcla (Music, SFX, UI, Voice, Ambience). **Snapshot**: un "preset de mezcla" (ej. bajo el agua).

## Dónde está todo: el menú `Sodd`
Todo SoddDesign sale de su propio menú, **Sodd**, en la barra de arriba de Unity:

| Menú | Para qué |
|---|---|
| `Sodd > SoddDesign Studio` | Editar eventos (la ventana principal) |
| `Sodd > Mixer` | Faders de buses, mute/solo y snapshots |
| `Sodd > Profiler` | Instancias y voces sonando en vivo |
| `Sodd > Guía de uso` | Abre esta guía |
| `Sodd > Atajos de Studio` | Abre el Studio con la tabla de atajos |
| `Sodd > Validate All Events` / `Rebuild Registry` / `Optimize Audio Import Settings…` | Mantenimiento |
| `Sodd > AI Bridge` | Conectar una IA (MCP) |

En el Studio, el botón **?** (arriba a la derecha) muestra los atajos en cualquier momento.

## El Studio
- **Izquierda**: todos los eventos. Buscador y tags. Doble click = escuchar. Click derecho = renombrar/duplicar/borrar.
- **Centro**: timeline. Rueda = zoom; botón del medio o Alt+arrastrar = moverse. Arriba: regla con loops (barra
  celeste) y markers (banderitas amarillas). Click derecho en la regla = agregar marker o loop.
- **Derecha**: Inspector de lo que tengas seleccionado (evento, track o región).
- **Abajo**: parámetros (sliders en vivo), transporte (▶ ■) y medidor.

### Atajos
| Tecla o gesto | Acción |
|---|---|
| Espacio | Play / Stop |
| Esc | Stop y volver al inicio |
| S | Cortar la región en el playhead |
| D o Ctrl+D | Duplicar región |
| Supr o Backspace | Borrar lo seleccionado (región, marker o track) |
| F | Ajustar zoom al evento |
| Ctrl+Z / Ctrl+Y | Deshacer / rehacer (todo se puede deshacer) |
| Shift + arrastrar | Mover sin snap (regiones, markers y playhead) |

Los atajos se pueden cambiar en `Edit > Shortcuts > SoddDesign Studio`.

### Regiones
- **Arrastrar** una región: moverla en el tiempo o a otro track. **Bordes**: recortar el inicio o el final.
- **Circulitos de arriba**: fade-in (izquierda) y fade-out (derecha).
- **Arrastrar wavs** desde el Project: crea regiones (si soltás debajo del último track, crea uno nuevo).
- **Click derecho**: dividir, duplicar o borrar.

### Tracks
- **Reordenar**: arrastrá el nombre del track hacia arriba o abajo. Una línea celeste marca dónde va a quedar.
- **Click derecho en el nombre**: renombrar, subir, bajar, duplicar (con regiones y efectos) o borrar.
- **Volumen rápido**: arrastrá el chip de dB del track hacia arriba o abajo (Shift = fino, doble click = 0 dB).
- **M / S**: mute y solo.

### Markers
- **Crear**: click derecho en la regla → *Agregar marker*.
- **Seleccionar**: click en la banderita. **Mover**: arrastrarla. **Renombrar**: doble click.
- **Borrar**: seleccionarlo y Supr, o click derecho → *Borrar marker*.
- El juego recibe cada marker como callback `OnMarker` con su nombre.

### Perillas (como en FMOD)
Volumen, pitch, pan y los parámetros de los efectos se editan con perillas:
- **Arrastrar arriba/abajo** sobre la perilla o su nombre cambia el valor. Con **Shift**, el ajuste es fino.
- **Rueda del mouse** encima: pasos chicos.
- **Doble click en la perilla**: vuelve al valor por defecto.
- **Doble click en el número**: escribís el valor exacto.
- El cambio se guarda al soltar: cada arrastre es un solo paso de Ctrl+Z.
- Los valores sin rango fijo (tiempos, distancias) siguen siendo campos de texto: arrastrá su nombre o escribí el número.

## Recetas
**Pasos que no suenan repetidos**: región RandomClip con 4+ variaciones, "Evitar repetir" ✔. En el evento:
Pitch ＋ Random (−1.5 a 1.5) y Volumen ＋ Random (−3 a 0 dB).

**Motor**: parámetro local `Speed` (0..1, seek 3/s). Región con el loop del motor (Loop del clip ✔) y un loop
region que cubra todo. Pitch ＋ ParameterCurve (Speed): curva de −5 a +7. Volumen ＋ ParameterCurve: −8 a 0.

**Música que reacciona**: track base + track "tensión" con Volumen ＋ ParameterCurve sobre `Intensity`
(−40 dB en 0, 0 dB en 1). Loop sobre el compás con "Salir si Intensity > 0.9": termina el compás y pasa al final.

**Más inestable con poca vida**: en Pitch ＋ RandomScaledByParameter, parámetro Health, mín −4 / máx 4,
curva: 1 en vida 0, 0 en vida 100.

**Lejos / bajo el agua**: efecto LowPass (cutoff 600–2000). Para todo el juego, usá el snapshot *Underwater*.

**Que no se sature si se dispara mucho**: en el evento, Máx. instancias 4 + "Al superar: Oldest"; o Cooldown 0.05 s.

## Moduladores en detalle
| Tipo | Para qué | Campos |
|---|---|---|
| Random | Variación en cada disparo | min, max, unidad (Auto = la de la perilla), por disparo |
| ParameterCurve | Seguir un parámetro del juego | parámetro + curva (x = parámetro, y = valor) |
| RandomScaledByParameter | Variación que crece/decrece con un parámetro | parámetro, min/max, curva de escala 0..1 |
| LFO | Oscilación (vibrato, trémolo, "respirar") | forma, rate (Hz), depth, fase |
| Envelope | Subida/bajada automática al empezar | attack, hold, release |
| ADSR | Fade-in al empezar y release al detener | attack, decay, sustain, release |

Editor de curvas: arrastrá los puntos, **doble click** agrega uno, **click derecho** lo borra.

## Efectos
LowPass/HighPass (filtros), Echo, Reverb, Distortion, Chorus (nativos de Unity) y Bitcrusher, Tremolo, AutoPan,
RingMod, Saturator, SimpleDelay, SimpleCompressor (propios). Todos sus parámetros aceptan moduladores.
El orden de proceso es fijo: filtros → saturación/distorsión → modulación → compresor → delays → reverb.

## Mixer y Profiler
- **Mixer** (botón en la barra del Studio): faders por bus, M/S/pausa, snapshots. "Guardar volúmenes" los deja
  fijos en el proyecto.
- **Profiler**: cuántas instancias y voces están sonando, cuántas se "robaron" por falta de voces.

## Prioridad y voces
Hay un número limitado de voces (32 por defecto). Si se llenan, se roba la voz menos importante:
**prioridad 0 = la más importante** (música, diálogos), 256 = la menos (ambiente lejano). A igual prioridad se roba
la que se escucha menos.

## Trabajar con IA
Si tenés Claude Code con SoddDesign conectado, podés pedir en castellano: "hacé el salto más grave", "que el
motor suene más agresivo", "creá un ambiente de lluvia con los archivos de la carpeta Rain". Todo lo que hace
queda en el Studio y se puede deshacer con Ctrl+Z.
