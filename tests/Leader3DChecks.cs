using System;
using System.Linq;
using Godot;

namespace BoidsLab.Tests;

public partial class Leader3DChecks : Node
{
    private int _checks;
    public override void _Ready()
    {
        if (DisplayServer.GetName() != "headless") { GetTree().Quit(2); return; }
        try
        {
            CheckLifecycle();
            CheckMotion();
            CheckSnapshots();
            CheckGroup(40, 1.5f, NeighborSearchMode.Naive);
            CheckGroup(200, 1.5f, NeighborSearchMode.UniformGrid);
            CheckGroup(40, 5, NeighborSearchMode.UniformGrid);
            GD.Print($"PASS: {_checks} 3D leader/motion/spacing assertions (headless).");
            GetTree().Quit();
        }
        catch (Exception error) { GD.PushError(error.ToString()); GetTree().Quit(1); }
    }

    private BoidManager3D World(BoidConfig? config = null)
    {
        var world = GD.Load<PackedScene>("res://scenes/worlds/boids_world_3d.tscn").Instantiate<BoidManager3D>();
        world.Initialize(config ?? new BoidConfig { BoidCount = 0 });
        AddChild(world);
        return world;
    }

    private void CheckLifecycle()
    {
        var world = World(new BoidConfig { BoidCount = 0, ShowBoundary = true });
        var camera = world.GetNode<SimulationCamera3D>("SimulationView/Camera3D");
        var view = world.GetNode<SimulationView3D>("SimulationView");
        var a = world.SpawnBoid(new Vector3(-10000, 3000, 5000), Vector3.Zero);
        var b = world.SpawnBoid(a.Position + new Vector3(200, 100, 50), Vector3.Zero);
        a.SetSelected(true);
        world.SetRunning(true);
        world.SetLeader(a);
        Check(world.LeaderId == a.Id && a.IsLeader && world.IsRunning && !world.IsLeaderControlActive,
            "Assignment preserves running state and requires explicit control");
        Check(a.GetNode<MeshInstance3D>("Body").MaterialOverride == a.SelectedLeaderMaterial, "Selected leader has a distinct appearance");
        a.SetSelected(false);
        Check(a.GetNode<MeshInstance3D>("Body").MaterialOverride == a.LeaderMaterial, "Deselection preserves leader highlight");
        Check(camera.Projection == Camera3D.ProjectionType.Perspective && camera.IsPositionInFrustum(a.GlobalPosition),
            "Mode change immediately frames a distant leader in perspective");
        Check(!view.GetNode<MeshInstance3D>("ReferenceFrame").Visible && !view.GetNode<MeshInstance3D>("Grid").Visible,
            "Infinite mode hides the reference box and floor grid");
        world.SetLeaderControlActive(true);
        world.SetLeader(b);
        Check(!world.IsLeaderControlActive && !a.IsLeader && b.IsLeader && world.IsRunning, "Transfer releases input and preserves a unique leader");
        Vector3[] before = world.Boids.Select(x => x.Position).ToArray();
        world.Config.WorldWidth = 600;
        world.ConfigurationChanged();
        Check(world.Boids.Select(x => x.Position).SequenceEqual(before) && !view.GetNode<MeshInstance3D>("ReferenceFrame").Visible,
            "Resizing while following does not enable boundaries or move any boid");
        world.SetLeader(null);
        Check(world.Boids.Select(x => x.Position).SequenceEqual(before) && world.IsRunning, "Canceling leader never teleports or pauses");
        Near(world.WorldBounds.GetCenter(), (before[0] + before[1]) * 0.5f, "Observation bounds recenter near the population");
        Check(camera.Projection == Camera3D.ProjectionType.Orthogonal && view.GetNode<MeshInstance3D>("ReferenceFrame").Visible,
            "Canceling restores observation projection and reference frame");
        world.SetLeader(a);
        world.RemoveBoid(a);
        Check(world.Leader is null && world.Boids.Count == 1, "Removing leader clears identity");
        Near(world.WorldBounds.GetCenter(), b.Position, "Remaining population determines the new center");
        var editor = world.GetNode<ObstacleEditor3D>("ObstacleEditor");
        editor.AddObstacle(ObstacleShape.Box, b.Position + Vector3.Right * 2000);
        world.SetLeader(b);
        world.SetLeaderControlActive(true);
        world.Config.BoidCount = 7;
        world.Reset();
        Check(world.Leader is null && !world.IsLeaderControlActive && !world.IsRunning && world.Boids.Count == 7
            && editor.Obstacles.Count == 1 && world.Boids.All(x => world.WorldBounds.HasPoint(x.Position)),
            "Reset clears control, respawns within local bounds, pauses, and keeps obstacles");
        world.Free();
    }

