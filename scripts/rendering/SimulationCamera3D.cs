using System;
using Godot;

namespace BoidsLab;

/// <summary>三维观察相机：右键环绕、中键平移、滚轮缩放。左键只发出选择手势。</summary>
public partial class SimulationCamera3D : Camera3D
{
    public const float MinZoomFactor = 0.25f;
    public const float MaxZoomFactor = 8f;
    public const float DragThresholdPixels = 5f;
    private const float ZoomPerNotch = 1.15f;
    private const float OrbitRadiansPerPixel = 0.006f;
    private const float PitchLimit = 85f * Mathf.Pi / 180f;

    public float ZoomFactor { get; private set; } = 1f;
    public Vector3 OrbitCenter { get; private set; }
    public event Action<Vector2>? WorldPressStarted;
    public event Action<bool>? WorldPressEnded;
    public event Action? ViewChanged;
    public event Action? ManualViewChanged;
    public event Action<float>? FollowDistanceRequested;
    public bool IsFollowing => _leader is not null;
    public bool IsFollowFrozen => _followFrozen;
    private Boid3D? _leader;
    private LeaderController3D? _leaderController;
    private BoidConfig? _followConfig;
    private bool _followFrozen;

    private Aabb _homeBounds;
    private float _distance;
    private float _referenceSize;
    private float _yaw;
    private float _pitch;
    private bool _configured;
    private bool _interactionEnabled = true;
    private MouseButton _gesture;
    private Vector2 _pressPosition;
    private Vector2 _pendingMovement;
    private bool _dragged;
    private Viewport? _viewport;
    private Window? _window;

    public override void _Ready()
    {
        _viewport = GetViewport();
        _window = GetWindow();
        _viewport.SizeChanged += OnViewportChanged;
        _window.FocusExited += CancelGesture;
        _window.MouseExited += CancelGesture;
    }

    public void StartFollowing(Boid3D leader, LeaderController3D controller, BoidConfig config)
    {
        CancelGesture();
        _leader = leader;
        _leaderController = controller;
        _followConfig = config;
        _followFrozen = false;
        Projection = ProjectionType.Perspective;
        Fov = 70;
        ApplyFollow(0, true);
        ManualViewChanged?.Invoke();
    }

    public void SetFollowFrozen(bool frozen) => _followFrozen = frozen;

    public override void _Process(double delta)
    {
        if (IsFollowing && !_followFrozen) ApplyFollow((float)delta, false);
    }

    private void ApplyFollow(float dt, bool snap)
    {
        if (_leader is null || _leaderController is null || _followConfig is null) return;
        float distance = _followConfig.FollowCameraDistance;
        Quaternion target = _leaderController.TargetOrientation;
        Vector3 up = target * Vector3.Up;
        OrbitCenter = GetParent<Node3D>().ToLocal(_leader.DisplayGlobalTransform.Origin);
        Vector3 desired = OrbitCenter - _leaderController.Forward * distance + up * distance * 0.15f;
        float blend = snap ? 1f : 1f - Mathf.Exp(-8f * dt);
        Position = Position.Lerp(desired, blend);
        Quaternion = Quaternion.Slerp(target, blend).Normalized();
        Far = Mathf.Max(5000, distance * 4);
        ViewChanged?.Invoke();
    }

    public void AdjustFollowDistance(float notches)
    {
        if (_followConfig is null || !IsFollowing) return;
        CancelGesture();
        FollowDistanceRequested?.Invoke((float)BoidConfig.FollowCameraDistanceRange.Constrain(
            _followConfig.FollowCameraDistance / Mathf.Pow(ZoomPerNotch, notches)));
        ManualViewChanged?.Invoke();
    }

    public void Configure(Aabb homeBounds)
    {
        if (!homeBounds.Position.IsFinite() || !homeBounds.Size.IsFinite()
            || homeBounds.Size.X <= 0 || homeBounds.Size.Y <= 0 || homeBounds.Size.Z <= 0)
            throw new ArgumentOutOfRangeException(nameof(homeBounds));
        _homeBounds = homeBounds;
        _configured = true;
        _distance = homeBounds.Size.Length() * 1.5f;
        _referenceSize = homeBounds.Size.Length();
        Projection = ProjectionType.Orthogonal;
        KeepAspect = KeepAspectEnum.Height;
        Far = _distance + homeBounds.Size.Length() * 2f;
        FitWorld();
    }

