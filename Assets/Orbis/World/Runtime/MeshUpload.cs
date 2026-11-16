using Orbis.Core.World.Meshing;
using UnityEngine;
using UnityEngine.Rendering;

namespace Orbis.World
{
    /// <summary>Copia un <see cref="MeshBuffer"/> (Core) in una Mesh di Unity senza conversioni.</summary>
    public static class MeshUpload
    {
        private static readonly VertexAttributeDescriptor[] Layout =
        {
            new VertexAttributeDescriptor(VertexAttribute.Position, VertexAttributeFormat.Float32, 3),
            new VertexAttributeDescriptor(VertexAttribute.Normal, VertexAttributeFormat.Float32, 3),
            new VertexAttributeDescriptor(VertexAttribute.Color, VertexAttributeFormat.UNorm8, 4),
            new VertexAttributeDescriptor(VertexAttribute.TexCoord0, VertexAttributeFormat.Float32, 4),
            new VertexAttributeDescriptor(VertexAttribute.TexCoord1, VertexAttributeFormat.Float32, 4),
        };

        public static Mesh ToMesh(MeshBuffer mb, string name, Mesh reuse = null)
        {
            var mesh = reuse != null ? reuse : new Mesh();
            mesh.Clear();
            mesh.name = name;
            const MeshUpdateFlags flags = MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontValidateIndices |
                                          MeshUpdateFlags.DontNotifyMeshUsers;
            mesh.SetVertexBufferParams(mb.VertexCount, Layout);
            mesh.SetVertexBufferData(mb.Data, 0, 0, mb.VertexCount * MeshBuffer.Stride, 0, flags);
            var format = mb.VertexCount > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16;
            mesh.SetIndexBufferParams(mb.IndexCount, format);
            if (format == IndexFormat.UInt32)
            {
                mesh.SetIndexBufferData(mb.Indices, 0, 0, mb.IndexCount, flags);
            }
            else
            {
                var idx = new ushort[mb.IndexCount];
                for (int i = 0; i < idx.Length; i++) idx[i] = (ushort)mb.Indices[i];
                mesh.SetIndexBufferData(idx, 0, 0, idx.Length, flags);
            }
            mesh.subMeshCount = 1;
            var bounds = new Bounds();
            bounds.SetMinMax(new Vector3(mb.MinX, mb.MinY, mb.MinZ), new Vector3(mb.MaxX, mb.MaxY, mb.MaxZ));
            mesh.SetSubMesh(0, new SubMeshDescriptor(0, mb.IndexCount) { bounds = bounds, vertexCount = mb.VertexCount }, flags);
            mesh.bounds = bounds;
            mesh.UploadMeshData(true); // libera la copia in RAM: la geometria serve solo alla GPU
            return mesh;
        }
    }
}
