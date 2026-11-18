using System;
using System.Threading;
using System.Threading.Tasks;
using Orbis.Core.Online;
using UnityEngine.Networking;

namespace Orbis.Net
{
    /// <summary>
    /// Trasporto HTTP con UnityWebRequest (uguale su iOS, Android e web): asincrono senza thread, così funziona
    /// anche nel browser. Gli errori di rete diventano Status = 0, quelli del server restano col loro codice.
    /// </summary>
    public sealed class UnityHttpTransport : IHttpTransport
    {
        private readonly string _base;

        public int TimeoutSeconds = 30;

        /// <param name="baseUrl">es. "https://giuliaa8.altervista.org/orbis" (i percorsi iniziano con "/api/…")</param>
        public UnityHttpTransport(string baseUrl) => _base = baseUrl.TrimEnd('/');

        public Task<ApiResponse> SendAsync(ApiRequest request, CancellationToken ct)
        {
            var tcs = new TaskCompletionSource<ApiResponse>();
            var uwr = new UnityWebRequest(_base + request.Path, request.Method) { downloadHandler = new DownloadHandlerBuffer(), timeout = TimeoutSeconds };
            if (request.Body != null)
            {
                uwr.uploadHandler = new UploadHandlerRaw(request.Body) { contentType = request.ContentType ?? "application/octet-stream" };
                uwr.SetRequestHeader("Content-Type", request.ContentType ?? "application/octet-stream");
            }
            foreach (var h in request.Headers) uwr.SetRequestHeader(h.Key, h.Value);
            var registration = ct.CanBeCanceled ? ct.Register(() => uwr.Abort()) : default;
            UnityWebRequestAsyncOperation op;
            try
            {
                op = uwr.SendWebRequest();
            }
            catch (Exception e)
            {
                registration.Dispose();
                uwr.Dispose();
                return Task.FromResult(new ApiResponse { Status = 0, NetworkError = e.Message });
            }
            op.completed += _ =>
            {
                registration.Dispose();
                var resp = new ApiResponse();
                if (uwr.result == UnityWebRequest.Result.ConnectionError || uwr.responseCode == 0)
                {
                    resp.Status = 0;
                    resp.NetworkError = uwr.error;
                }
                else
                {
                    resp.Status = (int)uwr.responseCode;
                    var headers = uwr.GetResponseHeaders();
                    if (headers != null)
                        foreach (var kv in headers) resp.Headers[kv.Key] = kv.Value;
                    resp.Body = uwr.downloadHandler?.data ?? Array.Empty<byte>();
                }
                uwr.Dispose();
                tcs.TrySetResult(resp);
            };
            return tcs.Task;
        }
    }
}
