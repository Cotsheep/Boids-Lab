using System;
using System.Linq;
using System.Threading.Tasks;
using Godot;

namespace BoidsLab.Tests;

/// <summary>真实三维主场景中的观察操作；仅在无图形模式下发送视口输入。</summary>
public partial class Observation3DChecks : Node
{
    private int _checks;
    private MainController _app = null!;
    private BoidManager3D _world = null!;
    private SimulationCamera3D _camera = null!;

    public override async void _Ready()
    {
        if (DisplayServer.GetName() != "headless")
        {
            GD.PushError("Run observation_3d_checks.tscn with --headless.");
            GetTree().Quit(2);
            return;
        }
        try
        {
            const string path = "res://.godot/validation/observation-3d.cfg";
            Check(new BoidConfig { BoidCount = 0 }.Save(path) == Error.Ok, "Test config is isolated");
            _app = GD.Load<PackedScene>("res://scenes/main_3d.tscn").Instantiate<MainController>();
            _app.ConfigurationPath = path;
            AddChild(_app);
            _world = _app.GetNode<BoidManager3D>("World/BoidsWorld3D");
            _camera = _world.GetNode<SimulationCamera3D>("SimulationView/Camera3D");
            await Frame();
            await CheckCamera();
            await CheckUiIsolation();
            await CheckSelectionAndDebug();
            GD.Print($"PASS: {_checks} 3D observation/input assertions (headless).");
            GetTree().Quit();
        }
        catch (Exception error)
        {
            GD.PushError(error.ToString());
            GetTree().Quit(1);
        }
    }

    private async Task CheckCamera()
    {
        ActionKey("toggle_ui");
        Vector2 pointer = new(700, 300);
        Transform3D home = _camera.Transform;
        Aabb bounds = _world.WorldBounds;
        Boid3D boid = _world.SpawnBoid(Vector3.Zero, new Vector3(10, 20, 30));
        Vector3 position = boid.Position;
        SimulationStats stats = _world.Stats;
        Drag(pointer, new Vector2(120, 60), MouseButton.Right);
        Check(!_camera.Transform.IsEqualApprox(home), $"Right drag orbits the camera; viewport={GetViewport().GetVisibleRect()}, HUD={_app.GetNode<Control>("%UIRoot").Visible}, input={_camera.IsProcessingUnhandledInput()}");
        Near(_camera.OrbitCenter, bounds.GetCenter(), 0.001f, "Orbit preserves its center");
        Near(-_camera.Basis.Z, (_camera.OrbitCenter - _camera.Position).Normalized(), 0.001f, "Camera keeps looking at its center");
        Check(_camera.Basis.IsFinite(), "Orbit basis remains finite");
        Vector3 anchor = _camera.OrbitCenter;
        Vector2 screenBefore = _camera.UnprojectPosition(anchor);
        Vector2 movement = new(50, -35);
        Drag(pointer, movement, MouseButton.Middle);
        Near(_camera.UnprojectPosition(anchor), screenBefore + movement, 0.01f, "Middle drag follows the mouse in the camera plane");
        Vector3 projected = _camera.ProjectPosition(pointer, 100);
        Wheel(pointer, true);
        Near(_camera.UnprojectPosition(projected), pointer, 0.01f, "Zoom keeps the mouse's projected point anchored");
        Check(_camera.ZoomFactor > 1, "Wheel zooms in");
        for (int i = 0; i < 80; i++) Wheel(pointer, true);
        Check(_camera.ZoomFactor == SimulationCamera3D.MaxZoomFactor, "Maximum zoom enforced");
        for (int i = 0; i < 100; i++) Wheel(pointer, false);
        Check(_camera.ZoomFactor == SimulationCamera3D.MinZoomFactor, "Minimum zoom enforced");
        for (int i = 0; i < 4; i++) Drag(pointer, new Vector2(0, 200), MouseButton.Right);
        Check(_camera.Basis.IsFinite() && Mathf.Abs(_camera.Basis.Z.Dot(Vector3.Up)) < 1, "Pitch cannot cross the pole");
        ActionKey("reset_camera");
        Check(_camera.Transform.IsEqualApprox(home) && _camera.ZoomFactor == 1, "Home restores the complete reference view");
        Check(_world.WorldBounds == bounds && boid.Position == position && _world.Stats == stats,
            "Camera gestures do not mutate bounds, simulation positions or statistics");

        Pointer(pointer);
        Button(pointer, MouseButton.Right, true);
        GetWindow().EmitSignal(Window.SignalName.FocusExited);
        Pointer(pointer + movement, movement, MouseButtonMask.Right);
        Button(pointer + movement, MouseButton.Right, false);
        Check(_camera.Transform.IsEqualApprox(home), "Losing focus cancels a drag");
        Button(pointer, MouseButton.Middle, true);
        Pointer(pointer + movement, movement); // 丢失按键状态。
        Pointer(pointer + movement * 2, movement, MouseButtonMask.Middle);
        Button(pointer + movement * 2, MouseButton.Middle, false);
        Check(_camera.Transform.IsEqualApprox(home), "Missing button state cancels a drag");

        Drag(pointer, movement, MouseButton.Middle);
        Vector3 centerBeforeResize = _camera.OrbitCenter;
        Wheel(pointer, true);
        centerBeforeResize = _camera.OrbitCenter;
        float zoomBeforeResize = _camera.ZoomFactor;
        GetWindow().Size = new Vector2I(1360, 860); // headless，无原生图形窗口。
        await Frame();
        Check(_camera.OrbitCenter == centerBeforeResize && _camera.ZoomFactor == zoomBeforeResize,
            "Headless viewport resize preserves pan and zoom");
        ActionKey("reset_camera");
        _world.SetRunning(true);
        Drag(new Vector2(750, 300), movement, MouseButton.Right);
        Check(_world.IsRunning, "Camera gestures keep a running simulation running");
        _world.SetRunning(false);
        ActionKey("reset_camera");
        ActionKey("toggle_ui");
    }

