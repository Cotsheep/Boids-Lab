using System;
using System.Linq;
using System.Threading.Tasks;
using Godot;

namespace BoidsLab.Tests;

/// <summary>通过真实主场景检查单步、GUI 输入隔离以及切换后的订阅生命周期。</summary>
public partial class SingleStepChecks : Node
{
    private int _checks;
    private MainController _app = null!;
    private ControlPanel _panel = null!;
    private ISimulationWorld World => (ISimulationWorld)_app.GetNode<Node>("%World").GetChild(0);
    private Button StepButton => (Button)_panel.FindChild("Step", true, false);
    private BoidConfig Config => World is BoidManager two ? two.Config : ((BoidManager3D)World).Config;

    public override async void _Ready()
    {
        if (DisplayServer.GetName() != "headless") { GetTree().Quit(2); return; }
        int originalFrequency = Engine.PhysicsTicksPerSecond;
        try
        {
            _app = GD.Load<PackedScene>("res://scenes/main.tscn").Instantiate<MainController>();
            _app.World2D = (SimulationWorldDefinition)_app.World2D.Duplicate();
            _app.World3D = (SimulationWorldDefinition)_app.World3D.Duplicate();
            _app.World2D.ConfigurationPath = "res://.godot/validation/step-2d.cfg";
            _app.World3D.ConfigurationPath = "res://.godot/validation/step-3d.cfg";
            foreach (var definition in new[] { _app.World2D, _app.World3D })
                Check(new BoidConfig { BoidCount = 6, RandomSeed = 431, ShowVelocity = true }.Save(definition.ConfigurationPath) == Error.Ok,
                    "Isolated single-step configuration");
            AddChild(_app);
            _panel = _app.GetNode<ControlPanel>("%ControlPanel");
            await Frame();
            foreach (int mode in new[] { 0, 1, 0, 1 })
            {
                if (_app.CurrentWorldIndex != mode)
                {
                    object[] before = Snapshot();
                    _app.RequestWorldSwitch(mode);
                    StepButton.EmitSignal(Button.SignalName.Pressed);
                    Check(Snapshot().SequenceEqual(before), "Pending world switch rejects a queued old-world step");
                    await Frame();
                }
                Check(_app.CurrentWorldIndex == mode && !World.IsRunning && World.CanStep && !StepButton.Disabled,
                    "Current world starts paused with single-step available");
                await CheckWorld();
            }
            GD.Print($"PASS: {_checks} single-step timing/UI/leader/lifecycle assertions (headless).");
            _app.Free();
            GetTree().Quit();
        }
        catch (Exception error) { GD.PushError(error.ToString()); GetTree().Quit(1); }
        finally { Engine.PhysicsTicksPerSecond = originalFrequency; }
    }

