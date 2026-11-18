using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Orbis.Core.Data;
using Orbis.Core.Online;

namespace Orbis.Core.Tests
{
    /// <summary>
    /// Backend finto in memoria con le stesse regole di quello PHP (docs/06 §4–§5): token di accesso con scadenza,
    /// refresh token ruotati (riuso → sessione del dispositivo chiusa), salvataggi con revisioni e conflitto 409,
    /// upload a pezzi. Simula anche la rete che manca o che perde dei pezzi.
    /// </summary>
    public sealed class FakeBackend : IHttpTransport
    {
        private sealed class User
        {
            public long Id;
            public string Username, Password, DisplayName;
        }

        private sealed class Refresh
        {
            public long UserId;
            public string DeviceId;
            public bool Revoked;
        }

        private sealed class Save
        {
            public int Revision;
            public byte[] Data;
            public string Name, City, DeviceId, DeviceName, Meta;
        }

        private sealed class Upload
        {
            public long UserId;
            public string Uuid, Name, City, DeviceId, DeviceName, Meta, Checksum;
            public int Base, Size, Count;
            public readonly Dictionary<int, byte[]> Chunks = new Dictionary<int, byte[]>();
        }

        private readonly Dictionary<string, User> _users = new Dictionary<string, User>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, Refresh> _refresh = new Dictionary<string, Refresh>();
        private readonly Dictionary<string, (long user, string device, DateTime exp)> _access = new Dictionary<string, (long, string, DateTime)>();
        private readonly Dictionary<(long, string), Save> _saves = new Dictionary<(long, string), Save>();
        private readonly Dictionary<string, Upload> _uploads = new Dictionary<string, Upload>();
        private int _next = 1;

        public Func<DateTime> Now = () => DateTime.UtcNow;
        public int ChunkSize = 16;
        public bool Offline;
        /// <summary>Quanti dei prossimi pezzi "si perdono" per strada (risposta senza rete).</summary>
        public int DropChunks;
        public int RefreshCalls;
        public int Requests;

        public int SaveCount(long userId) => _saves.Keys.Count(k => k.Item1 == userId);
        public byte[] SaveData(long userId, string uuid) => _saves.TryGetValue((userId, uuid), out var s) ? s.Data : null;
        public int SaveRevision(long userId, string uuid) => _saves.TryGetValue((userId, uuid), out var s) ? s.Revision : 0;

        /// <summary>Fa scadere tutti i token di accesso (il prossimo uso risponde 401).</summary>
        public void ExpireAccessTokens() => _access.Clear();

        public async Task<ApiResponse> SendAsync(ApiRequest req, CancellationToken ct)
        {
            // come la rete vera: la risposta arriva dopo (le chiamate concorrenti si sovrappongono)
            await Task.Yield();
            Requests++;
            if (Offline) return new ApiResponse { Status = 0, NetworkError = "offline" };
            try
            {
                return Route(req);
            }
            catch (Fail f)
            {
                var body = new JsonObject { ["error"] = new JsonObject { ["code"] = f.Code, ["message"] = f.Message } };
                if (f.Extra != null) foreach (var kv in f.Extra) body[kv.Key] = kv.Value;
                return Json(f.Status, body);
            }
        }

        private sealed class Fail : Exception
        {
            public readonly int Status;
            public readonly string Code;
            public readonly JsonObject Extra;

            public Fail(int status, string code, JsonObject extra = null) : base(code)
            {
                Status = status;
                Code = code;
                Extra = extra;
            }
        }

