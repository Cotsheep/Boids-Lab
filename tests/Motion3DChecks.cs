using System;
using System.Linq;
using Godot;

namespace BoidsLab.Tests;

/// <summary>真实 Godot 三维节点与共享 HUD 的回归检查；只通过无图形测试场景运行。</summary>
public partial class Motion3DChecks : Node
{
    private int _checks;

    public override async void _Ready()
    {
        if (DisplayServer.GetName() != "headless")
        {
            GD.PushError("Run motion_3d_checks.tscn with --headless.");
            GetTree().Quit(2);
            return;
        }
        try
        {
            CheckMotion();
            CheckFacing();
            CheckWorld();
            MainController app = CheckSharedUi();
            var world = app.GetNode<BoidManager3D>("World/BoidsWorld3D");
            Boid3D first = world.Boids[0];
            Vector3 initial = first.Position;
            Press(app, "toggle_pause");
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
            Check(first.Position != initial, "Native physics callbacks advance the running 3D world");
            Press(app, "toggle_pause");
            Vector3 paused = first.Position;
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
            Near(first.Position, paused, 0, "Pause stops actual physics updates");
            GD.Print($"PASS: {_checks} 3D motion/world/shared-UI checks (real Godot nodes, headless).");
            GetTree().Quit();
        }
        catch (Exception error)
        {
            GD.PushError(error.ToString());
            GetTree().Quit(1);
        }
    }

    private Boid3D MakeBoid(BoidConfig config, Vector3 velocity)
    {
        var boid = new Boid3D();
        boid.Initialize(config, 0, velocity);
        AddChild(boid);
        return boid;
    }

    private void CheckMotion()
    {
        var config = new BoidConfig { MaxSpeed = 1000, MaxForce = 100 };
        Boid3D uniform = MakeBoid(config, new Vector3(12, -5, 8));
        for (int i = 0; i < 60; i++) uniform.UpdateBoid(1.0 / 60);
        Near(uniform.Position, new Vector3(12, -5, 8), 0.001f, "Constant velocity uses seconds in all axes");

        Boid3D at60 = MakeBoid(config, Vector3.Zero);
        Boid3D at120 = MakeBoid(config, Vector3.Zero);
        Vector3 acceleration = new(6, 8, -4);
        at60.SetAcceleration(acceleration);
        at120.SetAcceleration(acceleration);
        for (int i = 0; i < 60; i++) at60.UpdateBoid(1.0 / 60);
        for (int i = 0; i < 120; i++) at120.UpdateBoid(1.0 / 120);
        Near(at60.Velocity, acceleration, 0.001f, "Linear acceleration integrates velocity");
        Near(at60.Velocity, at120.Velocity, 0.001f, "Velocity agrees at 60 and 120 steps per second");
        Near(at60.Position, acceleration * (0.5f * (1 + 1f / 60)), 0.001f, "Semi-implicit position integration");
        Near(at60.Position, at120.Position, 0.05f, "Position converges as the step shrinks");
        at60.SetAcceleration(Vector3.Zero);
        Vector3 before = at60.Velocity;
        at60.UpdateBoid(0.1, new Vector3(1, 2, 3));
        Near(at60.Velocity, before + new Vector3(0.1f, 0.2f, 0.3f), 0.001f, "Per-step steering is applied");
        at60.UpdateBoid(0.1);
        Near(at60.Acceleration, Vector3.Zero, 0, "Per-step input does not persist");

        Boid3D limited = MakeBoid(config, Vector3.Zero);
        limited.SetAcceleration(new Vector3(1000, 1000, -1000));
        limited.UpdateBoid(0.1, new Vector3(-1000, 1000, 1000));
        Near(limited.Acceleration, new Vector3(0, 100, 0), 0.001f, "Inputs combine before magnitude limiting");
        config.MaxSpeed = 2;
        limited.UpdateBoid(0.1);
        Check(limited.Velocity.Length() <= 2.001f, "Live speed limit clamps the shared configuration");
        config.MaxForce = 0;
        limited.UpdateBoid(0.1);
        Near(limited.Acceleration, Vector3.Zero, 0, "Zero force disables acceleration");
        config.MaxSpeed = 0;
        limited.UpdateBoid(0.1);
        Near(limited.Velocity, Vector3.Zero, 0, "Zero maximum speed stops linear movement");
        ExpectArgument(() => uniform.UpdateBoid(0), "Zero step rejected");
        ExpectArgument(() => uniform.UpdateBoid(double.NaN), "Non-finite step rejected");
        ExpectArgument(() => uniform.SetAcceleration(new Vector3(float.NaN, 0, 0)), "Invalid acceleration rejected");
    }

