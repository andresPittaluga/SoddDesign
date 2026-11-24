using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using static Sodd.Design.Tests.TestUtil;

namespace Sodd.Design.Tests
{
    public class EngineTests
    {
        [Test]
        public void HandleIsSafeAfterRelease()
        {
            var e = Headless();
            var a = MakeAsset(Event("h", "", Track(Region(0, 0.5f))));
            var inst = e.CreateInstance(a);
            Assert.IsTrue(inst.IsValid);
            inst.Start();
            Run(e, 0, 0.1);
            Assert.IsTrue(inst.IsPlaying);
            inst.Release();
            Run(e, 0.1, 2.0);
            Assert.IsFalse(inst.IsValid);
            inst.Stop(); inst.SetParameter("x", 1); inst.Release(); // no-ops seguros
            var again = e.CreateInstance(a);
            Assert.AreNotEqual(inst, again);
        }

        [Test]
        public void RegionsAreScheduledSampleAccurate()
        {
            var e = Headless();
            e.HeadlessClipLength = 2f;
            var a = MakeAsset(Event("s", "", Track(Region(0, 1) + "," + Region(0.5f, 1))));
            var inst = e.CreateInstance(a);
            e.Update(10.0, 0f);
            inst.StartAt(10.25);
            Run(e, 10.0, 10.8);
            var starts = new List<double>();
            for (int i = 0; i < e.VoiceCount; i++)
                if (e.GetVoice(i, out _, out double st, out double en, out _)) starts.Add(st);
            starts.Sort();
            Assert.AreEqual(2, starts.Count);
            Assert.AreEqual(10.25, starts[0], 1e-9);
            Assert.AreEqual(10.75, starts[1], 1e-9);
        }

        [Test]
        public void EventStopsWhenTimelineEnds()
        {
            var e = Headless();
            var a = MakeAsset(Event("end", "", Track(Region(0, 0.3f))));
            var inst = e.CreateInstance(a);
            bool stopped = false;
            inst.OnStopped += _ => stopped = true;
            inst.Start();
            Run(e, 0, 1.0);
            Assert.IsTrue(stopped);
            Assert.IsFalse(inst.IsPlaying);
        }

        [Test]
        public void LoopWrapsAndExitsOnCondition()
        {
            var e = Headless();
            e.HeadlessClipLength = 5f;
            var json = Event("loop", "\"parameters\": [{ \"name\": \"Go\", \"min\": 0, \"max\": 1 }], \"loopRegions\": [{ \"start\": 0, \"end\": 1, \"exitWhen\": { \"parameter\": \"Go\", \"op\": \"Greater\", \"value\": 0.5 } }], \"markers\": [{ \"name\": \"M\", \"time\": 0.5 }]",
                Track(Region(0, 1) + "," + Region(1, 0.5f)));
            var a = MakeAsset(json);
            var inst = e.CreateInstance(a);
            int markers = 0; bool exited = false, stopped = false;
            inst.OnMarker += (_, n) => { if (n == "M") markers++; };
            inst.OnLoopExit += (_, n) => exited = true;
            inst.OnStopped += _ => stopped = true;
            e.Update(0, 0);
            inst.StartAt(0.05);
            double t = Run(e, 0, 3.2);
            Assert.IsTrue(inst.IsPlaying, "debería seguir en loop");
            Assert.GreaterOrEqual(markers, 3);
            float pos = inst.TimelinePosition;
            Assert.That(pos, Is.InRange(0f, 1f));
            inst.SetParameter("Go", 1);
            t = Run(e, t, t + 2.5);
            Assert.IsTrue(exited);
            Assert.IsTrue(stopped);
        }

        [Test]
        public void VoiceStealingByPriority()
        {
            var e = Headless(voices: 2);
            e.HeadlessClipLength = 10f;
            var low = MakeAsset(Event("low", "\"priority\": 200", Track(Region(0, 10))));
            var high = MakeAsset(Event("high", "\"priority\": 10", Track(Region(0, 10))));
            var l1 = e.CreateInstance(low); l1.Start();
            var l2 = e.CreateInstance(low); l2.Start();
            Run(e, 0, 0.2);
            Assert.AreEqual(2, e.ActiveVoiceCount);
            var h = e.CreateInstance(high); h.Start();
            Run(e, 0.2, 0.4);
            Assert.AreEqual(2, e.ActiveVoiceCount);
            Assert.GreaterOrEqual(e.StatStolen, 1);
            var info = new SoddEngine.InstanceInfo[8];
            int n = e.GetInstances(info);
            bool highHasVoice = false;
            for (int i = 0; i < n; i++) if (info[i].path == "test/high" && info[i].voices == 1) highHasVoice = true;
            Assert.IsTrue(highHasVoice);
            // Un evento de menor prioridad no puede robarle a uno de mayor prioridad
            var l3 = e.CreateInstance(low); l3.Start();
            Run(e, 0.4, 0.6);
            n = e.GetInstances(info);
            for (int i = 0; i < n; i++) if (info[i].path == "test/high") Assert.AreEqual(1, info[i].voices);
        }

