using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using Orbis.Core.Device;
using Orbis.Platform;
using Orbis.UI.App;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace Orbis.Tests.PlayMode
{
    /// <summary>
    /// Avvio completo: logo → caricamento → menu → nuova partita → salvataggio → Continua, senza errori in console.
    /// I salvataggi restano in memoria (nessun file nella cartella dell'Editor).
    /// </summary>
    public class BootFlowTests
    {
        private sealed class TestServices : PlatformServicesBase
        {
            public TestServices() : base(PlatformKind.Editor)
            {
                DeviceProfile = new UnityDeviceProfile(PlatformKind.Editor, () => 1f, () => false);
            }
        }

        [SetUp]
        public void Setup() => PlatformServices.Override(new TestServices());

        [TearDown]
        public void TearDown() => PlatformServices.Override(null);

        private static void Submit(VisualElement target)
        {
            Assert.That(target, Is.Not.Null);
            using (var submit = NavigationSubmitEvent.GetPooled())
            {
                submit.target = target;
                target.SendEvent(submit);
            }
        }
        [UnityTest]
        public IEnumerator Avvio_ArrivaAlMenuSenzaErrori()
        {
            // gli errori fanno già fallire il test; qui si raccolgono anche gli avvisi
            var problems = new List<string>();
            Application.LogCallback collect = (message, stack, type) =>
            {
                if (type != LogType.Log)
                    problems.Add($"{type}: {message}");
            };
            Application.logMessageReceived += collect;

            var previous = OrbisApp.Instance;
            SceneManager.LoadScene("Boot");
            var deadline = Time.realtimeSinceStartup + 30f;
            // si aspetta la NUOVA istanza (quella del test precedente può essere ancora viva per un frame)
            while ((OrbisApp.Instance == null || OrbisApp.Instance == previous || OrbisApp.Instance.State != OrbisApp.AppState.MainMenu)
                   && Time.realtimeSinceStartup < deadline)
                yield return null;

            Assert.That(OrbisApp.Instance, Is.Not.Null, "OrbisApp non trovato nella scena");
            Assert.That(OrbisApp.Instance.State, Is.EqualTo(OrbisApp.AppState.MainMenu), "Il menu non è comparso entro 30 secondi");

            var root = OrbisApp.Instance.GetComponent<UIDocument>().rootVisualElement;
            var newGame = root.Q<Button>("btn-new-game");
            Assert.That(newGame, Is.Not.Null);
            Assert.That(newGame.enabledSelf, Is.True);
            Assert.That(root.Q<Button>("btn-continue").enabledSelf, Is.False, "Senza salvataggi 'Continua' deve essere disattivato");
            Assert.That(root.Q<Label>("version").text, Does.Contain("0."));

            // le voci non ancora pronte (via tastiera/gamepad: evento di conferma) mostrano l'avviso "in arrivo"
            var friends = root.Q<Button>("nav-friends");
            using (var submit = NavigationSubmitEvent.GetPooled())
            {
                submit.target = friends;
                friends.SendEvent(submit);
            }
            yield return null;
            Assert.That(OrbisApp.Instance.MainMenu.IsToastVisible, Is.True, "L'avviso non è comparso");

            // "Nuova partita" apre le opzioni; "Inizia" entra nel mondo: senza pack (test) l'HUD lo dice, senza errori
            Submit(newGame);
            yield return null;
            Assert.That(OrbisApp.Instance.Panel.IsOpen, Is.True, "Nuova partita non ha aperto il pannello");
            Submit(root.Q<Button>("newgame-difficulty-easy"));
            Submit(root.Q<Button>("newgame-start"));
            for (var i = 0; i < 30; i++) yield return null;
            Assert.That(OrbisApp.Instance.State, Is.EqualTo(OrbisApp.AppState.Game), "Nuova partita non ha aperto il mondo");
            Assert.That(OrbisApp.Instance.Session, Is.Not.Null);
            Assert.That(OrbisApp.Instance.Game, Is.Not.Null, "La partita (economia) non è partita");
            Assert.That(OrbisApp.Instance.Game.State.Config.Difficulty, Is.EqualTo(Orbis.Core.Game.Difficulty.Easy));
            Assert.That(root.Q("game").ClassListContains("screen--visible"), Is.True, "HUD del gioco non visibile");
            Assert.That(root.Q<Label>("hud-cash-value").text, Is.Not.Empty, "La cassa non è mostrata");
            long cash = OrbisApp.Instance.Game.State.Cash;

            // i pannelli della partita si aprono e si chiudono senza errori
            Submit(root.Q<Button>("hud-build"));
            yield return null;
            Assert.That(OrbisApp.Instance.Panel.IsOpen, Is.True, "Costruisci non ha aperto il catalogo");
            Assert.That(root.Q("panel-content").Query<Button>(className: "catalog-card").ToList().Count, Is.GreaterThan(3), "Catalogo vuoto");
            OrbisApp.Instance.Panel.Close();
            Submit(root.Q<Button>("hud-finance"));
            yield return null;
            Assert.That(OrbisApp.Instance.Panel.IsOpen, Is.True, "Finanze non si è aperto");
            OrbisApp.Instance.Panel.Close();
            Submit(root.Q<Button>("hud-rewind"));
            yield return null;
            OrbisApp.Instance.Panel.Close();

            OrbisApp.Instance.EndGame();
            for (var i = 0; i < 5; i++) yield return null;
            Assert.That(OrbisApp.Instance.State, Is.EqualTo(OrbisApp.AppState.MainMenu), "Il ritorno al menu non funziona");
            Assert.That(root.Q<Button>("btn-continue").enabledSelf, Is.True, "Dopo una partita 'Continua' deve essere attivo");

            // "Continua" elenca la partita salvata e la riapre com'era
            Submit(root.Q<Button>("btn-continue"));
            for (var i = 0; i < 5; i++) yield return null;
            Submit(root.Q<Button>("saves-play"));
            for (var i = 0; i < 30; i++) yield return null;
            Assert.That(OrbisApp.Instance.State, Is.EqualTo(OrbisApp.AppState.Game), "Continua non ha riaperto la partita");
            Assert.That(OrbisApp.Instance.Game.State.Cash, Is.EqualTo(cash), "La partita riaperta non è com'era");
            Assert.That(OrbisApp.Instance.Game.State.Config.Difficulty, Is.EqualTo(Orbis.Core.Game.Difficulty.Easy));
            OrbisApp.Instance.EndGame();
            yield return null;

            // i testi devono essere stati sostituiti dalla tabella (nessuna chiave "#..." a vista)
            root.Query<TextElement>().ForEach(t => Assert.That(t.text ?? string.Empty, Does.Not.StartWith("#"), t.name));

            Application.logMessageReceived -= collect;
            Assert.That(problems, Is.Empty, "Avvisi o errori in console durante l'avvio");
        }
    }
}
