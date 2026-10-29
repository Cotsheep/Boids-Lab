using System;
using Godot;

namespace BoidsLab.Tests;

/// <summary>领队转向、步前快照、生命周期与配置的无窗口检查。</summary>
public partial class LeaderChecks : Node
{
    private int _checks;
    private const float Dt = 1f / 60;

    public override void _Ready()
    {
        if (DisplayServer.GetName() != "headless")
        {
            GD.PushError("Run leader_checks.tscn with --headless.");
            GetTree().Quit(2);
            return;
        }
        try
        {
            GetWindow().MinSize = new Vector2I(960, 640);
            CheckMath();
            CheckFollowingAndArrival();
            CheckSnapshotOrder();
            CheckAvoidance();
            CheckConfigAndUiBinding();
            GD.Print($"PASS: {_checks} leader steering/lifecycle/configuration assertions (headless).");
            GetTree().Quit();
        }
        catch (Exception error)
        {
            GD.PushError(error.ToString());
            GetTree().Quit(1);
        }
    }

    private static BoidConfig Config() => new() { BoidCount = 0, PerceptionRadius = 1,
        SeparationEnabled = false, AlignmentEnabled = false, CohesionEnabled = false };

    private BoidManager World(BoidConfig config)
    {
        var world = GD.Load<PackedScene>("res://scenes/worlds/boids_world_2d.tscn").Instantiate<BoidManager>();
        world.Initialize(config);
        AddChild(world);
        return world;
    }

    private void Release(BoidManager world) { RemoveChild(world); world.Free(); }

    private void CheckMath()
    {
        var bounds = new Rect2(0, 0, 1000, 800);
        var config = Config();
        Vector2 follow = LeaderSteering.FollowPoint(new(30, 400), Vector2.Right, 80);
        Near(follow, new(-50, 400), "Behind point can be outside the viewport");
        Vector2 force = LeaderSteering.Arrive(new(0, new(10, 400), Vector2.Zero),
            new(990, 400), Vector2.Zero, config, Dt);
        Check(force.X > 0 && force.Y == 0, "Arrival uses direct world distance");
        force = LeaderSteering.Arrive(new(0, new(500, 400), new(100, 0)),
            new(500, 400), Vector2.Zero, config, Dt);
        Check(force.X < 0 && force.Length() <= config.MaxForce, "At target, brakes within acceleration limit");
        force = LeaderSteering.Arrive(new(0, new(500, 400), Vector2.Zero),
            new(500, 400), new(100, 0), config, Dt);
        Check(force.X > 0, "Following a moving target includes its velocity");
        config.MaxForce = 0;
        Check(LeaderSteering.Arrive(new(0, Vector2.Zero, Vector2.Zero), new(200, 200),
            Vector2.Zero, config, Dt) == Vector2.Zero, "Zero force cannot turn");
        config.MaxForce = 200;
        config.MaxSpeed = 0;
        Check(LeaderSteering.Arrive(new(0, Vector2.Zero, Vector2.Zero), new(200, 200),
            Vector2.Zero, config, Dt) == Vector2.Zero, "Zero speed requests no movement");
    }

    private void CheckFollowingAndArrival()
    {
        var config = Config();
        var world = World(config);
        try
        {
            Vector2 center = world.ViewBounds.GetCenter();
            Boid2D leader = world.SpawnBoid(center + new Vector2(100, 0), Vector2.Zero);
            Boid2D a = world.SpawnBoid(center + new Vector2(-200, -100), Vector2.Zero);
            Boid2D b = world.SpawnBoid(center + new Vector2(-200, 100), Vector2.Zero);
            world.SetLeader(leader);
            float radius = LeaderSteering.FollowRadius(2, config);
            Vector2 follow = leader.Position - Vector2.Right * (config.LeaderFollowDistance + radius);
            world.Step(Dt);
            Check(world.NeighborSearch.GetNeighborIndices(1).Count == 0 && a.Acceleration.X > 0
                && b.Acceleration.X > 0, "All distant Boids follow even with no sensed neighbors");
            for (int step = 0; step < 600; step++)
            {
                world.Step(Dt);
                foreach (Boid2D boid in world.Boids)
                    Check(boid.Position.IsFinite() && boid.Velocity.Length() <= config.MaxSpeed + 0.001f
                        && boid.Acceleration.Length() <= config.MaxForce + 0.001f, "Continuous following stays finite and bounded");
            }
            Check(a.Position.DistanceTo(follow) <= radius + 2.1f && b.Position.DistanceTo(follow) <= radius + 2.1f,
                "Both followers arrive inside the area behind stationary Leader");
            Near(leader.Position, center + new Vector2(100, 0), "Leader without target is not pulled by followers");

            Vector2 goal = leader.Position + new Vector2(180, 0);
            world.SetLeaderTarget(goal);
            Vector2 before = leader.Position;
            world._PhysicsProcess(Dt);
            Near(leader.Position, before, "Setting target while paused does not advance simulation");
            for (int step = 0; step < 600; step++) world.Step(Dt);
            Check(leader.Position.DistanceTo(goal) <= 2.1f && leader.Velocity.Length() < 0.01f,
                "Mouse destination is approached, slowed down and stopped at");
            Check(a.Position.DistanceTo(leader.Position - Vector2.Right * (config.LeaderFollowDistance + radius)) < radius + 3,
                "Followers track the moving leader then remain in its following area");

            config.LeaderFollowWeight = 5;
            a.Position = leader.Position - Vector2.Right * config.LeaderFollowDistance;
            a.Initialize(config, a.Id, new Vector2(0.4f, 0));
            world.Step(Dt);
            Check(a.Velocity.Length() < 0.01f, "High follow weight settles at target without reversing low speed");

            config.LeaderFollowWeight = 0;
            a.Position = center - new Vector2(300, 200);
            world.Step(Dt);
            Check(a.Acceleration == Vector2.Zero, "Zero follow weight removes follow contribution");
            // 只有领队使用目标转向；即使全局群集规则开启，也不能把它拉离目标。
            config.CohesionEnabled = true;
            config.CohesionWeight = 5;
            config.PerceptionRadius = 500;
            world.SetLeaderTarget(leader.Position + new Vector2(100, 0));
            world.Step(Dt);
            Check(leader.Acceleration.X > 0, "Leader ignores follower cohesion and obeys its own destination");
            Check(!world.IsRunning, "Direct assignments and stepping do not silently toggle IsRunning");

            world.SetLeader(a);
            Check(!leader.IsLeader && a.IsLeader && world.LeaderTarget is null, "Transfer clears prior identity and target");
            world.SetLeaderTarget(center);
            world.RemoveBoid(a);
            Check(world.Leader is null && world.LeaderTarget is null, "Removing leader clears identity and target");
            world.SetLeader(b);
            world.SetLeaderTarget(center);
            world.Reset();
            Check(world.Leader is null && world.LeaderTarget is null && world.Boids.Count == 0,
                "Reset clears runtime role and target");
        }
        finally { Release(world); }
    }

