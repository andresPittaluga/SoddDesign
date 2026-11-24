using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using NUnit.Framework;
using Sodd.Design.Editor;
using Sodd.Design.Json;
using UnityEngine.TestTools;

namespace Sodd.Design.Tests
{
    public class BridgeTests
    {
        [Test]
        public void DispatchListsAndValidates()
        {
            var r = SoddJson.Parse(SoddBridge.Dispatch("{\"jsonrpc\":\"2.0\",\"id\":7,\"method\":\"list_events\",\"params\":{\"filter\":\"player\"}}"));
            Assert.AreEqual(7, r["id"].AsInt());
            Assert.IsTrue(r["result"]["ok"].Bool);
            Assert.GreaterOrEqual(r["result"]["data"].Count, 2);
            var bad = SoddJson.Parse(SoddBridge.Dispatch("{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"list_event\"}"));
            StringAssert.Contains("list_events", bad["error"]["message"].String);
            var clips = SoddJson.Parse(SoddBridge.Dispatch("{\"jsonrpc\":\"2.0\",\"id\":2,\"method\":\"list_audio_clips\",\"params\":{\"filter\":\"kick\"}}"));
            Assert.AreEqual("Assets/Sandbox/Audio/kick.wav", clips["result"]["data"][0]["path"].String);
            var schema = SoddJson.Parse(SoddBridge.Dispatch("{\"jsonrpc\":\"2.0\",\"id\":3,\"method\":\"get_schema\"}"));
            Assert.AreEqual("SoddDesign Event", schema["result"]["data"]["title"].String);
            var guide = SoddJson.Parse(SoddBridge.Dispatch("{\"jsonrpc\":\"2.0\",\"id\":4,\"method\":\"get_ai_guide\"}"));
            Assert.IsTrue(guide["result"]["ok"].Bool, "AI_GUIDE.md debe existir");
        }

        [Test]
        public void HttpSecurityChecks()
        {
            var h = new Dictionary<string, string> { { "Host", "127.0.0.1:47821" }, { "Authorization", "Bearer abc" } };
            string Echo(string b) => b;
            Assert.AreEqual(200, SoddBridge.Process("POST", h, "{}", Echo, "abc").status);
            Assert.AreEqual(401, SoddBridge.Process("POST", h, "{}", Echo, "xyz").status);
            Assert.AreEqual(405, SoddBridge.Process("GET", h, "{}", Echo, "abc").status);
            var evil = new Dictionary<string, string>(h) { ["Host"] = "evil.com" };
            Assert.AreEqual(403, SoddBridge.Process("POST", evil, "{}", Echo, "abc").status);
            var browser = new Dictionary<string, string>(h) { ["Origin"] = "http://evil.com" };
            Assert.AreEqual(403, SoddBridge.Process("POST", browser, "{}", Echo, "abc").status);
        }

        [UnityTest]
        public IEnumerator RealSocketRoundTrip()
        {
            SoddBridge.Start(0);
            Assert.IsTrue(SoddBridge.IsRunning, "el bridge no arrancó");
            var cfg = SoddJson.Parse(File.ReadAllText(SoddBridge.ConfigPath));
            Assert.AreEqual("127.0.0.1", cfg["host"].String);
            int port = cfg["port"].AsInt();
            string token = cfg["token"].String;
            Task<string> Post(string body, string tok)
            {
                return Task.Run(async () =>
                {
                    using (var http = new HttpClient())
                    {
                        var req = new HttpRequestMessage(HttpMethod.Post, $"http://127.0.0.1:{port}/") { Content = new StringContent(body, Encoding.UTF8, "application/json") };
                        req.Headers.TryAddWithoutValidation("Authorization", "Bearer " + tok);
                        var res = await http.SendAsync(req);
                        return (int)res.StatusCode + " " + await res.Content.ReadAsStringAsync();
                    }
                });
            }
            var ok = Post("{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"ping\"}", token);
            var denied = Post("{\"jsonrpc\":\"2.0\",\"id\":2,\"method\":\"ping\"}", "wrong");
            for (int i = 0; i < 600 && !(ok.IsCompleted && denied.IsCompleted); i++) yield return null;
            Assert.IsTrue(ok.IsCompleted, "sin respuesta del bridge");
            StringAssert.StartsWith("200 ", ok.Result);
            StringAssert.Contains("\"pong\":true", ok.Result);
            StringAssert.StartsWith("401 ", denied.Result);
            SoddBridge.Stop();
            Assert.IsFalse(File.Exists(SoddBridge.ConfigPath));
        }
    }
}
