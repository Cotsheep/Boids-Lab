using System;
using System.Threading.Tasks;
using Godot;

namespace BoidsLab.Tests;

/// <summary>真实视口事件验证领队操控、GUI 隔离与相机/编辑器衔接。</summary>
public partial class LeaderInteraction3DChecks : Node
{
    private int _checks;
    private MainController _app = null!;
    private BoidManager3D _world = null!;
    private SimulationCamera3D _camera = null!;

    public override async void _Ready()
    {
        if (DisplayServer.GetName() != "headless") { GetTree().Quit(2); return; }
        try
        {
            const string path = "res://.godot/validation/leader-interaction-3d.cfg";
            new BoidConfig { BoidCount = 0 }.Save(path);
            _app = GD.Load<PackedScene>("res://scenes/main_3d.tscn").Instantiate<MainController>();
            _app.World3D = (SimulationWorldDefinition)_app.World3D.Duplicate();
            _app.World3D.ConfigurationPath = path;
            _app.World2D = (SimulationWorldDefinition)_app.World2D.Duplicate();
            _app.World2D.ConfigurationPath = "res://.godot/validation/leader-interaction-switch-2d.cfg";
            new BoidConfig { BoidCount = 0 }.Save(_app.World2D.ConfigurationPath);
            AddChild(_app);
            await Frame();
            GetViewport().NotifyMouseEntered();
            _world = _app.GetNode<BoidManager3D>("World/BoidsWorld3D");
            _camera = _world.GetNode<SimulationCamera3D>("SimulationView/Camera3D");
            var panel = _app.GetNode<ControlPanel>("%ControlPanel");
            var help = _app.GetNode<HelpOverlay>("%HelpOverlay");
            var hud = _app.GetNode<Control>("%UIRoot");
            var checkbox = (CheckBox)panel.FindChild("IsLeader", true, false);
            var control = (Button)panel.FindChild("ControlLeader", true, false);
            var status = (Label)panel.FindChild("LeaderStatus", true, false);
            var a = _world.SpawnBoid(Vector3.Zero, Vector3.Zero);
            var b = _world.SpawnBoid(Vector3.Right * 80, Vector3.Zero);
            Tap(Key.F1);
            Click(Screen(a));
            Check(_world.SelectedBoid == a, "Observation click selects the future leader");
            Tap(Key.F1);
            panel.RevealControl(checkbox);
            await Frame();
            Check(panel.GetNode<ScrollContainer>("LeaderPage").GetGlobalRect().Encloses(checkbox.GetGlobalRect()),
                "Revealed leader assignment is fully visible after restoring the HUD");
            Click(checkbox.GetGlobalRect().GetCenter());
            Check(_world.Leader == a && control.Disabled && _world.IsLeaderControlActive && !_world.IsRunning
                && GetViewport().GuiGetFocusOwner() is null,
                "Native assignment enters control and clears focus without starting the simulation");
            Check(status.Text.Contains("操控中") && status.Text.Contains("已暂停"), "3D status describes control and pause separately");
            Tap(Key.F1);
            Click(Screen(a));
            Check(_world.SelectedBoid is null && _world.Leader == a && a.IsLeader, "Perspective deselection preserves identity");
            Click(Screen(b));
            Check(_world.SelectedBoid == b && _world.Leader == a, "Perspective selection does not implicitly transfer leader");
            checkbox.ButtonPressed = true;
            Check(_world.Leader == b && !a.IsLeader && b.IsLeader && _world.IsLeaderControlActive
                && _world.LeaderController.MovementDirection == Vector3.Zero, "UI transfer assigns one identity and starts fresh control");
            _world.SetLeader(a);
            Tap(Key.F3);
            // Headless DisplayServer cannot capture an OS pointer; graphical acceptance checks the actual capture.
            Check(_world.IsLeaderControlActive, "Physical F3 enters direct control");
            Vector3 position = a.Position;
            Quaternion orientation = a.Quaternion;
            Quaternion target = _world.LeaderController.TargetOrientation;
            Pointer(new Vector2(600, 400), new Vector2(70, -90));
            await Frame();
            Check(a.Position == position && a.Quaternion == orientation && _world.LeaderController.TargetOrientation != target,
                "Paused mouse motion updates target view without integrating the leader");
            Boid3D? selection = _world.SelectedBoid;
            Click(Screen(b));
            Check(_world.SelectedBoid == selection, "Captured clicks cannot select the world or operate GUI");
            KeyEvent(Key.W, true);
            Tap(Key.Space);
            Check(_world.IsRunning && _world.IsLeaderControlActive, "Space resumes without releasing control");
            for (int i = 0; i < 120; i++) _world.Step(1.0 / 60);
            Check(a.Position.Y > 10 && a.Velocity.Length() > 60, "Physical WASD flies along the pitched target view");
            Tap(Key.Space);
            position = a.Position;
            await Frame();
            Check(a.Position == position && _world.IsLeaderControlActive, "Space pauses all motion while retaining capture");
            KeyEvent(Key.W, false);
            Tap(Key.Space);
            for (int i = 0; i < 180; i++) _world.Step(1.0 / 60);
            _world.SetRunning(false);
            Check(a.Velocity.Length() < 0.01f, "Key release reaches rest without stale movement");
            Button(new Vector2(600, 400), MouseButton.WheelDown, true);
            var saved = new BoidConfig();
            saved.Load(path);
            Check(saved.FollowCameraDistance > 300 && saved.FollowCameraDistance == _world.Config.FollowCameraDistance,
                "Captured wheel changes and persists follow distance");
            Tap(Key.Home);
            saved.Load(path);
            Check(saved.FollowCameraDistance == 300, "Home persists default follow distance");
            Tap(Key.Escape);
            Check(!_world.IsLeaderControlActive && _camera.IsFollowing && Input.MouseMode == Input.MouseModeEnum.Visible,
                "Escape releases pointer while keeping leader and perspective following");
            CheckReleasedView(a);
            Tap(Key.F1);
            var speed = (SpinBox)panel.FindChild("MaxSpeed", true, false);
            panel.RevealControl(speed);
            await Frame();
            speed.GetLineEdit().GrabFocus();
            Tap(Key.F3);
            KeyEvent(Key.W, true);
            Check(!_world.IsLeaderControlActive && _world.LeaderController.MovementDirection == Vector3.Zero,
                "Text editing blocks flight keys and the capture shortcut");
            KeyEvent(Key.W, false);
            speed.GetLineEdit().ReleaseFocus();
            await Frame(); // Let the deferred focus-scroll finish before explicitly scrolling back.
            panel.RevealControl(control);
            await Frame();
            Click(control.GetGlobalRect().GetCenter());
            Check(_world.IsLeaderControlActive && GetViewport().GuiGetFocusOwner() is null, $"Control button clears GUI focus and captures: active={_world.IsLeaderControlActive}, focus={GetViewport().GuiGetFocusOwner()}, hud={hud.Visible}, disabled={control.Disabled}, rect={control.GetGlobalRect()}, scroll={panel.CurrentCategory}");
            Tap(Key.F2);
            Check(help.Visible && !_world.IsLeaderControlActive, "Help releases control");
            Tap(Key.F3);
            Check(!_world.IsLeaderControlActive, "Help blocks capture");
            Tap(Key.Escape);
            Check(!help.Visible && !_world.IsLeaderControlActive, "Closing help never recaptures");
            GetViewport().GuiGetFocusOwner()?.ReleaseFocus();
            Tap(Key.F3);
            KeyEvent(Key.W, true);
            GetWindow().EmitSignal(Window.SignalName.FocusExited);
            Check(!_world.IsLeaderControlActive && _world.LeaderController.MovementDirection == Vector3.Zero, "Focus loss clears held input");
            GetWindow().EmitSignal(Window.SignalName.FocusEntered);
            Check(!_world.IsLeaderControlActive, "Focus recovery never recaptures");
            KeyEvent(Key.W, false);
            Tap(Key.F3);
            Tap(Key.F1);
            Check(!_world.IsLeaderControlActive, "HUD toggle releases control");
            if (hud.Visible) Tap(Key.F1);
            await CheckEditing(a);
            _world.SetLeaderControlActive(true);
            _world.SetLeader(b);
            Check(!_world.IsLeaderControlActive && _world.Leader == b && _camera.IsFollowing, "Transfer releases controls without observation mode");
            _world.SetLeaderControlActive(true);
            _world.Reset();
            Check(!_world.IsLeaderControlActive && _world.Leader is null && !_camera.IsFollowing, "Reset cleans both camera and capture state");
            a = _world.SpawnBoid(Vector3.Zero, Vector3.Zero);
            _world.SetLeader(a);
            Tap(Key.F3);
            _app.RequestWorldSwitch(0);
            await Frame();
            var world2D = _app.GetNode<BoidManager>("World/BoidsWorld2D");
            Check(!GodotObject.IsInstanceValid(_world) && !world2D.IsLeaderControlActive
                && Input.MouseMode == Input.MouseModeEnum.Visible, "World switching frees old control and releases the global pointer");
            _app.RequestWorldSwitch(1);
            await Frame();
            var fresh = _app.GetNode<BoidManager3D>("World/BoidsWorld3D");
            Check(fresh.Leader is null && !fresh.IsLeaderControlActive && !fresh.IsRunning,
                "Returning to 3D creates a clean paused observation world");
            GD.Print($"PASS: {_checks} 3D leader input/camera/editor/lifecycle assertions (headless).");
            GetTree().Quit();
        }
        catch (Exception error) { GD.PushError(error.ToString()); GetTree().Quit(1); }
    }

