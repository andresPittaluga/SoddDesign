using System.IO;
using NUnit.Framework;
using Sodd.Design.Editor;
using Sodd.Design.Json;
using UnityEditor;

namespace Sodd.Design.Tests
{
    public class CommandTests
    {
        const string P = "zz_tests/cmd_event";
        const string P2 = "zz_tests/moved/renamed";

        [SetUp]
        public void SetUp() { Cleanup(); SoddUndo.ClearHistory(); }

        [TearDown]
        public void TearDown() { SoddPreview.Shutdown(); Cleanup(); SoddUndo.ClearHistory(); }

        static void Cleanup()
        {
            string folder = SoddProjectFiles.EventsFolder + "/zz_tests";
            if (AssetDatabase.IsValidFolder(folder)) AssetDatabase.DeleteAsset(folder);
        }

        static JsonNode Dom(string evt) => SoddJson.Parse(SoddCommands.GetEvent(evt).data);

        [Test]
        public void CreateEditAndUndoRedo()
        {
            var r = SoddCommands.CreateEvent(P, "test", is3D: true);
            Assert.IsTrue(r.ok, r.ToString());
            string id = SoddJson.Parse(r.data)["id"].String;
            Assert.IsNotNull(AssetDatabase.LoadAssetAtPath<SoddEventAsset>(SoddCommands.FileForPath(P)));

            Assert.IsTrue(SoddCommands.AddTrack(P, "Kick").ok);
            r = SoddCommands.AddRegion(P, 0, 0.5f, 1f, "{\"type\":\"SingleClip\",\"clip\":\"kick\"}");
            Assert.IsTrue(r.ok, r.ToString());
            var clip = SoddJson.Resolve(Dom(P), "/tracks/0/regions/0/instrument/clip");
            Assert.AreEqual("Assets/Sandbox/Audio/kick.wav", clip["path"].String, "el nombre corto se normaliza a path + guid");
            Assert.AreEqual(32, clip["guid"].String.Length);

            Assert.IsTrue(SoddCommands.SetProperty(id, "/tracks/0/volume", "-6").ok);
            Assert.AreEqual(-6, SoddJson.Resolve(Dom(P), "/tracks/0/volume").Number);
            var asset = AssetDatabase.LoadAssetAtPath<SoddEventAsset>(SoddCommands.FileForPath(P));
            Assert.AreEqual(-6f, asset.data.tracks[0].volume.@base, "el asset importado refleja el cambio");

            Undo.PerformUndo();
            Assert.IsNull(SoddJson.Resolve(Dom(P), "/tracks/0/volume"), "undo quita el volumen");
            Undo.PerformRedo();
            Assert.AreEqual(-6, SoddJson.Resolve(Dom(P), "/tracks/0/volume").Number, "redo lo vuelve a poner");
        }

        [Test]
        public void InvalidEditIsRejectedAndNothingIsWritten()
        {
            Assert.IsTrue(SoddCommands.CreateEvent(P).ok);
            string before = File.ReadAllText(SoddCommands.FileForPath(P));
            var r = SoddCommands.AddModulator(P, "/pitch", "{\"type\":\"ParameterCurve\",\"parameter\":\"Vida\",\"curve\":[[0,0],[1,1]]}");
            Assert.IsFalse(r.ok);
            StringAssert.Contains("¿Quisiste decir 'Health'?", r.errors[0]);
            Assert.AreEqual(before, File.ReadAllText(SoddCommands.FileForPath(P)));
            Assert.IsFalse(SoddCommands.SetProperty(P, "/bus", "\"Musica\"").ok);
            Assert.IsFalse(SoddCommands.SetProperty(P, "/id", "\"x\"").ok);
        }

        [Test]
        public void ModulatorsAddAndRemove()
        {
            Assert.IsTrue(SoddCommands.CreateEvent(P).ok);
            var r = SoddCommands.AddModulator(P, "/pitch", "{\"type\":\"RandomScaledByParameter\",\"parameter\":\"Health\",\"range\":4,\"unit\":\"Semitones\",\"curve\":[[0,1],[100,0]]}");
            Assert.IsTrue(r.ok, r.ToString());
            Assert.AreEqual(1, SoddJson.Resolve(Dom(P), "/pitch/modulators").Count);
            Assert.IsTrue(SoddCommands.SetProperty(P, "/pitch", "2").ok, "un número sobre prop modulada cambia el base");
            Assert.AreEqual(2, SoddJson.Resolve(Dom(P), "/pitch/base").Number);
            Assert.IsTrue(SoddCommands.RemoveModulator(P, "/pitch", 0).ok);
            Assert.IsTrue(SoddJson.Resolve(Dom(P), "/pitch").IsNumber, "sin moduladores vuelve al atajo numérico");
        }

