using System;
using System.Threading.Tasks;
using Godot;

namespace BoidsLab.Tests;

public partial class CameraFade3DChecks : Node
{
    private int _checks;
    public override async void _Ready()
    {
        if (DisplayServer.GetName() != "headless") { GetTree().Quit(2); return; }
        try
        {
            const string path = "res://.godot/validation/camera-fade-3d.cfg";
            new BoidConfig { BoidCount = 0 }.Save(path);
            var app = GD.Load<PackedScene>("res://scenes/main_3d.tscn").Instantiate<MainController>();
            app.World3D = (SimulationWorldDefinition)app.World3D.Duplicate();
            app.World3D.ConfigurationPath = path;
            AddChild(app);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            var world = app.GetNode<BoidManager3D>("World/BoidsWorld3D");
            var camera = world.GetNode<SimulationCamera3D>("SimulationView/Camera3D");
            var view = world.GetNode<SimulationView3D>("SimulationView");
            var toggle = (CheckBox)app.GetNode<ControlPanel>("%ControlPanel").FindChild("FadeNearbyBoids", true, false);
            var leader = world.SpawnBoid(Vector3.Zero, Vector3.Zero);
            var ordinary = world.SpawnBoid(Vector3.Back * 180, Vector3.Zero);
            var body = ordinary.GetNode<MeshInstance3D>("Body");
            var source = (BaseMaterial3D)body.Mesh.SurfaceGetMaterial(0);
            Check(toggle.ButtonPressed && body.MaterialOverride is null, "Default fade setting leaves observation rendering unchanged");
            world.SetLeader(leader);
            Vector2 pointer = GetViewport().GetVisibleRect().Size * 0.5f;
            ordinary.Position = camera.ProjectRayOrigin(pointer) + camera.ProjectRayNormal(pointer) * 30;
            world.Step(1.0 / 60);
            var stats = world.Stats;
            Vector3 position = ordinary.Position, velocity = ordinary.Velocity;
            Quaternion facing = ordinary.Quaternion;
            Transform3D cameraTransform = camera.Transform;
            Material? faded = body.MaterialOverride;
            Check(faded is BaseMaterial3D && faded != source && source.DistanceFadeMode == BaseMaterial3D.DistanceFadeModeEnum.Disabled,
                "Follow mode uses a private fade material without mutating the scene resource");
            Check(world.PickBoid(pointer, out _) == ordinary, "Near-camera fading preserves geometric selection");
            toggle.ButtonPressed = false;
            var loaded = new BoidConfig();
            Check(body.MaterialOverride is null && loaded.Load(path) == Error.Ok && !loaded.FadeNearbyBoids,
                "UI disables fading immediately and persists the choice");
            Check(world.PickBoid(pointer, out _) == ordinary && world.Stats.Equals(stats)
                && position == ordinary.Position && velocity == ordinary.Velocity && facing == ordinary.Quaternion
                && cameraTransform == camera.Transform, "Visual toggle preserves picking, simulation, statistics and camera framing");
            toggle.ButtonPressed = true;
            Check(body.MaterialOverride == faded && loaded.Load(path) == Error.Ok && loaded.FadeNearbyBoids,
                "Reenabling reuses the material and persists the enabled setting");
            ordinary.SetSelected(true);
            Check(body.MaterialOverride == ordinary.SelectionMaterial, "Selected ordinary boid keeps its opaque highlight");
            ordinary.SetSelected(false);
            Check(body.MaterialOverride == faded, "Deselection restores distance fading");
            var spawned = world.SpawnBoid(Vector3.Back * 100, Vector3.Zero);
            Check(spawned.GetNode<MeshInstance3D>("Body").MaterialOverride == faded, "New ordinary boids share the world's fade material");
            world.SetLeader(ordinary);
            Check(body.MaterialOverride == ordinary.LeaderMaterial
                && leader.GetNode<MeshInstance3D>("Body").MaterialOverride == faded, "Transfer keeps the new identity clear and fades the former leader normally");
            ordinary.SetSelected(true);
            Check(body.MaterialOverride == ordinary.SelectedLeaderMaterial, "Selected leader retains its distinct opaque appearance");
            world.SetLeader(null);
            ordinary.SetSelected(false);
            Check(body.MaterialOverride is null && spawned.GetNode<MeshInstance3D>("Body").MaterialOverride is null,
                "Leaving follow mode restores original materials for all boids");
            world.SetLeader(ordinary);
            world.Config.BoidCount = 3;
            world.Reset();
            Check(world.Leader is null && world.Boids[0].GetNode<MeshInstance3D>("Body").MaterialOverride is null,
                "Reset restores observation appearance even when population count stays unchanged");
            using var old = new ConfigFile();
            old.SetValue("simulation", "boid_count", 0);
            old.Save(path);
            loaded = new BoidConfig();
            Check(loaded.Load(path) == Error.Ok && loaded.FadeNearbyBoids, "Older configurations enable near-camera fading by default");
            app.Free();
            CheckIdenticalTrajectories();
            GD.Print($"PASS: {_checks} near-camera fade/configuration/lifecycle/trajectory assertions (headless).");
            GetTree().Quit();
        }
        catch (Exception error) { GD.PushError(error.ToString()); GetTree().Quit(1); }
    }

    private void CheckIdenticalTrajectories()
    {
        BoidManager3D Make(bool fade)
        {
            var world = GD.Load<PackedScene>("res://scenes/worlds/boids_world_3d.tscn").Instantiate<BoidManager3D>();
            world.Initialize(new BoidConfig { BoidCount = 25, RandomSeed = 431, FadeNearbyBoids = fade,
                WorldWidth = 300, WorldHeight = 300, WorldDepth = 300, NeighborSearchMode = NeighborSearchMode.UniformGrid });
            AddChild(world);
            world.SetLeader(world.Boids[0]);
            world.SetLeaderControlActive(true);
            world.LeaderController._Input(new InputEventAction { Action = "leader_forward", Pressed = true });
            return world;
        }
        var plain = Make(false);
        var faded = Make(true);
        for (int step = 0; step < 180; step++)
        {
            foreach (var world in new[] { plain, faded })
            {
                world.LeaderController.RotateView(new Vector2(0.5f, 0.05f));
                world.Step(1.0 / 60);
            }
            for (int i = 0; i < plain.Boids.Count; i++)
                Check(plain.Boids[i].Position == faded.Boids[i].Position && plain.Boids[i].Velocity == faded.Boids[i].Velocity
                    && plain.Boids[i].Quaternion == faded.Boids[i].Quaternion,
                    "Fading does not change controlled leader or follower trajectories");
            Check(plain.NeighborSearch.CandidateChecks == faded.NeighborSearch.CandidateChecks,
                "Fading leaves neighbor-search results unchanged");
        }
        plain.Free();
        faded.Free();
    }

    private void Check(bool condition, string message)
    {
        _checks++;
        if (!condition) throw new InvalidOperationException(message);
    }
}
