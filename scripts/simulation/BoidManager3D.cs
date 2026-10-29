using System;
using System.Collections.Generic;
using System.Diagnostics;
using Godot;

namespace BoidsLab;

/// <summary>三维基础世界：管理实例、固定物理步、盒状环绕与真实统计，不依赖 UI。</summary>
public partial class BoidManager3D : Node3D, ISimulationWorld
{
    [Export] public Vector3 WorldSize { get; set; } = new(1280, 720, 720);
    [Export] public PackedScene BoidScene { get; set; } = null!;

    public string DisplayName => "三维世界 · 朴素邻居搜索";
    public string LengthUnit => "世界单位";
    public SimulationFeatures Features => SimulationFeatures.NeighborSearch | SimulationFeatures.DebugDrawing;
    public bool CoreImplemented => true; // 基础运动可运行；可选能力由 Features 明确声明。
    public bool CanStep => false;
    public bool IsRunning { get; private set; }
    public SimulationStats Stats { get; private set; }
    public Aabb WorldBounds { get; private set; }
    public IReadOnlyList<Boid3D> Boids { get; }
    public NeighborSearch3D NeighborSearch { get; } = new();
    public Boid3D? SelectedBoid { get; private set; }
    public int? SelectedBoidId => SelectedBoid?.Id;
    public event Action? BoidControlStateChanged;
    public BoidConfig Config => _config ?? throw new InvalidOperationException("Initialize the world first.");

    private readonly List<Boid3D> _boids = new();
    private BoidConfig? _config;
    private Node3D _boidsRoot = null!;
    private SimulationView3D _view = null!;
    private SimulationCamera3D _camera = null!;
    private Boid3D? _pressedBoid;
    private int _nextBoidId;

    public BoidManager3D() => Boids = _boids.AsReadOnly();

    public void Initialize(BoidConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        if (!WorldSize.IsFinite() || WorldSize.X <= 0 || WorldSize.Y <= 0 || WorldSize.Z <= 0)
            throw new InvalidOperationException("WorldSize must contain positive finite dimensions.");
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
        _camera.CancelGesture();
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
        boid.Position = WorldGeometry3D.Wrap(position, WorldBounds);
        boid.Initialize(Config, _nextBoidId++, velocity);
        _boidsRoot.AddChild(boid);
        _boids.Add(boid);
        return boid;
    }

    public void RemoveBoid(Boid3D boid)
    {
        if (!_boids.Remove(boid)) return;
        if (SelectedBoid == boid) SelectBoid(null);
        _boidsRoot.RemoveChild(boid);
        boid.QueueFree();
        InvalidateStats();
        RefreshDebug();
    }

    /// <summary>每个物理帧先快照并搜索邻居，再统一推进；未来在这两个阶段之间计算全体转向。</summary>
    public void Step(double deltaSeconds)
    {
        float dt = (float)deltaSeconds;
        if (!float.IsFinite(dt) || dt <= 0f) throw new ArgumentOutOfRangeException(nameof(deltaSeconds));
        long started = Stopwatch.GetTimestamp();
        foreach (Boid3D boid in _boids) boid.Position = WorldGeometry3D.Wrap(boid.Position, WorldBounds);
        NeighborSearch.Search(Boids, Config.PerceptionRadius, WorldBounds);
        foreach (Boid3D boid in _boids)
        {
            // TODO: 后续先从共同快照计算全体群集转向，再传入对应个体。本阶段搜索不改变运动。
            boid.UpdateBoid(deltaSeconds);
            boid.Position = WorldGeometry3D.Wrap(boid.Position, WorldBounds);
        }
        Stats = new SimulationStats(_boids.Count, Stopwatch.GetElapsedTime(started).TotalMilliseconds,
            NeighborSearch.CandidateChecks, NeighborSearch.AverageNeighbors, NeighborSearch.SearchMilliseconds);
        RefreshDebug(); // 不计入模拟耗时；当前画面查询不覆盖步前快照。
    }

    private void OnWorldPressStarted(Vector2 pointer)
    {
        _pressedBoid = null;
        Vector3 origin = _camera.ProjectRayOrigin(pointer);
        Vector3 direction = _camera.ProjectRayNormal(pointer).Normalized();
        float minimumRadius = _camera.ProjectPosition(pointer + Vector2.Right * 10, 1)
            .DistanceTo(_camera.ProjectPosition(pointer, 1));
        float closest = float.PositiveInfinity;
        foreach (Boid3D boid in _boids)
        {
            if (!_camera.IsPositionInFrustum(boid.GlobalPosition)) continue;
            if (boid.HitDistance(origin, direction, minimumRadius) is not { } distance) continue;
            if (distance > closest || (distance == closest && boid != SelectedBoid)) continue;
            closest = distance;
            _pressedBoid = boid;
        }
    }

    private void OnWorldPressEnded(bool clicked)
    {
        Boid3D? target = _pressedBoid;
        _pressedBoid = null;
        if (!clicked || (target is not null && !_boids.Contains(target))) return;
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

    private void RefreshDebug() => _view.RefreshDebug();

    private void InvalidateStats()
    {
        NeighborSearch.Invalidate();
        Stats = new SimulationStats(_boids.Count, null, null, null);
    }

    public void ConfigurationChanged()
    {
        NormalizeConfiguration();
        if (NeighborSearch.HasResults && Config.PerceptionRadius != NeighborSearch.Radius) InvalidateStats();
        RefreshDebug(); // 暂停时半径与绘制开关也立即刷新，不伪造一次模拟测量。
    }

    private void NormalizeConfiguration()
    {
        Config.Normalize();
        Config.NeighborSearchMode = NeighborSearchMode.Naive; // 三维网格尚未实现，不接受配置中的网格模式。
    }

    // TODO: 后续实现三维单步、领队与障碍物；对应 UI 仍不开放。
    public void StepOnce() { }
    public void SetInteractionEnabled(bool enabled) =>
        GetNode<SimulationCamera3D>("SimulationView/Camera3D").SetInteractionEnabled(enabled);
    public ObstacleSelection? SelectedObstacleInfo => null;
    public ObstacleShape? ObstaclePlacement => null;
    public int? LeaderId => null;
    public bool HasLeaderTarget => false;
    public event Action? ObstacleSelectionChanged { add { } remove { } }
    public void BeginObstaclePlacement(ObstacleShape shape) { }
    public void CancelObstaclePlacement() { }
    public void DeleteSelectedObstacle() { }
    public void ResizeSelectedObstacle(float width, float height) { }
    public void SetSelectedBoidLeader(bool enabled) { }

    public override void _ExitTree()
    {
        if (_camera is null) return;
        _camera.WorldPressStarted -= OnWorldPressStarted;
        _camera.WorldPressEnded -= OnWorldPressEnded;
        _camera.ViewChanged -= RefreshDebug;
    }
}
