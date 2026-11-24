using NUnit.Framework;
using Sodd.Design.Netcode;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace Sodd.Design.Tests
{
    public class NetcodeTests
    {
        [Test]
        public void MessageSerializationRoundTrip()
        {
            var p = new SoddNetParams().Add("Health", 25f).Add("Speed", 0.5f).Add(1234u, -2f);
            var msg = new SoddNetMessage { EventHash = 0xDEADBEEF, Position = new Vector3(1, 2, 3), Params = p };
            using (var w = new FastBufferWriter(256, Allocator.Temp))
            {
                w.WriteNetworkSerializable(msg);
                int size = w.Length;
                Assert.LessOrEqual(size, 4 + 12 + 1 + 1 + 3 * 8, "el mensaje debe ser compacto");
                using (var r = new FastBufferReader(w, Allocator.Temp))
                {
                    r.ReadNetworkSerializable(out SoddNetMessage back);
                    Assert.AreEqual(msg.EventHash, back.EventHash);
                    Assert.AreEqual(msg.Position, back.Position);
                    Assert.IsFalse(back.HasAttach);
                    Assert.AreEqual(3, back.Params.Count);
                    Assert.AreEqual(SoddText.Fnv1a("Health"), back.Params.Id0);
                    Assert.AreEqual(25f, back.Params.V0);
                    Assert.AreEqual(0.5f, back.Params.V1);
                    Assert.AreEqual(1234u, back.Params.Id2);
                    Assert.AreEqual(-2f, back.Params.V2);
                }
            }
        }

        [Test]
        public void ReceivePlaysRegisteredEventWithParams()
        {
            var e = TestUtil.Headless();
            SoddAudio.SetEngineOverride(e);
            var a = TestUtil.MakeAsset(TestUtil.Event("net", "\"networkMode\": \"Broadcast\", \"parameters\": [{ \"name\": \"Speed\", \"min\": 0, \"max\": 1 }]", TestUtil.Track(TestUtil.Region(0, 1))));
            SoddEvents.Register(a);
            try
            {
                var msg = new SoddNetMessage { EventHash = a.Hash, Position = Vector3.one, Params = new SoddNetParams().Add("Speed", 0.75f) };
                var inst = SoddNetworkAudio.Receive(msg);
                Assert.IsTrue(inst.IsPlaying);
                Assert.AreEqual(0.75f, inst.GetParameter("Speed"), 1e-5f);
                Assert.AreEqual(Vector3.one, inst.Position);
            }
            finally
            {
                SoddEvents.Unregister(a);
                SoddAudio.SetEngineOverride(null);
            }
        }

        [Test]
        public void WithoutNetworkFallsBackToLocal()
        {
            var e = TestUtil.Headless();
            SoddAudio.SetEngineOverride(e);
            var a = TestUtil.MakeAsset(TestUtil.Event("net2", "\"networkMode\": \"Broadcast\"", TestUtil.Track(TestUtil.Region(0, 1))));
            try
            {
                Assert.IsFalse(SoddAudio.Net.IsNetworked);
                var inst = SoddAudio.Net.PlayOneShot(new SoddEventReference(a), Vector3.zero);
                Assert.IsTrue(inst.IsPlaying);
            }
            finally { SoddAudio.SetEngineOverride(null); }
        }
    }
}
