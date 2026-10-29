using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace BoidsLab.Tests;

/// <summary>以独立的 27 幅周期图像距离为参照，验证三维搜索、环绕几何和统计生命周期。</summary>
public partial class Spatial3DChecks : Node
{
    private int _checks;
    private static readonly Aabb Bounds = new(new Vector3(-50, -40, -30), new Vector3(100, 80, 60));

    public override void _Ready()
    {
        if (DisplayServer.GetName() != "headless")
        {
            GD.PushError("Run spatial_3d_checks.tscn with --headless.");
            GetTree().Quit(2);
            return;
        }
        try
        {
            CheckSearch();
            CheckGeometry();
            CheckWorld();
            GD.Print($"PASS: {_checks} 3D neighbor/geometry/statistics assertions (headless).");
            GetTree().Quit();
        }
        catch (Exception error)
        {
            GD.PushError(error.ToString());
            GetTree().Quit(1);
        }
    }

    private Boid3D MakeBoid(Vector3 position, int id = 0, Vector3 velocity = default)
    {
        var boid = new Boid3D { Position = position };
        boid.Initialize(new BoidConfig(), id, velocity);
        AddChild(boid);
        return boid;
    }

    private void CheckSearch()
    {
        var search = new NeighborSearch3D();
        var boids = new List<Boid3D>();
        VerifySearch(boids, search, 5);
        boids.Add(MakeBoid(Vector3.Zero, 100));
        VerifySearch(boids, search, 5);
        Vector3[] special = { new(3, 4, 0), new(3, 4, 0.01f), Vector3.Zero,
            new(-49, -39, -29), new(49, 39, 29), new(-49, 0, 0), new(49, 0, 0),
            new(-25, 0, 0), new(25, 0, 0), new(351, -279, 211) };
        foreach (Vector3 point in special) boids.Add(MakeBoid(point, 100 + boids.Count * 13));
        foreach (float radius in new[] { 1f, 2f, 3f, 5f, 30f, 50f, 100f }) VerifySearch(boids, search, radius);
        search.Search(boids, 5, Bounds);
        Check(search.GetNeighborIndices(0).Contains(1) && !search.GetNeighborIndices(0).Contains(2), "Exact radius included; just outside excluded");
        Vector3 frozen = search.Snapshots[0].Position;
        boids[0].Position += Vector3.One * 20;
        Check(search.Snapshots[0].Position == frozen, "Node movement cannot rewrite a completed snapshot");
        search.Invalidate();
        Check(search.Snapshots.Count == 0, "Invalid results expose no snapshots");
        bool rejected = false;
        try { search.GetNeighborIndices(0); }
        catch (InvalidOperationException) { rejected = true; }
        Check(rejected, "Invalid results cannot be read as current neighbors");
        using var random = new RandomNumberGenerator { Seed = 927 };
        for (int i = 0; i < 120; i++)
            boids.Add(MakeBoid(Bounds.Position + new Vector3(random.Randf(), random.Randf(), random.Randf()) * Bounds.Size, 1000 + i * 7));
        foreach (float radius in new[] { 1f, 7f, 30f, 50f, 100f }) VerifySearch(boids, search, radius);
        search.Search(boids, 7, Bounds, NeighborSearchMode.UniformGrid);
        Check(search.CandidateChecks < (long)boids.Count * (boids.Count - 1), "Sparse grid actually prunes candidate pairs");
        // 测试数量缩小和空集合不会残留上次的邻居或平均值。
        VerifySearch(boids.Take(3).ToArray(), search, 10);
        VerifySearch(Array.Empty<Boid3D>(), search, 10);
        foreach (Boid3D boid in boids) boid.QueueFree();
    }

    private void VerifySearch(IReadOnlyList<Boid3D> boids, NeighborSearch3D search, float radius)
    {
        foreach (NeighborSearchMode mode in Enum.GetValues<NeighborSearchMode>()) VerifySearchMode(boids, search, radius, mode);
    }

    private void VerifySearchMode(IReadOnlyList<Boid3D> boids, NeighborSearch3D search, float radius, NeighborSearchMode mode)
    {
        Vector3[] positions = boids.Select(b => b.Position).ToArray();
        search.Search(boids, radius, Bounds, mode);
        long allPairs = (long)boids.Count * (boids.Count - 1);
        Check(mode == NeighborSearchMode.Naive ? search.CandidateChecks == allPairs : search.CandidateChecks <= allPairs,
            "Candidates exclude self and do not repeat periodic cells");
        long total = 0;
        for (int i = 0; i < boids.Count; i++)
        {
            var expected = new List<int>();
            for (int j = 0; j < boids.Count; j++)
                if (i != j && ReferenceDistanceSquared(positions[i], positions[j], Bounds) <= (double)radius * radius) expected.Add(j);
            Check(search.GetNeighborIndices(i).SequenceEqual(expected), "Neighbor indices match independent periodic-image reference");
            Check(search.GetNeighborIndices(i).Distinct().Count() == expected.Count && !expected.Contains(i), "No duplicates or self neighbors");
            Check(search.Snapshots[i].Id == boids[i].Id && boids[i].Position == positions[i], "Search preserves IDs and does not move nodes");
            total += expected.Count;
        }
        Check(Math.Abs(search.AverageNeighbors - (boids.Count == 0 ? 0 : (double)total / boids.Count)) < 0.000001, "Average neighbor count is measured");
        Check(search.SearchMilliseconds >= 0 && double.IsFinite(search.SearchMilliseconds), "Search timing is finite");
    }

