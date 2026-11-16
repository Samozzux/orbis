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
        public IEnumerator ClicMouse_SuNuovaPartita_MostraLAvviso()
        {
            yield return LoadMenu();
            var root = OrbisApp.Instance.GetComponent<UIDocument>().rootVisualElement;
            var button = root.Q<Button>("btn-new-game");
            var picked = button.panel.Pick(button.worldBound.center);
            yield return Click(button);
            Assert.That(OrbisApp.Instance.MainMenu.IsToastVisible, Is.True,
                $"Il clic col mouse su Nuova partita non ha mostrato l'avviso (sotto il puntatore: {Describe(picked)})");
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