    private void CheckSnapshotOrder()
    {
        var first = World(Config());
        var last = World(Config());
        try
        {
            Vector2 center = first.ViewBounds.GetCenter();
            var firstLeader = first.SpawnBoid(center, new(60, 0));
            var firstFollower = first.SpawnBoid(center - new Vector2(180, 70), new(30, 0));
            var lastFollower = last.SpawnBoid(center - new Vector2(180, 70), new(30, 0));
            var lastLeader = last.SpawnBoid(center, new(60, 0));
            first.SetLeader(firstLeader);
            last.SetLeader(lastLeader);
            first.SetLeaderTarget(center + new Vector2(100, 100));
            last.SetLeaderTarget(center + new Vector2(100, 100));
            first.Step(0.1);
            last.Step(0.1);
            Near(firstFollower.Acceleration, lastFollower.Acceleration, "Follower uses same step-start leader state regardless of list order");
        }
        finally { Release(first); Release(last); }
    }

    private void CheckAvoidance()
    {
        var config = Config();
        var world = World(config);
        try
        {
            Vector2 center = world.ViewBounds.GetCenter();
            var leader = world.SpawnBoid(center - new Vector2(100, 0), new(120, 0));
            world.SetLeader(leader);
            world.SetLeaderTarget(center + new Vector2(180, 0));
            var obstacle = world.GetNode<ObstacleEditor2D>("ObstacleEditor").AddObstacle(ObstacleShape.Rectangle, center);
            obstacle.SetDimensions(new(16, 100));
            world.Step(Dt);
            Check(leader.Acceleration.Y != 0, "Avoidance overrides forward leader target steering");
            for (int i = 0; i < 600; i++)
            {
                world.Step(Dt);
                Vector2 offset = leader.Position - obstacle.Position;
                Check(Mathf.Abs(offset.X) >= 8 + config.ObstacleClearance - 0.02f
                    || Mathf.Abs(offset.Y) >= 50 + config.ObstacleClearance - 0.02f,
                    "Leader never tunnels into obstacle while seeking target");
            }
            Check(leader.Position.DistanceTo(world.LeaderTarget!.Value) < 2.1f,
                "Leader reaches reachable target after detouring around simple obstacle");
        }
        finally { Release(world); }
    }

    private void CheckConfigAndUiBinding()
    {
        const string path = "res://.godot/validation/leader-config.cfg";
        var config = Config();
        var panel = new ControlPanel();
        AddChild(panel);
        panel.Bind(config, "像素");
        ((SpinBox)panel.FindChild("LeaderFollowDistance", true, false)).Value = 135;
        ((SpinBox)panel.FindChild("LeaderFollowWeight", true, false)).Value = 2.3;
        Check(config.LeaderFollowDistance == 135 && Mathf.IsEqualApprox(config.LeaderFollowWeight, 2.3f),
            "Follow controls update shared configuration");
        Check(config.Save(path) == Error.Ok, "Save parameters to isolated test config");
        var loaded = Config();
        Check(loaded.Load(path) == Error.Ok && loaded.LeaderFollowDistance == 135
            && loaded.LeaderFollowWeight == config.LeaderFollowWeight, "Follow parameters round-trip");
        using var old = new ConfigFile();
        old.SetValue("simulation", "boid_count", 0);
        old.Save(path);
        loaded = Config();
        loaded.Load(path);
        Check(loaded.LeaderFollowDistance == 80 && loaded.LeaderFollowWeight == 1.5f, "Old configuration gets follow defaults");
        loaded.LeaderFollowDistance = float.NaN;
        loaded.LeaderFollowWeight = -10;
        loaded.Normalize();
        Check(loaded.LeaderFollowDistance == 80 && loaded.LeaderFollowWeight == 0, "Invalid follow values normalize");
        RemoveChild(panel);
        panel.Free();
    }

    private void Near(Vector2 actual, Vector2 expected, string message) =>
        Check(actual.DistanceTo(expected) < 0.01f, $"{message}: {actual} != {expected}");
    private void Check(bool condition, string message)
    {
        _checks++;
        if (!condition) throw new InvalidOperationException(message);
    }
}
