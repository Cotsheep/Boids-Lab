using System;
using System.Collections.Generic;
using Godot;

namespace BoidsLab;

/// <summary>场景中的障碍物与编辑输入；不更新 Boid，不依赖 HUD 的具体控件。</summary>
public partial class ObstacleEditor2D : Node2D
{
    public event Action? Changed;
    public event Action? GeometryChanged;
    public Obstacle2D? SelectedObstacle { get; private set; }
    public ObstacleShape? PlacementShape { get; private set; }
    public IReadOnlyList<Obstacle2D> Obstacles { get; }
    public bool IsEditing => PlacementShape is not null || _dragTarget is not null || _swallowPlacementRelease;
    public ObstacleSelection? SelectionInfo => SelectedObstacle is { } selected
        ? new ObstacleSelection(selected.Shape, selected.Dimensions.X, selected.Dimensions.Y) : null;

    private readonly List<Obstacle2D> _obstacles = new();
    private BoidManager _world = null!;
    private SimulationCamera2D _camera = null!;
    private Node2D _container = null!;
    private Obstacle2D? _dragTarget;
    private bool _resizing;
    private bool _dragging;
    private Vector2 _pressScreen;
    private Vector2 _pressWorld;
    private Vector2 _startPosition;
    private Vector2 _startDimensions;
    private Vector2 _previewPosition;
    private bool _interactionEnabled = true;
    private bool _swallowPlacementRelease;
    private int _nextId;

    public ObstacleEditor2D() => Obstacles = _obstacles.AsReadOnly();

    public void Initialize(BoidManager world, SimulationCamera2D camera, Node2D container)
    {
        _world = world;
        _camera = camera;
        _container = container;
    }

    public override void _Ready()
    {
        GetWindow().FocusExited += CancelGesture;
        GetViewport().SizeChanged += CancelGesture;
    }

    public void BeginPlacement(ObstacleShape shape)
    {
        if (!_interactionEnabled) return;
        if (!Enum.IsDefined(shape)) throw new ArgumentOutOfRangeException(nameof(shape));
        CancelGesture();
        _camera.CancelPointerGesture();
        ClearSelection();
        _world.ClearBoidSelection();
        PlacementShape = shape;
        _previewPosition = ToWorld(GetViewport().GetMousePosition());
        NotifyChanged();
    }

    public void CancelPlacement()
    {
        if (PlacementShape is null) return;
        PlacementShape = null;
        NotifyChanged();
    }

    public Obstacle2D AddObstacle(ObstacleShape shape, Vector2 position)
    {
        if (!position.IsFinite()) throw new ArgumentOutOfRangeException(nameof(position));
        var obstacle = new Obstacle2D { Name = $"Obstacle_{_nextId++}", Position = position };
        obstacle.Initialize(shape, shape == ObstacleShape.Circle
            ? Obstacle2D.DefaultCircleSize : Obstacle2D.DefaultRectangleSize);
        _container.AddChild(obstacle);
        _obstacles.Add(obstacle);
        PlacementShape = null;
        Select(obstacle);
        GeometryChanged?.Invoke();
        return obstacle;
    }

    public void ClearSelection() => Select(null);

    private void Select(Obstacle2D? obstacle)
    {
        if (SelectedObstacle == obstacle) return;
        SelectedObstacle?.SetSelected(false);
        SelectedObstacle = obstacle;
        SelectedObstacle?.SetSelected(true);
        if (obstacle is not null) _world.ClearBoidSelection();
        NotifyChanged();
    }

    public void DeleteSelected()
    {
        if (!_interactionEnabled || SelectedObstacle is not { } obstacle) return;
        CancelGesture();
        _obstacles.Remove(obstacle);
        Select(null);
        _container.RemoveChild(obstacle);
        obstacle.QueueFree();
        GeometryChanged?.Invoke();
    }

    public void ResizeSelected(float width, float height)
    {
        if (!_interactionEnabled || SelectedObstacle is not { } obstacle) return;
        obstacle.SetDimensions(new Vector2(width, height));
        NotifyChanged(true);
    }

    public void SetInteractionEnabled(bool enabled)
    {
        _interactionEnabled = enabled;
        CancelGesture();
        if (!enabled) CancelPlacement();
        SetProcessInput(enabled);
        SetProcessUnhandledInput(enabled);
        SetProcessUnhandledKeyInput(enabled);
    }

    public void RefreshVisuals()
    {
        CancelGesture();
        foreach (Obstacle2D obstacle in _obstacles) obstacle.RefreshVisual();
        QueueRedraw();
    }

