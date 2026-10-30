using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;

namespace BoidsLab.Tests;

/// <summary>显式运行的图形检查：用同一运动快照比较开关效果，不能代替真实键盘验收。</summary>
public partial class CameraFadeVisual : Node
{
    private const string Output = "res://.godot/validation/camera-fade";
    private Label _caption = null!;
    private readonly List<string> _metrics = new() { "scenario,distance,fade,green_pixels,leader_pixels" };

    public override async void _Ready()
    {
        if (DisplayServer.GetName() == "headless") { GD.Print("Run this scene with the graphical renderer."); GetTree().Quit(2); return; }
        try
        {
            GetWindow().Title = "BoidsLab · 相机淡化图形检查";
            GetWindow().Size = new Vector2I(1200, 720);
            DirAccess.MakeDirRecursiveAbsolute(ProjectSettings.GlobalizePath(Output));
            var overlay = new CanvasLayer();
            AddChild(overlay);
            _caption = new Label { Position = new Vector2(20, 16), MouseFilter = Control.MouseFilterEnum.Ignore };
            overlay.AddChild(_caption);
            await Probe();
            foreach (int count in new[] { 40, 200 })
                foreach (string mode in new[] { "rest", "turn", "strafe", "backward" })
                {
                    var world = Create(count);
                    for (int i = 0; i < 1200; i++) world.Step(1.0 / 60);
                    if (mode != "rest")
                    {
                        world.SetLeaderControlActive(true);
                        string action = mode == "strafe" ? "leader_right" : mode == "backward" ? "leader_backward" : "leader_forward";
                        world.LeaderController._Input(new InputEventAction { Action = action, Pressed = true });
                        for (int i = 0; i < 600; i++)
                        {
                            if (mode == "turn") world.LeaderController.RotateView(new Vector2(0.5f, Mathf.Sin(i / 120f) * 0.1f));
                            world.Step(1.0 / 60);
                        }
                        world.SetLeaderControlActive(false);
                    }
                    var camera = world.GetNode<SimulationCamera3D>("SimulationView/Camera3D");
                    foreach (int distance in new[] { 300, 650 })
                    {
                        world.Config.FollowCameraDistance = distance;
                        camera._Process(10);
                        await Capture(world, $"{count}-{mode}", false);
                        await Capture(world, $"{count}-{mode}", true);
                    }
                    world.Free();
                    GD.Print($"Rendered same-state pairs: count={count}, mode={mode}");
                }
            using var file = Godot.FileAccess.Open(Output + "/metrics.csv", Godot.FileAccess.ModeFlags.Write);
            file.StoreString(string.Join("\n", _metrics) + "\n");
            GD.Print($"PASS: Compatibility render probe and 16 flock-view pairs. Images: {ProjectSettings.GlobalizePath(Output)}");
            GetTree().Quit();
        }
        catch (Exception error) { GD.PushError(error.ToString()); GetTree().Quit(1); }
    }

    private BoidManager3D Create(int count)
    {
        var world = GD.Load<PackedScene>("res://scenes/worlds/boids_world_3d.tscn").Instantiate<BoidManager3D>();
        world.Initialize(new BoidConfig { BoidCount = 0, NeighborSearchMode = NeighborSearchMode.UniformGrid });
        AddChild(world);
        var leader = world.SpawnBoid(Vector3.Zero, Vector3.Zero);
        for (int i = 0; i < count; i++)
        {
            float y = 1 - 2f * (i + 0.5f) / count;
            float angle = i * 2.399963f;
            float r = Mathf.Sqrt(1 - y * y);
            world.SpawnBoid(new Vector3(r * Mathf.Cos(angle), y, r * Mathf.Sin(angle)) * 160 + Vector3.Back * 180, Vector3.Zero);
        }
        world.SetLeader(leader);
        return world;
    }

    private async Task Probe()
    {
        var world = Create(0);
        var camera = world.GetNode<SimulationCamera3D>("SimulationView/Camera3D");
        Vector3 direction = (world.Leader!.Position - camera.Position).Normalized();
        var blocker = world.SpawnBoid(camera.Position + direction * 20, Vector3.Zero);
        var before = await Capture(world, "near-blocker", false);
        var after = await Capture(world, "near-blocker", true);
        if (before.Green < 4000 || after.Green > before.Green / 10 || after.Leader <= before.Leader)
            throw new InvalidOperationException($"Near-camera rendering did not clear occlusion: {before} -> {after}");
        blocker.SetSelected(true);
        await Capture(world, "near-selected", true);
        blocker.SetSelected(false);
        foreach (int depth in new[] { 120, 180, 240 })
        {
            blocker.Position = camera.Position + direction * depth + camera.Basis.X * 15;
            await Capture(world, $"fade-depth-{depth}", true);
        }
        world.SetLeader(null);
        await Capture(world, "observation-restored", true);
        world.Free();
    }

    private async Task<(int Green, int Leader)> Capture(BoidManager3D world, string scenario, bool fade)
    {
        world.Config.FadeNearbyBoids = fade;
        world.ConfigurationChanged();
        string name = $"{scenario}-{world.Config.FollowCameraDistance}-{(fade ? "on" : "off")}";
        _caption.Text = $"{scenario}  |  视距 {world.Config.FollowCameraDistance}  |  淡化{(fade ? "开启" : "关闭")}";
        for (int i = 0; i < 3; i++) await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        using Image image = GetViewport().GetTexture().GetImage();
        Error error = image.SavePng(Output + "/" + name + ".png");
        if (error != Error.Ok) throw new InvalidOperationException($"Could not save render: {error}");
        int green = 0, leader = 0;
        for (int y = 50; y < image.GetHeight(); y++)
            for (int x = 0; x < image.GetWidth(); x++)
            {
                Color c = image.GetPixel(x, y);
                if (c.G > 0.12f && c.G > c.R * 1.2f && c.G > c.B * 1.05f) green++;
                if (c.R > 0.4f && c.B > 0.5f && c.R > c.G * 1.2f && c.B > c.G * 1.2f) leader++;
            }
        _metrics.Add($"{scenario},{world.Config.FollowCameraDistance},{fade},{green},{leader}");
        return (green, leader);
    }
}
