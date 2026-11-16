using System;

namespace Orbis.Core.World.Geometry
{
    /// <summary>Punto/vettore 2D in metri (x est, y nord).</summary>
    public readonly struct Vec2 : IEquatable<Vec2>
    {
        public readonly float X;
        public readonly float Y;

        public Vec2(float x, float y)
        {
            X = x;
            Y = y;
        }

        public static Vec2 operator +(Vec2 a, Vec2 b) => new Vec2(a.X + b.X, a.Y + b.Y);
        public static Vec2 operator -(Vec2 a, Vec2 b) => new Vec2(a.X - b.X, a.Y - b.Y);
        public static Vec2 operator *(Vec2 a, float k) => new Vec2(a.X * k, a.Y * k);
        public static float Dot(Vec2 a, Vec2 b) => a.X * b.X + a.Y * b.Y;
        /// <summary>Prodotto vettoriale z: positivo se b è a sinistra di a.</summary>
        public static float Cross(Vec2 a, Vec2 b) => a.X * b.Y - a.Y * b.X;
        public float Length => (float)Math.Sqrt(X * X + Y * Y);
        public Vec2 Normalized
        {
            get
            {
                float l = Length;
                return l > 1e-9f ? new Vec2(X / l, Y / l) : new Vec2(0, 0);
            }
        }
        /// <summary>Perpendicolare a destra (per un anello antiorario punta verso l'esterno).</summary>
        public Vec2 Right => new Vec2(Y, -X);
        public static Vec2 Lerp(Vec2 a, Vec2 b, float t) => new Vec2(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t);

        public bool Equals(Vec2 o) => X == o.X && Y == o.Y;
        public override bool Equals(object obj) => obj is Vec2 v && Equals(v);
        public override int GetHashCode() => X.GetHashCode() * 397 ^ Y.GetHashCode();
        public override string ToString() => "(" + X.ToString("0.###") + ", " + Y.ToString("0.###") + ")";
    }
}
