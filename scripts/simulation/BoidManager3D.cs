using System;
using System.Collections.Generic;
using System.Diagnostics;
using Godot;

namespace BoidsLab;

/// <summary>三维世界：管理实例、邻居搜索、群集转向、固定物理步与软边界，不依赖 UI。</summary>
public partial class BoidManager3D : Node3D, ISimulationWorld
{
    public Vector3 WorldSize => new(Config.WorldWidth, Config.WorldHeight, Config.WorldDepth);
    [Export] public PackedScene BoidScene { get; set; } = null!;

    public string DisplayName => "三维世界";
    public string LengthUnit => "世界单位";
    public SimulationFeatures Features => SimulationFeatures.NeighborSearch | SimulationFeatures.DebugDrawing
        | SimulationFeatures.UniformGridSearch | SimulationFeatures.Flocking | SimulationFeatures.Obstacles | SimulationFeatures.Leader | SimulationFeatures.DirectLeaderControl;
    public bool CoreImplemented => true; // 基础运动可运行；可选能力由 Features 明确声明。
    public bool CanStep => !IsRunning;
    public bool IsRunning { get; private set; }
    public SimulationStats Stats { get; private set; }
    public Aabb WorldBounds { get; private set; }
    public IReadOnlyList<Boid3D> Boids { get; }
    public NeighborSearch3D NeighborSearch { get; } = new();
    public Boid3D? Leader { get; private set; }
    public LeaderController3D LeaderController { get; private set; } = null!;
    public bool IsLeaderControlActive => LeaderController is not null && LeaderController.Active;
    public event Action? ConfigurationEdited;
    private bool _interactionEnabled = true;
    public Boid3D? SelectedBoid { get; private set; }
    public int? SelectedBoidId => SelectedBoid?.Id;
    public event Action? BoidControlStateChanged;
    public BoidConfig Config => _config ?? throw new InvalidOperationException("Initialize the world first.");

    private readonly List<Boid3D> _boids = new();
    private Vector3[] _steeringAccelerations = Array.Empty<Vector3>();
    private BoidConfig? _config;
    private Node3D _boidsRoot = null!;
    private SimulationView3D _view = null!;
    private SimulationCamera3D _camera = null!;
    private ObstacleEditor3D _obstacleEditor = null!;
    private Boid3D? _pressedBoid;
    private int _nextBoidId;

    public BoidManager3D() => Boids = _boids.AsReadOnly();

    public void Initialize(BoidConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        _config = config;
        NormalizeConfiguration();
        WorldBounds = new Aabb(-WorldSize * 0.5f, WorldSize);
        if (IsNodeReady())
        {
            _view.Bind(Config, this);
            _view.Configure(WorldBounds);
            Reset();
        }
    }

    public override void _Ready()
    {
        _boidsRoot = GetNode<Node3D>("Boids");
        _view = GetNode<SimulationView3D>("SimulationView");
        _camera = _view.GetNode<SimulationCamera3D>("Camera3D");
        _obstacleEditor = GetNode<ObstacleEditor3D>("ObstacleEditor");
        _obstacleEditor.Initialize(this, _camera, GetNode<Node3D>("Obstacles"));
        LeaderController = GetNode<LeaderController3D>("LeaderController");
        LeaderController.Initialize(_camera);
        LeaderController.ActiveChanged += OnLeaderControlChanged;
        _camera.FollowDistanceRequested += OnFollowDistanceRequested;
        _obstacleEditor.Changed += OnObstaclesChanged;
        _obstacleEditor.GeometryChanged += InvalidateStats;
        _view.Bind(Config, this);
        _camera.WorldPressStarted += OnWorldPressStarted;
        _camera.WorldPressEnded += OnWorldPressEnded;
        _camera.ViewChanged += RefreshDebug;
        _view.Configure(WorldBounds);
        Reset();
    }

    public override void _PhysicsProcess(double delta)
    {
        if (IsRunning) Step(delta);
    }

    public void SetRunning(bool running) => IsRunning = running;