        private ApiResponse Route(ApiRequest r)
        {
            var body = r.ContentType == "application/json" && r.Body != null ? Orbis.Core.Data.Json.Parse(Encoding.UTF8.GetString(r.Body)) : JsonValue.Null;
            var p = r.Path.Split('/');
            switch (r.Method + " " + r.Path)
            {
                case "POST /api/auth/register":
                    {
                        string name = body["username"].AsString("");
                        if (_users.ContainsKey(name)) throw new Fail(409, "username_taken");
                        var u = new User { Id = _next++, Username = name, Password = body["password"].AsString(""), DisplayName = body["displayName"].AsString(name) };
                        _users[name] = u;
                        return Json(201, Tokens(u, body["deviceId"].AsString("")));
                    }
                case "POST /api/auth/login":
                    {
                        if (!_users.TryGetValue(body["username"].AsString(""), out var u) || u.Password != body["password"].AsString(""))
                            throw new Fail(401, "invalid_credentials");
                        return Json(200, Tokens(u, body["deviceId"].AsString("")));
                    }
                case "POST /api/auth/refresh":
                    {
                        RefreshCalls++;
                        string t = body["refreshToken"].AsString("");
                        if (!_refresh.TryGetValue(t, out var rt)) throw new Fail(401, "invalid_refresh");
                        if (rt.Revoked)
                        {
                            // riuso di un token già ruotato: si chiude tutta la sessione del dispositivo
                            foreach (var other in _refresh.Values)
                                if (other.UserId == rt.UserId && other.DeviceId == rt.DeviceId) other.Revoked = true;
                            throw new Fail(401, "refresh_reused");
                        }
                        rt.Revoked = true;
                        var u = _users.Values.First(x => x.Id == rt.UserId);
                        return Json(200, Tokens(u, rt.DeviceId));
                    }
                case "POST /api/auth/passkey/login/options":
                    return Json(200, new JsonObject { ["challengeId"] = "c" + _next++, ["publicKey"] = new JsonObject { ["challenge"] = "sfida", ["rpId"] = "samozzux.github.io" } });
                case "POST /api/auth/passkey/login/verify":
                    {
                        // nel finto basta che il browser abbia "firmato" la sfida per un utente registrato
                        var u = _users.Values.FirstOrDefault(x => "cred-" + x.Username == body["credentialId"].AsString(""));
                        if (u == null || body["signature"].AsString("") != "firma") throw new Fail(401, "invalid_passkey");
                        return Json(200, Tokens(u, body["deviceId"].AsString("")));
                    }
                case "POST /api/auth/logout":
                    if (_refresh.TryGetValue(body["refreshToken"].AsString(""), out var lt)) lt.Revoked = true;
                    return new ApiResponse { Status = 204 };
                case "GET /api/me":
                    return Json(200, new JsonObject { ["user"] = PublicUser(Authorize(r)) });
                case "GET /api/saves":
                    {
                        var u = Authorize(r);
                        var list = new List<object>();
                        foreach (var kv in _saves.Where(k => k.Key.Item1 == u.Id)) list.Add(Meta(kv.Key.Item2, kv.Value));
                        return Json(200, new JsonObject { ["saves"] = list });
                    }
            }
            if (p.Length >= 4 && p[1] == "api" && p[2] == "saves")
            {
                var u = Authorize(r);
                string uuid = p[3];
                _saves.TryGetValue((u.Id, uuid), out var current);
                if (r.Method == "POST" && p.Length == 6 && p[5] == "start")
                {
                    int b = body["baseRevision"].AsInt(-1);
                    if ((current?.Revision ?? 0) != b) throw Conflict(uuid, current);
                    var up = new Upload
                    {
                        UserId = u.Id, Uuid = uuid, Base = b, Size = body["size"].AsInt(), Checksum = body["checksum"].AsString(""),
                        Name = body["name"].AsString(""), City = body["cityName"].AsString(""), DeviceId = body["deviceId"].AsString(""),
                        DeviceName = body["deviceName"].AsString(""), Meta = JsonText.Of(body["meta"]),
                    };
                    up.Count = (up.Size + ChunkSize - 1) / ChunkSize;
                    string id = Guid.NewGuid().ToString("N");
                    _uploads[id] = up;
                    return Json(201, new JsonObject { ["uploadId"] = id, ["chunkSize"] = ChunkSize, ["chunkCount"] = up.Count });
                }
                if (r.Method == "PUT" && p.Length == 8 && p[6] == "chunk")
                {
                    if (DropChunks > 0)
                    {
                        DropChunks--;
                        return new ApiResponse { Status = 0, NetworkError = "connessione persa" };
                    }
                    var up = _uploads[p[5]];
                    up.Chunks[int.Parse(p[7], CultureInfo.InvariantCulture)] = r.Body;
                    return new ApiResponse { Status = 204 };
                }
                if (r.Method == "POST" && p.Length == 7 && p[6] == "finish")
                {
                    var up = _uploads[p[5]];
                    if (up.Chunks.Count != up.Count) throw new Fail(409, "upload_incomplete");
                    var data = Enumerable.Range(0, up.Count).SelectMany(i => up.Chunks[i]).ToArray();
                    if (Hashing.Sha256Hex(data) != up.Checksum) throw new Fail(422, "checksum_mismatch");
                    if ((current?.Revision ?? 0) != up.Base) throw Conflict(uuid, current);
                    var s = new Save { Revision = up.Base + 1, Data = data, Name = up.Name, City = up.City, DeviceId = up.DeviceId, DeviceName = up.DeviceName, Meta = up.Meta };
                    _saves[(u.Id, uuid)] = s;
                    _uploads.Remove(p[5]);
                    return Json(200, new JsonObject { ["save"] = Meta(uuid, s) });
                }
                if (r.Method == "GET" && p.Length == 5 && p[4] == "download")
                {
                    if (current == null) throw new Fail(404, "not_found");
                    var resp = new ApiResponse { Status = 200, Body = current.Data };
                    resp.Headers["X-Orbis-Revision"] = current.Revision.ToString(CultureInfo.InvariantCulture);
                    return resp;
                }
                if (r.Method == "DELETE" && p.Length == 4)
                {
                    if (current == null) throw new Fail(404, "not_found");
                    _saves.Remove((u.Id, uuid));
                    return new ApiResponse { Status = 204 };
                }
            }
            throw new Fail(404, "not_found");
        }

