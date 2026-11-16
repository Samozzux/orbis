using System;

namespace Orbis.Core.World.Meshing
{
    /// <summary>
    /// Vertici e indici di una mesh, già nel formato che Unity carica senza conversioni
    /// (Mesh.SetVertexBufferData): x est, y su, z nord; triangoli in senso orario visti dal fronte.
    /// Layout di un vertice (60 byte): posizione float3, normale float3, colore RGBA8, uv0 float4, uv1 float4.
    /// I costruttori lavorano in (est, nord, quota) con triangoli antiorari: la conversione è qui.
    /// </summary>
    public sealed class MeshBuffer
    {
        public const int Stride = 15; // in int (4 byte)

        /// <summary>Dati dei vertici: float salvati come bit dentro int (copiabili senza alterazioni).</summary>
        public int[] Data = new int[Stride * 256];
        public int VertexCount;
        public int[] Indices = new int[768];
        public int IndexCount;

        public float MinX = float.MaxValue, MinY = float.MaxValue, MinZ = float.MaxValue;
        public float MaxX = float.MinValue, MaxY = float.MinValue, MaxZ = float.MinValue;

        public bool IsEmpty => IndexCount == 0;

        public void Clear()
        {
            VertexCount = 0;
            IndexCount = 0;
            MinX = MinY = MinZ = float.MaxValue;
            MaxX = MaxY = MaxZ = float.MinValue;
        }

        public static uint Rgba(byte r, byte g, byte b, byte a = 255) => (uint)(r | g << 8 | b << 16 | a << 24);

        public static uint Rgb(uint rrggbb, byte a = 255) =>
            Rgba((byte)(rrggbb >> 16), (byte)(rrggbb >> 8), (byte)rrggbb, a);

        /// <summary>Aggiunge un vertice. e = est, n = nord, h = quota; normale (ne, nn, nh).</summary>
        public int Add(float e, float n, float h, float ne, float nn, float nh, uint color,
            float u0, float u1, float u2, float u3, float w0, float w1, float w2, float w3)
        {
            if ((VertexCount + 1) * Stride > Data.Length) Array.Resize(ref Data, Data.Length * 2);
            int o = VertexCount * Stride;
            var d = Data;
            d[o] = Bits(e);
            d[o + 1] = Bits(h);
            d[o + 2] = Bits(n);
            d[o + 3] = Bits(ne);
            d[o + 4] = Bits(nh);
            d[o + 5] = Bits(nn);
            d[o + 6] = (int)color;
            d[o + 7] = Bits(u0);
            d[o + 8] = Bits(u1);
            d[o + 9] = Bits(u2);
            d[o + 10] = Bits(u3);
            d[o + 11] = Bits(w0);
            d[o + 12] = Bits(w1);
            d[o + 13] = Bits(w2);
            d[o + 14] = Bits(w3);
            if (e < MinX) MinX = e;
            if (e > MaxX) MaxX = e;
            if (h < MinY) MinY = h;
            if (h > MaxY) MaxY = h;
            if (n < MinZ) MinZ = n;
            if (n > MaxZ) MaxZ = n;
            return VertexCount++;
        }

        /// <summary>Triangolo antiorario visto dall'esterno nel sistema (est, nord, quota).</summary>
        public void Triangle(int a, int b, int c)
        {
            if (IndexCount + 3 > Indices.Length) Array.Resize(ref Indices, Indices.Length * 2);
            Indices[IndexCount++] = a;
            Indices[IndexCount++] = c; // Unity: orario visto dal fronte
            Indices[IndexCount++] = b;
        }

        public void Quad(int a, int b, int c, int d)
        {
            Triangle(a, b, c);
            Triangle(a, c, d);
        }

        public float GetFloat(int vertex, int component) => Unbits(Data[vertex * Stride + component]);

        public static int Bits(float f) => BitConverter.SingleToInt32Bits(f);
        public static float Unbits(int i) => BitConverter.Int32BitsToSingle(i);
    }
}
