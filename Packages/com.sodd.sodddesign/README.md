# SoddDesign

**Middleware de audio gratuito para Unity 6** (estilo FMOD) de Sodd Studio: eventos con timeline, capas,
moduladores, efectos, buses, snapshots, audio 3D, editor visual (**SoddDesign Studio**), red (Netcode for
GameObjects) y un puente **MCP** para que una IA diseñe sonido con vos.

- Datos en **JSON legible** (`*.sodd.json`) + JSON Schema documentado → fáciles de versionar, revisar y generar con IA.
- Runtime sin dependencias, sin allocations por frame, timeline **sample-accurate** (`PlayScheduled`).
- Editor 100% UI Toolkit, con Undo en todo.

## Instalación
Package Manager → *Add package from git URL*:
```
https://github.com/<usuario>/SoddDesign.git?path=/Packages/com.sodd.sodddesign#v0.1.0
```
O copiá la carpeta `com.sodd.sodddesign` dentro de `Packages/` de tu proyecto.

Opcionales (se activan solos si están instalados): `com.unity.netcode.gameobjects` 2.x (`SODD_NGO`) y
`com.unity.addressables` (`SODD_ADDRESSABLES`).

## En 30 segundos
```csharp
using Sodd.Design;

public class Player : MonoBehaviour
{
    public SoddEventReference jump;          // campo con buscador en el Inspector
    SoddEventInstance _engine;

    void Start()
    {
        _engine = SoddAudio.CreateInstance("sfx/vehicles/engine");
        _engine.AttachTo(transform);
        _engine.Start();
    }

    void Update()
    {
        if (Input.GetButtonDown("Jump")) SoddAudio.PlayOneShot(jump, transform.position);
        _engine.SetParameter("Speed", rb.velocity.magnitude / maxSpeed);
        SoddAudio.SetGlobalParameter("Health", health);
    }

    void OnDestroy() { _engine.Stop(); _engine.Release(); }
}
```
Más: [QUICKSTART](Documentation~/QUICKSTART.md) · [Guía para técnicos de audio (sin código)](Documentation~/GUIA_TECNICO_AUDIO.md) ·
[Guía para IA](Documentation~/AI_GUIDE.md) · [MCP](Tools~/sodddesign-mcp/README.md) · [Mockup del Studio](Documentation~/ui-mockup.html)

## Qué incluye
| Área | Contenido |
|---|---|
| Eventos | Tracks (SFX/Music/Voice/Ambience), regiones con fades, instrumentos SingleClip / RandomClip (pesos, sin repetir) / Sequential / Silence / NestedEvent, loops con condición de salida, markers. |
| Moduladores | En cualquier propiedad numérica: Random, ParameterCurve, RandomScaledByParameter, LFO, Envelope, ADSR. |
| 3D | spatialBlend, min/max distance, rolloff Log/Linear/Custom, doppler, spread, gizmos. |
| Voces | Pool, prioridad 0–256, robo por prioridad/audibilidad, maxInstances + Oldest/Quietest/None, cooldown. |
| Efectos | Nativos: LowPass, HighPass, Echo, Reverb, Distortion, Chorus. DSP propios: Bitcrusher, Tremolo, AutoPan, RingMod, Saturator, SimpleDelay, SimpleCompressor. |
| Mezcla | Buses lógicos (volumen/mute/solo/pausa/fade), `SoddMixer.mixer` con ReverbSend, snapshots (volumen + filtros), `SoddReverbZone`. |
| Editor | SoddDesign Studio (navegador, timeline, inspector, parámetros, transporte, medidor), Mixer, Profiler, preview sin Play Mode, live update en Play Mode, optimizador de import settings. |
| IA | `SoddCommands` (capa única de edición con Undo), bridge JSON-RPC en 127.0.0.1 con token, servidor MCP (26 tools), `AI_GUIDE.md`. |
| Red | `SoddNetworkAudio` (Broadcast / OwnerOnly sin duplicados), `SoddNetworkEmitter` (NetworkVariables con umbral), `SoddAudio.Net`. |
| Bancos | `SoddBank`, `SoddBankLoader`, `SoddAddressableBanks`. |

## Archivos de proyecto
- `Assets/SoddDesign/sodd.settings.json` — buses, voces, snapshots.
- `Assets/SoddDesign/sodd.parameters.json` — parámetros globales/locales con rango, seek speed y descripción.
- `Assets/SoddDesign/Events/**.sodd.json` — un archivo por evento ([schema](Schema/sodd.event.schema.json)).
- `Assets/SoddDesign/Resources/SoddRegistry.asset` — lo mantiene el Editor (no editar).

## Licencia
MIT — Sodd Studio.
