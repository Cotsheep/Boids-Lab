using System;
using System.Linq;
using System.Threading.Tasks;
using Godot;

namespace BoidsLab.Tests;

/// <summary>角运动限制开关：直接朝向、低速滞回、最终接触速度、显示插值和真实 UI。</summary>
public partial class AngularLimitsChecks : Node
{
    private int _checks;
    private const float Dt = 1f / 60;
    private const string Path2D = "res://.godot/validation/angular-limits-2d.cfg";
    private const string Path3D = "res://.godot/validation/angular-limits-3d.cfg";

    public override async void _Ready()
    {
        try
        {
            CheckDirect2D(); CheckDirect3D(); CheckSwitches(); CheckContact(); CheckPresentation();
            await CheckConfigurationAndUi();
            GD.Print($"PASS: {_checks} angular limit toggle/direct facing/low-speed/contact/presentation assertions.");
            GetTree().Quit();
        }
        catch (Exception error) { GD.PushError(error.ToString()); GetTree().Quit(1); }
    }

    private void Check(bool condition, string message)
    {
        _checks++;
        if (!condition) throw new InvalidOperationException(message);
    }

    private static BoidConfig DirectConfig() => new() { BoidCount = 0, MaxSpeed = 1000, MaxForce = 0,
        AngularMotionLimitsEnabled = false, MaxAngularSpeedDegrees = 0, MaxAngularAccelerationDegrees = 0,
        SeparationEnabled = false, AlignmentEnabled = false, CohesionEnabled = false };

    private Boid2D Two(BoidConfig config, Vector2 velocity)
    {
        var boid = GD.Load<PackedScene>("res://scenes/boids/boid_2d.tscn").Instantiate<Boid2D>();
        boid.Initialize(config, 0, velocity);
        AddChild(boid);
        return boid;
    }

    private Boid3D Three(BoidConfig config, Vector3 velocity)
    {
        var boid = GD.Load<PackedScene>("res://scenes/boids/boid_3d.tscn").Instantiate<Boid3D>();
        boid.Initialize(config, 0, velocity);
        AddChild(boid);
        return boid;
    }

    private void Aligned(Boid2D boid, Vector2 direction, string message) =>
        Check(Vector2.FromAngle(boid.Rotation).DistanceTo(direction.Normalized()) < 0.00002f
            && boid.AngularVelocity == 0 && boid.AngularAcceleration == 0, message);

    private void Aligned(Boid3D boid, Vector3 direction, string message) =>
        Check((boid.Quaternion * Vector3.Forward).DistanceTo(direction.Normalized()) < 0.00003f
            && boid.AngularVelocity == Vector3.Zero && boid.AngularAcceleration == Vector3.Zero
            && Mathf.Abs(boid.Quaternion.Length() - 1) < 0.00001f, message);

    private void CheckDirect2D()
    {
        var config = DirectConfig();
        var boid = Two(config, Vector2.Right * 100);
        try
        {
            boid.Rotation = 1.2f;
            boid.UpdateBoid(Dt);
            Aligned(boid, Vector2.Right, "2D direct mode ignores both zero angular limits");
            Check(boid.Velocity == Vector2.Right * 100 && boid.Acceleration == Vector2.Zero,
                "Direct facing does not change linear motion or its limits");
            foreach (Vector2 direction in new[] { Vector2.Left, Vector2.Up, Vector2.Down, new Vector2(-3, 4).Normalized() })
            {
                boid.ApplyObstacleContact(boid.Position, direction * 100);
                Aligned(boid, direction, "2D exact reversal/contact direction aligns immediately");
            }
            boid.ApplyObstacleContact(boid.Position, Vector2.Right * 0.021f);
            boid.ApplyObstacleContact(boid.Position, Vector2.Up * 0.015f);
            Aligned(boid, Vector2.Up, "2D moving tracking remains valid above the stop threshold");
            boid.ApplyObstacleContact(boid.Position, Vector2.Left * 0.009f);
            float stable = boid.Rotation;
            for (int i = 0; i < 100; i++)
            {
                boid.ApplyObstacleContact(boid.Position, (i % 2 == 0 ? Vector2.Left : Vector2.Down) * 0.011f);
                boid.UpdateBoid(Dt);
                Check(boid.Rotation == stable, "2D near-zero direction noise cannot restart tracking");
            }
            boid.ApplyObstacleContact(boid.Position, Vector2.Right * 0.021f);
            Aligned(boid, Vector2.Right, "2D tracking resumes after crossing the higher threshold");
            config.MaxSpeed = 0;
            boid.UpdateBoid(Dt);
            Aligned(boid, Vector2.Right, "2D zero maximum speed holds the last stable orientation");
        }
        finally { boid.Free(); }
        var tiny = Two(DirectConfig(), new(1e-15f, -1e-15f));
        Check(tiny.Rotation == 0 && float.IsFinite(tiny.Rotation), "Tiny initial 2D speed uses a stable default orientation");
        tiny.Free();
    }