    private void CheckMotion()
    {
        var world = World(new BoidConfig { BoidCount = 0, MaxSpeed = 200, MaxForce = 200, AvoidanceEnabled = false });
        var leader = world.SpawnBoid(Vector3.Zero, Vector3.Zero);
        world.SetLeader(leader);
        world.SetLeaderControlActive(true);
        Move(world, "leader_right", true);
        for (int i = 0; i < 120; i++) world.Step(1.0 / 60);
        Check(leader.Position.X > 200 && (leader.Quaternion * Vector3.Forward).Dot(Vector3.Right) > 0.999f,
            "Strafing turns the leader toward actual movement");
        Near(world.LeaderController.Forward, Vector3.Forward, "Leader turning does not rotate the WASD camera reference");
        Move(world, "leader_forward", true);
        Check(world.LeaderController.MovementDirection.Length() <= 1.00001f, "Diagonal flight is normalized");
        Move(world, "leader_right", false);
        world.LeaderController._Input(new InputEventMouseMotion { Relative = new Vector2(0, -150) });
        for (int i = 0; i < 120; i++)
        {
            world.Step(1.0 / 60);
            Check(leader.Velocity.Length() <= 200.001f && leader.Acceleration.Length() <= 200.001f
                && leader.AngularVelocity.Length() <= Mathf.DegToRad(world.Config.MaxAngularSpeedDegrees) + 0.001f
                && leader.AngularAcceleration.Length() <= Mathf.DegToRad(world.Config.MaxAngularAccelerationDegrees) + 0.001f,
                "Controlled motion respects linear and angular limits");
        }
        Check(leader.Velocity.Y > 50, "Pitch changes flying altitude");
        Check((leader.Quaternion * Vector3.Forward).Dot(leader.Velocity.Normalized()) > 0.995f,
            "Leader facing converges toward pitched movement");
        Move(world, "leader_forward", false);
        for (int i = 0; i < 180; i++) world.Step(1.0 / 60);
        Check(leader.Velocity.Length() < 0.01f, "Releasing movement brakes to rest");
        Quaternion stoppedFacing = leader.Quaternion;
        Vector3 stoppedPosition = leader.Position;
        world.LeaderController._Input(new InputEventMouseMotion { Relative = new Vector2(200, 100) });
        for (int i = 0; i < 120; i++) world.Step(1.0 / 60);
        Near(leader.Position, stoppedPosition, "Looking around at rest does not move the leader");
        Check(leader.Quaternion.AngleTo(stoppedFacing) < 0.001f,
            "Looking around at rest does not turn the leader toward the camera view");
        Vector3 viewForward = world.LeaderController.Forward;
        Move(world, "leader_backward", true);
        for (int i = 0; i < 240; i++) world.Step(1.0 / 60);
        Check(leader.Velocity.Normalized().Dot(-viewForward) > 0.999f
            && (leader.Quaternion * Vector3.Forward).Dot(-viewForward) > 0.999f,
            "Backward input moves opposite the camera view and faces actual movement");
        Move(world, "leader_backward", false);
        Move(world, "leader_left", true);
        Vector3 viewLeft = -viewForward.Cross(Vector3.Up).Normalized();
        for (int i = 0; i < 240; i++) world.Step(1.0 / 60);
        Check(leader.Velocity.Normalized().Dot(viewLeft) > 0.999f
            && (leader.Quaternion * Vector3.Forward).Dot(viewLeft) > 0.999f,
            "Left input remains relative to the camera after the leader turns backward");
        Move(world, "leader_left", false);
        Move(world, "leader_forward", true);
        world.SetLeaderControlActive(false);
        world.SetLeaderControlActive(true);
        Check(world.LeaderController.MovementDirection == Vector3.Zero, "Reentering control does not retain held keys");
        world.SetLeader(null);
        world.RemoveBoid(leader);
        leader = world.SpawnBoid(Vector3.Zero, Vector3.Forward * 200);
        world.SetLeader(leader);
        world.SetLeaderControlActive(true);
        Move(world, "leader_forward", true);
        var wall = world.GetNode<ObstacleEditor3D>("ObstacleEditor").AddObstacle(ObstacleShape.Box, Vector3.Forward * 100);
        wall.SetDimensions(Vector3.One * 80);
        world.Step(0.1);
        Near(leader.Acceleration, Vector3.Zero, "Leader does not receive predictive avoidance");
        world.Step(1);
        Check(leader.Position.Z > -49 && Mathf.Abs(leader.Velocity.Z) < 0.01f,
            "Leader sweeps and blocks contact even with avoidance disabled");
        world.Free();
    }

