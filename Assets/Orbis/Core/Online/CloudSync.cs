using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Orbis.Core.Data;
using Orbis.Core.Persistence;

namespace Orbis.Core.Online
{
    public enum ConflictChoice
    {
        /// <summary>Tieni questo dispositivo: la copia locale sostituisce quella nel cloud.</summary>
        KeepDevice,
        /// <summary>Tieni il cloud: la copia del cloud sostituisce quella locale.</summary>
        KeepCloud,
        /// <summary>Tienili entrambi: la copia locale diventa una partita nuova, quella del cloud prende il suo posto.</summary>
        KeepBoth,
    }

    /// <summary>Esito di una sincronizzazione.</summary>
    public sealed class SyncReport
    {
        public int Uploaded, Downloaded, Created, Relinked, Unlinked, Deferred, Failed;
        /// <summary>Partite giocate sia qui sia altrove: le risolve il giocatore con <see cref="CloudSync.ResolveAsync"/>.</summary>
        public readonly List<SyncItem> Conflicts = new List<SyncItem>();
        /// <summary>La rete non ha risposto: si riprova più tardi.</summary>
        public bool Offline;
        /// <summary>Il server ha chiuso la sessione: serve un nuovo accesso.</summary>
        public bool SignedOut;
        public string LastError;

        public bool Changed => Uploaded + Downloaded + Created + Relinked + Unlinked > 0;
    }

    /// <summary>
    /// Sincronizzazione delle partite con il cloud (docs/06 §5). Chiavi nell'archivio locale:
    /// "saves/&lt;id&gt;/game" (partita), "saves/&lt;id&gt;/meta" (riassunto JSON), "saves/&lt;id&gt;/cloud" (legame col
    /// cloud), "cloud/deleted/&lt;uuid&gt;" (partite eliminate qui da togliere anche dal cloud alla prossima occasione).
    /// Una sincronizzazione alla volta; la partita aperta non viene sovrascritta mentre si gioca.
    /// </summary>
    public sealed class CloudSync
    {
        private readonly ApiClient _api;
        private readonly ISaveStore _store;
        private readonly DeviceIdentity _device;
        private readonly Func<DateTime> _utcNow;
        private readonly object _lock = new object();
        private Task<SyncReport> _running;

        /// <summary>Partita aperta adesso (null nel menu): non si scarica sopra di lei.</summary>
        public Func<string> ActiveSaveId = () => null;

        /// <summary>Identificativo locale per le partite che arrivano dal cloud.</summary>
        public Func<string> NewLocalId = () => DateTime.UtcNow.ToString("yyyyMMddHHmmssfff", CultureInfo.InvariantCulture);

        public Func<string> NewUuid = () => Guid.NewGuid().ToString("D");

        public CloudSync(ApiClient api, ISaveStore store, DeviceIdentity device, Func<DateTime> utcNow = null)
        {
            _api = api;
            _store = store;
            _device = device;
            _utcNow = utcNow ?? (() => DateTime.UtcNow);
        }

        public DateTime LastSyncUtc { get; private set; }

        /// <summary>Sincronizza tutte le partite (se ne è già in corso una, aspetta quella).</summary>
        public Task<SyncReport> SyncAsync(CancellationToken ct = default)
        {
            lock (_lock)
            {
                if (_running != null && !_running.IsCompleted) return _running;
                _running = RunAsync(ct);
                return _running;
            }
        }

        private async Task<SyncReport> RunAsync(CancellationToken ct)
        {
            var report = new SyncReport();
            try
            {
                var deleted = await PurgeDeletedAsync(ct);
                var cloud = await _api.ListSavesAsync(ct);
                cloud.RemoveAll(c => deleted.Contains(c.Uuid));
                var local = await LocalAsync();
                foreach (var item in SyncPlanner.Plan(local, cloud, NewUuid))
                {
                    try
                    {
                        await ExecuteAsync(item, report, ct);
                    }
                    catch (ApiException e) when (e.Status == 409 && e.Code == "conflict")
                    {
                        // qualcuno ha caricato nel frattempo: la versione cloud arriva con l'errore
                        if (!e.Body["cloud"].IsNull) item.Cloud = CloudSave.From(e.Body["cloud"]);
                        item.Action = SyncAction.Conflict;
                        report.Conflicts.Add(item);
                    }
                    catch (ApiException e) when (!e.IsNetwork && !e.IsSessionEnded)
                    {
                        report.Failed++;
                        report.LastError = e.Message;
                    }
                }
                LastSyncUtc = _utcNow();
            }
            catch (ApiException e) when (e.IsNetwork)
            {
                report.Offline = true;
                report.LastError = e.Message;
            }
            catch (ApiException e) when (e.IsSessionEnded)
            {
                report.SignedOut = true;
                report.LastError = e.Message;
            }
            return report;
        }

