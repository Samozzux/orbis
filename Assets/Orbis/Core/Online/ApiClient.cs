using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Orbis.Core.Data;

namespace Orbis.Core.Online
{
    /// <summary>
    /// Client delle API di Orbis (docs/06 §4–§5): account, sessioni dei dispositivi, salvataggi nel cloud con
    /// l'upload a pezzi. Le chiamate autenticate prendono il token da <see cref="AccessToken"/> e, se il server
    /// risponde 401, chiedono un rinnovo e riprovano una volta. Gli errori diventano <see cref="ApiException"/>.
    /// </summary>
    public sealed class ApiClient
    {
        private readonly IHttpTransport _http;

        /// <summary>Token di accesso valido; con force = true va rinnovato anche se non sembra scaduto (dopo un 401).</summary>
        public Func<bool, CancellationToken, Task<string>> AccessToken;

        /// <summary>Tentativi per ogni pezzo di un upload quando cade la rete.</summary>
        public int ChunkAttempts = 3;

        public ApiClient(IHttpTransport http) => _http = http;

        // ------------------------------------------------------------------ account

        public async Task<JsonValue> HealthAsync(CancellationToken ct = default) => (await JsonAsync("GET", "/api/health", null, false, ct));

        public async Task<TokenSet> RegisterAsync(string username, string password, string displayName, DeviceIdentity device, CancellationToken ct = default)
        {
            var body = new JsonObject { ["username"] = username, ["password"] = password, ["displayName"] = displayName ?? "" };
            device.AddTo(body);
            return TokenSet.From(await JsonAsync("POST", "/api/auth/register", body, false, ct));
        }

        public async Task<TokenSet> LoginAsync(string username, string password, DeviceIdentity device, CancellationToken ct = default)
        {
            var body = new JsonObject { ["username"] = username, ["password"] = password };
            device.AddTo(body);
            return TokenSet.From(await JsonAsync("POST", "/api/auth/login", body, false, ct));
        }

        /// <summary>Rinnovo con rotazione: il refresh token usato non vale più, va salvato quello nuovo.</summary>
        public async Task<TokenSet> RefreshAsync(string refreshToken, CancellationToken ct = default) =>
            TokenSet.From(await JsonAsync("POST", "/api/auth/refresh", new JsonObject { ["refreshToken"] = refreshToken }, false, ct));

        public Task LogoutAsync(string refreshToken, CancellationToken ct = default) =>
            SendAsync("POST", "/api/auth/logout", new JsonObject { ["refreshToken"] = refreshToken }, null, null, false, ct);

        public async Task<AccountUser> MeAsync(CancellationToken ct = default) => AccountUser.From((await JsonAsync("GET", "/api/me", null, true, ct))["user"]);

        public async Task<AccountUser> UpdateDisplayNameAsync(string displayName, CancellationToken ct = default) =>
            AccountUser.From((await JsonAsync("PATCH", "/api/me", new JsonObject { ["displayName"] = displayName }, true, ct))["user"]);

        public async Task<List<DeviceSession>> SessionsAsync(CancellationToken ct = default)
        {
            var list = new List<DeviceSession>();
            foreach (var s in (await JsonAsync("GET", "/api/auth/sessions", null, true, ct))["sessions"].Items) list.Add(DeviceSession.From(s));
            return list;
        }

        public Task RevokeSessionAsync(long id, CancellationToken ct = default) =>
            SendAsync("DELETE", "/api/auth/sessions/" + id.ToString(CultureInfo.InvariantCulture), null, null, null, true, ct);

        // ------------------------------------------------------------------ passkey (versione web)

        /// <summary>Opzioni per creare una passkey (serve l'accesso): {challengeId, publicKey} da passare al browser.</summary>
        public Task<JsonValue> PasskeyRegisterOptionsAsync(CancellationToken ct = default) =>
            JsonAsync("POST", "/api/auth/passkey/register/options", new JsonObject(), true, ct);

        /// <summary>Registra la passkey creata dal browser (credentialId, clientDataJSON, attestationObject in base64url).</summary>
        public async Task<PasskeyInfo> PasskeyRegisterVerifyAsync(string challengeId, JsonValue credential, string name, CancellationToken ct = default)
        {
            var body = new JsonObject
            {
                ["challengeId"] = challengeId, ["credentialId"] = credential["credentialId"].AsString(""),
                ["clientDataJSON"] = credential["clientDataJSON"].AsString(""), ["attestationObject"] = credential["attestationObject"].AsString(""),
                ["name"] = name ?? "",
            };
            return PasskeyInfo.From((await JsonAsync("POST", "/api/auth/passkey/register/verify", body, true, ct))["passkey"]);
        }

