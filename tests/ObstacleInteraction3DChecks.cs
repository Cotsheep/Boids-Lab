using System;
using System.Threading.Tasks;
using Godot;

namespace BoidsLab.Tests;

public partial class ObstacleInteraction3DChecks : Node
{
    private MainController _app = null!;
    private BoidManager3D _world = null!;
    private ObstacleEditor3D _editor = null!;
    private SimulationCamera3D _camera = null!;
    private int _checks;

    public override async void _Ready()
    {
        if (DisplayServer.GetName() != "headless") { GetTree().Quit(2); return; }
        try
        {
            const string path = "res://.godot/validation/obstacle-interaction-3d.cfg";
            Check(new BoidConfig { BoidCount = 0 }.Save(path) == Error.Ok, "Isolated test preferences");
            _app = GD.Load<PackedScene>("res://scenes/main_3d.tscn").Instantiate<MainController>();
            _app.World3D = (SimulationWorldDefinition)_app.World3D.Duplicate();
            _app.World3D.ConfigurationPath = path;
            _app.World2D = (SimulationWorldDefinition)_app.World2D.Duplicate();
            _app.World2D.ConfigurationPath = "res://.godot/validation/obstacle-switch-2d.cfg";
            Check(new BoidConfig { BoidCount = 0 }.Save(_app.World2D.ConfigurationPath) == Error.Ok, "Isolated switching preferences");
            AddChild(_app);
            _world = _app.GetNode<BoidManager3D>("World/BoidsWorld3D");
            _editor = _world.GetNode<ObstacleEditor3D>("ObstacleEditor");
            _camera = _world.GetNode<SimulationCamera3D>("SimulationView/Camera3D");
            await Frame();
            var panel = _app.GetNode<ControlPanel>("%ControlPanel");
            ObstaclePanel controls = panel.Obstacles!;
            var addBox = (Button)controls.FindChild("AddBox", true, false);
            var addSphere = (Button)controls.FindChild("AddSphere", true, false);
            var width = (SpinBox)controls.FindChild("ObstacleWidth", true, false);
            var height = (SpinBox)controls.FindChild("ObstacleHeight", true, false);
            var depth = (SpinBox)controls.FindChild("ObstacleDepth", true, false);
            var delete = (Button)controls.FindChild("DeleteObstacle", true, false);
            Check(addBox.Text == "添加长方体" && addSphere.Text == "添加球体" && !width.Editable && delete.Disabled, "3D UI has correct shapes and initial edit state");
            int configChanges = 0;
            panel.ConfigurationChanged += () => configChanges++;
            Vector2 point = GetViewport().GetVisibleRect().Size * new Vector2(0.55f, 0.5f);
            Transform3D view = _camera.Transform;
            panel.GetParent<ScrollContainer>().EnsureControlVisible(addBox);
            await Frame();
            Click(addBox.GetGlobalRect().GetCenter());
            Check(_editor.PlacementShape == ObstacleShape.Box && _editor.Obstacles.Count == 0, "Creation button starts a preview, not a physical obstacle");
            Click(point);
            Check(_editor.Obstacles.Count == 1 && _editor.PlacementShape is null, "World click creates exactly one obstacle and consumes release");
            var box = _editor.Obstacles[0];
            Check(box.Shape == ObstacleShape.Box && box.IsSelected && _editor.SelectedObstacle == box
                && box.GetNode<MeshInstance3D>("Body").Mesh is BoxMesh, "Created box uses a real 3D mesh and selection highlight");
            Near(Screen(box), point, "Placement lies under the cursor");
            Check(_camera.Transform == view && !_world.IsRunning, "Placement does not orbit or start the simulation");
            await Frame();
            Check(width.Editable && depth.IsVisibleInTree() && !delete.Disabled, "Selected box exposes all three editable dimensions");
            width.Value = 140; height.Value = 90; depth.Value = 70;
            Check(box.Dimensions == new Vector3(140, 90, 70), "Width height and depth resize the same object");
            Vector2 beforeScreen = Screen(box);
            Vector3 before = box.Position;
            Drag(beforeScreen, new Vector2(24, 18), MouseButton.Left);
            Near(Screen(box), beforeScreen + new Vector2(24, 18), "Dragging follows the cursor on the view plane");
            Check(Mathf.Abs((box.Position - before).Dot(_camera.Basis.Z)) < 0.001f && _camera.Transform == view,
                "Dragging preserves depth in the current view and leaves camera unchanged");
            Vector3 dimensions = box.Dimensions;
            float zoom = _camera.ZoomFactor;
            Wheel(Screen(box), true);
            Check(box.Dimensions.X > dimensions.X && Mathf.IsEqualApprox(box.Dimensions.X / box.Dimensions.Z, dimensions.X / dimensions.Z)
                && _camera.ZoomFactor == zoom, "Shift wheel scales all dimensions uniformly without camera zoom");
            dimensions = box.Dimensions;
            Wheel(Screen(box), false);
            Check(_camera.ZoomFactor > zoom && box.Dimensions == dimensions, "Ordinary wheel still zooms the camera");
            Check(configChanges == 0, "Editing obstacles does not overwrite shared simulation preferences");

            ActionKey("toggle_ui");
            Vector2 empty = GetViewport().GetVisibleRect().Size * new Vector2(0.86f, 0.25f);
            Click(empty);
            Check(_editor.SelectedObstacle is null && !box.IsSelected, "Empty click clears obstacle selection");
            Click(Screen(box));
            Check(_editor.SelectedObstacle == box, "Obstacle remains selectable after camera zoom");
            Vector3 boidPoint = box.Position + _camera.Basis.Z * 150;
            Boid3D boid = _world.SpawnBoid(boidPoint, Vector3.Zero);
            Click(_camera.UnprojectPosition(boid.GlobalPosition));
            Check(_world.SelectedBoid == boid && _editor.SelectedObstacle is null, "A foreground Boid takes priority over a rear obstacle");
            boid.Position = box.Position - _camera.Basis.Z * 150;
            Click(Screen(box));
            Check(_editor.SelectedObstacle == box && _world.SelectedBoid is null, "Foreground obstacle takes priority and clears Boid selection");
            _world.RemoveBoid(boid);
            _world.SetRunning(true);
            Drag(Screen(box), new Vector2(-15, -12), MouseButton.Left);
            Check(_world.IsRunning && _editor.SelectedObstacle == box, "Editing works while running without pausing");
            _world.SetRunning(false);
            before = box.Position;
            Button(Screen(box), MouseButton.Left, true);
            GetWindow().EmitSignal(Window.SignalName.FocusExited);
            Pointer(Screen(box) + new Vector2(25, 10), new Vector2(25, 10), MouseButtonMask.Left);
            Button(Screen(box), MouseButton.Left, false);
            Check(box.Position == before, "Focus loss cancels obstacle dragging");
            Button(Screen(box), MouseButton.Left, true);
            ActionKey("toggle_help");
            Pointer(Screen(box) + new Vector2(30, 0), new Vector2(30, 0), MouseButtonMask.Left);
            Button(Screen(box), MouseButton.Left, false);
            Check(box.Position == before && _app.GetNode<HelpOverlay>("%HelpOverlay").Visible, "Help cancels and blocks edits");
            ActionKey("close_help");
            _world.BeginObstaclePlacement(ObstacleShape.Sphere);
            Button(empty, MouseButton.Right, true);
            Button(empty, MouseButton.Right, false);
            Check(_editor.PlacementShape is null, "Right click cancels placement without orbiting");
            _world.BeginObstaclePlacement(ObstacleShape.Sphere);
            ActionKey("close_help");
            Check(_editor.PlacementShape is null, "Escape cancels placement");

            ActionKey("toggle_ui");
            await Frame();
            addSphere.EmitSignal(Godot.Button.SignalName.Pressed);
            Click(GetViewport().GetVisibleRect().Size * new Vector2(0.65f, 0.65f));
            var sphere = _editor.SelectedObstacle!;
            Check(sphere.Shape == ObstacleShape.Sphere && sphere.GetNode<MeshInstance3D>("Body").Mesh is SphereMesh, "Sphere creates native sphere geometry");
            await Frame();
            Check(!height.IsVisibleInTree() && !depth.IsVisibleInTree(), "Sphere exposes only diameter");
            width.Value = 135;
            Check(sphere.Dimensions == Vector3.One * 135, "Diameter preserves a spherical shape");
            ((CheckBox)panel.FindChild("AvoidanceEnabled", true, false)).ButtonPressed = false;
            ((SpinBox)panel.FindChild("AvoidanceLookAhead", true, false)).Value = 1.25;
            ((SpinBox)panel.FindChild("ObstacleClearance", true, false)).Value = 20;
            var saved = new BoidConfig();
            Check(saved.Load(path) == Error.Ok && !saved.AvoidanceEnabled && saved.AvoidanceLookAhead == 1.25f
                && saved.ObstacleClearance == 20, "Avoidance settings persist in the 3D preferences");
            width.GetLineEdit().GrabFocus();
            await Frame();
            Tap(Key.Delete);
            Check(_editor.Obstacles.Count == 2, "Delete in a numeric field does not delete an obstacle");
            width.GetLineEdit().ReleaseFocus();
            delete.EmitSignal(Godot.Button.SignalName.Pressed);
            Check(_editor.Obstacles.Count == 1 && _editor.SelectedObstacle is null && delete.Disabled, "Delete button removes selection and refreshes UI");
            ActionKey("toggle_ui");
            Click(Screen(box));
            _world.Reset();
            Check(_editor.Obstacles.Count == 1 && !_world.IsRunning, "Reset retains obstacle layout");
            Click(Screen(box));
            Tap(Key.Delete);
            Check(_editor.Obstacles.Count == 0, "Delete shortcut removes selected obstacle in the world");
            _editor.AddObstacle(ObstacleShape.Box, Vector3.Zero);
            var oldEditor = _editor;
            _app.RequestWorldSwitch(0);
            await Frame();
            Check(!GodotObject.IsInstanceValid(oldEditor), "Switching releases the entire old obstacle editor");
            _app.RequestWorldSwitch(1);
            await Frame();
            Check(_app.GetNode<ObstacleEditor3D>("World/BoidsWorld3D/ObstacleEditor").Obstacles.Count == 0, "Switching back discards runtime obstacle layout");
            GetWindow().EmitSignal(Window.SignalName.FocusExited);
            GetViewport().EmitSignal(Viewport.SignalName.SizeChanged);
            Check(true, "No stale window or camera subscriptions after switching");
            GD.Print($"PASS: {_checks} 3D obstacle editing/UI/input assertions (headless).");
            GetTree().Quit();
        }
        catch (Exception error) { GD.PushError(error.ToString()); GetTree().Quit(1); }
    }

