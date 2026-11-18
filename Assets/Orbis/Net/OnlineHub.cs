using System;
using System.Threading.Tasks;
using Orbis.Core.Device;
using Orbis.Core.Online;
using Orbis.Platform;
using UnityEngine;

namespace Orbis.Net
{
    /// <summary>
    /// Account e cloud del gioco (M3): crea client, sessione e sincronizzazione per il dispositivo, rientra
    /// all'avvio col token salvato (qui compare Face ID se attivo), sincronizza al ritorno nel menu, alla
    /// pausa dell'app e ogni 15 minuti di gioco (docs/06 §5). Tutto continua a funzionare offline.
    /// </summary>
    public sealed class OnlineHub
    {
        /// <summary>Backend su Altervista (docs/06 §1); "orbis.apiBase" nei PlayerPrefs lo sostituisce (prove).</summary>
        public const string DefaultApiBase = "https://giuliaa8.altervista.org/orbis";

        /// <summary>Ogni quanto si sincronizza durante il gioco (secondi reali).</summary>
        public const float AutoSyncSeconds = 15 * 60;

        public static OnlineHub Current { get; private set; }

        public ApiClient Api { get; private set; }
        public AccountSession Account { get; private set; }
        public CloudSync Sync { get; private set; }
        public PlatformTokenStore Tokens { get; private set; }
        public DeviceIdentity Device { get; private set; }

        /// <summary>Esito dell'ultima sincronizzazione (null prima della prima).</summary>
        public SyncReport LastReport { get; private set; }
        public bool Syncing { get; private set; }

        /// <summary>Fine di una sincronizzazione (anche offline o con conflitti da risolvere).</summary>
        public event Action<SyncReport> Synced;

        private float _lastSync = float.NegativeInfinity;

        /// <param name="biometricReason">testo della richiesta di Face ID / impronta</param>
        /// <param name="deviceName">nome leggibile del dispositivo per "Dispositivi collegati" (dalle tabelle dei testi)</param>
        public static async Task<OnlineHub> CreateAsync(IPlatformServices platform, string biometricReason, string deviceName)
        {
            var hub = new OnlineHub();
            string apiBase = PlayerPrefs.GetString("orbis.apiBase", "");
            if (string.IsNullOrWhiteSpace(apiBase)) apiBase = DefaultApiBase;
            hub.Device = await AccountSession.DeviceAsync(platform.SaveStore, deviceName, PlatformName(platform.Kind));
            hub.Api = new ApiClient(new UnityHttpTransport(apiBase));
            hub.Tokens = new PlatformTokenStore(platform, biometricReason);
            hub.Account = new AccountSession(hub.Api, hub.Tokens, platform.SaveStore, hub.Device);
            hub.Sync = new CloudSync(hub.Api, platform.SaveStore, hub.Device);
            Current = hub;
            return hub;
        }

        /// <summary>All'avvio: rientra se c'è un token salvato, poi sincronizza. Non lancia eccezioni.</summary>
        public async Task<bool> RestoreAsync()
        {
            try
            {
                bool ok = await Account.RestoreAsync();
                if (ok) await SyncNowAsync();
                return ok;
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Orbis] accesso automatico non riuscito: " + e.Message);
                return false;
            }
        }

        /// <summary>Sincronizza subito (se si è dentro l'account). Restituisce null se non c'è un account.</summary>
        public async Task<SyncReport> SyncNowAsync()
        {
            if (!Account.IsSignedIn) return null;
            Syncing = true;
            _lastSync = Time.realtimeSinceStartup;
            SyncReport report;
            try
            {
                report = await Sync.SyncAsync();
            }
            catch (Exception e)
            {
                report = new SyncReport { Failed = 1, LastError = e.Message };
                Debug.LogWarning("[Orbis] sincronizzazione non riuscita: " + e);
            }
            Syncing = false;
            LastReport = report;
            if (report.Changed || report.Conflicts.Count > 0 || report.Failed > 0)
                Debug.Log($"[Orbis] cloud: {report.Uploaded} caricate, {report.Downloaded + report.Created} scaricate, {report.Conflicts.Count} conflitti{(report.Offline ? ", offline" : "")}.");
            Synced?.Invoke(report);
            return report;
        }

        /// <summary>Da chiamare ogni frame: ogni 15 minuti una sincronizzazione in background.</summary>
        public void Tick()
        {
            if (Syncing || !Account.IsSignedIn) return;
            if (Time.realtimeSinceStartup - _lastSync >= AutoSyncSeconds) _ = SyncNowAsync();
        }

        /// <summary>L'app va in pausa o si chiude: si carica quello che c'è (al meglio, il sistema può fermarla prima).</summary>
        public void OnPause() => _ = SyncNowAsync();

        private static string PlatformName(PlatformKind kind)
        {
            switch (kind)
            {
                case PlatformKind.Ios: return "ios";
                case PlatformKind.Android: return "android";
                case PlatformKind.Web: return "web";
                default: return "editor";
            }
        }
    }
}
