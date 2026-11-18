using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Orbis.Core.Data;
using Orbis.Core.Persistence;

namespace Orbis.Core.Online
{
    /// <summary>
    /// Dove resta il refresh token tra un avvio e l'altro (docs/01 §10): Keychain protetto da Face ID su iOS,
    /// Keystore con l'impronta o il volto su Android, IndexedDB sul web. <see cref="LoadAsync"/> può chiedere
    /// lo sblocco biometrico e restituisce null se non c'è il token o se il giocatore annulla.
    /// </summary>
    public interface IRefreshTokenStore
    {
        Task<string> LoadAsync(CancellationToken ct);
        Task SaveAsync(string token, CancellationToken ct);
        Task ClearAsync(CancellationToken ct);
    }

    public enum AccountState
    {
        SignedOut,
        SignedIn,
    }

    /// <summary>
    /// Sessione dell'account sul dispositivo: token di accesso da 1 ora in memoria, refresh token (90 giorni,
    /// ruotato a ogni rinnovo) nell'archivio protetto, profilo in cache per mostrarlo anche offline.
    /// Un solo rinnovo alla volta: le chiamate che arrivano mentre è in corso aspettano lo stesso.
    /// Se il server rifiuta il rinnovo (scaduto, revocato, riusato) la sessione si chiude e serve la password.
    /// </summary>
    public sealed class AccountSession
    {
        private const string ProfileKey = "account/user";
        private static readonly TimeSpan Margin = TimeSpan.FromSeconds(60);

        private readonly ApiClient _api;
        private readonly IRefreshTokenStore _tokens;
        private readonly ISaveStore _store;
        private readonly Func<DateTime> _utcNow;
        private readonly object _lock = new object();
        private string _access;
        private DateTime _accessExpires;
        private string _refresh;
        private Task<string> _refreshing;

        public AccountSession(ApiClient api, IRefreshTokenStore tokens, ISaveStore store, DeviceIdentity device, Func<DateTime> utcNow = null)
        {
            _api = api;
            _tokens = tokens;
            _store = store;
            Device = device;
            _utcNow = utcNow ?? (() => DateTime.UtcNow);
            _api.AccessToken = GetAccessTokenAsync;
        }

        public DeviceIdentity Device { get; }
        public ApiClient Api => _api;
        public AccountState State { get; private set; }
        public AccountUser User { get; private set; }

        /// <summary>Perché la sessione si è chiusa l'ultima volta ("logout", "refresh_reused", "refresh_expired"…), per spiegarlo.</summary>
        public string EndReason { get; private set; }

        /// <summary>Cambio di stato o di profilo (entrata, uscita, rinnovo con un profilo nuovo).</summary>
        public event Action Changed;

        public bool IsSignedIn => State == AccountState.SignedIn;

        /// <summary>
        /// All'avvio: rientra col refresh token salvato (qui può comparire Face ID). Offline resta dentro col
        /// profilo in cache e rinnova appena c'è la rete. Restituisce false se serve un nuovo accesso.
        /// </summary>
        public async Task<bool> RestoreAsync(CancellationToken ct = default)
        {
            var token = await _tokens.LoadAsync(ct);
            if (string.IsNullOrEmpty(token))
            {
                State = AccountState.SignedOut;
                return false;
            }
            lock (_lock)
            {
                _refresh = token;
                _access = null;
            }
            User = await LoadProfileAsync();
            State = AccountState.SignedIn;
            Changed?.Invoke();
            try
            {
                await GetAccessTokenAsync(true, ct);
            }
            catch (ApiException e) when (e.IsNetwork)
            {
                // offline: si gioca lo stesso, il rinnovo riparte alla prossima chiamata
            }
            catch (ApiException e) when (e.IsSessionEnded)
            {
                return false;
            }
            return State == AccountState.SignedIn;
        }

        public async Task RegisterAsync(string username, string password, string displayName, CancellationToken ct = default) =>
            await ApplyAsync(await _api.RegisterAsync(username, password, displayName, Device, ct), ct);

        public async Task LoginAsync(string username, string password, CancellationToken ct = default) =>
            await ApplyAsync(await _api.LoginAsync(username, password, Device, ct), ct);

