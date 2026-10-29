using System;
using Godot;

namespace BoidsLab.Tests;

/// <summary>通过真实管理器复现密集追随，不把“所有个体到达同一点”当作成功。</summary>
public partial class LeaderSpacingChecks : Node
{
    public override void _Ready()
    {
        if (DisplayServer.GetName() != "headless") { GetTree().Quit(2); return; }
        try
        {
            CheckGroup(40, 1.5f, NeighborSearchMode.Naive);
            CheckGroup(200, 1.5f, NeighborSearchMode.UniformGrid);
            CheckGroup(40, 5f, NeighborSearchMode.UniformGrid);
            GD.Print("PASS: leader spacing at rest and during motion (headless).");
            GetTree().Quit();
        }
        catch (Exception error) { GD.PushError(error.ToString()); GetTree().Quit(1); }
    }

    private void CheckGroup(int count, float weight, NeighborSearchMode mode)
    {
        var config = new BoidConfig { BoidCount = 0, LeaderFollowWeight = weight, NeighborSearchMode = mode };
        var world = GD.Load<PackedScene>("res://scenes/worlds/boids_world_2d.tscn").Instantiate<BoidManager>();
        world.Initialize(config);
        AddChild(world);
        try
        {
            Vector2 center = world.ViewBounds.GetCenter();
            var leader = world.SpawnBoid(center + new Vector2(80, 0), Vector2.Zero);
            for (int i = 0; i < count; i++)
                world.SpawnBoid(center + Vector2.FromAngle(i * Mathf.Tau / count) * (100 + i % 3 * 20), Vector2.Zero);
            world.SetLeader(leader);
            for (int i = 0; i < 1200; i++) world.Step(1.0 / 60);
            CheckSpacing(world, count, $"rest, weight={weight}");
            // 持续移动并转弯的光标轨迹；每秒取样，避免只验证最终静止画面。
            Vector2 start = leader.Position;
            for (int i = 0; i < 1800; i++)
            {
                float t = i / 60f;
                world.SetLeaderTarget(start + new Vector2(t * 45, Mathf.Sin(t / 4) * 80));
                world.Step(1.0 / 60);
                if (i >= 600 && i % 60 == 0) CheckSpacing(world, count, $"moving t={t}, weight={weight}", false);
            }
            CheckSpacing(world, count, $"moving, weight={weight}");
            Vector2 groupCenter = Vector2.Zero;
            for (int i = 1; i < world.Boids.Count; i++) groupCenter += world.Boids[i].Position;
            groupCenter /= count;
            float areaRadius = LeaderSteering.FollowRadius(count, config);
            if (groupCenter.DistanceTo(leader.Position) > config.LeaderFollowDistance + 2 * areaRadius + 120)
                throw new InvalidOperationException("Followers must remain with the moving Leader, rather than solving crowding by abandoning it.");
        }
        finally { RemoveChild(world); world.Free(); }
    }

    private static void CheckSpacing(BoidManager world, int count, string phase, bool report = true)
    {
        float nearestSum = 0;
        int crowded = 0;
        for (int i = 1; i < world.Boids.Count; i++)
        {
            float nearest = float.PositiveInfinity;
            for (int j = 1; j < world.Boids.Count; j++)
                if (i != j) nearest = Mathf.Min(nearest, world.Boids[i].Position.DistanceTo(world.Boids[j].Position));
            nearestSum += nearest;
            if (nearest < 10) crowded++;
            Boid2D boid = world.Boids[i];
            if (!boid.Position.IsFinite() || boid.Velocity.Length() > world.Config.MaxSpeed + 0.001f)
                throw new InvalidOperationException("Following must preserve finite positions and speed limits.");
        }
        if (report) GD.Print($"Leader spacing ({count}, {phase}): mean nearest={nearestSum / count:F2}, crowded (<10)={crowded}/{count}");
        if (crowded > count / 10)
            throw new InvalidOperationException($"Followers collapsed during {phase}: {crowded}/{count} have nearest distance below 10.");
    }
}
