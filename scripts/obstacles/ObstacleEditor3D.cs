using System;
using System.Collections.Generic;
using Godot;

namespace BoidsLab;

/// <summary>三维障碍物编辑：原生节点显示，射线拾取，沿穿过对象的视角平面拖动。</summary>
public partial class ObstacleEditor3D : Node3D
{
    public IReadOnlyList<Obstacle3D> Obstacles { get; }
    public Obstacle3D? SelectedObstacle { get; private set; }
    public ObstacleShape? PlacementShape { get; private set; }
    public ObstacleSelection? SelectionInfo => SelectedObstacle is { } selected
        ? new(selected.Shape, selected.Dimensions.X, selected.Dimensions.Y, selected.Dimensions.Z) : null;
    public event Action? Changed;
    public event Action? GeometryChanged;
    private readonly List<Obstacle3D> _obstacles = new();
    private BoidManager3D _world = null!;
    private SimulationCamera3D _camera = null!;
    private Node3D _container = null!;
    private Obstacle3D? _preview;
    private Obstacle3D? _dragTarget;
    private Vector3 _planePoint, _planeNormal, _startPointer, _startPosition;
    private Vector2 _pressScreen;
    private bool _dragged, _swallowRelease;
    private bool _interactionEnabled = true;
    private int _nextId;

    public ObstacleEditor3D() => Obstacles = _obstacles.AsReadOnly();

    public override void _Ready()
    {
        GetWindow().FocusExited += CancelGesture;
        GetWindow().MouseExited += CancelGesture;
        GetViewport().SizeChanged += CancelGesture;
    }

    public void Initialize(BoidManager3D world, SimulationCamera3D camera, Node3D container)
    {
        _world = world;
        _camera = camera;
        _container = container;
        _camera.ViewChanged += OnViewChanged;
    }

    public void BeginPlacement(ObstacleShape shape)
    {
        if (!_interactionEnabled) return;
        if (shape is not (ObstacleShape.Box or ObstacleShape.Sphere)) throw new ArgumentOutOfRangeException(nameof(shape));
        CancelGesture();
        CancelPlacement();
        _camera.CancelGesture();
        ClearSelection();
        _world.ClearBoidSelection();
        PlacementShape = shape;
        _preview = new Obstacle3D { Name = "PlacementPreview" };
        _preview.Initialize(shape, DefaultSize(shape));
        _preview.SetPreview(true);
        _container.AddChild(_preview);
        UpdatePreview(GetViewport().GetMousePosition());
        Changed?.Invoke();
    }

    public void CancelPlacement()
    {
        if (PlacementShape is null) return;
        PlacementShape = null;
        if (_preview is not null)
        {
            _container.RemoveChild(_preview);
            _preview.QueueFree();
            _preview = null;
        }
        Changed?.Invoke();
    }

    public Obstacle3D AddObstacle(ObstacleShape shape, Vector3 position)
    {
        if (!position.IsFinite()) throw new ArgumentOutOfRangeException(nameof(position));
        var obstacle = new Obstacle3D { Name = $"Obstacle_{_nextId++}", Position = ClampCenter(position) };
        obstacle.Initialize(shape, DefaultSize(shape));
        _container.AddChild(obstacle);
        _obstacles.Add(obstacle);
        CancelPlacement();
        Select(obstacle);
        GeometryChanged?.Invoke();
        return obstacle;
    }

    public void ClearSelection() => Select(null);

    private void Select(Obstacle3D? obstacle)
    {
        if (SelectedObstacle == obstacle) return;
        SelectedObstacle?.SetSelected(false);
        SelectedObstacle = obstacle;
        SelectedObstacle?.SetSelected(true);
        if (obstacle is not null) _world.ClearBoidSelection();
        Changed?.Invoke();
    }

    public void DeleteSelected()
    {
        if (!_interactionEnabled || SelectedObstacle is not { } obstacle) return;
        CancelGesture();
        Select(null);
        _obstacles.Remove(obstacle);
        _container.RemoveChild(obstacle);
        obstacle.QueueFree();
        GeometryChanged?.Invoke();
    }