    private void CheckReleasedView(Boid3D leader)
    {
        Vector2 point = GetViewport().GetVisibleRect().Size * new Vector2(0.65f, 0.35f);
        Vector3 position = leader.Position;
        Quaternion orientation = leader.Quaternion;
        Quaternion target = _world.LeaderController.TargetOrientation;
        Transform3D camera = _camera.Transform;
        Pointer(point, new Vector2(30, 20));
        Check(_world.LeaderController.TargetOrientation == target, "Released pointer motion alone does not rotate the view");
        RightDrag(point);
        _camera._Process(0.5);
        Check(_world.LeaderController.TargetOrientation != target && _camera.Transform != camera
            && !_world.IsLeaderControlActive && !_world.IsRunning,
            "Right drag adjusts the paused follow view without capturing or running");
        Check(leader.Position == position && leader.Quaternion == orientation,
            "Paused released-view rotation preserves the leader position and facing");
        target = _world.LeaderController.TargetOrientation;
        Tap(Key.F3);
        KeyEvent(Key.W, true);
        Check(_world.IsLeaderControlActive && _world.LeaderController.TargetOrientation == target
            && _world.LeaderController.MovementDirection.Dot(target * Vector3.Forward) > 0.999f,
            "Recapturing keeps the adjusted view as the movement reference");
        KeyEvent(Key.W, false);
        Tap(Key.Escape);
        Tap(Key.F2);
        RightDrag(point);
        Check(_world.LeaderController.TargetOrientation == target, "Help blocks released follow-view rotation");
        Tap(Key.Escape);
        Tap(Key.F1);
        var selector = _app.GetNode<OptionButton>("%WorldSelector");
        RightDrag(selector.GetGlobalRect().GetCenter());
        Check(_world.LeaderController.TargetOrientation == target, "A right drag starting on UI does not rotate the follow view");
        Tap(Key.F1);
        Pointer(point);
        Button(point, MouseButton.Right, true);
        GetWindow().EmitSignal(Window.SignalName.FocusExited);
        Pointer(point + new Vector2(60, -35), new Vector2(60, -35), MouseButtonMask.Right);
        Button(point, MouseButton.Right, false);
        GetWindow().EmitSignal(Window.SignalName.FocusEntered);
        Check(_world.LeaderController.TargetOrientation == target && !_world.IsLeaderControlActive,
            "Focus loss cancels released-view dragging without recapturing on recovery");
    }

