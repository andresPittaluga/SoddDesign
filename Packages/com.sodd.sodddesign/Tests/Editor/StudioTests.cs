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
        public void MultiInstrumentStripsKeepTheirNamesReadable()
        {
            SoddInstrument Multi(int n) { var i = new SoddInstrument { type = SoddInstrumentType.RandomClip, clips = new SoddClipRef[n] }; for (int k = 0; k < n; k++) i.clips[k] = new SoddClipRef(); return i; }
            Assert.AreEqual(1, SoddTimelineCanvas.StripCount(new SoddInstrument { type = SoddInstrumentType.SingleClip, clips = new[] { new SoddClipRef() } }, 38f));
            Assert.AreEqual(2, SoddTimelineCanvas.StripCount(Multi(2), 38f));
            Assert.AreEqual(3, SoddTimelineCanvas.StripCount(Multi(5), 38f), "con 38 px entran 3 franjas de al menos 10 px");
            Assert.AreEqual(6, SoddTimelineCanvas.StripCount(Multi(12), 200f), "nunca más de 6");
            Assert.AreEqual(1, SoddTimelineCanvas.StripCount(Multi(4), 15f), "si no entran 2 con nombre, una sola waveform");
        }

        [Test]
        public void TrackHeightsStackAndClamp()
        {
            var s = new SoddTimelineState();
            s.SetTrackHeight(0, 100f, false);
            Assert.AreEqual(100f, s.TrackTop(1));
            Assert.AreEqual(100f + SoddTimelineState.DefaultTrackHeight, s.TrackTop(2));
            s.SetTrackHeight(1, 1000f, false);
            Assert.AreEqual(SoddTimelineState.MaxTrackHeight, s.TrackHeight(1));
            s.SetTrackHeight(1, 1f, false);
            Assert.AreEqual(SoddTimelineState.MinTrackHeight, s.TrackHeight(1));
        }

        [Test]
        public void F2RenamesWhatIsSelected()
        {
            Assert.AreEqual(SoddStudioWindow.RenameKind.Marker, SoddStudioWindow.RenameTarget(0, 1, 2));
            Assert.AreEqual(SoddStudioWindow.RenameKind.Region, SoddStudioWindow.RenameTarget(-1, 1, 2));
            Assert.AreEqual(SoddStudioWindow.RenameKind.Track, SoddStudioWindow.RenameTarget(-1, 1, -1));
            Assert.AreEqual(SoddStudioWindow.RenameKind.Event, SoddStudioWindow.RenameTarget(-1, -1, -1));
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
