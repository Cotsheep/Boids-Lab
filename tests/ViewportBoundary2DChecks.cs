using System;
using System.Linq;
using System.Threading.Tasks;
using Godot;

namespace BoidsLab.Tests;

/// <summary>二维视口软边界：连续运动、相机/身份切换、真实查询和配置/界面隔离。</summary>
public partial class ViewportBoundary2DChecks : Node
{
    private int _checks;
    private const float Dt = 1f / 60;
    private const string Config2D = "res://.godot/validation/viewport-boundary-2d.cfg";
    private const string Config3D = "res://.godot/validation/viewport-boundary-3d.cfg";

    public override async void _Ready()
    {
        try
        {
            GetWindow().MinSize = new(960, 640);
            CheckReturnAndLimits();
            CheckTopologyAndTransitions();
            CheckLeaderAndObstacles();
            await CheckCamera();
            await CheckConfigurationAndUi();
            GD.Print($"PASS: {_checks} 2D viewport soft boundary/mode/camera/configuration assertions.");
            GetTree().Quit();
        }
        catch (Exception error) { GD.PushError(error.ToString()); GetTree().Quit(1); }
    }

    private void Check(bool condition, string message)
    {
        _checks++;
        if (!condition) throw new InvalidOperationException(message);
    }

    private BoidManager World()
    {
        var world = GD.Load<PackedScene>("res://scenes/worlds/boids_world_2d.tscn").Instantiate<BoidManager>();
        world.WorldSize = new(400, 300);
        world.Initialize(new BoidConfig { BoidCount = 0, MaxSpeed = 60, MaxForce = 120,
            ViewportBoundaryEnabled = true, LeaderFollowWeight = 0,
            SeparationEnabled = false, AlignmentEnabled = false, CohesionEnabled = false });
        AddChild(world);
        ObstacleHarness2D.ClearLayout(world);
        return world;
    }

    private void CheckReturnAndLimits()
    {
        var world = World();
        try
        {
            Rect2 bounds = world.ViewportBoundaryBounds;
            Check(bounds.Size.IsEqualApprox(world.ViewBounds.Size * 1.2f)
                && bounds.GetCenter().IsEqualApprox(world.ViewBounds.GetCenter()),
                "Soft boundary is centered on the viewport with 10 percent extra space on each side");
            Check(world.IsViewportBoundaryActive && world.WrapBounds is null, "Enabled observation uses direct coordinates");
            foreach (Vector2 direction in new[] { Vector2.Right, Vector2.Left, Vector2.Up, Vector2.Down,
                new Vector2(1, 1).Normalized(), new Vector2(-1, 1).Normalized(),
                new Vector2(1, -1).Normalized(), new Vector2(-1, -1).Normalized() })
            {
                Vector2 start = bounds.GetCenter() + direction.Sign() * (bounds.Size * 0.5f + Vector2.One * 100);
                var boid = world.SpawnBoid(start, direction * 60);
                Check(boid.Position == start, "Outside spawn remains at its actual position");
                world.Step(Dt);
                Check(boid.Position.DistanceTo(start) <= 1.001f && boid.Acceleration.Dot(direction) < 0,
                    "Faces and corners turn inward without a position jump");
                for (int i = 0; i < 900; i++)
                {
                    Vector2 before = boid.Position;
                    world.Step(Dt);
                    Check(boid.Position.DistanceTo(before) <= 1.001f && boid.Velocity.Length() <= 60.001f
                        && boid.Acceleration.Length() <= 120.001f, "Continuous recovery respects motion limits");
                }
                Check(bounds.HasPoint(boid.Position), "Every face and corner returns inside the expanded soft boundary");
                world.RemoveBoid(boid);
            }
            var outside = world.SpawnBoid(bounds.End + new Vector2(100, 100), Vector2.Right * 60);
            Vector2 position = outside.Position;
            world.Config.MaxForce = 0;
            world.Step(1);
            Check(outside.Position.IsEqualApprox(position + Vector2.Right * 60), "Zero acceleration never forces a return");
            world.Config.MaxForce = 120;
            var sample = new BoidSnapshot(0, new(bounds.End.X - 1, bounds.GetCenter().Y), Vector2.Right * 60);
            Check(BoundarySteering2D.Apply(sample, bounds, world.Config, Vector2.Right * 120).X < 0,
                "Outward flocking cannot cancel an inward boundary request");
            Vector2 centralForce = new(10, -15);
            Check(BoundarySteering2D.Apply(new(0, bounds.GetCenter(), Vector2.Zero), bounds, world.Config, centralForce) == centralForce,
                "The central region preserves normal steering");
        }
        finally { world.Free(); }
    }

