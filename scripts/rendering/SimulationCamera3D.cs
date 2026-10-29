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

    private Aabb _homeBounds;
    private float _distance;
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

    public void Configure(Aabb homeBounds)
    {
        if (!homeBounds.Position.IsFinite() || !homeBounds.Size.IsFinite()
            || homeBounds.Size.X <= 0 || homeBounds.Size.Y <= 0 || homeBounds.Size.Z <= 0)
            throw new ArgumentOutOfRangeException(nameof(homeBounds));
        _homeBounds = homeBounds;
        _configured = true;
        _distance = homeBounds.Size.Length() * 1.5f;
        Projection = ProjectionType.Orthogonal;
        KeepAspect = KeepAspectEnum.Height;
        Far = _distance + homeBounds.Size.Length() * 2f;
        FitWorld();
    }

    public void FitWorld()
    {
        if (!_configured) return;
        CancelGesture();
        OrbitCenter = _homeBounds.GetCenter();
        ZoomFactor = 1f;
        Vector3 offset = new Vector3(1f, 0.75f, 1.2f).Normalized();
        _yaw = Mathf.Atan2(offset.X, offset.Z);
        _pitch = Mathf.Asin(offset.Y);
        ApplyView();
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
        CancelGesture();
        Vector3 before = ToParentPoint(ProjectPosition(button.Position, _distance));
        float amount = button.Factor > 0 ? button.Factor : 1;
        ZoomFactor = Mathf.Clamp(ZoomFactor * Mathf.Pow(ZoomPerNotch, direction * amount), MinZoomFactor, MaxZoomFactor);
        ApplySize();
        // 鼠标在观察中心平面上的投影保持不动。
        OrbitCenter += before - ToParentPoint(ProjectPosition(button.Position, _distance));
        ApplyView();
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
            _yaw = Mathf.Wrap(_yaw - movement.X * OrbitRadiansPerPixel, -Mathf.Pi, Mathf.Pi);
            _pitch = Mathf.Clamp(_pitch + movement.Y * OrbitRadiansPerPixel, -PitchLimit, PitchLimit);
            ApplyView();
        }
        else if (_gesture == MouseButton.Middle)
        {
            OrbitCenter += ToParentPoint(ProjectPosition(motion.Position - movement, _distance))
                - ToParentPoint(ProjectPosition(motion.Position, _distance));
            ApplyView();
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
        if (_configured) ApplyView();
    }

    private void ApplySize()
    {
        Vector2 viewportSize = GetViewport().GetVisibleRect().Size;
        float aspect = viewportSize.X / Mathf.Max(1f, viewportSize.Y);
        Size = _homeBounds.Size.Length() * 1.1f / (ZoomFactor * Mathf.Min(1f, aspect));
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
