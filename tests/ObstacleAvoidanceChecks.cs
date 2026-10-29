using System;
using Godot;

namespace BoidsLab.Tests;

/// <summary>解析避障、接触扫描与真实管理器接入检查，不使用图形窗口。</summary>
public partial class ObstacleAvoidanceChecks : Node
{
    private int _checks;
    private static readonly Rect2 Bounds = new(0, 0, 1000, 1000);

    public override void _Ready()
    {
        if (DisplayServer.GetName() != "headless")
        {
            GD.PushError("Run obstacle_avoidance_checks.tscn with --headless.");
            GetTree().Quit(2);
            return;
        }
        try
        {
            GetWindow().MinSize = new Vector2I(960, 640);
            CheckPrediction();
            CheckMotion();
            CheckManager();
            CheckContinuousAvoidance();
            CheckConfig();
            GD.Print($"PASS: {_checks} obstacle geometry/avoidance/contact/integration assertions (headless).");
            GetTree().Quit();
        }
        catch (Exception error)
        {
            GD.PushError(error.ToString());
            GetTree().Quit(1);
        }
    }

    private void Check(bool condition, string message)
    {
        _checks++;
        if (!condition) throw new InvalidOperationException(message);
    }

    private void Near(Vector2 actual, Vector2 expected, string message, float tolerance = 0.05f) =>
        Check(actual.DistanceTo(expected) <= tolerance, $"{message}: {actual} != {expected}");

    private static Obstacle2D Shape(ObstacleShape shape, Vector2 position, Vector2 dimensions)
    {
        var obstacle = new Obstacle2D { Position = position };
        obstacle.Initialize(shape, dimensions);
        return obstacle;
    }

    private void CheckPrediction()
    {
        var config = new BoidConfig { MaxSpeed = 200, MaxForce = 200, ObstacleClearance = 12, AvoidanceLookAhead = 0.75f };
        foreach (ObstacleShape kind in new[] { ObstacleShape.Rectangle, ObstacleShape.Circle })
        {
            var shape = Shape(kind, new Vector2(500, 500), new Vector2(100, 100));
            try
            {
                Obstacle2D[] list = { shape };
                Vector2 acceleration = ObstacleAvoidance.Calculate(new(0, new(300, 500), new(200, 0)), list, config);
                Check(acceleration.X < 0 && MathF.Abs(acceleration.Y) > 0, "Head-on approach brakes and selects a side");
                Check(acceleration.IsFinite() && acceleration.Length() <= config.MaxForce + 0.001f, "Avoidance acceleration respects maximum");
                Vector2 otherSide = ObstacleAvoidance.Calculate(new(1, new(300, 500), new(200, 0)), list, config);
                Check(MathF.Sign(acceleration.Y) == -MathF.Sign(otherSide.Y), "Direct center approach has deterministic side choice by ID");
                Near(ObstacleAvoidance.Calculate(new(0, new(300, 500), new(-200, 0)), list, config), Vector2.Zero,
                    "Departing obstacle does not trigger avoidance");
                Near(ObstacleAvoidance.Calculate(new(0, new(438, 500), new(-200, 0)), list, config), Vector2.Zero,
                    "Moving away from near surface does not get pulled back");
                Near(ObstacleAvoidance.Calculate(new(0, new(300, 500), Vector2.Zero), list, config), Vector2.Zero,
                    "Stationary distant individual stays stationary");
                Check(ObstacleAvoidance.Calculate(new(0, new(437.99f, 500), Vector2.Zero), list, config).Length() > 0,
                    "Stopped contact receives an escape direction");
                config.AvoidanceEnabled = false;
                Near(ObstacleAvoidance.Calculate(new(0, new(300, 500), new(200, 0)), list, config), Vector2.Zero,
                    "Disable switch removes avoidance steering");
                config.AvoidanceEnabled = true;
                config.MaxForce = 0;
                Near(ObstacleAvoidance.Calculate(new(0, new(300, 500), new(200, 0)), list, config), Vector2.Zero,
                    "Zero acceleration limit stays finite and produces no steering");
                config.MaxForce = 200;
                shape.Position = new Vector2(1100, 500);
                Check(ObstacleAvoidance.Calculate(new(0, new(950, 500), new(200, 0)), list, config).Length() > 0,
                    "Offscreen obstacles still steer approaching Boids");
            }
            finally { shape.Free(); }
        }
        var circle = Shape(ObstacleShape.Circle, Vector2.Zero, new Vector2(100, 200));
        try
        {
            Near(circle.Dimensions, new Vector2(100, 100), "Circle remains circular");
            circle.SetDimensions(new Vector2(1, 4000));
            Near(circle.Dimensions, Vector2.One * Obstacle2D.MinSize, "Circle diameter clamps to minimum");
            circle.SetDimensions(new Vector2(4000, 1));
            Near(circle.Dimensions, Vector2.One * Obstacle2D.MaxSize, "Circle diameter clamps to maximum");
        }
        finally { circle.Free(); }
    }

