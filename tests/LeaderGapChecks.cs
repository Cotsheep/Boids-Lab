using System;
using Godot;

namespace BoidsLab.Tests;

/// <summary>真实管理器和物理查询：鼠标目标在窄缝内，目标后方障碍不能迫使领队绕远。</summary>
public partial class LeaderGapChecks : Node
{
    private int _checks;
    private const float Dt = 1f / 60;

    public override void _Ready()
    {
        try
        {
            GetWindow().MinSize = new Vector2I(960, 640);
            foreach (NeighborSearchMode mode in Enum.GetValues<NeighborSearchMode>())
            {
                CheckGapDestination(mode, 0, 2, 0);
                CheckGapDestination(mode, Mathf.Pi * 0.5f, 2, 0);
                CheckGapDestination(mode, -0.6f, 2, 12);
                CheckGapDestination(mode, 0, 2, 30, new(180, -30));
                CheckGapDestination(mode, 0, 0, 0);
                CheckStationaryCorridor(mode, false);
                CheckStationaryCorridor(mode, true);
                CheckBrakingBeforeCloseWall(mode);
                CheckImpassableGap(mode);
            }
            GD.Print($"PASS: {_checks} target-aware 2D leader gap/braking/contact assertions.");
            GetTree().Quit();
        }
        catch (Exception error) { GD.PushError(error.ToString()); GetTree().Quit(1); }
    }

    private void Check(bool condition, string message)
    {
        _checks++;
        if (!condition) throw new InvalidOperationException(message);
    }

    private BoidManager World(NeighborSearchMode mode, float clearance = 2)
    {
        var world = GD.Load<PackedScene>("res://scenes/worlds/boids_world_2d.tscn").Instantiate<BoidManager>();
        world.Initialize(new BoidConfig { BoidCount = 0, MaxSpeed = 500, MaxForce = 1000,
            AvoidanceLookAhead = 1.5f, ObstacleClearance = clearance, NeighborSearchMode = mode,
            SeparationEnabled = false, AlignmentEnabled = false, CohesionEnabled = false });
        AddChild(world);
        ObstacleHarness2D.ClearLayout(world);
        return world;
    }

    private static Boid2D Leader(BoidManager world, Vector2 position, Vector2 velocity, Vector2 target)
    {
        // Spawn before assigning identity uses wrap coordinates; place only after infinite mode is active.
        var leader = world.SpawnBoid(world.ViewBounds.GetCenter(), velocity);
        world.SetLeader(leader);
        leader.Position = position;
        world.SetLeaderTarget(target);
        return leader;
    }

    private static Obstacle2D Wall(BoidManager world, Vector2 center, Vector2 size, float angle = 0)
    {
        var obstacle = world.GetNode<ObstacleEditor2D>("ObstacleEditor").AddObstacle(ObstacleShape.Rectangle, center);
        obstacle.SetDimensions(size);
        obstacle.Rotation = angle;
        return obstacle;
    }

    private void SafeStep(BoidManager world)
    {
        world.Step(Dt);
        var leader = world.Leader!;
        Check(leader.Position.IsFinite() && leader.Velocity.Length() <= world.Config.MaxSpeed + 0.01f
            && leader.Acceleration.Length() <= world.Config.MaxForce + 0.01f, "Motion remains finite and bounded");
        foreach (Obstacle2D wall in world.GetNode<ObstacleEditor2D>("ObstacleEditor").Obstacles)
        {
            Vector2 local = (leader.Position - wall.Position).Rotated(-wall.Rotation);
            // Independent rectangle-distance oracle, rather than asking the production query to validate itself.
            Vector2 outside = (local.Abs() - wall.Dimensions * 0.5f).Max(Vector2.Zero);
            Check(outside.Length() >= world.Config.ObstacleClearance - 0.03f
                && (Mathf.Abs(local.X) >= wall.Dimensions.X * 0.5f || Mathf.Abs(local.Y) >= wall.Dimensions.Y * 0.5f),
                "Leader retains clearance and never enters a wall");
        }
        Check(!world.IsRunning, "Direct fixed steps preserve paused state");
    }

