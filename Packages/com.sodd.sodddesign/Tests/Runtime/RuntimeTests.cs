using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Sodd.Design.Tests
{
    public class RuntimeTests
    {
        [UnityTest]
        public IEnumerator ManagerAutoInitializesAndPlaysOneShot()
        {
            yield return null;
            Assert.IsNotNull(SoddAudioManager.Engine, "SoddAudioManager no se auto-inicializó");
            Assert.IsNotNull(GameObject.Find("[SoddAudio]"));
            Assert.IsNotNull(SoddEvents.Registry, "Falta Resources/SoddRegistry");
            var inst = SoddAudio.PlayOneShot("sfx/ui/click", Vector3.zero);
            Assert.IsTrue(inst.IsValid);
            Assert.IsTrue(inst.IsPlaying);
            Assert.AreEqual(1f, SoddAudio.GetBus("Master").EffectiveGain, 1e-4f);
            Assert.AreEqual(100f, SoddAudio.GetGlobalParameter("Health"));
            yield return null;
        }

        [UnityTest]
        public IEnumerator EmitterPlaysAndStopsOnDestroy()
        {
            var go = new GameObject("emitter");
            go.SetActive(false);
            var em = go.AddComponent<SoddEmitter>();
            em.eventRef = SoddEventReference.FromPath("ambience/drone");
            em.playOn = SoddPlayTrigger.Enable;
            em.allowFadeOut = false;
            go.SetActive(true);
            yield return null;
            Assert.IsTrue(em.IsPlaying);
            var inst = em.Instance;
            Object.Destroy(go);
            yield return null;
            yield return null;
            Assert.IsFalse(inst.IsPlaying);
        }
    }
}
