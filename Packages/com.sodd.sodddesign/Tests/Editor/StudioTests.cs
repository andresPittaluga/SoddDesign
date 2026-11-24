using NUnit.Framework;
using Sodd.Design.Editor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Sodd.Design.Tests
{
    public class StudioTests
    {
        [Test]
        public void StudioBuildsForEverySandboxEventTrackAndRegion()
        {
            var w = ScriptableObject.CreateInstance<SoddStudioWindow>();
            try
            {
                w.CreateGUI();
                Assert.IsNotNull(w.Root, "UXML no cargó");
                foreach (var a in SoddProjectFiles.AllEventAssets())
                {
                    w.SelectEvent(a.Path);
                    Assert.AreEqual(a.Path, w.Root.Q<Label>("event-title").text);
                    var insp = w.Root.Q("inspector");
                    Assert.Greater(insp.Query<VisualElement>().ToList().Count, 5, "inspector de evento vacío: " + a.Path);
                    for (int t = 0; t < a.data.tracks.Length; t++)
                    {
                        w.State.Select(t, -1);
                        Assert.Greater(insp.childCount, 0);
                        for (int r = 0; r < a.data.tracks[t].regions.Length; r++) { w.State.Select(t, r); Assert.Greater(insp.childCount, 0); }
                    }
                    w.State.Select(-1, -1);
                    Assert.AreEqual(a.data.tracks.Length, w.Root.Q("track-headers").Query(className: "sd-thead").ToList().Count);
                }
            }
            finally { Object.DestroyImmediate(w); }
        }

        [Test]
        public void WaveformCacheBuildsTexture()
        {
            var clip = UnityEditor.AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Sandbox/Audio/kick.wav");
            var tex = SoddWaveformCache.Get(clip);
            Assert.IsNotNull(tex);
            Assert.AreSame(tex, SoddWaveformCache.Get(clip), "se cachea");
        }

        [Test]
        public void StudioShortcutsAreRegistered()
        {
            var ids = new System.Collections.Generic.List<string>(UnityEditor.ShortcutManagement.ShortcutManager.instance.GetAvailableShortcutIds());
            foreach (var id in new[] { "Play - Stop", "Stop y volver al inicio", "Deshacer", "Rehacer", "Dividir region", "Duplicar region", "Borrar", "Encuadrar" })
                Assert.Contains("SoddDesign Studio/" + id, ids);
            var play = UnityEditor.ShortcutManagement.ShortcutManager.instance.GetShortcutBinding("SoddDesign Studio/Play - Stop");
            Assert.AreEqual(KeyCode.Space, System.Linq.Enumerable.First(play.keyCombinationSequence).keyCode);
        }
    }
}