    private void CheckGapDestination(NeighborSearchMode mode, float angle, float clearance, float lateralStart,
        Vector2 goal = default)
    {
        var world = World(mode, clearance);
        try
        {
            foreach (float side in new[] { -1f, 1f })
                Wall(world, new Vector2(0, side * 124).Rotated(angle), new(160, 200), angle);
            Wall(world, new Vector2(260, 0).Rotated(angle), new(80, 160), angle);
            var leader = Leader(world, new Vector2(-220, lateralStart).Rotated(angle),
                Vector2.Right.Rotated(angle) * 500, goal.Rotated(angle));
            Vector2 target = goal.Rotated(angle);
            world.GetNode<ObstacleEditor2D>("ObstacleEditor").SynchronizeQuery();
            Check(!world.ObstacleQuery.Sweep(leader.Position, target - leader.Position, clearance).Blocked,
                "Known 48-unit gap has a clear path to the mouse destination");
            float lateral = 0;
            for (int i = 0; i < 600; i++)
            {
                SafeStep(world);
                lateral = Mathf.Max(lateral, Mathf.Abs(leader.Position.Rotated(-angle).Y));
            }
            GD.Print($"LEADER-GAP mode={mode} angle={angle:F2} radius={clearance} target={goal} lateral={lateral:F3} final_distance={leader.Position.DistanceTo(target):F3}");
            Check(lateral <= Mathf.Max(Mathf.Abs(lateralStart), Mathf.Abs(goal.Y)) + 2,
                $"Reachable goal through a passable gap must not trigger a wide detour: lateral {lateral}");
            Check(leader.Position.DistanceTo(target) <= 2.1f && leader.Velocity.Length() < 0.05f,
                "Leader enters the gap and settles at the reachable mouse target despite the obstacle beyond it");
        }
        finally { world.Free(); }
    }

    private void CheckStationaryCorridor(NeighborSearchMode mode, bool alreadyAtTarget)
    {
        var world = World(mode, 12);
        try
        {
            Wall(world, new(0, -114), new(400, 200));
            Wall(world, new(0, 114), new(400, 200)); // 28 wide: real radius 12 fits; near-contact radius 18 overlaps.
            Vector2 start = new(-80, 0);
            Vector2 target = alreadyAtTarget ? start : new(80, 0);
            var leader = Leader(world, start, Vector2.Zero, target);
            SafeStep(world);
            Check(Mathf.Abs(leader.Acceleration.Y) < 0.01f,
                "Near-surface caution must not push a stopped leader sideways into a passable corridor wall");
            for (int i = 0; i < 600; i++) SafeStep(world);
            Check(leader.Position.DistanceTo(target) <= 2.1f && Mathf.Abs(leader.Position.Y) < 0.05f,
                "Stopped leader follows the corridor center or stays settled at its existing target");
        }
        finally { world.Free(); }
    }

    private void CheckBrakingBeforeCloseWall(NeighborSearchMode mode)
    {
        var world = World(mode);
        try
        {
            Wall(world, new(80, 0), new(16, 300));
            var leader = Leader(world, Vector2.Zero, Vector2.Right * 500, new(-10, 0));
            SafeStep(world);
            Check(leader.Acceleration.X < 0 && Mathf.Abs(leader.Acceleration.Y) < 0.01f,
                "Close mouse goal requests braking rather than an outward full-speed detour");
            for (int i = 0; i < 600; i++) SafeStep(world);
            Check(leader.Position.DistanceTo(new(-10, 0)) <= 2.1f,
                "Momentum toward a nearby wall is constrained before returning to the mouse goal");
        }
        finally { world.Free(); }
    }

    private void CheckImpassableGap(NeighborSearchMode mode)
    {
        var world = World(mode, 25);
        try
        {
            Wall(world, new(0, -124), new(160, 200));
            Wall(world, new(0, 124), new(160, 200));
            Leader(world, new(-220, 0), Vector2.Right * 500, Vector2.Zero);
            for (int i = 0; i < 240; i++) SafeStep(world);
            Check(world.Leader!.Position.Length() > 2.1f,
                "A 50-unit diameter cannot be forced into a genuinely impassable 48-unit gap");
        }
        finally { world.Free(); }
    }
}