    private void CheckFacing()
    {
        var config = new BoidConfig { MaxSpeed = 120, MaxForce = 200 };
        foreach (Vector3 direction in new[] { Vector3.Right, Vector3.Up, Vector3.Down, Vector3.Back, new Vector3(1, 2, -3).Normalized() })
        {
            Boid3D boid = MakeBoid(config, direction * 100);
            Near(boid.Quaternion * Vector3.Forward, direction, 0.001f, "Initial -Z faces the velocity including vertical directions");
            boid.Quaternion = Quaternion.Identity;
            for (int i = 0; i < 600; i++)
            {
                boid.UpdateBoid(1.0 / 120);
                Check(boid.Quaternion.IsFinite() && Mathf.Abs(boid.Quaternion.Length() - 1) < 0.0001f, "Facing remains a finite unit quaternion");
                Check(boid.AngularVelocity.Length() <= Mathf.DegToRad(config.MaxAngularSpeedDegrees) + 0.001f, "Angular speed stays bounded");
                Check(boid.AngularAcceleration.Length() <= Mathf.DegToRad(config.MaxAngularAccelerationDegrees) + 0.01f, "Angular acceleration stays bounded");
            }
            Near(boid.Quaternion * Vector3.Forward, direction, 0.005f, "Smooth facing converges, including an exact 180 degree turn");
        }
        Boid3D stationary = MakeBoid(config, Vector3.Zero);
        stationary.UpdateBoid(1.0 / 60);
        Check(stationary.Quaternion == Quaternion.Identity && stationary.AngularVelocity == Vector3.Zero,
            "A stationary boid has no arbitrary facing target");
        Boid3D locked = MakeBoid(config, Vector3.Right);
        locked.Quaternion = Quaternion.Identity;
        config.MaxAngularAccelerationDegrees = 0;
        locked.UpdateBoid(1.0 / 60);
        Check(locked.Quaternion == Quaternion.Identity, "Zero angular acceleration preserves the stationary orientation");
    }