        /// <summary>Opzioni per entrare con una passkey (senza nome utente: passkey "discoverable").</summary>
        public Task<JsonValue> PasskeyLoginOptionsAsync(CancellationToken ct = default) =>
            JsonAsync("POST", "/api/auth/passkey/login/options", new JsonObject(), false, ct);

        /// <summary>Entra con la firma della passkey (credentialId, clientDataJSON, authenticatorData, signature, userHandle).</summary>
        public async Task<TokenSet> PasskeyLoginVerifyAsync(string challengeId, JsonValue assertion, DeviceIdentity device, CancellationToken ct = default)
        {
            var body = new JsonObject { ["challengeId"] = challengeId };
            foreach (var key in new[] { "credentialId", "clientDataJSON", "authenticatorData", "signature", "userHandle" })
                body[key] = assertion[key].AsString("");
            device.AddTo(body);
            return TokenSet.From(await JsonAsync("POST", "/api/auth/passkey/login/verify", body, false, ct));
        }

        public async Task<List<PasskeyInfo>> PasskeysAsync(CancellationToken ct = default)
        {
            var list = new List<PasskeyInfo>();
            foreach (var p in (await JsonAsync("GET", "/api/auth/passkeys", null, true, ct))["passkeys"].Items) list.Add(PasskeyInfo.From(p));
            return list;
        }

        public Task DeletePasskeyAsync(long id, CancellationToken ct = default) =>
            SendAsync("DELETE", "/api/auth/passkeys/" + id.ToString(CultureInfo.InvariantCulture), null, null, null, true, ct);

        // ------------------------------------------------------------------ salvataggi

        public async Task<List<CloudSave>> ListSavesAsync(CancellationToken ct = default)
        {
            var list = new List<CloudSave>();
            foreach (var s in (await JsonAsync("GET", "/api/saves", null, true, ct))["saves"].Items) list.Add(CloudSave.From(s));
            return list;
        }

        /// <summary>
        /// Carica un salvataggio a pezzi: apre l'upload con la revisione di partenza, manda i pezzi (ognuno riprovato
        /// se cade la rete), chiude e restituisce i metadati della nuova revisione. Se nel cloud c'è una revisione
        /// diversa da <paramref name="baseRevision"/> lancia un <see cref="ApiException"/> 409 "conflict" con la versione cloud.
        /// </summary>
        public async Task<CloudSave> UploadSaveAsync(string uuid, int baseRevision, byte[] data, string name, string cityName, JsonValue meta,
            DeviceIdentity device, IProgress<float> progress = null, CancellationToken ct = default)
        {
            var body = new JsonObject
            {
                ["baseRevision"] = baseRevision, ["size"] = data.Length, ["checksum"] = Hashing.Sha256Hex(data),
                ["name"] = name ?? "", ["cityName"] = cityName ?? "", ["meta"] = meta ?? (object)new JsonObject(),
            };
            device.AddTo(body);
            string root = "/api/saves/" + uuid;
            var start = await JsonAsync("POST", root + "/upload/start", body, true, ct);
            string uploadId = start["uploadId"].AsString("");
            int chunk = Math.Max(1, start["chunkSize"].AsInt(512 * 1024));
            int count = start["chunkCount"].AsInt((data.Length + chunk - 1) / chunk);
            for (int n = 0; n < count; n++)
            {
                int offset = n * chunk, len = Math.Min(chunk, data.Length - offset);
                var part = new byte[len];
                Buffer.BlockCopy(data, offset, part, 0, len);
                for (int attempt = 1; ; attempt++)
                {
                    try
                    {
                        await SendAsync("PUT", root + "/upload/" + uploadId + "/chunk/" + n.ToString(CultureInfo.InvariantCulture), null, part,
                            "application/octet-stream", true, ct);
                        break;
                    }
                    catch (ApiException e) when (e.IsNetwork && attempt < ChunkAttempts)
                    {
                        // la rete è caduta a metà: si rimanda lo stesso pezzo (il server lo sostituisce)
                    }
                }
                progress?.Report((n + 1f) / count);
            }
            return CloudSave.From((await JsonAsync("POST", root + "/upload/" + uploadId + "/finish", new JsonObject(), true, ct))["save"]);
        }

