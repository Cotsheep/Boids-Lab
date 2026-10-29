using System;
using System.Linq;
using Godot;

namespace BoidsLab.Tests;

/// <summary>真实世界中领队身份切换的边界、生命周期和搜索/绘制一致性检查。</summary>
public partial class BoundaryModeChecks : Node
{
    private int _checks;

    public override void _Ready()
    {
        if (DisplayServer.GetName() != "headless") { GetTree().Quit(2); return; }
        try
        {
            CheckModeTransitions();
            CheckSearchAndDebugSwitch();
            GD.Print($"PASS: {_checks} leader-dependent boundary assertions (headless).");
            GetTree().Quit();
        }
        catch (Exception error) { GD.PushError(error.ToString()); GetTree().Quit(1); }
    }

    private BoidManager World()
    {
        var world = GD.Load<PackedScene>("res://scenes/worlds/boids_world_2d.tscn").Instantiate<BoidManager>();
        world.Initialize(new BoidConfig { BoidCount = 0, LeaderFollowWeight = 0,
            SeparationEnabled = false, AlignmentEnabled = false, CohesionEnabled = false });
        AddChild(world);
        return world;
    }

    private void CheckModeTransitions()
    {
        var world = World();
        try
        {
            var camera = world.GetNode<SimulationCamera2D>("Camera2D");
            Rect2 initial = world.ViewBounds;
            var a = world.SpawnBoid(initial.End - new Vector2(10, 10), new Vector2(40, 0));
            var b = world.SpawnBoid(initial.GetCenter(), Vector2.Zero);
            Check(world.WrapBounds == initial, "World starts in wrapping mode");
            world.Step(1);
            Near(a.Position, new(initial.Position.X + 30, initial.End.Y - 10), "Positive exit retains overshoot");
            a.Position = initial.Position - initial.Size * 3 + new Vector2(17, 19);
            world.Step(0.5);
            Near(a.Position, initial.Position + new Vector2(37, 19), "Wrapping handles more than one world-width of displacement");

            world.SetLeader(b);
            Check(world.WrapBounds is null && !world.NeighborSearch.HasResults,
                "Assigning Leader switches topology and invalidates cached neighbors");
            // 领队存在即开放无限地图，与权重、普通选择、暂停状态无关。
            world.ClearBoidSelection();
            a.Position = initial.End - new Vector2(10, 10);
            world.Step(1);
            Near(a.Position, new(initial.End.X + 30, initial.End.Y - 10), "Zero follow weight still leaves the map unlimited");
            var far = world.SpawnBoid(initial.Position - initial.Size * 2 + new Vector2(23, 29), new Vector2(-30, 0));
            world.Step(0.01);
            Vector2[] positions = world.Boids.Select(x => x.Position).ToArray();
            Pan(camera, new Vector2(160, -80));
            Wheel(camera, MouseButton.WheelUp);
            Check(world.Boids.Select(x => x.Position).SequenceEqual(positions) && world.NeighborSearch.HasResults,
                "With Leader pan and zoom preserve world positions and statistics");
            world.SetLeader(far);
            Check(world.WrapBounds is null && world.Boids.Select(x => x.Position).SequenceEqual(positions),
                "Leader transfer does not temporarily wrap any individual");

            var before = world.Boids.Select(x => (x.Id, x.Position, x.Velocity, x.Rotation)).ToArray();
            world.SetLeader(null);
            Check(!world.IsRunning && world.WrapBounds == world.ViewBounds && !world.NeighborSearch.HasResults
                && world.Stats.NeighborCandidateChecks is null, "Removing identity immediately restores wrapping while paused");
            for (int i = 0; i < before.Length; i++)
            {
                Near(world.Boids[i].Position, ReferenceWrap(before[i].Position, world.ViewBounds), "Existing positions wrap into current view");
                Check(world.Boids[i].Id == before[i].Id && world.Boids[i].Velocity == before[i].Velocity
                    && world.Boids[i].Rotation == before[i].Rotation, "Mode switch preserves identity, velocity and facing");
            }
            world.Step(0.01);
            Pan(camera, new Vector2(-2000, 1300));
            Wheel(camera, MouseButton.WheelUp);
            Check(world.Boids.All(x => world.ViewBounds.HasPoint(x.Position)) && !world.NeighborSearch.HasResults,
                "Without Leader moving or shrinking the view immediately wraps the population");
            camera.FitWorld();
            Check(world.Boids.All(x => world.ViewBounds.HasPoint(x.Position)), "Without Leader Home also synchronizes the boundary");

            world.SetLeader(b);
            a.Position = world.ViewBounds.End + new Vector2(30, 40);
            world.SetRunning(true);
            world.RemoveBoid(b);
            Check(world.IsRunning && world.Leader is null && world.WrapBounds.HasValue
                && world.Boids.All(x => world.ViewBounds.HasPoint(x.Position)),
                "Deleting Leader restores wrapping without changing the running state");
            world.SetLeader(a);
            world.Config.BoidCount = 12;
            world.Reset();
            Check(!world.IsRunning && world.Leader is null && world.WrapBounds.HasValue
                && world.Boids.Count == 12 && world.Boids.All(x => world.ViewBounds.HasPoint(x.Position)),
                "Reset clears Leader and regenerates the population inside wrapping bounds");
        }
        finally { RemoveChild(world); world.Free(); }
    }

