using System;
using Godot;

namespace BoidsLab;

/// <summary>观察相机：拖动、缩放与 Home 复位；视角变化后通知世界更新可见边界。</summary>
public partial class SimulationCamera2D : Camera2D
{
    // 相对于 Home 基准视角的倍率。基准尺寸不随当前模拟边界改变。
    public const float MinZoomFactor = 0.25f;
    public const float MaxZoomFactor = 8f;
    private const float ZoomPerNotch = 1.15f;
    public const float DragThresholdPixels = 5f;

    public float ZoomFactor { get; private set; } = 1f;
    public event Action? ViewChanged;
    public event Action<Vector2>? WorldPressStarted;
    public event Action<bool>? WorldPressEnded;

    private Rect2 _homeBounds;
    private Viewport? _viewport;
    private Window? _window;
    private bool _isDragging;
    private bool _pointerDown;
    private Vector2 _pressPosition;
    private Vector2 _pendingDrag;

    public override void _Ready()
    {
        _viewport = GetViewport();
        _viewport.SizeChanged += OnViewportSizeChanged;
        _window = GetWindow();
        _window.FocusExited += StopDragging;
        ApplyZoom();
    }

    public void Configure(Rect2 homeBounds)
    {
        _homeBounds = homeBounds;
        FitWorld();
    }

    public void FitWorld()
    {
        StopDragging();
        ZoomFactor = 1f;
        Position = _homeBounds.GetCenter();
        ApplyZoom();
        ViewChanged?.Invoke();
    }

    public override void _Input(InputEvent @event)
    {
        if (!_pointerDown) return;

        // 拖动开始后接管移动与松开事件，移到 UI 上松手也能正常结束。
        if (@event is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: false } release)
        {
            bool clicked = !_isDragging && release.Position.DistanceTo(_pressPosition) < DragThresholdPixels
                && GetViewport().GuiGetHoveredControl() is null;
            EndPointerGesture(clicked);
            GetViewport().SetInputAsHandled();
        }
        else if (@event is InputEventMouseMotion motion)
        {
            if ((motion.ButtonMask & MouseButtonMask.Left) == 0)
            {
                StopDragging();
                return;
            }

            Vector2 movement = motion.Relative;
            if (!_isDragging)
            {
                _pendingDrag += motion.Relative;
                if (_pendingDrag.Length() < DragThresholdPixels)
                {
                    GetViewport().SetInputAsHandled();
                    return;
                }
                _isDragging = true;
                movement = _pendingDrag; // 越过阈值时补上累计位移，画面仍跟随鼠标。
            }
            GlobalPosition -= movement / Zoom;
            ForceUpdateScroll();
            ViewChanged?.Invoke();
            GetViewport().SetInputAsHandled();
        }
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (!_homeBounds.HasArea()) return;
        if (@event.IsActionPressed("reset_camera"))
        {
            FitWorld();
            GetViewport().SetInputAsHandled();
            return;
        }

        // 只在 UI 未处理的左键按下时开始，操作参数控件不会带动相机。
        if (@event is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true } press)
        {
            StopDragging();
            _pointerDown = true;
            _pressPosition = press.Position;
            _pendingDrag = Vector2.Zero;
            WorldPressStarted?.Invoke(press.Position);
            GetViewport().SetInputAsHandled();
            return;
        }

        // UI 先处理滚轮；只有落在世界上的滚轮事件才会来到这里。
        if (@event is not InputEventMouseButton { Pressed: true } wheel) return;
        float direction = wheel.ButtonIndex switch
        {
            MouseButton.WheelUp => 1f,
            MouseButton.WheelDown => -1f,
            _ => 0f,
        };
        if (direction == 0f) return;
        StopDragging();

        float amount = wheel.Factor > 0f ? wheel.Factor : 1f;
        float nextZoom = Mathf.Clamp(ZoomFactor * Mathf.Pow(ZoomPerNotch, direction * amount),
            MinZoomFactor, MaxZoomFactor);
        Vector2 pointerBeforeZoom = GetGlobalMousePosition();
        ZoomFactor = nextZoom;
        ApplyZoom();
        // 缩放前后鼠标指向同一个世界位置，方便观察局部群体。
        GlobalPosition += pointerBeforeZoom - GetGlobalMousePosition();
        ForceUpdateScroll();
        // 只发布鼠标中心补偿完成后的最终视角，避免一次缩放导致两次环绕。
        ViewChanged?.Invoke();
        GetViewport().SetInputAsHandled();
    }

    private void StopDragging() => EndPointerGesture(false);

    private void EndPointerGesture(bool clicked)
    {
        bool wasDown = _pointerDown;
        _pointerDown = false;
        _isDragging = false;
        _pendingDrag = Vector2.Zero;
        if (wasDown) WorldPressEnded?.Invoke(clicked);
    }

    private void OnViewportSizeChanged()
    {
        StopDragging();
        ApplyZoom();
        ViewChanged?.Invoke();
    }

    private void ApplyZoom()
    {
        if (_viewport is null || !_homeBounds.HasArea()) return;
        Vector2 viewportSize = _viewport.GetVisibleRect().Size;
        if (viewportSize.X <= 0f || viewportSize.Y <= 0f) return;
        float fit = Mathf.Min(viewportSize.X / _homeBounds.Size.X,
            viewportSize.Y / _homeBounds.Size.Y);
        // 视口尺寸变化时保留用户倍率和观察位置，不自动重置到全景。
        Zoom = Vector2.One * fit * ZoomFactor;
        ForceUpdateScroll();
    }

    public override void _ExitTree()
    {
        StopDragging();
        if (_window is not null) _window.FocusExited -= StopDragging;
        if (_viewport is not null) _viewport.SizeChanged -= OnViewportSizeChanged;
    }
}
