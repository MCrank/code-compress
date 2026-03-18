using System;

namespace GameProject.Models
{
    /// <summary>
    /// A 2D vector for position and movement calculations.
    /// Demonstrates: block-scoped namespace, struct, operator overloads.
    /// </summary>
    public struct Vector2
    {
        public float X { get; set; }
        public float Y { get; set; }

        public Vector2(float x, float y)
        {
            X = x;
            Y = y;
        }

        /// <summary>
        /// Gets the magnitude of the vector.
        /// </summary>
        public readonly float Magnitude => MathF.Sqrt(X * X + Y * Y);

        public static Vector2 operator +(Vector2 a, Vector2 b) => new(a.X + b.X, a.Y + b.Y);

        public static Vector2 operator -(Vector2 a, Vector2 b) => new(a.X - b.X, a.Y - b.Y);

        public static Vector2 operator *(Vector2 v, float scalar) => new(v.X * scalar, v.Y * scalar);

        public static bool operator ==(Vector2 a, Vector2 b) => a.X == b.X && a.Y == b.Y;

        public static bool operator !=(Vector2 a, Vector2 b) => !(a == b);

        public override bool Equals(object? obj) =>
            obj is Vector2 other && this == other;

        public override int GetHashCode() =>
            HashCode.Combine(X, Y);
    }
}
