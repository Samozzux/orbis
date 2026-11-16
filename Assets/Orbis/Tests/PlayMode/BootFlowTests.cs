using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using Orbis.UI.App;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace Orbis.Tests.PlayMode
{
    /// <summary>Avvio completo: logo → caricamento → menu, senza errori in console.</summary>
    public class BootFlowTests
    {
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

            // "Nuova partita" (via tastiera/gamepad: evento di conferma) mostra l'avviso "in arrivo"
            using (var submit = NavigationSubmitEvent.GetPooled())
            {
                submit.target = newGame;
                newGame.SendEvent(submit);
            }
            yield return null;
            Assert.That(OrbisApp.Instance.MainMenu.IsToastVisible, Is.True, "L'avviso non è comparso");

            // i testi devono essere stati sostituiti dalla tabella (nessuna chiave "#..." a vista)
            root.Query<TextElement>().ForEach(t => Assert.That(t.text ?? string.Empty, Does.Not.StartWith("#"), t.name));

            Application.logMessageReceived -= collect;
            Assert.That(problems, Is.Empty, "Avvisi o errori in console durante l'avvio");
        }
    }
}
