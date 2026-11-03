using System;
using System.Linq;
using System.Threading.Tasks;
using Godot;

namespace BoidsLab.Tests;

/// <summary>真实界面验证分类导航、草稿提交、固定操作栏与小窗口可达性。</summary>
public partial class UiLayoutChecks : Node
{
    private MainController _app = null!;
    private ControlPanel Panel => _app.GetNode<ControlPanel>("%ControlPanel");
    private ISimulationWorld World => (ISimulationWorld)_app.GetNode<Node>("%World").GetChild(0);
    private BoidConfig Config => World is BoidManager two ? two.Config : ((BoidManager3D)World).Config;
    private int _checks;
    private T Find<T>(string name) where T : Node => (T)Panel.FindChild(name, true, false);

    public override async void _Ready()
    {
        if (DisplayServer.GetName() != "headless") { GetTree().Quit(2); return; }
        try
        {
            _app = GD.Load<PackedScene>("res://scenes/main.tscn").Instantiate<MainController>();
            _app.World2D = (SimulationWorldDefinition)_app.World2D.Duplicate();
            _app.World3D = (SimulationWorldDefinition)_app.World3D.Duplicate();
            _app.World2D.ConfigurationPath = "res://.godot/validation/ui-layout-2d.cfg";
            _app.World3D.ConfigurationPath = "res://.godot/validation/ui-layout-3d.cfg";
            foreach (var definition in new[] { _app.World2D, _app.World3D })
                new BoidConfig { BoidCount = 4 }.Save(definition.ConfigurationPath);
            AddChild(_app);
            await Frame();
            GetViewport().NotifyMouseEntered();
            for (int mode = 0; mode < 2; mode++)
            {
                if (mode != _app.CurrentWorldIndex) { _app.RequestWorldSwitch(mode); await Frame(); }
                Check(Panel.CurrentCategory == "Basics", "World entry opens the basic settings");
                Check((Panel.FindChild("WorldWidth", true, false) is not null) == (mode == 1), "3D-only settings are hidden in 2D");
                await CheckLayout();
                await CheckEditingAndInput();
                await CheckContext();
            }
            var limited = new ControlPanel();
            AddChild(limited);
            limited.Bind(new BoidConfig(), "像素", SimulationFeatures.None);
            Check(limited.FindChild("CategoryBasics", true, false) is Button
                && limited.FindChild("CategoryLeader", true, false) is null
                && limited.FindChild("CategoryFlocking", true, false) is null, "Unavailable capabilities do not produce empty categories");
            limited.Free();
            GD.Print($"PASS: {_checks} categorized UI/layout/focus/accessibility assertions (headless).");
            _app.Free();
            GetTree().Quit();
        }
        catch (Exception error) { GD.PushError(error.ToString()); GetTree().Quit(1); }
    }

    private async Task CheckLayout()
    {
        foreach (Vector2I size in new[] { new Vector2I(960, 640), new Vector2I(1360, 860), new Vector2I(1920, 1080) })
        {
            GetWindow().Size = size;
            await Frame();
            Rect2 viewport = GetViewport().GetVisibleRect();
            Check(viewport.Encloses(Panel.GetGlobalRect()), "Settings card stays within the viewport");
            Check(viewport.Encloses(_app.GetNode<Control>("%StatsPanel").GetGlobalRect()), "Statistics stay within the viewport");
            Rect2 toolbar = Find<Button>("StartPause").GetGlobalRect();
            foreach (var (category, field) in new[] {
                ("Basics", "MaxAngularAcceleration"), ("Flocking", "CohesionWeight"),
                ("Leader", "LeaderFollowWeight"), ("Obstacles", "ObstacleClearance"), ("Display", "ShowVelocity") })
            {
                Button tab = Find<Button>("Category" + category);
                Check(viewport.Encloses(tab.GetGlobalRect()), "Every category button is accessible");
                Click(tab.GetGlobalRect().GetCenter());
                await Frame();
                var page = Find<ScrollContainer>(category + "Page");
                Check(Panel.CurrentCategory == category && tab.ButtonPressed && page.IsVisibleInTree(), "Click activates the intended category");
                Check(Panel.GetChildren().OfType<ScrollContainer>().Count(x => x.IsVisibleInTree()) == 1, "Only one category is visible");
                var target = Find<Control>(field);
                page.EnsureControlVisible(target);
                await Frame();
                Rect2 visible = page.GetGlobalRect();
                Check(visible.HasPoint(target.GetGlobalRect().GetCenter()), $"Last setting remains reachable at {size}: {category}");
                Check(target.GetGlobalRect().End.X <= visible.End.X + 1, "Settings do not require horizontal scrolling");
                Check(Find<Button>("StartPause").GetGlobalRect() == toolbar, "Actions remain fixed while switching and scrolling");
            }
            Panel.SelectCategory("Basics");
            await Frame();
        }
        GetWindow().Size = new Vector2I(960, 640);
        await Frame();
    }