    private void CheckDirect3D()
    {
        var boid = Three(DirectConfig(), Vector3.Right * 100);
        try
        {
            foreach (Vector3 direction in new[] { Vector3.Forward, Vector3.Back, Vector3.Up, Vector3.Down,
                Vector3.Right, new Vector3(0.00001f, 1, -0.00002f).Normalized(),
                new Vector3(0.00002f, -1, 0.00001f).Normalized(), new Vector3(-3, 4, -5).Normalized() })
            {
                boid.ApplyObstacleContact(boid.Position, direction * 100);
                boid.UpdateBoid(Dt);
                Aligned(boid, direction, "3D direct facing remains finite and aligned through poles and 180-degree turns");
            }
            boid.ApplyObstacleContact(boid.Position, Vector3.Right * 0.021f);
            boid.ApplyObstacleContact(boid.Position, Vector3.Up * 0.015f);
            Aligned(boid, Vector3.Up, "3D tracking stays active until the lower stop threshold");
            boid.ApplyObstacleContact(boid.Position, Vector3.Back * 0.009f);
            Quaternion stable = boid.Quaternion;
            for (int i = 0; i < 100; i++)
            {
                boid.ApplyObstacleContact(boid.Position, (i % 2 == 0 ? Vector3.Down : Vector3.Back) * 0.011f);
                boid.UpdateBoid(Dt);
                Check(boid.Quaternion == stable, "3D tiny direction noise cannot restart tracking");
            }
            boid.ApplyObstacleContact(boid.Position, Vector3.Forward * 0.021f);
            Aligned(boid, Vector3.Forward, "3D direction following resumes at meaningful speed");
            boid.ApplyObstacleContact(boid.Position, Vector3.Zero);
            Aligned(boid, Vector3.Forward, "3D zero velocity preserves the last stable orientation");
        }
        finally { boid.Free(); }
        var tiny = Three(DirectConfig(), new(1e-15f, -1e-15f, 1e-15f));
        Check(tiny.Quaternion == Quaternion.Identity, "Tiny initial 3D speed uses stable identity orientation");
        tiny.Free();
    }

    private void CheckSwitches()
    {
        var config = new BoidConfig();
        var two = Two(config, Vector2.Right * 100);
        var three = Three(config, Vector3.Right * 100);
        try
        {
            two.Rotation = -1;
            three.Quaternion = Quaternion.Identity;
            two.UpdateBoid(Dt); three.UpdateBoid(Dt);
            Check(two.AngularVelocity != 0 && three.AngularVelocity != Vector3.Zero, "Default mode retains bounded angular dynamics");
            Vector2 position2 = two.Position, velocity2 = two.Velocity;
            Vector3 position3 = three.Position, velocity3 = three.Velocity;
            config.AngularMotionLimitsEnabled = false;
            two.RefreshFacingConfiguration(); three.RefreshFacingConfiguration();
            Aligned(two, Vector2.Right, "Disabling clears existing 2D angular momentum");
            Aligned(three, Vector3.Right, "Disabling clears existing 3D angular momentum");
            Check(two.Position == position2 && two.Velocity == velocity2 && three.Position == position3 && three.Velocity == velocity3,
                "Configuration refresh advances neither dimension's position or linear velocity");
            config.AngularMotionLimitsEnabled = true;
            two.RefreshFacingConfiguration(); three.RefreshFacingConfiguration();
            two.Rotation = -1;
            three.Quaternion = Quaternion.Identity;
            two.UpdateBoid(Dt); three.UpdateBoid(Dt);
            float angularStep = Mathf.DegToRad(config.MaxAngularAccelerationDegrees) * Dt;
            Check(Mathf.Abs(two.AngularVelocity) <= angularStep + 0.00001f && three.AngularVelocity.Length() <= angularStep + 0.00001f,
                "Re-enabling starts from zero angular momentum and restores both acceleration limits");
            config.MaxAngularSpeedDegrees = 0;
            float rotation = two.Rotation;
            Quaternion orientation = three.Quaternion;
            two.UpdateBoid(Dt); three.UpdateBoid(Dt);
            Check(two.Rotation == rotation && three.Quaternion == orientation, "Re-enabled zero angular speed locks orientation again");
        }
        finally { two.Free(); three.Free(); }
    }

