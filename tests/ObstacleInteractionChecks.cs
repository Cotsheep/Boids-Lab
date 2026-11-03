using System;
using System.Linq;
using System.Threading.Tasks;
using Godot;

namespace BoidsLab.Tests;

/// <summary>真实主场景的障碍物编辑与输入隔离；不保存用户配置、不创建图形窗口。</summary>
public partial class ObstacleInteractionChecks : Node
{
    private int _checks;
    private BoidManager _world = null!;
    private SimulationCamera2D _camera = null!;
    private ObstacleEditor2D _editor = null!;

    public override async void _Ready()
    {
        if (DisplayServer.GetName() != "headless")
        {
            GD.PushError("Run obstacle_interaction_checks.tscn with --headless.");
            GetTree().Quit(2);
            return;
        }
        try
        {
            var game = GD.Load<PackedScene>("res://scenes/main.tscn").Instantiate<MainController>();
            AddChild(game);
            await Frame();
            GetViewport().NotifyMouseEntered();
            _world = game.GetNode<BoidManager>("World/BoidsWorld2D");
            _camera = _world.GetNode<SimulationCamera2D>("Camera2D");
            _editor = _world.GetNode<ObstacleEditor2D>("ObstacleEditor");
            ObstacleHarness2D.ClearLayout(_world);
            var panel = game.GetNode<ControlPanel>("%ControlPanel");
            panel.SelectCategory("Obstacles");
            await Frame();
            var help = game.GetNode<HelpOverlay>("%HelpOverlay");
            var hud = game.GetNode<Control>("%UIRoot");
            var addRectangle = (Button)panel.Obstacles!.FindChild("AddRectangle", true, false);
            var addCircle = (Button)panel.Obstacles!.FindChild("AddCircle", true, false);
            var delete = (Button)panel.Obstacles!.FindChild("DeleteObstacle", true, false);
            var width = (SpinBox)panel.Obstacles!.FindChild("ObstacleWidth", true, false);
            var height = (SpinBox)panel.Obstacles!.FindChild("ObstacleHeight", true, false);
            int configChanges = 0;
            panel.ConfigurationChanged += () => configChanges++;
            foreach (Boid2D boid in _world.Boids.ToArray()) _world.RemoveBoid(boid);
            Check(_editor.Obstacles.Count == 0 && delete.Disabled && !width.Editable,
                "No initial obstacles; editing controls are disabled without a selection");

            Vector2 viewport = GetViewport().GetVisibleRect().Size;
            Vector2 rectanglePoint = viewport * new Vector2(0.70f, 0.45f);
            Vector2 circlePoint = viewport * new Vector2(0.87f, 0.65f);
            Vector2 emptyPoint = viewport * new Vector2(0.92f, 0.30f);
            Vector2 cameraBefore = _camera.Position;
            Click(addRectangle.GetGlobalRect().GetCenter());
            Check(_editor.PlacementShape == ObstacleShape.Rectangle && _editor.Obstacles.Count == 0,
                "Create button starts placement without creating at a fixed location");
            Click(rectanglePoint);
            Check(_editor.Obstacles.Count == 1 && _editor.PlacementShape is null,
                "World click creates one rectangle and ends placement");
            var rectangle = _editor.Obstacles[0];
            Check(rectangle.Shape == ObstacleShape.Rectangle && rectangle.IsSelected
                && _editor.SelectedObstacle == rectangle && Screen(rectangle).DistanceTo(rectanglePoint) < 0.01f,
                "Created rectangle is highlighted at clicked world position");
            Check(_camera.Position == cameraBefore && !_world.IsRunning,
                "Placing does not drag the camera or resume the simulation");
            await Frame();
            Check(width.Editable && !delete.Disabled && Mathf.IsEqualApprox((float)width.Value, rectangle.Dimensions.X),
                "Selection updates editable dimensions and delete state");

            // 显式尺寸输入与鼠标缩放都修改同一个障碍物，不经过模拟配置保存。
            width.Value = 140;
            height.Value = 90;
            Check(rectangle.Dimensions == new Vector2(140, 90), "Panel dimensions resize the selected rectangle immediately");
            Vector2 dimensions = rectangle.Dimensions;
            Vector2 rectangleBefore = rectangle.Position;
            Drag(Screen(rectangle), new Vector2(34, 18));
            Check(rectangle.Position.DistanceTo(rectangleBefore + new Vector2(34, 18) / _camera.Zoom) < 0.01f,
                "Dragging body moves the obstacle in world units");
            Check(_camera.Position == cameraBefore && rectangle.Dimensions == dimensions,
                "Body drag does not move camera or resize obstacle");
            rectangleBefore = rectangle.Position;
            Drag(Screen(rectangle, rectangle.HandlePosition), new Vector2(15, 10));
            Vector2 expectedSize = dimensions + new Vector2(30, 20) / _camera.Zoom;
            Check(rectangle.Dimensions.DistanceTo(expectedSize) < 0.01f && rectangle.Position == rectangleBefore,
                "Rectangle handle independently resizes width and height around its center");
            dimensions = rectangle.Dimensions;
            Vector2 zoomBefore = _camera.Zoom;
            Wheel(Screen(rectangle), MouseButton.WheelUp, true);
            Check(rectangle.Dimensions.X > dimensions.X
                && Mathf.IsEqualApprox(rectangle.Dimensions.X / rectangle.Dimensions.Y, dimensions.X / dimensions.Y)
                && _camera.Zoom == zoomBefore,
                "Shift-wheel uniformly scales selected obstacle without zooming camera");
            dimensions = rectangle.Dimensions;
            Wheel(Screen(rectangle), MouseButton.WheelUp);
            Check(_camera.Zoom != zoomBefore && rectangle.Dimensions == dimensions,
                "Ordinary wheel still zooms the camera and preserves obstacle dimensions");
            Click(Screen(rectangle));
            Check(_editor.SelectedObstacle == rectangle && rectangle.HitResizeHandle(Screen(rectangle, rectangle.HandlePosition)),
                "Selection and resize-handle hits remain aligned after camera scaling");
            Tap(Key.Home);
            await Frame();

            Click(addCircle.GetGlobalRect().GetCenter());
            Check(_editor.PlacementShape == ObstacleShape.Circle, "Circle button starts circle placement");
            Click(circlePoint);
            var circle = _editor.SelectedObstacle!;
            Check(_editor.Obstacles.Count == 2 && circle.Shape == ObstacleShape.Circle
                && circle.IsSelected && !rectangle.IsSelected,
                "Circle placement replaces the old obstacle selection");
            await Frame();
            width.Value = 80;
            Check(circle.Dimensions == Vector2.One * 80 && !height.IsVisibleInTree(),
                "Circle diameter uses one input and keeps both dimensions equal");
            Vector2 circleBefore = circle.Position;
            Drag(Screen(circle, circle.HandlePosition), new Vector2(12, 0));
            Check(circle.Dimensions.X > 80 && circle.Dimensions.X == circle.Dimensions.Y && circle.Position == circleBefore,
                "Circle handle changes radius without making an ellipse or moving its center");

            // 参数输入框拥有 Delete 时应编辑文本，不能删除场景对象。
            var text = width.GetLineEdit();
            text.GrabFocus();
            string oldText = text.Text;
            text.CaretColumn = 0;
            Tap(Key.Delete);
            Check(_editor.SelectedObstacle == circle && _editor.Obstacles.Count == 2,
                "Delete in a dimension text field does not delete an obstacle");
            text.Text = oldText;
            text.ReleaseFocus();
            _editor.ResizeSelected(-10, -10);
            Check(circle.Dimensions == Vector2.One * Obstacle2D.MinSize, "Resize clamps at the positive minimum");
            _editor.ResizeSelected(Obstacle2D.MaxSize * 2, Obstacle2D.MaxSize * 2);
            Check(circle.Dimensions == Vector2.One * Obstacle2D.MaxSize, "Resize clamps at the maximum");
            _editor.ResizeSelected(80, 80);

            Click(emptyPoint);
            Check(_editor.SelectedObstacle is null && !circle.IsSelected && delete.Disabled,
                "Empty world click clears obstacle selection and disables deletion");
            Boid2D probe = _world.SpawnBoid(ToWorld(emptyPoint), Vector2.Zero);
            await Frame();
            Click(emptyPoint);
            Check(_world.SelectedBoid == probe && _editor.SelectedObstacle is null, "Boid selection works outside obstacles");
            Click(Screen(rectangle));
            Check(_editor.SelectedObstacle == rectangle && _world.SelectedBoid is null,
                "Selecting obstacle clears Boid selection");
            Click(emptyPoint);
            Check(_world.SelectedBoid == probe && _editor.SelectedObstacle is null,
                "Selecting Boid clears obstacle selection");

            // 已运行或暂停时都允许编辑；这里冻结物理回调，仅检查输入没有偷改运行标志。
            _world.SetPhysicsProcess(false);
            _world.SetRunning(true);
            Click(Screen(circle));
            circleBefore = circle.Position;
            Drag(Screen(circle), new Vector2(-10, -10));
            Check(circle.Position != circleBefore && _world.IsRunning, "Running simulation permits obstacle selection and dragging");
            _world.SetRunning(false);
            _world.SetPhysicsProcess(true);

            _editor.BeginPlacement(ObstacleShape.Circle);
            Tap(Key.Escape);
            Check(_editor.PlacementShape is null && _editor.Obstacles.Count == 2, "Escape cancels placement without creating an object");
            _editor.BeginPlacement(ObstacleShape.Rectangle);
            MouseButtonEvent(emptyPoint, MouseButton.Right, true);
            MouseButtonEvent(emptyPoint, MouseButton.Right, false);
            Check(_editor.PlacementShape is null && _editor.Obstacles.Count == 2, "Right-click cancels placement");

            Click(Screen(circle));
            circleBefore = circle.Position;
            Pointer(Screen(circle));
            MouseButtonEvent(Screen(circle), MouseButton.Left, true);
            Tap(Key.F2);
            await Frame();
            Pointer(Screen(circle) + new Vector2(25, 0), new Vector2(25, 0), true);
            Tap(Key.Delete);
            Tap(Key.F2);
            Pointer(Screen(circle) + new Vector2(50, 0), new Vector2(25, 0), true);
            MouseButtonEvent(Screen(circle) + new Vector2(50, 0), MouseButton.Left, false);
            Check(!help.Visible && circle.Position == circleBefore && _editor.Obstacles.Count == 2,
                "Help blocks deletion and cancels a held obstacle drag across close");
            _editor.BeginPlacement(ObstacleShape.Rectangle);
            Tap(Key.F2);
            Check(help.Visible && _editor.PlacementShape is null, "Opening help cancels unfinished placement");
            Tap(Key.Escape);
            Tap(Key.F1);
            Check(!hud.Visible, "HUD can be hidden for obstacle editing");
            Click(Screen(circle));
            GetViewport().GuiGetFocusOwner()?.ReleaseFocus();
            Tap(Key.Delete);
            Check(_editor.Obstacles.Count == 1 && _editor.SelectedObstacle is null,
                "Delete removes selected obstacle when HUD is hidden");
            Tap(Key.F1);
            await Frame();
            Click(Screen(rectangle));
            rectangleBefore = rectangle.Position;
            dimensions = rectangle.Dimensions;
            _world.Reset();
            Check(_editor.Obstacles.Count == 1 && _editor.Obstacles[0] == rectangle
                && rectangle.Position == rectangleBefore && rectangle.Dimensions == dimensions,
                "Resetting birds preserves obstacle identity, position and size");
            await Frame();
            delete.GrabFocus();
            await Frame();
            Click(delete.GetGlobalRect().GetCenter());
            Check(_editor.Obstacles.Count == 0 && _editor.SelectedObstacle is null && delete.Disabled,
                "Delete button removes selected obstacle and refreshes selection controls");
            Check(configChanges == 0, "Obstacle editing does not modify or save shared simulation configuration");
            GD.Print($"PASS: {_checks} obstacle placement/editing/input assertions (headless).");
            GetTree().Quit();
        }
        catch (Exception error)
        {
            GD.PushError(error.ToString());
            GetTree().Quit(1);
        }
    }