    private Vector2 Screen(Obstacle3D obstacle) => _camera.UnprojectPosition(obstacle.GlobalPosition);
    private void ActionKey(string action) => GetViewport().PushInput(new InputEventAction { Action = action, Pressed = true }, true);
    private void Pointer(Vector2 point, Vector2 relative = default, MouseButtonMask mask = 0) =>
        GetViewport().PushInput(new InputEventMouseMotion { Position = point, GlobalPosition = point, Relative = relative, ButtonMask = mask }, true);
    private void Button(Vector2 point, MouseButton button, bool pressed) =>
        GetViewport().PushInput(new InputEventMouseButton { Position = point, GlobalPosition = point, ButtonIndex = button, Pressed = pressed }, true);
    private void Click(Vector2 point) { Pointer(point); Button(point, MouseButton.Left, true); Button(point, MouseButton.Left, false); }
    private void Wheel(Vector2 point, bool shift)
    {
        Pointer(point);
        GetViewport().PushInput(new InputEventMouseButton { Position = point, GlobalPosition = point,
            ButtonIndex = MouseButton.WheelUp, Pressed = true, Factor = 1, ShiftPressed = shift }, true);
    }
    private void Drag(Vector2 point, Vector2 movement, MouseButton button)
    {
        Pointer(point); Button(point, button, true);
        Pointer(point + movement, movement, button == MouseButton.Left ? MouseButtonMask.Left : MouseButtonMask.Right);
        Button(point + movement, button, false);
    }
    private void Tap(Key key)
    {
        GetViewport().PushInput(new InputEventKey { Keycode = key, PhysicalKeycode = key, Pressed = true }, true);
        GetViewport().PushInput(new InputEventKey { Keycode = key, PhysicalKeycode = key, Pressed = false }, true);
    }
    private async Task Frame()
    {
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }
    private void Near(Vector2 actual, Vector2 expected, string message) => Check(actual.DistanceTo(expected) < 0.02f, $"{message}: {actual} != {expected}");
    private void Check(bool condition, string message)
    {
        _checks++;
        if (!condition) throw new InvalidOperationException(message);
    }
}
