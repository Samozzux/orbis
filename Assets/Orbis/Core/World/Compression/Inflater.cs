using System;

namespace Orbis.Core.World.Compression
{
    /// <summary>
    /// Decompressore DEFLATE "raw" (RFC 1951) scritto in C# puro.
    /// Serve perché i tile del mondo sono compressi con Deflate e la decompressione deve
    /// funzionare uguale su iOS, Android, web (IL2CPP senza thread) e nei test .NET,
    /// senza dipendere da librerie native.
    /// </summary>
    public static class Inflater
    {
        private static readonly ushort[] LengthBase =
            { 3, 4, 5, 6, 7, 8, 9, 10, 11, 13, 15, 17, 19, 23, 27, 31, 35, 43, 51, 59, 67, 83, 99, 115, 131, 163, 195, 227, 258 };
        private static readonly byte[] LengthExtra =
            { 0, 0, 0, 0, 0, 0, 0, 0, 1, 1, 1, 1, 2, 2, 2, 2, 3, 3, 3, 3, 4, 4, 4, 4, 5, 5, 5, 5, 0 };
        private static readonly ushort[] DistanceBase =
            { 1, 2, 3, 4, 5, 7, 9, 13, 17, 25, 33, 49, 65, 97, 129, 193, 257, 385, 513, 769, 1025, 1537, 2049, 3073, 4097, 6145, 8193, 12289, 16385, 24577 };
        private static readonly byte[] DistanceExtra =
            { 0, 0, 0, 0, 1, 1, 2, 2, 3, 3, 4, 4, 5, 5, 6, 6, 7, 7, 8, 8, 9, 9, 10, 10, 11, 11, 12, 12, 13, 13 };
        private static readonly byte[] CodeLengthOrder =
            { 16, 17, 18, 0, 8, 7, 9, 6, 10, 5, 11, 4, 12, 3, 13, 2, 14, 1, 15 };

        /// <summary>Decomprime <paramref name="length"/> byte da <paramref name="offset"/>.</summary>
        /// <param name="expectedSize">dimensione attesa (per allocare una volta sola); 0 se ignota</param>
        public static byte[] Decompress(byte[] data, int offset, int length, int expectedSize = 0)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            if (offset < 0 || length < 0 || offset + length > data.Length) throw new ArgumentOutOfRangeException(nameof(length));
            var state = new State(data, offset, length, expectedSize > 0 ? expectedSize : Math.Max(1024, length * 4));
            bool last;
            do
            {
                last = state.Bits(1) == 1;
                int type = (int)state.Bits(2);
                switch (type)
                {
                    case 0: state.Stored(); break;
                    case 1: state.Codes(Fixed.Literals, Fixed.Distances); break;
                    case 2: state.Dynamic(); break;
                    default: throw new CorruptDataException("blocco Deflate di tipo non valido");
                }
            } while (!last);
            return state.Result();
        }

        public static byte[] Decompress(byte[] data) => Decompress(data, 0, data.Length);

        /// <summary>Codici fissi del blocco di tipo 1 (costruiti una volta, in modo sicuro tra thread).</summary>
        private static class Fixed
        {
            public static readonly Huffman Literals = BuildLiterals();
            public static readonly Huffman Distances = BuildDistances();

            private static Huffman BuildLiterals()
            {
                var lengths = new byte[288];
                for (int i = 0; i < 144; i++) lengths[i] = 8;
                for (int i = 144; i < 256; i++) lengths[i] = 9;
                for (int i = 256; i < 280; i++) lengths[i] = 7;
                for (int i = 280; i < 288; i++) lengths[i] = 8;
                return Huffman.Build(lengths, 0, 288);
            }

            private static Huffman BuildDistances()
            {
                var lengths = new byte[30];
                for (int i = 0; i < 30; i++) lengths[i] = 5;
                return Huffman.Build(lengths, 0, 30);
            }
        }

