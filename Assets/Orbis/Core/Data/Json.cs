using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Orbis.Core.Data
{
    public enum JsonType { Null, Bool, Number, String, Array, Object }

    /// <summary>
    /// Valore JSON letto da <see cref="Json.Parse"/>. Lettore minimo in C# puro per i dati del gioco
    /// (catalogo, bilanciamento, missioni): niente dipendenze, uguale su iOS, Android, web e nei test.
    /// </summary>
    public sealed class JsonValue
    {
        public static readonly JsonValue Null = new JsonValue(JsonType.Null);

        public readonly JsonType Type;
        private readonly bool _bool;
        private readonly double _number;
        private readonly string _string;
        private readonly List<JsonValue> _array;
        private readonly Dictionary<string, JsonValue> _object;
        private readonly List<string> _keys;

        private JsonValue(JsonType t) => Type = t;
        internal JsonValue(bool b) : this(JsonType.Bool) => _bool = b;
        internal JsonValue(double n) : this(JsonType.Number) => _number = n;
        internal JsonValue(string s) : this(JsonType.String) => _string = s;
        internal JsonValue(List<JsonValue> a) : this(JsonType.Array) => _array = a;

        internal JsonValue(Dictionary<string, JsonValue> o, List<string> keys) : this(JsonType.Object)
        {
            _object = o;
            _keys = keys;
        }

        public bool IsNull => Type == JsonType.Null;
        public int Count => Type == JsonType.Array ? _array.Count : (Type == JsonType.Object ? _object.Count : 0);

        /// <summary>Elemento di un array (Null se fuori dai limiti o se non è un array).</summary>
        public JsonValue this[int i] => Type == JsonType.Array && i >= 0 && i < _array.Count ? _array[i] : Null;

        /// <summary>Campo di un oggetto (Null se manca o se non è un oggetto).</summary>
        public JsonValue this[string key] => Type == JsonType.Object && _object.TryGetValue(key, out var v) ? v : Null;

        public bool Has(string key) => Type == JsonType.Object && _object.ContainsKey(key);

        /// <summary>Chiavi nell'ordine del file.</summary>
        public IReadOnlyList<string> Keys => _keys ?? (IReadOnlyList<string>)Array.Empty<string>();

        public IEnumerable<JsonValue> Items
        {
            get
            {
                if (Type != JsonType.Array) yield break;
                foreach (var v in _array) yield return v;
            }
        }

        public string AsString(string fallback = null) => Type == JsonType.String ? _string : (Type == JsonType.Number ? _number.ToString(CultureInfo.InvariantCulture) : fallback);
        public double AsDouble(double fallback = 0) => Type == JsonType.Number ? _number : fallback;
        public float AsFloat(float fallback = 0) => Type == JsonType.Number ? (float)_number : fallback;
        public int AsInt(int fallback = 0) => Type == JsonType.Number ? (int)Math.Round(_number) : fallback;
        public long AsLong(long fallback = 0) => Type == JsonType.Number ? (long)Math.Round(_number) : fallback;
        public bool AsBool(bool fallback = false) => Type == JsonType.Bool ? _bool : fallback;
    }

    public sealed class JsonException : Exception
    {
        public JsonException(string message, int position) : base(message + " (posizione " + position + ")") { }
    }

    public static class Json
    {
        public static JsonValue Parse(string text)
        {
            if (text == null) throw new ArgumentNullException(nameof(text));
            int i = 0;
            SkipWhitespace(text, ref i);
            var v = ParseValue(text, ref i, 0);
            SkipWhitespace(text, ref i);
            if (i != text.Length) throw new JsonException("testo dopo la fine del JSON", i);
            return v;
        }

        private static JsonValue ParseValue(string s, ref int i, int depth)
        {
            if (depth > 128) throw new JsonException("JSON troppo annidato", i);
            if (i >= s.Length) throw new JsonException("JSON incompleto", i);
            char c = s[i];
            switch (c)
            {
                case '{': return ParseObject(s, ref i, depth);
                case '[': return ParseArray(s, ref i, depth);
                case '"': return new JsonValue(ParseString(s, ref i));
                case 't': Expect(s, ref i, "true"); return new JsonValue(true);
                case 'f': Expect(s, ref i, "false"); return new JsonValue(false);
                case 'n': Expect(s, ref i, "null"); return JsonValue.Null;
                default:
                    if (c == '-' || (c >= '0' && c <= '9')) return new JsonValue(ParseNumber(s, ref i));
                    throw new JsonException("carattere inatteso '" + c + "'", i);
            }
        }

        private static JsonValue ParseObject(string s, ref int i, int depth)
        {
            var dict = new Dictionary<string, JsonValue>();
            var keys = new List<string>();
            i++; // {
            SkipWhitespace(s, ref i);
            if (i < s.Length && s[i] == '}')
            {
                i++;
                return new JsonValue(dict, keys);
            }
            while (true)
            {
                SkipWhitespace(s, ref i);
                if (i >= s.Length || s[i] != '"') throw new JsonException("attesa una chiave tra virgolette", i);
                var key = ParseString(s, ref i);
                SkipWhitespace(s, ref i);
                if (i >= s.Length || s[i] != ':') throw new JsonException("attesi i due punti", i);
                i++;
                SkipWhitespace(s, ref i);
                var value = ParseValue(s, ref i, depth + 1);
                if (!dict.ContainsKey(key)) keys.Add(key);
                dict[key] = value;
                SkipWhitespace(s, ref i);
                if (i < s.Length && s[i] == ',')
                {
                    i++;
                    continue;
                }
                if (i < s.Length && s[i] == '}')
                {
                    i++;
                    return new JsonValue(dict, keys);
                }
                throw new JsonException("attesa ',' o '}'", i);
            }
        }

        private static JsonValue ParseArray(string s, ref int i, int depth)
        {
            var list = new List<JsonValue>();
            i++; // [
            SkipWhitespace(s, ref i);
            if (i < s.Length && s[i] == ']')
            {
                i++;
                return new JsonValue(list);
            }
            while (true)
            {
                SkipWhitespace(s, ref i);
                list.Add(ParseValue(s, ref i, depth + 1));
                SkipWhitespace(s, ref i);
                if (i < s.Length && s[i] == ',')
                {
                    i++;
                    continue;
                }
                if (i < s.Length && s[i] == ']')
                {
                    i++;
                    return new JsonValue(list);
                }
                throw new JsonException("attesa ',' o ']'", i);
            }
        }

        private static string ParseString(string s, ref int i)
        {
            i++; // "
            var sb = new StringBuilder();
            while (i < s.Length)
            {
                char c = s[i++];
                if (c == '"') return sb.ToString();
                if (c != '\\')
                {
                    sb.Append(c);
                    continue;
                }
                if (i >= s.Length) break;
                char e = s[i++];
                switch (e)
                {
                    case '"': sb.Append('"'); break;
                    case '\\': sb.Append('\\'); break;
                    case '/': sb.Append('/'); break;
                    case 'b': sb.Append('\b'); break;
                    case 'f': sb.Append('\f'); break;
                    case 'n': sb.Append('\n'); break;
                    case 'r': sb.Append('\r'); break;
                    case 't': sb.Append('\t'); break;
                    case 'u':
                        if (i + 4 > s.Length) throw new JsonException("sequenza \\u incompleta", i);
                        sb.Append((char)int.Parse(s.Substring(i, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                        i += 4;
                        break;
                    default: throw new JsonException("sequenza di escape non valida", i);
                }
            }
            throw new JsonException("stringa non chiusa", i);
        }

        private static double ParseNumber(string s, ref int i)
        {
            int start = i;
            if (s[i] == '-') i++;
            while (i < s.Length && (char.IsDigit(s[i]) || s[i] == '.' || s[i] == 'e' || s[i] == 'E' || s[i] == '+' || s[i] == '-')) i++;
            if (!double.TryParse(s.Substring(start, i - start), NumberStyles.Float, CultureInfo.InvariantCulture, out var d))
                throw new JsonException("numero non valido", start);
            return d;
        }

        private static void Expect(string s, ref int i, string word)
        {
            if (string.CompareOrdinal(s, i, word, 0, word.Length) != 0) throw new JsonException("atteso " + word, i);
            i += word.Length;
        }

        private static void SkipWhitespace(string s, ref int i)
        {
            while (i < s.Length)
            {
                char c = s[i];
                if (c == ' ' || c == '\t' || c == '\n' || c == '\r') i++;
                else if (c == '/' && i + 1 < s.Length && s[i + 1] == '/')
                {
                    // commenti di riga ammessi nei file di dati scritti a mano
                    while (i < s.Length && s[i] != '\n') i++;
                }
                else break;
            }
        }
    }
}
