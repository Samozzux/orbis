using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Orbis.Core.Online
{
    /// <summary>Richiesta HTTP verso il backend (percorso relativo alla base delle API, es. "/api/saves").</summary>
    public sealed class ApiRequest
    {
        public string Method = "GET";
        public string Path = "/";
        public readonly Dictionary<string, string> Headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        public byte[] Body;
        public string ContentType;
    }

    /// <summary>Risposta HTTP. <see cref="Status"/> = 0 se la rete non ha risposto (offline, timeout, DNS).</summary>
    public sealed class ApiResponse
    {
        public int Status;
        public readonly Dictionary<string, string> Headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        public byte[] Body = Array.Empty<byte>();
        /// <summary>Descrizione dell'errore di rete quando <see cref="Status"/> è 0.</summary>
        public string NetworkError;

        public string Header(string name) => Headers.TryGetValue(name, out var v) ? v : null;
    }

    /// <summary>
    /// Trasporto HTTP della piattaforma (UnityWebRequest nel gioco, un server finto nei test). Non lancia
    /// eccezioni per gli errori di rete: restituisce <see cref="ApiResponse.Status"/> = 0.
    /// </summary>
    public interface IHttpTransport
    {
        Task<ApiResponse> SendAsync(ApiRequest request, CancellationToken ct);
    }
}