        /// <summary>Tabella di decodifica: indice = prossimi Bits bit dell'ingresso, valore = (simbolo &lt;&lt; 4) | lunghezza.</summary>
        private sealed class Huffman
        {
            public int[] Table;
            public int Bits;

            public static Huffman Build(byte[] lengths, int start, int count)
            {
                var blCount = new int[16];
                int maxLen = 0;
                for (int i = 0; i < count; i++)
                {
                    int l = lengths[start + i];
                    if (l > 15) throw new CorruptDataException("lunghezza di codice non valida");
                    blCount[l]++;
                    if (l > maxLen) maxLen = l;
                }
                var h = new Huffman { Bits = maxLen };
                if (maxLen == 0)
                {
                    h.Table = new int[1];
                    return h; // nessun codice (ammesso per le distanze se non ci sono riferimenti)
                }
                blCount[0] = 0;
                // controllo: il codice non deve essere sovra-assegnato
                int left = 1;
                for (int len = 1; len <= 15; len++)
                {
                    left <<= 1;
                    left -= blCount[len];
                    if (left < 0) throw new CorruptDataException("codice di Huffman sovra-assegnato");
                }
                var nextCode = new int[16];
                int code = 0;
                for (int len = 1; len <= 15; len++)
                {
                    code = (code + blCount[len - 1]) << 1;
                    nextCode[len] = code;
                }
                h.Table = new int[1 << maxLen];
                for (int sym = 0; sym < count; sym++)
                {
                    int len = lengths[start + sym];
                    if (len == 0) continue;
                    int c = nextCode[len]++;
                    // i codici sono scritti dal bit più significativo: nella tabella vanno invertiti
                    int rev = 0;
                    for (int b = 0; b < len; b++)
                    {
                        rev = (rev << 1) | (c & 1);
                        c >>= 1;
                    }
                    int entry = (sym << 4) | len;
                    for (int i = rev; i < h.Table.Length; i += 1 << len) h.Table[i] = entry;
                }
                return h;
            }
        }

        private sealed class State
        {
            private readonly byte[] _in;
            private int _pos;
            private readonly int _end;
            private ulong _bitBuf;
            private int _bitCount;
            private int _padBytes; // byte "finti" (zeri) letti oltre la fine dell'ingresso
            private byte[] _out;
            private int _outLen;

            public State(byte[] input, int offset, int length, int capacity)
            {
                _in = input;
                _pos = offset;
                _end = offset + length;
                _out = new byte[capacity];
            }

            private void Need(int n)
            {
                while (_bitCount < n)
                {
                    ulong b;
                    if (_pos < _end) b = _in[_pos++];
                    else
                    {
                        b = 0;
                        _padBytes++;
                    }
                    _bitBuf |= b << _bitCount;
                    _bitCount += 8;
                }
            }

            private void Drop(int n)
            {
                _bitBuf >>= n;
                _bitCount -= n;
                if (_padBytes > 0 && _padBytes * 8 > _bitCount)
                    throw new CorruptDataException("dati Deflate troncati");
            }

            public uint Bits(int n)
            {
                if (n == 0) return 0;
                Need(n);
                uint v = (uint)(_bitBuf & ((1UL << n) - 1));
                Drop(n);
                return v;
            }

            private int Decode(Huffman h)
            {
                if (h.Bits == 0) throw new CorruptDataException("codice di Huffman vuoto");
                Need(h.Bits);
                int e = h.Table[(int)(_bitBuf & (ulong)((1 << h.Bits) - 1))];
                if (e == 0) throw new CorruptDataException("codice di Huffman incompleto");
                Drop(e & 15);
                return e >> 4;
            }

            private void Put(byte b)
            {
                if (_outLen == _out.Length) Grow(_outLen + 1);
                _out[_outLen++] = b;
            }

            private void Grow(int min)
            {
                int size = Math.Max(min, _out.Length * 2);
                var n = new byte[size];
                Buffer.BlockCopy(_out, 0, n, 0, _outLen);
                _out = n;
            }