    public void Reset()
    {
        IsRunning = false;
        SetLeaderControlActive(false);
        SetLeader(null);
        _camera.CancelGesture();
        _obstacleEditor.CancelGesture();
        _obstacleEditor.CancelPlacement();
        SelectBoid(null);
        NormalizeConfiguration();
        foreach (Boid3D boid in _boids)
        {
            _boidsRoot.RemoveChild(boid);
            boid.QueueFree();
        }
        _boids.Clear();
        _nextBoidId = 0;
        using var random = new RandomNumberGenerator { Seed = (ulong)Config.RandomSeed };
        for (int i = 0; i < Config.BoidCount; i++)
        {
            Vector3 position = WorldBounds.Position + new Vector3(
                random.Randf() * WorldSize.X, random.Randf() * WorldSize.Y, random.Randf() * WorldSize.Z);
            // 球面均匀采样方向，避免按三个独立欧拉角随机导致方向偏置。
            float y = random.RandfRange(-1f, 1f);
            float azimuth = random.RandfRange(-Mathf.Pi, Mathf.Pi);
            float horizontal = Mathf.Sqrt(Mathf.Max(0f, 1f - y * y));
            Vector3 direction = new(horizontal * Mathf.Cos(azimuth), y, horizontal * Mathf.Sin(azimuth));
            CreateBoid(position, direction * Config.MaxSpeed);
        }
        InvalidateStats();
        RefreshDebug();
    }

    public Boid3D SpawnBoid(Vector3 position, Vector3 velocity)
    {
        Boid3D boid = CreateBoid(position, velocity);
        InvalidateStats();
        RefreshDebug();
        return boid;
    }

    private Boid3D CreateBoid(Vector3 position, Vector3 velocity)
    {
        if (!position.IsFinite()) throw new ArgumentOutOfRangeException(nameof(position));
        if (!velocity.IsFinite()) throw new ArgumentOutOfRangeException(nameof(velocity));
        if (BoidScene is null) throw new InvalidOperationException("Assign BoidScene before spawning.");
        var boid = BoidScene.Instantiate<Boid3D>();
        boid.Name = $"Boid_{_nextBoidId}";
        boid.Position = position;
        boid.Initialize(Config, _nextBoidId++, velocity);
        _boidsRoot.AddChild(boid);
        _boids.Add(boid);
        return boid;
    }

    public void RemoveBoid(Boid3D boid)
    {
        if (!_boids.Remove(boid)) return;
        if (Leader == boid) SetLeader(null);
        if (SelectedBoid == boid) SelectBoid(null);
        _boidsRoot.RemoveChild(boid);
        boid.QueueFree();
        InvalidateStats();
        RefreshDebug();
    }

    /// <summary>快照并搜索邻居 → 算完全体转向 → 统一推进，保证个体更新顺序不影响本步输入。</summary>
    public void Step(double deltaSeconds)
    {
        float dt = (float)deltaSeconds;
        if (!float.IsFinite(dt) || dt <= 0f) throw new ArgumentOutOfRangeException(nameof(deltaSeconds));
        long started = Stopwatch.GetTimestamp();
        NeighborSearch.Search(Boids, Config.PerceptionRadius, Config.NeighborSearchMode);
        if (_steeringAccelerations.Length != _boids.Count) _steeringAccelerations = new Vector3[_boids.Count];
        int leaderIndex = Leader is null ? -1 : _boids.IndexOf(Leader);
        BoidSnapshot3D leaderSnapshot = leaderIndex < 0 ? default : NeighborSearch.Snapshots[leaderIndex];
        Vector3 heading = Leader is null ? Vector3.Forward : Leader.Quaternion * Vector3.Forward;
        float radius = LeaderSteering3D.FollowRadius(_boids.Count - 1, Config);
        Vector3 followPoint = LeaderSteering3D.FollowPoint(leaderSnapshot.Position, heading, Config.LeaderFollowDistance + radius);
        for (int i = 0; i < _boids.Count; i++)
        {
            BoidSnapshot3D self = NeighborSearch.Snapshots[i];
            if (i == leaderIndex)
            {
                _steeringAccelerations[i] = LeaderSteering3D.Arrive(self, self.Position, LeaderController.MovementDirection * Config.MaxSpeed, Config, dt);
                continue;
            }
            Vector3 avoidance = ObstacleAvoidance3D.Calculate(self, _obstacleEditor.Obstacles, Config);
            if (avoidance != Vector3.Zero) { _steeringAccelerations[i] = avoidance; continue; }
            bool following = leaderIndex >= 0 && Config.LeaderFollowWeight > 0;
            Vector3 guidance = following
                ? LeaderSteering3D.Follow(self, followPoint, leaderSnapshot.Velocity, Config, dt, radius) : Vector3.Zero;
            Vector3 flocking = BoidSteering3D.Calculate(NeighborSearch, i, Config, guidance, following, following ? leaderSnapshot.Velocity : Vector3.Zero);
            _steeringAccelerations[i] = leaderIndex < 0
                ? BoundarySteering3D.Apply(self, WorldBounds, Config, flocking) : flocking;
        }
        for (int i = 0; i < _boids.Count; i++)
        {
            Boid3D boid = _boids[i];
            Vector3 before = boid.Position;
            boid.UpdateBoid(deltaSeconds, _steeringAccelerations[i]);
            if ((Config.AvoidanceEnabled || boid == Leader) && _obstacleEditor.Obstacles.Count > 0)
            {
                Vector3 position = boid.Position, velocity = boid.Velocity;
                ObstacleAvoidance3D.ResolveMotion(before, ref position, ref velocity,
                    _obstacleEditor.Obstacles, Config.ObstacleClearance);
                boid.ApplyObstacleContact(position, velocity);
            }
        }
        Stats = new SimulationStats(_boids.Count, Stopwatch.GetElapsedTime(started).TotalMilliseconds,
            NeighborSearch.CandidateChecks, NeighborSearch.AverageNeighbors, NeighborSearch.SearchMilliseconds);
        RefreshDebug(); // 不计入模拟耗时；当前画面查询不覆盖步前快照。
    }

