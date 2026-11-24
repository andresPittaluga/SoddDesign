using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using Sodd.Design.Json;
using UnityEngine;
using static Sodd.Design.Tests.TestUtil;

namespace Sodd.Design.Tests
{
    public class JsonTests
    {
        [Test]
        public void ParseWriteRoundtripPreservesOrder()
        {
            var n = SoddJson.Parse("{\"b\":1,\"a\":[1,2,{\"x\":\"y\\n\"}],\"c\":true,\"d\":null,\"e\":-1.5e2}");
            Assert.AreEqual("b", n.Props[0].Key);
            Assert.AreEqual(-150, n["e"].Number);
            var again = SoddJson.Parse(SoddJson.Write(n));
            Assert.IsTrue(n.DeepEquals(again));
        }

        [Test]
        public void ParseErrorReportsLine()
        {
            var ex = Assert.Throws<JsonParseException>(() => SoddJson.Parse("{\n  \"a\": 1,\n  \"b\" 2\n}"));
            Assert.AreEqual(3, ex.Line);
        }

        [Test]
        public void ToleratesCommentsAndTrailingCommas()
        {
            var n = SoddJson.Parse("// hola\n{ \"a\": [1,2,], /* x */ \"b\": 2, }");
            Assert.AreEqual(2, n["a"].Count);
        }

        [Test]
        public void PointerSetRemove()
        {
            var n = SoddJson.Parse("{\"tracks\":[{\"name\":\"A\"}]}");
            Assert.IsTrue(SoddJson.SetAt(n, "/tracks/0/volume", JsonNode.From(-3), out _));
            Assert.AreEqual(-3, SoddJson.Resolve(n, "/tracks/0/volume").Number);
            Assert.IsTrue(SoddJson.SetAt(n, "/tracks/-", JsonNode.NewObject(), out _));
            Assert.AreEqual(2, n["tracks"].Count);
            Assert.IsTrue(SoddJson.RemoveAt(n, "/tracks/0", out _));
            Assert.AreEqual(1, n["tracks"].Count);
            Assert.IsNull(SoddJson.Resolve(n, "/nope/0"));
        }

        [Test]
        public void JsonPatchOps()
        {
            var n = SoddJson.Parse("{\"a\":1,\"list\":[1,2]}");
            var patch = SoddJson.Parse("[{\"op\":\"replace\",\"path\":\"/a\",\"value\":5},{\"op\":\"add\",\"path\":\"/list/1\",\"value\":9},{\"op\":\"remove\",\"path\":\"/list/0\"},{\"op\":\"copy\",\"from\":\"/a\",\"path\":\"/b\"},{\"op\":\"test\",\"path\":\"/b\",\"value\":5}]");
            Assert.IsTrue(SoddJson.ApplyPatch(n, patch, out var err), err);
            Assert.AreEqual(5, n["a"].Number);
            Assert.AreEqual(9, n["list"][0].Number);
            Assert.AreEqual(5, n["b"].Number);
            Assert.IsFalse(SoddJson.ApplyPatch(n, SoddJson.Parse("[{\"op\":\"replace\",\"path\":\"/zz\",\"value\":1}]"), out _));
        }
    }

    public class ReaderTests
    {
        static readonly SoddParameterDef[] Known = { new SoddParameterDef { name = "Health", min = 0, max = 100, isGlobal = true } };

        [Test]
        public void ValidEventParses()
        {
            var json = Event("e1", "\"is3D\": true, \"volume\": { \"base\": -3, \"modulators\": [{ \"type\": \"Random\", \"min\": -2, \"max\": 0, \"unit\": \"dB\" }] }",
                Track(Region(0, 1)));
            var d = SoddReader.ParseEvent(json, out var r, Known);
            Assert.IsTrue(r.ok, r.ToString());
            Assert.AreEqual(-3, d.volume.@base);
            Assert.AreEqual(SoddModulatorType.Random, d.volume.modulators[0].type);
            Assert.AreEqual(1, d.tracks[0].regions[0].length);
        }

        [Test]
        public void UnknownParameterSuggestsSpanishSynonym()
        {
            var json = Event("e2", "\"pitch\": { \"base\": 0, \"modulators\": [{ \"type\": \"ParameterCurve\", \"parameter\": \"Vida\", \"curve\": [[0,0],[1,1]] }] }", Track(Region(0, 1)));
            SoddReader.ParseEvent(json, out var r, Known);
            Assert.IsFalse(r.ok);
            StringAssert.Contains("El parámetro 'Vida' no existe. ¿Quisiste decir 'Health'?", r.errors[0]);
        }

