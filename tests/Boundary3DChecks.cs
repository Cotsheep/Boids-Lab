using System;
using Godot;

namespace BoidsLab.Tests;

public partial class Boundary3DChecks : Node
{
    private int _checks;
    public override void _Ready()
    {
        if (DisplayServer.GetName() != "headless") { GetTree().Quit(2); return; }
        try
        {
            var config = new BoidConfig { BoidCount = 0, MaxSpeed = 60, MaxForce = 120,
                SeparationEnabled = false, AlignmentEnabled = false, CohesionEnabled = false,
                WorldWidth = 400, WorldHeight = 400, WorldDepth = 400 };
            var world = GD.Load<PackedScene>("res://scenes/worlds/boids_world_3d.tscn").Instantiate<BoidManager3D>();
            world.Initialize(config);
            AddChild(world);
            foreach (Vector3 direction in new[] { Vector3.Right, Vector3.Left, Vector3.Up, Vector3.Down,
                Vector3.Forward, Vector3.Back, Vector3.One.Normalized(), -Vector3.One.Normalized() })
            {
                var boid = world.SpawnBoid(direction * 500, direction * 60);
                Vector3 start = boid.Position;
                world.Step(1.0 / 60);
                Check(start.DistanceTo(boid.Position) <= 1.001f, "Outside motion stays continuous");
                Check(boid.Acceleration.Dot(direction) < 0, "Outside boid receives inward acceleration");
                for (int i = 0; i < 900; i++) world.Step(1.0 / 60);
                Check(world.WorldBounds.HasPoint(boid.Position), "Every face and corner returns without teleporting");
                world.RemoveBoid(boid);
            }
            var outside = world.SpawnBoid(new Vector3(150, 0, 0), Vector3.Right * 60);
            var camera = world.GetNode<SimulationCamera3D>("SimulationView/Camera3D");
            Transform3D view = camera.Transform;
            float size = camera.Size;
            config.WorldWidth = 100;
            world.ConfigurationChanged();
            Check(outside.Position == new Vector3(150, 0, 0) && !world.IsRunning, "Paused resize leaves population in place");
            Check(camera.Transform == view && camera.Size == size, "Resize preserves actual camera view");
            Check(world.WorldBounds.Size.X == 100, "Resize changes the simulation bounds immediately");
            config.MaxForce = 0;
            world.Step(1);
            Check(outside.Position.IsEqualApprox(new Vector3(210, 0, 0)), "Zero acceleration never forces a return");
            var sample = new BoidSnapshot3D(0, new Vector3(49, 0, 0), Vector3.Right * 60);
            config.MaxForce = 120;
            Check(BoundarySteering3D.Apply(sample, world.WorldBounds, config, Vector3.Right * 120).X < 0,
                "Outward flocking cannot cancel boundary steering");
            config.WorldDepth = 9870;
            config.FollowCameraDistance = 550;
            const string path = "res://.godot/validation/boundary-3d.cfg";
            Check(config.Save(path) == Error.Ok, "New settings save");
            var loaded = new BoidConfig();
            Check(loaded.Load(path) == Error.Ok && loaded.WorldWidth == 100 && loaded.WorldDepth == 9870
                && loaded.FollowCameraDistance == 550, "Three-dimensional settings round trip");
            using var old = new ConfigFile();
            old.SetValue("simulation", "boid_count", 7);
            old.Save(path);
            loaded = new BoidConfig();
            Check(loaded.Load(path) == Error.Ok && loaded.WorldWidth == 2560 && loaded.WorldHeight == 1440
                && loaded.WorldDepth == 1440 && loaded.FollowCameraDistance == 300, "Old settings receive new defaults");
            world.QueueFree();
            GD.Print($"PASS: {_checks} 3D soft boundary/configuration assertions (headless).");
            GetTree().Quit();
        }
        catch (Exception error) { GD.PushError(error.ToString()); GetTree().Quit(1); }
    }

    private void Check(bool condition, string message)
    {
        _checks++;
        if (!condition) throw new InvalidOperationException(message);
    }
}
