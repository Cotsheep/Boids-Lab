using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace BoidsLab.Tests;

/// <summary>用独立的九幅平移图像距离参考验证两种搜索，不以一种实现作为另一种的唯一标准。</summary>
public partial class NeighborChecks : Node
{
    private int _checks;

    public override void _Ready()
    {
        if (DisplayServer.GetName() != "headless")
        {
            GD.PushError("Run neighbor_checks.tscn with --headless.");
            GetTree().Quit(2);
            return;
        }
        try
        {
            CheckGeometry();
            CheckRandomPopulations();
            CheckManagerAndUi();
            CheckConfiguration();
            GD.Print($"PASS: {_checks} neighbor search/statistics/integration assertions (headless).");
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

    private void CheckGeometry()
    {
        var bounds = new Rect2(0, 0, 103, 77);
        Compare(Array.Empty<Vector2>(), bounds, 10);
        Compare(new[] { new Vector2(1, 1) }, bounds, 10);
        Vector2[] points = { new(0, 0), new(6, 8), new(6, 8), new(6, 8.01f),
            new(102, 76), new(1, 76), new(102, 1), new(50, 38) };
        foreach (float radius in new[] { 1f, 10f, 20f, 30f, 40f, 60f, 500f })
            Compare(points, bounds, radius);
        // 非整除尺寸、负原点、恰在分格线上、跨缝、只有 1/2 行列、狭长区域。
        foreach (Vector2 size in new[] { new Vector2(5, 7), new Vector2(103, 77), new Vector2(500, 10) })
        {
            var shifted = new Rect2(new Vector2(-200, -300), size);
            Vector2[] edges = { shifted.Position, shifted.End - new Vector2(0.25f, 0.25f),
                shifted.Position + size / 2, shifted.Position + size / 3,
                shifted.End, shifted.Position - size * 2 };
            foreach (float radius in new[] { 1f, 4f, 10f, 26f, 500f }) Compare(edges, shifted, radius);
        }

        // 稀疏规则点阵：网格必须确实减少候选检查；不对运行时间做易波动的断言。
        Vector2[] sparse = Enumerable.Range(0, 100).Select(i => new Vector2(i % 10 * 100, i / 10 * 100)).ToArray();
        Check(Compare(sparse, new Rect2(0, 0, 1000, 1000), 20) == 0, "Sparse cells avoid all unnecessary pair checks");
        Vector2[] dense = Enumerable.Repeat(new Vector2(50, 50), 64).ToArray();
        Check(Compare(dense, bounds, 10) == 64L * 63, "Dense grid still checks every other individual exactly once");
    }

    private void CheckRandomPopulations()
    {
        var random = new Random(42);
        for (int trial = 0; trial < 32; trial++)
        {
            var bounds = new Rect2(-317.25f, 81.5f, 20 + trial * 31.75f, 17 + trial * 17.25f);
            var points = Enumerable.Range(0, 80).Select(_ => bounds.Position + new Vector2(
                (float)random.NextDouble() * bounds.Size.X, (float)random.NextDouble() * bounds.Size.Y)).ToArray();
            foreach (float radius in new[] { 1f, 19f, 80f, 500f }) Compare(points, bounds, radius);
        }
    }

    private long Compare(Vector2[] positions, Rect2 bounds, float radius)
    {
        var config = new BoidConfig();
        var boids = new List<Boid2D>();
        try
        {
            for (int i = 0; i < positions.Length; i++)
            {
                var boid = new Boid2D { Position = positions[i] };
                boid.Initialize(config, i * 3 + 7, Vector2.Right); // 故意不使用连续 ID。
                boids.Add(boid);
            }
            Vector2[] normalized = positions.Select(p => bounds.HasPoint(p) ? p : new Vector2(
                Mathf.Wrap(p.X, bounds.Position.X, bounds.End.X),
                Mathf.Wrap(p.Y, bounds.Position.Y, bounds.End.Y))).ToArray();
            int[][] expected = Enumerable.Range(0, positions.Length).Select(i =>
                Enumerable.Range(0, positions.Length).Where(j => i != j
                    && ReferenceDistanceSquared(normalized[i], normalized[j], bounds.Size) <= (double)radius * radius).ToArray()).ToArray();
            var search = new NeighborSearch2D();
            long gridChecks = 0;
            foreach (NeighborSearchMode mode in Enum.GetValues<NeighborSearchMode>())
            {
                search.Search(boids, bounds, radius, mode);
                Check(search.HasResults && search.Snapshots.Count == positions.Length, "Snapshot population matches input");
                for (int i = 0; i < positions.Length; i++)
                {
                    IReadOnlyList<int> neighbors = search.GetNeighborIndices(i);
                    Check(neighbors.Order().SequenceEqual(expected[i]), $"{mode}: radius {radius}, bounds {bounds}, individual {i} matches reference without duplicates");
                    Check(search.Snapshots[i].Id == boids[i].Id, "Snapshot indices are independent of unique IDs");
                    foreach (int j in neighbors)
                    {
                        Vector2 offset = search.GetOffset(i, j);
                        Check(Math.Abs(offset.LengthSquared() - ReferenceDistanceSquared(normalized[i], normalized[j], bounds.Size)) < 0.1,
                            "Neighbor offset uses shortest toroidal distance");
                    }
                }
                double expectedAverage = positions.Length == 0 ? 0 : (double)expected.Sum(n => n.Length) / positions.Length;
                Check(search.AverageNeighbors == expectedAverage, "Measured average includes every individual and excludes self");
                Check(search.SearchMilliseconds >= 0, "Search timing is measured");
                long allPairs = positions.Length * (positions.Length - 1L);
                Check(mode == NeighborSearchMode.Naive ? search.CandidateChecks == allPairs : search.CandidateChecks <= allPairs,
                    "Directed candidate check semantics match both algorithms");
                if (mode == NeighborSearchMode.UniformGrid) gridChecks = search.CandidateChecks;
            }
            search.Invalidate();
            Check(!search.HasResults && search.Snapshots.Count == 0, "Invalidation hides stale snapshots");
            return gridChecks;
        }
        finally
        {
            foreach (Boid2D boid in boids) boid.Free();
        }
    }

    private static double ReferenceDistanceSquared(Vector2 a, Vector2 b, Vector2 size)
    {
        double closest = double.PositiveInfinity;
        for (int y = -1; y <= 1; y++)
            for (int x = -1; x <= 1; x++)
            {
                double dx = (double)b.X + x * (double)size.X - a.X;
                double dy = (double)b.Y + y * (double)size.Y - a.Y;
                closest = Math.Min(closest, dx * dx + dy * dy);
            }
        return closest;
    }

    private void CheckManagerAndUi()
    {
        var config = new BoidConfig { BoidCount = 0, MaxSpeed = 1000, PerceptionRadius = 80,
            SeparationEnabled = false, AlignmentEnabled = false, CohesionEnabled = false };
        BoidManager manager = GD.Load<PackedScene>("res://scenes/worlds/boids_world_2d.tscn").Instantiate<BoidManager>();
        manager.Initialize(config);
        AddChild(manager);
        Check(!manager.NeighborSearch.HasResults && manager.Stats.NeighborCandidateChecks is null, "Initial state is unmeasured");
        manager.Step(1.0 / 60);
        Check(manager.Stats.NeighborCandidateChecks == 0 && manager.Stats.AverageNeighbors == 0, "Empty population produces measured zeros");

        Vector2 origin = manager.WorldBounds.Position + new Vector2(100, 100);
        Boid2D removed = manager.SpawnBoid(origin, Vector2.Zero);
        manager.RemoveBoid(removed);
        Boid2D a = manager.SpawnBoid(origin, new Vector2(100, 0));
        Boid2D b = manager.SpawnBoid(origin + new Vector2(60, 0), Vector2.Zero);
        a.SetAcceleration(new Vector2(20, 0));
        manager.Step(1);
        NeighborSearch2D search = manager.NeighborSearch;
        Check(search.Snapshots[0].Id == a.Id && a.Id != 0, "Removed ID does not break snapshot lookup");
        Check(search.Snapshots[0].Position == origin && search.Snapshots[0].Velocity == new Vector2(100, 0), "Snapshot precedes all motion integration");
        Check(a.Position != origin && search.GetOffset(0, 1) == new Vector2(60, 0), "Moving nodes cannot change cached neighbor geometry");
        Check(search.GetNeighborIndices(0).SequenceEqual(new[] { 1 }) && search.GetNeighborIndices(1).SequenceEqual(new[] { 0 }), "Both queries see the same time step");
        Check(manager.Stats.NeighborCandidateChecks == 2 && manager.Stats.AverageNeighbors == 1, "Manager publishes actual search measurements");
        Check(manager.Stats.UpdateMilliseconds >= manager.Stats.NeighborSearchMilliseconds, "Total step time includes search time");
        manager._PhysicsProcess(1);
        Check(search.Snapshots[0].Position == origin, "Paused physics does not run searches");
        config.ShowGrid = !config.ShowGrid;
        manager.ConfigurationChanged();
        Check(search.HasResults, "Unrelated visual options preserve last measurements");

        var panel = new ControlPanel();
        AddChild(panel);
        panel.Bind(config, "像素");
        panel.ConfigurationChanged += manager.ConfigurationChanged;
        var selector = (OptionButton)panel.FindChild("NeighborSearchMode", true, false);
        Check(selector.Selected == 0 && selector.GetItemText(0) == "朴素遍历" && selector.GetItemText(1) == "均匀网格", "Native UI uses Chinese mode labels and shared default");
        selector.Select(1);
        selector.EmitSignal(OptionButton.SignalName.ItemSelected, 1L);
        Check(config.NeighborSearchMode == NeighborSearchMode.UniformGrid && !search.HasResults
            && manager.Stats.NeighborCandidateChecks is null, "UI mode change invalidates old measurements without resetting population");
        Check(manager.Boids.Count == 2 && manager.Boids[0] == a, "Mode switch preserves individuals");
        manager.Step(1.0 / 60);
        Check(search.Mode == NeighborSearchMode.UniformGrid && search.HasResults, "Next step uses selected mode");

        var statsPanel = new StatsPanel();
        AddChild(statsPanel);
        statsPanel.Refresh(manager.Stats, 0);
        var checks = (Label)statsPanel.FindChild("NeighborChecks", true, false);
        var timer = (Label)statsPanel.FindChild("SearchTime", true, false);
        Check(checks.Text == "2" && timer.Text != "--", "Statistics panel displays real counters and search timing");
        config.PerceptionRadius = 1;
        manager.ConfigurationChanged();
        statsPanel.Refresh(manager.Stats, 0);
        Check(!search.HasResults && checks.Text == "--" && timer.Text == "--", "Changing radius clears stale results and UI values");
        manager.Step(1.0 / 60);
        var camera = manager.GetNode<SimulationCamera2D>("Camera2D");
        camera._UnhandledInput(new InputEventMouseButton { ButtonIndex = MouseButton.WheelUp, Pressed = true });
        Check(!search.HasResults && manager.Stats.NeighborSearchMilliseconds is null, "Camera boundary changes invalidate old searches");
        manager.Step(1.0 / 60);
        Check(search.Bounds == manager.WorldBounds, "Next grid uses current camera bounds");
        manager.RemoveBoid(b);
        Check(!search.HasResults && manager.Stats.BoidCount == 1, "Removal invalidates results and updates actual count");
        manager.Step(1.0 / 60);
        Check(search.Snapshots.Count == 1 && search.GetNeighborIndices(0).Count == 0 && search.CandidateChecks == 0, "Storage shrinks and single individual excludes itself");
        manager.Reset();
        Check(!search.HasResults && manager.Stats.BoidCount == 0 && manager.Stats.AverageNeighbors is null, "Reset returns statistics to unmeasured state");
        manager.Free();
        panel.Free();
        statsPanel.Free();
    }

    private void CheckConfiguration()
    {
        const string path = "res://.godot/validation/neighbor-test.cfg";
        DirAccess.MakeDirRecursiveAbsolute(ProjectSettings.GlobalizePath("res://.godot/validation"));
        var config = new BoidConfig { NeighborSearchMode = NeighborSearchMode.UniformGrid };
        Check(config.Save(path) == Error.Ok, "Save selected search mode");
        var loaded = new BoidConfig();
        Check(loaded.Load(path) == Error.Ok && loaded.NeighborSearchMode == NeighborSearchMode.UniformGrid, "Restore selected search mode");
        using var file = new ConfigFile();
        file.SetValue("simulation", "boid_count", 3);
        Check(file.Save(path) == Error.Ok, "Write old configuration");
        loaded = new BoidConfig();
        Check(loaded.Load(path) == Error.Ok && loaded.NeighborSearchMode == NeighborSearchMode.Naive, "Old configurations default to naive mode");
        foreach (Variant invalid in new Variant[] { 99, -1, 1.5, "grid" })
        {
            loaded.NeighborSearchMode = NeighborSearchMode.UniformGrid;
            file.SetValue("simulation", "neighbor_search_mode", invalid);
            Check(file.Save(path) == Error.Ok, "Write invalid mode");
            Check(loaded.Load(path) == Error.Ok && loaded.NeighborSearchMode == NeighborSearchMode.Naive, "Invalid stored modes safely fall back");
        }
        config.NeighborSearchMode = (NeighborSearchMode)99;
        config.Normalize();
        Check(config.NeighborSearchMode == NeighborSearchMode.Naive, "Normalize rejects invalid enum values");
        DirAccess.RemoveAbsolute(ProjectSettings.GlobalizePath(path));
    }
}
