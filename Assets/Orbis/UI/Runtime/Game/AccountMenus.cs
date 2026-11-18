using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Orbis.Core.Data;
using Orbis.Core.Device;
using Orbis.Core.Online;
using Orbis.Gameplay;
using Orbis.Net;
using Orbis.Platform;
using Orbis.UI.Elements;
using Orbis.UI.Localization;
using UnityEngine;
using UnityEngine.UIElements;

namespace Orbis.UI.Game
{
    /// <summary>
    /// Pannelli dell'account (M3, docs/04 §1, docs/06 §4–§5): entra o crea l'account (o "Gioca offline"),
    /// proposta di Face ID / impronta dopo il primo accesso, profilo con lo stato del cloud, sincronizzazione a
    /// mano, dispositivi collegati (scollega), esci; scelta del giocatore quando una partita è stata giocata in
    /// due posti (Tieni questo dispositivo / Tieni il cloud / Tienili entrambi).
    /// </summary>
    public sealed class AccountMenus
    {
        private const string OfflinePref = "orbis.account.offline";

        private readonly GamePanel _panel;
        private readonly IPlatformServices _services;
        private readonly OnlineHub _hub;
        private readonly Queue<SyncItem> _conflicts = new Queue<SyncItem>();
        private bool _busy;

        /// <summary>Le partite locali sono cambiate (scaricate dal cloud, conflitto risolto).</summary>
        public event Action SavesChanged;

        /// <summary>Avviso breve (benvenuto, errori di rete).</summary>
        public event Action<string> Toast;

        public AccountMenus(GamePanel panel, IPlatformServices services, OnlineHub hub)
        {
            _panel = panel;
            _services = services;
            _hub = hub;
            _hub.Synced += OnSynced;
        }

        /// <summary>Il giocatore aveva scelto di giocare senza account: all'avvio non si chiede più.</summary>
        public static bool PlaysOffline => PlayerPrefs.GetInt(OfflinePref, 0) == 1;

        /// <summary>Nome del dispositivo per "Dispositivi collegati" (dalle tabelle dei testi).</summary>
        public static string DeviceName(PlatformKind kind)
        {
            switch (kind)
            {
                case PlatformKind.Ios:
                    return Loc.Get(SystemInfo.deviceModel.StartsWith("iPad", StringComparison.Ordinal) ? "device.ipad" : "device.iphone");
                case PlatformKind.Android:
                    return string.IsNullOrWhiteSpace(SystemInfo.deviceModel) ? Loc.Get("device.android") : SystemInfo.deviceModel.Trim();
                case PlatformKind.Web:
                    {
                        string os = SystemInfo.operatingSystem ?? "";
                        string where = os.Contains("iPad") ? "iPad" : os.Contains("iPhone") || os.Contains("iOS") ? "iPhone" : os.Contains("Android") ? "Android"
                            : os.Contains("Mac") ? "Mac" : os.Contains("Windows") ? "Windows" : os.Contains("Linux") ? "Linux" : null;
                        return where == null ? Loc.Get("device.browser") : Loc.Format("device.browser_on", where);
                    }
                default:
                    return Loc.Get("device.editor");
            }
        }

        /// <summary>Testo della richiesta di Face ID / impronta all'avvio.</summary>
        public static string BiometricReason => Loc.Get("account.biometric.reason");

        private bool IsIos => _services.Kind == PlatformKind.Ios;

        // ------------------------------------------------------------------ avvio

        /// <summary>
        /// All'avvio, nel menu: rientra col token salvato (Face ID se attivo); se non c'è un account e il giocatore
        /// non ha scelto di giocare offline, mostra la schermata di accesso.
        /// </summary>
        public async Task StartupAsync()
        {
            bool ok = await _hub.RestoreAsync();
            if (ok)
            {
                Toast?.Invoke(Loc.Format("account.welcome_back", _hub.Account.User?.DisplayName ?? ""));
                return;
            }
            if (!string.IsNullOrEmpty(_hub.Account.EndReason) || !PlaysOffline) Open(true);
        }

