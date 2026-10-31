using System;
using System.Linq;
using System.Threading.Tasks;
using Godot;

namespace BoidsLab.Tests;

/// <summary>真实主场景中的领队勾选、鼠标目标与其他 UI/编辑工具隔离。</summary>
public partial class LeaderInteractionChecks : Node
{
    private int _checks;

    public override async void _Ready()
    {
        if (DisplayServer.GetName() != "headless")
        {
            GD.PushError("Run leader_interaction_checks.tscn with --headless.");
            GetTree().Quit(2);
            return;
        }
        try
        {
            var game = GD.Load<PackedScene>("res://scenes/main.tscn").Instantiate<MainController>();
            AddChild(game);
            await Frame();
            GetViewport().NotifyMouseEntered();
            var world = game.GetNode<BoidManager>("World/BoidsWorld2D");
            var camera = world.GetNode<SimulationCamera2D>("Camera2D");
            var editor = world.GetNode<ObstacleEditor2D>("ObstacleEditor");
            var panel = game.GetNode<ControlPanel>("%ControlPanel");
            panel.SelectCategory("Leader");
            await Frame();
            var help = game.GetNode<HelpOverlay>("%HelpOverlay");
            var hud = game.GetNode<Control>("%UIRoot");
            var checkbox = (CheckBox)panel.FindChild("IsLeader", true, false);
            var status = (Label)panel.FindChild("LeaderStatus", true, false);
            var reset = (Button)panel.FindChild("Reset", true, false);
            int settingsChanges = 0;
            panel.ConfigurationChanged += () => settingsChanges++;
            foreach (Boid2D boid in world.Boids.ToArray()) world.RemoveBoid(boid);
            Vector2 size = GetViewport().GetVisibleRect().Size;
            Vector2 aPoint = size * new Vector2(0.67f, 0.43f);
            Vector2 bPoint = size * new Vector2(0.83f, 0.60f);
            Vector2 targetPoint = size * new Vector2(0.9f, 0.38f);
            var a = world.SpawnBoid(ToWorld(world, aPoint), Vector2.Zero);
            var b = world.SpawnBoid(ToWorld(world, bPoint), Vector2.Zero);
            Color original = a.GetNode<Polygon2D>("Body").Color;
            Check(checkbox.Disabled && !checkbox.ButtonPressed && world.Leader is null, "No selection disables Leader assignment");
            Pointer(targetPoint);
            await Frame();
            Check(world.LeaderTarget is null, "Moving cursor without Leader does not create a target");
            Click(Screen(a));
            Check(!checkbox.Disabled && !checkbox.ButtonPressed, "Selecting normal Boid enables unchecked assignment");
            Click(checkbox.GetGlobalRect().GetCenter());
            Check(world.Leader == a && a.IsLeader && checkbox.ButtonPressed && !world.IsRunning,
                "Native checkbox assigns selected Boid without resuming");
            Check(a.GetNode<Polygon2D>("Body").Color == Boid2D.LeaderColor,
                "Leader uses special color even while selected");
            Click(Screen(a));
            Check(world.SelectedBoid is null && world.Leader == a && a.IsLeader && checkbox.Disabled,
                "Deselecting does not clear Leader identity");
            Click(Screen(b));
            Check(!checkbox.ButtonPressed && !checkbox.Disabled && world.Leader == a,
                "Selecting another Boid does not transfer Leader implicitly");
            Click(checkbox.GetGlobalRect().GetCenter());
            Check(world.Leader == b && b.IsLeader && !a.IsLeader && world.Boids.Count(x => x.IsLeader) == 1,
                "Checking another Boid transfers the unique Leader");
            Check(a.GetNode<Polygon2D>("Body").Color == original, "Former Leader restores original unselected color");
            Pointer(targetPoint);
            await Frame();
            Check(world.LeaderTarget.HasValue && world.LeaderTarget.Value.DistanceTo(ToWorld(world, targetPoint)) < 0.01f
                && status.Text.Contains("跟随鼠标"), "Cursor motion alone sets world-space goal and status while paused");
            Vector2 target = world.LeaderTarget!.Value;
            Vector2 before = b.Position;
            await Frame();
            Check(b.Position == before && !world.IsRunning, "Paused goal changes do not move Leader");
            Click(reset.GetGlobalRect().GetCenter(), MouseButton.Right);
            await Frame();
            Check(world.LeaderTarget == target && world.Leader == b, "Right click on HUD neither retargets nor resets");

            checkbox.GrabFocus();
            Tap(Key.Space);
            Check(world.IsRunning && checkbox.ButtonPressed && world.Leader == b, "Space with Leader checkbox focus only resumes");
            KeyEvent(Key.Space, true, true);
            Check(world.IsRunning, "Repeated Space does not toggle twice");
            Tap(Key.Space);
            Check(!world.IsRunning && world.Leader == b, "Space pauses without changing Leader");

            Tap(Key.F2);
            Click(size * new Vector2(0.94f, 0.5f), MouseButton.Right);
            Click(checkbox.GetGlobalRect().GetCenter());
            Check(world.Leader == b && world.LeaderTarget == target,
                "Help blocks retargeting and covered Leader checkbox");
            Tap(Key.Escape);
            Check(!help.Visible, "Help closes normally");
            world.BeginObstaclePlacement(ObstacleShape.Circle);
            Pointer(targetPoint + new Vector2(20, 0));
            await Frame();
            Check(world.LeaderTarget == target, "Obstacle placement does not steer Leader with the preview cursor");
            Click(targetPoint + new Vector2(20, 0), MouseButton.Right);
            Check(editor.PlacementShape is null && world.LeaderTarget == target && world.Leader == b,
                "Right click cancels obstacle placement instead of retargeting");
            var obstacle = editor.AddObstacle(ObstacleShape.Circle, ToWorld(world, size * new Vector2(0.55f, 0.7f)));
            Check(world.SelectedBoid is null && world.Leader == b && checkbox.Disabled && b.IsLeader,
                "Selecting obstacle preserves Leader and disables per-Boid checkbox");
            editor.DeleteSelected();

            // 视角改变后，即使光标不动，也持续按最终相机变换读取目标。
            Pointer(targetPoint);
            await Frame();
            Wheel(targetPoint);
            await Frame();
            Check(world.LeaderTarget!.Value.DistanceTo(ToWorld(world, targetPoint)) < 0.01f,
                "Cursor target accounts for camera zoom");
            Vector2 cameraBefore = camera.Position;
            Pointer(size * new Vector2(0.93f, 0.72f));
            Button(size * new Vector2(0.93f, 0.72f), MouseButton.Left, true);
            Pointer(size * new Vector2(0.93f, 0.72f) + new Vector2(30, 20), new Vector2(30, 20), true);
            Button(size * new Vector2(0.93f, 0.72f) + new Vector2(30, 20), MouseButton.Left, false);
            await Frame();
            Vector2 panPointer = size * new Vector2(0.93f, 0.72f) + new Vector2(30, 20);
            Check(camera.Position != cameraBefore && world.Leader == b
                && world.LeaderTarget!.Value.DistanceTo(ToWorld(world, panPointer)) < 0.01f,
                "Camera pan preserves role and tracks cursor in world coordinates");
            Vector2 anchored = b.Position;
            camera.GlobalPosition += new Vector2(10000, -10000);
            camera.ForceUpdateScroll();
            await Frame();
            Check(world.LeaderTarget!.Value.DistanceTo(ToWorld(world, panPointer)) < 0.01f && b.Position == anchored,
                "Stationary cursor retargets after camera movement without relocating paused Leader");
            Tap(Key.Home);
            await Frame();
            Tap(Key.F1);
            Check(!hud.Visible, "F1 hides HUD");
            Pointer(targetPoint);
            await Frame();
            Check(world.LeaderTarget!.Value.DistanceTo(ToWorld(world, targetPoint)) < 0.01f && b.IsLeader,
                "Hidden HUD retains Leader and world target controls");
            world.SetRunning(true);
            Pointer(targetPoint + new Vector2(-60, 40));
            await Frame();
            Check(world.LeaderTarget!.Value.DistanceTo(ToWorld(world, targetPoint + new Vector2(-60, 40))) < 0.01f,
                "Running Leader follows cursor motion without any mouse button");
            world.SetRunning(false);
            Vector2 lastTarget = world.LeaderTarget.Value;
            GetWindow().EmitSignal(Window.SignalName.FocusExited);
            Pointer(targetPoint);
            await Frame();
            Check(world.LeaderTarget == lastTarget, "Focus loss freezes the last target");
            GetWindow().EmitSignal(Window.SignalName.FocusEntered);
            await Frame();
            Check(world.LeaderTarget!.Value.DistanceTo(ToWorld(world, targetPoint)) < 0.01f, "Regaining focus resumes cursor sampling");
            Tap(Key.F1);
            await Frame();
            Click(Screen(b));
            Check(checkbox.ButtonPressed, "Reselecting current Leader shows checked state");
            Click(checkbox.GetGlobalRect().GetCenter());
            Check(world.Leader is null && world.LeaderTarget is null && !b.IsLeader && world.SelectedBoid == b,
                "Unchecking removes role and target while retaining normal selection");
            Check(b.GetNode<Polygon2D>("Body").Color != Boid2D.LeaderColor && checkbox.ButtonPressed == false,
                "Former selected Leader returns to ordinary selection appearance");
            Click(checkbox.GetGlobalRect().GetCenter());
            Pointer(targetPoint);
            await Frame();
            world.RemoveBoid(b);
            Check(world.Leader is null && world.LeaderTarget is null && checkbox.Disabled,
                "Deleting Leader clears UI, selection and goal without dangling references");

            Click(Screen(a));
            Click(checkbox.GetGlobalRect().GetCenter());
            Pointer(targetPoint);
            await Frame();
            Click(reset.GetGlobalRect().GetCenter());
            Check(world.Leader is null && world.LeaderTarget is null && !checkbox.ButtonPressed && checkbox.Disabled
                && world.Boids.All(x => !x.IsLeader), "Reset clears identity even when new Boids reuse IDs");
            Check(settingsChanges == 0, "Selection, identity and target never save simulation settings");
            GD.Print($"PASS: {_checks} leader selection/target/input assertions (headless).");
            GetTree().Quit();
        }
        catch (Exception error)
        {
            GD.PushError(error.ToString());
            GetTree().Quit(1);
        }
    }