        private async Task ExecuteAsync(SyncItem item, SyncReport report, CancellationToken ct)
        {
            switch (item.Action)
            {
                case SyncAction.Upload:
                    await UploadAsync(item.LocalId, item.Uuid, item.BaseRevision, ct);
                    report.Uploaded++;
                    break;
                case SyncAction.Download:
                    if (item.LocalId == ActiveSaveId())
                    {
                        report.Deferred++;
                        break;
                    }
                    await DownloadAsync(item.LocalId, item.Cloud, ct);
                    report.Downloaded++;
                    break;
                case SyncAction.DownloadNew:
                    await DownloadAsync(NewLocalId(), item.Cloud, ct);
                    report.Created++;
                    break;
                case SyncAction.Relink:
                    await WriteLinkAsync(item.LocalId, new CloudLink { Uuid = item.Uuid, Revision = item.Cloud.Revision, Checksum = item.Local.Checksum, SyncedAt = _utcNow() });
                    report.Relinked++;
                    break;
                case SyncAction.Conflict:
                    report.Conflicts.Add(item);
                    break;
                case SyncAction.Unlink:
                    await _store.DeleteAsync(LinkKey(item.LocalId));
                    report.Unlinked++;
                    break;
            }
        }

        /// <summary>Risolve un conflitto con la scelta del giocatore.</summary>
        public async Task ResolveAsync(SyncItem conflict, ConflictChoice choice, CancellationToken ct = default)
        {
            switch (choice)
            {
                case ConflictChoice.KeepDevice:
                    await UploadAsync(conflict.LocalId, conflict.Uuid, conflict.Cloud.Revision, ct);
                    break;
                case ConflictChoice.KeepCloud:
                    await DownloadAsync(conflict.LocalId, conflict.Cloud, ct);
                    break;
                default:
                    {
                        // la copia di questo dispositivo diventa una partita nuova (caricata subito), il cloud prende il posto dell'originale
                        string copy = NewLocalId();
                        var game = await _store.ReadAsync(GameKey(conflict.LocalId)) ?? Array.Empty<byte>();
                        var meta = await ReadMetaAsync(conflict.LocalId);
                        string name = meta["name"].AsString("Partita");
                        await _store.WriteAsync(GameKey(copy), game);
                        await _store.WriteAsync(MetaKey(copy), Encoding.UTF8.GetBytes(WithFields(meta, copy, name + " (" + _device.Name + ")")));
                        await DownloadAsync(conflict.LocalId, conflict.Cloud, ct);
                        await UploadAsync(copy, NewUuid(), 0, ct);
                        break;
                    }
            }
        }

        /// <summary>
        /// Da chiamare quando il giocatore elimina una partita: se era nel cloud, viene tolta anche da lì
        /// (subito alla prossima sincronizzazione, anche se adesso si è offline).
        /// </summary>
        public static async Task MarkDeletedAsync(ISaveStore store, string localId)
        {
            var link = await ReadLinkAsync(store, localId);
            await store.DeleteAsync(LinkKey(localId));
            if (link != null && link.Revision > 0) await store.WriteAsync("cloud/deleted/" + link.Uuid, new byte[] { 1 });
        }

        // ------------------------------------------------------------------ passi

        private async Task UploadAsync(string localId, string uuid, int baseRevision, CancellationToken ct)
        {
            var game = await _store.ReadAsync(GameKey(localId));
            if (game == null || game.Length == 0) return;
            var meta = await ReadMetaAsync(localId);
            string checksum = Hashing.Sha256Hex(game);
            // il legame si scrive prima: se l'upload si interrompe, la prossima volta si riusa lo stesso uuid
            var link = await ReadLinkAsync(_store, localId);
            if (link == null || link.Uuid != uuid)
                await WriteLinkAsync(localId, new CloudLink { Uuid = uuid, Revision = 0, Checksum = "", SyncedAt = _utcNow() });
            string name = meta["name"].AsString("Partita");
            var saved = await _api.UploadSaveAsync(uuid, baseRevision, game, name, meta["city"].AsString(name), meta, _device, null, ct);
            await WriteLinkAsync(localId, new CloudLink { Uuid = uuid, Revision = saved.Revision, Checksum = checksum, SyncedAt = _utcNow() });
        }