    private void CheckWorld()
    {
        var config = new BoidConfig { BoidCount = 24, RandomSeed = 1234,
            SeparationEnabled = false, AlignmentEnabled = false, CohesionEnabled = false }; // 单独验证运动与边界。
        var world = GD.Load<PackedScene>("res://scenes/worlds/boids_world_3d.tscn").Instantiate<BoidManager3D>();
        world.Initialize(config);
        AddChild(world);
        Check(ReferenceEquals(world.Config, config) && world.Boids.Count == 24 && !world.IsRunning, "World starts paused with the shared configuration");
        var states = world.Boids.Select(boid => (boid.Position, boid.Velocity, boid.Quaternion)).ToArray();
        Check(world.Boids.All(boid => world.WorldBounds.HasPoint(boid.Position)), "Spawn positions lie inside the 3D volume");
        world.Reset();
        Check(world.Boids.Select(boid => (boid.Position, boid.Velocity, boid.Quaternion)).SequenceEqual(states), "Reset reproduces the seeded population");
        config.RandomSeed++;
        world.Reset();
        Check(world.Boids[0].Position != states[0].Position, "A new seed changes the population");
        config.BoidCount = 0;
        world.ConfigurationChanged();
        Check(world.Boids.Count == 24, "Target count changes only on reset");
        world.Reset();
        Check(world.Boids.Count == 0 && world.Stats.BoidCount == 0, "Zero population reset is valid");
        world.Step(1.0 / 60);
        Check(world.Stats.UpdateMilliseconds.HasValue && world.Stats.NeighborCandidateChecks == 0
            && world.Stats.AverageNeighbors == 0 && world.Stats.NeighborSearchMilliseconds.HasValue,
            "Empty-world neighbor measurements are actual zero values after a step");

        config.MaxForce = 0; // Isolate continuous motion from the independently checked soft boundary.
        Boid3D spawned = world.SpawnBoid(world.WorldBounds.End - Vector3.One, new Vector3(10, 20, 30));
        var readOnlyView = world.Boids;
        Vector3 speed = spawned.Velocity;
        Quaternion facing = spawned.Quaternion;
        world.Step(1);
        Near(spawned.Position, world.WorldBounds.End + new Vector3(9, 19, 29), 0.001f, "Positive crossings remain continuous on all axes");
        Near(spawned.Velocity, speed, 0, "Boundary crossing preserves velocity");
        Check(spawned.Quaternion.IsEqualApprox(facing), "Boundary crossing preserves orientation");
        Boid3D negative = world.SpawnBoid(world.WorldBounds.Position + Vector3.One, new Vector3(-10, -20, -30));
        world.Step(1);
        Near(negative.Position, world.WorldBounds.Position - new Vector3(9, 19, 29), 0.001f, "Negative crossings remain continuous on all axes");
        Boid3D far = world.SpawnBoid(world.WorldBounds.Position + world.WorldSize * new Vector3(4, -3, 5) + new Vector3(3, 4, 5), Vector3.Zero);
        Near(far.Position, world.WorldBounds.Position + world.WorldSize * new Vector3(4, -3, 5) + new Vector3(3, 4, 5), 0.001f, "Spawning outside the box preserves actual coordinates");
        int removedId = negative.Id;
        world.RemoveBoid(negative);
        Check(readOnlyView.Count == 2 && world.Stats.BoidCount == 2, "Read-only collection reflects dynamic removal");
        Boid3D added = world.SpawnBoid(Vector3.Zero, Vector3.Zero);
        Check(added.Id > removedId && world.Boids.Select(b => b.Id).Distinct().Count() == 3, "Dynamic additions have unique IDs");
        world.SetRunning(true);
        world.Reset();
        Check(!world.IsRunning && world.Stats.UpdateMilliseconds is null, "Reset pauses and invalidates old timing");

        var camera = world.GetNode<Camera3D>("SimulationView/Camera3D");
        Check(camera.Current && camera.Projection == Camera3D.ProjectionType.Orthogonal, "Active native 3D camera is configured");
        for (int i = 0; i < 8; i++)
        {
            Vector3 corner = world.WorldBounds.Position + new Vector3((i & 1) == 0 ? 0 : world.WorldSize.X,
                (i & 2) == 0 ? 0 : world.WorldSize.Y, (i & 4) == 0 ? 0 : world.WorldSize.Z);
            Check(camera.IsPositionInFrustum(world.ToGlobal(corner)), "Camera frames all eight corners of the volume");
        }
        var environment = world.GetNode<WorldEnvironment>("SimulationView/WorldEnvironment").Environment;
        Check(environment.AmbientLightSource == Godot.Environment.AmbientSource.Color, "Ambient illumination does not require a sky asset");
        world.QueueFree();
    }