    private void OnWorldPressStarted(Vector2 pointer)
    {
        _pressedBoid = PickBoid(pointer, out _);
    }

    internal Boid3D? PickBoid(Vector2 pointer, out float closest)
    {
        Boid3D? target = null;
        Vector3 origin = _camera.ProjectRayOrigin(pointer);
        Vector3 direction = _camera.ProjectRayNormal(pointer).Normalized();
        closest = float.PositiveInfinity;
        foreach (Boid3D boid in _boids)
        {
            if (!_camera.IsPositionInFrustum(boid.GlobalPosition)) continue;
            if (boid.HitDistance(origin, direction, _camera.UnitsPerPixelAt(boid.Position) * 10) is not { } distance) continue;
            if (distance > closest || (distance == closest && boid != SelectedBoid)) continue;
            closest = distance;
            target = boid;
        }
        return target;
    }

    private void OnWorldPressEnded(bool clicked)
    {
        Boid3D? target = _pressedBoid;
        _pressedBoid = null;
        if (!clicked || (target is not null && !_boids.Contains(target))) return;
        _obstacleEditor.ClearSelection();
        SelectBoid(target == SelectedBoid ? null : target);
    }

    private void SelectBoid(Boid3D? boid)
    {
        if (SelectedBoid == boid) return;
        SelectedBoid?.SetSelected(false);
        SelectedBoid = boid;
        SelectedBoid?.SetSelected(true);
        BoidControlStateChanged?.Invoke();
        RefreshDebug();
    }

    internal void ClearBoidSelection() { _pressedBoid = null; SelectBoid(null); }
    private void OnObstaclesChanged() => ObstacleSelectionChanged?.Invoke();

    private void RefreshDebug() => _view.RefreshDebug();

    private void InvalidateStats()
    {
        NeighborSearch.Invalidate();
        Stats = new SimulationStats(_boids.Count, null, null, null);
    }

    public void ConfigurationChanged()
    {
        NormalizeConfiguration();
        if (WorldBounds.Size != WorldSize)
        {
            WorldBounds = new Aabb(WorldBounds.GetCenter() - WorldSize * 0.5f, WorldSize);
            _view.Configure(WorldBounds, false);
            InvalidateStats();
        }
        if (NeighborSearch.HasResults && (Config.PerceptionRadius != NeighborSearch.Radius
            || Config.NeighborSearchMode != NeighborSearch.Mode)) InvalidateStats();
        RefreshDebug(); // 暂停时半径与绘制开关也立即刷新，不伪造一次模拟测量。
    }

