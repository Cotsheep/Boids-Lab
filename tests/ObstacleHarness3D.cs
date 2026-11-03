using System.Collections.Generic;
using Godot;

namespace BoidsLab.Tests;

// Test fixture only: every operation creates a real, synchronous Godot query space.
internal static class ObstacleHarness3D
{
    public static Vector3 Calculate(BoidSnapshot3D self, IReadOnlyList<Obstacle3D> obstacles, BoidConfig config)
    {
        using var query = new ObstacleQuery3D();
        query.Synchronize(obstacles);
        return ObstacleAvoidance3D.Calculate(self, query, config);
    }
    public static void ResolveMotion(Vector3 from, ref Vector3 position, ref Vector3 velocity, IReadOnlyList<Obstacle3D> obstacles, float clearance)
    {
        using var query = new ObstacleQuery3D();
        query.Synchronize(obstacles);
        ObstacleContact3D.Resolve(from, ref position, ref velocity, query, clearance);
    }
    // Independent known geometry oracle, deliberately absent from production paths.
    public static void Surface(Vector3 point, Obstacle3D obstacle, float clearance, out float distance, out Vector3 normal)
    {
        Vector3 offset = point - obstacle.Position;
        if (obstacle.Shape == ObstacleShape.Sphere)
        {
            distance = offset.Length() - obstacle.Dimensions.X * 0.5f - clearance;
            normal = offset.Normalized();
            return;
        }
        Vector3 q = offset.Abs() - obstacle.Dimensions * 0.5f;
        Vector3 outside = q.Max(Vector3.Zero);
        distance = outside.Length() + Mathf.Min(Mathf.Max(q.X, Mathf.Max(q.Y, q.Z)), 0) - clearance;
        if (outside.LengthSquared() > 0) normal = (outside * offset.Sign()).Normalized();
        else
        {
            int axis = q.X >= q.Y && q.X >= q.Z ? 0 : q.Y >= q.Z ? 1 : 2;
            normal = Vector3.Zero;
            normal[axis] = offset[axis] < 0 ? -1 : 1;
        }
    }
}
