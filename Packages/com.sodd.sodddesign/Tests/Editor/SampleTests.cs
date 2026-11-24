using System.IO;
using NUnit.Framework;
using Sodd.Design.Editor;

namespace Sodd.Design.Tests
{
    public class SampleTests
    {
        const string Root = "Packages/com.sodd.sodddesign/Samples~/Basic";

        [Test]
        public void BasicSampleEventsAreValidAndClipsShipWithMetas()
        {
            var known = SoddProjectFiles.LoadParameters()?.parameters;
            var files = Directory.GetFiles(Root + "/Events", "*.sodd.json");
            Assert.AreEqual(3, files.Length);
            foreach (var f in files)
            {
                var d = SoddReader.ParseEvent(File.ReadAllText(f), out var r, known);
                Assert.IsTrue(r.ok, f + " → " + r);
                StringAssert.StartsWith("samples/basic/", d.path);
                foreach (var t in d.tracks)
                    foreach (var g in t.regions)
                        foreach (var c in g.instrument.clips)
                        {
                            string meta = Root + "/Audio/" + Path.GetFileName(c.path) + ".meta";
                            Assert.IsTrue(File.Exists(meta), meta);
                            StringAssert.Contains("guid: " + c.guid, File.ReadAllText(meta));
                        }
            }
            foreach (var tpl in new[] { "sodd.settings.json.txt", "sodd.parameters.json.txt" })
                Assert.IsTrue(File.Exists(Root + "/Templates/" + tpl));
        }
    }
}
