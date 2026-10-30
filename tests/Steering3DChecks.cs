using System;
using Godot;

namespace BoidsLab.Tests;

/// <summary>使用真实节点检查规则方向、共同快照、两种搜索的轨迹一致性与长期运动约束。</summary>
public partial class Steering3DChecks : Node
{
    private int _checks;
    private static readonly Aabb Bounds = new(new Vector3(-50, -40, -30), new Vector3(100, 80, 60));

    public override void _Ready()
    {
        if (DisplayServer.GetName() != "headless") { GetTree().Quit(2); return; }
        try
        {
            CheckRules();
            CheckSnapshotIntegration();
            CheckSearchParity();
            GD.Print($"PASS: {_checks} 3D flocking assertions (headless).");
            GetTree().Quit();
        }
        catch (Exception error)
        {
            GD.PushError(error.ToString());
            GetTree().Quit(1);
        }
    }

    private Boid3D MakeBoid(BoidConfig config, int id, Vector3 position, Vector3 velocity = default)
    {
        var boid = new Boid3D { Position = position };
        boid.Initialize(config, id, velocity);
        AddChild(boid);
        return boid;
    }

    private void CheckRules()
    {
        var config = new BoidConfig { MaxSpeed = 10, MaxForce = 100, PerceptionRadius = 20, SeparationRadius = 10,
            SeparationWeight = 1, AlignmentWeight = 1, CohesionWeight = 1,
            SeparationEnabled = true, AlignmentEnabled = false, CohesionEnabled = false };
        Boid3D a = MakeBoid(config, 17, Vector3.Zero);
        Boid3D b = MakeBoid(config, 92, new Vector3(3, 4, 0), Vector3.Back * 10);
        Boid3D[] pair = { a, b };
        var search = new NeighborSearch3D();
        Vector3 Force(int index = 0)
        {
            search.Search(pair, config.PerceptionRadius, config.NeighborSearchMode);
            return BoidSteering3D.Calculate(search, index, config);
        }
        foreach (NeighborSearchMode mode in Enum.GetValues<NeighborSearchMode>())
        {
            config.NeighborSearchMode = mode;
            config.SeparationEnabled = true;
            config.AlignmentEnabled = config.CohesionEnabled = false;
            a.Position = Vector3.Zero;
            b.Position = new Vector3(3, 4, 0);
            Near(Force(), new Vector3(-6, -8, 0), "Separation accelerates away from a close neighbor");
            config.SeparationRadius = 4;
            Near(Force(), Vector3.Zero, "Outside separation radius contributes no separation");
            config.SeparationRadius = 5;
            Near(Force(), new Vector3(-6, -8, 0), "Exact separation radius is inclusive");
            config.SeparationWeight = 0.5f;
            Near(Force(), new Vector3(-3, -4, 0), "Weight scales a rule before total clamping");
            config.SeparationWeight = 0;
            Near(Force(), Vector3.Zero, "Zero separation weight disables its contribution");
            config.SeparationWeight = 1;
            config.SeparationEnabled = false;
            config.AlignmentEnabled = true;
            Near(Force(), Vector3.Back * 10, "Alignment uses the neighbor velocity in the third axis");
            config.AlignmentWeight = 0;
            Near(Force(), Vector3.Zero, "Zero alignment weight disables its contribution");
            config.AlignmentWeight = 1;
            config.AlignmentEnabled = false;
            config.CohesionEnabled = true;
            Near(Force(), new Vector3(6, 8, 0), "Cohesion accelerates toward the neighbor center");
            config.CohesionWeight = 0;
            Near(Force(), Vector3.Zero, "Zero cohesion weight disables its contribution");
            config.CohesionWeight = 1;
            a.Position = new Vector3(49, 39, 29);
            b.Position = new Vector3(-49, -39, -29);
            Near(Force(), Vector3.Zero, "Cohesion does not connect opposite box corners");
            config.CohesionEnabled = false;
            config.SeparationEnabled = true;
            Near(Force(), Vector3.Zero, "Separation uses direct displacement");
            b.Position = a.Position;
            Vector3 first = Force();
            b.Initialize(config, 92, Vector3.Zero);
            Near(first, Vector3.Left * 10, "Overlapping IDs produce a deterministic separation direction");
            Near(Force(1), -first, "The overlapping pair has opposite separation directions");
            b.Initialize(config, 92, Vector3.Back * 10);
            a.Position = Vector3.Zero;
            b.Position = new Vector3(30, 0, 0);
            config.SeparationRadius = 50;
            Near(Force(), Vector3.Zero, "Separation never sees beyond the perception neighborhood");
            config.SeparationRadius = 10;
        }

        b.Position = new Vector3(3, 4, 0);
        config.SeparationEnabled = config.AlignmentEnabled = config.CohesionEnabled = true;
        config.MaxForce = 2;
        Check(Force().Length() <= 2.0001f, "Weighted sum respects total acceleration limit");
        config.MaxForce = 0;
        Near(Force(), Vector3.Zero, "Zero force disables all rule accelerations");
        config.MaxForce = 100;
        config.MaxSpeed = 0;
        Near(Force(), Vector3.Zero, "Zero speed disables rule targets");
        config.MaxSpeed = 10;
        config.SeparationEnabled = config.AlignmentEnabled = config.CohesionEnabled = false;
        Near(Force(), Vector3.Zero, "All toggles off preserves inertia");

        config.SeparationEnabled = config.CohesionEnabled = true;
        b.Initialize(config, 92, Vector3.Zero);
        b.Position = Vector3.Right * 5;
        Boid3D c = MakeBoid(config, 301, Vector3.Left * 5);
        search.Search(new[] { a, b, c }, 20);
        Near(BoidSteering3D.Calculate(search, 0, config), Vector3.Zero, "Symmetric neighbors do not create a spurious target");
        config.CohesionEnabled = false;
        b.Position = Vector3.Right * 2;
        c.Position = Vector3.Up * 4;
        search.Search(new[] { a, b, c }, 20);
        Vector3 weighted = BoidSteering3D.Calculate(search, 0, config);
        Check(weighted.X < 0 && weighted.Y < 0 && Mathf.IsEqualApprox(weighted.X / weighted.Y, 2),
            "A nearer neighbor has proportionally stronger influence on separation direction");
        search.Invalidate();
        bool rejected = false;
        try { BoidSteering3D.Calculate(search, 0, config); }
        catch (InvalidOperationException) { rejected = true; }
        Check(rejected, "Stale neighbor results cannot drive steering");
        foreach (Boid3D boid in new[] { a, b, c }) boid.QueueFree();
    }

