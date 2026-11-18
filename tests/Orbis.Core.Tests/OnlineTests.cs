using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Orbis.Core.Data;
using Orbis.Core.Online;
using Orbis.Core.Persistence;
using Xunit;

namespace Orbis.Core.Tests
{
    /// <summary>Account e sincronizzazione delle partite (M3) contro il backend finto.</summary>
    public class OnlineTests
    {
        private DateTime _now = new DateTime(2026, 11, 18, 12, 0, 0, DateTimeKind.Utc);

        private sealed class Device
        {
            public MemorySaveStore Store = new MemorySaveStore();
            public MemoryTokenStore Tokens = new MemoryTokenStore();
            public AccountSession Session;
            public CloudSync Sync;
            public int NextId;
        }

        private Device NewDevice(FakeBackend backend, string name, string platform = "ios")
        {
            var d = new Device();
            var identity = new DeviceIdentity { Id = DeviceIdentity.NewId(), Name = name, Platform = platform };
            d.Session = new AccountSession(new ApiClient(backend), d.Tokens, d.Store, identity, () => _now);
            d.Sync = new CloudSync(d.Session.Api, d.Store, identity, () => _now);
            d.Sync.NewLocalId = () => name + "-" + (++d.NextId);
            return d;
        }

        private FakeBackend Backend()
        {
            var b = new FakeBackend { Now = () => _now };
            return b;
        }

        private static async Task SaveGame(Device d, string id, string content, string city = "Chieti")
        {
            await d.Store.WriteAsync("saves/" + id + "/game", Encoding.UTF8.GetBytes(content));
            await d.Store.WriteAsync("saves/" + id + "/meta", Encoding.UTF8.GetBytes(new JsonObject { ["id"] = id, ["name"] = city, ["level"] = 3, ["cash"] = 120000 }.ToString()));
        }

        private static async Task<string> GameOf(Device d, string id)
        {
            var b = await d.Store.ReadAsync("saves/" + id + "/game");
            return b == null ? null : Encoding.UTF8.GetString(b);
        }

        private static async Task<List<string>> LocalIds(Device d) =>
            (await d.Store.ListAsync("saves/")).Where(k => k.EndsWith("/meta", StringComparison.Ordinal)).Select(k => k.Split('/')[1]).ToList();

        // ------------------------------------------------------------------ account

        [Fact]
        public async Task Registrazione_TokenSalvatoEProfiloInCache()
        {
            var backend = Backend();
            var a = NewDevice(backend, "iPhone");
            await a.Session.RegisterAsync("samuele", "password-segreta", "Samuele");
            Assert.True(a.Session.IsSignedIn);
            Assert.Equal("Samuele", a.Session.User.DisplayName);
            Assert.StartsWith("ref-", a.Tokens.Token);
            Assert.NotNull(await a.Store.ReadAsync("account/user"));
            var me = await a.Session.Api.MeAsync();
            Assert.Equal("samuele", me.Username);
        }

        [Fact]
        public async Task TokenScaduto_SiRinnovaUnaVoltaSola_AncheConPiuChiamateInsieme()
        {
            var backend = Backend();
            var a = NewDevice(backend, "iPhone");
            await a.Session.RegisterAsync("samuele", "password-segreta", "Samuele");
            string first = a.Tokens.Token;
            _now = _now.AddMinutes(61);
            // tre chiamate partono insieme: un solo rinnovo, tutte con il token nuovo
            await Task.WhenAll(a.Session.Api.MeAsync(), a.Session.Api.ListSavesAsync(), a.Session.Api.MeAsync());
            Assert.Equal(1, backend.RefreshCalls);
            Assert.NotEqual(first, a.Tokens.Token);
        }

        [Fact]
        public async Task Risposta401_RinnovaERiprova()
        {
            var backend = Backend();
            var a = NewDevice(backend, "iPhone");
            await a.Session.RegisterAsync("samuele", "password-segreta", "Samuele");
            backend.ExpireAccessTokens();
            var me = await a.Session.Api.MeAsync();
            Assert.Equal("samuele", me.Username);
            Assert.Equal(1, backend.RefreshCalls);
        }

