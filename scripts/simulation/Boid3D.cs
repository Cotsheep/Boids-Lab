using System;
using Godot;

namespace BoidsLab;

/// <summary>三维个体：原生 Node3D 保存位姿，由管理器统一推进；本地 -Z 是前方。</summary>
public partial class Boid3D : Node3D
{
    [Export] public Material? SelectionMaterial { get; set; }
    [Export] public Material? LeaderMaterial { get; set; }
    [Export] public Material? SelectedLeaderMaterial { get; set; }
    public bool IsLeader { get; private set; }
    public int Id { get; private set; }
    public bool IsSelected { get; private set; }
    private MeshInstance3D? _body;
    private Material? _normalOverride;
    private Material? _viewMaterial;
    private Transform3D _bodyRestTransform = Transform3D.Identity;
    private Transform3D _previousPose, _currentPose, _displayPose;
    private bool _interpolating;
    // 根节点始终保存物理位姿；只有 Body 的局部变换补偿为显示位姿。
    public Transform3D DisplayTransform => _interpolating && Transform == _currentPose ? _displayPose : Transform;
    public Vector3 DisplayPosition => DisplayTransform.Origin;
    public Transform3D DisplayGlobalTransform => GetParent() is Node3D parent ? parent.GlobalTransform * DisplayTransform : DisplayTransform;
    private BoidThemeMaterials? _themeMaterials;
    internal void ApplyPalette(BoidThemeMaterials materials)
    {
        _themeMaterials = materials;
        SelectionMaterial = materials.Selection;
        LeaderMaterial = materials.Leader;
        SelectedLeaderMaterial = materials.SelectedLeader;
        RefreshAppearance();
    }
    internal Material? NormalMaterial => _themeMaterials?.Normal ?? _normalOverride ?? _body?.Mesh?.SurfaceGetMaterial(0);
    // 所有矢量都属于 Boids 容器坐标系，时间单位为秒，角量使用弧度。
    public Vector3 Velocity { get; private set; }
    public Vector3 Acceleration { get; private set; }
    public Vector3 AngularVelocity { get; private set; }
    public Vector3 AngularAcceleration { get; private set; }

    private const float FacingResponse = 6f;
    private const float DirectionSpeedSquared = 0.0001f;
    private BoidConfig? _config;
    private Vector3 _accelerationInput;

    public override void _Ready()
    {
        _body = GetNodeOrNull<MeshInstance3D>("Body");
        _normalOverride = _body?.MaterialOverride;
        _bodyRestTransform = _body?.Transform ?? Transform3D.Identity;
        ResetPresentation();
        RefreshAppearance();
    }

    internal void BeginPresentationStep() => _previousPose = Transform;

    internal void CommitPresentationStep(bool interpolate)
    {
        if (!interpolate) { ResetPresentation(); return; }
        _currentPose = Transform;
        _interpolating = true;
        SetDisplayPose(_previousPose);
    }

    internal void PreparePresentation(float fraction)
    {
        // 初始化、手动改位姿等非物理步修改不应从旧位置拖出一条插值轨迹。
        if (!_interpolating) return;
        if (Transform != _currentPose) { ResetPresentation(); return; }
        SetDisplayPose(_previousPose.InterpolateWith(_currentPose, Mathf.Clamp(fraction, 0, 1)));
    }

    internal void ResetPresentation()
    {
        _interpolating = false;
        _previousPose = _currentPose = _displayPose = Transform;
        if (_body is not null) _body.Transform = _bodyRestTransform;
    }

    private void SetDisplayPose(Transform3D pose)
    {
        _displayPose = pose;
        if (_body is not null)
        {
            Basis inverse = _currentPose.Basis.Inverse();
            // 先求相对位移，避免无限坐标下两个大平移分别变换后相减造成额外精度损失。
            _body.Transform = new Transform3D(inverse * pose.Basis, inverse * (pose.Origin - _currentPose.Origin)) * _bodyRestTransform;
        }
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
        if (_body is not null) _body.MaterialOverride = IsLeader
            ? (IsSelected ? SelectedLeaderMaterial : LeaderMaterial)
            : IsSelected ? SelectionMaterial : _viewMaterial ?? _themeMaterials?.Normal ?? _normalOverride;
    }

    internal void SetViewMaterial(Material? material)
    {
        if (_viewMaterial == material) return;
        _viewMaterial = material;
        RefreshAppearance();
    }

