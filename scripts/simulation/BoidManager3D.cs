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

    public string DisplayName => "三维世界 · 基础运动";
    public string LengthUnit => "世界单位";
    public SimulationFeatures Features => SimulationFeatures.None;
    public bool CoreImplemented => true; // 仅基础运动可运行；可选能力由 Features 明确声明。
    public bool CanStep => false;
    public bool IsRunning { get; private set; }
    public SimulationStats Stats { get; private set; }
    public Aabb WorldBounds { get; private set; }
    public IReadOnlyList<Boid3D> Boids { get; }
    public BoidConfig Config => _config ?? throw new InvalidOperationException("Initialize the world first.");

    private readonly List<Boid3D> _boids = new();
    private BoidConfig? _config;
    private Node3D _boidsRoot = null!;
    private int _nextBoidId;

    public BoidManager3D() => Boids = _boids.AsReadOnly();

    public void Initialize(BoidConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        if (!WorldSize.IsFinite() || WorldSize.X <= 0 || WorldSize.Y <= 0 || WorldSize.Z <= 0)
            throw new InvalidOperationException("WorldSize must contain positive finite dimensions.");
        _config = config;
        Config.Normalize();
        WorldBounds = new Aabb(-WorldSize * 0.5f, WorldSize);
        if (IsNodeReady())
        {
            GetNode<SimulationView3D>("SimulationView").Configure(WorldBounds);
            Reset();
        }
    }

    public override void _Ready()
    {
        _boidsRoot = GetNode<Node3D>("Boids");
        GetNode<SimulationView3D>("SimulationView").Configure(WorldBounds);
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
        Config.Normalize();
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
            SpawnBoid(position, direction * Config.MaxSpeed);
        }
        InvalidateStats();
    }

    public Boid3D SpawnBoid(Vector3 position, Vector3 velocity)
    {
        if (!position.IsFinite()) throw new ArgumentOutOfRangeException(nameof(position));
        if (!velocity.IsFinite()) throw new ArgumentOutOfRangeException(nameof(velocity));
        if (BoidScene is null) throw new InvalidOperationException("Assign BoidScene before spawning.");
        var boid = BoidScene.Instantiate<Boid3D>();
        boid.Name = $"Boid_{_nextBoidId}";
        boid.Position = Wrap(position);
        boid.Initialize(Config, _nextBoidId++, velocity);
        _boidsRoot.AddChild(boid);
        _boids.Add(boid);
        InvalidateStats();
        return boid;
    }

    public void RemoveBoid(Boid3D boid)
    {
        if (!_boids.Remove(boid)) return;
        _boidsRoot.RemoveChild(boid);
        boid.QueueFree();
        InvalidateStats();
    }

    /// <summary>物理帧只调用一次。未来先集中计算三维邻居和转向，再统一推进所有个体。</summary>
    public void Step(double deltaSeconds)
    {
        float dt = (float)deltaSeconds;
        if (!float.IsFinite(dt) || dt <= 0f) throw new ArgumentOutOfRangeException(nameof(deltaSeconds));
        long started = Stopwatch.GetTimestamp();
        foreach (Boid3D boid in _boids)
        {
            // TODO: 实现 3D 邻居搜索与群集规则后，传入该个体本步的转向加速度。
            boid.UpdateBoid(deltaSeconds);
            boid.Position = Wrap(boid.Position);
        }
        Stats = new SimulationStats(_boids.Count, Stopwatch.GetElapsedTime(started).TotalMilliseconds, null, null);
    }

    private Vector3 Wrap(Vector3 position)
    {
        Vector3 min = WorldBounds.Position;
        Vector3 max = WorldBounds.End;
        return new Vector3(Mathf.Wrap(position.X, min.X, max.X),
            Mathf.Wrap(position.Y, min.Y, max.Y), Mathf.Wrap(position.Z, min.Z, max.Z));
    }

    private void InvalidateStats() => Stats = new SimulationStats(_boids.Count, null, null, null);
    public void ConfigurationChanged()
    {
        Config.Normalize();
        InvalidateStats(); // 数量和种子在重置后生效；运动上限在下一物理步生效。
    }

    // TODO: 按需逐项实现三维交互。Features 为 None，共享 UI 不会展示这些功能。
    public void StepOnce() { }
    public void SetInteractionEnabled(bool enabled) =>
        GetNode<SimulationCamera3D>("SimulationView/Camera3D").SetInteractionEnabled(enabled);
    public ObstacleSelection? SelectedObstacleInfo => null;
    public ObstacleShape? ObstaclePlacement => null;
    public int? SelectedBoidId => null;
    public int? LeaderId => null;
    public bool HasLeaderTarget => false;
    public event Action? ObstacleSelectionChanged { add { } remove { } }
    public event Action? BoidControlStateChanged { add { } remove { } }
    public void BeginObstaclePlacement(ObstacleShape shape) { }
    public void CancelObstaclePlacement() { }
    public void DeleteSelectedObstacle() { }
    public void ResizeSelectedObstacle(float width, float height) { }
    public void SetSelectedBoidLeader(bool enabled) { }
}
