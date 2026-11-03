using System;
using System.Threading.Tasks;
using Godot;

namespace BoidsLab.Tests;

/// <summary>显式图形样例：固定鸟群与放置预览，四种形状、两种主题；不读写用户配置或捕获鼠标。</summary>
public partial class PaletteVisual : Node
{
    private const string Output = "res://.godot/validation/palette";
    private Label _caption = null!;

    public override async void _Ready()
    {
        if (DisplayServer.GetName() == "headless") { GetTree().Quit(2); return; }
        try
        {
            GetWindow().Title = "BoidsLab · 配色检查";
            GetWindow().Size = new Vector2I(1000, 640);
            DirAccess.MakeDirRecursiveAbsolute(ProjectSettings.GlobalizePath(Output));
            var overlay = new CanvasLayer();
            AddChild(overlay);
            _caption = new Label { Position = new Vector2(24, 20) };
            overlay.AddChild(_caption);
            await TwoDimensions();
            await ThreeDimensions();
            GD.Print($"PASS: 8 palette/placement frames rendered: {ProjectSettings.GlobalizePath(Output)}");
            GetTree().Quit();
        }
        catch (Exception error) { GD.PushError(error.ToString()); GetTree().Quit(1); }
    }

    private async Task TwoDimensions()
    {
        var world = GD.Load<PackedScene>("res://scenes/worlds/boids_world_2d.tscn").Instantiate<BoidManager>();
        world.Initialize(new BoidConfig { BoidCount = 0, ShowGrid = false });
        AddChild(world);
        await Frame();
        Vector2 center = world.ViewBounds.GetCenter();
        for (int y = 0; y < 3; y++)
            for (int x = 0; x < 5; x++)
                world.SpawnBoid(center + new Vector2(-260 + x * 45, -60 + y * 60), new Vector2(30, -20));
        var editor = world.GetNode<ObstacleEditor2D>("ObstacleEditor");
        editor.AddObstacle(ObstacleShape.Rectangle, center + new Vector2(225, 200));
        foreach (bool light in new[] { false, true })
        {
            world.GetNode<SimulationView>("SimulationView").ApplyPalette(LabPalette.For(light));
            foreach (var shape in new[] { ObstacleShape.Rectangle, ObstacleShape.Circle })
            {
                editor.BeginPlacement(shape);
                editor._Input(new InputEventMouseMotion { Position = new Vector2(680, 320) });
                await Capture($"2d-{shape}", light);
                editor.CancelPlacement();
            }
        }
        world.Free();
    }

    private async Task ThreeDimensions()
    {
        var world = GD.Load<PackedScene>("res://scenes/worlds/boids_world_3d.tscn").Instantiate<BoidManager3D>();
        world.Initialize(new BoidConfig { BoidCount = 0, ShowGrid = false });
        AddChild(world);
        var camera = world.GetNode<SimulationCamera3D>("SimulationView/Camera3D");
        camera.Projection = Camera3D.ProjectionType.Perspective;
        camera.Position = new Vector3(0, 0, 400);
        camera.Quaternion = Quaternion.Identity;
        camera.Fov = 70;
        for (int y = 0; y < 3; y++)
            for (int x = 0; x < 5; x++)
                world.SpawnBoid(new Vector3(-170 + x * 30, -45 + y * 45, 0), new Vector3(20, 20, -40));
        var editor = world.GetNode<ObstacleEditor3D>("ObstacleEditor");
        editor.AddObstacle(ObstacleShape.Box, new Vector3(175, -150, 0)).SetDimensions(Vector3.One * 80);
        foreach (bool light in new[] { false, true })
        {
            world.GetNode<SimulationView3D>("SimulationView").ApplyPalette(LabPalette.For(light));
            foreach (var shape in new[] { ObstacleShape.Box, ObstacleShape.Sphere })
            {
                editor.BeginPlacement(shape);
                editor._Input(new InputEventMouseMotion { Position = new Vector2(700, 320) });
                await Capture($"3d-{shape}", light);
                editor.CancelPlacement();
            }
        }
        world.Free();
    }

    private async Task Frame()
    {
        for (int i = 0; i < 3; i++) await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
    }

    private async Task Capture(string name, bool light)
    {
        _caption.Text = $"冰岩 · {(light ? "明色" : "暗色")}    {name}    左：Boid  右上：放置预览  右下：已有障碍";
        _caption.AddThemeColorOverride("font_color", LabPalette.For(light).Text);
        await Frame();
        using Image image = GetViewport().GetTexture().GetImage();
        Error error = image.SavePng($"{Output}/{name}-{(light ? "light" : "dark")}.png");
        if (error != Error.Ok) throw new InvalidOperationException($"Could not save palette render: {error}");
    }
}
