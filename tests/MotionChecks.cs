using System;
using System.Linq;
using Godot;

namespace BoidsLab.Tests;

/// <summary>使用真实 Godot 节点验证运动；仅在 --headless 下显式运行测试场景。</summary>
public partial class MotionChecks : Node
{
    private int _checks;

    public override void _Ready()
    {
        if (DisplayServer.GetName() != "headless")
        {
            GD.PushError("Run motion_checks.tscn with --headless.");
            GetTree().Quit(2);
            return;
        }
        try
        {
            CheckLinearMotion();
            CheckAngularMotion();
            CheckManager();
            CheckConfiguration();
            GD.Print($"PASS: {_checks} motion/lifecycle/configuration checks (real Godot nodes, headless).");
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

    private void Near(Vector2 actual, Vector2 expected, float tolerance, string message) =>
        Check(actual.DistanceTo(expected) <= tolerance, $"{message}: {actual} != {expected}");

    private Boid2D MakeBoid(BoidConfig config, Vector2 velocity)
    {
        var boid = new Boid2D();
        boid.Initialize(config, 0, velocity);
        AddChild(boid);
        return boid;
    }

    private void CheckLinearMotion()
    {
        var config = new BoidConfig { MaxSpeed = 1000, MaxForce = 100 };
        Boid2D uniform = MakeBoid(config, new Vector2(12, -5));
        for (int i = 0; i < 60; i++) uniform.UpdateBoid(1.0 / 60);
        Near(uniform.Position, new Vector2(12, -5), 0.001f, "Uniform movement over one second");

        Boid2D at60 = MakeBoid(config, Vector2.Zero);
        Boid2D at120 = MakeBoid(config, Vector2.Zero);
        Vector2 acceleration = new(6, 8);
        at60.SetAcceleration(acceleration);
        at120.SetAcceleration(acceleration);
        for (int i = 0; i < 60; i++) at60.UpdateBoid(1.0 / 60);
        for (int i = 0; i < 120; i++) at120.UpdateBoid(1.0 / 120);
        Near(at60.Velocity, acceleration, 0.001f, "Acceleration integrates velocity with seconds");
        Near(at120.Velocity, at60.Velocity, 0.001f, "Velocity agrees at 60 and 120 steps");
        Near(at60.Position, acceleration * (0.5f * (1 + 1f / 60)), 0.001f, "Semi-implicit position integration");
        Near(at60.Position, at120.Position, 0.05f, "Position error decreases with smaller steps");
        at60.SetAcceleration(Vector2.Zero);
        Vector2 before = at60.Position;
        at60.UpdateBoid(0.5);
        Near(at60.Position - before, acceleration * 0.5f, 0.001f, "Zero acceleration preserves velocity");

        Boid2D limited = MakeBoid(config, Vector2.Zero);
        limited.SetAcceleration(new Vector2(300, 400));
        limited.UpdateBoid(0.1);
        Near(limited.Acceleration, new Vector2(60, 80), 0.001f, "Acceleration magnitude cap preserves direction");
        config.MaxSpeed = 5;
        limited.UpdateBoid(0.1);
        Check(limited.Velocity.Length() <= 5.001f, "Speed magnitude is capped");
        config.MaxForce = 0;
        limited.UpdateBoid(0.1);
        Check(limited.Acceleration == Vector2.Zero, "Zero acceleration limit is supported");
        config.MaxSpeed = 0;
        limited.UpdateBoid(0.1);
        Check(limited.Velocity == Vector2.Zero && limited.Position.IsFinite(), "Zero speed is finite and stops translation");
        bool rejected = false;
        try { limited.UpdateBoid(0); } catch (ArgumentOutOfRangeException) { rejected = true; }
        Check(rejected, "Reject zero time step");
        rejected = false;
        try { limited.SetAcceleration(new Vector2(float.NaN, 0)); } catch (ArgumentOutOfRangeException) { rejected = true; }
        Check(rejected, "Reject non-finite acceleration");
    }

    private void CheckAngularMotion()
    {
        var config = new BoidConfig { MaxSpeed = 1000, MaxAngularSpeedDegrees = 120, MaxAngularAccelerationDegrees = 240 };
        Boid2D boid = MakeBoid(config, Vector2.Right * 100);
        boid.Rotation = -Mathf.Pi / 2;
        boid.UpdateBoid(1.0 / 60);
        Check(boid.Rotation > -Mathf.Pi / 2 && boid.Rotation < 0, "Heading turns gradually, without snapping");
        Check(boid.AngularAcceleration > 0, "Angle error produces angular acceleration");
        Near(boid.Velocity, Vector2.Right * 100, 0.001f, "Rotation does not redirect velocity");
        for (int i = 0; i < 300; i++)
        {
            float previous = boid.AngularVelocity;
            boid.UpdateBoid(1.0 / 60);
            Check(Mathf.Abs(boid.AngularVelocity) <= Mathf.DegToRad(120) + 0.0001f, "Angular speed cap");
            Check(Mathf.Abs(boid.AngularVelocity - previous) * 60 <= Mathf.DegToRad(240) + 0.0001f, "Angular acceleration cap");
        }
        Check(Mathf.Abs(Mathf.AngleDifference(boid.Rotation, 0)) < 0.005f, "Heading converges to velocity direction");

        Boid2D acrossPi = MakeBoid(config, Vector2.FromAngle(Mathf.DegToRad(-179)) * 100);
        acrossPi.Rotation = Mathf.DegToRad(179);
        acrossPi.UpdateBoid(1.0 / 60);
        Check(acrossPi.AngularVelocity > 0, "Use shortest turn across the +/-180 degree boundary");

        Boid2D stopped = MakeBoid(config, Vector2.Right * 100);
        stopped.Rotation = -1;
        stopped.UpdateBoid(1.0 / 60);
        float movingAngularSpeed = Mathf.Abs(stopped.AngularVelocity);
        config.MaxSpeed = 0;
        stopped.UpdateBoid(1.0 / 60);
        Check(Mathf.Abs(stopped.AngularVelocity) < movingAngularSpeed, "Zero linear velocity brakes angular motion");
        Check(Mathf.Abs(stopped.Rotation) > 0.5f, "Stopping does not snap heading to zero");
        config.MaxAngularSpeedDegrees = 0;
        stopped.UpdateBoid(1.0 / 60);
        Check(stopped.AngularVelocity == 0 && float.IsFinite(stopped.Rotation), "Zero angular speed is supported");

        config.MaxSpeed = 1000;
        config.MaxAngularSpeedDegrees = 360;
        config.MaxAngularAccelerationDegrees = 0;
        Boid2D noTorque = MakeBoid(config, Vector2.Right * 100);
        noTorque.Rotation = -1;
        noTorque.UpdateBoid(1.0 / 60);
        Check(noTorque.Rotation == -1 && noTorque.AngularVelocity == 0, "Zero angular acceleration cannot initiate rotation");
    }

    private void CheckManager()
    {
        // 此组隔离验证运动与生命周期；规则本身由 SteeringChecks 验证。
        var config = new BoidConfig { BoidCount = 3, RandomSeed = 42, MaxSpeed = 100,
            SeparationEnabled = false, AlignmentEnabled = false, CohesionEnabled = false };
        BoidManager manager = GD.Load<PackedScene>("res://scenes/worlds/boids_world_2d.tscn").Instantiate<BoidManager>();
        manager.Initialize(config);
        AddChild(manager);
        Check(manager.Boids.Count == 3 && manager.Stats.BoidCount == 3, "Generate actual configured population");
        Check(!manager.IsRunning && !manager.CanStep && manager.CoreImplemented, "Start paused and keep UI single-step disabled");
        var snapshot = manager.Boids.Select(b => (b.Position, b.Velocity, b.Id)).ToArray();
        manager.Reset();
        Check(manager.Boids.Select(b => (b.Position, b.Velocity, b.Id)).SequenceEqual(snapshot), "Reset repeats seeded initial state");
        manager._PhysicsProcess(1.0 / 60);
        Near(manager.Boids[0].Position, snapshot[0].Position, 0, "Pause gates physics updates");
        manager.SetRunning(true);
        manager._PhysicsProcess(1.0 / 60);
        Check(manager.Boids[0].Position != snapshot[0].Position, "Start enables physical movement");
        manager.SetRunning(false);
        Vector2 paused = manager.Boids[0].Position;
        manager._PhysicsProcess(1.0 / 60);
        Near(manager.Boids[0].Position, paused, 0, "Pause freezes movement again");
        Check(manager.Stats.UpdateMilliseconds is >= 0 && manager.Stats.NeighborSearchMilliseconds is >= 0
            && manager.Stats.NeighborCandidateChecks == 6 && manager.Stats.AverageNeighbors is >= 0,
            "Real search and motion measurements are reported");
        manager.Boids[0].SetAcceleration(Vector2.One);
        manager.Reset();
        Check(manager.Boids.All(b => b.Acceleration == Vector2.Zero && b.AngularVelocity == 0), "Reset clears dynamics");
        manager.Step(1.0 / 60);
        Check(manager.Boids.All(b => b.Acceleration == Vector2.Zero), "Reset clears acceleration input too");
        config.RandomSeed = 43;
        config.BoidCount = 4;
        manager.ConfigurationChanged();
        Check(manager.Boids.Count == 3, "Count edits wait for Reset");
        manager.Reset();
        Check(manager.Boids.Count == 4 && manager.Boids[0].Position != snapshot[0].Position, "Reset applies new count and seed");
        config.BoidCount = 0;
        config.MaxSpeed = 1000;
        manager.Reset();
        Check(manager.Stats.BoidCount == 0 && manager.Stats.UpdateMilliseconds is null, "Empty population reports a real zero");
        Rect2 bounds = manager.WorldBounds;
        Boid2D wrapped = manager.SpawnBoid(bounds.End - Vector2.One, new Vector2(50, 50));
        manager.Step(1);
        Near(wrapped.Position, bounds.Position + new Vector2(49, 49), 0.001f, "Wrap keeps overshoot across positive edges");
        manager.RemoveBoid(wrapped);
        Boid2D negative = manager.SpawnBoid(bounds.Position, new Vector2(-50, -50));
        manager.Step(1);
        Near(negative.Position, bounds.End - new Vector2(50, 50), 0.001f, "Wrap works across negative edges");
        CheckCameraBounds(manager);
        manager.QueueFree();
    }

    private void CheckCameraBounds(BoidManager manager)
    {
        var camera = manager.GetNode<SimulationCamera2D>("Camera2D");
        Rect2 home = manager.WorldBounds;
        Rect2 viewport = GetViewport().GetVisibleRect();
        Rect2 screenBounds = manager.GetGlobalTransformWithCanvas() * home;
        Near(screenBounds.Position, viewport.Position, 0.01f, "Boundary starts at the visible viewport corner");
        Near(screenBounds.Size, viewport.Size, 0.01f, "Background/boundary cover the whole viewport");

        Boid2D boid = manager.Boids[0];
        boid.Position = home.End - Vector2.One;
        Vector2 originalPosition = boid.Position;
        Vector2 velocity = boid.Velocity;
        float rotation = boid.Rotation;
        camera._UnhandledInput(new InputEventMouseButton { ButtonIndex = MouseButton.WheelUp, Pressed = true });
        Near(manager.WorldBounds.Size, home.Size / 1.15f, 0.01f, "Zoom-in shrinks the simulation area once");
        Check(!manager.WorldBounds.HasPoint(originalPosition), "Probe starts outside the new zoomed area");
        Check(manager.WorldBounds.HasPoint(boid.Position), "Zoom immediately wraps outside Boids even while paused");
        Near(boid.Position, new Vector2(
            Mathf.Wrap(originalPosition.X, manager.WorldBounds.Position.X, manager.WorldBounds.End.X),
            Mathf.Wrap(originalPosition.Y, manager.WorldBounds.Position.Y, manager.WorldBounds.End.Y)),
            0.01f, "Wrap uses the final cursor-anchored view, without intermediate relocation");
        Near(boid.Velocity, velocity, 0, "Changing bounds preserves velocity");
        Check(boid.Rotation == rotation && !manager.IsRunning && manager.Boids[0] == boid,
            "View changes preserve heading, pause state and individual identity");

        Rect2 beforePan = manager.WorldBounds;
        Vector2 drag = new(120, 60);
        camera._UnhandledInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true });
        camera._Input(new InputEventMouseMotion { Relative = drag, ButtonMask = MouseButtonMask.Left });
        camera._Input(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false });
        Near(manager.WorldBounds.Position, beforePan.Position - drag / camera.Zoom, 0.01f, "Panning translates boundaries with the view");
        Near(manager.WorldBounds.Size, beforePan.Size, 0.01f, "Panning keeps the area size");
        Check(manager.WorldBounds.HasPoint(boid.Position), "Panning also keeps Boids inside the active area");

