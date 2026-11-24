using System.Linq;
using NUnit.Framework;
using Sodd.Design.Editor;
using UnityEngine;

namespace Sodd.Design.Tests
{
    public class BankTests
    {
        [Test]
        public void BankLoadRegistersAndUnloadStops()
        {
            var e = TestUtil.Headless();
            SoddAudio.SetEngineOverride(e);
            var a = TestUtil.MakeAsset(TestUtil.Event("bank_ev", "", TestUtil.Track(TestUtil.Region(0, 5))));
            var bank = ScriptableObject.CreateInstance<SoddBank>();
            bank.events = new[] { a };
            try
            {
                Assert.IsNull(SoddEvents.Find("test/bank_ev"));
                SoddBanks.Load(bank); SoddBanks.Load(bank);
                Assert.AreSame(a, SoddEvents.Find("test/bank_ev"));
                var inst = SoddAudio.PlayOneShot("test/bank_ev");
                Assert.IsTrue(inst.IsPlaying);
                SoddBanks.Unload(bank);
                Assert.IsTrue(SoddBanks.IsLoaded(bank), "refcount");
                SoddBanks.Unload(bank);
                Assert.IsFalse(SoddBanks.IsLoaded(bank));
                Assert.IsNull(SoddEvents.Find("test/bank_ev"));
                Assert.IsFalse(inst.IsPlaying, "al descargar se detienen sus instancias");
            }
            finally { SoddAudio.SetEngineOverride(null); }
        }

        [Test]
        public void ImportOptimizerClassifiesClips()
        {
            var plan = SoddImportOptimizer.Plan();
            Assert.IsNotEmpty(plan);
            var music = plan.First(c => c.path.EndsWith("music_loop_120bpm.wav"));
            Assert.AreEqual(AudioClipLoadType.Streaming, music.loadType);
            var step = plan.First(c => c.path.EndsWith("footstep_01.wav"));
            Assert.AreEqual(AudioClipLoadType.DecompressOnLoad, step.loadType);
            Assert.IsTrue(step.mono, "solo se usa en eventos 3D");
            var kick = plan.First(c => c.path.EndsWith("kick.wav"));
            Assert.IsFalse(kick.mono, "kick también se usa en la música 2D");
        }
    }
}