    private static Vector2 Screen(Boid2D boid) => boid.GetGlobalTransformWithCanvas() * Vector2.Zero;
    private static Vector2 ToWorld(BoidManager world, Vector2 position) => world.GetGlobalTransformWithCanvas().AffineInverse() * position;
    private async Task Frame()
    {
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }
    private void Tap(Key key) { KeyEvent(key, true); KeyEvent(key, false); }
    private void KeyEvent(Key key, bool pressed, bool echo = false) =>
        GetViewport().PushInput(new InputEventKey { Keycode = key, PhysicalKeycode = key, Pressed = pressed, Echo = echo }, true);
    private void Pointer(Vector2 position, Vector2 relative = default, bool held = false) =>
        GetViewport().PushInput(new InputEventMouseMotion { Position = position, GlobalPosition = position,
            Relative = relative, ButtonMask = held ? MouseButtonMask.Left : 0 }, true);
    private void Button(Vector2 position, MouseButton button, bool pressed) =>
        GetViewport().PushInput(new InputEventMouseButton { Position = position, GlobalPosition = position,
            ButtonIndex = button, Pressed = pressed,
            ButtonMask = !pressed ? 0 : button == MouseButton.Left ? MouseButtonMask.Left : MouseButtonMask.Right }, true);
    private void Click(Vector2 position, MouseButton button = MouseButton.Left)
    { Pointer(position); Button(position, button, true); Button(position, button, false); }
    private void Wheel(Vector2 position)
    {
        Pointer(position);
        foreach (bool pressed in new[] { true, false })
            GetViewport().PushInput(new InputEventMouseButton { Position = position, GlobalPosition = position,
                ButtonIndex = MouseButton.WheelUp, Pressed = pressed, Factor = 1 }, true);
    }
    private void Check(bool condition, string message)
    {
        _checks++;
        if (!condition) throw new InvalidOperationException(message);
    }
}