    private async Task CheckEditingAndInput()
    {
        Panel.SelectCategory("Basics");
        var count = Find<SpinBox>("BoidCount");
        Panel.RevealControl(count);
        await Frame();
        int submissions = 0;
        void OnSubmit(int value) => submissions++;
        Panel.PopulationCountRequested += OnSubmit;
        count.GetLineEdit().GrabFocus();
        count.GetLineEdit().Text = "7";
        Click(Find<Button>("CategoryFlocking").GetGlobalRect().GetCenter());
        await Frame();
        Check(Config.BoidCount == 7 && World.Stats.BoidCount == 7 && submissions == 1, "Category change commits population draft exactly once");
        Check(GetViewport().GuiGetFocusOwner() is not LineEdit, "Hidden page retains no text focus");
        Panel.PopulationCountRequested -= OnSubmit;
        for (int i = 0; i < 16; i++)
        {
            Tap(Key.Tab);
            Control? focus = GetViewport().GuiGetFocusOwner();
            Check(focus is null || focus.IsVisibleInTree(), "Keyboard traversal never focuses hidden settings");
        }
        Panel.SelectCategory("Basics");
        var speed = Find<SpinBox>("MaxSpeed");
        Panel.RevealControl(speed);
        await Frame();
        speed.GetLineEdit().GrabFocus();
        speed.GetLineEdit().Text = "345";
        Panel.SelectCategory("Display");
        Check(Config.MaxSpeed == 345, "Programmatic category navigation commits a numeric draft");
        var saved = new BoidConfig();
        Check(saved.Load(_app.ConfigurationPath) == Error.Ok && saved.MaxSpeed == 345 && saved.BoidCount == 7, "Category navigation saves the committed settings");
        await Frame();
        var display = Find<ScrollContainer>("DisplayPage");
        display.EnsureControlVisible(Find<Control>("ShowVelocity"));
        await Frame();
        int scroll = display.ScrollVertical;
        Click(Find<Button>("CategoryLeader").GetGlobalRect().GetCenter());
        await Frame();
        Click(Find<Button>("CategoryDisplay").GetGlobalRect().GetCenter());
        await Frame();
        Check(display.ScrollVertical == scroll, "Each category remembers its scroll position");
        string category = Panel.CurrentCategory;
        var help = _app.GetNode<HelpOverlay>("%HelpOverlay");
        help.Open();
        await Frame();
        Click(Find<Button>("CategoryBasics").GetGlobalRect().GetCenter());
        Check(Panel.CurrentCategory == category, "Help prevents background category navigation");
        help.Close();
        Tap(Key.F1);
        Tap(Key.F1);
        Check(Panel.CurrentCategory == category, "Hiding and restoring the HUD preserves the selected category");
        Tap(Key.Space);
        Check(World.IsRunning && Find<Button>("Step").Disabled, "Global pause shortcut remains effective on every category");
        Tap(Key.Space);
        Click(Find<Button>("Step").GetGlobalRect().GetCenter());
        Check(!World.IsRunning && World.Stats.UpdateMilliseconds.HasValue, "Persistent step action works while another category is active");
        await Frame();
    }