        [Test]
        public void UnknownKeyWarnsWithSuggestion()
        {
            var json = Event("e3", "\"volumen\": -3", Track(Region(0, 1)));
            SoddReader.ParseEvent(json, out var r, Known);
            Assert.IsTrue(r.ok);
            Assert.IsTrue(r.warnings.Exists(w => w.Contains("'volumen'") && w.Contains("'volume'")), r.ToString());
        }

        [Test]
        public void BadEnumAndMissingFields()
        {
            var json = "{ \"id\": \"x\", \"stealMode\": \"Oldst\", \"tracks\": [] }";
            SoddReader.ParseEvent(json, out var r, Known);
            Assert.IsTrue(r.errors.Exists(e => e.Contains("path")));
            Assert.IsTrue(r.errors.Exists(e => e.Contains("Oldst") && e.Contains("Oldest")));
        }

        [Test]
        public void EffectParamsValidated()
        {
            var json = Event("e4", "", Track(Region(0, 1), "\"effects\": [{ \"type\": \"lowpass\", \"params\": { \"cutof\": 800 } }]"));
            var d = SoddReader.ParseEvent(json, out var r, Known);
            Assert.AreEqual("LowPass", d.tracks[0].effects[0].type);
            Assert.IsTrue(r.warnings.Exists(w => w.Contains("cutoff")), r.ToString());
            Assert.AreEqual(5000, d.tracks[0].effects[0].Find("cutoff").@base);
        }

        [Test]
        public void SettingsAndParametersParse()
        {
            var s = SoddReader.ParseSettings("{\"maxVoices\":16,\"buses\":[{\"name\":\"Music\"}],\"snapshots\":[{\"name\":\"U\",\"buses\":{\"Music\":{\"lowpass\":500}}}]}", out var r1);
            Assert.IsTrue(r1.ok, r1.ToString());
            Assert.AreEqual(16, s.maxVoices);
            Assert.AreEqual("Master", s.buses[0].name);
            Assert.AreEqual(500, s.snapshots[0].buses[0].lowpass);
            var p = SoddReader.ParseParameters("{\"parameters\":[{\"name\":\"A\",\"scope\":\"global\",\"min\":0,\"max\":10,\"default\":5}]}", out var r2);
            Assert.IsTrue(r2.ok, r2.ToString());
            Assert.AreEqual(5, p.parameters[0].defaultValue);
            Assert.IsTrue(p.parameters[0].isGlobal);
        }
    }

    public class SchemaTests
    {
        const string SchemaPath = "Packages/com.sodd.sodddesign/Schema/sodd.event.schema.json";

        [Test]
        public void SchemaIsValidJsonAndEveryPropertyHasDescription()
        {
            var root = SoddJson.Parse(File.ReadAllText(SchemaPath));
            Assert.AreEqual("https://json-schema.org/draft/2020-12/schema", root["$schema"].String);
            var missing = new List<string>();
            CheckDescriptions(root, "", missing);
            Assert.IsEmpty(missing, "Sin description: " + string.Join(", ", missing));
        }

        static void CheckDescriptions(JsonNode n, string path, List<string> missing)
        {
            if (n == null) return;
            if (n.IsObject)
            {
                var props = n["properties"];
                if (props != null && props.IsObject)
                    foreach (var kv in props.Props)
                        if (kv.Value["description"] == null) missing.Add(path + "/" + kv.Key);
                foreach (var kv in n.Props) CheckDescriptions(kv.Value, path + "/" + kv.Key, missing);
            }
            else if (n.IsArray) foreach (var it in n.Items) CheckDescriptions(it, path, missing);
        }

        [Test]
        public void SchemaListsAllEffectsOfCatalog()
        {
            var root = SoddJson.Parse(File.ReadAllText(SchemaPath));
            var en = SoddJson.Resolve(root, "/$defs/effect/properties/type/enum");
            var names = new List<string>();
            foreach (var it in en.Items) names.Add(it.String);
            foreach (var fx in SoddEffectCatalog.All) CollectionAssert.Contains(names, fx.name);
        }
    }

