using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;

namespace BoidsLab.Tests;

/// <summary>在复制的配色资源中比较完整身份状态，不修改默认主题或用户配置。</summary>
public partial class IdentityPaletteVisual : Node
{
    private const string Output = "res://.godot/validation/identity-options";
    private readonly record struct Scheme(string Id, string Title, string Description, string[] Light, string[] Dark);
    private readonly record struct Tile(SubViewport Viewport, BoidManager World, Boid2D Leader, Boid2D Selected, bool SelectLeader);
    private static readonly Scheme[] Schemes =
    {
        new("a", "A · 珊瑚领队 × 青灰群体", "暖色主角、安静背景；普通选中用晴蓝，选中领队用亮珊瑚。",
            new[] { "53778c", "247cc4", "e95f50", "d94c40", "b43a30" },
            new[] { "9cb7cf", "7fd4ff", "ff9b87", "ffe4dc", "ff9b87" }),
        new("b", "B · 薄荷领队 × 橙色群体", "保留橙色群体；用薄荷青明确区分领队，普通选中用天蓝。",
            new[] { "df6415", "267bc1", "008b7c", "006f65", "006a60" },
            new[] { "ffa94d", "8ccbff", "68dcc0", "c2fff0", "68dcc0" }),
        new("c", "C · 天蓝领队 × 橙色群体", "橙蓝对比清楚；普通选中用玫瑰红，领队使用独立蓝色系。",
            new[] { "df6415", "d95377", "2d7cc5", "185ea9", "245c92" },
            new[] { "ffa94d", "ff96b2", "83bfff", "d3ebff", "83bfff" }),
    };

