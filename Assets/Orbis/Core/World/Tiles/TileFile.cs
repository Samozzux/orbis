using System;
using Orbis.Core.World.Compression;

namespace Orbis.Core.World.Tiles
{
    /// <summary>
    /// Contenitore dei file del pack (tile e indice): "OTZ1" + dimensione non compressa (uint32 LE)
    /// + dati FlatBuffers compressi con Deflate raw. Lo scrive orbis_world/encode.py.
    /// </summary>
    public static class TileFile
    {
        public const int HeaderSize = 8;
        private static readonly byte[] Magic = { (byte)'O', (byte)'T', (byte)'Z', (byte)'1' };

        public static bool HasHeader(byte[] file)
        {
            if (file == null || file.Length < HeaderSize) return false;
            for (int i = 0; i < 4; i++)
                if (file[i] != Magic[i]) return false;
            return true;
        }

        /// <summary>Decomprime il file e restituisce il buffer FlatBuffers.</summary>
        public static byte[] Unpack(byte[] file)
        {
            if (!HasHeader(file)) throw new CorruptDataException("file del mondo non riconosciuto");
            int size = file[4] | file[5] << 8 | file[6] << 16 | file[7] << 24;
            if (size < 0 || size > 512 * 1024 * 1024) throw new CorruptDataException("dimensione non valida");
            var raw = Inflater.Decompress(file, HeaderSize, file.Length - HeaderSize, size);
            if (raw.Length != size) throw new CorruptDataException("dimensione decompressa diversa da quella attesa");
            return raw;
        }
    }

    /// <summary>CRC-32 (IEEE, come zlib.crc32) per verificare i file scaricati.</summary>
    public static class Crc32
    {
        private static readonly uint[] Table = BuildTable();

        private static uint[] BuildTable()
        {
            var t = new uint[256];
            for (uint i = 0; i < 256; i++)
            {
                uint c = i;
                for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
                t[i] = c;
            }
            return t;
        }

        public static uint Compute(byte[] data, int offset = 0, int length = -1)
        {
            if (length < 0) length = data.Length - offset;
            uint crc = 0xFFFFFFFFu;
            for (int i = offset; i < offset + length; i++) crc = Table[(crc ^ data[i]) & 0xFF] ^ (crc >> 8);
            return crc ^ 0xFFFFFFFFu;
        }
    }
}