    private async Task CheckWorld()
    {
        Config.BoidCount = 6;
        foreach (var search in new[] { NeighborSearchMode.Naive, NeighborSearchMode.UniformGrid })
            foreach (int frequency in new[] { 60, 120 })
            {
                Engine.PhysicsTicksPerSecond = frequency;
                Config.NeighborSearchMode = search;
                Reset();
                DirectStep();
                object[] expected = Snapshot();
                long? expectedChecks = World.Stats.NeighborCandidateChecks;
                Reset();
                ulong[] ids = Instances();
                object[] before = Snapshot();
                int seed = Config.RandomSeed;
                Click(StepButton.GetGlobalRect().GetCenter());
                Check(Snapshot().SequenceEqual(expected) && !Snapshot().SequenceEqual(before),
                    $"{World.DisplayName}: click equals one direct step at {frequency} Hz with {search}");
                Check(!World.IsRunning && World.CanStep && Instances().SequenceEqual(ids) && Config.RandomSeed == seed,
                    "Step preserves pause, instances and seed");
                Check(World.Stats.UpdateMilliseconds is >= 0 && World.Stats.NeighborCandidateChecks == expectedChecks
                    && ((Label)_app.GetNode<StatsPanel>("%StatsPanel").FindChild("UpdateTime", true, false)).Text != "--",
                    "One step immediately measures and displays current statistics");
                await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
                await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
                await Frame();
                Check(Snapshot().SequenceEqual(expected), "Following physics/display frames do not accumulate extra movement");
                CheckDebugPositions();
            }

        // 单个匀速个体给出独立于 Step 实现的时间步参照。
        Config.BoidCount = 0;
        Reset();
        if (World is BoidManager two)
        {
            Vector2 p = two.ViewBounds.GetCenter();
            var boid = two.SpawnBoid(p, Vector2.Right * 60);
            World.StepOnce();
            Check(boid.Position.IsEqualApprox(p + Vector2.Right * 0.5f), "120 Hz step advances a 60-unit/s 2D boid by 0.5 units");
        }
        else
        {
            var three = (BoidManager3D)World;
            Vector3 p = three.WorldBounds.GetCenter();
            var boid = three.SpawnBoid(p, Vector3.Right * 60);
            World.StepOnce();
            Check(boid.Position.IsEqualApprox(p + Vector3.Right * 0.5f), "120 Hz step advances a 60-unit/s 3D boid by 0.5 units");
        }

        object[] state = Snapshot();
        StepButton.GrabFocus();
        KeyEvent(Key.Space, true);
        KeyEvent(Key.Space, true, true);
        KeyEvent(Key.Space, false);
        Check(World.IsRunning && !World.CanStep && StepButton.Disabled && Snapshot().SequenceEqual(state),
            "Space with step focus only resumes; it does not also step");
        World.StepOnce();
        Click(StepButton.GetGlobalRect().GetCenter());
        StepButton.EmitSignal(Button.SignalName.Pressed);
        Check(Snapshot().SequenceEqual(state), "Running worlds reject direct, disabled-button and queued single-step requests");
        Tap(Key.Space);
        Check(!World.IsRunning && !StepButton.Disabled, "Pause makes single-step available again");

        var help = _app.GetNode<HelpOverlay>("%HelpOverlay");
        help.Open();
        StepButton.EmitSignal(Button.SignalName.Pressed);
        Click(StepButton.GetGlobalRect().GetCenter());
        Check(Snapshot().SequenceEqual(state), "Help blocks background single-step without click-through");
        help.Close();

        var speed = (SpinBox)_panel.FindChild("MaxSpeed", true, false);
        speed.GetLineEdit().GrabFocus();
        await Frame(); // 让 ScrollContainer 完成数字输入框的焦点滚动。
        Tap(Key.Enter);
        Check(Snapshot().SequenceEqual(state), "Enter in a numeric editor does not activate single-step");
        StepButton.GrabFocus();
        _panel.RevealControl(StepButton);
        await Frame();
        Tap(Key.Enter);
        Check(!Snapshot().SequenceEqual(state) && !World.IsRunning, "Enter on the step button advances once and remains paused");

        await CheckLeader();
        Config.BoidCount = 0;
        Reset();
        Click(StepButton.GetGlobalRect().GetCenter());
        Check(Snapshot().Length == 0 && !World.IsRunning && World.Stats.BoidCount == 0
            && World.Stats.NeighborCandidateChecks == 0, "Empty worlds can step and report zero counts");
        Reset();
        Check(!World.IsRunning && !StepButton.Disabled && World.Stats.UpdateMilliseconds is null,
            "Reset preserves step availability and clears old measurements");
    }