    private BoidManager3D MakeWorld(BoidConfig config)
    {
        config.WorldWidth = config.WorldHeight = config.WorldDepth = 200;
        var world = GD.Load<PackedScene>("res://scenes/worlds/boids_world_3d.tscn").Instantiate<BoidManager3D>();
        world.Initialize(config);
        AddChild(world);
        return world;
    }

    private void CheckSnapshotIntegration()
    {
        var config = new BoidConfig { BoidCount = 0, MaxSpeed = 10, MaxForce = 100, PerceptionRadius = 20,
            SeparationEnabled = false, CohesionEnabled = false, AlignmentEnabled = true, AlignmentWeight = 1 };
        BoidManager3D world = MakeWorld(config);
        Boid3D a = world.SpawnBoid(Vector3.Zero, Vector3.Right * 10);
        Boid3D b = world.SpawnBoid(Vector3.Right * 10, Vector3.Up * 10);
        world.Step(0.25);
        Near(a.Velocity, new Vector3(7.5f, 2.5f, 0), "First individual turns using the old neighbor velocity");
        Near(b.Velocity, new Vector3(2.5f, 7.5f, 0), "Second individual also uses the old, not already-integrated velocity");
        Near(a.Position, new Vector3(1.875f, 0.625f, 0), "Steering integrates position using seconds");
        Near(world.NeighborSearch.Snapshots[0].Velocity, Vector3.Right * 10, "Completed snapshot is immutable under motion");
        Check(world.Stats.NeighborCandidateChecks == 2 && world.Stats.AverageNeighbors == 1,
            "Statistics correspond to the shared step-start neighborhood");
        config.AlignmentEnabled = false;
        a.SetAcceleration(Vector3.Back * 4);
        world.ConfigurationChanged();
        world.Step(0.25);
        Near(a.Velocity, new Vector3(7.5f, 2.5f, 1), "Live rule toggle removes steering while external acceleration still works");
        Near(b.Acceleration, Vector3.Zero, "Disabled rules leave no stale acceleration on another individual");
        a.SetAcceleration(Vector3.Zero);
        world.RemoveBoid(b);
        config.AlignmentEnabled = true;
        world.Step(0.25);
        Near(a.Acceleration, Vector3.Zero, "Removing the last neighbor clears its contribution");
        world.QueueFree();
    }