    private void CheckTopologyAndTransitions()
    {
        foreach (NeighborSearchMode mode in Enum.GetValues<NeighborSearchMode>())
        {
            var world = World();
            try
            {
                world.Config.NeighborSearchMode = mode;
                world.Config.MaxForce = 0;
                world.Config.PerceptionRadius = 10;
                world.Config.ShowNeighbors = world.Config.ShowPerceptionRadius = true;
                Rect2 bounds = world.ViewBounds;
                var a = world.SpawnBoid(new(bounds.Position.X + 2, bounds.GetCenter().Y), Vector2.Zero);
                var b = world.SpawnBoid(new(bounds.End.X - 2, bounds.GetCenter().Y), Vector2.Zero);
                world.Step(Dt);
                Check(world.NeighborSearch.GetNeighborIndices(0).Count == 0, "Soft boundaries use Euclidean neighbor distance in either search");
                var geometry = new DebugOverlayGeometry();
                geometry.Build(world.Boids, a, world.Config, bounds, 1, world.WrapBounds.HasValue);
                Check(geometry.NeighborCount == 0 && geometry.RangePoints.All(point => point.X < bounds.Position.X + 13),
                    "Soft boundary debug does not show opposite-edge neighbors or periodic ranges");
                a.Position = bounds.End + new Vector2(30, 40);
                Vector2 velocity = a.Velocity;
                int id = a.Id;
                world.Config.ViewportBoundaryEnabled = false;
                world.ConfigurationChanged();
                Check(world.WrapBounds == bounds && !world.NeighborSearch.HasResults && bounds.HasPoint(a.Position)
                    && a.Id == id && a.Velocity == velocity && !world.IsRunning,
                    "Disabling restores original wrapping and invalidates old topology without resetting identity");
                a.Position = new(bounds.Position.X + 2, bounds.GetCenter().Y);
                world.Step(Dt);
                Check(world.NeighborSearch.GetNeighborIndices(0).SequenceEqual(new[] { 1 }), "Disabled setting restores across-edge neighbors");
                a.Position = bounds.Position - new Vector2(70, 80);
                Vector2 outside = a.Position;
                world.Config.ViewportBoundaryEnabled = true;
                world.ConfigurationChanged();
                Check(world.IsViewportBoundaryActive && a.Position == outside && !world.NeighborSearch.HasResults,
                    "Enabling while paused leaves outside population in place and clears periodic results");
                world.Step(Dt);
                Check(a.Position == outside && world.NeighborSearch.GetNeighborIndices(0).Count == 0,
                    "Soft boundary does not reintroduce a wrap during the next fixed step");
                world.SetRunning(true);
                world.Config.ViewportBoundaryEnabled = false;
                world.ConfigurationChanged();
                world.Config.ViewportBoundaryEnabled = true;
                world.ConfigurationChanged();
                Check(world.IsRunning && a.Id == id && a.Velocity == velocity,
                    "Toggling during a run preserves running state, identity and velocity");
                world.SetRunning(false);
            }
            finally { world.Free(); }
        }
    }