    private async Task CheckLeader()
    {
        Config.BoidCount = 0;
        Reset();
        if (World is BoidManager two)
        {
            Vector2 p = two.ViewBounds.GetCenter();
            var leader = two.SpawnBoid(p, Vector2.Right * 60);
            two.SpawnBoid(p + new Vector2(-100, 30), Vector2.Right * 30);
            var editor = two.GetNode<ObstacleEditor2D>("ObstacleEditor");
            var obstacle = editor.AddObstacle(ObstacleShape.Circle, p + Vector2.Up * 150);
            two.SetLeader(leader);
            Click(leader.GetGlobalTransformWithCanvas() * Vector2.Zero);
            Check(two.SelectedBoid == leader, "2D leader is selected before stepping");
            two.SetLeaderTarget(p + Vector2.Right * 200);
            Click(StepButton.GetGlobalRect().GetCenter());
            Check(two.Leader == leader && two.SelectedBoid == leader && leader.Position != p
                && editor.Obstacles.Contains(obstacle), "2D step preserves identity/selection/obstacles while following the current target");
        }
        else
        {
            var three = (BoidManager3D)World;
            Vector3 p = three.WorldBounds.GetCenter();
            var leader = three.SpawnBoid(p, Vector3.Right * 60);
            three.SpawnBoid(p + new Vector3(-100, 30, 0), Vector3.Right * 30);
            var editor = three.GetNode<ObstacleEditor3D>("ObstacleEditor");
            var obstacle = editor.AddObstacle(ObstacleShape.Sphere, p + Vector3.Up * 150);
            three.SetLeader(leader);
            three.SetLeaderControlActive(true);
            three.LeaderController._Input(new InputEventAction { Action = "leader_forward", Pressed = true });
            object[] captured = Snapshot();
            StepButton.EmitSignal(Button.SignalName.Pressed);
            Check(Snapshot().SequenceEqual(captured), "UI step requires releasing leader control first");
            Tap(Key.Escape);
            var camera = three.GetNode<SimulationCamera3D>("SimulationView/Camera3D");
            Click(camera.UnprojectPosition(leader.GlobalPosition));
            Check(three.SelectedBoid == leader, "3D leader is selected before stepping");
            Click(StepButton.GetGlobalRect().GetCenter());
            Check(three.Leader == leader && three.SelectedBoid == leader && leader.Position != p
                && leader.Velocity.Length() < 60 && three.LeaderController.MovementDirection == Vector3.Zero
                && editor.Obstacles.Contains(obstacle), "Released 3D leader brakes for one step, preserving identity/selection/obstacles");
            object[] paused = Snapshot();
            three.LeaderController.RotateView(new Vector2(30, 10));
            Transform3D oldView = camera.Transform;
            await Frame();
            Check(camera.Transform != oldView && Snapshot().SequenceEqual(paused), "Camera continues following while simulation stays paused after a step");
        }
    }

    private void CheckDebugPositions()
    {
        if (World is BoidManager two)
        {
            var lines = two.GetNode<SimulationView>("SimulationView").DebugGeometry.VelocityPoints;
            Check(lines.Count > 0 && two.Boids.Any(b => lines.Contains(b.Position)), "2D redraw reflects stepped motion");
        }
        else
        {
            var three = (BoidManager3D)World;
            var lines = three.GetNode<SimulationView3D>("SimulationView").DebugGeometry.VelocityPoints;
            Check(lines.Count > 0 && three.Boids.All(b => lines.Contains(b.Position)), "3D debug arrows reflect stepped motion");
        }
    }

    private object[] Snapshot() => World is BoidManager two
        ? two.Boids.Select(b => (object)(b.Id, b.Position, b.Velocity, b.Acceleration, b.Rotation, b.AngularVelocity, b.AngularAcceleration)).ToArray()
        : ((BoidManager3D)World).Boids.Select(b => (object)(b.Id, b.Position, b.Velocity, b.Acceleration, b.Quaternion, b.AngularVelocity, b.AngularAcceleration)).ToArray();
    private ulong[] Instances() => World is BoidManager two ? two.Boids.Select(b => b.GetInstanceId()).ToArray()
        : ((BoidManager3D)World).Boids.Select(b => b.GetInstanceId()).ToArray();
    private void DirectStep()
    {
        if (World is BoidManager two) two.Step(1.0 / Engine.PhysicsTicksPerSecond);
        else ((BoidManager3D)World).Step(1.0 / Engine.PhysicsTicksPerSecond);
    }
    private void Reset() => ((Button)_panel.FindChild("Reset", true, false)).EmitSignal(Button.SignalName.Pressed);
    private void Tap(Key key)
    {
        foreach (bool down in new[] { true, false })
            KeyEvent(key, down);
    }
    private void KeyEvent(Key key, bool down, bool echo = false) =>
        GetViewport().PushInput(new InputEventKey { Keycode = key, PhysicalKeycode = key, Pressed = down, Echo = echo }, true);
    private void Click(Vector2 point)
    {
        GetViewport().PushInput(new InputEventMouseMotion { Position = point, GlobalPosition = point }, true);
        foreach (bool down in new[] { true, false })
            GetViewport().PushInput(new InputEventMouseButton { Position = point, GlobalPosition = point,
                ButtonIndex = MouseButton.Left, Pressed = down }, true);
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
