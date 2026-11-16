using System.Collections;
using NUnit.Framework;
using Orbis.Core.Device;
using Orbis.Core.Settings;
using Orbis.Platform;
using Orbis.UI.App;
using Orbis.UI.Settings;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace Orbis.Tests.PlayMode
{
    /// <summary>Impostazioni → Schermo: rotazione automatica di default, blocco in verticale/orizzontale.</summary>
    public class SettingsTests
    {
        /// <summary>Orientamento finto che si comporta come nelle app iOS/Android.</summary>
        private sealed class FakeOrientation : IScreenOrientation
        {
            public bool CanControl => true;
            public FixedOrientation Current { get; set; } = FixedOrientation.Portrait;
            public DisplaySettings LastApplied { get; private set; }
            public int ApplyCount { get; private set; }

            public void Apply(DisplaySettings settings)
            {
                LastApplied = DisplaySettings.Deserialize(settings.Serialize());
                ApplyCount++;
            }
        }

        private sealed class TestServices : PlatformServicesBase
        {
            public TestServices(IScreenOrientation orientation) : base(PlatformKind.Editor)
            {
                DeviceProfile = new UnityDeviceProfile(PlatformKind.Editor, () => 1f, () => true);
                ScreenOrientation = orientation;
            }
        }

        [SetUp]
        public void SetUp() => PlayerPrefs.DeleteKey(SettingsRepository.DisplayKey);

        [TearDown]
        public void TearDown()
        {
            PlatformServices.Override(null);
            PlayerPrefs.DeleteKey(SettingsRepository.DisplayKey);
        }

        [UnityTest]
        public IEnumerator Impostazioni_RotazioneAutomaticaEBlocco()
        {
            var orientation = new FakeOrientation { Current = FixedOrientation.Landscape };
            PlatformServices.Override(new TestServices(orientation));

            var previous = OrbisApp.Instance;
            SceneManager.LoadScene("Boot");
            var deadline = Time.realtimeSinceStartup + 30f;
            // si aspetta la NUOVA istanza (quella del test precedente può essere ancora viva per un frame)
            while ((OrbisApp.Instance == null || OrbisApp.Instance == previous || OrbisApp.Instance.State != OrbisApp.AppState.MainMenu)
                   && Time.realtimeSinceStartup < deadline)
                yield return null;
            Assert.That(OrbisApp.Instance.State, Is.EqualTo(OrbisApp.AppState.MainMenu));

            // all'avvio: rotazione automatica (predefinita) già applicata
            Assert.That(orientation.ApplyCount, Is.GreaterThanOrEqualTo(1));
            Assert.That(orientation.LastApplied.AutoRotate, Is.True);

            var root = OrbisApp.Instance.GetComponent<UIDocument>().rootVisualElement;
            var settings = OrbisApp.Instance.Settings;
            Submit(root.Q<Button>("nav-settings"));
            yield return null;
            yield return null;
            Assert.That(settings.IsOpen, Is.True, "Il pannello Impostazioni non si è aperto");
            Assert.That(root.Q("orientation-controls").resolvedStyle.display, Is.EqualTo(DisplayStyle.Flex));

            // spegnendo la rotazione si blocca l'orientamento in uso (qui orizzontale)
            Submit(root.Q<Button>("toggle-autorotate"));
            yield return null;
            Assert.That(orientation.LastApplied.AutoRotate, Is.False);
            Assert.That(orientation.LastApplied.Allowed, Is.EqualTo(new AllowedOrientations(false, true)));
            Assert.That(PlayerPrefs.GetString(SettingsRepository.DisplayKey), Is.EqualTo("fixed:landscape"));

            // poi si sceglie il verticale
            Submit(root.Q<Button>("orientation-portrait"));
            yield return null;
            Assert.That(orientation.LastApplied.Allowed, Is.EqualTo(new AllowedOrientations(true, false)));
            Assert.That(SettingsRepository.LoadDisplay().Fixed, Is.EqualTo(FixedOrientation.Portrait));

            // riaccendendo la rotazione si torna a entrambi
            Submit(root.Q<Button>("toggle-autorotate"));
            yield return null;
            Assert.That(orientation.LastApplied.Allowed, Is.EqualTo(new AllowedOrientations(true, true)));

            Submit(root.Q<Button>("settings-close"));
            yield return null;
            Assert.That(settings.IsOpen, Is.False);
        }

        private static void Submit(VisualElement target)
        {
            Assert.That(target, Is.Not.Null);
            using (var e = NavigationSubmitEvent.GetPooled())
            {
                e.target = target;
                target.SendEvent(e);
            }
        }
    }
}