        // ------------------------------------------------------------------ pannello

        /// <summary>Apre l'account: modulo di accesso se si è fuori, profilo se si è dentro.</summary>
        public void Open() => Open(false);

        private void Open(bool atStartup)
        {
            if (_hub.Account.IsSignedIn) OpenProfile();
            else OpenSignIn(atStartup, false);
        }

        private void OpenSignIn(bool atStartup, bool register)
        {
            _panel.Open(Loc.Get("account.title"));
            _panel.Tabs(new List<(string, string)> { ("login", Loc.Get("account.tab.login")), ("register", Loc.Get("account.tab.register")) },
                register ? "register" : "login", key => OpenSignIn(atStartup, key == "register"));
            string ended = _hub.Account.EndReason;
            if (!string.IsNullOrEmpty(ended) && ended != "logout")
                Notice(Loc.Get(ended == "refresh_reused" ? "account.ended.refresh_reused" : ended == "refresh_expired" ? "account.ended.refresh_expired" : "account.ended.generic"));
            else _panel.Text(Loc.Get("account.intro"));

            if (!register && PasskeysHere)
            {
                var status = _panel.Text("");
                status.AddToClassList("panel-text--error");
                status.style.display = DisplayStyle.None;
                var row = new VisualElement();
                row.AddToClassList("panel-actions");
                _panel.Content.Add(row);
                PasskeyButton(row, Loc.Get("account.passkey.login"), "primary", false, () => _hub.Api.PasskeyLoginOptionsAsync(), async (challengeId, credential) =>
                {
                    await _hub.Account.LoginWithPasskeyAsync(challengeId, credential);
                    await SignedInAsync(false);
                }, status).name = "account-passkey-login";
                _panel.Text(Loc.Get("account.passkey.or"));
            }

            _panel.Section(Loc.Get("account.username"));
            var user = Field("account-username", 20, false);
            if (register) _panel.Text(Loc.Get("account.username.note"));
            TextField display = null;
            if (register)
            {
                _panel.Section(Loc.Get("account.display_name"));
                display = Field("account-display", 40, false);
            }
            _panel.Section(Loc.Get("account.password"));
            var pass = Field("account-password", 200, true);
            if (register) _panel.Text(Loc.Get("account.password.note"));
            var error = _panel.Text("");
            error.AddToClassList("panel-text--error");
            error.style.display = DisplayStyle.None;

            if (atStartup)
                _panel.Footer(Loc.Get("account.offline"), OrbisIconKind.Play, "", () =>
                {
                    PlayerPrefs.SetInt(OfflinePref, 1);
                    PlayerPrefs.Save();
                    _panel.Close();
                    Toast?.Invoke(Loc.Get("account.offline.note"));
                }).name = "account-offline";
            _panel.Footer(Loc.Get(register ? "account.register" : "account.login"), OrbisIconKind.User, "primary", async () =>
            {
                if (_busy) return;
                string u = (user.value ?? "").Trim(), p = pass.value ?? "";
                string d = display != null ? (display.value ?? "").Trim() : "";
                if (u.Length == 0 || p.Length == 0) return;
                _busy = true;
                error.style.display = DisplayStyle.Flex;
                error.text = Loc.Get("account.working");
                try
                {
                    if (register) await _hub.Account.RegisterAsync(u, p, d.Length > 0 ? d : u);
                    else await _hub.Account.LoginAsync(u, p);
                    await SignedInAsync(true);
                }
                catch (ApiException e)
                {
                    _services.Haptics.Play(HapticKind.Warning);
                    error.text = ErrorText(e);
                }
                finally
                {
                    _busy = false;
                }
            }).name = register ? "account-register" : "account-login";
        }