        [Test]
        public void MoveRenameDeleteWithUndo()
        {
            var r = SoddCommands.CreateEvent(P);
            string id = SoddJson.Parse(r.data)["id"].String;
            string guidBefore = AssetDatabase.AssetPathToGUID(SoddCommands.FileForPath(P));
            Assert.IsTrue(SoddCommands.MoveEvent(id, P2).ok);
            Assert.IsTrue(File.Exists(SoddCommands.FileForPath(P2)));
            Assert.IsFalse(File.Exists(SoddCommands.FileForPath(P)));
            Assert.AreEqual(guidBefore, AssetDatabase.AssetPathToGUID(SoddCommands.FileForPath(P2)), "mover conserva el GUID del asset");
            Assert.AreEqual(P2, Dom(id)["path"].String);
            Undo.PerformUndo();
            Assert.IsTrue(File.Exists(SoddCommands.FileForPath(P)), "undo del move");
            Assert.IsTrue(SoddCommands.RenameEvent(id, "nuevo").ok);
            Assert.AreEqual("zz_tests/nuevo", Dom(id)["path"].String);
            Assert.IsTrue(SoddCommands.DeleteEvent(id).ok);
            Assert.IsNull(SoddCommands.LocateFile(id));
            Undo.PerformUndo();
            Assert.IsNotNull(SoddCommands.LocateFile(id), "undo del delete restaura el archivo");
        }

        [Test]
        public void SplitDuplicateMarkersLoopsEffectsPatch()
        {
            Assert.IsTrue(SoddCommands.CreateEvent(P).ok);
            Assert.IsTrue(SoddCommands.AddTrack(P, "A").ok);
            Assert.IsTrue(SoddCommands.AddRegion(P, 0, 0, 2, "{\"type\":\"SingleClip\",\"clip\":\"Assets/Sandbox/Audio/drone_110.wav\"}").ok);
            Assert.IsTrue(SoddCommands.SplitRegion(P, 0, 0, 0.5f).ok);
            var regs = SoddJson.Resolve(Dom(P), "/tracks/0/regions");
            Assert.AreEqual(2, regs.Count);
            Assert.AreEqual(0.5, regs[1]["start"].Number, 1e-6);
            Assert.AreEqual(0.5, regs[1]["instrument"]["startOffset"].Number, 1e-6);
            Assert.IsTrue(SoddCommands.DuplicateRegion(P, 0, 1).ok);
            Assert.IsTrue(SoddCommands.MoveRegion(P, 0, 2, 3f).ok);
            Assert.IsTrue(SoddCommands.AddMarker(P, "Hit", 0.25f).ok);
            Assert.IsTrue(SoddCommands.AddLoopRegion(P, 0, 2, "{\"parameter\":\"Intensity\",\"op\":\"Greater\",\"value\":0.5}").ok);
            Assert.IsTrue(SoddCommands.AddEffect(P, 0, "{\"type\":\"LowPass\",\"params\":{\"cutoff\":800}}").ok);
            Assert.IsFalse(SoddCommands.AddEffect(P, 0, "\"Flanger\"").ok);
            Assert.IsTrue(SoddCommands.EditEvent(P, "[{\"op\":\"replace\",\"path\":\"/description\",\"value\":\"patched\"}]").ok);
            var d = Dom(P);
            Assert.AreEqual("patched", d["description"].String);
            Assert.AreEqual(1, d["markers"].Count);
            Assert.AreEqual(1, d["loopRegions"].Count);
            Assert.AreEqual(3, SoddJson.Resolve(d, "/tracks/0/regions/2/start").Number, 1e-6);
            Assert.IsTrue(SoddCommands.Validate(P).ok);
        }

        [Test]
        public void LocalParameterAndCreateFromJson()
        {
            var r = SoddCommands.CreateEventFromJson("{\"path\":\"" + P + "\",\"description\":\"motor\",\"tracks\":[]}");
            Assert.IsTrue(r.ok, r.ToString());
            Assert.IsTrue(SoddCommands.AddParameter(P, "{\"name\":\"RPM\",\"min\":0,\"max\":8000,\"default\":800}").ok);
            Assert.IsTrue(SoddCommands.AddModulator(P, "/pitch", "{\"type\":\"ParameterCurve\",\"parameter\":\"RPM\",\"curve\":[[0,-12],[8000,12]]}").ok);
            Assert.IsFalse(SoddCommands.AddParameter(P, "{\"name\":\"RPM\"}").ok, "duplicado");
            Assert.IsTrue(SoddCommands.ListEvents("zz_tests").data.Contains(P));
            Assert.IsTrue(SoddCommands.ListParameters().data.Contains("Health"));
        }

        [Test]
        public void PreviewPlaysWithoutPlayMode()
        {
            var r = SoddCommands.PreviewEvent("sfx/impacts/hit", "{\"Health\": 50}");
            Assert.IsTrue(r.ok, r.ToString());
            Assert.IsTrue(SoddPreview.IsRunning);
            SoddPreview.ManualTick(0.05f);
            var info = new SoddEngine.InstanceInfo[4];
            Assert.AreEqual(1, SoddPreview.Engine.GetInstances(info));
            Assert.AreEqual("sfx/impacts/hit", info[0].path);
            Assert.IsTrue(SoddCommands.StopPreview().ok);
            SoddPreview.ManualTick(0.05f);
            Assert.AreEqual(0, SoddPreview.Engine.GetInstances(info));
            Assert.IsFalse(SoddCommands.PreviewEvent("sfx/impacts/hitt").ok);
        }
    }
}
