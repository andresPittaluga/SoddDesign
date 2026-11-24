# SoddDesign — Netcode Demo (NGO 2.x)

Requiere `com.unity.netcode.gameobjects` 2.x (el assembly `Sodd.Design.Netcode` compila solo si está instalado).

## Setup (5 minutos)
1. Escena con un **NetworkManager** (Unity Transport) y un GameObject vacío "SoddNet" con **NetworkObject** +
   **SoddNetworkAudio** (`Add Component > Sodd > Sodd Network Audio`). Dejalo en la escena (in-scene placed).
2. Prefab de jugador con **NetworkObject** + `NetDemoPlayer` (este sample) y opcional **SoddNetworkEmitter**
   (evento con loop, ej. un motor; `parameterNames = ["Speed"]`). Asignalo como Player Prefab del NetworkManager.
3. Build + Editor (o Multiplayer Play Mode): uno Host, otro Client.

## Qué probar
- **J**: salto (`networkMode: Broadcast`). Suena al instante en quien salta y en el resto llega por RPC.
  El host NO lo escucha dos veces (estrategia: reproducción local + `SendTo.NotMe`).
- **F**: paso (`Local`): solo lo escucha quien lo dispara.
- **H**: golpe con `ownerClientId` → si el evento es `OwnerOnly`, solo lo escucha el dueño.
- **↑** con SoddNetworkEmitter: el parámetro Speed se sincroniza por NetworkVariable con umbral (0.02) y
  frecuencia máxima (20 Hz). Cada cliente reproduce su propia instancia local adjunta al objeto.

## Qué viaja por red
`hash (uint) del id del evento` + `posición` o `NetworkObjectReference` + hasta 4 parámetros `(hash del nombre, float)`.
Nunca audio. ~30–50 bytes por one-shot.

## API
```csharp
SoddAudio.Net.PlayOneShot(jumpRef, transform, new SoddNetParams().Add("Health", 30));
SoddAudio.Net.PlayOneShot("sfx/impacts/hit", pos, default, ownerClientId);
GetComponent<SoddNetworkEmitter>().SetParameter("Speed", 0.8f); // solo el dueño
```
