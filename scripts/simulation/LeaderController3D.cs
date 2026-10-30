using System;
using System.Collections.Generic;
using Godot;

namespace BoidsLab;

/// <summary>只保存飞行输入意图。个体运动仍由管理器的固定时间步推进。</summary>
public partial class LeaderController3D : Node
{
    private const float Sensitivity = 0.004f;
    private const float PitchLimit = 85f * Mathf.Pi / 180f;
    private readonly HashSet<string> _held = new();
    private static readonly string[] MoveActions = { "leader_forward", "leader_backward", "leader_left", "leader_right" };
    private Boid3D? _leader;
    private SimulationCamera3D _camera = null!;
    private Window? _window;
    private float _yaw, _pitch;
    public bool Active { get; private set; }
    public event Action? ActiveChanged;
    public Vector3 Forward => new(Mathf.Sin(_yaw) * Mathf.Cos(_pitch), Mathf.Sin(_pitch), -Mathf.Cos(_yaw) * Mathf.Cos(_pitch));
    // 目标视角只驱动相机与 WASD 参考系，个体朝向由实际速度决定。
    public Quaternion TargetOrientation => Basis.LookingAt(Forward, Vector3.Up).GetRotationQuaternion();
    public Vector3 MovementDirection
    {
        get
        {
            if (!Active || GetViewport().GuiGetFocusOwner() is LineEdit or TextEdit) return Vector3.Zero;
            float forward = (_held.Contains("leader_forward") ? 1 : 0) - (_held.Contains("leader_backward") ? 1 : 0);
            float right = (_held.Contains("leader_right") ? 1 : 0) - (_held.Contains("leader_left") ? 1 : 0);
            return (Forward * forward + Forward.Cross(Vector3.Up).Normalized() * right).LimitLength(1);
        }
    }

    public void Initialize(SimulationCamera3D camera) => _camera = camera;

    public override void _Ready()
    {
        _window = GetWindow();
        _window.FocusExited += Release;
    }

    public void AssignLeader(Boid3D? leader)
    {
        SetActive(false);
        _leader = leader;
        if (leader is null) return;
        Vector3 forward = leader.Quaternion * Vector3.Forward;
        _yaw = Mathf.Atan2(forward.X, -forward.Z);
        _pitch = Mathf.Clamp(Mathf.Asin(Mathf.Clamp(forward.Y, -1, 1)), -PitchLimit, PitchLimit);
    }

    public void SetActive(bool active)
    {
        active &= _leader is not null;
        if (Active == active) return;
        _held.Clear();
        Active = active;
        Input.MouseMode = active ? Input.MouseModeEnum.Captured : Input.MouseModeEnum.Visible;
        ActiveChanged?.Invoke();
    }

    private void Release() => SetActive(false);

    public override void _Process(double delta)
    {
        if (Active && (GetViewport().GuiGetFocusOwner() is LineEdit or TextEdit
            || (DisplayServer.GetName() != "headless" && Input.MouseMode != Input.MouseModeEnum.Captured))) Release();
    }

    public override void _Input(InputEvent @event)
    {
        if (!Active) return;
        if (GetViewport().GuiGetFocusOwner() is LineEdit or TextEdit) { Release(); return; }
        foreach (string action in MoveActions)
        {
            if (!@event.IsAction(action)) continue;
            if (@event.IsActionPressed(action, true)) _held.Add(action);
            else if (@event.IsActionReleased(action)) _held.Remove(action);
            GetViewport().SetInputAsHandled();
            return;
        }
        if (@event.IsAction("reset_camera"))
        {
            if (@event.IsActionPressed("reset_camera")) _camera.FitWorld();
            GetViewport().SetInputAsHandled();
            return;
        }
        if (@event is InputEventMouseMotion motion)
        {
            _yaw = Mathf.Wrap(_yaw + motion.Relative.X * Sensitivity, -Mathf.Pi, Mathf.Pi);
            _pitch = Mathf.Clamp(_pitch - motion.Relative.Y * Sensitivity, -PitchLimit, PitchLimit);
        }
        else if (@event is InputEventMouseButton { Pressed: true } button)
        {
            if (button.ButtonIndex == MouseButton.WheelUp) _camera.AdjustFollowDistance(1);
            else if (button.ButtonIndex == MouseButton.WheelDown) _camera.AdjustFollowDistance(-1);
        }
        // 全局键留给宿主处理；捕获期间的所有鼠标事件都不能穿透 GUI。
        if (@event is InputEventMouse) GetViewport().SetInputAsHandled();
    }

    public override void _ExitTree()
    {
        Release();
        if (_window is not null) _window.FocusExited -= Release;
    }
}