        manager.Config.BoidCount = 25;
        manager.Reset();
        Check(manager.Boids.All(b => manager.WorldBounds.HasPoint(b.Position)), "Reset generates population in current view");
        camera.FitWorld();
        Near(manager.WorldBounds.Position, home.Position, 0.01f, "Home restores the original center");
        Near(manager.WorldBounds.Size, home.Size, 0.01f, "Dynamic bounds cannot feed back into Home zoom");
        Check(manager.Boids.All(b => manager.WorldBounds.HasPoint(b.Position)), "Home keeps all individual centers inside bounds");
        camera._UnhandledInput(new InputEventMouseButton { ButtonIndex = MouseButton.WheelDown, Pressed = true });
        Near(manager.WorldBounds.Size, home.Size * 1.15f, 0.01f, "Zoom-out expands the simulation area");
        screenBounds = manager.GetGlobalTransformWithCanvas() * manager.WorldBounds;
        Near(screenBounds.Position, viewport.Position, 0.01f, "Zoomed background starts at the screen edge");
        Near(screenBounds.Size, viewport.Size, 0.01f, "Zoomed background still covers the whole viewport");
    }

    private void CheckConfiguration()
    {
        const string path = "res://.godot/validation/motion-test.cfg";
        DirAccess.MakeDirRecursiveAbsolute(ProjectSettings.GlobalizePath("res://.godot/validation"));
        var config = new BoidConfig { MaxAngularSpeedDegrees = 123, MaxAngularAccelerationDegrees = 456 };
        Check(config.Save(path) == Error.Ok, "Save angular limits");
        var loaded = new BoidConfig();
        Check(loaded.Load(path) == Error.Ok && loaded.MaxAngularSpeedDegrees == 123
            && loaded.MaxAngularAccelerationDegrees == 456, "Restore angular limits");
        using (var oldFile = new ConfigFile())
        {
            oldFile.SetValue("simulation", "boid_count", 4);
            Check(oldFile.Save(path) == Error.Ok, "Prepare old configuration without new fields");
        }
        loaded = new BoidConfig();
        Check(loaded.Load(path) == Error.Ok && loaded.MaxAngularSpeedDegrees == 360
            && loaded.MaxAngularAccelerationDegrees == 720, "Old configurations use defaults for new fields");
        DirAccess.RemoveAbsolute(ProjectSettings.GlobalizePath(path));
    }
}