    private void CheckSearchParity()
    {
        var configA = new BoidConfig { BoidCount = 48, RandomSeed = 827, PerceptionRadius = 25, SeparationRadius = 10,
            MaxSpeed = 30, MaxForce = 20, NeighborSearchMode = NeighborSearchMode.Naive };
        var configB = new BoidConfig { BoidCount = 48, RandomSeed = 827, PerceptionRadius = 25, SeparationRadius = 10,
            MaxSpeed = 30, MaxForce = 20, NeighborSearchMode = NeighborSearchMode.UniformGrid };
        BoidManager3D naive = MakeWorld(configA);
        BoidManager3D grid = MakeWorld(configB);
        bool movedByRules = false;
        for (int step = 0; step < 240; step++)
        {
            if (step == 60)
            {
                naive.RemoveBoid(naive.Boids[9]);
                grid.RemoveBoid(grid.Boids[9]);
                naive.SpawnBoid(new Vector3(49, 39, 29), Vector3.One * 5);
                grid.SpawnBoid(new Vector3(49, 39, 29), Vector3.One * 5);
            }
            if (step == 120)
            {
                configA.PerceptionRadius = configB.PerceptionRadius = 45;
                naive.ConfigurationChanged();
                grid.ConfigurationChanged();
            }
            naive.Step(1.0 / 60);
            grid.Step(1.0 / 60);
            for (int i = 0; i < naive.Boids.Count; i++)
            {
                Boid3D a = naive.Boids[i];
                Boid3D b = grid.Boids[i];
                Check(a.Position == b.Position && a.Velocity == b.Velocity && a.Acceleration == b.Acceleration,
                    "Naive and grid searches produce identical trajectories, including changes in count and radius");
                Check(a.Position.IsFinite() && a.Velocity.IsFinite() && a.Acceleration.IsFinite() && a.Quaternion.IsFinite(),
                    "Long-running motion remains finite");
                Check(a.Velocity.Length() <= configA.MaxSpeed + 0.001f && a.Acceleration.Length() <= configA.MaxForce + 0.001f,
                    "Long-running speed and acceleration stay bounded without position clipping");
                movedByRules |= a.Acceleration.LengthSquared() > 0;
            }
        }
        Check(movedByRules, "Enabled rules actually drive the world");
        naive.QueueFree();
        grid.QueueFree();
    }

    private void Near(Vector3 actual, Vector3 expected, string message) =>
        Check(actual.DistanceTo(expected) < 0.0001f, $"{message}: got {actual}, expected {expected}");

    private void Check(bool condition, string message)
    {
        _checks++;
        if (!condition) throw new InvalidOperationException(message);
    }
}
