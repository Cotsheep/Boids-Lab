using System;
using System.Threading.Tasks;
using Godot;

namespace BoidsLab.Tests;

/// <summary>固定场景的明暗主题开关对照；只输出截图，不读写用户配置或捕获鼠标。</summary>
public partial class LeaderFollowRegionVisual : Node
{
    private const string Output = "res://.godot/validation/leader-follow-region";
    private Label _caption = null!;

    public override async void _Ready()
    {
        if (DisplayServer.GetName() == "headless") { GetTree().Quit(2); return; }
        try
        {
            GetWindow().Title = "BoidsLab · 跟随范围检查";
            GetWindow().Size = new Vector2I(1000, 640);
            DirAccess.MakeDirRecursiveAbsolute(ProjectSettings.GlobalizePath(Output));
            var overlay = new CanvasLayer(); AddChild(overlay);
            _caption = new Label { Position = new(24, 20) }; overlay.AddChild(_caption);
            await TwoDimensions();
            await ThreeDimensions();
            GD.Print($"PASS: 10 follow-region theme/on-off/selected-leader frames: {ProjectSettings.GlobalizePath(Output)}");
            GetTree().Quit();
        }
        catch (Exception error) { GD.PushError(error.ToString()); GetTree().Quit(1); }
    }

    private async Task TwoDimensions()
    {
        var world = GD.Load<PackedScene>("res://scenes/worlds/boids_world_2d.tscn").Instantiate<BoidManager>();
        world.Initialize(new BoidConfig { BoidCount = 0, ShowGrid = false }); AddChild(world);
        ObstacleHarness2D.ClearLayout(world);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        Vector2 center = world.ViewBounds.GetCenter();
        var leader = world.SpawnBoid(center + new Vector2(180, 0), Vector2.Right * 100);
        for (int i = 0; i < 48; i++)
        {
            float angle = i * 2.3999632f;
            float radius = Mathf.Sqrt((i + 1f) / 48) * 120;
            world.SpawnBoid(center - Vector2.Right * 15 + Vector2.FromAngle(angle) * radius,
                Vector2.FromAngle(0.15f * Mathf.Sin(i)) * 80);
        }
        world.SetLeader(leader);
        var view = world.GetNode<SimulationView>("SimulationView");
        foreach (bool light in new[] { false, true })
        {
            view.ApplyPalette(LabPalette.For(light));
            foreach (bool enabled in new[] { false, true })
            {
                world.Config.ShowLeaderFollowRegion = enabled; world.ConfigurationChanged();
                await Capture("2d", light, enabled);
            }
        }
        world.Free();
    }

    private async Task ThreeDimensions()
    {
        var world = GD.Load<PackedScene>("res://scenes/worlds/boids_world_3d.tscn").Instantiate<BoidManager3D>();
        world.Initialize(new BoidConfig { BoidCount = 0, ShowGrid = false }); AddChild(world);
        var leader = world.SpawnBoid(new(160, 0, 0), Vector3.Right * 100);
        for (int i = 0; i < 48; i++)
        {
            float angle = i * 2.3999632f;
            float radius = Mathf.Sqrt((i + 1f) / 48) * 65;
            world.SpawnBoid(new(-10 + Mathf.Cos(angle) * radius, Mathf.Sin(angle) * radius, (i % 5 - 2) * 20), Vector3.Right * 80);
        }
        world.SetLeader(leader);
        var camera = world.GetNode<SimulationCamera3D>("SimulationView/Camera3D");
        camera.SetFollowFrozen(true);
        camera.Position = new(260, 170, 470); camera.LookAt(new(15, 0, 0)); camera.Fov = 55;
        var view = world.GetNode<SimulationView3D>("SimulationView");
        foreach (bool light in new[] { false, true })
        {
            view.ApplyPalette(LabPalette.For(light));
            foreach (bool enabled in new[] { false, true })
            {
                world.Config.ShowLeaderFollowRegion = enabled; world.ConfigurationChanged();
                await Capture("3d", light, enabled);
            }
            leader.SetSelected(true);
            await Capture("3d-selected", light, true);
            leader.SetSelected(false);
        }
        world.Free();
    }

    private async Task Capture(string dimension, bool light, bool enabled)
    {
        _caption.Text = $"{dimension.ToUpperInvariant()} · {(light ? "明色" : "暗色")} · 跟随范围{(enabled ? "开启" : "关闭")}";
        _caption.AddThemeColorOverride("font_color", LabPalette.For(light).Text);
        for (int i = 0; i < 4; i++) await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        using Image image = GetViewport().GetTexture().GetImage();
        if (image.SavePng($"{Output}/{dimension}-{(light ? "light" : "dark")}-{(enabled ? "on" : "off")}.png") != Error.Ok)
            throw new InvalidOperationException("Could not save follow region render.");
    }
}