        [Fact]
        public async Task Riavvio_RientraColTokenSalvato_EOfflineRestaDentro()
        {
            var backend = Backend();
            var a = NewDevice(backend, "iPhone");
            await a.Session.RegisterAsync("samuele", "password-segreta", "Samuele");
            // nuovo avvio dell'app con lo stesso archivio
            var again = new AccountSession(new ApiClient(backend), a.Tokens, a.Store, a.Session.Device, () => _now);
            Assert.True(await again.RestoreAsync());
            Assert.Equal("Samuele", again.User.DisplayName);
            Assert.Equal(1, backend.RefreshCalls);
            // senza rete: si gioca lo stesso col profilo in cache
            backend.Offline = true;
            var offline = new AccountSession(new ApiClient(backend), a.Tokens, a.Store, a.Session.Device, () => _now);
            Assert.True(await offline.RestoreAsync());
            Assert.True(offline.IsSignedIn);
            Assert.Equal("Samuele", offline.User.DisplayName);
        }

        [Fact]
        public async Task TokenRiusato_LaSessioneSiChiude()
        {
            var backend = Backend();
            var a = NewDevice(backend, "iPhone");
            await a.Session.RegisterAsync("samuele", "password-segreta", "Samuele");
            string stolen = a.Tokens.Token;
            await a.Session.GetAccessTokenAsync(true, default);
            // qualcuno usa il vecchio token: il server chiude la sessione del dispositivo
            var e = await Assert.ThrowsAsync<ApiException>(() => new ApiClient(backend).RefreshAsync(stolen));
            Assert.Equal("refresh_reused", e.Code);
            await Assert.ThrowsAsync<ApiException>(() => a.Session.GetAccessTokenAsync(true, default));
            Assert.False(a.Session.IsSignedIn);
            Assert.Null(a.Tokens.Token);
            Assert.Null(await a.Store.ReadAsync("account/user"));
        }

        [Fact]
        public async Task Passkey_SiEntraSenzaPassword()
        {
            var backend = Backend();
            var phone = NewDevice(backend, "iPhone");
            await phone.Session.RegisterAsync("samuele", "password-segreta", "Samuele");
            var web = NewDevice(backend, "Safari sul Mac", "web");
            var options = await web.Session.Api.PasskeyLoginOptionsAsync();
            Assert.Equal("samozzux.github.io", options["publicKey"]["rpId"].AsString());
            // risposta del browser (navigator.credentials.get)
            var assertion = Orbis.Core.Data.Json.Parse(new JsonObject
            {
                ["credentialId"] = "cred-samuele", ["clientDataJSON"] = "e30", ["authenticatorData"] = "AA", ["signature"] = "firma", ["userHandle"] = "",
            }.ToString());
            await web.Session.LoginWithPasskeyAsync(options["challengeId"].AsString(), assertion);
            Assert.True(web.Session.IsSignedIn);
            Assert.Equal("samuele", web.Session.User.Username);
            Assert.NotNull(web.Tokens.Token);
            var wrong = Orbis.Core.Data.Json.Parse(new JsonObject { ["credentialId"] = "cred-samuele", ["signature"] = "falsa" }.ToString());
            var e = await Assert.ThrowsAsync<ApiException>(() => NewDevice(backend, "Altro").Session.LoginWithPasskeyAsync("c1", wrong));
            Assert.Equal("invalid_passkey", e.Code);
        }

        [Fact]
        public async Task Uscita_ChiudeLaSessioneAncheSulServer()
        {
            var backend = Backend();
            var a = NewDevice(backend, "iPhone");
            await a.Session.RegisterAsync("samuele", "password-segreta", "Samuele");
            string token = a.Tokens.Token;
            await a.Session.LogoutAsync();
            Assert.False(a.Session.IsSignedIn);
            Assert.Equal("logout", a.Session.EndReason);
            await Assert.ThrowsAsync<ApiException>(() => new ApiClient(backend).RefreshAsync(token));
        }

        [Fact]
        public async Task IdentitaDelDispositivo_StabileTraUnAvvioEAltro()
        {
            var store = new MemorySaveStore();
            var a = await AccountSession.DeviceAsync(store, "iPhone di Samuele", "ios");
            var b = await AccountSession.DeviceAsync(store, "iPhone di Samuele", "ios");
            Assert.Equal(a.Id, b.Id);
            Assert.InRange(a.Id.Length, 8, 64);
            Assert.Matches("^[A-Za-z0-9_-]+$", a.Id);
        }

