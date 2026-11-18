using System;
using System.Globalization;
using Orbis.Core.Data;

namespace Orbis.Core.Online
{
    /// <summary>Errore del backend ({error:{code,message}}) o della rete (<see cref="IsNetwork"/>).</summary>
    public sealed class ApiException : Exception
    {
        public readonly int Status;
        public readonly string Code;
        /// <summary>Tutta la risposta (es. "cloud" con i metadati della versione nel cloud in un conflitto 409).</summary>
        public readonly JsonValue Body;
        /// <summary>Secondi da aspettare prima di riprovare (429), 0 se non indicati.</summary>
        public readonly int RetryAfter;

        public ApiException(int status, string code, string message, JsonValue body = null, int retryAfter = 0) : base(message)
        {
            Status = status;
            Code = code ?? "";
            Body = body ?? JsonValue.Null;
            RetryAfter = retryAfter;
        }

        /// <summary>La rete non ha risposto: il gioco continua offline e riprova più tardi.</summary>
        public bool IsNetwork => Status == 0;

        /// <summary>Il rinnovo è stato rifiutato (token scaduto, revocato o riusato): bisogna rientrare con la password.</summary>
        public bool IsSessionEnded => Status == 401;

        public static ApiException Network(string detail) => new ApiException(0, "network", "Connessione non disponibile" + (string.IsNullOrEmpty(detail) ? "." : ": " + detail));
    }

    /// <summary>Profilo pubblico dell'utente.</summary>
    public sealed class AccountUser
    {
        public long Id;
        public string Username = "";
        public string DisplayName = "";
        public DateTime CreatedAt;

        public static AccountUser From(JsonValue j) => new AccountUser
        {
            Id = j["id"].AsLong(),
            Username = j["username"].AsString(""),
            DisplayName = j["displayName"].AsString(""),
            CreatedAt = ApiTime.Parse(j["createdAt"].AsString()),
        };

        public JsonObject ToJson() => new JsonObject { ["id"] = Id, ["username"] = Username, ["displayName"] = DisplayName, ["createdAt"] = CreatedAt };
    }

    /// <summary>Token restituiti da registrazione, accesso e rinnovo.</summary>
    public sealed class TokenSet
    {
        public string AccessToken = "";
        public int AccessExpiresIn;
        public string RefreshToken = "";
        public DateTime RefreshExpiresAt;
        public AccountUser User;

        public static TokenSet From(JsonValue j) => new TokenSet
        {
            AccessToken = j["accessToken"].AsString(""),
            AccessExpiresIn = j["accessExpiresIn"].AsInt(3600),
            RefreshToken = j["refreshToken"].AsString(""),
            RefreshExpiresAt = ApiTime.Parse(j["refreshExpiresAt"].AsString()),
            User = AccountUser.From(j["user"]),
        };
    }

    /// <summary>Dispositivo collegato all'account (elenco delle sessioni).</summary>
    public sealed class DeviceSession
    {
        public long Id;
        public string DeviceName = "";
        public string Platform = "";
        /// <summary>Ultimo rinnovo della sessione (l'ultima volta che il dispositivo è entrato).</summary>
        public DateTime LastUsedAt;
        public DateTime ExpiresAt;
        /// <summary>È la sessione di questo dispositivo.</summary>
        public bool Current;

        public static DeviceSession From(JsonValue j) => new DeviceSession
        {
            Id = j["id"].AsLong(),
            DeviceName = j["deviceName"].AsString(""),
            Platform = j["platform"].AsString(""),
            LastUsedAt = ApiTime.Parse(j["lastUsedAt"].AsString()),
            ExpiresAt = ApiTime.Parse(j["expiresAt"].AsString()),
            Current = j["current"].AsBool(),
        };
    }

    /// <summary>Metadati di un salvataggio nel cloud.</summary>
    public sealed class CloudSave
    {
        public string Uuid = "";
        public string Name = "";
        public string CityName = "";
        public int Revision;
        public string DeviceId = "";
        public string DeviceName = "";
        public long SizeBytes;
        public string Checksum = "";
        public DateTime UpdatedAt;
        /// <summary>Dati descrittivi del gioco (gli stessi del file meta locale: livello, cassa, ora di gioco…).</summary>
        public JsonValue Meta = JsonValue.Null;

        public static CloudSave From(JsonValue j) => new CloudSave
        {
            Uuid = j["uuid"].AsString(""),
            Name = j["name"].AsString(""),
            CityName = j["cityName"].AsString(""),
            Revision = j["revision"].AsInt(),
            DeviceId = j["deviceId"].AsString(""),
            DeviceName = j["deviceName"].AsString(""),
            SizeBytes = j["sizeBytes"].AsLong(),
            Checksum = j["checksum"].AsString(""),
            UpdatedAt = ApiTime.Parse(j["updatedAt"].AsString()),
            Meta = j["meta"],
        };
    }

    /// <summary>Identità di questo dispositivo, inviata con l'accesso e con ogni salvataggio caricato.</summary>
    public sealed class DeviceIdentity
    {
        /// <summary>8–64 caratteri [A-Za-z0-9_-], generato una volta per installazione.</summary>
        public string Id = "";
        public string Name = "";
        /// <summary>ios, android, web o editor.</summary>
        public string Platform = "editor";

        public void AddTo(JsonObject o)
        {
            o["deviceId"] = Id;
            o["deviceName"] = Name;
            o["platform"] = Platform;
        }

        /// <summary>Nuovo identificativo casuale (128 bit in base64url, 22 caratteri).</summary>
        public static string NewId()
        {
            var bytes = new byte[16];
            using (var rng = System.Security.Cryptography.RandomNumberGenerator.Create()) rng.GetBytes(bytes);
            return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        }
    }

    internal static class ApiTime
    {
        public static DateTime Parse(string s)
        {
            if (string.IsNullOrEmpty(s)) return DateTime.MinValue;
            return DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var t) ? t : DateTime.MinValue;
        }
    }
}