    private async Task CheckUiIsolation()
    {
        var panel = _app.GetNode<ControlPanel>("%ControlPanel");
        var start = (Button)panel.FindChild("StartPause", true, false);
        Vector2 uiPoint = start.GetGlobalRect().GetCenter();
        Transform3D before = _camera.Transform;
        float zoom = _camera.ZoomFactor;
        Drag(uiPoint, new Vector2(50, 20), MouseButton.Right);
        Drag(uiPoint, new Vector2(50, 20), MouseButton.Middle);
        Wheel(uiPoint, true);
        Check(_camera.Transform.IsEqualApprox(before) && _camera.ZoomFactor == zoom,
            "UI consumes orbit, pan and wheel gestures");

        Vector2 worldPoint = new(750, 300);
        Pointer(worldPoint);
        Button(worldPoint, MouseButton.Middle, true);
        Pointer(uiPoint, new Vector2(20, 10), MouseButtonMask.Middle);
        Button(uiPoint, MouseButton.Middle, false);
        Transform3D released = _camera.Transform;
        Pointer(worldPoint, new Vector2(70, 10), MouseButtonMask.Middle);
        Check(_camera.Transform.IsEqualApprox(released), "A world drag can end over UI without sticking");

        Pointer(worldPoint);
        Button(worldPoint, MouseButton.Right, true);
        ActionKey("toggle_help");
        var help = _app.GetNode<HelpOverlay>("%HelpOverlay");
        Check(help.Visible, "Shared help opens");
        before = _camera.Transform;
        Pointer(worldPoint + new Vector2(80, 40), new Vector2(80, 40), MouseButtonMask.Right);
        Button(worldPoint, MouseButton.Right, false);
        ActionKey("reset_camera");
        Wheel(worldPoint, true);
        Check(_camera.Transform.IsEqualApprox(before), "Help cancels the pending gesture and blocks world input");
        ActionKey("close_help");
        Pointer(worldPoint, new Vector2(50, 30), MouseButtonMask.Right);
        Check(_camera.Transform.IsEqualApprox(before), "Closing help does not resume an old drag");
        ActionKey("reset_camera");
        var speed = (SpinBox)panel.FindChild("MaxSpeed", true, false);
        Drag(worldPoint, new Vector2(50, 20), MouseButton.Middle);
        before = _camera.Transform;
        speed.GetLineEdit().GrabFocus();
        GetViewport().PushInput(new InputEventKey { Keycode = Key.Home, PhysicalKeycode = Key.Home, Pressed = true }, true);
        GetViewport().PushInput(new InputEventKey { Keycode = Key.Home, PhysicalKeycode = Key.Home, Pressed = false }, true);
        Check(_camera.Transform.IsEqualApprox(before), "Home edits focused text instead of resetting the camera");
        speed.GetLineEdit().ReleaseFocus();
        await Frame();
    }