        /// <summary>Dopo l'accesso: benvenuto, sincronizzazione, proposta di Face ID / impronta (app) o di una passkey (web).</summary>
        private async Task SignedInAsync(bool withPassword)
        {
            PlayerPrefs.DeleteKey(OfflinePref);
            _services.Haptics.Play(HapticKind.Success);
            Toast?.Invoke(Loc.Format("account.welcome", _hub.Account.User.DisplayName));
            _ = _hub.SyncNowAsync();
            if (_hub.Tokens.BiometricsAvailable && !await _hub.Tokens.BiometricsEnabledAsync()) OfferBiometrics();
            else if (withPassword && PasskeysHere && !await HasPasskeysAsync()) OfferPasskey();
            else OpenProfile();
        }

        private TextField Field(string name, int maxLength, bool password)
        {
            var f = new TextField { name = name, maxLength = maxLength, isPasswordField = password, isDelayed = false };
            f.AddToClassList("panel-field");
            _panel.Content.Add(f);
            return f;
        }

        private void Notice(string text)
        {
            var l = _panel.Text(text);
            l.AddToClassList("panel-text--notice");
        }

        private static string ErrorText(ApiException e)
        {
            if (e.IsNetwork) return Loc.Get("account.error.offline");
            switch (e.Code)
            {
                case "invalid_credentials": return Loc.Get("account.error.credentials");
                case "username_taken": return Loc.Get("account.error.username_taken");
                case "invalid_username": return Loc.Get("account.error.username");
                case "invalid_password": return Loc.Get("account.error.password");
                case "too_many_attempts": return Loc.Get("account.error.too_many");
                case "unknown_passkey": return Loc.Get("account.passkey.unknown");
                case "invalid_passkey":
                case "passkey_cloned":
                case "invalid_challenge": return Loc.Get("account.passkey.error");
                case "passkey_exists": return Loc.Get("account.passkey.exists");
                default: return Loc.Get("account.error.generic");
            }
        }

        private void OfferBiometrics()
        {
            string os = IsIos ? "ios" : "android";
            _panel.Open(Loc.Get("account.biometric.offer.title." + os));
            _panel.Text(Loc.Get("account.biometric.offer.text." + os));
            _panel.Footer(Loc.Get("account.biometric.no"), OrbisIconKind.Close, "", OpenProfile).name = "account-biometric-no";
            _panel.Footer(Loc.Get("account.biometric.yes"), OrbisIconKind.Lock, "primary", async () =>
            {
                await SetBiometricsAsync(true);
                OpenProfile();
            }).name = "account-biometric-yes";
        }

        private async Task SetBiometricsAsync(bool on)
        {
            // il token attuale passa nell'archivio giusto (scriverlo non chiede lo sblocco)
            await _hub.Tokens.SetBiometricsAsync(on);
            await _hub.Account.PersistTokenAsync();
        }

