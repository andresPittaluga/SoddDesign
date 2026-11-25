using System;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.Audio;

namespace Sodd.Design.Editor
{
    /// <summary>
    /// Genera el SoddMixer.mixer fijo del paquete (Runtime/Resources). Unity no tiene API pública para crear
    /// AudioMixers, así que se usa la API interna del Editor por reflection. Solo lo usa el desarrollador del
    /// paquete: el .mixer generado se versiona.
    /// Grupos: Master > Music, SFX, UI, Voice, Ambience, ReverbSend. SFX y Ambience mandan (Send) a ReverbSend.
    /// Parámetros expuestos: "<Grupo>_Volume" para todos los grupos.
    /// </summary>
    public static class SoddMixerBuilder
    {
        public const string MixerPath = "Packages/com.sodd.sodddesign/Runtime/Resources/SoddMixer.mixer";
        static readonly string[] Groups = { "Music", "SFX", "UI", "Voice", "Ambience", "ReverbSend" };
        static readonly string[] SendsToReverb = { "SFX", "Ambience", "Voice" };

        static Assembly EditorAsm => typeof(UnityEditor.Editor).Assembly;
        static Type T(string name) => EditorAsm.GetType("UnityEditor.Audio." + name, true);

        static object Call(object target, string method, params object[] args)
        {
            var type = target as Type ?? target.GetType();
            var flags = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
            var m = type.GetMethods(flags).FirstOrDefault(x => x.Name == method && x.GetParameters().Length == args.Length);
            if (m == null) throw new MissingMethodException(type.Name, method);
            return m.Invoke(target is Type ? null : target, args);
        }

        static object Get(object target, string prop) =>
            target.GetType().GetProperty(prop, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).GetValue(target);

        static void SetProp(object target, string prop, object value) =>
            target.GetType().GetProperty(prop, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).SetValue(target, value);

        [MenuItem("Sodd/Dev/Rebuild SoddMixer (package)", priority = 101)]
        public static void Build()
        {
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(MixerPath));
            if (AssetDatabase.LoadAssetAtPath<AudioMixer>(MixerPath) != null) AssetDatabase.DeleteAsset(MixerPath);

            var controllerType = T("AudioMixerController");
            var controller = Call(controllerType, "CreateMixerControllerAtPath", MixerPath);
            var master = Get(controller, "masterGroup");
            var snapshot = Get(controller, "TargetSnapshot");
            var groups = new System.Collections.Generic.Dictionary<string, object> { { "Master", master } };

            foreach (var g in Groups)
            {
                var grp = Call(controller, "CreateNewGroup", g, false);
                Call(controller, "AddChildToParent", grp, master);
                groups[g] = grp;
            }

            // ReverbSend: Receive + SFX Reverb al principio de la cadena.
            var effectType = T("AudioMixerEffectController");
            object NewEffect(string name)
            {
                var fx = Activator.CreateInstance(effectType, name);
                Call(fx, "PreallocateGUIDs");
                AssetDatabase.AddObjectToAsset((UnityEngine.Object)fx, (UnityEngine.Object)controller);
                return fx;
            }
            var rvGroup = groups["ReverbSend"];
            var receive = NewEffect("Receive");
            Call(rvGroup, "InsertEffect", receive, 0);
            var reverb = NewEffect("SFX Reverb");
            Call(rvGroup, "InsertEffect", reverb, 1);

            foreach (var g in SendsToReverb)
            {
                var send = NewEffect("Send");
                SetProp(send, "sendTarget", receive);
                var grp = groups[g];
                var effects = (Array)Get(grp, "effects");
                Call(grp, "InsertEffect", send, effects.Length); // después del Attenuation (post-fader)
                Call(send, "SetValueForMixLevel", controller, snapshot, 0f);
            }

            // Parámetros expuestos: volumen de cada grupo.
            var groupPathType = T("AudioGroupParameterPath");
            var exposedNames = new System.Collections.Generic.List<string>();
            foreach (var kv in groups)
            {
                var guid = Call(kv.Value, "GetGUIDForVolume");
                var path = Activator.CreateInstance(groupPathType, kv.Value, guid);
                Call(controller, "AddExposedParameter", path);
                exposedNames.Add(kv.Key + "_Volume");
            }
            // Renombrar (AddExposedParameter les pone nombres genéricos, en el mismo orden)
            var prop = controller.GetType().GetProperty("exposedParameters", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            var arr = (Array)prop.GetValue(controller);
            for (int i = 0; i < arr.Length && i < exposedNames.Count; i++)
            {
                var item = arr.GetValue(i);
                item.GetType().GetField("name").SetValue(item, exposedNames[i]);
                arr.SetValue(item, i);
            }
            prop.SetValue(controller, arr);
            // ReverbSend arranca en silencio: lo sube SoddReverbZone.
            Call(rvGroup, "SetValueForVolume", controller, snapshot, -80f);

            EditorUtility.SetDirty((UnityEngine.Object)controller);
            AssetDatabase.SaveAssets();
            AssetDatabase.ImportAsset(MixerPath);
            var mixer = AssetDatabase.LoadAssetAtPath<AudioMixer>(MixerPath);
            Debug.Log($"[SoddDesign] SoddMixer generado: {mixer != null}, grupos: {string.Join(", ", mixer.FindMatchingGroups("").Select(g => g.name))}, " +
                      $"expuestos: {string.Join(", ", exposedNames)}");
        }
    }
}
