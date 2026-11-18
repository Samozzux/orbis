using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Orbis.Core.Data
{
    /// <summary>
    /// Oggetto JSON da scrivere (richieste alle API, file di stato): chiavi nell'ordine di inserimento, valori
    /// stringa, numeri, booleani, null, <see cref="JsonValue"/> letti da <see cref="Json.Parse"/>, altri
    /// <see cref="JsonObject"/> e liste. <c>ToString()</c> restituisce il testo JSON.
    /// </summary>
    public sealed class JsonObject : IEnumerable<KeyValuePair<string, object>>
    {
        private readonly List<KeyValuePair<string, object>> _fields = new List<KeyValuePair<string, object>>();

        public object this[string key]
        {
            set
            {
                for (int i = 0; i < _fields.Count; i++)
                {
                    if (_fields[i].Key != key) continue;
                    _fields[i] = new KeyValuePair<string, object>(key, value);
                    return;
                }
                _fields.Add(new KeyValuePair<string, object>(key, value));
            }
        }

        public void Add(string key, object value) => this[key] = value;

        public IEnumerator<KeyValuePair<string, object>> GetEnumerator() => _fields.GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        public override string ToString()
        {
            var sb = new StringBuilder();
            JsonText.Write(sb, this);
            return sb.ToString();
        }
    }

    /// <summary>Scrittura di testo JSON (con l'escape corretto delle stringhe).</summary>
    public static class JsonText
    {
        public static string Of(object value)
        {
            var sb = new StringBuilder();
            Write(sb, value);
            return sb.ToString();
        }

        public static void Write(StringBuilder sb, object value)
        {
            switch (value)
            {
                case null:
                    sb.Append("null");
                    return;
                case string s:
                    Quote(sb, s);
                    return;
                case bool b:
                    sb.Append(b ? "true" : "false");
                    return;
                case int i:
                    sb.Append(i.ToString(CultureInfo.InvariantCulture));
                    return;
                case long l:
                    sb.Append(l.ToString(CultureInfo.InvariantCulture));
                    return;
                case float f:
                    sb.Append(((double)f).ToString("R", CultureInfo.InvariantCulture));
                    return;
                case double d:
                    sb.Append(d.ToString("R", CultureInfo.InvariantCulture));
                    return;
                case DateTime t:
                    Quote(sb, t.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss'Z'", CultureInfo.InvariantCulture));
                    return;
                case JsonValue j:
                    WriteValue(sb, j);
                    return;
                case JsonObject o:
                    {
                        sb.Append('{');
                        bool first = true;
                        foreach (var kv in o)
                        {
                            if (!first) sb.Append(',');
                            first = false;
                            Quote(sb, kv.Key);
                            sb.Append(':');
                            Write(sb, kv.Value);
                        }
                        sb.Append('}');
                        return;
                    }
                case IEnumerable list:
                    {
                        sb.Append('[');
                        bool first = true;
                        foreach (var item in list)
                        {
                            if (!first) sb.Append(',');
                            first = false;
                            Write(sb, item);
                        }
                        sb.Append(']');
                        return;
                    }
                default:
                    Quote(sb, Convert.ToString(value, CultureInfo.InvariantCulture));
                    return;
            }
        }

        /// <summary>Riscrive un valore letto (per passare avanti dati che il gioco non interpreta, es. i metadati di un salvataggio).</summary>
        public static void WriteValue(StringBuilder sb, JsonValue v)
        {
            switch (v.Type)
            {
                case JsonType.Null:
                    sb.Append("null");
                    break;
                case JsonType.Bool:
                    sb.Append(v.AsBool() ? "true" : "false");
                    break;
                case JsonType.Number:
                    sb.Append(v.AsDouble().ToString("R", CultureInfo.InvariantCulture));
                    break;
                case JsonType.String:
                    Quote(sb, v.AsString());
                    break;
                case JsonType.Array:
                    {
                        sb.Append('[');
                        bool first = true;
                        foreach (var item in v.Items)
                        {
                            if (!first) sb.Append(',');
                            first = false;
                            WriteValue(sb, item);
                        }
                        sb.Append(']');
                        break;
                    }
                default:
                    {
                        sb.Append('{');
                        bool first = true;
                        foreach (var key in v.Keys)
                        {
                            if (!first) sb.Append(',');
                            first = false;
                            Quote(sb, key);
                            sb.Append(':');
                            WriteValue(sb, v[key]);
                        }
                        sb.Append('}');
                        break;
                    }
            }
        }

        public static void Quote(StringBuilder sb, string s)
        {
            sb.Append('"');
            foreach (char c in s ?? "")
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    case '\b': sb.Append("\\b"); break;
                    case '\f': sb.Append("\\f"); break;
                    default:
                        if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        else sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
        }
    }
}