    public override void _Input(InputEvent @event)
    {
        // 编辑手势会消费事件，仍记录光标以便结束编辑后恢复连续跟随。
        _camera.ObservePointer(@event);
        if (@event is InputEventMouseMotion preview && PlacementShape is not null)
        {
            _previewPosition = ToWorld(preview.Position);
            QueueRedraw();
        }
        if (@event is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: false })
        {
            if (_dragTarget is null && !_swallowPlacementRelease) return;
            CancelGesture();
            GetViewport().SetInputAsHandled();
            return;
        }
        if (_dragTarget is not { } target || @event is not InputEventMouseMotion motion) return;
        if ((motion.ButtonMask & MouseButtonMask.Left) == 0)
        {
            CancelGesture();
            return;
        }
        if (!_dragging && motion.Position.DistanceTo(_pressScreen) >= SimulationCamera2D.DragThresholdPixels)
            _dragging = true;
        if (_dragging)
        {
            Vector2 pointer = ToWorld(motion.Position);
            if (_resizing)
            {
                Vector2 dimensions = target.Shape == ObstacleShape.Circle
                    ? Vector2.One * (pointer - _startPosition).Length() * 2
                    : _startDimensions + (pointer - _pressWorld) * 2;
                target.SetDimensions(dimensions);
            }
            else target.Position = _startPosition + pointer - _pressWorld;
            NotifyChanged(true);
        }
        GetViewport().SetInputAsHandled();
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is not InputEventMouseButton { Pressed: true } mouse) return;
        if (mouse.ButtonIndex == MouseButton.Right && PlacementShape is not null)
        {
            CancelPlacement();
            GetViewport().SetInputAsHandled();
            return;
        }
        if (mouse.ButtonIndex is MouseButton.WheelUp or MouseButton.WheelDown
            && mouse.ShiftPressed && SelectedObstacle is { } selected
            && (selected.HitTest(mouse.Position) || selected.HitResizeHandle(mouse.Position)))
        {
            CancelGesture();
            float factor = Mathf.Pow(1.1f, (mouse.ButtonIndex == MouseButton.WheelUp ? 1 : -1)
                * (mouse.Factor > 0 ? mouse.Factor : 1));
            // 一致缩放到最先到达的尺寸边界，长方形保持原来的宽高比。
            Vector2 size = selected.Dimensions;
            factor = Mathf.Clamp(factor, Obstacle2D.MinSize / Mathf.Min(size.X, size.Y),
                Obstacle2D.MaxSize / Mathf.Max(size.X, size.Y));
            selected.SetDimensions(size * factor);
            NotifyChanged(true);
            GetViewport().SetInputAsHandled();
            return;
        }
        if (mouse.ButtonIndex != MouseButton.Left) return;
        if (PlacementShape is { } shape)
        {
            AddObstacle(shape, ToWorld(mouse.Position));
            _swallowPlacementRelease = true;
            GetViewport().SetInputAsHandled();
            return;
        }
        Obstacle2D? hit = null;
        bool handle = SelectedObstacle?.HitResizeHandle(mouse.Position) == true;
        if (handle) hit = SelectedObstacle;
        else
            for (int i = _obstacles.Count - 1; i >= 0; i--)
                if (_obstacles[i].HitTest(mouse.Position)) { hit = _obstacles[i]; break; }
        if (hit is null) return; // 世界空白与 Boid 点击继续交给原来的相机/选择流程。
        _camera.CancelPointerGesture();
        Select(hit);
        _dragTarget = hit;
        _resizing = handle;
        _dragging = false;
        _pressScreen = mouse.Position;
        _pressWorld = ToWorld(mouse.Position);
        _startPosition = hit.Position;
        _startDimensions = hit.Dimensions;
        GetViewport().SetInputAsHandled();
    }

    public override void _UnhandledKeyInput(InputEvent @event)
    {
        // 文本框先处理 Delete，编辑数字不会误删障碍物。
        if (!@event.IsActionPressed("delete_obstacle")) return;
        if (GetViewport().GuiGetFocusOwner() is LineEdit or TextEdit) return;
        if (SelectedObstacle is null) return;
        DeleteSelected();
        GetViewport().SetInputAsHandled();
    }

    public void CancelGesture()
    {
        _dragTarget = null;
        _dragging = false;
        _swallowPlacementRelease = false;
    }

    private Vector2 ToWorld(Vector2 screen) => GetGlobalTransformWithCanvas().AffineInverse() * screen;
    private void NotifyChanged(bool geometryChanged = false)
    {
        QueueRedraw();
        if (geometryChanged) GeometryChanged?.Invoke();
        Changed?.Invoke();
    }

    public override void _Draw()
    {
        if (PlacementShape is not { } shape) return;
        float unitsPerPixel = 1 / Mathf.Max(0.001f, GetGlobalTransformWithCanvas().X.Length());
        var color = new Color(0.45f, 0.85f, 1, 0.7f);
        if (shape == ObstacleShape.Circle)
            DrawArc(_previewPosition, Obstacle2D.DefaultCircleSize.X / 2, 0, Mathf.Tau, 64,
                color, 2 * unitsPerPixel, true);
        else DrawRect(new Rect2(_previewPosition - Obstacle2D.DefaultRectangleSize / 2,
            Obstacle2D.DefaultRectangleSize), color, false, 2 * unitsPerPixel);
    }

    public override void _ExitTree()
    {
        GetWindow().FocusExited -= CancelGesture;
        GetViewport().SizeChanged -= CancelGesture;
    }
}
