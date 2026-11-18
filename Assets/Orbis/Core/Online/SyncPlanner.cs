using System;
using System.Collections.Generic;

namespace Orbis.Core.Online
{
    /// <summary>Legame di una partita locale con il cloud (file "saves/&lt;id&gt;/cloud").</summary>
    public sealed class CloudLink
    {
        public string Uuid = "";
        /// <summary>Revisione del cloud da cui parte la copia locale (0 = mai caricata).</summary>
        public int Revision;
        /// <summary>SHA-256 della partita locale all'ultima sincronizzazione: se cambia, la partita è stata giocata qui.</summary>
        public string Checksum = "";
        public DateTime SyncedAt;
    }

    /// <summary>Stato di una partita locale visto dalla sincronizzazione.</summary>
    public sealed class LocalSaveState
    {
        public string Id = "";
        public string Name = "";
        /// <summary>SHA-256 della partita com'è adesso.</summary>
        public string Checksum = "";
        public CloudLink Link;
    }

    public enum SyncAction
    {
        /// <summary>Uguale nei due posti.</summary>
        None,
        /// <summary>Giocata solo qui: si carica (revisione di partenza = <see cref="SyncItem.BaseRevision"/>).</summary>
        Upload,
        /// <summary>Giocata solo altrove: si scarica sopra quella locale.</summary>
        Download,
        /// <summary>C'è solo nel cloud (iniziata su un altro dispositivo): si scarica come partita nuova.</summary>
        DownloadNew,
        /// <summary>Stesso contenuto ma revisione diversa (es. scaricata a mano): si aggiorna solo il legame.</summary>
        Relink,
        /// <summary>Giocata sia qui sia altrove: decide il giocatore.</summary>
        Conflict,
        /// <summary>Era nel cloud ed è stata eliminata da un altro dispositivo: resta solo qui, senza legame.</summary>
        Unlink,
    }

    public sealed class SyncItem
    {
        public SyncAction Action;
        /// <summary>Partita locale (vuoto per <see cref="SyncAction.DownloadNew"/>).</summary>
        public string LocalId = "";
        public string Uuid = "";
        public int BaseRevision;
        public CloudSave Cloud;
        public LocalSaveState Local;

        public override string ToString() => Action + " " + (LocalId.Length > 0 ? LocalId : Uuid);
    }

    /// <summary>
    /// Decide cosa fare per ogni partita confrontando le copie locali con il cloud (docs/06 §5):
    /// giocata solo qui → carica; solo altrove → scarica; in entrambi i posti → conflitto da far scegliere
    /// (Tieni questo dispositivo, Tieni il cloud, Tienili entrambi); solo nel cloud → scarica come nuova.
    /// Le partite mai collegate si caricano come nuove (uuid nuovo). Pura logica: niente rete, niente file.
    /// </summary>
    public static class SyncPlanner
    {
        public static List<SyncItem> Plan(IReadOnlyList<LocalSaveState> local, IReadOnlyList<CloudSave> cloud, Func<string> newUuid)
        {
            var byUuid = new Dictionary<string, CloudSave>(StringComparer.OrdinalIgnoreCase);
            foreach (var c in cloud) byUuid[c.Uuid] = c;
            var linked = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var plan = new List<SyncItem>();
            foreach (var l in local)
            {
                var link = l.Link;
                if (link == null || string.IsNullOrEmpty(link.Uuid))
                {
                    plan.Add(new SyncItem { Action = SyncAction.Upload, LocalId = l.Id, Uuid = newUuid(), BaseRevision = 0, Local = l });
                    continue;
                }
                linked.Add(link.Uuid);
                bool localChanged = !string.Equals(l.Checksum, link.Checksum, StringComparison.OrdinalIgnoreCase);
                if (!byUuid.TryGetValue(link.Uuid, out var c))
                {
                    // mai arrivata nel cloud (upload interrotto) → si riprova; eliminata altrove → resta solo qui
                    plan.Add(link.Revision == 0
                        ? new SyncItem { Action = SyncAction.Upload, LocalId = l.Id, Uuid = link.Uuid, BaseRevision = 0, Local = l }
                        : new SyncItem { Action = SyncAction.Unlink, LocalId = l.Id, Uuid = link.Uuid, Local = l });
                    continue;
                }
                bool cloudChanged = c.Revision != link.Revision;
                var item = new SyncItem { LocalId = l.Id, Uuid = link.Uuid, Cloud = c, Local = l, BaseRevision = link.Revision };
                if (string.Equals(c.Checksum, l.Checksum, StringComparison.OrdinalIgnoreCase))
                    item.Action = cloudChanged || localChanged ? SyncAction.Relink : SyncAction.None;
                else if (!cloudChanged) item.Action = localChanged ? SyncAction.Upload : SyncAction.None;
                else item.Action = localChanged ? SyncAction.Conflict : SyncAction.Download;
                plan.Add(item);
            }
            foreach (var c in cloud)
                if (!linked.Contains(c.Uuid))
                    plan.Add(new SyncItem { Action = SyncAction.DownloadNew, Uuid = c.Uuid, Cloud = c, BaseRevision = c.Revision });
            return plan;
        }
    }
}
