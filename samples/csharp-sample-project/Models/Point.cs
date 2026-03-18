namespace GameProject.Models;

/// <summary>
/// An immutable 2D point. Demonstrates: readonly record struct.
/// </summary>
public readonly record struct Point(double X, double Y)
{
    /// <summary>
    /// Gets the distance from the origin.
    /// </summary>
    public double DistanceFromOrigin => Math.Sqrt(X * X + Y * Y);

    public double DistanceTo(Point other) =>
        Math.Sqrt(Math.Pow(X - other.X, 2) + Math.Pow(Y - other.Y, 2));
}