    private void CheckLeaderAndObstacles()
    {
        var world = World();
        try
        {
            var a = world.SpawnBoid(world.ViewBounds.End + new Vector2(100, 0), Vector2.Right * 60);
            var leader = world.SpawnBoid(world.ViewBounds.GetCenter(), Vector2.Zero);
            world.SetLeader(leader);
            Check(!world.IsViewportBoundaryActive && world.WrapBounds is null, "Leader identity temporarily disables soft boundaries");
            Vector2 before = a.Position;
            world.Step(Dt);
            Check(a.Acceleration == Vector2.Zero && a.Position.X > before.X, "Leader mode preserves unbounded motion with follow weight zero");
            before = a.Position;
            world.Config.ViewportBoundaryEnabled = false;
            world.ConfigurationChanged();
            world.Config.ViewportBoundaryEnabled = true;
            world.ConfigurationChanged();
            world.SetLeader(a);
            Check(a.Position == before && world.WrapBounds is null, "Editing the setting and transferring identity never briefly wrap leader mode");
            world.SetLeader(null);
            Check(world.IsViewportBoundaryActive && a.Position == before && !world.NeighborSearch.HasResults,
                "Removing leader restores soft boundaries and invalidates old results without moving anybody");
            world.Step(Dt);
            Check(a.Acceleration.X < 0, "An outside former leader receives inward steering on the next step");
            world.RemoveBoid(a); world.RemoveBoid(leader);
            world.Config.MaxSpeed = 120;
            world.Config.MaxForce = 200;
            var editor = world.GetNode<ObstacleEditor2D>("ObstacleEditor");
            var wall = editor.AddObstacle(ObstacleShape.Rectangle, new(world.ViewBounds.End.X + 40, world.ViewBounds.GetCenter().Y));
            wall.SetDimensions(new(16, 200));
            var boid = world.SpawnBoid(new(world.ViewBounds.End.X - 40, wall.Position.Y), Vector2.Right * 120);
            editor.SynchronizeQuery(world.WrapBounds);
            Vector2 avoidance = ObstacleAvoidance.Calculate(new(boid.Id, boid.Position, boid.Velocity), world.ObstacleQuery, world.Config);
            Check(world.ObstacleQuery.BodyCount == 1 && avoidance.Y != 0, "Soft boundaries retain obstacle detection outside the viewport");
            world.Step(Dt);
            Check(boid.Acceleration.IsEqualApprox(avoidance), "Obstacle avoidance takes priority over boundary steering");
            world.SetLeader(boid);
            world.Config.BoidCount = 5;
            world.Reset();
            Check(world.IsViewportBoundaryActive && world.Leader is null && !world.IsRunning
                && world.Boids.Count == 5 && world.Boids.All(b => world.ViewBounds.HasPoint(b.Position))
                && editor.Obstacles.Contains(wall), "Reset clears identity, regenerates within view and preserves obstacles and boundary preference");
        }
        finally { world.Free(); }
    }

