using Sodd.Design;
using UnityEngine;

/// <summary>Sample Basic de SoddDesign: B = beep, P = paso, M = loop on/off (usa el Input Manager clásico o el nuevo vía OnGUI).</summary>
public class BasicDemo : MonoBehaviour
{
    public SoddEventReference beep = SoddEventReference.FromPath("samples/basic/beep");
    public SoddEventReference steps = SoddEventReference.FromPath("samples/basic/steps");
    public SoddEventReference loop = SoddEventReference.FromPath("samples/basic/loop");
    SoddEventInstance _loop;

    void OnGUI()
    {
        GUILayout.BeginArea(new Rect(10, 10, 220, 140), GUI.skin.box);
        if (GUILayout.Button("Beep")) SoddAudio.PlayOneShot(beep);
        if (GUILayout.Button("Paso (3D, delante)")) SoddAudio.PlayOneShot(steps, transform.position + transform.forward * 3f);
        if (GUILayout.Button(_loop.IsPlaying ? "Parar loop" : "Loop")) ToggleLoop();
        GUILayout.EndArea();
    }

    void ToggleLoop()
    {
        if (_loop.IsPlaying) { _loop.Stop(); _loop.Release(); return; }
        _loop = SoddAudio.CreateInstance(loop);
        _loop.OnMarker += (_, m) => Debug.Log("Marker " + m);
        _loop.Start();
    }
}