        private async void OpenProfile()
        {
            var user = _hub.Account.User;
            _panel.Open(Loc.Get("account.title"));
            var name = new Label(user?.DisplayName ?? "");
            name.AddToClassList("account-name");
            _panel.Content.Add(name);
            _panel.Text(Loc.Format("account.username_line", user?.Username ?? ""));

            _panel.Section(Loc.Get("account.cloud"));
            var status = _panel.Text(SyncText());
            status.name = "account-sync-status";
            var actions = new VisualElement();
            actions.AddToClassList("panel-actions");
            _panel.Content.Add(actions);
            GamePanel.ActionButton(actions, Loc.Get("account.sync_now"), OrbisIconKind.Cloud, "primary", async () =>
            {
                status.text = Loc.Get("account.cloud.syncing");
                await _hub.SyncNowAsync();
                if (_panel.IsOpen) status.text = SyncText();
            }).name = "account-sync";

            if (_hub.Tokens.BiometricsAvailable)
            {
                bool on = await _hub.Tokens.BiometricsEnabledAsync();
                if (!_panel.IsOpen) return;
                string os = IsIos ? "ios" : "android";
                Switch(Loc.Get("account.biometric." + os), Loc.Get("account.biometric.note"), on, async v => await SetBiometricsAsync(v));
            }

            if (PasskeysHere) PasskeySection();

            _panel.Section(Loc.Get("account.devices"));
            var list = new VisualElement();
            _panel.Content.Add(list);
            var loading = _panel.Text(Loc.Get("account.devices.loading"), list);
            _panel.Footer(Loc.Get("account.logout"), OrbisIconKind.Close, "danger", () =>
                _panel.Confirm(Loc.Get("account.logout.title"), Loc.Get("account.logout.text"), Loc.Get("account.logout"), async () =>
                {
                    await _hub.Account.LogoutAsync();
                    Toast?.Invoke(Loc.Get("account.logged_out"));
                })).name = "account-logout";

            List<DeviceSession> sessions;
            try
            {
                sessions = await _hub.Api.SessionsAsync();
            }
            catch (ApiException e)
            {
                if (_panel.IsOpen) loading.text = e.IsNetwork ? Loc.Get("account.cloud.offline") : Loc.Get("account.devices.error");
                return;
            }
            if (!_panel.IsOpen) return;
            list.Clear();
            foreach (var s in sessions)
            {
                var card = new VisualElement();
                card.AddToClassList("save-card");
                var title = new Label(s.DeviceName);
                title.AddToClassList("save-card__name");
                var info = new Label(s.Current ? Loc.Get("account.devices.this") : Loc.Format("account.devices.last", Fmt.DateTime(s.LastUsedAt.ToLocalTime())));
                info.AddToClassList("save-card__info");
                card.Add(title);
                card.Add(info);
                if (!s.Current)
                {
                    var row = new VisualElement();
                    row.AddToClassList("save-card__actions");
                    var session = s;
                    GamePanel.ActionButton(row, Loc.Get("account.devices.remove"), OrbisIconKind.Trash, "danger", () =>
                        _panel.Confirm(Loc.Get("account.devices.remove.title"), Loc.Format("account.devices.remove.text", session.DeviceName),
                            Loc.Get("account.devices.remove"), async () =>
                            {
                                try
                                {
                                    await _hub.Api.RevokeSessionAsync(session.Id);
                                }
                                catch (ApiException e)
                                {
                                    Toast?.Invoke(ErrorText(e));
                                }
                                OpenProfile();
                            }));
                    card.Add(row);
                }
                list.Add(card);
            }
        }

        private string SyncText()
        {
            if (_hub.Syncing) return Loc.Get("account.cloud.syncing");
            var r = _hub.LastReport;
            if (r == null) return Loc.Get("account.cloud.never");
            if (r.Offline) return Loc.Get("account.cloud.offline");
            if (r.Failed > 0) return Loc.Get("account.cloud.failed");
            return Loc.Format("account.cloud.synced", _hub.Sync.LastSyncUtc.ToLocalTime().ToString("HH:mm", Fmt.It));
        }

        private void Switch(string label, string detail, bool value, Action<bool> changed)
        {
            var row = new Button { name = "account-biometric" };
            row.AddToClassList("setting-row");
            row.AddToClassList("panel-switch");
            var texts = new VisualElement { pickingMode = PickingMode.Ignore };
            texts.AddToClassList("setting-row__texts");
            var l = new Label(label) { pickingMode = PickingMode.Ignore };
            l.AddToClassList("setting-row__label");
            var n = new Label(detail) { pickingMode = PickingMode.Ignore };
            n.AddToClassList("setting-row__detail");
            texts.Add(l);
            texts.Add(n);
            var sw = new VisualElement { pickingMode = PickingMode.Ignore };
            sw.AddToClassList("switch");
            var knob = new VisualElement { pickingMode = PickingMode.Ignore };
            knob.AddToClassList("switch__knob");
            sw.Add(knob);
            sw.EnableInClassList("switch--on", value);
            row.Add(texts);
            row.Add(sw);
            row.clicked += () =>
            {
                _services.Haptics.Play(HapticKind.Selection);
                value = !value;
                sw.EnableInClassList("switch--on", value);
                changed(value);
            };
            _panel.Content.Add(row);
        }