    private static double ReferenceDistanceSquared(Vector3 a, Vector3 b, Aabb bounds)
    {
        // 独立实现：先用余数规范化，再枚举邻近 27 个世界副本，不调用生产距离函数。
        for (int axis = 0; axis < 3; axis++)
        {
            float size = bounds.Size[axis], min = bounds.Position[axis];
            a[axis] = ((a[axis] - min) % size + size) % size + min;
            b[axis] = ((b[axis] - min) % size + size) % size + min;
        }
        double best = double.PositiveInfinity;
        for (int x = -1; x <= 1; x++)
            for (int y = -1; y <= 1; y++)
                for (int z = -1; z <= 1; z++)
                {
                    double dx = (double)b.X + x * bounds.Size.X - a.X;
                    double dy = (double)b.Y + y * bounds.Size.Y - a.Y;
                    double dz = (double)b.Z + z * bounds.Size.Z - a.Z;
                    best = Math.Min(best, dx * dx + dy * dy + dz * dz);
                }
        return best;
    }

    private void CheckGeometry()
    {
        Boid3D a = MakeBoid(new Vector3(49, 10, 15), velocity: Vector3.Right * 20);
        Boid3D b = MakeBoid(new Vector3(-49, 12, 16), 1, Vector3.Up * 30);
        Boid3D c = MakeBoid(Vector3.Zero, 2);
        Boid3D[] boids = { a, b, c };
        var config = new BoidConfig { PerceptionRadius = 3, ShowNeighbors = true, ShowPerceptionRadius = false, ShowVelocity = true };
        var geometry = new DebugOverlayGeometry3D();
        geometry.Build(boids, a, config, Bounds, 1);
        Check(geometry.NeighborCount == 1, "Only selected boid's neighbors are shown");
        double length = 0;
        for (int i = 0; i < geometry.NeighborPoints.Count; i += 2) length += geometry.NeighborPoints[i].DistanceTo(geometry.NeighborPoints[i + 1]);
        Check(Math.Abs(length - 3) < 0.001, "Wrapped link has shortest length rather than crossing the whole box");
        Check(geometry.NeighborPoints.Any(p => Mathf.IsEqualApprox(p.X, -50))
            && geometry.NeighborPoints.Any(p => Mathf.IsEqualApprox(p.X, 50)), "Link continues on both boundary faces");
        Check(geometry.VelocityCount == 2, "Velocity arrows apply to all moving boids, excluding zero velocity");
        CheckPointsInside(geometry.NeighborPoints.Concat(geometry.VelocityPoints));
        config.ShowPerceptionRadius = true;
        config.ShowNeighbors = false;
        a.Position = new Vector3(49, 39, 29);
        foreach (float radius in new[] { 8f, 35f, 50f })
        {
            config.PerceptionRadius = radius;
            geometry.Build(boids, a, config, Bounds, 1);
            Check(geometry.PerceptionCount == 1 && geometry.RangePoints.Count > 0 && geometry.NeighborPoints.Count == 0,
                "Single selected range works independently of links");
            CheckPointsInside(geometry.RangePoints);
            foreach (Vector3 point in geometry.RangePoints)
            {
                double distance = Math.Sqrt(ReferenceDistanceSquared(a.Position, point, Bounds));
                Check(distance <= radius + 0.002 && distance >= radius * 0.997,
                    "Range wire stays on the nearest periodic sphere, including radii exceeding half an axis");
            }
        }
        config.PerceptionRadius = 100;
        geometry.Build(boids, a, config, Bounds, 1);
        Check(geometry.RangePoints.Count == 24, "All-world perception is represented by the twelve box edges");
        config.ShowSeparationRadius = true;
        config.PerceptionRadius = 8;
        config.SeparationRadius = 20;
        geometry.Build(boids, a, config, Bounds, 1);
        Check(geometry.SeparationCount == 1 && geometry.SeparationRangePoints.SequenceEqual(geometry.RangePoints),
            "Separation range is capped by perception and shares periodic clipping");
        config.ShowPerceptionRadius = false;
        config.SeparationRadius = 3;
        geometry.Build(boids, a, config, Bounds, 1);
        Check(geometry.RangePoints.Count == 0 && geometry.SeparationRangePoints.Count > 0, "Separation sphere is independently switchable");
        CheckPointsInside(geometry.SeparationRangePoints);
        foreach (Vector3 point in geometry.SeparationRangePoints)
            Check(Math.Abs(Math.Sqrt(ReferenceDistanceSquared(a.Position, point, Bounds)) - 3) < 0.01,
                "Separation sphere represents the configured smaller radius");
        geometry.Build(boids, null, config, Bounds, 1);
        Check(geometry.PerceptionCount == 0 && geometry.RangePoints.Count == 0 && geometry.NeighborCount == 0
            && geometry.SeparationCount == 0 && geometry.SeparationRangePoints.Count == 0
            && geometry.VelocityCount == 2, "Deselection clears local overlays and keeps all velocity arrows");
        config.ShowVelocity = false;
        geometry.Build(boids, null, config, Bounds, 1);
        Check(geometry.VelocityPoints.Count == 0, "Disabling arrows clears old geometry");
        foreach (Boid3D boid in boids) boid.QueueFree();
    }

