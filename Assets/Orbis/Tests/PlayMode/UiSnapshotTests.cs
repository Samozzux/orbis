using System;
using System.Collections;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Orbis.Core.Device;
using Orbis.Core.Game;
using Orbis.Gameplay;
using Orbis.Platform;
using Orbis.UI.App;
using Orbis.UI.Game;
using Orbis.UI.Layout;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace Orbis.Tests.PlayMode
{
    /// <summary>
    /// Fotografie dell'interfaccia sopra il mondo (controllo visivo, non gira in CI): telefono in verticale con la
    /// Dynamic Island e desktop in orizzontale. L'interfaccia si disegna in una texture e si sovrappone alla scena.
    /// Servono il pack del mondo, la GPU e ORBIS_SNAPSHOT_DIR (ORBIS_SNAPSHOT_UI=phone|desktop per uno solo).
    /// </summary>
    [Category("Snapshot")]
    public class UiSnapshotTests
    {
        private sealed class SnapServices : PlatformServicesBase
        {
            public SnapServices(float ppp, bool touch) : base(PlatformKind.Editor)
            {
                DeviceProfile = new UnityDeviceProfile(PlatformKind.Editor, () => ppp, () => touch);
            }
        }

        [TearDown]
        public void TearDown()
        {
            AdaptiveLayout.VirtualScreen = null;
            PlatformServices.Override(null);
        }

        [UnityTest]
        [Timeout(600000)]
        public IEnumerator Interfaccia()
        {
            var dir = Environment.GetEnvironmentVariable("ORBIS_SNAPSHOT_DIR");
            if (string.IsNullOrEmpty(dir)) Assert.Ignore("ORBIS_SNAPSHOT_DIR non impostata");
            Directory.CreateDirectory(dir);
            var only = Environment.GetEnvironmentVariable("ORBIS_SNAPSHOT_UI");
            if (string.IsNullOrEmpty(only) || only == "phone")
                // iPhone 15 Pro in verticale: 1179×2556 px, 3 px/pt, Dynamic Island in alto (59 pt) e indicatore Home (34 pt)
                yield return Run(dir, "phone", 1179, 2556, 3f, true, new Rect(0, 34 * 3, 1179, 2556 - 59 * 3 - 34 * 3));
            if (string.IsNullOrEmpty(only) || only == "desktop")
                yield return Run(dir, "desktop", 1600, 900, 1f, false, new Rect(0, 0, 1600, 900));
        }

        private IEnumerator Run(string dir, string tag, int w, int h, float ppp, bool touch, Rect safe)
        {
            PlatformServices.Override(new SnapServices(ppp, touch));
            AdaptiveLayout.VirtualScreen = new Vector2Int(w, h);
            AdaptiveLayout.VirtualSafeArea = safe;
            var previous = OrbisApp.Instance;
            SceneManager.LoadScene("Boot");
            var deadline = Time.realtimeSinceStartup + 60f;
            while ((OrbisApp.Instance == null || OrbisApp.Instance == previous || OrbisApp.Instance.State != OrbisApp.AppState.MainMenu)
                   && Time.realtimeSinceStartup < deadline)
                yield return null;
            var app = OrbisApp.Instance;
            var doc = app.GetComponent<UIDocument>();
            var ui = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32);
            doc.panelSettings.targetTexture = ui;
            doc.panelSettings.clearColor = true;
            doc.panelSettings.colorClearValue = new Color(0, 0, 0, 0);
            var root = doc.rootVisualElement;
            yield return new WaitForSecondsRealtime(1.5f);
            Shot(dir, tag, "01-menu", Camera.main, ui);

            Click(root, "btn-new-game");
            yield return new WaitForSecondsRealtime(0.6f);
            Shot(dir, tag, "02-nuova-partita", Camera.main, ui);
            app.Panel.Close();
            yield return new WaitForSecondsRealtime(0.4f);

            // account (M3): schermata di accesso e scheda "Crea account"
            var ready = Time.realtimeSinceStartup + 10f;
            while (app.Account == null && Time.realtimeSinceStartup < ready) yield return null;
            Click(root, "nav-account");
            yield return new WaitForSecondsRealtime(0.6f);
            Shot(dir, tag, "02b-account", Camera.main, ui);
            app.Panel.Close();
            yield return new WaitForSecondsRealtime(0.4f);

            app.StartGame(new GameStart { Name = "Foto", Transient = true });
            var session = app.Session;
            deadline = Time.realtimeSinceStartup + 120f;
            while (session.State == WorldSession.Phase.Loading && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.That(session.State, Is.EqualTo(WorldSession.Phase.Playing), "il mondo non si è caricato (pack presente?)");
            var game = app.Game;
            var ctx = game.Session.Context;
            game.State.Cash += 5_000_000; // per costruire un po' di tutto nelle foto

            // qualche elemento attorno al centro
            var catalog = game.Content.Catalog;
            Vector3 focus = Vector3.zero;
            int firstId = 0;
            foreach (var key in new[] { "villetta_abruzzese", "casa_a_schiera", "bar", "palazzina_storica", "casa_due_piani", "panchina", "lampione" })
            {
                var item = catalog.Get(key) ?? catalog.InCategory(CatalogCategory.Residential).First();
                if (!GameplayTests.FindSpot(session, ctx, item, out var lot, out var hx, out var hy)) continue;
                if (game.Do(new PlaceCommand { Item = item.Key, Lot = lot, HalfX = hx, HalfY = hy }) != BuildResult.Ok) continue;
                if (firstId == 0)
                {
                    firstId = game.State.Objects.Keys.Last();
                    var view = session.World.Tiles.Values.First(v => v.Id.X == lot.TileX && v.Id.Y == lot.TileY);
                    var f = Placement.Footprint(Placement.FindLot(view.Data, lot.LotId), item, hx, hy, 0);
                    focus = view.Origin + new Vector3((f[0] + f[4]) / 2, 0, (f[1] + f[5]) / 2);
                }
            }
            Assert.That(firstId, Is.Not.EqualTo(0), "nessun elemento piazzato");
            game.Session.Advance(TimeSpan.FromDays(3)); // lavori finiti, qualche giorno nel bilancio
            game.Session.Advance(new DateTime(game.State.Now.Year, game.State.Now.Month, game.State.Now.Day, 11, 0, 0).AddDays(1) - game.State.Now);
            var rig = session.CameraRig;
            rig.Target = focus;
            rig.Yaw = 200;
            rig.Pitch = 38;
            rig.Distance = 160;
            rig.Snap();
            yield return Settle(session);
            Shot(dir, tag, "03-hud", rig.Cam, ui);

            Click(root, "mission-card");
            yield return new WaitForSecondsRealtime(0.4f);
            Shot(dir, tag, "04-missione", rig.Cam, ui);
            Click(root, "mission-card");

            session.SelectPlaced(firstId);
            yield return new WaitForSecondsRealtime(0.5f);
            Shot(dir, tag, "05-dettagli", rig.Cam, ui);
            session.Select(null);

            Click(root, "hud-build");
            // le anteprime 3D arrivano poche alla volta
            var until = Time.realtimeSinceStartup + 20f;
            while (game.Previews.Rendered < 12 && Time.realtimeSinceStartup < until) yield return null;
            yield return new WaitForSecondsRealtime(0.8f);
            Shot(dir, tag, "06-catalogo", rig.Cam, ui);
            app.Panel.Close();
            Click(root, "hud-finance");
            yield return new WaitForSecondsRealtime(0.6f);
            Shot(dir, tag, "07-finanze", rig.Cam, ui);
            app.Panel.Close();
            Click(root, "hud-rewind");
            yield return new WaitForSecondsRealtime(0.6f);
            Shot(dir, tag, "08-riavvolgi", rig.Cam, ui);
            app.Panel.Close();
            yield return new WaitForSecondsRealtime(0.4f);

            game.Build.AimAtPointer = false; // fantasma al centro dello schermo, come sul telefono
            game.Build.StartPlacing(catalog.Get("condominio_4") ?? catalog.InCategory(CatalogCategory.Residential).Last());
            yield return new WaitForSecondsRealtime(0.8f);
            Shot(dir, tag, "09-costruzione", rig.Cam, ui);
            game.Build.Cancel();

            app.EndGame();
            yield return new WaitForSecondsRealtime(0.5f);
            doc.panelSettings.targetTexture = null;
            ui.Release();
        }

        private static void Click(VisualElement root, string name)
        {
            var b = root.Q<Button>(name);
            Assert.That(b, Is.Not.Null, name);
            using (var e = NavigationSubmitEvent.GetPooled())
            {
                e.target = b;
                b.SendEvent(e);
            }
        }

        private static IEnumerator Settle(WorldSession session)
        {
            var until = Time.realtimeSinceStartup + 4f;
            while (Time.realtimeSinceStartup < until || (session.World.PendingBuilds > 0 && Time.realtimeSinceStartup < until + 60f))
                yield return null;
            for (int i = 0; i < 30; i++) yield return null;
        }

        /// <summary>Scena + interfaccia (colori premoltiplicati) in un PNG.</summary>
        private static void Shot(string dir, string tag, string name, Camera cam, RenderTexture ui)
        {
            int w = ui.width, h = ui.height;
            var scene = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
            var old = cam.targetTexture;
            cam.targetTexture = scene;
            cam.Render();
            cam.targetTexture = old;
            var a = Read(scene);
            var b = Read(ui);
            var pa = a.GetPixels32();
            var pb = b.GetPixels32();
            for (int i = 0; i < pa.Length; i++)
            {
                float k = 1f - pb[i].a / 255f;
                pa[i] = new Color32((byte)Mathf.Min(255, pb[i].r + pa[i].r * k), (byte)Mathf.Min(255, pb[i].g + pa[i].g * k),
                    (byte)Mathf.Min(255, pb[i].b + pa[i].b * k), 255);
            }
            a.SetPixels32(pa);
            a.Apply();
            File.WriteAllBytes(Path.Combine(dir, $"ui-{tag}-{name}.png"), a.EncodeToPNG());
            UnityEngine.Object.Destroy(a);
            UnityEngine.Object.Destroy(b);
            scene.Release();
            UnityEngine.Object.Destroy(scene);
        }

        private static Texture2D Read(RenderTexture rt)
        {
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGBA32, false);
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            return tex;
        }
    }
}