    private void RightDrag(Vector2 point)
    {
        Pointer(point);
        Button(point, MouseButton.Right, true);
        Pointer(point + new Vector2(60, -35), new Vector2(60, -35), MouseButtonMask.Right);
        Button(point + new Vector2(60, -35), MouseButton.Right, false);
    }

    private async Task CheckEditing(Boid3D leader)
    {
        _world.SetRunning(false);
        _world.SetLeader(null);
        leader.Position = new Vector3(20000, 1000, -30000);
        leader.Initialize(_world.Config, leader.Id, Vector3.Zero);
        _world.SetLeader(leader);
        var editor = _world.GetNode<ObstacleEditor3D>("ObstacleEditor");
        var obstacle = editor.AddObstacle(ObstacleShape.Box, leader.Position + Vector3.Right * 100);
        obstacle.SetDimensions(Vector3.One * 40);
        Vector2 point = _camera.UnprojectPosition(obstacle.GlobalPosition);
        Pointer(point);
        Button(point, MouseButton.Left, true);
        Transform3D before = _camera.Transform;
        leader.Position += Vector3.Right * 20;
        _camera._Process(1);
        Check(_camera.Transform == before, "Obstacle drag freezes following at a distant coordinate");
        Pointer(point + new Vector2(20, 10), new Vector2(20, 10), MouseButtonMask.Left);
        Check(_camera.UnprojectPosition(obstacle.GlobalPosition).DistanceTo(point + new Vector2(20, 10)) < 0.1f,
            "Perspective drag tracks the fixed view plane without box clamping");
        Button(point + new Vector2(20, 10), MouseButton.Left, false);
        _camera._Process(1);
        Check(_camera.Transform != before, "Finishing the drag resumes camera follow");
        _world.BeginObstaclePlacement(ObstacleShape.Sphere);
        before = _camera.Transform;
        leader.Position += Vector3.Up * 20;
        _camera._Process(1);
        Check(_camera.Transform == before, "Placement preview freezes follow and its editing plane");
        Vector2 place = GetViewport().GetVisibleRect().Size * new Vector2(0.7f, 0.4f);
        Click(place);
        Check(editor.Obstacles.Count == 2 && _camera.UnprojectPosition(editor.Obstacles[1].GlobalPosition).DistanceTo(place) < 0.1f,
            "Infinite-world placement remains under the cursor");
        Check(_camera.UnitsPerPixelAt(leader.Position + Vector3.Forward * 1000) > _camera.UnitsPerPixelAt(leader.Position),
            "Perspective picking and debug scale increase with depth");
        await Frame();
    }

    private Vector2 Screen(Boid3D boid) => _camera.UnprojectPosition(boid.GlobalPosition);
    private void KeyEvent(Key key, bool pressed) => GetViewport().PushInput(new InputEventKey { Keycode = key, PhysicalKeycode = key, Pressed = pressed }, true);
    private void Tap(Key key) { KeyEvent(key, true); KeyEvent(key, false); }
    private void Pointer(Vector2 point, Vector2 relative = default, MouseButtonMask mask = 0) =>
        GetViewport().PushInput(new InputEventMouseMotion { Position = point, GlobalPosition = point, Relative = relative, ButtonMask = mask }, true);
    private void Button(Vector2 point, MouseButton button, bool down) => GetViewport().PushInput(new InputEventMouseButton
        { Position = point, GlobalPosition = point, ButtonIndex = button, Pressed = down }, true);
    private void Click(Vector2 point) { Pointer(point); Button(point, MouseButton.Left, true); Button(point, MouseButton.Left, false); }
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
