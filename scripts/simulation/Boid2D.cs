using System;
using Godot;

namespace BoidsLab;

/// <summary>独立 Boid 节点：保存自身状态，由管理器显式调用更新；子节点负责外观。</summary>
public partial class Boid2D : Node2D
{
    public int Id { get; private set; }
    public bool IsSelected { get; private set; }
    public bool IsLeader { get; private set; }
    public static readonly Color LeaderColor = new("83bfff");
    private Polygon2D? _body;
    private LabPalette _palette = LabPalette.Dark;
    internal void ApplyPalette(LabPalette palette) { _palette = palette; RefreshAppearance(); }
    /// <summary>世界像素/秒；与 Position 同属 Boids 容器坐标系。</summary>
    public Vector2 Velocity { get; private set; }
    /// <summary>本步施加的线性加速度（像素/秒²），受 MaxForce 限制。</summary>
    public Vector2 Acceleration { get; private set; }
    /// <summary>弧度/秒；Godot 的二维正旋转在屏幕上为顺时针。</summary>
    public float AngularVelocity { get; private set; }
    /// <summary>弧度/秒²；由朝向跟随控制计算。</summary>
    public float AngularAcceleration { get; private set; }

    // 临界阻尼跟随的响应频率（1/秒）；角速度反馈让朝向接近目标时减速。
    private const float FacingResponse = 6f;
    private BoidConfig? _config;
    private Vector2 _accelerationInput;
    private bool _angularLimitsEnabled;
    private bool _trackingVelocityFacing;

    public override void _Ready()
    {
        _body = GetNodeOrNull<Polygon2D>("Body");
        RefreshAppearance();
    }

    internal void SetSelected(bool selected)
    {
        IsSelected = selected;
        RefreshAppearance();
    }

    internal void SetLeader(bool leader)
    {
        IsLeader = leader;
        RefreshAppearance();
    }

    private void RefreshAppearance()
    {
        if (_body is not null)
            _body.Color = IsLeader ? (IsSelected ? _palette.SelectedLeader : _palette.Leader)
                : IsSelected ? _palette.Selection : _palette.Boid;
    }

    internal bool HitTest(Vector2 viewportPosition)
    {
        // 三角形内精确命中；缩小时额外保留 10 个视口像素的点击容差。
        if (_body is not null && Geometry2D.IsPointInPolygon(
            _body.GetGlobalTransformWithCanvas().AffineInverse() * viewportPosition, _body.Polygon))
            return true;
        Vector2 center = GetGlobalTransformWithCanvas() * Vector2.Zero;
        return center.DistanceSquaredTo(viewportPosition) <= 100f;
    }

    public void Initialize(BoidConfig config, int id, Vector2 velocity)
    {
        ArgumentNullException.ThrowIfNull(config);
        if (!velocity.IsFinite()) throw new ArgumentOutOfRangeException(nameof(velocity));
        _config = config;
        Id = id;
        Velocity = velocity.LimitLength(config.MaxSpeed);
        _angularLimitsEnabled = config.AngularMotionLimitsEnabled;
        _trackingVelocityFacing = false;
        Rotation = _angularLimitsEnabled && Velocity.LengthSquared() > VelocityFacing.StopSpeedSquared ? Velocity.Angle() : 0f;
        if (!_angularLimitsEnabled) AlignWithVelocity();
        _accelerationInput = Vector2.Zero;
        Acceleration = Vector2.Zero;
        AngularVelocity = 0f;
        AngularAcceleration = 0f;
        // Position 直接使用原生 Node2D 属性，由管理器在加入场景树前设置。
        // 坐标属于 Boids 容器；该容器与世界绘制节点均保持默认变换。
    }

    /// <summary>
    /// 设置持续施加的外部加速度；传入 Vector2.Zero 撤销。
    /// 每步与群集转向加速度相加后按共享配置限制模长，群集规则不会覆盖此输入。
    /// </summary>
    public void SetAcceleration(Vector2 acceleration)
    {
        if (!acceleration.IsFinite()) throw new ArgumentOutOfRangeException(nameof(acceleration));
        _accelerationInput = acceleration;
    }