    private void CheckPointsInside(IEnumerable<Vector3> points)
    {
        foreach (Vector3 point in points)
            Check(point.IsFinite() && point.X >= Bounds.Position.X - 0.001 && point.X <= Bounds.End.X + 0.001
                && point.Y >= Bounds.Position.Y - 0.001 && point.Y <= Bounds.End.Y + 0.001
                && point.Z >= Bounds.Position.Z - 0.001 && point.Z <= Bounds.End.Z + 0.001, "Debug segment stays inside the simulation volume");
    }

    private void CheckWorld()
    {
        var config = new BoidConfig { BoidCount = 0, PerceptionRadius = 50, NeighborSearchMode = NeighborSearchMode.UniformGrid,
            SeparationEnabled = false, AlignmentEnabled = false, CohesionEnabled = false }; // 本用例隔离搜索和运动阶段。
        var world = GD.Load<PackedScene>("res://scenes/worlds/boids_world_3d.tscn").Instantiate<BoidManager3D>();
        world.Initialize(config);
        AddChild(world);
        Check(config.NeighborSearchMode == NeighborSearchMode.UniformGrid, "Saved grid mode is supported");
        Boid3D a = world.SpawnBoid(Vector3.Zero, Vector3.Right * 120);
        world.SpawnBoid(new Vector3(40, 0, 0), Vector3.Left * 120);
        Check(world.Stats.NeighborCandidateChecks is null, "No measurement before the first physical step");
        world.Step(0.5);
        Check(world.Stats.NeighborCandidateChecks == 2 && world.Stats.AverageNeighbors == 1, "Statistics describe the step-start state");
        Check(world.NeighborSearch.Snapshots[0].Position == Vector3.Zero && a.Position.IsEqualApprox(new Vector3(60, 0, 0)),
            "Snapshot precedes motion while search itself does not steer");
        Check(world.Stats.UpdateMilliseconds >= world.Stats.NeighborSearchMilliseconds, "Whole-step measurement includes the search");
        var measured = world.Stats;
        config.ShowNeighbors = true;
        world.ConfigurationChanged();
        var geometry = new DebugOverlayGeometry3D();
        geometry.Build(world.Boids, a, config, world.WorldBounds, 1);
        Check(geometry.NeighborCount == 0 && world.Stats == measured, "Current-position debug does not overwrite the previous physics result");
        config.PerceptionRadius = 20;
        world.ConfigurationChanged();
        Check(!world.NeighborSearch.HasResults && world.Stats.NeighborCandidateChecks is null && !world.IsRunning,
            "Paused radius changes invalidate stale statistics without advancing time");
        world.Step(1.0 / 60);
        Check(world.NeighborSearch.Radius == 20 && world.Stats.AverageNeighbors == 0, "Next step measures the new radius");
        config.NeighborSearchMode = NeighborSearchMode.Naive;
        world.ConfigurationChanged();
        Check(!world.NeighborSearch.HasResults && world.Stats.NeighborCandidateChecks is null, "Paused algorithm changes invalidate old counts");
        world.Step(1.0 / 60);
        Check(world.NeighborSearch.Mode == NeighborSearchMode.Naive && world.Stats.NeighborCandidateChecks == 2,
            "Next step uses the selected algorithm");
        world.RemoveBoid(a);
        Check(!world.NeighborSearch.HasResults && world.Stats.BoidCount == 1, "Removal invalidates indices and counts");
        config.BoidCount = 1;
        world.Reset();
        Check(!world.NeighborSearch.HasResults && world.Stats.NeighborSearchMilliseconds is null, "Reset clears previous measurement");
        world.Step(1.0 / 60);
        Check(world.Stats.NeighborCandidateChecks == 0 && world.Stats.AverageNeighbors == 0, "Single boid has no self neighbor");
        world.QueueFree();
    }

    private void Check(bool condition, string message)
    {
        _checks++;
        if (!condition) throw new InvalidOperationException(message);
    }
}