    // 用网格包围球进行轻量拾取，无需给每只个体添加碰撞体。保留最小屏幕点击容差。
    internal float? HitDistance(Vector3 rayOrigin, Vector3 rayDirection, float minimumRadius)
    {
        Transform3D displayedBody = DisplayGlobalTransform * _bodyRestTransform;
        Vector3 center = _body is null ? DisplayGlobalTransform.Origin : displayedBody * _body.GetAabb().GetCenter();
        float radius = 14f;
        if (_body is not null)
        {
            Basis basis = displayedBody.Basis;
            float scale = Mathf.Max(basis.X.Length(), Mathf.Max(basis.Y.Length(), basis.Z.Length()));
            radius = _body.GetAabb().Size.Length() * 0.5f * scale;
        }
        radius = Mathf.Max(radius, minimumRadius);
        Vector3 toCenter = center - rayOrigin;
        float along = toCenter.Dot(rayDirection);
        if (along < 0) return null;
        float perpendicularSquared = (toCenter - rayDirection * along).LengthSquared();
        if (perpendicularSquared > radius * radius) return null;
        return Mathf.Max(0, along - Mathf.Sqrt(Mathf.Max(0, radius * radius - perpendicularSquared)));
    }

    public void Initialize(BoidConfig config, int id, Vector3 velocity)
    {
        ArgumentNullException.ThrowIfNull(config);
        if (!velocity.IsFinite()) throw new ArgumentOutOfRangeException(nameof(velocity));
        _config = config;
        Id = id;
        Velocity = velocity.LimitLength(config.MaxSpeed);
        _accelerationInput = Acceleration = AngularVelocity = AngularAcceleration = Vector3.Zero;
        Quaternion = Quaternion.Identity;
        if (Velocity.LengthSquared() > DirectionSpeedSquared)
        {
            Vector3 direction = Velocity.Normalized();
            Vector3 up = Mathf.Abs(direction.Dot(Vector3.Up)) > 0.99f ? Vector3.Right : Vector3.Up;
            Quaternion = Basis.LookingAt(direction, up).GetRotationQuaternion();
        }
        ResetPresentation();
    }

    /// <summary>持续施加的外部加速度；传入零撤销。群集规则通过 UpdateBoid 的参数提供当步转向。</summary>
    public void SetAcceleration(Vector3 acceleration)
    {
        if (!acceleration.IsFinite()) throw new ArgumentOutOfRangeException(nameof(acceleration));
        _accelerationInput = acceleration;
    }

    /// <summary>接触修正只更新位置和受限速度；下个物理步继续平滑跟随新的运动方向。</summary>
    internal void ApplyObstacleContact(Vector3 position, Vector3 velocity)
    {
        Position = position;
        Velocity = velocity.LimitLength(_config!.MaxSpeed);
    }

    // 普通方法，不注册个体 _Process / _PhysicsProcess，避免重复积分。
    public void UpdateBoid(double deltaSeconds, Vector3 steeringAcceleration = default)
    {
        if (_config is null) throw new InvalidOperationException("Initialize the Boid before updating it.");
        float dt = (float)deltaSeconds;
        if (!float.IsFinite(dt) || dt <= 0f) throw new ArgumentOutOfRangeException(nameof(deltaSeconds));
        if (!steeringAcceleration.IsFinite()) throw new ArgumentOutOfRangeException(nameof(steeringAcceleration));

        // 半隐式欧拉：加速度 -> 速度 -> 位置；当前没有群集转向时自然保持匀速。
        Acceleration = (_accelerationInput + steeringAcceleration).LimitLength(_config.MaxForce);
        Velocity = (Velocity + Acceleration * dt).LimitLength(_config.MaxSpeed);
        Position += Velocity * dt;
        UpdateFacing(dt);
    }

    private void UpdateFacing(float dt)
    {
        float maxSpeed = Mathf.DegToRad(_config!.MaxAngularSpeedDegrees);
        float maxAcceleration = Mathf.DegToRad(_config.MaxAngularAccelerationDegrees);
        Vector3 previousVelocity = AngularVelocity.LimitLength(maxSpeed);
        Vector3 angleError = Vector3.Zero;
        Quaternion orientation = Quaternion.Normalized();

        if (Velocity.LengthSquared() > DirectionSpeedSquared)
        {
            Vector3 forward = orientation * Vector3.Forward;
            Vector3 target = Velocity.Normalized();
            Vector3 cross = forward.Cross(target);
            float sine = cross.Length();
            float cosine = Mathf.Clamp(forward.Dot(target), -1f, 1f);
            if (sine > 0.00001f)
                angleError = cross / sine * Mathf.Atan2(sine, cosine);
            else if (cosine < 0f)
                angleError = (orientation * Vector3.Up) * Mathf.Pi; // 恰好掉头时选取稳定的转轴。
        }

        // 沿最短转向轴进行阻尼跟随，不锁定欧拉角，也不强制保持世界 Y 为上方。
        Vector3 requested = FacingResponse * FacingResponse * angleError - 2f * FacingResponse * previousVelocity;
        Vector3 acceleration = requested.LimitLength(maxAcceleration);
        AngularVelocity = (previousVelocity + acceleration * dt).LimitLength(maxSpeed);
        AngularAcceleration = (AngularVelocity - previousVelocity) / dt;
        float speed = AngularVelocity.Length();
        if (speed > 0.000001f)
            Quaternion = (new Quaternion(AngularVelocity / speed, speed * dt) * orientation).Normalized();
    }
}