    private async Task Frame()
    {
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    private async Task CheckSelectionAndDebug()
    {
        ActionKey("toggle_ui"); // 隐藏 HUD 后，在真实主场景中验证三维命中与绘制。
        ActionKey("reset_camera");
        foreach (Boid3D boid in _world.Boids.ToArray()) _world.RemoveBoid(boid);
        var view = _world.GetNode<SimulationView3D>("SimulationView");
        var panel = _app.GetNode<ControlPanel>("%ControlPanel");
        BoidConfig config = _world.Config;
        ((SpinBox)panel.FindChild("PerceptionRadius", true, false)).Value = 80;
        foreach (string name in new[] { "ShowNeighbors", "ShowPerceptionRadius", "ShowVelocity" })
            ((CheckBox)panel.FindChild(name, true, false)).ButtonPressed = true;
        Boid3D a = _world.SpawnBoid(Vector3.Zero, _camera.Basis.X * 120);
        Boid3D b = _world.SpawnBoid(_camera.Basis.X * 40, _camera.Basis.Y * 20);
        _world.SpawnBoid(-_camera.Basis.Y * 100, Vector3.Zero);
        await Click(Screen(a));
        Check(_world.SelectedBoid == a && a.IsSelected && !b.IsSelected, "Paused left click selects exactly one individual");
        Check(a.GetNode<MeshInstance3D>("Body").MaterialOverride == a.SelectionMaterial
            && b.GetNode<MeshInstance3D>("Body").MaterialOverride is null, "Highlight does not change the shared normal material");
        Check(view.DebugGeometry.PerceptionCount == 1 && view.DebugGeometry.NeighborCount == 1
            && view.DebugGeometry.VelocityCount == 2, "Local range/links and all moving velocity arrows are independent");
        Check(((Label)panel.FindChild("SelectedBoidStatus", true, false)).Text.Contains($"#{a.Id}"), "Shared UI shows the selected ID");
        await Click(Screen(a));
        Check(_world.SelectedBoid is null && !a.IsSelected && a.GetNode<MeshInstance3D>("Body").MaterialOverride is null,
            "Repeated click deselects and restores the original appearance");
        Check(view.DebugGeometry.RangePoints.Count == 0 && view.DebugGeometry.NeighborPoints.Count == 0
            && view.DebugGeometry.VelocityCount == 2, "Deselection removes only local debug overlays");
        await Click(Screen(b));
        Check(_world.SelectedBoid == b && !a.IsSelected, "Selection can transfer to another individual");
        await Click(new Vector2(1100, 300));
        Check(_world.SelectedBoid is null, "Clicking empty world clears selection");

        Vector2 press = Screen(a);
        Pointer(press);
        Button(press, MouseButton.Left, true);
        _world.SetRunning(true);
        _world.Step(0.5);
        Check(Screen(a).DistanceTo(press) > 10, "Moving individual leaves the press position");
        Button(press, MouseButton.Left, false);
        _world.SetRunning(false);
        Check(_world.SelectedBoid == a, "A running click retains the identity hit on press");
        var stats = _world.Stats;
        ((CheckBox)panel.FindChild("ShowPerceptionRadius", true, false)).ButtonPressed = false;
        ((CheckBox)panel.FindChild("ShowNeighbors", true, false)).ButtonPressed = false;
        ((CheckBox)panel.FindChild("ShowGrid", true, false)).ButtonPressed = false;
        Check(view.DebugGeometry.RangePoints.Count == 0 && view.DebugGeometry.NeighborPoints.Count == 0
            && !view.GetNode<MeshInstance3D>("Grid").Visible && view.GetNode<MeshInstance3D>("ReferenceFrame").Visible,
            "Paused UI toggles update real overlay geometry and reference grid");
        Check(_world.Stats == stats && _world.SelectedBoid == a, "Debug toggles preserve selection and measured statistics");
        ((CheckBox)panel.FindChild("ShowPerceptionRadius", true, false)).ButtonPressed = true;
        ((CheckBox)panel.FindChild("ShowNeighbors", true, false)).ButtonPressed = true;
        ((SpinBox)panel.FindChild("PerceptionRadius", true, false)).Value = 1;
        Check(_world.Stats.NeighborCandidateChecks is null && view.DebugGeometry.NeighborCount == 0
            && view.DebugGeometry.PerceptionCount == 1, "Paused radius update redraws immediately and invalidates old statistics");
        var saved = new BoidConfig();
        Check(saved.Load(_app.ConfigurationPath) == Error.Ok && saved.PerceptionRadius == 1 && !saved.ShowGrid
            && saved.ShowNeighbors && saved.ShowPerceptionRadius, "Implemented debug choices persist to the separate 3D config");

        Transform3D cameraBefore = _camera.Transform;
        Drag(Screen(b), new Vector2(30, 0), MouseButton.Left);
        Check(_world.SelectedBoid == a && _camera.Transform.IsEqualApprox(cameraBefore), "Left drag neither selects nor moves the camera");
        Drag(Screen(a), new Vector2(20, 15), MouseButton.Right);
        Check(_world.SelectedBoid == a, "Orbit over a boid does not change selection");
        ActionKey("reset_camera");
        ActionKey("toggle_ui");
        await Frame();
        // 顶部帮助按钮始终可见；参数区可能因先前的数字焦点而已经滚动。
        var helpButton = _app.GetNode<Button>("%HelpButton");
        await Click(helpButton.GetGlobalRect().GetCenter());
        Check(_world.SelectedBoid == a, "UI click does not select a background boid");
        ActionKey("close_help");
        _world.SetRunning(false);
        press = Screen(b);
        Pointer(press);
        Button(press, MouseButton.Left, true);
        ActionKey("toggle_help");
        Button(press, MouseButton.Left, false);
        ActionKey("close_help");
        Check(_world.SelectedBoid == a, "Opening help cancels an unfinished selection gesture");

        ActionKey("toggle_ui");
        foreach (Boid3D boid in _world.Boids.ToArray()) _world.RemoveBoid(boid);
        Check(_world.SelectedBoid is null && view.DebugGeometry.RangePoints.Count == 0, "Deleting the selected individual clears its overlays");
        Boid3D back = _world.SpawnBoid(-_camera.Basis.Z * 80, Vector3.Zero);
        Boid3D front = _world.SpawnBoid(_camera.Basis.Z * 80, Vector3.Zero);
        await Click(Screen(back));
        Check(_world.SelectedBoid == front, "Overlapping projections select the nearest ray hit");
        await Click(Screen(front));
        Check(_world.SelectedBoid is null, "Nearest overlapping individual can be deselected");
        Pointer(Screen(front));
        Button(Screen(front), MouseButton.Left, true);
        _world.RemoveBoid(front);
        Button(Screen(back), MouseButton.Left, false);
        Check(_world.SelectedBoid is null, "Removing a pressed individual does not select a replacement");
        await Click(Screen(back));
        Check(_world.SelectedBoid == back, "Remaining individual is selectable");
        press = Screen(back);
        Button(press, MouseButton.Left, true);
        _world.Reset();
        Button(press, MouseButton.Left, false);
        Check(_world.SelectedBoid is null && _world.Boids.Count == 0 && view.DebugGeometry.NeighborPoints.Count == 0,
            "Reset clears selection, pending gesture and local debug data");
        Boid3D behind = _world.SpawnBoid(Vector3.Zero, Vector3.Zero);
        behind.Position = _camera.Position + _camera.Basis.Z * 50;
        await Click(_camera.GetViewport().GetVisibleRect().GetCenter());
        Check(_world.SelectedBoid is null, "Objects behind the camera are not selectable");
    }

    private Vector2 Screen(Boid3D boid) => _camera.UnprojectPosition(boid.GlobalPosition);
    private async Task Click(Vector2 point)
    {
        Pointer(point);
        Button(point, MouseButton.Left, true);
        Button(point, MouseButton.Left, false);
        await Frame();
    }

    private void ActionKey(string action) => GetViewport().PushInput(new InputEventAction { Action = action, Pressed = true }, true);
    private void Pointer(Vector2 position, Vector2 relative = default, MouseButtonMask mask = 0) =>
        GetViewport().PushInput(new InputEventMouseMotion { Position = position, GlobalPosition = position, Relative = relative, ButtonMask = mask }, true);
    private void Button(Vector2 point, MouseButton button, bool pressed) =>
        GetViewport().PushInput(new InputEventMouseButton { Position = point, GlobalPosition = point, ButtonIndex = button, Pressed = pressed }, true);
    private void Wheel(Vector2 point, bool up)
    {
        Pointer(point);
        Button(point, up ? MouseButton.WheelUp : MouseButton.WheelDown, true);
    }
    private void Drag(Vector2 point, Vector2 movement, MouseButton button)
    {
        Pointer(point);
        Button(point, button, true);
        MouseButtonMask mask = button == MouseButton.Right ? MouseButtonMask.Right : button == MouseButton.Middle ? MouseButtonMask.Middle : MouseButtonMask.Left;
        Pointer(point + movement, movement, mask);
        Button(point + movement, button, false);
    }
    private void Near(Vector3 actual, Vector3 expected, float epsilon, string message) => Check(actual.DistanceTo(expected) <= epsilon, message);
    private void Near(Vector2 actual, Vector2 expected, float epsilon, string message) => Check(actual.DistanceTo(expected) <= epsilon, $"{message}: {actual} != {expected}");
    private void Check(bool condition, string message)
    {
        _checks++;
        if (!condition) throw new InvalidOperationException(message);
    }
}
