using System.Collections;
using System.Threading.Tasks;
using NUnit.Framework;
using Orbis.Core.Device;
using Orbis.Platform;
using Orbis.UI.App;
using Orbis.UI.Localization;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace Orbis.Tests.PlayMode
{
    /// <summary>
    /// Face ID / impronta annullati all'avvio (M3): finché c'è il token protetto, il modulo di accesso offre di
    /// riprovare invece di chiedere subito la password.
    /// </summary>
    public class AccountBiometricTests
    {
        /// <summary>Biometria presente, ma il giocatore annulla sempre la richiesta.</summary>
        private sealed class CancelledBiometrics : IBiometricAuth
        {
            public int Reads;
            public BiometricKind Kind => BiometricKind.AndroidBiometric;
            public bool IsAvailable => true;
            public Task<bool> StoreProtectedAsync(string key, string secret) => Task.FromResult(true);

            public Task<string> ReadProtectedAsync(string key, string reason)
            {
                Reads++;
                return Task.FromResult<string>(null);
            }

            public Task DeleteProtectedAsync(string key) => Task.CompletedTask;
        }

        private sealed class TestServices : PlatformServicesBase
        {
            public TestServices(IBiometricAuth biometrics, bool protectedToken) : base(PlatformKind.Editor)
            {
                DeviceProfile = new UnityDeviceProfile(PlatformKind.Editor, () => 1f, () => false);
                Biometrics = biometrics;
                // stesso contenuto che lascia PlatformTokenStore dopo "Sì, attiva" e un token salvato
                SecureStore.SetAsync("orbis.biometric", "1");
                if (protectedToken) SecureStore.SetAsync("orbis.refresh.protected", "1");
            }
        }

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

        private static IEnumerator OpenSignIn()
        {
            var previous = OrbisApp.Instance;
            SceneManager.LoadScene("Boot");
            var deadline = Time.realtimeSinceStartup + 30f;
            while ((OrbisApp.Instance == null || OrbisApp.Instance == previous || OrbisApp.Instance.State != OrbisApp.AppState.MainMenu
                    || OrbisApp.Instance.Account == null) && Time.realtimeSinceStartup < deadline)
                yield return null;
            Assert.That(OrbisApp.Instance.Account, Is.Not.Null, "Account non pronto entro 30 secondi");
            OrbisApp.Instance.Account.Open();
            for (var i = 0; i < 3; i++) yield return null;
            Assert.That(OrbisApp.Instance.Panel.IsOpen, Is.True, "L'accesso non si è aperto");
        }

        [UnityTest]
        public IEnumerator BiometriaAnnullata_ModuloOffreDiRiprovare()
        {
            var biometrics = new CancelledBiometrics();
            PlatformServices.Override(new TestServices(biometrics, true));
            yield return OpenSignIn();

            var root = OrbisApp.Instance.GetComponent<UIDocument>().rootVisualElement;
            var retry = root.Q<Button>("account-biometric-login");
            Assert.That(retry, Is.Not.Null, "Manca \"Entra con l'impronta o il volto\" nel modulo di accesso");
            Assert.That(root.Q<TextField>("account-password"), Is.Not.Null, "La password deve restare possibile");

            // annullata di nuovo: resta nel modulo con la spiegazione, nessuna sessione
            Submit(retry);
            for (var i = 0; i < 3; i++) yield return null;
            Assert.That(biometrics.Reads, Is.EqualTo(1), "Il pulsante deve chiedere di nuovo l'impronta");
            Assert.That(OrbisApp.Instance.Panel.IsOpen, Is.True, "Senza token letto si resta nel modulo di accesso");
            Assert.That(root.Q<TextField>("account-password"), Is.Not.Null);
            string failed = Loc.Get("account.biometric.failed.android");
            Assert.That(root.Q("panel-content").Query<Label>().Where(l => l.text == failed && l.style.display.value == DisplayStyle.Flex).ToList(),
                Is.Not.Empty, "Manca l'avviso dopo l'annullamento");
        }

        [UnityTest]
        public IEnumerator SenzaTokenProtetto_NienteRiprova()
        {
            PlatformServices.Override(new TestServices(new CancelledBiometrics(), false));
            yield return OpenSignIn();

            var root = OrbisApp.Instance.GetComponent<UIDocument>().rootVisualElement;
            Assert.That(root.Q<Button>("account-biometric-login"), Is.Null, "Dopo \"Esci\" non c'è niente da riprovare");
            Assert.That(root.Q<Button>("account-login"), Is.Not.Null);
        }
    }
}
