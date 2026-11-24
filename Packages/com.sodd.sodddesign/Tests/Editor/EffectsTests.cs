using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Audio;

namespace Sodd.Design.Tests
{
    public class EffectsTests
    {
        GameObject _go;

        [TearDown]
        public void TearDown() { if (_go != null) Object.DestroyImmediate(_go); }

        T Make<T>() where T : SoddDspFilter
        {
            _go = new GameObject("dsp");
            return _go.AddComponent<T>();
        }

        static float[] Sine(int frames, int ch, float amp, float freq = 440f, int sr = 48000)
        {
            var d = new float[frames * ch];
            for (int i = 0; i < frames; i++) for (int c = 0; c < ch; c++) d[i * ch + c] = amp * Mathf.Sin(2 * Mathf.PI * freq * i / sr);
            return d;
        }

        static float Peak(float[] d, int from = 0) { float p = 0; for (int i = from; i < d.Length; i++) p = Mathf.Max(p, Mathf.Abs(d[i])); return p; }

        [Test]
        public void BitcrusherQuantizes()
        {
            var f = Make<SoddBitcrusher>();
            f.SetParam(0, 2); f.SetParam(1, 1); f.SetParam(2, 1);
            var d = Sine(1024, 2, 0.9f);
            f.Render(d, 2);
            var distinct = new System.Collections.Generic.HashSet<float>(d);
            Assert.LessOrEqual(distinct.Count, 5);
        }

        [Test]
        public void CompressorReducesLoudSignal()
        {
            var f = Make<SoddSimpleCompressor>();
            f.SetParam(0, -20); f.SetParam(1, 10); f.SetParam(2, 0.1f); f.SetParam(3, 50); f.SetParam(4, 0);
            var d = Sine(48000, 1, 1f);
            f.Render(d, 1);
            Assert.Less(Peak(d, 24000), 0.35f);
            Assert.Greater(f.GainReductionDb, 10f);
        }

        [Test]
        public void DelayProducesEcho()
        {
            var f = Make<SoddSimpleDelay>();
            f.SetParam(0, 0.01f); f.SetParam(1, 0); f.SetParam(2, 1);
            var d = new float[48000 * 2];
            d[0] = 1f; d[1] = 1f;
            f.Render(d, 2);
            int sr = AudioSettings.outputSampleRate > 0 ? AudioSettings.outputSampleRate : 48000;
            int idx = (int)(0.01f * sr) * 2;
            Assert.AreEqual(1f, d[idx], 1e-4f, "eco a 10 ms");
        }

        [Test]
        public void SaturatorStaysBoundedAndTremoloModulates()
        {
            var s = Make<SoddSaturator>();
            s.SetParam(0, 20); s.SetParam(1, 1); s.SetParam(2, 0);
            var d = Sine(4096, 2, 3f);
            s.Render(d, 2);
            Assert.LessOrEqual(Peak(d), 1.0001f);
            Object.DestroyImmediate(_go);

            var t = Make<SoddTremolo>();
            t.SetParam(0, 10); t.SetParam(1, 1);
            var c = new float[48000];
            for (int i = 0; i < c.Length; i++) c[i] = 1f;
            t.Render(c, 1);
            float min = 1; foreach (var v in c) min = Mathf.Min(min, v);
            Assert.Less(min, 0.05f);
        }

        [Test]
        public void MixerHasGroupsAndExposedParams()
        {
            var mixer = Resources.Load<AudioMixer>("SoddMixer");
            Assert.IsNotNull(mixer, "Falta Runtime/Resources/SoddMixer.mixer");
            foreach (var g in new[] { "Master", "Music", "SFX", "UI", "Voice", "Ambience", "ReverbSend" })
                Assert.IsNotEmpty(mixer.FindMatchingGroups(g), g);
            Assert.IsTrue(mixer.GetFloat(SoddReverbZone.ReverbReturnParam, out _));
            Assert.IsTrue(mixer.GetFloat("Music_Volume", out _));
        }