    /// <summary>普通方法，不是 Godot 回调；返回本次是否改变了运动状态。</summary>
    public bool UpdateBoid(double deltaSeconds, Vector2 steeringAcceleration = default)
    {
        if (_config is null)
            throw new InvalidOperationException("Initialize the Boid before updating it.");
        float dt = (float)deltaSeconds;
        if (!float.IsFinite(dt) || dt <= 0f)
            throw new ArgumentOutOfRangeException(nameof(deltaSeconds));
        if (!steeringAcceleration.IsFinite()) throw new ArgumentOutOfRangeException(nameof(steeringAcceleration));

        Vector2 previousPosition = Position;
        Vector2 previousVelocity = Velocity;
        float previousRotation = Rotation;
        float previousAngularVelocity = AngularVelocity;

        // 半隐式欧拉积分：先更新速度，再用新速度更新位置。
        Acceleration = (_accelerationInput + steeringAcceleration).LimitLength(_config.MaxForce);
        Velocity = (Velocity + Acceleration * dt).LimitLength(_config.MaxSpeed);
        Position += Velocity * dt;
        UpdateFacing(dt);

        return Position != previousPosition || Velocity != previousVelocity
            || Rotation != previousRotation || AngularVelocity != previousAngularVelocity;
    }

    private void UpdateFacing(float dt)
    {
        SynchronizeFacingMode();
        if (!_angularLimitsEnabled) { AlignWithVelocity(); return; }
        float maxSpeed = Mathf.DegToRad(_config!.MaxAngularSpeedDegrees);
        float maxAcceleration = Mathf.DegToRad(_config.MaxAngularAccelerationDegrees);
        // 下调速度上限属于硬限制；先裁剪旧值，再积分本步受限的角加速度。
        float previousVelocity = Mathf.Clamp(AngularVelocity, -maxSpeed, maxSpeed);
        float angleError = Velocity.LengthSquared() > VelocityFacing.StopSpeedSquared
            ? Mathf.AngleDifference(Rotation, Velocity.Angle())
            : 0f; // 静止时没有目标方向，只制动已有旋转。

        float requestedAcceleration = FacingResponse * FacingResponse * angleError
            - 2f * FacingResponse * previousVelocity;
        float acceleration = Mathf.Clamp(requestedAcceleration, -maxAcceleration, maxAcceleration);
        AngularVelocity = Mathf.Clamp(previousVelocity + acceleration * dt, -maxSpeed, maxSpeed);
        AngularAcceleration = (AngularVelocity - previousVelocity) / dt;
        if (AngularVelocity != 0f)
            Rotation = Mathf.Wrap(Rotation + AngularVelocity * dt, -Mathf.Pi, Mathf.Pi);
    }

    private bool SynchronizeFacingMode()
    {
        if (_angularLimitsEnabled == _config!.AngularMotionLimitsEnabled) return false;
        _angularLimitsEnabled = _config.AngularMotionLimitsEnabled;
        AngularVelocity = AngularAcceleration = 0;
        _trackingVelocityFacing = false;
        return true;
    }

    internal bool RefreshFacingConfiguration()
    {
        if (!SynchronizeFacingMode()) return false;
        if (!_angularLimitsEnabled) AlignWithVelocity();
        return true;
    }

    private void AlignWithVelocity()
    {
        AngularVelocity = AngularAcceleration = 0;
        _trackingVelocityFacing = VelocityFacing.ShouldTrack(Velocity.LengthSquared(), _trackingVelocityFacing);
        if (_trackingVelocityFacing) Rotation = Velocity.Angle();
    }

    /// <summary>避障接触兜底：修正穿透位置并去掉朝障碍物内部的速度，不重置个体状态。</summary>
    internal bool ApplyObstacleContact(Vector2 position, Vector2 velocity)
    {
        bool changed = Position != position || Velocity != velocity;
        float previousRotation = Rotation;
        Position = position;
        Velocity = velocity.LimitLength(_config!.MaxSpeed);
        if (!_config.AngularMotionLimitsEnabled)
        {
            SynchronizeFacingMode();
            AlignWithVelocity();
        }
        return changed || previousRotation != Rotation;
    }
}