    private async Task CheckContext()
    {
        Panel.SelectCategory("Obstacles");
        await Frame();
        var width = Find<SpinBox>("ObstacleWidth");
        Check(!width.IsVisibleInTree(), "No inactive obstacle dimensions without a selection");
        if (World is BoidManager two)
            two.GetNode<ObstacleEditor2D>("ObstacleEditor").AddObstacle(ObstacleShape.Rectangle, two.ViewBounds.GetCenter());
        else
            ((BoidManager3D)World).GetNode<ObstacleEditor3D>("ObstacleEditor").AddObstacle(ObstacleShape.Box, Vector3.Zero);
        await Frame();
        Check(width.IsVisibleInTree() && width.Editable, "Selecting an obstacle reveals editable dimensions");
        Check(_app.GetNode<Label>("%WorldHint").Text.Contains("障碍页")
            && Find<Label>("SelectedBoidStatus").Text.Contains("障碍物"), "Selection hints describe the selected obstacle rather than asking to select a boid");
        World.BeginObstaclePlacement(World is BoidManager ? ObstacleShape.Circle : ObstacleShape.Sphere);
        await Frame();
        Check(!width.IsVisibleInTree() && Find<Button>("CancelObstaclePlacement").IsVisibleInTree(), "Placement shows cancellation rather than inactive size editors");
        Check(_app.GetNode<Label>("%WorldHint").Text.Contains("取消"), "Placement replaces camera hints with placement guidance");
        World.CancelObstaclePlacement();
        Panel.SelectCategory("Flocking");
        Find<SpinBox>("SeparationRadius").Value = 150;
        Find<SpinBox>("PerceptionRadius").Value = 80;
        Check(Find<Label>("SeparationRadiusWarning").IsVisibleInTree(), "Related radii and warning are available together");
        Find<SpinBox>("PerceptionRadius").Value = 150;
        Check(!Find<Label>("SeparationRadiusWarning").Visible, "Warning clears when the radii match");
        if (World is BoidManager twoD) twoD.SetLeader(twoD.Boids[0]);
        else ((BoidManager3D)World).SetLeader(((BoidManager3D)World).Boids[0]);
        Check(_app.GetNode<Label>("%WorldTitle").Text.Contains("领队模式"), "Scene title reflects the leader mode");
        if (World is BoidManager3D threeD)
        {
            Check(_app.GetNode<Label>("%WorldHint").Text.Contains("F3"), "Released following offers the capture shortcut");
            threeD.SetLeaderControlActive(true);
            Check(_app.GetNode<Label>("%WorldHint").Text.Contains("WASD"), "Captured following offers flight guidance");
            threeD.SetLeaderControlActive(false);
            Check(_app.GetNode<Label>("%WorldHint").Text.Contains("F3"), "Release restores follow-view guidance immediately");
            threeD.SetLeader(null);
        }
        else ((BoidManager)World).SetLeader(null);
        Check(_app.GetNode<Label>("%WorldTitle").Text.Contains("观察模式"), "Removing the leader restores observation guidance");
        var count = Find<SpinBox>("BoidCount");
        count.GetLineEdit().Text = "0";
        count.GetLineEdit().EmitSignal(LineEdit.SignalName.TextSubmitted, "0");
        Check(_app.GetNode<Label>("%WorldHint").Text.Contains("暂无个体"), "An empty world offers population guidance");
    }

    private void Tap(Key key)
    {
        foreach (bool pressed in new[] { true, false })
            GetViewport().PushInput(new InputEventKey { Keycode = key, PhysicalKeycode = key, Pressed = pressed }, true);
    }
    private void Click(Vector2 point)
    {
        GetViewport().PushInput(new InputEventMouseMotion { Position = point, GlobalPosition = point }, true);
        foreach (bool pressed in new[] { true, false })
            GetViewport().PushInput(new InputEventMouseButton { Position = point, GlobalPosition = point, ButtonIndex = MouseButton.Left, Pressed = pressed }, true);
    }
    private async Task Frame()
    {
        for (int i = 0; i < 4; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }
    private void Check(bool condition, string message)
    {
        _checks++;
        if (!condition) throw new InvalidOperationException(message);
    }
}