        /// <summary>Scarica il salvataggio; se arriva un checksum diverso da quello atteso riprova una volta, poi errore 422.</summary>
        public async Task<byte[]> DownloadSaveAsync(string uuid, string expectedChecksum, CancellationToken ct = default)
        {
            for (int attempt = 0; ; attempt++)
            {
                var r = await SendAsync("GET", "/api/saves/" + uuid + "/download", null, null, null, true, ct);
                if (string.IsNullOrEmpty(expectedChecksum) || string.Equals(Hashing.Sha256Hex(r.Body), expectedChecksum, StringComparison.OrdinalIgnoreCase))
                    return r.Body;
                if (attempt >= 1) throw new ApiException(422, "checksum_mismatch", "Il salvataggio scaricato è rovinato: riprova.");
            }
        }

        public Task DeleteSaveAsync(string uuid, CancellationToken ct = default) =>
            SendAsync("DELETE", "/api/saves/" + uuid, null, null, null, true, ct);

        // ------------------------------------------------------------------ trasporto

        private async Task<JsonValue> JsonAsync(string method, string path, JsonObject body, bool auth, CancellationToken ct)
        {
            var r = await SendAsync(method, path, body, null, null, auth, ct);
            if (r.Body == null || r.Body.Length == 0) return JsonValue.Null;
            try
            {
                return Json.Parse(Encoding.UTF8.GetString(r.Body));
            }
            catch (JsonException)
            {
                throw new ApiException(r.Status, "invalid_response", "Risposta del server non valida.");
            }
        }

        private async Task<ApiResponse> SendAsync(string method, string path, JsonObject json, byte[] raw, string rawType, bool auth, CancellationToken ct)
        {
            ApiRequest Build(string token)
            {
                var req = new ApiRequest { Method = method, Path = path };
                if (json != null)
                {
                    req.Body = Encoding.UTF8.GetBytes(json.ToString());
                    req.ContentType = "application/json";
                }
                else if (raw != null)
                {
                    req.Body = raw;
                    req.ContentType = rawType ?? "application/octet-stream";
                }
                if (token != null) req.Headers["Authorization"] = "Bearer " + token;
                return req;
            }

            string access = null;
            if (auth)
            {
                if (AccessToken == null) throw new ApiException(401, "unauthorized", "Serve l'accesso.");
                access = await AccessToken(false, ct);
            }
            var resp = await _http.SendAsync(Build(access), ct);
            if (resp.Status == 0) throw ApiException.Network(resp.NetworkError);
            if (auth && resp.Status == 401)
            {
                // token scaduto o revocato: un rinnovo e un secondo tentativo
                access = await AccessToken(true, ct);
                resp = await _http.SendAsync(Build(access), ct);
                if (resp.Status == 0) throw ApiException.Network(resp.NetworkError);
            }
            if (resp.Status >= 400) throw Error(resp);
            return resp;
        }

        private static ApiException Error(ApiResponse r)
        {
            JsonValue body = JsonValue.Null;
            try
            {
                if (r.Body != null && r.Body.Length > 0) body = Json.Parse(Encoding.UTF8.GetString(r.Body));
            }
            catch (JsonException)
            {
                body = JsonValue.Null;
            }
            var e = body["error"];
            int.TryParse(r.Header("Retry-After") ?? "", NumberStyles.Integer, CultureInfo.InvariantCulture, out int retry);
            return new ApiException(r.Status, e["code"].AsString("http_" + r.Status.ToString(CultureInfo.InvariantCulture)),
                e["message"].AsString("Errore del server (" + r.Status.ToString(CultureInfo.InvariantCulture) + ")."), body, retry);
        }
    }

    public static class Hashing
    {
        public static string Sha256Hex(byte[] data)
        {
            using (var sha = System.Security.Cryptography.SHA256.Create())
            {
                var h = sha.ComputeHash(data ?? Array.Empty<byte>());
                var sb = new StringBuilder(64);
                foreach (var b in h) sb.Append(b.ToString("x2", CultureInfo.InvariantCulture));
                return sb.ToString();
            }
        }
    }
}