    private BoidManager World2D(BoidConfig config)
    {
        var world = GD.Load<PackedScene>("res://scenes/worlds/boids_world_2d.tscn").Instantiate<BoidManager>();
        world.Initialize(config); AddChild(world);
        ObstacleHarness2D.ClearLayout(world);
        return world;
    }

    private BoidManager3D World3D(BoidConfig config)
    {
        var world = GD.Load<PackedScene>("res://scenes/worlds/boids_world_3d.tscn").Instantiate<BoidManager3D>();
        world.Initialize(config); AddChild(world);
        var editor = world.GetNode<ObstacleEditor3D>("ObstacleEditor");
        foreach (var obstacle in editor.Obstacles.ToArray()) obstacle.Free();
        editor.SynchronizeQuery();
        return world;
    }

    private void CheckContact()
    {
        var two = World2D(DirectConfig());
        var three = World3D(DirectConfig());
        try
        {
            Vector2 center = two.ViewBounds.GetCenter();
            var wall2 = two.GetNode<ObstacleEditor2D>("ObstacleEditor").AddObstacle(ObstacleShape.Rectangle, center);
            wall2.SetDimensions(new(16, 1000));
            var boid2 = two.SpawnBoid(center - new Vector2(50, 50), new(100, 100));
            two.Step(1);
            Check(boid2.Velocity.X <= 0.01f && boid2.Velocity.Y > 90,
                $"2D fixture produces real surface sliding: {boid2.Velocity}");
            Aligned(boid2, boid2.Velocity, "2D appearance follows final contact velocity in the same step");
            var wall3 = three.GetNode<ObstacleEditor3D>("ObstacleEditor").AddObstacle(ObstacleShape.Box, Vector3.Zero);
            wall3.SetDimensions(new(16, 1000, 1000));
            var boid3 = three.SpawnBoid(new(-50, -50, 0), new(100, 100, 0));
            three.SetRunning(true);
            three.Step(1);
            three.GetNode<SimulationView3D>("SimulationView").PreparePresentation(0.25f);
            Check(boid3.Velocity.X <= 0.01f && boid3.Velocity.Y > 90,
                $"3D fixture produces real surface sliding: {boid3.Velocity}");
            Aligned(boid3, boid3.Velocity, "3D physical orientation matches final contact velocity");
            Check((boid3.DisplayTransform.Basis * Vector3.Forward).DistanceTo(boid3.Velocity.Normalized()) < 0.00003f,
                "3D displayed orientation has no angular interpolation delay after contact");
            Check(boid3.GetNode<MeshInstance3D>("Body").GlobalBasis.Y.Normalized().DistanceTo(boid3.Velocity.Normalized()) < 0.00003f,
                "The actual cone tip follows final contact velocity after the mesh rest rotation");
        }
        finally { two.Free(); three.Free(); }
    }

    private void CheckPresentation()
    {
        var config = new BoidConfig { BoidCount = 0, MaxForce = 0 };
        var world = World3D(config);
        try
        {
            var boid = world.SpawnBoid(Vector3.Zero, Vector3.Right * 100);
            boid.Quaternion = Quaternion.Identity;
            world.SetRunning(true);
            world.Step(0.1);
            world.GetNode<SimulationView3D>("SimulationView").PreparePresentation(0.4f);
            Vector3 displayed = boid.DisplayPosition;
            Vector3 position = boid.Position;
            config.AngularMotionLimitsEnabled = false;
            world.ConfigurationChanged();
            Check(boid.Position == position && boid.DisplayPosition.IsEqualApprox(displayed),
                "Toggling while interpolating retains physical and displayed position");
            Aligned(boid, Vector3.Right, "Live toggle aligns physical orientation immediately");
            Check((boid.DisplayTransform.Basis * Vector3.Forward).DistanceTo(Vector3.Right) < 0.00003f,
                "Live toggle aligns the visible mesh immediately");
            Check(boid.GetNode<MeshInstance3D>("Body").GlobalBasis.Y.Normalized().DistanceTo(Vector3.Right) < 0.00003f,
                "The rendered cone tip immediately matches the new facing mode");
            config.AngularMotionLimitsEnabled = true;
            world.ConfigurationChanged();
            Check(boid.DisplayPosition.IsEqualApprox(displayed) && boid.AngularVelocity == Vector3.Zero,
                "Re-enabling keeps position interpolation and clears angular history");
        }
        finally { world.Free(); }
    }

