# QUICKSTART — SoddDesign en 10 minutos

## 1. Instalar
Agregá el paquete (git URL o carpeta en `Packages/`). Al abrir Unity se crea, si hace falta,
`Assets/SoddDesign/Resources/SoddRegistry.asset`. Creá `Assets/SoddDesign/sodd.settings.json` y
`sodd.parameters.json` (podés copiarlos del sample *Basic*).

## 2. Primer evento (sin código)
1. `Sodd > SoddDesign Studio` → **＋ Evento** → `sfx/player/jump`.
2. Arrastrá un `.wav` desde el Project al timeline → se crea un track con una región.
3. Click en el track → Inspector → Pitch → **＋** → *Random* (min −1, max 1). Cada salto suena distinto.
4. **Espacio** para escuchar (no hace falta Play Mode).

## 3. Reproducirlo desde código
```csharp
public SoddEventReference jump;                       // elegilo en el Inspector (tiene buscador)
SoddAudio.PlayOneShot(jump, transform.position);      // fire & forget
SoddAudio.PlayOneShot("sfx/player/jump", transform);  // por path, adjunto al transform
```

## 4. Eventos largos / con parámetros
```csharp
var music = SoddAudio.CreateInstance("music/level1");
music.OnMarker += (i, name) => Debug.Log("marker " + name);
music.OnStopped += i => Debug.Log("terminó");
music.Start();
SoddAudio.SetGlobalParameter("Intensity", 0.8f);     // la música sale del loop si así está configurada
music.Stop(SoddStopMode.AllowFadeOut);               // respeta el release del ADSR
music.Release();                                     // el handle queda inválido (seguro)
```

## 5. Sin código: SoddEmitter
`Add Component > Sodd > Sodd Emitter` → elegí el evento, *Play On* (Start/Enable/TriggerEnter/CollisionEnter),
parámetros iniciales y *Stop On*. Gizmos de min/max distance en la Scene.

## 6. Mezcla
```csharp
SoddAudio.GetBus("Music").FadeTo(-12, 1f);           // dB
SoddAudio.GetBus("SFX").Mute = true;
SoddAudio.SetSnapshot("Underwater", 0.8f);           // definido en sodd.settings.json
SoddAudio.ClearSnapshot(0.8f);
SoddAudio.Pause(true);                               // menú de pausa
SoddAudio.StopAll("SFX");
```

## 7. Red (Netcode for GameObjects 2.x)
Poné un `SoddNetworkAudio` en un NetworkObject de la escena y usá:
```csharp
SoddAudio.Net.PlayOneShot(jump, transform, new SoddNetParams().Add("Health", hp));
```
El evento decide (`networkMode`): Local / Broadcast / OwnerOnly. Ver `Samples~/NetcodeDemo`.

## 8. IA
`Sodd > AI Bridge > Copy Claude Code command` → pegalo en una terminal. Después pedile a Claude cosas
como "creá un sonido de moneda que suba de tono y hacémelo escuchar".

## 9. Antes de publicar
- `Sodd > Validate All Events`.
- `Sodd > Optimize Audio Import Settings…` (o botón **Optimizar** en el Studio).
- Si tenés muchos eventos por nivel: `Create > Sodd > Sound Bank` + `SoddBankLoader` o Addressables.

## Referencia rápida de la API
| Llamada | Qué hace |
|---|---|
| `SoddAudio.PlayOneShot(ref/path, pos/transform)` | Dispara y se libera solo. |
| `SoddAudio.CreateInstance(ref/path)` | Instancia controlable (Start/Stop/SetParameter/AttachTo/Set3DPosition/Release). |
| `instance.IsPlaying / TimelinePosition` | Estado. |
| `instance.OnMarker / OnStopped / OnLoopExit` | Callbacks. |
| `SoddAudio.SetGlobalParameter(name/id, v)` | Parámetros globales (con seek speed). |
| `SoddAudio.GetBus(name)` | Volume (dB), Mute, Solo, Paused, FadeTo. |
| `SoddAudio.SetSnapshot(name, fade)` | Snapshots. |
| `SoddAudio.StopAll(bus, mode)` / `SoddAudio.Pause(bool)` | Global. |