            public void Stored()
            {
                Drop(_bitCount & 7); // allinea al byte
                int len = (int)Bits(16);
                int nlen = (int)Bits(16);
                if ((len ^ 0xFFFF) != nlen) throw new CorruptDataException("blocco non compresso corrotto");
                if (_outLen + len > _out.Length) Grow(_outLen + len);
                while (len > 0 && _bitCount >= 8)
                {
                    _out[_outLen++] = (byte)_bitBuf;
                    Drop(8);
                    len--;
                }
                if (len > _end - _pos) throw new CorruptDataException("dati Deflate troncati");
                Buffer.BlockCopy(_in, _pos, _out, _outLen, len);
                _pos += len;
                _outLen += len;
            }

            public void Dynamic()
            {
                int nlen = (int)Bits(5) + 257;
                int ndist = (int)Bits(5) + 1;
                int ncode = (int)Bits(4) + 4;
                if (nlen > 286 || ndist > 30) throw new CorruptDataException("intestazione Deflate non valida");
                var lengths = new byte[320];
                for (int i = 0; i < ncode; i++) lengths[CodeLengthOrder[i]] = (byte)Bits(3);
                var lencode = Huffman.Build(lengths, 0, 19);
                Array.Clear(lengths, 0, 19);
                int index = 0;
                while (index < nlen + ndist)
                {
                    int sym = Decode(lencode);
                    if (sym < 16)
                    {
                        lengths[index++] = (byte)sym;
                        continue;
                    }
                    byte value = 0;
                    int repeat;
                    if (sym == 16)
                    {
                        if (index == 0) throw new CorruptDataException("ripetizione senza lunghezza precedente");
                        value = lengths[index - 1];
                        repeat = 3 + (int)Bits(2);
                    }
                    else if (sym == 17) repeat = 3 + (int)Bits(3);
                    else repeat = 11 + (int)Bits(7);
                    if (index + repeat > nlen + ndist) throw new CorruptDataException("troppe lunghezze di codice");
                    while (repeat-- > 0) lengths[index++] = value;
                }
                if (lengths[256] == 0) throw new CorruptDataException("manca il codice di fine blocco");
                var lit = Huffman.Build(lengths, 0, nlen);
                var dist = Huffman.Build(lengths, nlen, ndist);
                Codes(lit, dist);
            }

            public void Codes(Huffman lit, Huffman dist)
            {
                while (true)
                {
                    int sym = Decode(lit);
                    if (sym < 256)
                    {
                        Put((byte)sym);
                        continue;
                    }
                    if (sym == 256) return;
                    sym -= 257;
                    if (sym >= 29) throw new CorruptDataException("lunghezza non valida");
                    int len = LengthBase[sym] + (int)Bits(LengthExtra[sym]);
                    int dsym = Decode(dist);
                    if (dsym >= 30) throw new CorruptDataException("distanza non valida");
                    int d = DistanceBase[dsym] + (int)Bits(DistanceExtra[dsym]);
                    if (d > _outLen) throw new CorruptDataException("distanza oltre l'inizio dei dati");
                    if (_outLen + len > _out.Length) Grow(_outLen + len);
                    int from = _outLen - d;
                    if (d >= len)
                    {
                        Buffer.BlockCopy(_out, from, _out, _outLen, len);
                        _outLen += len;
                    }
                    else
                    {
                        for (int i = 0; i < len; i++) _out[_outLen++] = _out[from + i];
                    }
                }
            }

            public byte[] Result()
            {
                if (_outLen == _out.Length) return _out;
                var r = new byte[_outLen];
                Buffer.BlockCopy(_out, 0, r, 0, _outLen);
                return r;
            }
        }
    }

    /// <summary>Dati compressi o tile non validi.</summary>
    public sealed class CorruptDataException : Exception
    {
        public CorruptDataException(string message) : base(message) { }
    }
}