        // ------------------------------------------------------------------ sincronizzazione

        [Fact]
        public async Task PartitaIniziataSullIphone_ContinuaSulMac()
        {
            var backend = Backend();
            var phone = NewDevice(backend, "iPhone");
            var mac = NewDevice(backend, "Safari sul Mac", "web");
            await phone.Session.RegisterAsync("samuele", "password-segreta", "Samuele");
            await mac.Session.LoginAsync("samuele", "password-segreta");

            await SaveGame(phone, "p1", "citta-v1");
            var r1 = await phone.Sync.SyncAsync();
            Assert.Equal(1, r1.Uploaded);
            var link = await CloudSync.ReadLinkAsync(phone.Store, "p1");
            Assert.Equal(1, link.Revision);

            var r2 = await mac.Sync.SyncAsync();
            Assert.Equal(1, r2.Created);
            var macId = (await LocalIds(mac)).Single();
            Assert.Equal("citta-v1", await GameOf(mac, macId));
            var meta = Orbis.Core.Data.Json.Parse(Encoding.UTF8.GetString(await mac.Store.ReadAsync("saves/" + macId + "/meta")));
            Assert.Equal(macId, meta["id"].AsString());
            Assert.Equal("Chieti", meta["name"].AsString());
            Assert.Equal(3, meta["level"].AsInt());

            // si gioca sul Mac, poi si torna sull'iPhone
            await SaveGame(mac, macId, "citta-v2");
            Assert.Equal(1, (await mac.Sync.SyncAsync()).Uploaded);
            var r3 = await phone.Sync.SyncAsync();
            Assert.Equal(1, r3.Downloaded);
            Assert.Equal("citta-v2", await GameOf(phone, "p1"));
            // niente da fare se nessuno ha giocato
            var r4 = await phone.Sync.SyncAsync();
            Assert.False(r4.Changed);
            Assert.Empty(r4.Conflicts);
            Assert.Equal(1, backend.SaveCount(1));
        }

        [Fact]
        public async Task GiocataInDuePosti_Conflitto_TieniIlCloud()
        {
            var (backend, phone, mac, macId) = await TwoDevicesSameGame();
            await SaveGame(phone, "p1", "iphone-v2");
            await SaveGame(mac, macId, "mac-v2");
            Assert.Equal(1, (await mac.Sync.SyncAsync()).Uploaded);
            var r = await phone.Sync.SyncAsync();
            var c = Assert.Single(r.Conflicts);
            Assert.Equal("p1", c.LocalId);
            Assert.Equal(2, c.Cloud.Revision);
            await phone.Sync.ResolveAsync(c, ConflictChoice.KeepCloud);
            Assert.Equal("mac-v2", await GameOf(phone, "p1"));
            Assert.False((await phone.Sync.SyncAsync()).Changed);
            Assert.Equal(1, backend.SaveCount(1));
        }

        [Fact]
        public async Task GiocataInDuePosti_Conflitto_TieniQuestoDispositivo()
        {
            var (backend, phone, mac, macId) = await TwoDevicesSameGame();
            await SaveGame(phone, "p1", "iphone-v2");
            await SaveGame(mac, macId, "mac-v2");
            await mac.Sync.SyncAsync();
            var c = (await phone.Sync.SyncAsync()).Conflicts.Single();
            await phone.Sync.ResolveAsync(c, ConflictChoice.KeepDevice);
            var link = await CloudSync.ReadLinkAsync(phone.Store, "p1");
            Assert.Equal(3, link.Revision);
            Assert.Equal("iphone-v2", Encoding.UTF8.GetString(backend.SaveData(1, link.Uuid)));
            // il Mac, che non ha più giocato, scarica la versione dell'iPhone
            Assert.Equal(1, (await mac.Sync.SyncAsync()).Downloaded);
            Assert.Equal("iphone-v2", await GameOf(mac, macId));
        }

