using System;
using System.Collections;
using System.IO;
using NUnit.Framework;
using Orbis.Gameplay;
using Orbis.UI.App;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Orbis.Tests.PlayMode
{
    /// <summary>
    /// Fotografie del mondo per il controllo visivo (non gira in CI): servono il pack in
    /// StreamingAssets/world/chieti (tools/world-pipeline/scarica-pack.sh), la GPU e la variabile
    /// ORBIS_SNAPSHOT_DIR con la cartella dove salvare i PNG.
    /// </summary>
    [Category("Snapshot")]
    public class WorldSnapshotTests
    {
        private struct Shot
        {
            public string Name;
            public Vector3 Target;
            public float Yaw, Pitch, Distance;
            public int Hour;
            public bool TopDown;
            public Core.World.Sky.WeatherKind Weather;
        }

        [UnityTest]
        [Timeout(600000)]
        public IEnumerator Fotografie()
        {
            var dir = Environment.GetEnvironmentVariable("ORBIS_SNAPSHOT_DIR");
            if (string.IsNullOrEmpty(dir)) Assert.Ignore("ORBIS_SNAPSHOT_DIR non impostata");
            Directory.CreateDirectory(dir);
            // ORBIS_SNAPSHOT_QUALITY=0 → profilo Mobile (come iPhone, Android e web)
            var q = Environment.GetEnvironmentVariable("ORBIS_SNAPSHOT_QUALITY");
            if (int.TryParse(q, out var level)) QualitySettings.SetQualityLevel(level, true);

            SceneManager.LoadScene("Boot");
            var deadline = Time.realtimeSinceStartup + 60f;
            while ((OrbisApp.Instance == null || OrbisApp.Instance.State != OrbisApp.AppState.MainMenu) && Time.realtimeSinceStartup < deadline)
                yield return null;
            OrbisApp.Instance.StartGame(new Orbis.UI.Game.GameStart { Transient = true });
            var session = OrbisApp.Instance.Session;
            deadline = Time.realtimeSinceStartup + 120f;
            while (session.State == WorldSession.Phase.Loading && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.That(session.State, Is.EqualTo(WorldSession.Phase.Playing), "il mondo non si è caricato (pack presente?)");

            var shots = new[]
            {
                new Shot { Name = "01-centro", Target = new Vector3(0, 0, 0), Yaw = 200, Pitch = 38, Distance = 520, Hour = 11 },
                new Shot { Name = "02-panoramica", Target = new Vector3(0, 0, 0), Yaw = 30, Pitch = 30, Distance = 2600, Hour = 16 },
                new Shot { Name = "03-vicino", Target = new Vector3(60, 0, -40), Yaw = 140, Pitch = 30, Distance = 160, Hour = 10 },
                new Shot { Name = "04-mappa2d", Target = new Vector3(0, 0, 0), Yaw = 0, Pitch = 89, Distance = 700, Hour = 12, TopDown = true },
                new Shot { Name = "05-tramonto", Target = new Vector3(0, 0, 0), Yaw = 250, Pitch = 22, Distance = 1400, Hour = 19 },
                new Shot { Name = "06-notte", Target = new Vector3(0, 0, 0), Yaw = 200, Pitch = 35, Distance = 600, Hour = 22 },
                new Shot { Name = "07-campagna", Target = new Vector3(-3500, 0, -2500), Yaw = 60, Pitch = 32, Distance = 900, Hour = 15 },
                new Shot { Name = "08-pioggia", Target = new Vector3(0, 0, 0), Yaw = 120, Pitch = 30, Distance = 450, Hour = 12, Weather = Core.World.Sky.WeatherKind.Rain },
                new Shot { Name = "09-costa", Target = new Vector3(14000, 0, 4000), Yaw = 70, Pitch = 25, Distance = 4000, Hour = 9 },
            };
            var cam = session.CameraRig.Cam;
            var list = new System.Collections.Generic.List<Shot>(shots);
            var only = Environment.GetEnvironmentVariable("ORBIS_SNAPSHOT_ONLY");
            if (!string.IsNullOrEmpty(only)) list.RemoveAll(x => !x.Name.Contains(only));
            for (int n = 0; n < list.Count; n++)
            {
                var s = list[n];
                var rig = session.CameraRig;
                rig.Target = s.Target;
                rig.Yaw = s.Yaw;
                rig.Pitch = s.Pitch;
                rig.Distance = s.Distance;
                rig.SetTopDown(s.TopDown);
                var now = session.Clock.Now;
                session.Clock.Advance(new DateTime(now.Year, now.Month, now.Day, s.Hour, 0, 0) - now);
                session.Sky.SetWeather(Core.World.Sky.WeatherState.For(s.Weather));
                // tempo per volo della telecamera, streaming dei tile e meteo
                var until = Time.realtimeSinceStartup + 4f;
                while (Time.realtimeSinceStartup < until || (session.World.PendingBuilds > 0 && Time.realtimeSinceStartup < until + 60f))
                    yield return null;
                for (int i = 0; i < 60; i++) yield return null;
                Save(cam, Path.Combine(dir, s.Name + ".png"));
                Debug.Log($"[Orbis] foto {s.Name}: {session.World.Tiles.Count} tile, {session.World.LoadedBuildings} edifici, {session.World.Vegetation.TotalInstances} piante");
                if (s.Name == "02-panoramica")
                {
                    // dopo la panoramica i tile attorno a Chieti sono caricati: si cercano i landmark
                    foreach (var view in session.World.Tiles.Values)
                    {
                        if (view.Data == null) continue;
                        foreach (var b in view.Data.Buildings)
                            if (!string.IsNullOrEmpty(b.Landmark) && !list.Exists(x => x.Name == "10-" + b.Landmark))
                                list.Add(new Shot { Name = "10-" + b.Landmark, Target = view.Origin + new Vector3((b.Bounds.MinX + b.Bounds.MaxX) / 2, 0, (b.Bounds.MinY + b.Bounds.MaxY) / 2), Yaw = 160, Pitch = 32, Distance = 140, Hour = 11 });
                        foreach (var a in view.Data.Areas)
                            if (!string.IsNullOrEmpty(a.Landmark) && !list.Exists(x => x.Name == "10-" + a.Landmark))
                                list.Add(new Shot { Name = "10-" + a.Landmark, Target = view.Origin + new Vector3((a.Bounds.MinX + a.Bounds.MaxX) / 2, 0, (a.Bounds.MinY + a.Bounds.MaxY) / 2), Yaw = 30, Pitch = 35, Distance = 260, Hour = 15 });
                    }
                }
            }
        }

        [UnityTest]
        [Timeout(300000)]
        public IEnumerator Globo()
        {
            var dir = Environment.GetEnvironmentVariable("ORBIS_SNAPSHOT_DIR");
            if (string.IsNullOrEmpty(dir)) Assert.Ignore("ORBIS_SNAPSHOT_DIR non impostata");
            SceneManager.LoadScene("Boot");
            var deadline = Time.realtimeSinceStartup + 60f;
            while ((OrbisApp.Instance == null || OrbisApp.Instance.State != OrbisApp.AppState.MainMenu) && Time.realtimeSinceStartup < deadline)
                yield return null;
            OrbisApp.Instance.StartGame(new Orbis.UI.Game.GameStart { Transient = true });
            var session = OrbisApp.Instance.Session;
            while (session.State == WorldSession.Phase.Loading) yield return null;
            for (int i = 0; i < 120; i++) yield return null; // texture del globo
            session.Globe.Enter(42.35, 14.17, 9000f);
            for (int i = 0; i < 90; i++) yield return null;
            Save(session.CameraRig.Cam, Path.Combine(dir, "20-globo.png"));
            session.Globe.Enter(42.35, 14.17, 900f);
            for (int i = 0; i < 90; i++) yield return null;
            Save(session.CameraRig.Cam, Path.Combine(dir, "21-globo-italia.png"));
            session.Globe.Exit();
            for (int i = 0; i < 30; i++) yield return null;
            Assert.That(session.Globe.Active, Is.False);
            Assert.That(session.World.Visible, Is.True);
        }

        /// <summary>Addobbi "da gioco" di un bar vero: livello 1, livello 5, livello 5 di sera, e una via del centro.</summary>
        [UnityTest]
        [Timeout(600000)]
        public IEnumerator Addobbi()
        {
            var dir = Environment.GetEnvironmentVariable("ORBIS_SNAPSHOT_DIR");
            if (string.IsNullOrEmpty(dir)) Assert.Ignore("ORBIS_SNAPSHOT_DIR non impostata");
            Directory.CreateDirectory(dir);
            SceneManager.LoadScene("Boot");
            var deadline = Time.realtimeSinceStartup + 60f;
            while ((OrbisApp.Instance == null || OrbisApp.Instance.State != OrbisApp.AppState.MainMenu) && Time.realtimeSinceStartup < deadline)
                yield return null;
            OrbisApp.Instance.StartGame(new Orbis.UI.Game.GameStart { Transient = true });
            var session = OrbisApp.Instance.Session;
            deadline = Time.realtimeSinceStartup + 120f;
            while (session.State == WorldSession.Phase.Loading && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.That(session.State, Is.EqualTo(WorldSession.Phase.Playing));
            while (session.World.PendingBuilds > 0) yield return null;
            var game = OrbisApp.Instance.Game;
            game.State.Cash += 50_000_000;

            // un bar (o caffè, ristorante) dentro un edificio del centro, il più vicino all'origine
            Orbis.World.TileView tile = null;
            Core.World.Tiles.PoiInfo bar = null;
            float best = float.MaxValue;
            foreach (var v in session.World.Tiles.Values)
            {
                if (v.Lod != Orbis.World.TileLod.Full) continue;
                foreach (var p in v.Data.Pois)
                {
                    if (p.Building < 0 || (p.Category != "amenity/bar" && p.Category != "amenity/cafe" && p.Category != "amenity/restaurant")) continue;
                    var w = v.Origin + new Vector3(p.X, 0, p.Y);
                    float d = w.sqrMagnitude;
                    if (d < best)
                    {
                        best = d;
                        bar = p;
                        tile = v;
                    }
                }
            }
            Assert.That(bar, Is.Not.Null, "nessun bar nei tile vicini");
            var b = tile.Data.Buildings[bar.Building];
            // facciata d'ingresso: il lato più vicino al POI; la telecamera la guarda dalla strada
            var ring = Core.World.Geometry.Polygon2D.Clean(b.Outer, true);
            var poi = new Core.World.Geometry.Vec2(bar.X, bar.Y);
            Core.World.Geometry.Vec2 facade = poi, normal = new Core.World.Geometry.Vec2(0, 1);
            float bestEdge = float.MaxValue;
            for (int i = 0; i < ring.Count; i++)
            {
                var a = ring[i];
                var c = ring[(i + 1) % ring.Count];
                var ab = c - a;
                float len = ab.Length;
                if (len < 2f) continue;
                float t = Mathf.Clamp01(Core.World.Geometry.Vec2.Dot(poi - a, ab) / (len * len));
                var q = a + ab * t;
                float d = (poi - q).Length;
                if (d >= bestEdge) continue;
                bestEdge = d;
                facade = q;
                normal = new Core.World.Geometry.Vec2(ab.Y / len, -ab.X / len);
            }
            var target = tile.Origin + new Vector3(facade.X + normal.X * 2f, 0, facade.Y + normal.Y * 2f);
            target.y = session.World.HeightAt(target.x, target.z) + 2.5f;
            var rig = session.CameraRig;
            rig.Target = target;
            rig.Yaw = Mathf.Atan2(-normal.X, -normal.Y) * Mathf.Rad2Deg;
            rig.Pitch = 58;
            rig.Distance = 34;
            rig.Snap();
            Debug.Log($"[Orbis] foto addobbi: {bar.Name} ({bar.Category}), edificio {b.OsmId}");
            int deco = 0, build = 0, full = 0;
            foreach (var v in session.World.Tiles.Values)
            {
                if (v.Lod != Orbis.World.TileLod.Full) continue;
                full++;
                deco += v.DecorationTriangles;
                build += v.BuildingTriangles;
            }
            Debug.Log($"[Orbis] addobbi: {full} tile vicini, {build} triangoli di edifici, {deco} triangoli di addobbi");

            yield return At(session, 11);
            Save(rig.Cam, Path.Combine(dir, "30-bar-livello1.png"));
            long key = Orbis.Core.Game.GameState.OsmKey(b.OsmType, b.OsmId);
            for (int i = 0; i < 4; i++)
                Assert.That(game.Do(new Orbis.Core.Game.UpgradeOsmCommand { OsmKey = key, Lot = new Orbis.Core.Game.LotKey(tile.Id.X, tile.Id.Y, 0), FloorArea = 300 }),
                    Is.EqualTo(Orbis.Core.Game.BuildResult.Ok));
            for (int i = 0; i < 5; i++) yield return null;
            while (session.World.PendingBuilds > 0) yield return null;
            yield return At(session, 11);
            Save(rig.Cam, Path.Combine(dir, "31-bar-livello5.png"));
            yield return At(session, 21);
            Save(rig.Cam, Path.Combine(dir, "32-bar-livello5-sera.png"));
            rig.Distance = 160;
            rig.Pitch = 38;
            rig.Snap();
            yield return At(session, 11);
            Save(rig.Cam, Path.Combine(dir, "33-via-del-centro.png"));
            OrbisApp.Instance.EndGame();
        }

        private static IEnumerator At(WorldSession session, int hour)
        {
            var now = session.Game != null ? session.Game.State.Now : session.Clock.Now;
            var t = new DateTime(now.Year, now.Month, now.Day, hour, 0, 0);
            if (t < now) t = t.AddDays(1);
            if (session.Game != null) session.Game.Session.Advance(t - now);
            else session.Clock.Advance(t - now);
            for (int i = 0; i < 40; i++) yield return null;
            while (session.World.PendingBuilds > 0) yield return null;
            for (int i = 0; i < 10; i++) yield return null;
        }

        private static void Save(Camera cam, string path)
        {
            var rt = new RenderTexture(1600, 900, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
            var old = cam.targetTexture;
            cam.targetTexture = rt;
            cam.Render();
            cam.targetTexture = old;
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            File.WriteAllBytes(path, tex.EncodeToPNG());
            UnityEngine.Object.Destroy(tex);
            rt.Release();
            UnityEngine.Object.Destroy(rt);
        }
    }
}