        /// <summary>Esce: chiude subito la sessione locale, poi prova a revocarla sul server (offline scadrà da sola).</summary>
        public async Task LogoutAsync(CancellationToken ct = default)
        {
            string token;
            lock (_lock) token = _refresh;
            await EndAsync("logout", ct);
            if (string.IsNullOrEmpty(token)) return;
            try
            {
                await _api.LogoutAsync(token, ct);
            }
            catch (ApiException)
            {
                // offline o sessione già chiusa: va bene così
            }
        }

        /// <summary>Risalva il refresh token attuale (es. dopo aver attivato o spento Face ID: cambia l'archivio).</summary>
        public Task PersistTokenAsync(CancellationToken ct = default)
        {
            string token;
            lock (_lock) token = _refresh;
            return string.IsNullOrEmpty(token) ? Task.CompletedTask : _tokens.SaveAsync(token, ct);
        }

        /// <summary>Token di accesso valido (rinnovato se manca o scade entro un minuto, o se <paramref name="force"/>).</summary>
        public Task<string> GetAccessTokenAsync(bool force, CancellationToken ct)
        {
            lock (_lock)
            {
                if (!force && _access != null && _utcNow() < _accessExpires - Margin) return Task.FromResult(_access);
                if (_refreshing != null && !_refreshing.IsCompleted) return _refreshing;
                if (string.IsNullOrEmpty(_refresh)) return Task.FromException<string>(new ApiException(401, "signed_out", "Serve l'accesso."));
                _refreshing = RefreshAsync(_refresh, ct);
                return _refreshing;
            }
        }

        private async Task<string> RefreshAsync(string refresh, CancellationToken ct)
        {
            try
            {
                var t = await _api.RefreshAsync(refresh, ct);
                await ApplyAsync(t, ct);
                lock (_lock) return _access;
            }
            catch (ApiException e) when (e.IsSessionEnded)
            {
                await EndAsync(e.Code, ct);
                throw;
            }
        }

        private async Task ApplyAsync(TokenSet t, CancellationToken ct)
        {
            lock (_lock)
            {
                _access = t.AccessToken;
                _accessExpires = _utcNow().AddSeconds(Math.Max(60, t.AccessExpiresIn));
                _refresh = t.RefreshToken;
            }
            // il token nuovo va salvato subito: quello vecchio non vale più
            await _tokens.SaveAsync(t.RefreshToken, ct);
            User = t.User;
            await _store.WriteAsync(ProfileKey, Encoding.UTF8.GetBytes(t.User.ToJson().ToString()));
            State = AccountState.SignedIn;
            EndReason = null;
            Changed?.Invoke();
        }

        private async Task EndAsync(string reason, CancellationToken ct)
        {
            lock (_lock)
            {
                _access = null;
                _refresh = null;
            }
            await _tokens.ClearAsync(ct);
            await _store.DeleteAsync(ProfileKey);
            User = null;
            State = AccountState.SignedOut;
            EndReason = reason;
            Changed?.Invoke();
        }

        private async Task<AccountUser> LoadProfileAsync()
        {
            var bytes = await _store.ReadAsync(ProfileKey);
            if (bytes == null) return null;
            try
            {
                return AccountUser.From(Json.Parse(Encoding.UTF8.GetString(bytes)));
            }
            catch (JsonException)
            {
                return null;
            }
        }

        /// <summary>Identità del dispositivo salvata nell'archivio ("device/id"), creata al primo avvio.</summary>
        public static async Task<DeviceIdentity> DeviceAsync(ISaveStore store, string name, string platform)
        {
            var bytes = await store.ReadAsync("device/id");
            string id = bytes != null ? Encoding.UTF8.GetString(bytes).Trim() : null;
            if (string.IsNullOrEmpty(id) || id.Length < 8)
            {
                id = DeviceIdentity.NewId();
                await store.WriteAsync("device/id", Encoding.UTF8.GetBytes(id));
            }
            return new DeviceIdentity { Id = id, Name = string.IsNullOrWhiteSpace(name) ? "Dispositivo" : name.Trim(), Platform = platform };
        }
    }
}
