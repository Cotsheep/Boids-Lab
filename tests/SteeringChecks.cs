using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace BoidsLab.Tests;

/// <summary>验证规则方向、合成限制、快照时序与真实管理器的持续运行。</summary>
public partial class SteeringChecks : Node
{
    private int _checks;
    private static readonly Rect2 Bounds = new(0, 0, 1000, 1000);

    public override void _Ready()
    {
        if (DisplayServer.GetName() != "headless")
        {
            GD.PushError("Run steering_checks.tscn with --headless.");
            GetTree().Quit(2);
            return;
        }
        try
        {
            CheckIndividualRules();
            CheckDegenerateCases();
            CheckSearchEquivalence();
            CheckManagerAndControls();
            CheckConfiguration();
            GD.Print($"PASS: {_checks} steering/controls/integration assertions (headless).");
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

    private void Near(Vector2 actual, Vector2 expected, string message, float tolerance = 0.001f) =>
        Check(actual.DistanceTo(expected) <= tolerance, $"{message}: {actual} != {expected}");

    private static BoidConfig Config() => new()
    {
        MaxSpeed = 100, MaxForce = 1000, PerceptionRadius = 80, SeparationRadius = 25,
        SeparationWeight = 1, AlignmentWeight = 1, CohesionWeight = 1,
        SeparationEnabled = false, AlignmentEnabled = false, CohesionEnabled = false,
    };

    private Vector2[] Evaluate(BoidConfig config, BoidSnapshot[] states, NeighborSearchMode mode = NeighborSearchMode.Naive)
    {
        var boids = new List<Boid2D>();
        try
        {
            foreach (BoidSnapshot state in states)
            {
                var boid = new Boid2D { Position = state.Position };
                boid.Initialize(config, state.Id, state.Velocity);
                boids.Add(boid);
            }
            var search = new NeighborSearch2D();
            search.Search(boids, config.PerceptionRadius, mode);
            return Enumerable.Range(0, states.Length).Select(i => BoidSteering.Calculate(search, i, config)).ToArray();
        }
        finally
        {
            foreach (Boid2D boid in boids) boid.Free();
        }
    }

    private void CheckIndividualRules()
    {
        var config = Config();
        BoidSnapshot[] pair = { new(7, new(100, 100), Vector2.Zero), new(12, new(110, 100), Vector2.Zero) };
        config.SeparationEnabled = true;
        Vector2[] result = Evaluate(config, pair);
        Near(result[0], new Vector2(-100, 0), "Separation accelerates away from close neighbor");
        Near(result[1], -result[0], "Pair separates in opposite directions");
        config.SeparationWeight = 0.3f;
        Near(Evaluate(config, pair)[0], new Vector2(-30, 0), "Rule weight scales contribution before total clamp");
        config.SeparationWeight = 0;
        Near(Evaluate(config, pair)[0], Vector2.Zero, "Zero weight disables a rule");
        config.SeparationWeight = 1;
        config.SeparationRadius = 9;
        Near(Evaluate(config, pair)[0], Vector2.Zero, "Distant perceived neighbor does not trigger separation");
        config.SeparationRadius = 10;
        Near(Evaluate(config, pair)[0], new Vector2(-100, 0), "Separation includes exact radius boundary");
        config.SeparationRadius = 25;
        BoidSnapshot[] unequal = { pair[0], pair[1], new(20, new(100, 120), Vector2.Zero) };
        Near(Evaluate(config, unequal)[0], new Vector2(-2, -1).Normalized() * 100, "Closer neighbor has greater directional influence");

        config.SeparationEnabled = false;
        config.AlignmentEnabled = true;
        BoidSnapshot[] turning = { new(7, new(100, 100), new(100, 0)), new(12, new(110, 100), new(0, 100)) };
        Near(Evaluate(config, turning)[0], new Vector2(-100, 100), "Alignment steers toward average velocity direction");
        config.AlignmentEnabled = false;
        config.CohesionEnabled = true;
        Near(Evaluate(config, pair)[0], new Vector2(100, 0), "Cohesion accelerates toward neighbor center");
        BoidSnapshot[] seam = { new(7, new(1, 1), Vector2.Zero), new(12, new(999, 999), Vector2.Zero) };
        Near(Evaluate(config, seam)[0], Vector2.Zero, "Opposite viewport edges are not neighboring positions");
        config.CohesionEnabled = false;
        config.SeparationEnabled = true;
        Near(Evaluate(config, seam)[0], Vector2.Zero, "Separation does not jump across the viewport");

        config.CohesionEnabled = config.AlignmentEnabled = true;
        config.SeparationWeight = config.AlignmentWeight = config.CohesionWeight = 5;
        config.MaxForce = 30;
        Check(Evaluate(config, turning).All(a => a.IsFinite() && a.Length() <= 30.001f), "Weighted sum remains within maximum acceleration");
    }

    private void CheckDegenerateCases()
    {
        var config = Config();
        config.SeparationEnabled = config.AlignmentEnabled = config.CohesionEnabled = true;
        Check(Evaluate(config, Array.Empty<BoidSnapshot>()).Length == 0, "Empty population is valid");
        BoidSnapshot self = new(7, new(100, 100), new(100, 0));
        Near(Evaluate(config, new[] { self })[0], Vector2.Zero, "No neighbors means no steering or braking");
        BoidSnapshot[] symmetric = { self, new(12, new(110, 100), new(0, 100)), new(20, new(90, 100), new(0, -100)) };
        Near(Evaluate(config, symmetric)[0], Vector2.Zero, "Zero average direction and symmetric forces do not cause arbitrary braking");
        config.AlignmentEnabled = config.CohesionEnabled = false;
        BoidSnapshot[] overlap = { self with { Velocity = Vector2.Zero }, new(12, self.Position, Vector2.Zero) };
        Vector2[] split = Evaluate(config, overlap);
        Check(split.All(v => v.IsFinite() && v.Length() > 0), "Coincident individuals receive finite nonzero separation");
        Near(split[0], -split[1], "Coincident pair gets opposite deterministic directions");
        Near(Evaluate(config, overlap.Reverse().ToArray())[1], split[0], "Coincidence fallback depends on IDs, not iteration order");
        config.MaxSpeed = 0;
        Near(Evaluate(config, overlap)[0], Vector2.Zero, "Zero max speed yields no flock steering");
        config.MaxSpeed = 100;
        config.MaxForce = 0;
        Near(Evaluate(config, overlap)[0], Vector2.Zero, "Zero max acceleration yields no flock steering");

        config.MaxForce = 50;
        var boid = new Boid2D();
        try
        {
            boid.Initialize(config, 1, Vector2.Zero);
            boid.SetAcceleration(new Vector2(30, 0));
            boid.UpdateBoid(0.1, new Vector2(0, 40));
            Near(boid.Acceleration, new Vector2(30, 40), "External and flock accelerations are combined");
            Near(boid.Velocity, new Vector2(3, 4), "Acceleration uses seconds, not per-frame increments");
            boid.UpdateBoid(0.1, new Vector2(1000, 1000));
            Check(boid.Acceleration.Length() <= 50.001f, "Combined external and steering acceleration is capped");
            boid.UpdateBoid(0.1);
            Near(boid.Acceleration, new Vector2(30, 0), "External input persists but steering does not leak into next step");
        }
        finally
        {
            boid.Free();
        }
    }

    private void CheckSearchEquivalence()
    {
        var config = new BoidConfig();
        var random = new Random(42);
        var states = Enumerable.Range(0, 120).Select(i => new BoidSnapshot(i * 3 + 7,
            new Vector2((float)random.NextDouble() * 1000, (float)random.NextDouble() * 1000),
            Vector2.FromAngle((float)random.NextDouble() * Mathf.Tau) * config.MaxSpeed)).ToArray();
        foreach (float radius in new[] { 1f, 80f, 500f })
        {
            config.PerceptionRadius = radius;
            Vector2[] naive = Evaluate(config, states);
            Vector2[] grid = Evaluate(config, states, NeighborSearchMode.UniformGrid);
            Vector2[] reversed = Evaluate(config, states.Reverse().ToArray());
            for (int i = 0; i < states.Length; i++)
            {
                Near(grid[i], naive[i], "Search modes produce equivalent steering within float tolerance", 0.005f);
                Near(reversed[states.Length - i - 1], naive[i], "List order does not bias steering", 0.005f);
            }
        }
    }

    private void CheckManagerAndControls()
    {
        var config = Config();
        config.BoidCount = 0;
        config.AlignmentEnabled = true;
        BoidManager manager = GD.Load<PackedScene>("res://scenes/worlds/boids_world_2d.tscn").Instantiate<BoidManager>();
        manager.Initialize(config);
        AddChild(manager);
        Vector2 origin = manager.ViewBounds.Position + new Vector2(100, 100);
        Boid2D a = manager.SpawnBoid(origin, new Vector2(100, 0));
        Boid2D b = manager.SpawnBoid(origin + new Vector2(10, 0), new Vector2(0, 100));
        manager.Step(0.1);
        Near(a.Acceleration, new Vector2(-100, 100), "Manager applies rule acceleration to first individual");
        Near(b.Acceleration, new Vector2(100, -100), "Later individual uses old snapshot velocity, not already-updated node");
        Near(a.Velocity, new Vector2(90, 10), "Manager integrates steering over physical time step");
        var panel = new ControlPanel();
        AddChild(panel);
        panel.Bind(config, "像素");
        panel.ConfigurationChanged += manager.ConfigurationChanged;
        var align = (CheckBox)panel.FindChild("AlignmentEnabled", true, false);
        align.ButtonPressed = false;
        Vector2 before = a.Velocity;
        manager.Step(0.1);
        Near(a.Acceleration, Vector2.Zero, "UI disabling rule clears its contribution on next step");
        Near(a.Velocity, before, "Disabling rules preserves inertia");
        var cohere = (CheckBox)panel.FindChild("CohesionEnabled", true, false);
        cohere.ButtonPressed = true;
        var weight = (SpinBox)panel.FindChild("CohesionWeight", true, false);
        weight.Value = 0;
        manager.Step(0.1);
        Near(a.Acceleration, Vector2.Zero, "UI zero weight takes effect immediately");
        weight.Value = 1;
        manager.Step(0.1);
        Check(a.Acceleration.LengthSquared() > 0, "UI nonzero weight restores behavior");
        var radius = (SpinBox)panel.FindChild("SeparationRadius", true, false);
        radius.Value = 45;
        Check(config.SeparationRadius == 45, "Separation radius shares live configuration");
        manager.SetRunning(false);
        Vector2 paused = a.Position;
        manager._PhysicsProcess(0.1);
        Near(a.Position, paused, "Pause prevents both steering and motion updates");

        config.BoidCount = 80;
        config.MaxSpeed = 120;
        config.MaxForce = 200;
        config.SeparationEnabled = config.AlignmentEnabled = config.CohesionEnabled = true;
        config.SeparationRadius = 25;
        config.SeparationWeight = 1.5f;
        foreach (NeighborSearchMode mode in Enum.GetValues<NeighborSearchMode>())
        {
            config.NeighborSearchMode = mode;
            manager.Reset();
            Check(manager.Boids.All(x => x.Acceleration == Vector2.Zero), "Reset clears prior steering state");
            Vector2[] initialVelocity = manager.Boids.Select(x => x.Velocity).ToArray();
            manager.SetRunning(true);
            for (int frame = 0; frame < 300; frame++)
            {
                manager._PhysicsProcess(1.0 / 60);
                Check(manager.Boids.All(x => x.Position.IsFinite() && x.Velocity.IsFinite()
                    && x.Acceleration.IsFinite() && float.IsFinite(x.Rotation)
                    && x.Velocity.Length() <= config.MaxSpeed + 0.01f
                    && x.Acceleration.Length() <= config.MaxForce + 0.01f
                    ), "Sustained flocking preserves finite state and speed/force caps");
            }
            Check(manager.Boids.Where((x, i) => x.Velocity.DistanceTo(initialVelocity[i]) > 1).Any(), "Enabled rules actually change trajectories");
            Check(manager.Stats.UpdateMilliseconds >= manager.Stats.NeighborSearchMilliseconds, "Total time includes steering and motion");
        }
        panel.Free();
        manager.Free();
    }

    private void CheckConfiguration()
    {
        const string path = "res://.godot/validation/steering-test.cfg";
        DirAccess.MakeDirRecursiveAbsolute(ProjectSettings.GlobalizePath("res://.godot/validation"));
        var config = new BoidConfig { SeparationRadius = 37, SeparationEnabled = false,
            AlignmentWeight = 2.3f, CohesionWeight = 0.4f };
        Check(config.Save(path) == Error.Ok, "Save rule settings");
        var loaded = new BoidConfig();
        Check(loaded.Load(path) == Error.Ok && loaded.SeparationRadius == 37 && !loaded.SeparationEnabled
            && loaded.AlignmentWeight == 2.3f && loaded.CohesionWeight == 0.4f, "Restore radius, switches and weights");
        using var oldFile = new ConfigFile();
        oldFile.SetValue("simulation", "boid_count", 20);
        Check(oldFile.Save(path) == Error.Ok, "Write old configuration");
        loaded = new BoidConfig();
        Check(loaded.Load(path) == Error.Ok && loaded.SeparationRadius == 25, "Old config receives default separation radius");
        loaded.SeparationRadius = float.NaN;
        loaded.Normalize();
        Check(loaded.SeparationRadius == 25, "Invalid radius normalizes to default");
        DirAccess.RemoveAbsolute(ProjectSettings.GlobalizePath(path));
    }
}
