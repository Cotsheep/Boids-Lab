using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;

namespace BoidsLab.Tests;

public partial class Presentation3DChecks : Node
{
    private int _checks;
    private Action? _afterFrame;
    public override void _Process(double delta) => _afterFrame?.Invoke();

    public override async void _Ready()
    {
        try
        {
            GetWindow().Size = new Vector2I(1360, 860);
            foreach (int fps in new[] { 144, 120, 75, 60, 30 })
                CheckSteadyFollow(fps);
            CheckLifecycleAndPicking();
            CheckRotationAndContact();
            await CheckEngineScheduling();
            GD.Print($"PASS: {_checks} presentation/camera/physics isolation assertions ({DisplayServer.GetName()}).");
            GetTree().Quit();
        }
        catch (Exception error) { GD.PushError(error.ToString()); GetTree().Quit(1); }
    }

    private void CheckLifecycleAndPicking()
    {
        var world = Create();
        world.Config.MaxSpeed = 1000;
        world.Config.PerceptionRadius = 20;
        world.Config.ShowPerceptionRadius = world.Config.ShowSeparationRadius = world.Config.ShowNeighbors = world.Config.ShowVelocity = true;
        var view = world.GetNode<SimulationView3D>("SimulationView");
        var camera = view.GetNode<SimulationCamera3D>("Camera3D");
        var leader = world.SpawnBoid(Vector3.Zero, Vector3.Right * 1000);
        leader.Quaternion = Quaternion.Identity;
        var other = world.SpawnBoid(Vector3.Right * 110, Vector3.Zero);
        world.SetLeader(leader);
        world.SetRunning(true);
        world.Step(0.1);
        view.PreparePresentation(0.25f);
        camera._Process(0.01);
        Check(leader.Position.X == 100 && Mathf.Abs(leader.DisplayPosition.X - 25) < 0.001f, "Physics and display have separate positions");
        Check(leader.GetNode<MeshInstance3D>("Body").GlobalPosition.IsEqualApprox(leader.DisplayGlobalTransform.Origin), "Actual rendered mesh uses the displayed transform");
        Check(world.PickBoid(camera.UnprojectPosition(leader.DisplayGlobalTransform.Origin), out _) == leader,
            "Perspective picking hits the displayed body between physics ticks");
        Check(world.PickBoid(camera.UnprojectPosition(leader.GlobalPosition), out _) != leader,
            "Picking does not hit the invisible future physics position");
        var geometry = new DebugOverlayGeometry3D();
        geometry.Build(world.Boids, leader, world.Config, camera.UnitsPerPixelAt);
        Vector3 rangeCenter = geometry.RangePoints.Aggregate(Vector3.Zero, (sum, point) => sum + point) / geometry.RangePoints.Count;
        Check(rangeCenter.DistanceTo(leader.DisplayPosition) < 0.001f && geometry.VelocityPoints[0] == leader.DisplayPosition,
            "Ranges and velocity arrows follow the displayed position");
        Check(geometry.NeighborCount == 1 && geometry.NeighborPoints[0] == leader.DisplayPosition
            && geometry.NeighborPoints[1] == other.DisplayPosition, "Neighbor eligibility uses physics positions while lines join displayed bodies");

        world.BeginObstaclePlacement(ObstacleShape.Box);
        Transform3D frozen = camera.Transform;
        world.Step(1.0 / 60);
        view.PreparePresentation(0.5f);
        camera._Process(0.1);
        Check(camera.Transform == frozen && leader.DisplayPosition != leader.Position && world.ObstaclePlacement == ObstacleShape.Box,
            "Placement freezes the camera while interpolated boids keep moving");
        world.CancelObstaclePlacement();
        camera._Process(0.1);
        Check(camera.Transform != frozen, "Camera follow resumes after editing");
        world.SetRunning(false);
        Transform3D stopped = leader.Transform;
        for (int i = 0; i < 4; i++) view.PreparePresentation(i / 3f);
        Check(leader.DisplayTransform == stopped && leader.GetNode<MeshInstance3D>("Body").GlobalPosition.IsEqualApprox(leader.GlobalPosition),
            "Pausing flushes interpolation and keeps the exact final pose");
        world.StepOnce();
        view.PreparePresentation(0);
        Check(leader.Transform != stopped && leader.DisplayTransform == leader.Transform && !world.IsRunning,
            "Single-step shows its final pose immediately without replaying an old interval");
        stopped = leader.Transform;
        world.SetRunning(true);
        view.PreparePresentation(0);
        Check(leader.DisplayTransform == stopped, "Resuming starts from the paused pose");
        var spawned = world.SpawnBoid(new Vector3(1000000, -200000, -500000), Vector3.Zero);
        view.PreparePresentation(0.1f);
        Check(spawned.DisplayPosition == spawned.Position, "A new distant boid has no trail from the origin");
        world.Step(1.0 / 60);
        view.PreparePresentation(0.1f);
        world.SetLeader(other);
        Check(world.Boids.All(b => b.DisplayTransform == b.Transform) && camera.OrbitCenter == other.Position,
            "Transferring identity clears old intervals and snaps the new follow target");
        world.RemoveBoid(other);
        Check(!camera.IsFollowing && world.Boids.All(b => b.DisplayTransform == b.Transform), "Removing the leader restores clean observation presentation");
        world.Config.BoidCount = 3;
        world.Reset();
        Check(!world.IsRunning && world.Leader is null && world.Boids.All(b => b.DisplayTransform == b.Transform),
            "Reset creates a paused world without stale display history");
        world.Free();
    }