        [Fact]
        public async Task GiocataInDuePosti_Conflitto_TieniliEntrambi()
        {
            var (backend, phone, mac, macId) = await TwoDevicesSameGame();
            await SaveGame(phone, "p1", "iphone-v2");
            await SaveGame(mac, macId, "mac-v2");
            await mac.Sync.SyncAsync();
            var c = (await phone.Sync.SyncAsync()).Conflicts.Single();
            await phone.Sync.ResolveAsync(c, ConflictChoice.KeepBoth);
            var ids = await LocalIds(phone);
            Assert.Equal(2, ids.Count);
            Assert.Equal("mac-v2", await GameOf(phone, "p1"));
            string copy = ids.Single(i => i != "p1");
            Assert.Equal("iphone-v2", await GameOf(phone, copy));
            var meta = Orbis.Core.Data.Json.Parse(Encoding.UTF8.GetString(await phone.Store.ReadAsync("saves/" + copy + "/meta")));
            Assert.Equal("Chieti (iPhone)", meta["name"].AsString());
            Assert.Equal(2, backend.SaveCount(1));
            // il Mac riceve la copia come partita nuova
            Assert.Equal(1, (await mac.Sync.SyncAsync()).Created);
        }

        [Fact]
        public async Task PartitaAperta_NonVieneSovrascrittaMentreSiGioca()
        {
            var (_, phone, mac, macId) = await TwoDevicesSameGame();
            await SaveGame(mac, macId, "mac-v2");
            await mac.Sync.SyncAsync();
            phone.Sync.ActiveSaveId = () => "p1";
            var r = await phone.Sync.SyncAsync();
            Assert.Equal(1, r.Deferred);
            Assert.Equal("citta-v1", await GameOf(phone, "p1"));
            // tornati al menu si scarica
            phone.Sync.ActiveSaveId = () => null;
            Assert.Equal(1, (await phone.Sync.SyncAsync()).Downloaded);
        }

        [Fact]
        public async Task PartitaEliminata_SparisceAncheDalCloud_EGliAltriLaTengonoSoloInLocale()
        {
            var (backend, phone, mac, macId) = await TwoDevicesSameGame();
            await phone.Store.DeleteAsync("saves/p1/game");
            await phone.Store.DeleteAsync("saves/p1/meta");
            await CloudSync.MarkDeletedAsync(phone.Store, "p1");
            backend.Offline = true;
            Assert.True((await phone.Sync.SyncAsync()).Offline);
            backend.Offline = false;
            await phone.Sync.SyncAsync();
            Assert.Equal(0, backend.SaveCount(1));
            var r = await mac.Sync.SyncAsync();
            Assert.Equal(1, r.Unlinked);
            Assert.Equal("citta-v1", await GameOf(mac, macId));
            Assert.Null(await CloudSync.ReadLinkAsync(mac.Store, macId));
        }

        [Fact]
        public async Task UploadInterrotto_SiRiprendeConLoStessoUuid()
        {
            var backend = Backend();
            backend.ChunkSize = 10;
            var phone = NewDevice(backend, "iPhone");
            await phone.Session.RegisterAsync("samuele", "password-segreta", "Samuele");
            await SaveGame(phone, "p1", "una-partita-lunga-di-quaranta-caratteri!");
            // un pezzo perso viene rimandato senza problemi
            backend.DropChunks = 1;
            Assert.Equal(1, (await phone.Sync.SyncAsync()).Uploaded);
            Assert.Equal(1, backend.SaveCount(1));

            await SaveGame(phone, "p2", "un'altra-partita-lunga-di-quaranta-car");
            // la rete cade per tutti i tentativi: si riprova al prossimo giro con lo stesso uuid
            backend.DropChunks = 10;
            Assert.True((await phone.Sync.SyncAsync()).Offline);
            var link = await CloudSync.ReadLinkAsync(phone.Store, "p2");
            Assert.Equal(0, link.Revision);
            backend.DropChunks = 0;
            Assert.Equal(1, (await phone.Sync.SyncAsync()).Uploaded);
            Assert.Equal(1, backend.SaveRevision(1, link.Uuid));
            Assert.Equal(2, backend.SaveCount(1));
        }

        [Fact]
        public async Task SessioneChiusaDuranteLaSincronizzazione()
        {
            var backend = Backend();
            var phone = NewDevice(backend, "iPhone");
            await phone.Session.RegisterAsync("samuele", "password-segreta", "Samuele");
            string token = phone.Tokens.Token;
            await phone.Session.GetAccessTokenAsync(true, default);
            await Assert.ThrowsAsync<ApiException>(() => new ApiClient(backend).RefreshAsync(token));
            backend.ExpireAccessTokens();
            await SaveGame(phone, "p1", "v1");
            var r = await phone.Sync.SyncAsync();
            Assert.True(r.SignedOut);
            Assert.False(phone.Session.IsSignedIn);
        }

