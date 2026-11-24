using Sodd.Design;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Panel de prueba del Sandbox (OnGUI a propósito: es solo un panel de debug en runtime).
/// Teclas: 1 click UI · 2 paso · 3 salto · 4 golpe · M música · U/P/N snapshots · Espacio pausa.
/// </summary>
public class SandboxDemo : MonoBehaviour
{
    public SoddEventReference click = SoddEventReference.FromPath("sfx/ui/click");
    public SoddEventReference footstep = SoddEventReference.FromPath("sfx/player/footstep");
    public SoddEventReference jump = SoddEventReference.FromPath("sfx/player/jump");
    public SoddEventReference hit = SoddEventReference.FromPath("sfx/impacts/hit");
    public SoddEventReference music = SoddEventReference.FromPath("music/loop");
    public SoddEventReference lofi = SoddEventReference.FromPath("sfx/fx/lofi_beeps");
    public SoddEventReference robot = SoddEventReference.FromPath("sfx/fx/robot");
    public Transform orbiter;

    SoddEventInstance _music;
    float _health = 100, _intensity, _musicVol, _sfxVol;
    bool _paused;
    string _log = "";

    static bool Pressed(Key k) => Keyboard.current != null && Keyboard.current[k].wasPressedThisFrame;

    void Update()
    {
        if (orbiter != null)
        {
            float t = Time.time * 0.6f;
            orbiter.position = new Vector3(Mathf.Cos(t) * 8f, 1f, Mathf.Sin(t) * 8f);
        }
        if (Pressed(Key.Digit1)) SoddAudio.PlayOneShot(click);
        if (Pressed(Key.Digit2)) SoddAudio.PlayOneShot(footstep, transform.position + Random.insideUnitSphere * 3f);
        if (Pressed(Key.Digit3)) SoddAudio.PlayOneShot(jump, transform.position);
        if (Pressed(Key.Digit4)) Hit();
        if (Pressed(Key.Digit5)) SoddAudio.PlayOneShot(lofi);
        if (Pressed(Key.Digit6)) SoddAudio.PlayOneShot(robot);
        if (Pressed(Key.M)) ToggleMusic();
        if (Pressed(Key.U)) SoddAudio.SetSnapshot("Underwater", 1f);
        if (Pressed(Key.P)) SoddAudio.SetSnapshot("Pause", 0.5f);
        if (Pressed(Key.N)) SoddAudio.ClearSnapshot(1f);
        if (Pressed(Key.Space)) { _paused = !_paused; SoddAudio.Pause(_paused); }
    }

    void Hit() => SoddAudio.PlayOneShot(hit, orbiter != null ? orbiter.position : transform.position);

    void ToggleMusic()
    {
        if (_music.IsPlaying) { _music.Stop(); return; }
        _music.Release();
        _music = SoddAudio.CreateInstance(music);
        _music.OnMarker += (_, m) => _log = "Marker: " + m + " @ " + Time.time.ToString("0.00");
        _music.OnLoopExit += (_, l) => _log = "Salió del loop " + l;
        _music.OnStopped += _ => _log = "Música detenida";
        _music.Start();
    }

    void OnGUI()
    {
        GUILayout.BeginArea(new Rect(12, 12, 400, 540), GUI.skin.box);
        GUILayout.Label("<b>SoddDesign Sandbox</b>", new GUIStyle(GUI.skin.label) { richText = true, fontSize = 16 });
        GUILayout.Label("1 click · 2 paso · 3 salto · 4 golpe (orbita) · 5 lo-fi · 6 robot · M música\nU underwater · P menú pausa · N normal · Espacio pausa todo");
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Click")) SoddAudio.PlayOneShot(click);
        if (GUILayout.Button("Paso")) SoddAudio.PlayOneShot(footstep, transform.position);
        if (GUILayout.Button("Salto")) SoddAudio.PlayOneShot(jump, transform.position);
        if (GUILayout.Button("Golpe")) Hit();
        if (GUILayout.Button("Lo-fi")) SoddAudio.PlayOneShot(lofi);
        if (GUILayout.Button("Robot")) SoddAudio.PlayOneShot(robot);
        GUILayout.EndHorizontal();
        if (GUILayout.Button(_music.IsPlaying ? "■ Parar música (fade)" : "▶ Música en loop")) ToggleMusic();
        GUILayout.Label($"Health (global): {_health:0}  → salto más inestable con poca vida");
        float h = GUILayout.HorizontalSlider(_health, 0, 100);
        if (!Mathf.Approximately(h, _health)) { _health = h; SoddAudio.SetGlobalParameter("Health", h); }
        GUILayout.Label($"Intensity (global): {_intensity:0.00}  → sube el drone; > 0.9 sale del loop");
        float i = GUILayout.HorizontalSlider(_intensity, 0, 1);
        if (!Mathf.Approximately(i, _intensity)) { _intensity = i; SoddAudio.SetGlobalParameter("Intensity", i); }
        GUILayout.Label($"Bus Music: {_musicVol:0} dB");
        float mv = GUILayout.HorizontalSlider(_musicVol, -40, 6);
        if (!Mathf.Approximately(mv, _musicVol)) { _musicVol = mv; var b = SoddAudio.GetBus("Music"); if (b != null) b.Volume = mv; }
        GUILayout.Label($"Bus SFX: {_sfxVol:0} dB");
        float sv = GUILayout.HorizontalSlider(_sfxVol, -40, 6);
        if (!Mathf.Approximately(sv, _sfxVol)) { _sfxVol = sv; var b = SoddAudio.GetBus("SFX"); if (b != null) b.Volume = sv; }
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Underwater")) SoddAudio.SetSnapshot("Underwater", 1f);
        if (GUILayout.Button("Menú pausa")) SoddAudio.SetSnapshot("Pause", 0.5f);
        if (GUILayout.Button("Normal")) SoddAudio.ClearSnapshot(1f);
        GUILayout.EndHorizontal();
        var e = SoddAudio.Engine;
        if (e != null)
            GUILayout.Label($"Instancias: {e.StatInstances}  Voces: {e.StatRealVoices}/{e.VoiceCount}  Robadas: {e.StatStolen}\n" +
                            $"Snapshot: {SoddAudio.CurrentSnapshot ?? "-"}   Música t={_music.TimelinePosition:0.00}s\n{_log}");
        GUILayout.EndArea();
    }
}