    private void CheckMotion()
    {
        foreach (ObstacleShape kind in new[] { ObstacleShape.Rectangle, ObstacleShape.Circle })
        {
            var obstacle = Shape(kind, new Vector2(500, 500), kind == ObstacleShape.Rectangle ? new Vector2(16, 200) : new Vector2(100, 100));
            try
            {
                Obstacle2D[] list = { obstacle };
                Vector2 position = new(900, 500);
                Vector2 velocity = new(800, 0);
                ObstacleAvoidance.ResolveMotion(new(100, 500), ref position, ref velocity, list, 12);
                float left = 500 - obstacle.Dimensions.X / 2 - 12;
                Check(position.X < left && position.X > left - 0.1f, "High-speed segment cannot tunnel through shape");
                Near(velocity, Vector2.Zero, "Head-on contact removes inward velocity");
                position = new(500, 500);
                velocity = new(0, 100);
                ObstacleAvoidance.ResolveMotion(position, ref position, ref velocity, list, 12);
                Check(position.IsFinite() && velocity.IsFinite() && position.DistanceTo(new Vector2(500, 500)) >= obstacle.Dimensions.X / 2,
                    "Initial overlap recovers along a finite outward normal");
                position = new(750, 200);
                velocity = new(500, 0);
                ObstacleAvoidance.ResolveMotion(new(250, 200), ref position, ref velocity, list, 12);
                Near(position, new(750, 200), "Unobstructed trajectory remains unchanged");
                Near(velocity, new(500, 0), "Unobstructed velocity remains unchanged");
            }
            finally { obstacle.Free(); }
        }
        var wall = Shape(ObstacleShape.Rectangle, new(500, 500), new(16, 200));
        try
        {
            Vector2 position = new(900, 560);
            Vector2 velocity = new(800, 160);
            ObstacleAvoidance.ResolveMotion(new(100, 400), ref position, ref velocity, new[] { wall }, 12);
            Near(position, new(479.99f, 560), "Wall contact preserves tangential displacement");
            Near(velocity, new(0, 160), "Wall contact preserves tangential velocity");
            wall.Position = new(60, 500);
            wall.SetDimensions(new(16, 200));
            position = new(1100, 500);
            velocity = new(200, 0);
            ObstacleAvoidance.ResolveMotion(new(900, 500), ref position, ref velocity, new[] { wall }, 12);
            Near(position, new(1100, 500), "Opposite edge obstacle does not block the world trajectory");
            position = new(1100, 500);
            ObstacleAvoidance.ResolveMotion(new(900, 500), ref position, ref velocity, new[] { wall }, 12, Bounds);
            Near(position, new(39.99f, 500), "Wrapping mode checks the opposite-side arrival path");
            wall.Position = new(950, 500);
            position = new(-100, 500);
            velocity = new(-200, 0);
            ObstacleAvoidance.ResolveMotion(new(100, 500), ref position, ref velocity, new[] { wall }, 12);
            Near(position, new(-100, 500), "Motion continues into negative world coordinates");
            position = new(-100, 500);
            ObstacleAvoidance.ResolveMotion(new(100, 500), ref position, ref velocity, new[] { wall }, 12, Bounds);
            Near(position, new(970.01f, 500), "Negative wrapping also sweeps the arrival path");
            wall.Position = new(1100, 500);
            position = new(1100, 500);
            velocity = new(200, 0);
            ObstacleAvoidance.ResolveMotion(new(900, 500), ref position, ref velocity, new[] { wall }, 12);
            Near(position, new(1079.99f, 500), "Offscreen obstacle still stops the swept trajectory");
            position = new(1100, 500);
            velocity = new(200, 0);
            ObstacleAvoidance.ResolveMotion(new(900, 500), ref position, ref velocity, new[] { wall }, 12, Bounds);
            Near(position, new(100, 500), "Wrapping mode ignores completely outside obstacles");
        }
        finally { wall.Free(); }
    }

