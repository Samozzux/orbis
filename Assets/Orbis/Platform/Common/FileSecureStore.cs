using System;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Orbis.Core.Persistence;

namespace Orbis.Platform
{
    /// <summary>
    /// Archivio "protetto" su file nella cartella dati dell'app: editor, desktop e web (dove la cartella sta in
    /// IndexedDB, legata al sito). È il ripiego della specifica per il web (docs/06 §4: sessione lunga col
    /// refresh token in IndexedDB) finché non arrivano le passkey.
    /// </summary>
    public sealed class FileSecureStore : ISecureStore
    {
        private static readonly Regex Unsafe = new Regex("[^A-Za-z0-9_-]");
        private readonly ISaveStore _files;

        public FileSecureStore(string directory, Action afterWrite = null) => _files = new FileSaveStore(directory, afterWrite);

        public bool IsPersistent => true;

        private static string KeyOf(string key) => Unsafe.Replace(key ?? "", "_");

        public Task SetAsync(string key, string value) => _files.WriteAsync(KeyOf(key), Encoding.UTF8.GetBytes(value ?? ""));

        public async Task<string> GetAsync(string key)
        {
            var bytes = await _files.ReadAsync(KeyOf(key));
            return bytes == null ? null : Encoding.UTF8.GetString(bytes);
        }

        public Task DeleteAsync(string key) => _files.DeleteAsync(KeyOf(key));
    }
}
