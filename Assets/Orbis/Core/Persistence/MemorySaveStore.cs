using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Orbis.Core.Persistence
{
    /// <summary>Archivio in memoria: per i test e come ripiego dove non c'è ancora l'archivio vero.</summary>
    public sealed class MemorySaveStore : ISaveStore
    {
        private readonly Dictionary<string, byte[]> _data = new Dictionary<string, byte[]>(StringComparer.Ordinal);

        public Task<bool> ExistsAsync(string key)
        {
            Validate(key);
            return Task.FromResult(_data.ContainsKey(key));
        }

        public Task<byte[]> ReadAsync(string key)
        {
            Validate(key);
            return Task.FromResult(_data.TryGetValue(key, out var bytes) ? (byte[])bytes.Clone() : null);
        }

        public Task WriteAsync(string key, byte[] data)
        {
            Validate(key);
            if (data == null) throw new ArgumentNullException(nameof(data));
            _data[key] = (byte[])data.Clone();
            return Task.CompletedTask;
        }

        public Task DeleteAsync(string key)
        {
            Validate(key);
            _data.Remove(key);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<string>> ListAsync(string prefix)
        {
            prefix = prefix ?? string.Empty;
            IReadOnlyList<string> keys = _data.Keys
                .Where(k => k.StartsWith(prefix, StringComparison.Ordinal))
                .OrderBy(k => k, StringComparer.Ordinal)
                .ToList();
            return Task.FromResult(keys);
        }

        private static void Validate(string key)
        {
            if (string.IsNullOrWhiteSpace(key))
                throw new ArgumentException("Chiave vuota.", nameof(key));
        }
    }
}
