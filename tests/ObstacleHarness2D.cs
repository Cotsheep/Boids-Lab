using System.Collections.Generic;
using Godot;

namespace BoidsLab.Tests;

// Test fixture only: every operation creates a real, synchronous Godot query space.
internal static class ObstacleHarness2D
{
    // Scenario fixtures define their own layout, independent of authored sample obstacles.
    public static void ClearLayout(BoidManager world)
    {
        foreach (Node child in world.GetNode("Obstacles").GetChildren())
            if (child is Obstacle2D) child.Free();
        world.GetNode<ObstacleEditor2D>("ObstacleEditor").SynchronizeQuery();
    }

    public static Vector2 Calculate(BoidSnapshot self, IReadOnlyList<Obstacle2D> obstacles, BoidConfig config, Rect2? bounds = null)
    {
        using var query = new ObstacleQuery2D();
        query.Synchronize(obstacles, bounds);
        return ObstacleAvoidance.Calculate(self, query, config);
    }
    public static void ResolveMotion(Vector2 from, ref Vector2 position, ref Vector2 velocity, IReadOnlyList<Obstacle2D> obstacles, float clearance, Rect2? bounds = null)
    {
        using var query = new ObstacleQuery2D();
        query.Synchronize(obstacles, bounds);
        ObstacleContact2D.Resolve(from, ref position, ref velocity, query, clearance);
    }
}
