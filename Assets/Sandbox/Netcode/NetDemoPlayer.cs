using Sodd.Design;
using Sodd.Design.Netcode;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Demo de SoddDesign + Netcode for GameObjects (también en Samples~/NetcodeDemo).
/// Jugador en red: J = salto (evento Broadcast: lo escuchan todos, el que salta sin latencia),
/// F = paso (Local), H = golpe solo para el dueño (OwnerOnly vía parámetro ownerClientId).
/// Requiere un SoddNetworkAudio en un NetworkObject de la escena y un SoddNetworkEmitter opcional en el prefab.
/// </summary>
public class NetDemoPlayer : NetworkBehaviour
{
    public SoddEventReference jump = SoddEventReference.FromPath("sfx/player/jump");
    public SoddEventReference footstep = SoddEventReference.FromPath("sfx/player/footstep");
    public SoddEventReference hit = SoddEventReference.FromPath("sfx/impacts/hit");
    [Range(0, 100)] public float health = 100;

    void Update()
    {
        if (!IsOwner || Keyboard.current == null) return;
        var kb = Keyboard.current;
        if (kb.jKey.wasPressedThisFrame)
            SoddAudio.Net.PlayOneShot(jump, transform, new SoddNetParams().Add("Health", health));
        if (kb.fKey.wasPressedThisFrame)
            SoddAudio.Net.PlayOneShot(footstep, transform.position);
        if (kb.hKey.wasPressedThisFrame)
            SoddAudio.Net.PlayOneShot(hit, transform.position, default, OwnerClientId);
        if (kb.upArrowKey.isPressed && TryGetComponent<SoddNetworkEmitter>(out var engine))
            engine.SetParameter("Speed", Mathf.PingPong(Time.time * 0.5f, 1f));
    }
}