    public void UpdateHomeBounds(Aabb bounds)
    {
        _homeBounds = bounds;
        if (!IsFollowing) Far = Mathf.Max(Far, Position.DistanceTo(bounds.GetCenter()) + bounds.Size.Length() * 2f);
    }

    public float UnitsPerPixelAt(Vector3 position)
    {
        float depth = Mathf.Max(Near, (ToGlobal(Vector3.Zero) - GetParent<Node3D>().ToGlobal(position)).Dot(GlobalBasis.Z));
        return Projection == ProjectionType.Orthogonal
            ? Size / Mathf.Max(1, GetViewport().GetVisibleRect().Size.Y)
            : 2 * depth * Mathf.Tan(Mathf.DegToRad(Fov * 0.5f)) / Mathf.Max(1, GetViewport().GetVisibleRect().Size.Y);
    }

    public void FitWorld()
    {
        if (!_configured) return;
        CancelGesture();
        if (IsFollowing)
        {
            FollowDistanceRequested?.Invoke((float)BoidConfig.FollowCameraDistanceRange.Default);
            ApplyFollow(0, true);
            ManualViewChanged?.Invoke();
            return;
        }
        _distance = _homeBounds.Size.Length() * 1.5f;
        Far = _distance + _homeBounds.Size.Length() * 2f;
        _referenceSize = _homeBounds.Size.Length();
        OrbitCenter = _homeBounds.GetCenter();
        ZoomFactor = 1f;
        Vector3 offset = new Vector3(1f, 0.75f, 1.2f).Normalized();
        _yaw = Mathf.Atan2(offset.X, offset.Z);
        _pitch = Mathf.Asin(offset.Y);
        ApplyView();
        ManualViewChanged?.Invoke();
    }

    public void RecenterObservation(Vector3 center)
    {
        _leader = null;
        _leaderController = null;
        _followConfig = null;
        _followFrozen = false;
        Projection = ProjectionType.Orthogonal;
        OrbitCenter = center;
        Far = _distance + _homeBounds.Size.Length() * 2f;
        ApplyView();
        ManualViewChanged?.Invoke();
    }

    public void SetInteractionEnabled(bool enabled)
    {
        CancelGesture();
        _interactionEnabled = enabled;
        SetProcessUnhandledInput(enabled);
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (!_interactionEnabled || !_configured) return;
        if (@event.IsActionPressed("reset_camera"))
        {
            FitWorld();
            GetViewport().SetInputAsHandled();
            return;
        }
        if (@event is not InputEventMouseButton { Pressed: true } button) return;
        if (button.ButtonIndex is MouseButton.Left or MouseButton.Right or MouseButton.Middle)
        {
            if (IsFollowing && button.ButtonIndex == MouseButton.Middle) return;
            CancelGesture();
            _gesture = button.ButtonIndex;
            _pressPosition = button.Position;
            if (_gesture == MouseButton.Left) WorldPressStarted?.Invoke(button.Position);
            GetViewport().SetInputAsHandled();
            return;
        }
        float direction = button.ButtonIndex switch
        {
            MouseButton.WheelUp => 1f,
            MouseButton.WheelDown => -1f,
            _ => 0f,
        };
        if (direction == 0f) return;
        if (IsFollowing)
        {
            AdjustFollowDistance(direction * (button.Factor > 0 ? button.Factor : 1));
            GetViewport().SetInputAsHandled();
            return;
        }
        CancelGesture();
        Vector3 before = ToParentPoint(ProjectPosition(button.Position, _distance));
        float amount = button.Factor > 0 ? button.Factor : 1;
        ZoomFactor = Mathf.Clamp(ZoomFactor * Mathf.Pow(ZoomPerNotch, direction * amount), MinZoomFactor, MaxZoomFactor);
        ApplySize();
        // 鼠标在观察中心平面上的投影保持不动。
        OrbitCenter += before - ToParentPoint(ProjectPosition(button.Position, _distance));
        ApplyView();
        ManualViewChanged?.Invoke();
        GetViewport().SetInputAsHandled();
    }