    private MainController CheckSharedUi()
    {
        const string testPath = "res://.godot/validation/3d-ui-test.cfg";
        var initial = new BoidConfig { BoidCount = 8, RandomSeed = 314 };
        Check(initial.Save(testPath) == Error.Ok, "Test configuration saved outside real user preferences");
        var app = GD.Load<PackedScene>("res://scenes/main_3d.tscn").Instantiate<MainController>();
        Check(app.ConfigurationPath == "user://boids_lab_3d.cfg", "3D uses its own persistent settings file");
        app.World3D = (SimulationWorldDefinition)app.World3D.Duplicate();
        app.World3D.ConfigurationPath = testPath;
        AddChild(app);
        var world = app.GetNode<BoidManager3D>("World/BoidsWorld3D");
        var controls = app.GetNode<ControlPanel>("%ControlPanel");
        Check(world.Boids.Count == 8 && world.Config.RandomSeed == 314, "Inherited main scene loads configuration and instantiates the 3D world");
        Check(controls.Obstacles is not null && controls.Obstacles.FindChild("AddBox", true, false) is Button
            && controls.FindChild("IsLeader", true, false) is CheckBox
            && controls.FindChild("LeaderFollowDistance", true, false) is SpinBox
            && controls.FindChild("LeaderFollowWeight", true, false) is SpinBox, "3D obstacle and leader controls are available");
        Check(controls.FindChild("SeparationEnabled", true, false) is CheckBox
            && controls.FindChild("AlignmentEnabled", true, false) is CheckBox
            && controls.FindChild("CohesionEnabled", true, false) is CheckBox, "All three implemented rules have controls");
        ((CheckBox)controls.FindChild("ShowSeparationRadius", true, false)).ButtonPressed = true;
        ((SpinBox)controls.FindChild("SeparationWeight", true, false)).Value = 2.3;
        ((CheckBox)controls.FindChild("AlignmentEnabled", true, false)).ButtonPressed = false;
        var rulesSaved = new BoidConfig();
        Check(rulesSaved.Load(testPath) == Error.Ok && rulesSaved.ShowSeparationRadius && !rulesSaved.AlignmentEnabled
            && Mathf.IsEqualApprox(rulesSaved.SeparationWeight, 2.3f) && world.Config.ShowSeparationRadius,
            "3D rule controls and separation display update the shared configuration and persist");
        var searchMode = (OptionButton)controls.FindChild("NeighborSearchMode", true, false);
        Check(!searchMode.Disabled && searchMode.ItemCount == 2 && searchMode.GetItemId(1) == (int)NeighborSearchMode.UniformGrid,
            "3D offers both implemented search methods");
        Check(controls.FindChild("ShowNeighbors", true, false) is CheckBox, "Implemented debug controls are available");
        var count = (SpinBox)controls.FindChild("BoidCount", true, false);
        count.GetLineEdit().Text = "12";
        count.GetLineEdit().EmitSignal(LineEdit.SignalName.TextSubmitted, "12");
        Check(world.Config.BoidCount == 12 && world.Boids.Count == 12, "Submitting population count updates configuration and existing population");
        var saved = new BoidConfig();
        Check(saved.Load(testPath) == Error.Ok && saved.BoidCount == 12, "UI writes to its isolated configuration path");
        ((Button)controls.FindChild("Reset", true, false)).EmitSignal(Button.SignalName.Pressed);
        Check(world.Boids.Count == 12 && !world.IsRunning, "Shared reset button regenerates the 3D population");
        var body = world.Boids[0].GetNode<MeshInstance3D>("Body");
        Check(body.Mesh is CylinderMesh, "Each 3D individual is rendered by a native mesh node");
        Near(body.Basis * Vector3.Up, Vector3.Forward, 0.001f, "Cone tip agrees with the boid -Z facing convention");
        Check(!((Button)controls.FindChild("Step", true, false)).Disabled, "Paused 3D world allows single-step");
        Check(app.GetNodeOrNull<Control>("%CorePlaceholder") is null, "Main scene has no obsolete development placeholder");
        Press(app, "toggle_ui");
        Check(!app.GetNode<Control>("%UIRoot").Visible, "F1 hides the shared HUD");
        Press(app, "toggle_help");
        var help = app.GetNode<HelpOverlay>("%HelpOverlay");
        Check(help.Visible && help.GetNode<RichTextLabel>("Card/Column/Instructions").Text.Contains("三维观察与领队飞行"), "F2 shows 3D instructions even with HUD hidden");
        Press(app, "close_help");
        Press(app, "toggle_ui");
        return app;
    }

    private static void Press(MainController app, string action)
    {
        using var input = new InputEventAction { Action = action, Pressed = true };
        app._Input(input);
    }

    private void ExpectArgument(Action action, string message)
    {
        try { action(); }
        catch (ArgumentOutOfRangeException) { Check(true, message); return; }
        Check(false, message);
    }

    private void Near(Vector3 actual, Vector3 expected, float tolerance, string message) =>
        Check(actual.DistanceTo(expected) <= tolerance, $"{message}: {actual} != {expected}");

    private void Check(bool condition, string message)
    {
        _checks++;
        if (!condition) throw new InvalidOperationException(message);
    }
}
