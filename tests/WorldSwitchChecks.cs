using System;
using System.Linq;
using System.Threading.Tasks;
using Godot;

namespace BoidsLab.Tests;

public partial class WorldSwitchChecks : Node
{
    private int _checks;
    private MainController _app = null!;
    private ControlPanel _panel = null!;
    private OptionButton _selector = null!;
    private const string Path2D = "res://.godot/validation/switch-2d.cfg";
    private const string Path3D = "res://.godot/validation/switch-3d.cfg";

    public override async void _Ready()
    {
        if (DisplayServer.GetName() != "headless") { GetTree().Quit(2); return; }
        try
        {
            Check(new BoidConfig { BoidCount = 6, RandomSeed = 71, PerceptionRadius = 70,
                NeighborSearchMode = NeighborSearchMode.UniformGrid }.Save(Path2D) == Error.Ok, "Isolated 2D config");
            Check(new BoidConfig { BoidCount = 9, RandomSeed = 93, PerceptionRadius = 40 }.Save(Path3D) == Error.Ok, "Isolated 3D config");
            _app = GD.Load<PackedScene>("res://scenes/main.tscn").Instantiate<MainController>();
            _app.World2D = (SimulationWorldDefinition)_app.World2D.Duplicate();
            _app.World3D = (SimulationWorldDefinition)_app.World3D.Duplicate();
            _app.World2D.ConfigurationPath = Path2D;
            _app.World3D.ConfigurationPath = Path3D;
            AddChild(_app);
            await Frame();
            _panel = _app.GetNode<ControlPanel>("%ControlPanel");
            _selector = _app.GetNode<OptionButton>("%WorldSelector");
            var old2D = _app.GetNode<BoidManager>("World/BoidsWorld2D");
            Check(_app.CurrentWorldIndex == 0 && _selector.Selected == 0 && old2D.Boids.Count == 6, "2D entry and selector agree");
            Check(_selector.GetGlobalRect().End.X <= GetViewport().GetVisibleRect().Size.X, "Selector fits the desktop header");
            old2D.GetNode<ObstacleEditor2D>("ObstacleEditor").AddObstacle(ObstacleShape.Circle, new Vector2(200, 200));
            old2D.SetLeader(old2D.Boids[0]);
            old2D.SetRunning(true);
            old2D.Step(1.0 / 60);
            CountInput().GetLineEdit().GrabFocus();
            await Frame();
            CountInput().GetLineEdit().Text = "15"; // 未按 Enter 的数字，也必须随切换保存。
            _selector.Select(1);
            _selector.EmitSignal(OptionButton.SignalName.ItemSelected, 1L);
            Check(_app.IsSwitchingWorld && _selector.Disabled && GodotObject.IsInstanceValid(old2D), "Switch is deferred until signal dispatch completes");
            await Frame();
            var world3D = _app.GetNode<BoidManager3D>("World/BoidsWorld3D");
            Check(!GodotObject.IsInstanceValid(old2D), "Old 2D world is actually freed");
            Check(!world3D.IsRunning && world3D.Boids.Count == 9 && world3D.Config.PerceptionRadius == 40, "3D uses its own settings and starts paused");
            Check(_panel.Obstacles is null && _panel.FindChild("IsLeader", true, false) is null, "2D-only controls are removed");
            Check(GetViewport().GetCamera2D() is null && GetViewport().GetCamera3D() is not null, "Only the new camera remains active");
            Check(HelpText().Contains("三维基础运动") && world3D.Stats.UpdateMilliseconds is null, "Help and fresh statistics match the new world");
            Check(_app.GetNode<StatsPanel>("%StatsPanel").FindChild("NeighborChecks", true, false) is Label { Text: "--" }, "Old measurements do not leak into the new HUD");
            var saved2D = new BoidConfig();
            Check(saved2D.Load(Path2D) == Error.Ok && saved2D.BoidCount == 15 && saved2D.NeighborSearchMode == NeighborSearchMode.UniformGrid,
                "Pending numeric input is committed without changing the 2D search mode");
            CountInput().Value = 12;
            ((SpinBox)_panel.FindChild("PerceptionRadius", true, false)).Value = 55;
            ((CheckBox)_panel.FindChild("ShowVelocity", true, false)).ButtonPressed = true;
            world3D.SetRunning(true);
            world3D.Step(0.5);
            var help = _app.GetNode<HelpOverlay>("%HelpOverlay");
            KeyAction("toggle_ui");
            help.Open();
            await Switch(0);
            Check(!help.Visible && !_app.GetNode<Control>("%UIRoot").Visible, "Switch closes old help and preserves HUD visibility");
            Check(!GodotObject.IsInstanceValid(world3D), "Old 3D nodes and environment are freed");
            var world2D = _app.GetNode<BoidManager>("World/BoidsWorld2D");
            Check(!world2D.IsRunning && world2D.Boids.Count == 15 && world2D.Config.PerceptionRadius == 70,
                "Returning to 2D rebuilds from the preserved 2D parameters");
            Check(world2D.Leader is null && world2D.SelectedBoid is null
                && world2D.GetNode<ObstacleEditor2D>("ObstacleEditor").Obstacles.Count == 0, "Leader, selection and obstacle layout are discarded");
            Check(_panel.Obstacles is not null && _panel.FindChild("IsLeader", true, false) is CheckBox, "2D controls are restored");
            Check(GetViewport().GetCamera3D() is null && GetViewport().GetCamera2D() is not null, "3D camera no longer affects the viewport");
            Check(!HelpText().Contains("三维基础运动"), "2D instructions are restored");
            Vector2[] resetPositions = world2D.Boids.Select(b => b.Position).ToArray();
            await Switch(1);
            world3D = _app.GetNode<BoidManager3D>("World/BoidsWorld3D");
            Check(world3D.Boids.Count == 12 && world3D.Config.PerceptionRadius == 55 && world3D.Config.ShowVelocity,
                "Returning to 3D preserves its count, radius and visualization options");
            Vector3[] positions3D = world3D.Boids.Select(b => b.Position).ToArray();
            KeyAction("toggle_ui");
            for (int i = 0; i < 6; i++)
            {
                int target = i % 2;
                await Switch(target);
                Check(_app.GetNode<Node>("%World").GetChildCount() == 1, "Exactly one world is attached after repeated switches");
                Check(_panel.FindChildren("StartPause", "Button", true, false).Count == 1, "Controls do not accumulate");
                ISimulationWorld world = (ISimulationWorld)_app.GetNode<Node>("%World").GetChild(0);
                Check(!world.IsRunning && world.Stats.UpdateMilliseconds is null, "Every switch resets runtime state and timing");
                ((Button)_panel.FindChild("StartPause", true, false)).EmitSignal(Button.SignalName.Pressed);
                Check(world.IsRunning, "Single button activation has exactly one subscribed handler");
                KeyAction("toggle_pause");
                Check(!world.IsRunning, "Global pause targets only the current world");
                if (world is BoidManager twoD)
                    Check(twoD.Boids.Select(b => b.Position).SequenceEqual(resetPositions), "2D regenerates deterministic positions");
                else
                    Check(((BoidManager3D)world).Boids.Select(b => b.Position).SequenceEqual(positions3D), "3D regenerates deterministic positions");
                GetWindow().EmitSignal(Window.SignalName.FocusExited); // 不得调用已释放世界中的旧订阅。
                GetWindow().EmitSignal(Window.SignalName.FocusEntered);
            }
            Node retained = _app.GetNode<Node>("%World").GetChild(0);
            _app.RequestWorldSwitch(_app.CurrentWorldIndex);
            await Frame();
            Check(_app.GetNode<Node>("%World").GetChild(0) == retained, "Choosing the current mode is a no-op");
            var saved3D = new BoidConfig();
            Check(saved3D.Load(Path3D) == Error.Ok && saved3D.BoidCount == 12 && saved3D.PerceptionRadius == 55, "Both modes persist to distinct files");
            GD.Print($"PASS: {_checks} runtime world switching/configuration/lifecycle assertions (headless).");
            GetTree().Quit();
        }
        catch (Exception error) { GD.PushError(error.ToString()); GetTree().Quit(1); }
    }

    private SpinBox CountInput() => (SpinBox)_panel.FindChild("BoidCount", true, false);
    private string HelpText() => _app.GetNode<HelpOverlay>("%HelpOverlay").GetNode<RichTextLabel>("Card/Column/Instructions").Text;
    private void KeyAction(string action) => GetViewport().PushInput(new InputEventAction { Action = action, Pressed = true }, true);
    private async Task Switch(int index)
    {
        _app.RequestWorldSwitch(index);
        await Frame();
        Check(_app.CurrentWorldIndex == index && _selector.Selected == index && !_app.IsSwitchingWorld, "World and selector synchronize after switching");
    }
    private async Task Frame()
    {
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }
    private void Check(bool condition, string message)
    {
        _checks++;
        if (!condition) throw new InvalidOperationException(message);
    }
}