    private async Task CheckCamera()
    {
        var world = World();
        try
        {
            var camera = world.GetNode<SimulationCamera2D>("Camera2D");
            var boid = world.SpawnBoid(world.ViewBounds.GetCenter(), Vector2.Right * 60);
            var wall = world.GetNode<ObstacleEditor2D>("ObstacleEditor").AddObstacle(ObstacleShape.Circle, world.ViewBounds.GetCenter() + Vector2.Down * 80);
            var original = (boid.Position, boid.Velocity, boid.Rotation, wall.Position);
            Rect2 oldBounds = world.ViewBounds;
            camera._UnhandledInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true });
            camera._Input(new InputEventMouseMotion { Relative = new(1600, 900), ButtonMask = MouseButtonMask.Left });
            camera._Input(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false });
            camera._UnhandledInput(new InputEventMouseButton { ButtonIndex = MouseButton.WheelUp, Pressed = true });
            Check(world.ViewBounds != oldBounds && original == (boid.Position, boid.Velocity, boid.Rotation, wall.Position),
                "Paused pan and zoom update the boundary without relocating Boids or obstacles");
            Check(world.ViewportBoundaryBounds.Size.IsEqualApprox(world.ViewBounds.Size * 1.2f)
                && world.ViewportBoundaryBounds.GetCenter().IsEqualApprox(world.ViewBounds.GetCenter()),
                "Pan and zoom keep the proportional boundary margin aligned with the viewport");
            Check(!world.ViewBounds.HasPoint(boid.Position), "Panning can leave the unchanged population outside the new viewport");
            world.StepOnce();
            Check(boid.Position.DistanceTo(original.Item1) <= 1.001f && !world.IsRunning,
                "After panning, single-step returns continuously and remains paused");
            original = (boid.Position, boid.Velocity, boid.Rotation, wall.Position);
            camera.FitWorld();
            GetWindow().Size = new(1360, 860);
            await Frame();
            Check(original == (boid.Position, boid.Velocity, boid.Rotation, wall.Position), "Home and viewport resize leave simulation positions intact");
            Check(world.ViewportBoundaryBounds.Size.IsEqualApprox(world.ViewBounds.Size * 1.2f)
                && world.ViewportBoundaryBounds.GetCenter().IsEqualApprox(world.ViewBounds.GetCenter()),
                "Home and resize retain the viewport-centered margin");
            GetWindow().Size = new(960, 640);
            await Frame();
        }
        finally { world.Free(); }
    }

    private async Task CheckConfigurationAndUi()
    {
        using (var old = new ConfigFile())
        {
            old.SetValue("simulation", "boid_count", 0);
            old.Save(Config2D);
        }
        var config = new BoidConfig();
        Check(config.Load(Config2D) == Error.Ok && !config.ViewportBoundaryEnabled, "Old configs retain the original wrapping default");
        config.ViewportBoundaryEnabled = true;
        config.Save(Config2D);
        new BoidConfig { BoidCount = 0 }.Save(Config3D);
        var app = GD.Load<PackedScene>("res://scenes/main.tscn").Instantiate<MainController>();
        app.World2D = (SimulationWorldDefinition)app.World2D.Duplicate();
        app.World3D = (SimulationWorldDefinition)app.World3D.Duplicate();
        app.World2D.ConfigurationPath = Config2D;
        app.World3D.ConfigurationPath = Config3D;
        AddChild(app);
        try
        {
            await Frame();
            var panel = app.GetNode<ControlPanel>("%ControlPanel");
            var two = app.GetNode<BoidManager>("World/BoidsWorld2D");
            var toggle = (CheckBox)panel.FindChild("ViewportBoundary", true, false);
            panel.SelectCategory("Display");
            await Frame();
            Check(toggle.ButtonPressed && toggle.IsVisibleInTree() && two.IsViewportBoundaryActive,
                "Saved option is shown in the 2D display category and activates the world");
            toggle.ButtonPressed = false;
            config = new BoidConfig();
            Check(two.WrapBounds.HasValue && config.Load(Config2D) == Error.Ok && !config.ViewportBoundaryEnabled,
                "The real UI toggles behavior and saves the 2D preference");
            toggle.ButtonPressed = true;
            app.RequestWorldSwitch(1);
            await Frame();
            Check(panel.FindChild("ViewportBoundary", true, false) is null
                && !app.GetNode<BoidManager3D>("World/BoidsWorld3D").Config.ViewportBoundaryEnabled,
                "3D has no 2D boundary control and keeps its own configuration");
            app.RequestWorldSwitch(0);
            await Frame();
            two = app.GetNode<BoidManager>("World/BoidsWorld2D");
            Check(two.Config.ViewportBoundaryEnabled && two.IsViewportBoundaryActive
                && ((CheckBox)panel.FindChild("ViewportBoundary", true, false)).ButtonPressed,
                "Returning to 2D restores its saved boundary mode");
        }
        finally { app.Free(); }
    }

    private async Task Frame()
    {
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }
}