        [Test]
        public void VoiceEffectsFollowTrackAndSnapshot()
        {
            _go = new GameObject("engine");
            var settings = SoddSettingsData.CreateDefault();
            settings.maxVoices = 4;
            settings.snapshots = new[] { new SoddSnapshotDef { name = "Under", buses = new[] { new SoddSnapshotBus { bus = "Ambience", lowpass = 400 } } } };
            var e = new SoddEngine(settings, _go.transform);
            var clip = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Sandbox/Audio/drone_110.wav");
            Assert.IsNotNull(clip);

            var json = TestUtil.Event("fx", "\"bus\": \"Ambience\"", TestUtil.Track(TestUtil.Region(0, 4), "\"effects\": [{ \"type\": \"Bitcrusher\", \"params\": { \"bits\": 4 } }]"));
            var data = SoddReader.ParseEvent(json, out var r);
            Assert.IsTrue(r.ok, r.ToString());
            data.tracks[0].regions[0].instrument.clips[0].clipIndex = 0;
            var a = ScriptableObject.CreateInstance<SoddEventAsset>();
            a.SetData(data, new[] { clip });

            var inst = e.CreateInstance(a);
            inst.Start();
            e.Update(AudioSettings.dspTime, 0.016f);
            Assert.AreEqual(1, e.ActiveVoiceCount);
            Assert.IsTrue(_go.GetComponentInChildren<SoddBitcrusher>().enabled, "el efecto del track se habilita en la voz");
            Assert.IsFalse(_go.GetComponentInChildren<AudioLowPassFilter>().enabled);
            e.Snapshots.Set("Under", 0f);
            e.Update(AudioSettings.dspTime, 0.016f);
            var lp = _go.GetComponentInChildren<AudioLowPassFilter>();
            Assert.IsTrue(lp.enabled, "el snapshot activa el lowpass del bus");
            Assert.AreEqual(400f, lp.cutoffFrequency, 1f);
            inst.Stop(SoddStopMode.Immediate);
            Assert.IsFalse(_go.GetComponentInChildren<SoddBitcrusher>().enabled, "al liberar la voz se apagan los efectos");
            e.Dispose();
        }

        [Test]
        public void ReverbZoneWeight()
        {
            _go = new GameObject("zone");
            var z = _go.AddComponent<SoddReverbZone>();
            z.minDistance = 2; z.maxDistance = 6;
            Assert.AreEqual(1f, z.Weight(Vector3.one), 1e-4f);
            Assert.AreEqual(0.5f, z.Weight(new Vector3(4, 0, 0)), 1e-4f);
            Assert.AreEqual(0f, z.Weight(new Vector3(10, 0, 0)), 1e-4f);
        }

        [Test]
        public void ThreeEQAtZeroDbPassesTheSignalUnchanged()
        {
            var f = Make<SoddThreeEQ>();
            var d = Sine(4800, 2, 0.5f, 1000f);
            var original = (float[])d.Clone();
            f.Render(d, 2);
            float maxDiff = 0;
            for (int i = 0; i < d.Length; i++) maxDiff = Mathf.Max(maxDiff, Mathf.Abs(d[i] - original[i]));
            Assert.Less(maxDiff, 1e-4f);
        }

        [Test]
        public void ThreeEQCutsOnlyTheHighBand()
        {
            var high = Make<SoddThreeEQ>();
            high.SetParam(2, -20.5f);
            var hiTone = Sine(9600, 1, 0.5f, 10000f);
            high.Render(hiTone, 1);
            Assert.Less(Peak(hiTone, 4800), 0.5f * 0.2f, "10 kHz should drop about 20 dB");
            Object.DestroyImmediate(_go);

            var mid = Make<SoddThreeEQ>();
            mid.SetParam(2, -20.5f);
            var midTone = Sine(9600, 1, 0.5f, 1000f);
            mid.Render(midTone, 1);
            Assert.Greater(Peak(midTone, 4800), 0.5f * 0.8f, "1 kHz stays in the mid band");
        }

        [Test]
        public void TremoloLfoFollowsFmodShapeControls()
        {
            Assert.AreEqual(1f, SoddTremolo.Lfo(0.25f, 1f, 0f, 0.5f, 0f), 1e-3f, "sine peak at a quarter cycle");
            Assert.AreEqual(-1f, SoddTremolo.Lfo(0.75f, 1f, 0f, 0.5f, 0f), 1e-3f, "sine trough at three quarters");
            Assert.AreEqual(0.5f, SoddTremolo.Lfo(0.125f, 0f, 0f, 0.5f, 0f), 1e-3f, "triangle is linear");
            Assert.AreEqual(1f, SoddTremolo.Lfo(0.05f, 1f, 0f, 0.5f, 1f), 1e-3f, "square flattens to full level");
            Assert.Greater(SoddTremolo.Lfo(0.8f, 1f, 0f, 0.9f, 0f), 0f, "duty 0.9 keeps 90% of the cycle in the loud half");
        }
    }
}
