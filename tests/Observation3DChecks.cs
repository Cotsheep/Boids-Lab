using System;
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