        // ------------------------------------------------------------------ passkey (versione web)

        /// <summary>Le passkey si usano solo nella versione web (sulle app c'è Face ID / l'impronta).</summary>
        private bool PasskeysHere => _services.Kind == PlatformKind.Web && WebPasskeys.Supported;

        private async Task<bool> HasPasskeysAsync()
        {
            try
            {
                return (await _hub.Api.PasskeysAsync()).Count > 0;
            }
            catch (ApiException)
            {
                return true; // nel dubbio non si propone
            }
        }

        /// <summary>
        /// Pulsante che crea o usa una passkey. Le opzioni del server si preparano subito; la richiesta al browser si
        /// prepara quando il dito o il mouse preme il pulsante e parte al rilascio (Safari la vuole dentro un gesto).
        /// </summary>
        private Button PasskeyButton(VisualElement parent, string label, string style, bool create, Func<Task<JsonValue>> options,
            Func<string, JsonValue, Task> onCredential, Label status)
        {
            Task<JsonValue> prepared = options();
            Task<string> pending = null;
            int armed = 0;
            string challengeId = null;
            Button b = null;

            void Arm()
            {
                if (pending != null || prepared == null || prepared.Status != TaskStatus.RanToCompletion) return;
                challengeId = prepared.Result["challengeId"].AsString("");
                armed = WebPasskeys.Arm(create, JsonText.Of(prepared.Result), out pending);
            }

            void Show(string text)
            {
                status.text = text;
                status.style.display = string.IsNullOrEmpty(text) ? DisplayStyle.None : DisplayStyle.Flex;
            }

            b = GamePanel.ActionButton(parent, label, OrbisIconKind.Lock, style, async () =>
            {
                if (_busy) return;
                if (prepared != null && !prepared.IsCompleted)
                {
                    Show(Loc.Get("account.passkey.loading"));
                    try
                    {
                        await prepared;
                    }
                    catch (ApiException)
                    {
                        // gestito qui sotto
                    }
                }
                if (prepared == null || prepared.Status != TaskStatus.RanToCompletion)
                {
                    var e = prepared?.Exception?.GetBaseException() as ApiException;
                    Show(e != null ? ErrorText(e) : Loc.Get("account.passkey.error"));
                    prepared = options();
                    return;
                }
                Arm();
                WebPasskeys.RunNow(armed);
                var task = pending;
                var challenge = challengeId;
                pending = null;
                // la sfida si usa una volta: la prossima prova ne chiede una nuova
                prepared = options();
                _busy = true;
                try
                {
                    var result = Json.Parse(await task);
                    if (!result["ok"].AsBool())
                    {
                        string err = result["error"].AsString("");
                        Show(Loc.Get(err == "NotAllowedError" || err == "AbortError" ? "account.passkey.cancelled"
                            : err == "Timeout" ? "account.passkey.tap_again" : err == "InvalidStateError" ? "account.passkey.exists" : "account.passkey.error"));
                        return;
                    }
                    Show(Loc.Get("account.working"));
                    await onCredential(challenge, result["credential"]);
                }
                catch (ApiException e)
                {
                    _services.Haptics.Play(HapticKind.Warning);
                    Show(ErrorText(e));
                }
                catch (JsonException)
                {
                    Show(Loc.Get("account.passkey.error"));
                }
                finally
                {
                    _busy = false;
                }
            });
            // il gesto: la richiesta parte al rilascio del dito o del tasto
            b.RegisterCallback<PointerDownEvent>(_ => Arm(), TrickleDown.TrickleDown);
            b.RegisterCallback<KeyDownEvent>(_ => Arm(), TrickleDown.TrickleDown);
            return b;
        }