    private void NormalizeConfiguration()
    {
        Config.Normalize();
    }

    public void StepOnce()
    {
        if (CanStep) Step(1.0 / Engine.PhysicsTicksPerSecond);
    }
    public void SetInteractionEnabled(bool enabled)
    {
        _interactionEnabled = enabled;
        if (!enabled) SetLeaderControlActive(false);
        UpdateInteraction();
    }
    public ObstacleSelection? SelectedObstacleInfo => _obstacleEditor.SelectionInfo;
    public ObstacleShape? ObstaclePlacement => _obstacleEditor.PlacementShape;
    public int? LeaderId => Leader?.Id;
    public bool HasLeaderTarget => false;
    public event Action? ObstacleSelectionChanged;
    public void BeginObstaclePlacement(ObstacleShape shape) => _obstacleEditor.BeginPlacement(shape);
    public void CancelObstaclePlacement() => _obstacleEditor.CancelPlacement();
    public void DeleteSelectedObstacle() => _obstacleEditor.DeleteSelected();
    public void ResizeSelectedObstacle(float width, float height, float depth = 0) => _obstacleEditor.ResizeSelected(width, height, depth);
    public void SetSelectedBoidLeader(bool enabled)
    {
        if (SelectedBoid is null) return;
        if (enabled) SetLeader(SelectedBoid);
        else if (SelectedBoid == Leader) SetLeader(null);
    }

    public void SetLeader(Boid3D? leader)
    {
        if (leader is not null && !_boids.Contains(leader)) throw new ArgumentException("Leader must belong to this world.", nameof(leader));
        if (Leader == leader) return;
        Vector3 fallback = Leader?.Position ?? WorldBounds.GetCenter();
        Leader?.SetLeader(false);
        Leader = leader;
        Leader?.SetLeader(true);
        LeaderController.AssignLeader(leader);
        if (leader is not null) _camera.StartFollowing(leader, LeaderController, Config);
        else
        {
            Vector3 center = fallback;
            if (_boids.Count > 0)
            {
                // 每项先除以数量，避免求和先溢出。
                center = Vector3.Zero;
                foreach (Boid3D boid in _boids) center += boid.Position / _boids.Count;
            }
            WorldBounds = new Aabb(center - WorldSize * 0.5f, WorldSize);
            _view.Configure(WorldBounds, false);
            _camera.RecenterObservation(center);
        }
        _camera.CancelGesture();
        _obstacleEditor.CancelGesture();
        _obstacleEditor.CancelPlacement();
        _pressedBoid = null;
        InvalidateStats();
        RefreshDebug();
        BoidControlStateChanged?.Invoke();
    }

    public void SetLeaderControlActive(bool active)
    {
        if (active && (!_interactionEnabled || Leader is null)) return;
        if (active)
        {
            GetViewport().GuiGetFocusOwner()?.ReleaseFocus();
            _pressedBoid = null;
            _camera.CancelGesture();
            _obstacleEditor.CancelGesture();
            _obstacleEditor.CancelPlacement();
        }
        LeaderController?.SetActive(active);
    }

    private void UpdateInteraction()
    {
        bool enabled = _interactionEnabled && !IsLeaderControlActive;
        _camera.SetInteractionEnabled(enabled);
        _obstacleEditor.SetInteractionEnabled(enabled);
    }

    private void OnLeaderControlChanged()
    {
        UpdateInteraction();
        BoidControlStateChanged?.Invoke();
    }

    private void OnFollowDistanceRequested(float distance)
    {
        Config.FollowCameraDistance = distance;
        ConfigurationEdited?.Invoke();
    }

    public override void _ExitTree()
    {
        if (_camera is null) return;
        SetLeaderControlActive(false);
        LeaderController.ActiveChanged -= OnLeaderControlChanged;
        _camera.FollowDistanceRequested -= OnFollowDistanceRequested;
        _camera.WorldPressStarted -= OnWorldPressStarted;
        _camera.WorldPressEnded -= OnWorldPressEnded;
        _camera.ViewChanged -= RefreshDebug;
        _obstacleEditor.Changed -= OnObstaclesChanged;
        _obstacleEditor.GeometryChanged -= InvalidateStats;
    }
}