    public void ResizeSelected(float width, float height, float depth)
    {
        if (!_interactionEnabled || SelectedObstacle is not { } obstacle) return;
        obstacle.SetDimensions(new Vector3(width, height, depth));
        GeometryChanged?.Invoke();
        Changed?.Invoke();
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

    public override void _Input(InputEvent @event)
    {
        if (@event is InputEventMouseMotion preview && PlacementShape is not null) UpdatePreview(preview.Position);
        if (@event is InputEventMouseButton button)
        {
            if (button.ButtonIndex == MouseButton.Left && !button.Pressed && (_dragTarget is not null || _swallowRelease))
            {
                CancelGesture();
                GetViewport().SetInputAsHandled();
            }
            else if (button.Pressed && button.ButtonIndex != MouseButton.Left) CancelGesture();
            return;
        }
        if (_dragTarget is not { } target || @event is not InputEventMouseMotion motion) return;
        if ((motion.ButtonMask & MouseButtonMask.Left) == 0) { CancelGesture(); return; }
        if (!_dragged && motion.Position.DistanceTo(_pressScreen) >= SimulationCamera3D.DragThresholdPixels) _dragged = true;
        if (_dragged)
        {
            Vector3 pointer = PointOnPlane(motion.Position, _planePoint, _planeNormal);
            target.Position = ClampCenter(_startPosition + _container.ToLocal(pointer) - _container.ToLocal(_startPointer));
            GeometryChanged?.Invoke();
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
            && mouse.ShiftPressed && SelectedObstacle is { } selected && Pick(mouse.Position, out _) == selected)
        {
            CancelGesture();
            float amount = mouse.Factor > 0 ? mouse.Factor : 1;
            float factor = Mathf.Pow(1.1f, (mouse.ButtonIndex == MouseButton.WheelUp ? 1 : -1) * amount);
            Vector3 size = selected.Dimensions;
            factor = Mathf.Clamp(factor, ObstacleSize.Min / Mathf.Min(size.X, Mathf.Min(size.Y, size.Z)),
                ObstacleSize.Max / Mathf.Max(size.X, Mathf.Max(size.Y, size.Z)));
            selected.SetDimensions(size * factor);
            GeometryChanged?.Invoke();
            Changed?.Invoke();
            GetViewport().SetInputAsHandled();
            return;
        }
        if (mouse.ButtonIndex != MouseButton.Left) return;
        if (PlacementShape is { } shape)
        {
            AddObstacle(shape, PlacementPoint(mouse.Position));
            _swallowRelease = true;
            GetViewport().SetInputAsHandled();
            return;
        }
        Obstacle3D? hit = Pick(mouse.Position, out float distance);
        if (hit is null) return;
        // 尊重三维遮挡：前方的 Boid 仍能被选择，不让后方障碍物抢走点击。
        if (_world.PickBoid(mouse.Position, out float boidDistance) is not null && boidDistance < distance) return;
        _camera.CancelGesture();
        Select(hit);
        _dragTarget = hit;
        _dragged = false;
        _pressScreen = mouse.Position;
        _startPosition = hit.Position;
        _planePoint = hit.GlobalPosition;
        _planeNormal = _camera.GlobalBasis.Z;
        _startPointer = PointOnPlane(mouse.Position, _planePoint, _planeNormal);
        GetViewport().SetInputAsHandled();
    }

    public override void _UnhandledKeyInput(InputEvent @event)
    {
        if (!@event.IsActionPressed("delete_obstacle") || SelectedObstacle is null
            || GetViewport().GuiGetFocusOwner() is LineEdit or TextEdit) return;
        DeleteSelected();
        GetViewport().SetInputAsHandled();
    }

    private Obstacle3D? Pick(Vector2 screen, out float closest)
    {
        closest = float.PositiveInfinity;
        Obstacle3D? target = null;
        Vector3 origin = _container.ToLocal(_camera.ProjectRayOrigin(screen));
        Vector3 direction = (_container.GlobalBasis.Inverse() * _camera.ProjectRayNormal(screen)).Normalized();
        foreach (Obstacle3D obstacle in _obstacles)
        {
            if (ObstacleAvoidance3D.HitDistance(origin, direction, obstacle, _camera.Far) is not { } distance) continue;
            if (distance > closest || (distance == closest && obstacle != SelectedObstacle)) continue;
            target = obstacle;
            closest = distance;
        }
        return target;
    }

    private Vector3 PlacementPoint(Vector2 screen) => ClampCenter(_container.ToLocal(PointOnPlane(screen,
        _camera.GetParent<Node3D>().ToGlobal(_camera.OrbitCenter), _camera.GlobalBasis.Z)));

    private Vector3 PointOnPlane(Vector2 screen, Vector3 point, Vector3 normal)
    {
        Vector3 origin = _camera.ProjectRayOrigin(screen);
        Vector3 direction = _camera.ProjectRayNormal(screen);
        float denominator = direction.Dot(normal);
        return Mathf.Abs(denominator) < 0.000001f ? point : origin + direction * ((point - origin).Dot(normal) / denominator);
    }

    private Vector3 ClampCenter(Vector3 position) => position.Clamp(_world.WorldBounds.Position, _world.WorldBounds.End - Vector3.One * 0.001f);
    private static Vector3 DefaultSize(ObstacleShape shape) => shape == ObstacleShape.Sphere ? Obstacle3D.DefaultSphereSize : Obstacle3D.DefaultBoxSize;
    private void UpdatePreview(Vector2 screen) { if (_preview is not null) _preview.Position = PlacementPoint(screen); }
    private void OnViewChanged() { CancelGesture(); UpdatePreview(GetViewport().GetMousePosition()); }
    public void CancelGesture() { _dragTarget = null; _dragged = false; _swallowRelease = false; }

    public override void _ExitTree()
    {
        GetWindow().FocusExited -= CancelGesture;
        GetWindow().MouseExited -= CancelGesture;
        GetViewport().SizeChanged -= CancelGesture;
        if (_camera is not null) _camera.ViewChanged -= OnViewChanged;
    }
}