        private async Task DownloadAsync(string localId, CloudSave cloud, CancellationToken ct)
        {
            var bytes = await _api.DownloadSaveAsync(cloud.Uuid, cloud.Checksum, ct);
            await _store.WriteAsync(GameKey(localId), bytes);
            await _store.WriteAsync(MetaKey(localId), Encoding.UTF8.GetBytes(WithFields(cloud.Meta, localId, cloud.Meta["name"].AsString(cloud.Name))));
            await WriteLinkAsync(localId, new CloudLink { Uuid = cloud.Uuid, Revision = cloud.Revision, Checksum = Hashing.Sha256Hex(bytes), SyncedAt = _utcNow() });
        }

        /// <summary>Elimina dal cloud le partite cancellate qui; restituisce gli uuid da ignorare in questo giro.</summary>
        private async Task<HashSet<string>> PurgeDeletedAsync(CancellationToken ct)
        {
            var uuids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var key in await _store.ListAsync("cloud/deleted/"))
            {
                string uuid = key.Substring("cloud/deleted/".Length);
                uuids.Add(uuid);
                try
                {
                    await _api.DeleteSaveAsync(uuid, ct);
                }
                catch (ApiException e) when (e.Status == 404)
                {
                    // già eliminata altrove
                }
                await _store.DeleteAsync(key);
            }
            return uuids;
        }

        private async Task<List<LocalSaveState>> LocalAsync()
        {
            var list = new List<LocalSaveState>();
            foreach (var key in await _store.ListAsync("saves/"))
            {
                if (!key.EndsWith("/meta", StringComparison.Ordinal)) continue;
                string id = key.Substring("saves/".Length, key.Length - "saves/".Length - "/meta".Length);
                var game = await _store.ReadAsync(GameKey(id));
                if (game == null || game.Length == 0) continue;
                var meta = await ReadMetaAsync(id);
                list.Add(new LocalSaveState { Id = id, Name = meta["name"].AsString(""), Checksum = Hashing.Sha256Hex(game), Link = await ReadLinkAsync(_store, id) });
            }
            return list;
        }

        // ------------------------------------------------------------------ file

        private static string GameKey(string id) => "saves/" + id + "/game";
        private static string MetaKey(string id) => "saves/" + id + "/meta";
        private static string LinkKey(string id) => "saves/" + id + "/cloud";

        private async Task<JsonValue> ReadMetaAsync(string id)
        {
            var bytes = await _store.ReadAsync(MetaKey(id));
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

        /// <summary>Legame col cloud della partita (null se non è collegata).</summary>
        public static async Task<CloudLink> ReadLinkAsync(ISaveStore store, string localId)
        {
            var bytes = await store.ReadAsync(LinkKey(localId));
            if (bytes == null) return null;
            try
            {
                var j = Json.Parse(Encoding.UTF8.GetString(bytes));
                return new CloudLink
                {
                    Uuid = j["uuid"].AsString(""), Revision = j["revision"].AsInt(), Checksum = j["checksum"].AsString(""),
                    SyncedAt = ApiTime.Parse(j["synced"].AsString()),
                };
            }
            catch (JsonException)
            {
                return null;
            }
        }

        private Task WriteLinkAsync(string localId, CloudLink link) =>
            _store.WriteAsync(LinkKey(localId), Encoding.UTF8.GetBytes(new JsonObject
            {
                ["uuid"] = link.Uuid, ["revision"] = link.Revision, ["checksum"] = link.Checksum, ["synced"] = link.SyncedAt,
            }.ToString()));

        /// <summary>Riassunto della partita con l'id locale e il nome indicati (gli altri campi restano com'erano).</summary>
        private static string WithFields(JsonValue meta, string id, string name)
        {
            var o = new JsonObject { ["id"] = id, ["name"] = name };
            foreach (var key in meta.Keys)
                if (key != "id" && key != "name") o[key] = meta[key];
            return o.ToString();
        }
    }
}
