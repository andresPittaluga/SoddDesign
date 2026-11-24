using Sodd.Design;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>Genera Assets/Sandbox/Sandbox.unity (menú Sodd/Sandbox/Rebuild Sandbox Scene o batchmode -executeMethod).</summary>
public static class SandboxSceneBuilder
{
    public const string ScenePath = "Assets/Sandbox/Sandbox.unity";

    static SoddEventAsset Ev(string p) => AssetDatabase.LoadAssetAtPath<SoddEventAsset>("Assets/SoddDesign/Events/" + p + ".sodd.json");

    [MenuItem("Sodd/Sandbox/Rebuild Sandbox Scene")]
    public static void Build()
    {
        var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
        var cam = Camera.main;
        cam.transform.position = new Vector3(0, 9, -14);
        cam.transform.rotation = Quaternion.Euler(32, 0, 0);
        cam.gameObject.AddComponent<SoddListener>();
        var demo = cam.gameObject.AddComponent<SandboxDemo>();
        demo.click = new SoddEventReference(Ev("sfx/ui/click"));
        demo.footstep = new SoddEventReference(Ev("sfx/player/footstep"));
        demo.jump = new SoddEventReference(Ev("sfx/player/jump"));
        demo.hit = new SoddEventReference(Ev("sfx/impacts/hit"));
        demo.music = new SoddEventReference(Ev("music/loop"));

        var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
        floor.name = "Floor";
        floor.transform.localScale = new Vector3(3, 1, 3);

        var orbiter = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        orbiter.name = "Orbiter (golpes 3D)";
        demo.orbiter = orbiter.transform;

        var drone = GameObject.CreatePrimitive(PrimitiveType.Cube);
        drone.name = "Drone Emitter (ambience/drone)";
        drone.transform.position = new Vector3(-6, 0.5f, 4);
        var em = drone.AddComponent<SoddEmitter>();
        em.eventRef = new SoddEventReference(Ev("ambience/drone"));
        em.playOn = SoddPlayTrigger.Start;

        var hitPad = GameObject.CreatePrimitive(PrimitiveType.Cube);
        hitPad.name = "Hit Emitter (sin auto-play)";
        hitPad.transform.position = new Vector3(6, 0.5f, 4);
        var em2 = hitPad.AddComponent<SoddEmitter>();
        em2.eventRef = new SoddEventReference(Ev("sfx/impacts/hit"));
        em2.playOn = SoddPlayTrigger.None;

        EditorSceneManager.SaveScene(scene, ScenePath);
        var list = new System.Collections.Generic.List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
        if (!list.Exists(s => s.path == ScenePath)) { list.Insert(0, new EditorBuildSettingsScene(ScenePath, true)); EditorBuildSettings.scenes = list.ToArray(); }
        Debug.Log("[SoddDesign] Sandbox scene generada en " + ScenePath);
    }
}