    private void CheckSnapshots()
    {
        var config = new BoidConfig { BoidCount = 0, PerceptionRadius = 1,
            SeparationEnabled = false, AlignmentEnabled = false, CohesionEnabled = false };
        var world = World(config);
        var a = world.SpawnBoid(Vector3.Zero, Vector3.Right * 60);
        a.Quaternion = Quaternion.Identity;
        var b = world.SpawnBoid(Vector3.Back * 80, Vector3.Zero);
        world.SetLeader(a);
        world.Step(0.1);
        Check(b.Velocity.X > 0 && Mathf.Abs(b.Velocity.Z) < 0.001,
            "Follower matches step-start leader velocity inside the region, outside perception radius");
        Check(a.Velocity.X < 60, "Uncontrolled leader brakes instead of being pulled by flocking");
        world.SetLeader(null);
        world.RemoveBoid(b);
        b = world.SpawnBoid(Vector3.Back * 600, Vector3.Zero);
        var previous = world.Boids.Select(x => x.Position).ToArray();
        world.SetLeader(a);
        Check(world.Boids.Select(x => x.Position).SequenceEqual(previous), "Entering infinite mode preserves coordinates");
        world.Step(1.0 / 60);
        Check(b.Acceleration.Z < 0, "Distant follower globally approaches the region behind the leader");
        world.Free();
    }

    private void CheckGroup(int count, float weight, NeighborSearchMode mode)
    {
        var world = World(new BoidConfig { BoidCount = 0, LeaderFollowWeight = weight, NeighborSearchMode = mode });
        var leader = world.SpawnBoid(Vector3.Zero, Vector3.Zero);
        for (int i = 0; i < count; i++)
        {
            float y = 1 - 2f * (i + 0.5f) / count;
            float angle = i * 2.399963f;
            float r = Mathf.Sqrt(1 - y * y);
            world.SpawnBoid(new Vector3(r * Mathf.Cos(angle), y, r * Mathf.Sin(angle)) * 160 + Vector3.Back * 180, Vector3.Zero);
        }
        world.SetLeader(leader);
        for (int i = 0; i < 1200; i++) world.Step(1.0 / 60);
        Spacing(world, count, "rest");
        world.SetLeaderControlActive(true);
        Move(world, "leader_forward", true);
        for (int i = 0; i < 1800; i++)
        {
            world.LeaderController._Input(new InputEventMouseMotion { Relative = new Vector2(0.25f, Mathf.Sin(i / 120f) * 0.05f) });
            world.Step(1.0 / 60);
            if (i >= 600 && i % 120 == 0) Spacing(world, count, "moving");
        }
        Spacing(world, count, "final");
        Vector3 center = Vector3.Zero;
        foreach (Boid3D boid in world.Boids.Skip(1)) center += boid.Position / count;
        float radius = LeaderSteering3D.FollowRadius(count, world.Config);
        Check(center.DistanceTo(leader.Position) < world.Config.LeaderFollowDistance + 2 * radius + 240,
            $"Followers remain with a moving leader: count={count}, distance={center.DistanceTo(leader.Position)}");
        GD.Print($"Leader 3D spacing passed: count={count}, weight={weight}, center distance={center.DistanceTo(leader.Position):F1}");
        world.Free();
    }

    private void Spacing(BoidManager3D world, int count, string phase)
    {
        int crowded = 0;
        for (int i = 1; i < world.Boids.Count; i++)
        {
            float nearest = float.PositiveInfinity;
            for (int j = 1; j < world.Boids.Count; j++)
                if (i != j) nearest = Mathf.Min(nearest, world.Boids[i].Position.DistanceTo(world.Boids[j].Position));
            if (nearest < 10) crowded++;
            Check(world.Boids[i].Position.IsFinite() && world.Boids[i].Velocity.Length() <= world.Config.MaxSpeed + 0.001f,
                "Following preserves finite motion and speed limits");
        }
        Check(crowded <= count / 10, $"Followers must not collapse: {phase}, {crowded}/{count} below 10 units");
    }

    private static void Move(BoidManager3D world, string action, bool pressed) =>
        world.LeaderController._Input(new InputEventAction { Action = action, Pressed = pressed });
    private void Near(Vector3 actual, Vector3 expected, string message) => Check(actual.DistanceTo(expected) < 0.02f, message);
    private void Check(bool condition, string message)
    {
        _checks++;
        if (!condition) throw new InvalidOperationException(message);
    }
}