        private void OfferPasskey()
        {
            _panel.Open(Loc.Get("account.passkey.offer.title"));
            _panel.Text(Loc.Get("account.passkey.offer.text"));
            var status = _panel.Text("");
            status.AddToClassList("panel-text--error");
            status.style.display = DisplayStyle.None;
            _panel.Footer(Loc.Get("account.biometric.no"), OrbisIconKind.Close, "", OpenProfile).name = "account-passkey-no";
            var footer = new VisualElement();
            footer.AddToClassList("panel-actions");
            _panel.Content.Add(footer);
            PasskeyButton(footer, Loc.Get("account.passkey.offer.yes"), "primary", true, () => _hub.Api.PasskeyRegisterOptionsAsync(), CreatedAsync, status)
                .name = "account-passkey-create";
        }

        private async Task CreatedAsync(string challengeId, JsonValue credential)
        {
            await _hub.Api.PasskeyRegisterVerifyAsync(challengeId, credential, _hub.Device.Name);
            _services.Haptics.Play(HapticKind.Success);
            Toast?.Invoke(Loc.Get("account.passkey.created"));
            OpenProfile();
        }

        /// <summary>Profilo sul web: passkey registrate (con Elimina) e "Crea una passkey".</summary>
        private async void PasskeySection()
        {
            _panel.Section(Loc.Get("account.passkey.section"));
            _panel.Text(Loc.Get("account.passkey.note"));
            var list = new VisualElement();
            _panel.Content.Add(list);
            var status = _panel.Text("");
            status.AddToClassList("panel-text--error");
            status.style.display = DisplayStyle.None;
            var row = new VisualElement();
            row.AddToClassList("panel-actions");
            _panel.Content.Add(row);
            PasskeyButton(row, Loc.Get("account.passkey.create"), "", true, () => _hub.Api.PasskeyRegisterOptionsAsync(), CreatedAsync, status)
                .name = "account-passkey-create";
            List<PasskeyInfo> passkeys;
            try
            {
                passkeys = await _hub.Api.PasskeysAsync();
            }
            catch (ApiException)
            {
                return;
            }
            if (!_panel.IsOpen) return;
            if (passkeys.Count == 0) _panel.Text(Loc.Get("account.passkey.none"), list);
            foreach (var p in passkeys)
            {
                var card = new VisualElement();
                card.AddToClassList("save-card");
                var title = new Label(p.Name);
                title.AddToClassList("save-card__name");
                var info = new Label(p.LastUsedAt > DateTime.MinValue ? Loc.Format("account.passkey.used", Fmt.DateTime(p.LastUsedAt.ToLocalTime())) : Loc.Get("account.passkey.never_used"));
                info.AddToClassList("save-card__info");
                var actions = new VisualElement();
                actions.AddToClassList("save-card__actions");
                var passkey = p;
                GamePanel.ActionButton(actions, Loc.Get("account.passkey.remove"), OrbisIconKind.Trash, "danger", () =>
                    _panel.Confirm(Loc.Get("account.passkey.remove.title"), Loc.Format("account.passkey.remove.text", passkey.Name), Loc.Get("account.passkey.remove"), async () =>
                    {
                        try
                        {
                            await _hub.Api.DeletePasskeyAsync(passkey.Id);
                        }
                        catch (ApiException e)
                        {
                            Toast?.Invoke(ErrorText(e));
                        }
                        OpenProfile();
                    }));
                card.Add(title);
                card.Add(info);
                card.Add(actions);
                list.Add(card);
            }
        }

        // ------------------------------------------------------------------ conflitti

        /// <summary>Vero se ci sono conflitti in attesa della scelta del giocatore.</summary>
        public bool HasConflicts => _conflicts.Count > 0;

