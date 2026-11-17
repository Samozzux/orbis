using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Orbis.Core.Persistence;

namespace Orbis.Platform
{
    /// <summary>
    /// Archivio su file nella cartella dati dell'app (docs/01 §7, decisione di M2 in STATO: file binari
    /// versionati invece di SQLite). Sul web la cartella è il file system IDBFS di Unity, salvato in IndexedDB
    /// dopo ogni scrittura (<see cref="_afterWrite"/>).
    /// </summary>
    public sealed class FileSaveStore : ISaveStore
    {
        private static readonly Regex ValidKey = new Regex(@"^[A-Za-z0-9_\-]+(/[A-Za-z0-9_\-.]+)*$");
        private readonly string _root;
        private readonly Action _afterWrite;

        public FileSaveStore(string rootDirectory, Action afterWrite = null)
        {
            if (string.IsNullOrEmpty(rootDirectory)) throw new ArgumentException("Cartella obbligatoria.", nameof(rootDirectory));
            _root = Path.GetFullPath(rootDirectory);
            _afterWrite = afterWrite;
        }

        public Task<bool> ExistsAsync(string key) => Task.FromResult(File.Exists(PathOf(key)));

        public Task<byte[]> ReadAsync(string key)
        {
            var path = PathOf(key);
            return Task.FromResult(File.Exists(path) ? File.ReadAllBytes(path) : null);
        }

        public Task WriteAsync(string key, byte[] data)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            var path = PathOf(key);
            Directory.CreateDirectory(Path.GetDirectoryName(path) ?? _root);
            // scrittura atomica: prima un file temporaneo, poi lo scambio
            var tmp = path + ".tmp";
            File.WriteAllBytes(tmp, data);
            if (File.Exists(path)) File.Delete(path);
            File.Move(tmp, path);
            _afterWrite?.Invoke();
            return Task.CompletedTask;
        }

        public Task DeleteAsync(string key)
        {
            var path = PathOf(key);
            if (File.Exists(path)) File.Delete(path);
            _afterWrite?.Invoke();
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<string>> ListAsync(string prefix)
        {
            prefix = prefix ?? string.Empty;
            IReadOnlyList<string> keys = Directory.Exists(_root)
                ? Directory.GetFiles(_root, "*", SearchOption.AllDirectories)
                    .Where(p => !p.EndsWith(".tmp", StringComparison.Ordinal))
                    .Select(p => p.Substring(_root.Length).TrimStart(Path.DirectorySeparatorChar).Replace(Path.DirectorySeparatorChar, '/'))
                    .Where(k => k.StartsWith(prefix, StringComparison.Ordinal))
                    .OrderBy(k => k, StringComparer.Ordinal)
                    .ToList()
                : new List<string>();
            return Task.FromResult(keys);
        }

        private string PathOf(string key)
        {
            if (string.IsNullOrEmpty(key) || !ValidKey.IsMatch(key) || key.Contains(".."))
                throw new ArgumentException($"Chiave non valida: '{key}'", nameof(key));
            return Path.Combine(_root, key.Replace('/', Path.DirectorySeparatorChar));
        }
    }
}