    private void CheckRotationAndContact()
    {
        var world = Create();
        world.Config.MaxAngularSpeedDegrees = 360;
        world.Config.MaxAngularAccelerationDegrees = 720;
        var view = world.GetNode<SimulationView3D>("SimulationView");
        var boid = world.SpawnBoid(Vector3.Zero, Vector3.Right * 120);
        boid.Quaternion = Quaternion.Identity;
        world.SetRunning(true);
        world.Step(0.1);
        Quaternion actual = boid.Quaternion;
        view.PreparePresentation(0.5f);
        Quaternion shown = boid.DisplayTransform.Basis.GetRotationQuaternion();
        Check(actual != Quaternion.Identity && Mathf.Abs(Quaternion.Identity.AngleTo(shown) - shown.AngleTo(actual)) < 0.001f
            && boid.Quaternion == actual, "Rotation interpolates between angularly limited physical poses");
        boid.Position = new Vector3(-1000000, 200000, 500000);
        boid.Initialize(world.Config, boid.Id, Vector3.Zero);
        view.PreparePresentation(0.1f);
        Check(boid.DisplayTransform == boid.Transform, "Explicit reinitialization discards old translation and rotation history");
        world.Free();

        world = Create();
        world.Config.MaxSpeed = 1000;
        world.Config.ObstacleClearance = 0;
        view = world.GetNode<SimulationView3D>("SimulationView");
        boid = world.SpawnBoid(Vector3.Zero, Vector3.Right * 1000);
        world.SetLeader(boid);
        world.GetNode<ObstacleEditor3D>("ObstacleEditor").AddObstacle(ObstacleShape.Box, new Vector3(50, 0, 0)).SetDimensions(new Vector3(10, 100, 100));
        world.SetRunning(true);
        world.Step(0.1);
        view.PreparePresentation(0.5f);
        Check(boid.Position.X <= 45.001f && boid.Position.X > 0 && boid.DisplayPosition.X >= 0
            && boid.DisplayPosition.X <= boid.Position.X && boid.Velocity.X == 0,
            "Interpolation uses contact-resolved endpoints rather than the unclipped sweep destination");
        world.Free();
    }

