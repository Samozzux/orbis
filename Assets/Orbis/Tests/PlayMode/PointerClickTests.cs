using System.Collections;
using NUnit.Framework;
using Orbis.Core.Device;
using Orbis.Platform;
using Orbis.UI.App;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace Orbis.Tests.PlayMode
{
    /// <summary>
    /// Clic veri col mouse attraverso Input System + EventSystem (la strada del web e del desktop):
    /// i test di avvio usano solo eventi di navigazione, questo controlla il puntatore.
    /// Va eseguito senza GPU (-nographics, come in CI): con la GPU del Mac la libreria di test
    /// dell'Input System va in conflitto con mouse e tastiera reali.
    /// </summary>
    public class PointerClickTests : InputTestFixture
    {
        private Mouse _mouse;

        /// <summary>Desktop con 1 punto = 1 pixel: nel batchmode lo schermo è 640×480 e con la densità del Mac il pannello sarebbe minuscolo.</summary>
        private sealed class DesktopServices : PlatformServicesBase
        {
            public DesktopServices() : base(PlatformKind.Desktop)
            {
                DeviceProfile = new UnityDeviceProfile(PlatformKind.Desktop, () => 1f, () => false);
            }
        }

        public override void Setup()
        {
            base.Setup();
            _mouse = InputSystem.AddDevice<Mouse>();
            PlatformServices.Override(new DesktopServices());
        }

        public override void TearDown()
        {
            PlatformServices.Override(null);
            base.TearDown();
        }

        [UnityTest]
        public IEnumerator ClicMouse_SuImpostazioni_ApreIlPannello()
        {
            yield return LoadMenu();
            var root = OrbisApp.Instance.GetComponent<UIDocument>().rootVisualElement;
            yield return Click(root.Q<Button>("nav-settings"));
            Assert.That(OrbisApp.Instance.Settings.IsOpen, Is.True, "Il clic col mouse su Impostazioni non ha aperto il pannello");
        }

        [UnityTest]
        public IEnumerator ClicMouse_SuCrediti_MostraLAvviso()
        {
            yield return LoadMenu();
            var root = OrbisApp.Instance.GetComponent<UIDocument>().rootVisualElement;
            var button = root.Q<Button>("nav-credits");
            var picked = button.panel.Pick(button.worldBound.center);
            yield return Click(button);
            Assert.That(OrbisApp.Instance.MainMenu.IsToastVisible, Is.True,
                $"Il clic col mouse su Crediti non ha mostrato l'avviso (sotto il puntatore: {Describe(picked)})");
        }

        [UnityTest]
        public IEnumerator ClicMouse_SuAccount_ApreLAccesso()
        {
            yield return LoadMenu();
            var deadline = Time.realtimeSinceStartup + 10f;
            while (OrbisApp.Instance.Account == null && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.That(OrbisApp.Instance.Account, Is.Not.Null, "Account non pronto");
            var root = OrbisApp.Instance.GetComponent<UIDocument>().rootVisualElement;
            yield return Click(root.Q<Button>("nav-account"));
            Assert.That(OrbisApp.Instance.Panel.IsOpen, Is.True, "Il clic su Account non ha aperto il pannello");
            Assert.That(root.Q<TextField>("account-username"), Is.Not.Null, "Manca il campo del nome utente");
            Assert.That(root.Q<Button>("account-login"), Is.Not.Null, "Manca il pulsante Entra");
        }

        [UnityTest]
        public IEnumerator ClicMouse_SuNuovaPartita_ApreIlMondo()
        {
            yield return LoadMenu();
            var root = OrbisApp.Instance.GetComponent<UIDocument>().rootVisualElement;
            yield return Click(root.Q<Button>("btn-new-game"));
            Assert.That(OrbisApp.Instance.Panel.IsOpen, Is.True, "Il clic su Nuova partita non ha aperto il pannello");
            yield return new WaitForSecondsRealtime(0.6f); // animazione d'entrata del pannello
            yield return Click(root.Q<Button>("newgame-start"));
            Assert.That(OrbisApp.Instance.State, Is.EqualTo(OrbisApp.AppState.Game), "Il clic su Inizia non ha aperto il mondo");
            // il pannello che si chiude sfuma in 0,25 s e fino ad allora copre lo schermo
            yield return new WaitForSecondsRealtime(0.5f);
            // il pulsante Casa (mouse) riporta al menu
            yield return Click(root.Q<Button>("hud-home"));
            Assert.That(OrbisApp.Instance.State, Is.EqualTo(OrbisApp.AppState.MainMenu), "Il clic su Casa non è tornato al menu");
        }

        [UnityTest]
        public IEnumerator ClicMouse_SuUnEdificio_ApreIDettagli()
        {
            yield return LoadMenu();
            OrbisApp.Instance.StartGame();
            var session = OrbisApp.Instance.Session;
            var deadline = Time.realtimeSinceStartup + 90f;
            while (session.State == Orbis.Gameplay.WorldSession.Phase.Loading && Time.realtimeSinceStartup < deadline) yield return null;
            if (session.State != Orbis.Gameplay.WorldSession.Phase.Playing) Assert.Ignore("pack del mondo assente (normale in CI)");
            // un edificio del tile al centro, visto dall'alto
            Orbis.World.TileView tile = null;
            foreach (var v in session.World.Tiles.Values)
                if (v.Lod == Orbis.World.TileLod.Full && v.Data.Buildings.Length > 50) { tile = v; break; }
            Assert.That(tile, Is.Not.Null);
            Orbis.Core.World.Tiles.BuildingInfo building = null;
            foreach (var b in tile.Data.Buildings)
                if (b.Bounds.Width > 15 && b.Bounds.Height > 15 && b.Height > 6) { building = b; break; }
            Assert.That(building, Is.Not.Null);
            var center = tile.Origin + new Vector3((building.Bounds.MinX + building.Bounds.MaxX) / 2, 0, (building.Bounds.MinY + building.Bounds.MaxY) / 2);
            var rig = session.CameraRig;
            rig.Target = center;
            rig.Pitch = 89f;
            rig.Distance = 250f;
            rig.Snap();
            for (var i = 0; i < 30; i++) yield return null;
            var ground = session.World.HeightAt(center.x, center.z);
            var screen = (Vector2)rig.Cam.WorldToScreenPoint(new Vector3(center.x, ground + building.Height * 0.5f, center.z));
            Move(_mouse.position, screen);
            yield return null;
            Press(_mouse.leftButton);
            for (var i = 0; i < 3; i++) yield return null;
            Release(_mouse.leftButton);
            for (var i = 0; i < 5; i++) yield return null;
            Assert.That(session.Selection, Is.Not.Null, "il clic sull'edificio non lo ha selezionato");
            var details = OrbisApp.Instance.GetComponent<UIDocument>().rootVisualElement.Q("details");
            Assert.That(details.ClassListContains("details--visible"), Is.True, "il pannello Dettagli non si è aperto");
            OrbisApp.Instance.EndGame();
        }

        private static string Describe(VisualElement e)
        {
            var parts = new System.Collections.Generic.List<string>();
            for (var x = e; x != null && parts.Count < 4; x = x.parent)
                parts.Add($"{x.GetType().Name}#{x.name}.{string.Join(".", x.GetClasses())}[{x.pickingMode}]");
            return e == null ? "niente" : string.Join(" < ", parts);
        }

        private static IEnumerator LoadMenu()
        {
            var previous = OrbisApp.Instance;
            // InputTestFixture sostituisce lo stato dell'Input System, ma l'asset di azioni salvato nella scena
            // conserva lo stato dei test precedenti: all'OnEnable il modulo UI può lanciare un'eccezione
            // dell'Input System (solo nei test, mai nel gioco). Qui la si ignora e sotto si danno al modulo
            // azioni nuove; gli errori veri dell'avvio li controlla BootFlowTests.
            LogAssert.ignoreFailingMessages = true;
            SceneManager.LoadScene("Boot");
            var deadline = Time.realtimeSinceStartup + 30f;
            while ((OrbisApp.Instance == null || OrbisApp.Instance == previous || OrbisApp.Instance.State != OrbisApp.AppState.MainMenu)
                   && Time.realtimeSinceStartup < deadline)
                yield return null;
            Assert.That(OrbisApp.Instance.State, Is.EqualTo(OrbisApp.AppState.MainMenu));
            // il modulo UI tiene in una variabile statica le azioni predefinite create dal test precedente
            // (con un altro stato dell'Input System): qui gliene si danno di nuove, legate al mouse del test
            var module = Object.FindAnyObjectByType<InputSystemUIInputModule>();
            var actions = new DefaultInputActions();
            module.actionsAsset = actions.asset;
            module.point = InputActionReference.Create(actions.UI.Point);
            module.leftClick = InputActionReference.Create(actions.UI.Click);
            module.move = InputActionReference.Create(actions.UI.Navigate);
            module.submit = InputActionReference.Create(actions.UI.Submit);
            module.cancel = InputActionReference.Create(actions.UI.Cancel);
            module.scrollWheel = InputActionReference.Create(actions.UI.ScrollWheel);
            yield return null;
            LogAssert.ignoreFailingMessages = false;
            // le animazioni di entrata del menu (translate) devono finire prima di cliccare
            yield return new WaitForSecondsRealtime(1.2f);
        }

        /// <summary>Clic "umano": posizione, pressione, qualche frame, rilascio.</summary>
        private IEnumerator Click(VisualElement target)
        {
            Assert.That(target, Is.Not.Null);
            var center = target.worldBound.center;
            // da coordinate del pannello (punti, y verso il basso) a pixel dello schermo (y verso l'alto)
            var scale = Screen.width / target.panel.visualTree.layout.width;
            var screen = new Vector2(center.x * scale, Screen.height - center.y * scale);
            Move(_mouse.position, screen);
            yield return null;
            yield return null;
            Assert.That(screen.x, Is.InRange(0f, Screen.width), "Il bottone è fuori dallo schermo");
            Assert.That(screen.y, Is.InRange(0f, Screen.height), "Il bottone è fuori dallo schermo");
            Press(_mouse.leftButton);
            for (var i = 0; i < 4; i++) yield return null;
            Release(_mouse.leftButton);
            for (var i = 0; i < 3; i++) yield return null;
        }
    }
}