    private void CheckSearchAndDebugSwitch()
    {
        foreach (NeighborSearchMode mode in Enum.GetValues<NeighborSearchMode>())
        {
            var world = World();
            try
            {
                world.Config.PerceptionRadius = 10;
                world.Config.NeighborSearchMode = mode;
                world.Config.ShowNeighbors = world.Config.ShowPerceptionRadius = true;
                Rect2 bounds = world.ViewBounds;
                var a = world.SpawnBoid(new(bounds.Position.X + 2, bounds.GetCenter().Y), Vector2.Zero);
                var b = world.SpawnBoid(new(bounds.End.X - 2, bounds.GetCenter().Y), Vector2.Zero);
                var leader = world.SpawnBoid(bounds.GetCenter(), Vector2.Zero);
                var geometry = new DebugOverlayGeometry();
                for (int cycle = 0; cycle < 3; cycle++)
                {
                    world.SetLeader(null);
                    world.Step(0.01);
                    Check(world.NeighborSearch.GetNeighborIndices(0).SequenceEqual(new[] { 1 }), "Both searches find the across-edge neighbor when wrapping");
                    Near(world.NeighborSearch.GetOffset(0, 1), new(-4, 0), "Steering receives shortest wrapped displacement");
                    geometry.Build(world.Boids, a, world.Config, bounds, 1, world.WrapBounds.HasValue);
                    Check(geometry.NeighborCount == 1 && geometry.NeighborPoints.Count == 4,
                        "Debug links agree with wrapping neighbors and draw both edge segments");
                    world.SetLeader(leader);
                    Check(!world.NeighborSearch.HasResults, "Every topology change clears stale neighbor results");
                    world.Step(0.01);
                    Check(world.NeighborSearch.GetNeighborIndices(0).Count == 0, "Both searches use direct distance with Leader");
                    geometry.Build(world.Boids, a, world.Config, bounds, 1, world.WrapBounds.HasValue);
                    Check(geometry.NeighborCount == 0 && geometry.RangePoints.All(p => p.X < bounds.Position.X + 13),
                        "Unlimited debug geometry clears old wrapped links and opposite-side arcs");
                }
            }
            finally { RemoveChild(world); world.Free(); }
        }
    }

    private static Vector2 ReferenceWrap(Vector2 point, Rect2 bounds) => new(
        bounds.Position.X + (float)((((double)point.X - bounds.Position.X) % bounds.Size.X + bounds.Size.X) % bounds.Size.X),
        bounds.Position.Y + (float)((((double)point.Y - bounds.Position.Y) % bounds.Size.Y + bounds.Size.Y) % bounds.Size.Y));

    private static void Pan(SimulationCamera2D camera, Vector2 movement)
    {
        camera._UnhandledInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true });
        camera._Input(new InputEventMouseMotion { Relative = movement, ButtonMask = MouseButtonMask.Left });
        camera._Input(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false });
    }
    private static void Wheel(SimulationCamera2D camera, MouseButton button) =>
        camera._UnhandledInput(new InputEventMouseButton { ButtonIndex = button, Pressed = true });
    private void Near(Vector2 actual, Vector2 expected, string message) =>
        Check(actual.DistanceTo(expected) < 0.01f, $"{message}: {actual} != {expected}");
    private void Check(bool condition, string message)
    {
        _checks++;
        if (!condition) throw new InvalidOperationException(message);
    }
}