        [Test]
        public void MaxInstancesOldestAndNone()
        {
            var e = Headless();
            e.HeadlessClipLength = 10f;
            var a = MakeAsset(Event("mi", "\"maxInstances\": 2, \"stealMode\": \"Oldest\"", Track(Region(0, 10))));
            var i1 = e.CreateInstance(a); i1.Start(); Run(e, 0, 0.1);
            var i2 = e.CreateInstance(a); i2.Start(); Run(e, 0.1, 0.2);
            var i3 = e.CreateInstance(a); i3.Start(); Run(e, 0.2, 0.3);
            Assert.IsFalse(i1.IsPlaying);
            Assert.IsTrue(i2.IsPlaying && i3.IsPlaying);

            var b = MakeAsset(Event("mn", "\"maxInstances\": 1, \"stealMode\": \"None\"", Track(Region(0, 10))));
            var j1 = e.CreateInstance(b); j1.Start();
            var j2 = e.CreateInstance(b); j2.Start();
            Run(e, 0.3, 0.4);
            Assert.IsTrue(j1.IsPlaying);
            Assert.IsFalse(j2.IsPlaying);
        }

        [Test]
        public void CooldownBlocksSpam()
        {
            var e = Headless();
            var a = MakeAsset(Event("cd", "\"cooldown\": 0.5", Track(Region(0, 0.1f))));
            var i1 = e.CreateInstance(a); i1.Start();
            Run(e, 0, 0.1);
            var i2 = e.CreateInstance(a); i2.Start();
            Assert.IsFalse(i2.IsPlaying);
            Run(e, 0.1, 0.7);
            var i3 = e.CreateInstance(a); i3.Start();
            Assert.IsTrue(i3.IsPlaying);
        }

        [Test]
        public void RandomClipAvoidsRepeat()
        {
            var e = Headless(voices: 32, instances: 64);
            var json = Event("rc", "", "{ \"name\": \"T\", \"regions\": [{ \"start\": 0, \"length\": 0.1, \"instrument\": { \"type\": \"RandomClip\", \"avoidRepeat\": true, \"clips\": [\"a.wav\", \"b.wav\", \"c.wav\"] } }] }");
            var a = MakeAsset(json);
            int last = -1; double t = 0;
            var counts = new int[3];
            for (int k = 0; k < 60; k++)
            {
                var i = e.CreateInstance(a); i.Start(); i.Release();
                t = Run(e, t, t + 0.02);
                int pick = SoddEngine.LastPickedClip(a, 0, 0);
                Assert.AreNotEqual(last, pick);
                counts[pick]++;
                last = pick;
            }
            foreach (var c in counts) Assert.Greater(c, 5);
        }

        [Test]
        public void StopWithFadeOutAndBusVolume()
        {
            var e = Headless();
            e.HeadlessClipLength = 10f;
            var a = MakeAsset(Event("fo", "\"bus\": \"Music\"", Track(Region(0, 10))));
            var inst = e.CreateInstance(a); inst.Start();
            double t = Run(e, 0, 0.3);
            e.GetVoice(0, out _, out _, out _, out float g0);
            Assert.AreEqual(1f, g0, 1e-3f);
            e.Buses.Find("Music").Volume = -6.0206f;
            t = Run(e, t, t + 0.05);
            e.GetVoice(0, out _, out _, out _, out float g1);
            Assert.AreEqual(0.5f, g1, 1e-2f);
            inst.Stop(SoddStopMode.AllowFadeOut);
            Assert.IsTrue(inst.IsStopping);
            Run(e, t, t + 0.5);
            Assert.IsFalse(inst.IsPlaying);
        }

        [Test]
        public void GlobalParameterSeek()
        {
            var e = Headless();
            e.Parameters.Load(new[] { new SoddParameterDef { name = "I", min = 0, max = 1, seekSpeed = 2, isGlobal = true } });
            int id = e.Parameters.IndexOf("I");
            e.Parameters.Set(id, 1);
            e.Update(0, 0.25f);
            Assert.AreEqual(0.5f, e.Parameters.Get(id), 1e-4f);
            e.Update(0, 0.5f);
            Assert.AreEqual(1f, e.Parameters.Get(id), 1e-4f);
        }

        [Test]
        public void SnapshotBlendsBusVolume()
        {
            var s = SoddSettingsData.CreateDefault();
            s.snapshots = new[] { new SoddSnapshotDef { name = "Under", buses = new[] { new SoddSnapshotBus { bus = "SFX", volume = -12, lowpass = 500 } } } };
            var e = new SoddEngine(s, null);
            Assert.IsTrue(e.Snapshots.Set("Under", 1f));
            e.Update(0, 0.5f);
            var bus = e.Buses.Find("SFX");
            Assert.AreEqual(-6f, bus.snapshotDb, 0.01f);
            e.Update(0, 0.6f);
            Assert.AreEqual(500f, bus.EffectiveLowpass, 1f);
            Assert.IsFalse(e.Snapshots.Set("Nope", 0));
        }

        [Test]
        public void ZeroAllocationsInSteadyStateUpdate()
        {
            var e = Headless(voices: 16);
            e.HeadlessClipLength = 100f;
            var a = MakeAsset(Event("za", "\"volume\": { \"base\": 0, \"modulators\": [{ \"type\": \"LFO\", \"rate\": 2 }] }, \"loopRegions\": [{ \"start\": 0, \"end\": 0.5 }]", Track(Region(0, 0.5f))));
            for (int k = 0; k < 4; k++) { var i = e.CreateInstance(a); i.Start(); }
            double t = Run(e, 0, 2);
            long before = System.GC.GetAllocatedBytesForCurrentThread();
            t = Run(e, t, t + 2);
            long after = System.GC.GetAllocatedBytesForCurrentThread();
            Assert.AreEqual(0, after - before, "El Update del motor no debe allocar");
        }
    }
}