    public override async void _Ready()
    {
        if (DisplayServer.GetName() == "headless") { GetTree().Quit(2); return; }
        try
        {
            GetWindow().Title = "BoidsLab · 身份配色候选";
            GetWindow().Size = new(1100, 760);
            DirAccess.MakeDirRecursiveAbsolute(ProjectSettings.GlobalizePath(Output));
            var layer = new CanvasLayer(); AddChild(layer);
            var background = new ColorRect { Color = new("e8e9ec"), Size = new(1100, 760), MouseFilter = Control.MouseFilterEnum.Ignore };
            layer.AddChild(background);
            Scheme[] plans = Array.IndexOf(OS.GetCmdlineUserArgs(), "--current") >= 0
                ? new[] { new Scheme("current", "当前工程配色 · 方案 C", "普通选中为玫瑰红，领队为天蓝；领队选中只加强同一个圆环。",
                    Array.Empty<string>(), Array.Empty<string>()) }
                : Schemes;
            foreach (Scheme scheme in plans)
            {
                var content = new VBoxContainer { Position = new(20, 16), Size = new(1060, 728) };
                content.AddThemeConstantOverride("separation", 8); layer.AddChild(content);
                content.AddChild(Text(scheme.Title, new("242a34"), 25));
                content.AddChild(Text(scheme.Description, new("47515e"), 16));
                var columns = new HBoxContainer(); columns.AddThemeConstantOverride("separation", 16); content.AddChild(columns);
                var tiles = new List<Tile>();
                foreach (bool light in new[] { true, false })
                {
                    LabPalette palette = MakePalette(scheme, light);
                    var card = new PanelContainer { CustomMinimumSize = new(522, 640), SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
                    card.AddThemeStyleboxOverride("panel", new StyleBoxFlat
                    {
                        BgColor = palette.Canvas, CornerRadiusTopLeft = 12, CornerRadiusTopRight = 12,
                        CornerRadiusBottomLeft = 12, CornerRadiusBottomRight = 12,
                        ContentMarginLeft = 12, ContentMarginRight = 12, ContentMarginTop = 10, ContentMarginBottom = 10,
                    });
                    columns.AddChild(card);
                    var column = new VBoxContainer(); column.AddThemeConstantOverride("separation", 7); card.AddChild(column);
                    column.AddChild(Text(light ? "明色主题" : "暗色主题", palette.Text, 20));
                    var legend = new HBoxContainer(); legend.AddThemeConstantOverride("separation", 10); column.AddChild(legend);
                    Swatch(legend, "普通", palette.Boid, palette.Text);
                    Swatch(legend, "选中", palette.Selection, palette.Text);
                    Swatch(legend, "领队", palette.Leader, palette.Text);
                    Swatch(legend, "选中领队", palette.SelectedLeader, palette.Text);
                    foreach (bool selectLeader in new[] { false, true })
                    {
                        column.AddChild(Text(selectLeader ? "领队选中：只加强右侧同一个圆环" : "普通选中：左侧选择环，右侧领队身份环", palette.Muted, 15));
                        var container = new SubViewportContainer { CustomMinimumSize = new(498, 260), Stretch = true };
                        column.AddChild(container);
                        var viewport = new SubViewport { Size = new(498, 260), RenderTargetUpdateMode = SubViewport.UpdateMode.Always };
                        container.AddChild(viewport);
                        var world = GD.Load<PackedScene>("res://scenes/worlds/boids_world_2d.tscn").Instantiate<BoidManager>();
                        world.Initialize(new BoidConfig { BoidCount = 0, ShowGrid = false, ShowLeaderFollowRegion = true, AvoidanceEnabled = false });
                        viewport.AddChild(world); ObstacleHarness2D.ClearLayout(world);
                        Vector2 center = world.ViewBounds.GetCenter();
                        var leader = world.SpawnBoid(center + Vector2.Right * 180, Vector2.Right * 100);
                        world.SetLeader(leader);
                        var selected = world.SpawnBoid(center + Vector2.Left * 145, Vector2.Right * 80);
                        for (int i = 0; i < 18; i++)
                            world.SpawnBoid(center + new Vector2(30, 0) + Vector2.FromAngle(i * 2.3999632f)
                                * Mathf.Sqrt((i + 1f) / 18) * 55, Vector2.Right * 80);
                        world.GetNode<SimulationView>("SimulationView").ApplyPalette(palette);
                        tiles.Add(new(viewport, world, leader, selected, selectLeader));
                    }
                }
                await Frames();
                foreach (Tile tile in tiles)
                {
                    var camera = tile.World.GetNode<SimulationCamera2D>("Camera2D");
                    camera.Position = tile.Leader.Position - Vector2.Right * 180;
                    camera.Zoom = Vector2.One; camera.ForceUpdateScroll();
                }
                await Frames();
                foreach (Tile tile in tiles)
                {
                    tile.Viewport.NotifyMouseEntered();
                    Boid2D target = tile.SelectLeader ? tile.Leader : tile.Selected;
                    Vector2 point = target.GetGlobalTransformWithCanvas() * Vector2.Zero;
                    foreach (bool pressed in new[] { true, false })
                        tile.Viewport.PushInput(new InputEventMouseButton { Position = point, GlobalPosition = point,
                            ButtonIndex = MouseButton.Left, Pressed = pressed, ButtonMask = pressed ? MouseButtonMask.Left : 0 }, true);
                    if (tile.World.SelectedBoid != target) throw new InvalidOperationException("Preview selection did not reach the real world.");
                    tile.World.SetLeaderTarget(tile.Leader.Position);
                    tile.World.SetInteractionEnabled(false);
                }
                await Frames();
                using Image frame = GetViewport().GetTexture().GetImage();
                if (frame.SavePng($"{Output}/option-{scheme.Id}.png") != Error.Ok)
                    throw new InvalidOperationException("Could not save identity scheme preview.");
                content.Free();
            }
            GD.Print($"PASS: {plans.Length} complete identity schemes in both themes and selection states: {ProjectSettings.GlobalizePath(Output)}");
            GetTree().Quit();
        }
        catch (Exception error) { GD.PushError(error.ToString()); GetTree().Quit(1); }
    }

    private static LabPalette MakePalette(Scheme scheme, bool light)
    {
        if (scheme.Id == "current") return LabPalette.For(light);
        var palette = (LabPalette)LabPalette.For(light).Duplicate();
        string[] colors = light ? scheme.Light : scheme.Dark;
        palette.Boid = new(colors[0]); palette.Selection = new(colors[1]); palette.Leader = new(colors[2]);
        palette.SelectedLeader = new(colors[3]); palette.LeaderText = new(colors[4]);
        return palette;
    }

    private static Label Text(string text, Color color, int size)
    {
        var label = new Label { Text = text, MouseFilter = Control.MouseFilterEnum.Ignore };
        label.AddThemeColorOverride("font_color", color); label.AddThemeFontSizeOverride("font_size", size);
        return label;
    }

    private static void Swatch(HBoxContainer row, string text, Color color, Color ink)
    {
        var group = new HBoxContainer(); group.AddThemeConstantOverride("separation", 4); row.AddChild(group);
        group.AddChild(new ColorRect { Color = color, CustomMinimumSize = new(14, 14),
            SizeFlagsVertical = Control.SizeFlags.ShrinkCenter, MouseFilter = Control.MouseFilterEnum.Ignore });
        group.AddChild(Text(text, ink, 14));
    }

    private async Task Frames()
    {
        for (int i = 0; i < 4; i++) await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
    }
}
