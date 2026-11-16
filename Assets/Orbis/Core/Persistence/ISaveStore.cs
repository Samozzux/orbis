using System.Collections.Generic;
using System.Threading.Tasks;

namespace Orbis.Core.Persistence
{
    /// <summary>
    /// Archivio locale dei salvataggi (docs/01 §7). Implementazioni per piattaforma in Orbis.Platform:
    /// SQLite su iOS/Android, IndexedDB sul web (da M2). Le chiavi sono percorsi logici, es. "saves/abc/state".
    /// </summary>
    public interface ISaveStore
    {
        Task<bool> ExistsAsync(string key);

        /// <summary>Restituisce null se la chiave non esiste.</summary>
        Task<byte[]> ReadAsync(string key);

        Task WriteAsync(string key, byte[] data);

        Task DeleteAsync(string key);

        /// <summary>Chiavi che iniziano con <paramref name="prefix"/>, in ordine alfabetico.</summary>
        Task<IReadOnlyList<string>> ListAsync(string prefix);
    }
}