        private Fail Conflict(string uuid, Save current) =>
            new Fail(409, "conflict", new JsonObject { ["cloud"] = current == null ? null : (object)Meta(uuid, current) });

        private User Authorize(ApiRequest r)
        {
            if (!r.Headers.TryGetValue("Authorization", out var h) || !h.StartsWith("Bearer ", StringComparison.Ordinal)) throw new Fail(401, "unauthorized");
            if (!_access.TryGetValue(h.Substring(7), out var a) || a.exp <= Now()) throw new Fail(401, "invalid_token");
            return _users.Values.First(u => u.Id == a.user);
        }

        private JsonObject Tokens(User u, string device)
        {
            string access = "acc-" + _next++, refresh = "ref-" + _next++;
            _access[access] = (u.Id, device, Now().AddHours(1));
            _refresh[refresh] = new Refresh { UserId = u.Id, DeviceId = device };
            return new JsonObject
            {
                ["accessToken"] = access, ["accessExpiresIn"] = 3600, ["refreshToken"] = refresh,
                ["refreshExpiresAt"] = Now().AddDays(90), ["user"] = PublicUser(u),
            };
        }

        private static JsonObject PublicUser(User u) => new JsonObject { ["id"] = u.Id, ["username"] = u.Username, ["displayName"] = u.DisplayName, ["createdAt"] = "2026-11-18T10:00:00Z" };

        private JsonObject Meta(string uuid, Save s) => new JsonObject
        {
            ["uuid"] = uuid, ["name"] = s.Name, ["cityName"] = s.City, ["revision"] = s.Revision, ["deviceId"] = s.DeviceId, ["deviceName"] = s.DeviceName,
            ["sizeBytes"] = s.Data.Length, ["checksum"] = Hashing.Sha256Hex(s.Data), ["updatedAt"] = Now(), ["meta"] = Orbis.Core.Data.Json.Parse(s.Meta),
        };

        private static ApiResponse Json(int status, JsonObject body) =>
            new ApiResponse { Status = status, Body = Encoding.UTF8.GetBytes(body.ToString()) };
    }

    /// <summary>Archivio del refresh token in memoria (al posto del Keychain).</summary>
    public sealed class MemoryTokenStore : IRefreshTokenStore
    {
        public string Token;

        public Task<string> LoadAsync(CancellationToken ct) => Task.FromResult(Token);

        public Task SaveAsync(string token, CancellationToken ct)
        {
            Token = token;
            return Task.CompletedTask;
        }

        public Task ClearAsync(CancellationToken ct)
        {
            Token = null;
            return Task.CompletedTask;
        }
    }
}
