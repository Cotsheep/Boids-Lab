using System;
using System.Linq;
using Godot;

namespace BoidsLab.Tests;

/// <summary>同种子、同布局、网格搜索；记录管理器更新时间，不推算桌面 FPS。</summary>
public partial class ObstacleBenchmark : Node
{
    public override void _Ready()
    {
        try
        {
            foreach (int count in new[] { 200, 1000, 5000 })
                foreach (int dimension in new[] { 2, 3 }) Run(count, dimension);
            GD.Print("PASS: obstacle benchmark completed.");
            GetTree().Quit();
        }
        catch (Exception error) { GD.PushError(error.ToString()); GetTree().Quit(1); }
    }

    private void Run(int count, int dimension)
    {
        var config = new BoidConfig { BoidCount = count, RandomSeed = 42,
            NeighborSearchMode = NeighborSearchMode.UniformGrid, ShowGrid = false };
        string path = $"res://scenes/worlds/boids_world_{dimension}d.tscn";
        Node node = GD.Load<PackedScene>(path).Instantiate();
        var world = (ISimulationWorld)node;
        world.Initialize(config);
        AddChild(node);
        if (node is BoidManager two)
        {
            ObstacleHarness2D.ClearLayout(two);
            var editor = two.GetNode<ObstacleEditor2D>("ObstacleEditor");
            Vector2 c = two.ViewBounds.GetCenter();
            for (int i = 0; i < 6; i++) editor.AddObstacle(i % 2 == 0 ? ObstacleShape.Rectangle : ObstacleShape.Circle,
                c + new Vector2((i % 3 - 1) * 240, (i / 3 * 2 - 1) * 140));
        }
        else if (node is BoidManager3D three)
        {
            var editor = three.GetNode<ObstacleEditor3D>("ObstacleEditor");
            for (int i = 0; i < 6; i++) editor.AddObstacle(i % 2 == 0 ? ObstacleShape.Box : ObstacleShape.Sphere,
                new Vector3((i % 3 - 1) * 240, (i / 3 * 2 - 1) * 140, 0));
        }
        int samples = count == 5000 ? 5 : 40;
        double[] milliseconds = new double[samples];
        long queries = 0;
        for (int step = -10; step < samples; step++)
        {
            if (node is BoidManager a) a.Step(1.0 / 60); else ((BoidManager3D)node).Step(1.0 / 60);
            if (step >= 0)
            {
                milliseconds[step] = world.Stats.UpdateMilliseconds ?? 0;
                queries += node is BoidManager world2D ? world2D.ObstacleQuery.QueryCount
                    : ((BoidManager3D)node).ObstacleQuery.QueryCount;
            }
        }
        Array.Sort(milliseconds);
        GD.Print($"OBSTACLE-BENCH dimension={dimension} count={count} samples={samples} mean_ms={milliseconds.Average():F4} p95_ms={milliseconds[Math.Clamp((int)Math.Ceiling(samples * 0.95) - 1, 0, samples - 1)]:F4} native_queries_mean={(double)queries / samples:F1}");
        RemoveChild(node);
        node.Free();
    }
}