        private async Task<(FakeBackend, Device, Device, string)> TwoDevicesSameGame()
        {
            var backend = Backend();
            var phone = NewDevice(backend, "iPhone");
            var mac = NewDevice(backend, "Mac", "web");
            await phone.Session.RegisterAsync("samuele", "password-segreta", "Samuele");
            await mac.Session.LoginAsync("samuele", "password-segreta");
            await SaveGame(phone, "p1", "citta-v1");
            await phone.Sync.SyncAsync();
            await mac.Sync.SyncAsync();
            return (backend, phone, mac, (await LocalIds(mac)).Single());
        }

        // ------------------------------------------------------------------ piano (logica pura)

        private static CloudSave Cloud(string uuid, int rev, string checksum) => new CloudSave { Uuid = uuid, Revision = rev, Checksum = checksum };

        private static LocalSaveState Local(string id, string checksum, string uuid = null, int rev = 0, string linked = null) => new LocalSaveState
        {
            Id = id, Checksum = checksum, Link = uuid == null ? null : new CloudLink { Uuid = uuid, Revision = rev, Checksum = linked ?? checksum },
        };

        [Theory]
        [InlineData("aaa", 1, "aaa", 1, "aaa", SyncAction.None)]
        [InlineData("bbb", 1, "aaa", 1, "aaa", SyncAction.Upload)]
        [InlineData("aaa", 2, "ccc", 1, "aaa", SyncAction.Download)]
        [InlineData("bbb", 2, "ccc", 1, "aaa", SyncAction.Conflict)]
        [InlineData("ccc", 2, "ccc", 1, "aaa", SyncAction.Relink)]
        public void Piano_CasiDiBase(string localNow, int cloudRev, string cloudSum, int linkRev, string linkSum, SyncAction expected)
        {
            var plan = SyncPlanner.Plan(new[] { Local("p1", localNow, "u1", linkRev, linkSum) }, new[] { Cloud("u1", cloudRev, cloudSum) }, () => "nuovo");
            Assert.Equal(expected, Assert.Single(plan).Action);
        }

        [Fact]
        public void Piano_NuovaLocale_CaricaConUuidNuovo_SoloNelCloud_ScaricaComeNuova()
        {
            var plan = SyncPlanner.Plan(new[] { Local("p1", "aaa") }, new[] { Cloud("u9", 4, "zzz") }, () => "uuid-nuovo");
            Assert.Equal(2, plan.Count);
            Assert.Contains(plan, i => i.Action == SyncAction.Upload && i.Uuid == "uuid-nuovo" && i.BaseRevision == 0);
            Assert.Contains(plan, i => i.Action == SyncAction.DownloadNew && i.Uuid == "u9");
        }

        [Fact]
        public void Piano_EliminataAltrove_ResTaSoloQui_MaiArrivata_SiRiprova()
        {
            var plan = SyncPlanner.Plan(new[] { Local("p1", "aaa", "u1", 3), Local("p2", "bbb", "u2", 0, "") }, Array.Empty<CloudSave>(), () => "x");
            Assert.Equal(SyncAction.Unlink, plan.Single(i => i.LocalId == "p1").Action);
            var retry = plan.Single(i => i.LocalId == "p2");
            Assert.Equal(SyncAction.Upload, retry.Action);
            Assert.Equal("u2", retry.Uuid);
        }

        [Fact]
        public void Json_ScritturaConEscapeERilettura()
        {
            var o = new JsonObject { ["nome"] = "Città \"vera\"\n", ["n"] = 3, ["ok"] = true, ["vuoto"] = null, ["lista"] = new[] { 1, 2 } };
            var j = Orbis.Core.Data.Json.Parse(o.ToString());
            Assert.Equal("Città \"vera\"\n", j["nome"].AsString());
            Assert.Equal(3, j["n"].AsInt());
            Assert.True(j["ok"].AsBool());
            Assert.True(j["vuoto"].IsNull);
            Assert.Equal(2, j["lista"].Count);
            Assert.Equal(o.ToString(), JsonText.Of(j));
        }
    }
}
