using System.Threading;
using System.Threading.Tasks;
using Orbis.Core.Online;
using Orbis.Platform;

namespace Orbis.Net
{
    /// <summary>
    /// Refresh token nell'archivio protetto della piattaforma (docs/06 §4). Con lo sblocco biometrico attivo
    /// sta nell'elemento protetto da Face ID (iOS) o dall'impronta/volto (Android) e leggerlo mostra la richiesta;
    /// altrimenti sta nell'archivio protetto normale (Keychain, Keystore, IndexedDB). Scriverlo non chiede mai lo sblocco.
    /// </summary>
    public sealed class PlatformTokenStore : IRefreshTokenStore
    {
        private const string TokenKey = "orbis.refresh";
        private const string BiometricKey = "orbis.biometric";

        private readonly IPlatformServices _platform;
        private readonly string _reason;
        private bool? _biometric;

        /// <param name="reason">testo mostrato nella richiesta di Face ID / impronta</param>
        public PlatformTokenStore(IPlatformServices platform, string reason)
        {
            _platform = platform;
            _reason = reason;
        }

        public bool BiometricsAvailable => _platform.Biometrics.IsAvailable;

        /// <summary>Il giocatore ha scelto di entrare con Face ID / l'impronta.</summary>
        public async Task<bool> BiometricsEnabledAsync()
        {
            if (_biometric == null) _biometric = await _platform.SecureStore.GetAsync(BiometricKey) == "1";
            return _biometric.Value && BiometricsAvailable;
        }

        /// <summary>
        /// Attiva o spegne lo sblocco biometrico; poi va risalvato il token (AccountSession.PersistTokenAsync),
        /// che finisce nell'archivio giusto e sparisce dall'altro.
        /// </summary>
        public async Task SetBiometricsAsync(bool on)
        {
            _biometric = on;
            await _platform.SecureStore.SetAsync(BiometricKey, on ? "1" : "0");
        }

        public async Task<string> LoadAsync(CancellationToken ct)
        {
            if (await BiometricsEnabledAsync())
            {
                var token = await _platform.Biometrics.ReadProtectedAsync(TokenKey, _reason);
                if (!string.IsNullOrEmpty(token)) return token;
                // Face ID annullato o cambiato: il token protetto non si legge, resta la password
                return null;
            }
            return await _platform.SecureStore.GetAsync(TokenKey);
        }

        public async Task SaveAsync(string token, CancellationToken ct)
        {
            if (await BiometricsEnabledAsync() && await _platform.Biometrics.StoreProtectedAsync(TokenKey, token))
            {
                await _platform.SecureStore.DeleteAsync(TokenKey);
                return;
            }
            await _platform.SecureStore.SetAsync(TokenKey, token);
            await _platform.Biometrics.DeleteProtectedAsync(TokenKey);
        }

        public async Task ClearAsync(CancellationToken ct)
        {
            await _platform.SecureStore.DeleteAsync(TokenKey);
            await _platform.Biometrics.DeleteProtectedAsync(TokenKey);
        }

        /// <summary>C'è un token salvato in chiaro (o protetto): serve a sapere se all'avvio provare a rientrare.</summary>
        public async Task<bool> HasPlainTokenAsync() => !string.IsNullOrEmpty(await _platform.SecureStore.GetAsync(TokenKey));
    }
}