    private async Task CheckConfigurationAndUi()
    {
        using (var legacy = new ConfigFile())
        {
            legacy.SetValue("simulation", "boid_count", 1);
            legacy.Save(Path2D);
        }
        var loaded = new BoidConfig();
        Check(loaded.Load(Path2D) == Error.Ok && loaded.AngularMotionLimitsEnabled, "Old files keep angular limits enabled");
        new BoidConfig { BoidCount = 1 }.Save(Path2D);
        new BoidConfig { BoidCount = 1 }.Save(Path3D);
        var app = GD.Load<PackedScene>("res://scenes/main.tscn").Instantiate<MainController>();
        app.World2D = (SimulationWorldDefinition)app.World2D.Duplicate();
        app.World3D = (SimulationWorldDefinition)app.World3D.Duplicate();
        app.World2D.ConfigurationPath = Path2D; app.World3D.ConfigurationPath = Path3D;
        AddChild(app);
        try
        {
            await Frame();
            for (int dimension = 0; dimension < 2; dimension++)
            {
                if (dimension != app.CurrentWorldIndex) { app.RequestWorldSwitch(dimension); await Frame(); }
                var panel = app.GetNode<ControlPanel>("%ControlPanel");
                var toggle = (CheckBox)panel.FindChild("AngularMotionLimits", true, false);
                var speed = (SpinBox)panel.FindChild("MaxAngularSpeed", true, false);
                var acceleration = (SpinBox)panel.FindChild("MaxAngularAcceleration", true, false);
                Check(toggle.ButtonPressed && speed.Editable && acceleration.Editable, "Both modes expose one enabled switch for both limits");
                speed.Value = dimension == 0 ? 33 : 99;
                acceleration.Value = dimension == 0 ? 66 : 198;
                var two = dimension == 0 ? app.GetNode<BoidManager>("World/BoidsWorld2D") : null;
                var three = dimension == 1 ? app.GetNode<BoidManager3D>("World/BoidsWorld3D") : null;
                Vector2 p2 = two?.Boids[0].Position ?? Vector2.Zero;
                Vector3 p3 = three?.Boids[0].Position ?? Vector3.Zero;
                toggle.ButtonPressed = false;
                Check(!speed.Editable && !acceleration.Editable && speed.Value == (dimension == 0 ? 33 : 99)
                    && acceleration.Value == (dimension == 0 ? 66 : 198), "Disabled numeric inputs retain both saved limit values");
                if (two is not null)
                {
                    Check(!two.IsRunning && two.Boids[0].Position == p2, "Paused 2D toggle advances no simulation position");
                    Aligned(two.Boids[0], two.Boids[0].Velocity, "Paused 2D toggle updates orientation immediately");
                    var untouched = new BoidConfig(); untouched.Load(Path3D);
                    Check(untouched.AngularMotionLimitsEnabled, "2D preference does not affect 3D");
                }
                if (three is not null)
                {
                    Check(!three.IsRunning && three.Boids[0].Position == p3, "Paused 3D toggle advances no simulation position");
                    Aligned(three.Boids[0], three.Boids[0].Velocity, "Paused 3D toggle updates orientation immediately");
                }
                loaded = new BoidConfig(); loaded.Load(dimension == 0 ? Path2D : Path3D);
                Check(!loaded.AngularMotionLimitsEnabled, "UI saves the direct-facing mode independently for each world");
                toggle.ButtonPressed = true;
                Check(speed.Editable && acceleration.Editable && speed.Value == (dimension == 0 ? 33 : 99),
                    "Enabling restores editability and previous numeric limits");
                toggle.ButtonPressed = false;
            }
            app.RequestWorldSwitch(0); await Frame();
            var restored = app.GetNode<ControlPanel>("%ControlPanel");
            Check(!((CheckBox)restored.FindChild("AngularMotionLimits", true, false)).ButtonPressed
                && !((SpinBox)restored.FindChild("MaxAngularSpeed", true, false)).Editable
                && app.GetNode<BoidManager>("World/BoidsWorld2D").Config.MaxAngularSpeedDegrees == 33,
                "Returning to a world restores its own mode and numeric limits");
        }
        finally { app.Free(); }
    }

    private async Task Frame()
    {
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }
}