    public override void _Input(InputEvent @event)
    {
        if (!_interactionEnabled || _gesture == MouseButton.None) return;
        if (@event is InputEventMouseButton button)
        {
            if (!button.Pressed && button.ButtonIndex == _gesture)
            {
                bool clicked = _gesture == MouseButton.Left && !_dragged
                    && button.Position.DistanceTo(_pressPosition) < DragThresholdPixels
                    && GetViewport().GetVisibleRect().HasPoint(button.Position)
                    && GetViewport().GuiGetHoveredControl() is null;
                EndGesture(clicked);
                GetViewport().SetInputAsHandled();
            }
            else if (button.Pressed) CancelGesture();
            return;
        }
        if (@event is not InputEventMouseMotion motion) return;
        MouseButtonMask required = _gesture switch
        {
            MouseButton.Left => MouseButtonMask.Left,
            MouseButton.Right => MouseButtonMask.Right,
            _ => MouseButtonMask.Middle,
        };
        if ((motion.ButtonMask & required) == 0)
        {
            CancelGesture();
            return;
        }
        Vector2 movement = motion.Relative;
        if (!_dragged)
        {
            _pendingMovement += movement;
            if (_pendingMovement.Length() < DragThresholdPixels)
            {
                GetViewport().SetInputAsHandled();
                return;
            }
            _dragged = true;
            movement = _pendingMovement;
        }
        if (_gesture == MouseButton.Right)
        {
            if (IsFollowing) _leaderController!.RotateView(movement);
            else
            {
                _yaw = Mathf.Wrap(_yaw - movement.X * OrbitRadiansPerPixel, -Mathf.Pi, Mathf.Pi);
                _pitch = Mathf.Clamp(_pitch + movement.Y * OrbitRadiansPerPixel, -PitchLimit, PitchLimit);
                ApplyView();
            }
            ManualViewChanged?.Invoke();
        }
        else if (_gesture == MouseButton.Middle)
        {
            OrbitCenter += ToParentPoint(ProjectPosition(motion.Position - movement, _distance))
                - ToParentPoint(ProjectPosition(motion.Position, _distance));
            ApplyView();
            ManualViewChanged?.Invoke();
        }
        GetViewport().SetInputAsHandled();
    }

    public void CancelGesture() => EndGesture(false);

    private void EndGesture(bool clicked)
    {
        bool wasSelection = _gesture == MouseButton.Left;
        _gesture = MouseButton.None;
        _pendingMovement = Vector2.Zero;
        _dragged = false;
        if (wasSelection) WorldPressEnded?.Invoke(clicked);
    }

    private Vector3 ToParentPoint(Vector3 globalPoint) =>
        GetParent() is Node3D parent ? parent.ToLocal(globalPoint) : globalPoint;

    private void OnViewportChanged()
    {
        CancelGesture();
        if (_configured && !IsFollowing) ApplyView();
        ManualViewChanged?.Invoke();
    }

    private void ApplySize()
    {
        Vector2 viewportSize = GetViewport().GetVisibleRect().Size;
        float aspect = viewportSize.X / Mathf.Max(1f, viewportSize.Y);
        Size = _referenceSize * 1.1f / (ZoomFactor * Mathf.Min(1f, aspect));
    }

    private void ApplyView()
    {
        Vector3 offset = new(Mathf.Sin(_yaw) * Mathf.Cos(_pitch), Mathf.Sin(_pitch), Mathf.Cos(_yaw) * Mathf.Cos(_pitch));
        Position = OrbitCenter + offset * _distance;
        Basis = Basis.LookingAt(-offset, Vector3.Up);
        ApplySize();
        ViewChanged?.Invoke();
    }

    public override void _ExitTree()
    {
        CancelGesture();
        if (_viewport is not null) _viewport.SizeChanged -= OnViewportChanged;
        if (_window is not null)
        {
            _window.FocusExited -= CancelGesture;
            _window.MouseExited -= CancelGesture;
        }
    }
}