    public class ModulatorTests
    {
        static SoddModContext Ctx(float health = 100, float t = 0, uint seed = 1) =>
            new SoddModContext { global = new[] { health }, local = new float[4], time = t, stopTime = -1, triggerSeed = seed, instanceSeed = 99 };

        static SoddModProp Prop(params SoddModulator[] mods) => new SoddModProp(0) { modulators = mods };

        [Test]
        public void RandomStaysInRangeAndVariesPerTrigger()
        {
            var p = Prop(new SoddModulator { type = SoddModulatorType.Random, min = -2, max = 3, unit = SoddUnit.Semitones });
            var seen = new HashSet<float>();
            for (uint s = 1; s < 200; s++)
            {
                var c = Ctx(seed: s);
                float v = SoddModulation.Evaluate(p, SoddPropKind.Semitones, ref c);
                Assert.That(v, Is.InRange(-2f, 3f));
                seen.Add(Mathf.Round(v * 100));
            }
            Assert.Greater(seen.Count, 50);
        }

        [Test]
        public void LinearUnitConvertsToDb()
        {
            var p = Prop(new SoddModulator { type = SoddModulatorType.Random, min = 0.5f, max = 0.5f, unit = SoddUnit.Linear });
            var c = Ctx();
            Assert.AreEqual(-6.02f, SoddModulation.Evaluate(p, SoddPropKind.GainDb, ref c), 0.05f);
        }

        [Test]
        public void ParameterCurveFollowsParameter()
        {
            var m = new SoddModulator { type = SoddModulatorType.ParameterCurve, curve = new SoddCurve(new Vector2(0, -40), new Vector2(100, 0)), paramSlot = SoddModulation.GlobalOffset };
            var p = Prop(m);
            var c = Ctx(health: 50);
            Assert.AreEqual(-20f, SoddModulation.Evaluate(p, SoddPropKind.GainDb, ref c), 1e-4f);
        }

        [Test]
        public void RandomScaledByHealthIsStableAtFullHealth()
        {
            var m = new SoddModulator { type = SoddModulatorType.RandomScaledByParameter, min = -5, max = 5, curve = new SoddCurve(new Vector2(0, 1), new Vector2(100, 0)), paramSlot = SoddModulation.GlobalOffset };
            var p = Prop(m);
            float maxAbsFull = 0, maxAbsLow = 0;
            for (uint s = 1; s < 100; s++)
            {
                var full = Ctx(100, seed: s); var low = Ctx(0, seed: s);
                maxAbsFull = Mathf.Max(maxAbsFull, Mathf.Abs(SoddModulation.Evaluate(p, SoddPropKind.Semitones, ref full)));
                maxAbsLow = Mathf.Max(maxAbsLow, Mathf.Abs(SoddModulation.Evaluate(p, SoddPropKind.Semitones, ref low)));
            }
            Assert.AreEqual(0f, maxAbsFull, 1e-5f);
            Assert.Greater(maxAbsLow, 3f);
        }

        [Test]
        public void LfoAndEnvelopes()
        {
            var lfo = Prop(new SoddModulator { type = SoddModulatorType.LFO, shape = SoddLfoShape.Sine, rate = 1, depth = 2 });
            var c = Ctx(t: 0.25f);
            Assert.AreEqual(2f, SoddModulation.Evaluate(lfo, SoddPropKind.Generic, ref c), 1e-4f);
            Assert.AreEqual(0.5f, SoddModulation.EnvelopeAHR(0.5f, 1, 0, 1), 1e-4f);
            var adsr = new SoddModulator { type = SoddModulatorType.ADSR, attack = 1, decay = 1, sustain = 0.5f, release = 2 };
            Assert.AreEqual(0.5f, SoddModulation.Adsr(5f, -1f, adsr), 1e-4f);
            Assert.AreEqual(0.25f, SoddModulation.Adsr(5f, 1f, adsr), 1e-4f);
        }

        [Test]
        public void CurveEvaluation()
        {
            var c = new SoddCurve(new Vector2(0, 0), new Vector2(1, 10), new Vector2(2, 0));
            Assert.AreEqual(5f, c.Evaluate(0.5f), 1e-4f);
            Assert.AreEqual(0f, c.Evaluate(-3f));
            Assert.AreEqual(0f, c.Evaluate(9f));
        }
    }
}