        private void OnSynced(SyncReport report)
        {
            if (report.Changed) SavesChanged?.Invoke();
            foreach (var c in report.Conflicts)
            {
                bool known = false;
                foreach (var q in _conflicts) known |= q.LocalId == c.LocalId;
                if (!known) _conflicts.Enqueue(c);
            }
            if (report.SignedOut) Toast?.Invoke(Loc.Get("account.ended.generic"));
        }

        /// <summary>Mostra il prossimo conflitto da risolvere (nel menu; durante la partita aspettano).</summary>
        public async void ShowNextConflict()
        {
            if (_conflicts.Count == 0 || _panel.IsOpen) return;
            var c = _conflicts.Peek();
            var local = await LocalMetaAsync(c.LocalId);
            string name = local["name"].AsString(c.Cloud?.Name ?? "");
            string other = c.Cloud?.DeviceName ?? "";
            _panel.Open(Loc.Get("conflict.title"));
            _panel.Text(Loc.Format("conflict.text", name, other));
            _panel.Section(Loc.Get("conflict.local"));
            _panel.Text(Info(local, DateTime.MinValue));
            _panel.Section(Loc.Format("conflict.cloud", other));
            _panel.Text(Info(c.Cloud?.Meta ?? JsonValue.Null, c.Cloud?.UpdatedAt ?? DateTime.MinValue));
            _panel.Text(Loc.Get("conflict.both.note"));
            _panel.Footer(Loc.Get("conflict.keep_both"), OrbisIconKind.Layers, "", () => Resolve(c, ConflictChoice.KeepBoth)).name = "conflict-both";
            _panel.Footer(Loc.Get("conflict.keep_cloud"), OrbisIconKind.Cloud, "", () => Resolve(c, ConflictChoice.KeepCloud)).name = "conflict-cloud";
            _panel.Footer(Loc.Get("conflict.keep_device"), OrbisIconKind.Check, "primary", () => Resolve(c, ConflictChoice.KeepDevice)).name = "conflict-device";
        }

        private async void Resolve(SyncItem c, ConflictChoice choice)
        {
            if (_busy) return;
            _busy = true;
            try
            {
                await _hub.Sync.ResolveAsync(c, choice);
                _conflicts.Dequeue();
                SavesChanged?.Invoke();
            }
            catch (ApiException e)
            {
                Debug.LogWarning("[Orbis] conflitto non risolto: " + e.Message);
                _conflicts.Dequeue();
                Toast?.Invoke(e.IsNetwork ? Loc.Get("account.cloud.offline") : Loc.Get("conflict.error"));
            }
            finally
            {
                _busy = false;
            }
            _panel.Close();
            if (_conflicts.Count > 0) ShowNextConflict();
        }

        private static string Info(JsonValue meta, DateTime updated)
        {
            DateTime saved = DateTime.TryParse(meta["saved"].AsString(""), Fmt.It, System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal, out var t)
                ? t : updated;
            return Loc.Format("conflict.info", meta["level"].AsInt(1), Fmt.Money(meta["cash"].AsLong()), saved == DateTime.MinValue ? "—" : Fmt.DateTime(saved.ToLocalTime()));
        }

        private async Task<JsonValue> LocalMetaAsync(string id)
        {
            var bytes = await _services.SaveStore.ReadAsync("saves/" + id + "/meta");
            if (bytes == null) return JsonValue.Null;
            try
            {
                return Json.Parse(Encoding.UTF8.GetString(bytes));
            }
            catch (JsonException)
            {
                return JsonValue.Null;
            }
        }

        // ------------------------------------------------------------------ elenco delle partite

        /// <summary>Stato nel cloud di una partita per l'elenco "Continua" (vuoto se non c'è un account).</summary>
        public async Task<string> CloudLabelAsync(string saveId)
        {
            if (!_hub.Account.IsSignedIn) return "";
            var link = await CloudSync.ReadLinkAsync(_services.SaveStore, saveId);
            return Loc.Get(link != null && link.Revision > 0 ? "saves.cloud.synced" : "saves.cloud.local");
        }
    }
}