    private Vector2 ToWorld(Vector2 point) => _world.GetGlobalTransformWithCanvas().AffineInverse() * point;
    private static Vector2 Screen(Node2D node, Vector2 local = default) => node.GetGlobalTransformWithCanvas() * local;
    private async Task Frame()
    {
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }
    private void Tap(Key key)
    {
        foreach (bool pressed in new[] { true, false })
            GetViewport().PushInput(new InputEventKey { Keycode = key, PhysicalKeycode = key, Pressed = pressed }, true);
    }
    private void Pointer(Vector2 point, Vector2 relative = default, bool held = false) =>
        GetViewport().PushInput(new InputEventMouseMotion { Position = point, GlobalPosition = point,
            Relative = relative, ButtonMask = held ? MouseButtonMask.Left : 0 }, true);
    private void MouseButtonEvent(Vector2 point, MouseButton button, bool pressed, bool shift = false) =>
        GetViewport().PushInput(new InputEventMouseButton { Position = point, GlobalPosition = point,
            ButtonIndex = button, Pressed = pressed, ShiftPressed = shift, Factor = 1,
            ButtonMask = pressed && button == MouseButton.Left ? MouseButtonMask.Left : 0 }, true);
    private void Click(Vector2 point)
    {
        Pointer(point);
        MouseButtonEvent(point, MouseButton.Left, true);
        MouseButtonEvent(point, MouseButton.Left, false);
    }
    private void Drag(Vector2 from, Vector2 delta)
    {
        Pointer(from);
        MouseButtonEvent(from, MouseButton.Left, true);
        Pointer(from + delta, delta, true);
        MouseButtonEvent(from + delta, MouseButton.Left, false);
    }
    private void Wheel(Vector2 point, MouseButton button, bool shift = false)
    {
        Pointer(point);
        MouseButtonEvent(point, button, true, shift);
        MouseButtonEvent(point, button, false, shift);
    }
    private void Check(bool condition, string message)
    {
        _checks++;
        if (!condition) throw new InvalidOperationException(message);
    }
}
