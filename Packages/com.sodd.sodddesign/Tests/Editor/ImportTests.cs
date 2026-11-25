using NUnit.Framework;
using Sodd.Design.Editor;
using UnityEditor;

namespace Sodd.Design.Tests
{
    public class ImportTests
    {
        [Test]
        public void SandboxEventsImportWithoutErrors()
        {
            var files = SoddProjectFiles.AllEventFiles();
            Assert.GreaterOrEqual(files.Count, 6, "Faltan eventos de ejemplo en Assets/SoddDesign/Events");
            foreach (var f in files)
            {
                var a = AssetDatabase.LoadAssetAtPath<SoddEventAsset>(f);
                Assert.IsNotNull(a, "No se importó como SoddEventAsset: " + f);
                foreach (var w in a.importWarnings) Assert.IsFalse(w.StartsWith("✖"), f + " → " + w);
                Assert.IsNotEmpty(a.data.id);
                foreach (var t in a.data.tracks)
                    foreach (var r in t.regions)
                        foreach (var c in r.instrument.clips)
                        {
                            Assert.GreaterOrEqual(c.clipIndex, 0, f + ": clip sin resolver " + c.path);
                            Assert.IsNotNull(a.clips[c.clipIndex]);
                        }
            }
        }

        [Test]
        public void EventsImportedBeforeTheirClipsHealOnReimport()
        {
            SoddAssetPostprocessor.ReimportEventsWithMissingClips();
            foreach (var a in SoddProjectFiles.AllEventAssets())
                foreach (var w in a.importWarnings)
                    Assert.IsFalse(SoddAssetPostprocessor.ClipNowAvailable(w), $"{a.Path}: {w}");
        }

        [Test]
        public void RegistryFindsEventsByPathAndId()
        {
            var reg = SoddProjectFiles.RebuildRegistry();
            Assert.IsNotNull(reg);
            Assert.IsNotNull(reg.Find("sfx/player/jump"));
            Assert.IsNotNull(reg.Find("5b0e7d9a-3c1f-4c55-9a51-0f1e2a3b4c03"));
            Assert.IsNotNull(reg.parametersJson);
            Assert.IsNotNull(reg.settingsJson);
            Assert.AreSame(reg.Find("sfx/player/jump"), reg.FindByHash(SoddText.Fnv1a("5b0e7d9a-3c1f-4c55-9a51-0f1e2a3b4c03")));
        }
    }
}