    private async Task CheckEngineScheduling()
    {
        var world = Create();
        var view = world.GetNode<SimulationView3D>("SimulationView");
        var camera = view.GetNode<SimulationCamera3D>("Camera3D");
        var leader = world.SpawnBoid(Vector3.Zero, Vector3.Right * 120);
        leader.Quaternion = Quaternion.Identity;
        world.SetLeader(leader);
        world.SetRunning(true);
        world.SetPhysicsProcess(true);
        view.SetProcess(true);
        camera.SetProcess(true);
        ProcessPriority = 100;
        Engine.MaxFps = 144;
        if (DisplayServer.GetName() != "headless") DisplayServer.WindowSetVsyncMode(DisplayServer.VSyncMode.Disabled);
        int frames = 0, intermediateFrames = 0;
        float meshError = 0, followError = 0;
        _afterFrame = () =>
        {
            frames++;
            float fraction = (float)Engine.GetPhysicsInterpolationFraction();
            if (leader.Position.X <= 0) return;
            if (fraction > 0.05f && fraction < 0.95f) intermediateFrames++;
            float expected = leader.Position.X - 120f / Engine.PhysicsTicksPerSecond * (1 - fraction);
            meshError = Mathf.Max(meshError, Mathf.Abs(leader.GetNode<MeshInstance3D>("Body").GlobalPosition.X - expected));
            followError = Mathf.Max(followError, Mathf.Abs(camera.OrbitCenter.X - expected));
        };
        while (frames < 120) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        _afterFrame = null;
        Check(intermediateFrames > 10, "The real engine supplies interpolation fractions between ticks");
        Check(meshError < 0.002f && followError < 0.002f, $"Real process order presents the same pose to mesh and camera: mesh={meshError}, camera={followError}");
        world.Free();
    }

    private BoidManager3D Create()
    {
        var world = GD.Load<PackedScene>("res://scenes/worlds/boids_world_3d.tscn").Instantiate<BoidManager3D>();
        world.Initialize(new BoidConfig { BoidCount = 0, MaxSpeed = 120, MaxForce = 0,
            MaxAngularSpeedDegrees = 0, MaxAngularAccelerationDegrees = 0,
            SeparationEnabled = false, AlignmentEnabled = false, CohesionEnabled = false,
            LeaderFollowWeight = 0, AvoidanceEnabled = false });
        AddChild(world);
        world.SetPhysicsProcess(false);
        var camera = world.GetNode<SimulationCamera3D>("SimulationView/Camera3D");
        camera.SetProcess(false);
        world.GetNode<SimulationView3D>("SimulationView").SetProcess(false);
        return world;
    }

    private void CheckSteadyFollow(int fps)
    {
        var world = Create();
        var view = world.GetNode<SimulationView3D>("SimulationView");
        var camera = view.GetNode<SimulationCamera3D>("Camera3D");
        var leader = world.SpawnBoid(Vector3.Zero, Vector3.Right * 120);
        leader.Quaternion = Quaternion.Identity;
        var follower = world.SpawnBoid(new Vector3(40, 20, 80), Vector3.Right * 120);
        world.SetLeader(leader);
        world.SetRunning(true);
        var leaderX = new List<float>();
        var followerX = new List<float>();
        int tick = 0;
        for (int frame = 1; frame <= fps * 4; frame++)
        {
            double time = frame / (double)fps;
            while ((tick + 1) / 60.0 <= time + 1e-10) { world._PhysicsProcess(1.0 / 60); tick++; }
            Vector3 actual = leader.Position;
            var stats = world.Stats;
            float alpha = (float)((time - tick / 60.0) * 60);
            view.PreparePresentation(alpha);
            camera._Process(1.0 / fps);
            Check(leader.Position == actual && world.Stats.Equals(stats), "Displaying a frame never advances physics or statistics");
            if (time > 2)
            {
                leaderX.Add(camera.UnprojectPosition(leader.GetNode<MeshInstance3D>("Body").GlobalPosition).X);
                followerX.Add(camera.UnprojectPosition(follower.GetNode<MeshInstance3D>("Body").GlobalPosition).X);
            }
        }
        float range = leaderX.Max() - leaderX.Min(), followerRange = followerX.Max() - followerX.Min();
        Check(range < 0.01f && followerRange < 0.01f, $"Steady formation must not jitter at {fps} FPS: leader={range}, follower={followerRange}");
        Check(Mathf.Abs(leader.Position.X - 480) < 0.001f, "The 60 Hz physical trajectory remains unchanged");
        world.Free();
    }

    private void Check(bool value, string message)
    {
        _checks++;
        if (!value) throw new InvalidOperationException(message);
    }
}