    private void CheckManager()
    {
        var config = new BoidConfig { BoidCount = 0, MaxSpeed = 1000, MaxForce = 0,
            SeparationEnabled = false, AlignmentEnabled = false, CohesionEnabled = false };
        var world = GD.Load<PackedScene>("res://scenes/worlds/boids_world_2d.tscn").Instantiate<BoidManager>();
        world.Initialize(config);
        AddChild(world);
        try
        {
            var editor = world.GetNode<ObstacleEditor2D>("ObstacleEditor");
            Vector2 center = world.ViewBounds.GetCenter();
            Boid2D boid = world.SpawnBoid(center - new Vector2(200, 0), new Vector2(500, 0));
            world.Step(0.2);
            Near(boid.Position, center - new Vector2(100, 0), "No obstacles preserves original velocity integration");
            Obstacle2D obstacle = editor.AddObstacle(ObstacleShape.Rectangle, center);
            obstacle.SetDimensions(new(16, 200));
            world.Step(0.4);
            Check(boid.Position.X < center.X - 8 && boid.Velocity.X == 0, "Manager contact prevents high-speed tunneling even at zero acceleration");
            config.MaxForce = 200;
            world.Step(0.1);
            Check(boid.Acceleration.Length() > 0 && boid.Velocity.Length() > 0,
                "Contact can escape while all flocking rules are disabled");
            boid.Position = center;
            world.Step(0.01);
            Check(MathF.Abs(boid.Position.X - center.X) > 8 || MathF.Abs(boid.Position.Y - center.Y) > 100,
                "Manager recovers a Boid covered by a newly edited obstacle");
            config.AvoidanceEnabled = false;
            config.MaxForce = 0;
            world.RemoveBoid(boid);
            boid = world.SpawnBoid(center - new Vector2(100, 0), new(500, 0));
            world.Step(0.4);
            Near(boid.Position, center + new Vector2(100, 0), "Disabled avoidance also disables contact fallback");
            world.Reset();
            Check(editor.Obstacles.Count == 1 && world.Boids.Count == 0, "Reset rebuilds flock without deleting obstacles");
        }
        finally { RemoveChild(world); world.Free(); }
    }

    private void CheckContinuousAvoidance()
    {
        foreach (ObstacleShape kind in new[] { ObstacleShape.Rectangle, ObstacleShape.Circle })
        {
            var config = new BoidConfig { BoidCount = 0, MaxSpeed = 120, MaxForce = 200,
                SeparationEnabled = false, AlignmentEnabled = false, CohesionEnabled = false };
            var world = GD.Load<PackedScene>("res://scenes/worlds/boids_world_2d.tscn").Instantiate<BoidManager>();
            world.Initialize(config);
            AddChild(world);
            try
            {
                Vector2 center = world.ViewBounds.GetCenter();
                var obstacle = world.GetNode<ObstacleEditor2D>("ObstacleEditor").AddObstacle(kind, center);
                Vector2 start = center - new Vector2(200, 0);
                Boid2D boid = world.SpawnBoid(start, new Vector2(120, 0));
                bool approached = false;
                bool passedSide = false;
                for (int step = 0; step < 300; step++)
                {
                    world.Step(1.0 / 60);
                    Vector2 offset = boid.Position - center;
                    float halfWidth = obstacle.Dimensions.X / 2 + config.ObstacleClearance;
                    float halfHeight = obstacle.Dimensions.Y / 2 + config.ObstacleClearance;
                    bool outside = kind == ObstacleShape.Circle
                        ? offset.Length() >= halfWidth - 0.05f
                        : Mathf.Abs(offset.X) >= halfWidth - 0.05f || Mathf.Abs(offset.Y) >= halfHeight - 0.05f;
                    Check(outside && boid.Position.IsFinite() && boid.Velocity.IsFinite(),
                        "Continuous simulation preserves clearance and finite state");
                    approached |= boid.Position.X > start.X + 10;
                    // 没有目的地的 Boid 可以沿任一侧离开；不要求它绕回原来的直线路径。
                    passedSide |= Mathf.Abs(offset.Y) > halfHeight + 20 && boid.Velocity.Length() > 30;
                }
                Check(approached && passedSide, "Head-on individual continuously moves past an obstacle side instead of parking at contact");
            }
            finally { RemoveChild(world); world.Free(); }
        }
    }

    private void CheckConfig()
    {
        const string path = "res://.godot/validation/obstacle-config.cfg";
        var config = new BoidConfig { AvoidanceEnabled = false, AvoidanceLookAhead = 1.25f, ObstacleClearance = 24 };
        Check(config.Save(path) == Error.Ok, "Avoidance config saves to isolated test file");
        var loaded = new BoidConfig();
        Check(loaded.Load(path) == Error.Ok && !loaded.AvoidanceEnabled
            && loaded.AvoidanceLookAhead == 1.25f && loaded.ObstacleClearance == 24, "Avoidance settings roundtrip");
        using var legacy = new ConfigFile();
        legacy.SetValue("simulation", "boid_count", 5);
        Check(legacy.Save(path) == Error.Ok, "Legacy test config created");
        loaded = new BoidConfig();
        Check(loaded.Load(path) == Error.Ok && loaded.AvoidanceEnabled
            && loaded.AvoidanceLookAhead == 0.75f && loaded.ObstacleClearance == 12, "Old configurations use avoidance defaults");
        loaded.AvoidanceLookAhead = float.NaN;
        loaded.ObstacleClearance = -100;
        loaded.Normalize();
        Check(loaded.AvoidanceLookAhead == 0.75f && loaded.ObstacleClearance == 0, "Avoidance numeric values normalize centrally");
    }
}
